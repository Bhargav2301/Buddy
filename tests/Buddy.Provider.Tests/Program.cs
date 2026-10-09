using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Buddy.Server;

// Every handler/socket is synthetic. This program performs no external provider calls.
internal static class Program
{
    private const string FixtureToken = "buddy-fixture-token-NOT-A-CREDENTIAL";
    private const string Answer = "Hello there.";
    private static int passed;

    private static async Task Main()
    {
        await HttpChecks();
        await RealtimeChecks();
        Console.WriteLine($"PASS: {passed} mock-only provider transport assertions. No live provider, credentials, sockets, or audio used.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++;
    }

    private static async Task<T> Reject<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); }
        catch (T error)
        {
            Check(!error.ToString().Contains(FixtureToken, StringComparison.Ordinal), name + " has no token leakage");
            return error;
        }
        throw new Exception("FAIL: " + name);
    }

    private static async Task HttpChecks()
    {
        Check(ProviderTransportFactory.LiveActivationAvailable && !ProviderProtocols.Connected, "explicit production setup available; no default connection");
        await Reject<NotSupportedException>(() => ProviderTransportFactory.CreateProduction().CompleteAsync("openai", "fixture-model", "Hello"), "production refuses activation");
        Check(!typeof(ProviderHttpTextTransport).IsPublic && !typeof(RealtimeTextTransport).IsPublic, "experimental transports internal");
        await Reject<ArgumentException>(() => { using var handler = new HttpClientHandler(); using var transport = new ProviderHttpTextTransport(handler); return Task.CompletedTask; }, "built-in HTTP handler prohibited");
        await Reject<ArgumentException>(() => { using var handler = new SocketsHttpHandler(); using var transport = new ProviderHttpTextTransport(handler); return Task.CompletedTask; }, "socket HTTP handler prohibited");
        await Reject<ArgumentException>(() => { using var handler = new Forwarder(); using var transport = new ProviderHttpTextTransport(handler); return Task.CompletedTask; }, "delegating HTTP handler prohibited");

        foreach (string provider in new[] { "openai", "anthropic", "gemini", "openrouter" })
        {
            var handler = new MockHandler(async (request, ct) =>
            {
                Check(request.Method == HttpMethod.Post, provider + " uses POST");
                Check(request.RequestUri == ProviderProtocols.Build(provider, "fixture-model", "Hello").Endpoint, provider + " fixed endpoint");
                Check(request.RequestUri!.Scheme == "https" && request.RequestUri.Query == "" && !request.RequestUri.ToString().Contains(FixtureToken), provider + " no key in URI");
                Check(request.Headers.Accept.Single().MediaType == "application/json" && request.Content!.Headers.ContentType!.MediaType == "application/json", provider + " JSON headers");
                if (provider == "anthropic")
                {
                    Check(request.Headers.GetValues("x-api-key").Single() == FixtureToken && request.Headers.GetValues("anthropic-version").Single() == "2023-06-01", "Anthropic header shape");
                    Check(request.Headers.Authorization is null && !request.Headers.Contains("x-goog-api-key"), "Anthropic header isolation");
                }
                else if (provider == "gemini")
                {
                    Check(request.Headers.GetValues("x-goog-api-key").Single() == FixtureToken && request.Headers.Authorization is null && !request.Headers.Contains("x-api-key"), "Gemini header isolation");
                }
                else Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == FixtureToken && !request.Headers.Contains("x-api-key"), provider + " bearer shape");
                var body = await request.Content!.ReadAsStringAsync(ct);
                using var document = JsonDocument.Parse(body);
                Check(!document.RootElement.TryGetProperty("tools", out _) && !document.RootElement.TryGetProperty("audio", out _) && !body.Contains(FixtureToken), provider + " text request shape");
                Check(provider == "gemini" || !document.RootElement.GetProperty("stream").GetBoolean(), provider + " complete response request");
                return Response(Valid(provider));
            });
            using (var transport = new ProviderHttpTextTransport(handler)) Check(await transport.CompleteAsync(provider, "fixture-model", "Hello", FixtureToken) == Answer, provider + " accepts complete text");
            Check(handler.Disposed && handler.Calls == 1, provider + " owned handler disposed, no retry");
        }

        foreach (var uri in new[] { "http://api.openai.com/v1/chat/completions", "https://api.openai.com.evil.invalid/v1/chat/completions", "https://api.openai.com/v1/chat/completions?key=fixture", "https://user@api.openai.com/v1/chat/completions", "https://api.openai.com:444/v1/chat/completions", "https://api.openai.com/v1/chat/completions#fragment", "https://api.openai.com/v1/other" })
            await Reject<InvalidOperationException>(() => { ProviderHttpTextTransport.ValidateEndpoint("openai", "fixture-model", new Uri(uri)); return Task.CompletedTask; }, "endpoint rejected");

        var invalidInputs = new[] { ("unknown", "model", FixtureToken), ("openai", "../bad?x", FixtureToken), ("gemini", "models/name", FixtureToken), ("openai", "model", "fixture\r\nHeader: injected") };
        foreach (var input in invalidInputs)
        {
            var handler = new MockHandler((_, _) => Task.FromResult(Response(Valid("openai"))));
            using var transport = new ProviderHttpTextTransport(handler);
            await Reject<InvalidOperationException>(() => transport.CompleteAsync(input.Item1, input.Item2, "Hello", input.Item3), "invalid input rejected before send");
            Check(handler.Calls == 0, "invalid input sends nothing");
        }

        var invalidResponses = new[]
        {
            ("openai", "{}"), ("openai", "not JSON"), ("openai", "{\"choices\":[]}"),
            ("openai", "{\"error\":{\"message\":\"fixture error\"}}"),
            ("openai", Valid("openai").Replace("\"stop\"", "\"length\"")),
            ("openai", Valid("openai").Replace("\"content\":", "\"tool_calls\":[],\"content\":")),
            ("openai", Valid("openai").Replace("\"content\":", "\"function_call\":{},\"content\":")),
            ("openai", Valid("openai").Replace("\"content\":", "\"audio\":{},\"content\":")),
            ("openai", Valid("openai").Replace("\"content\":", "\"refusal\":\"No\",\"content\":")),
            ("openai", Valid("openai").Replace("\"content\":", "\"content\":\"shadow\",\"content\":")),
            ("openai", Valid("openai").Replace("Hello there.", new string('x', 1700))),
            ("openai", Valid("openai").Replace("Hello there.", "")),
            ("anthropic", Valid("anthropic").Replace("end_turn", "max_tokens")),
            ("anthropic", Valid("anthropic").Replace("\"type\":\"text\"", "\"type\":\"tool_use\"")),
            ("gemini", Valid("gemini").Replace("STOP", "SAFETY")),
            ("gemini", Valid("gemini").Replace("\"text\":", "\"fileData\":{},\"text\":")),
            ("gemini", Valid("gemini").Replace("\"text\":", "\"functionCall\":{},\"text\":")),
            ("gemini", Valid("gemini").Replace("\"text\":", "\"inlineData\":{},\"text\":")),
            ("openrouter", Valid("openai").Replace("\"stop\"", "\"tool_calls\""))
        };
        foreach (var (provider, body) in invalidResponses)
        {
            var stream = new TrackingStream(Encoding.UTF8.GetBytes(body));
            var handler = new MockHandler((_, _) => Task.FromResult(Response(stream)));
            using var transport = new ProviderHttpTextTransport(handler);
            await Reject<InvalidOperationException>(() => transport.CompleteAsync(provider, "fixture-model", "Hello", FixtureToken), "unsupported response refused");
            Check(stream.Disposed, "rejected response body disposed");
        }

        foreach (int status in new[] { 301, 302, 303, 307, 308, 401, 429, 500 })
        {
            var stream = new TrackingStream(Encoding.UTF8.GetBytes(FixtureToken));
            var handler = new MockHandler((_, _) =>
            {
                var response = Response(stream); response.StatusCode = (HttpStatusCode)status;
                response.Headers.Location = new Uri("https://external.invalid/redirect"); return Task.FromResult(response);
            });
            using var transport = new ProviderHttpTextTransport(handler);
            await Reject<InvalidOperationException>(() => transport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken), "HTTP failure refused");
            Check(handler.Calls == 1 && stream.BytesRead == 0 && stream.Disposed, "no follow/retry/body reflection on HTTP failure");
        }

        foreach (bool declared in new[] { true, false })
        {
            var stream = new TrackingStream(new byte[2048]);
            var handler = new MockHandler((_, _) => { var response = Response(stream); if (declared) response.Content.Headers.ContentLength = 2048; return Task.FromResult(response); });
            using var transport = new ProviderHttpTextTransport(handler, new() { MaxResponseBytes = 1024 });
            await Reject<InvalidOperationException>(() => transport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken), "oversize refused");
            Check(stream.Disposed && stream.BytesRead <= 1025 && (!declared || stream.BytesRead == 0), "bounded streamed and declared body");
        }

        foreach (string mutation in new[] { "redirected", "mime", "encoding" })
        {
            var handler = new MockHandler((_, _) =>
            {
                var response = Response(Valid("openai"));
                if (mutation == "redirected") response.RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://external.invalid/");
                if (mutation == "mime") response.Content.Headers.ContentType = new("text/event-stream");
                if (mutation == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
                return Task.FromResult(response);
            });
            using var transport = new ProviderHttpTextTransport(handler);
            await Reject<InvalidOperationException>(() => transport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken), "transport metadata refused");
        }

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var handler = new MockHandler((_, _) => Task.FromResult(Response(Valid("openai"))));
            using var transport = new ProviderHttpTextTransport(handler);
            await Reject<OperationCanceledException>(() => transport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken, cancelled.Token), "pre-cancelled request");
            Check(handler.Calls == 0, "pre-cancellation sends nothing");
        }
        foreach (bool cancel in new[] { true, false })
        {
            var stream = new TrackingStream([], stall: true);
            var handler = new MockHandler((_, _) => Task.FromResult(Response(stream)));
            using var transport = new ProviderHttpTextTransport(handler, new() { Timeout = TimeSpan.FromMilliseconds(100) });
            using var cts = new CancellationTokenSource();
            var operation = transport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken, cts.Token);
            await stream.Started.Task;
            if (cancel) { cts.Cancel(); await Reject<OperationCanceledException>(() => operation, "body read cancellation"); }
            else await Reject<TimeoutException>(() => operation, "body read deadline");
            Check(stream.Disposed, "cancelled body disposed");
        }
        using (var transport = new ProviderHttpTextTransport(new MockHandler((_, _) => throw new HttpRequestException(FixtureToken))))
            await Reject<InvalidOperationException>(() => transport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken), "handler exception scrubbed");

        var disposalStream = new TrackingStream([], stall: true);
        var disposalHandler = new MockHandler((_, _) => Task.FromResult(Response(disposalStream)));
        var disposedTransport = new ProviderHttpTextTransport(disposalHandler);
        var activeOnDispose = disposedTransport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken);
        await disposalStream.Started.Task;
        disposedTransport.Dispose();
        await Reject<OperationCanceledException>(() => activeOnDispose, "transport disposal cancels pending read");
        Check(disposalStream.Disposed && disposalHandler.Disposed, "transport disposal releases response and handler");
        await Reject<ObjectDisposedException>(() => disposedTransport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken), "disposed transport rejects reuse");

        var lateResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var transport = new ProviderHttpTextTransport(new MockHandler((_, _) => lateResponse.Task), new() { Timeout = TimeSpan.FromMilliseconds(50) }))
        {
            await Reject<TimeoutException>(() => transport.CompleteAsync("openai", "fixture-model", "Hello", FixtureToken), "uncooperative handler deadline");
            var lateBody = new TrackingStream([]);
            lateResponse.SetResult(Response(lateBody));
            await lateBody.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(lateBody.Disposed, "late HTTP response disposed");
        }
        Console.WriteLine("HTTP fixture checks passed.");
    }

    private static async Task RealtimeChecks()
    {
        var success = new MockSocket(socket =>
        {
            socket.Queue(Created("old", "old-response"));
            socket.Queue(Created(socket.Correlation));
            socket.Queue(Delta("stale", "Wrong.", "old-response"));
            socket.Queue(Delta("delta1", "Hello "), fragment: 11);
            socket.Queue(Delta("delta1", "Hello "));
            socket.Queue(Delta("delta2", "there."));
            socket.Queue(TextDone()); socket.Queue(Done(socket.Correlation));
        });
        Check(await new RealtimeTextTransport(success).CompleteAsync("Hello") == Answer, "realtime fragments and stale/duplicate guard");
        Check(success.Disposed && success.CloseCalls == 1 && success.CancelCalls == 0, "successful socket cleanup");
        using (var request = JsonDocument.Parse(success.Sent[0]))
        {
            var response = request.RootElement.GetProperty("response");
            Check(response.GetProperty("output_modalities").EnumerateArray().Single().GetString() == "text", "realtime text only");
            Check(response.GetProperty("tools").GetArrayLength() == 0 && response.GetProperty("tool_choice").GetString() == "none", "realtime tools disabled");
            Check(response.GetProperty("conversation").GetString() == "none" && response.GetProperty("input")[0].GetProperty("content")[0].GetProperty("type").GetString() == "input_text", "realtime isolated text input");
        }

        var badEvents = new Func<MockSocket, string>[]
        {
            _ => Delta("bad", "Hello there.").Replace("response.output_text.delta", "response.output_audio.delta"),
            _ => "{\"type\":\"response.function_call_arguments.delta\",\"event_id\":\"bad\"}",
            _ => "{\"type\":\"error\",\"event_id\":\"bad\",\"message\":\"fixture error\"}",
            _ => Delta("bad", new string('x', 1601)),
            _ => Delta("bad", "Hello there.").Replace("\"output_index\":0", "\"output_index\":1"),
            _ => Delta("bad", "Hello there.").Replace("\"item_id\":\"item1\"", "\"item_id\":\"item2\""),
            _ => Delta("delta1", "Conflicting duplicate"),
            _ => Delta("bad", "Hello").Replace("\"delta\":", "\"delta\":\"shadow\",\"delta\":"),
            _ => TextDone("Different."),
            s => Done(s.Correlation).Replace("\"completed\"", "\"incomplete\""),
            s => Done(s.Correlation).Replace("output_text", "output_audio"),
            s => Done(s.Correlation).Replace("\"text\":", "\"audio\":{},\"text\":"),
            s => Done(s.Correlation).Replace("Hello there.", "Different."),
            s => Done(s.Correlation).Replace("\"type\":\"message\"", "\"type\":\"function_call\""),
            s => Done("wrong-correlation"),
            s => Created(s.Correlation, "second-response"),
            _ => "{\"type\":\"response.content_part.added\",\"event_id\":\"bad\",\"response_id\":\"r1\",\"item_id\":\"item1\",\"output_index\":0,\"content_index\":0,\"part\":{\"type\":\"refusal\"}}",
            _ => "not JSON"
        };
        foreach (var makeBad in badEvents)
        {
            var socket = new MockSocket(s =>
            {
                s.Queue(Created(s.Correlation)); s.Queue(Delta("delta1", Answer));
                var bad = makeBad(s);
                if (bad.Contains("\"type\":\"response.done\"", StringComparison.Ordinal)) s.Queue(TextDone());
                s.Queue(bad); s.Queue(TextDone()); s.Queue(Done(s.Correlation));
            });
            await Reject<InvalidOperationException>(() => new RealtimeTextTransport(socket).CompleteAsync("Hello"), "unsupported realtime response refused");
            Check(socket.Disposed && socket.CloseCalls == 1 && socket.CancelCalls == 1 && socket.CancelResponseId == "r1", "rejected active response cancelled and closed");
        }

        var missingTextDone = new MockSocket(s => { s.Queue(Created(s.Correlation)); s.Queue(Delta("delta1", Answer)); s.Queue(Done(s.Correlation)); });
        await Reject<InvalidOperationException>(() => new RealtimeTextTransport(missingTextDone).CompleteAsync("Hello"), "missing text completion refused");

        var lateDelta = new MockSocket(s => { s.Queue(Created(s.Correlation)); s.Queue(Delta("delta1", Answer)); s.Queue(TextDone()); s.Queue(Delta("late_delta", "Late.")); });
        await Reject<InvalidOperationException>(() => new RealtimeTextTransport(lateDelta).CompleteAsync("Hello"), "delta after text completion refused");

        foreach (WebSocketMessageType kind in new[] { WebSocketMessageType.Binary, WebSocketMessageType.Close })
        {
            var socket = new MockSocket(s => s.Chunks.Enqueue(new([], true, kind)));
            await Reject<InvalidOperationException>(() => new RealtimeTextTransport(socket).CompleteAsync("Hello"), "non-text socket frame refused");
            Check(socket.Disposed && socket.CloseCalls == 1, "non-text frame cleanup");
        }
        foreach (string bound in new[] { "message", "total", "messages", "frames" })
        {
            var socket = new MockSocket(s =>
            {
                if (bound == "message") s.Queue(new string('a', 1025));
                else if (bound == "frames") for (int i = 0; i < 4; i++) s.Chunks.Enqueue(new([], false, WebSocketMessageType.Text));
                else for (int i = 0; i < 8; i++) s.Queue(JsonSerializer.Serialize(new { type = "session.created", event_id = "session" + i, padding = new string('a', 60) }));
            });
            var limits = new RealtimeTextLimits { MaxMessageBytes = 1024, MaxTotalBytes = bound == "total" ? 1024 : 4096, MaxMessages = bound == "messages" ? 3 : 20, MaxFrames = bound == "frames" ? 3 : 30 };
            // Total case must exceed 1024 even with small valid events.
            if (bound == "total") socket.AfterCreate = s => { for (int i = 0; i < 8; i++) s.Queue(JsonSerializer.Serialize(new { type = "session.created", event_id = "extra" + i, padding = new string('a', 60) })); };
            await Reject<InvalidOperationException>(() => new RealtimeTextTransport(socket, limits).CompleteAsync("Hello"), "realtime " + bound + " bound");
            Check(socket.Disposed && socket.CloseCalls == 1, "bound failure cleanup");
        }

        foreach (bool cancel in new[] { true, false })
        {
            var socket = new MockSocket(s => { s.Queue(Created(s.Correlation)); s.Queue(Delta("delta1", "Partial ")); });
            using var cts = new CancellationTokenSource();
            var operation = new RealtimeTextTransport(socket, new() { Timeout = TimeSpan.FromMilliseconds(100) }).CompleteAsync("Hello", cts.Token);
            await socket.Waiting.Task;
            if (cancel) { cts.Cancel(); await Reject<OperationCanceledException>(() => operation, "active realtime cancellation"); }
            else await Reject<TimeoutException>(() => operation, "realtime deadline");
            Check(socket.CancelCalls == 1 && socket.CancelResponseId == "r1" && socket.CloseCalls == 1 && socket.Disposed, "active cancel targets response and closes");
        }
        var beforeCreated = new MockSocket(_ => { });
        using (var cts = new CancellationTokenSource())
        {
            var operation = new RealtimeTextTransport(beforeCreated).CompleteAsync("Hello", cts.Token);
            await beforeCreated.Waiting.Task; cts.Cancel();
            await Reject<OperationCanceledException>(() => operation, "cancel before created");
            Check(beforeCreated.CancelCalls == 1 && beforeCreated.CancelResponseId is null && beforeCreated.Disposed, "unknown response cancellation closes socket");
        }
        var preCancelled = new MockSocket(_ => { });
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            await Reject<OperationCanceledException>(() => new RealtimeTextTransport(preCancelled).CompleteAsync("Hello", cts.Token), "realtime pre-cancellation");
            Check(preCancelled.Sent.Count == 0 && preCancelled.Disposed, "pre-cancellation sends no text");
        }
        var stalledCleanup = new MockSocket(s => { s.Queue(Created(s.Correlation)); s.Queue("{}"); }) { StallCleanup = true };
        await Reject<InvalidOperationException>(() => new RealtimeTextTransport(stalledCleanup, new() { CleanupTimeout = TimeSpan.FromMilliseconds(30) }).CompleteAsync("Hello"), "cleanup deadline");
        Check(stalledCleanup.AbortCalls > 0 && stalledCleanup.Disposed && stalledCleanup.CloseCalls == 1, "cleanup abort fallback");
        var single = new RealtimeTextTransport(new MockSocket(s => { s.Queue(Created(s.Correlation)); s.Queue(Delta("delta1", Answer)); s.Queue(TextDone()); s.Queue(Done(s.Correlation)); }));
        Check(await single.CompleteAsync("Hello") == Answer, "single use first request");
        await Reject<InvalidOperationException>(() => single.CompleteAsync("Again"), "socket reuse refused");
        Console.WriteLine("Realtime fixture checks passed.");
    }

    private static string Valid(string provider) => provider switch
    {
        "anthropic" => "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"Hello there.\"}]}",
        "gemini" => "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"Hello there.\"}]}}]}",
        _ => "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello there.\"}}]}"
    };
    private static HttpResponseMessage Response(string json) => Response(new TrackingStream(Encoding.UTF8.GetBytes(json)));
    private static HttpResponseMessage Response(Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new("application/json"); return response;
    }
    private static string Created(string correlation, string responseId = "r1") => JsonSerializer.Serialize(new { type = "response.created", event_id = "created_" + responseId, response = new { id = responseId, status = "in_progress", metadata = new { buddy_request_id = correlation } } });
    private static string Delta(string eventId, string text, string responseId = "r1") => JsonSerializer.Serialize(new { type = "response.output_text.delta", event_id = eventId, response_id = responseId, item_id = "item1", output_index = 0, content_index = 0, delta = text });
    private static string TextDone(string text = Answer) => JsonSerializer.Serialize(new { type = "response.output_text.done", event_id = "text_done", response_id = "r1", item_id = "item1", output_index = 0, content_index = 0, text });
    private static string Done(string correlation) => JsonSerializer.Serialize(new { type = "response.done", event_id = "done", response = new { id = "r1", status = "completed", metadata = new { buddy_request_id = correlation }, output = new[] { new { id = "item1", type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = Answer } } } } } });

    private sealed class MockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        internal int Calls;
        internal bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return action(request, cancellationToken); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class Forwarder : DelegatingHandler { }

    private sealed class TrackingStream(byte[] data, bool stall = false) : Stream
    {
        private int position;
        internal int BytesRead;
        internal bool Disposed;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (stall) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            int count = Math.Min(buffer.Length, data.Length - position);
            data.AsMemory(position, count).CopyTo(buffer); position += count; BytesRead += count; return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; DisposedSignal.TrySetResult(); base.Dispose(disposing); }
    }

    private sealed record Chunk(byte[] Bytes, bool End, WebSocketMessageType Kind);
    private sealed class MockSocket(Action<MockSocket> create) : IRealtimeTextSocket
    {
        internal Queue<Chunk> Chunks = new();
        internal List<byte[]> Sent = new();
        internal string Correlation = "";
        internal int CancelCalls, CloseCalls, AbortCalls;
        internal string? CancelResponseId;
        internal bool Disposed, StallCleanup;
        internal Action<MockSocket>? AfterCreate;
        internal TaskCompletionSource Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Queue(string json, int fragment = 4096)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            for (int i = 0; i < bytes.Length; i += fragment) Chunks.Enqueue(new(bytes.Skip(i).Take(fragment).ToArray(), i + fragment >= bytes.Length, WebSocketMessageType.Text));
        }
        public Task SendAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken)
        {
            Sent.Add(utf8Json.ToArray());
            using var doc = JsonDocument.Parse(utf8Json);
            var root = doc.RootElement;
            if (root.GetProperty("type").GetString() == "response.create")
            {
                Correlation = root.GetProperty("response").GetProperty("metadata").GetProperty("buddy_request_id").GetString()!;
                create(this); AfterCreate?.Invoke(this);
            }
            else
            {
                CancelCalls++;
                CancelResponseId = root.TryGetProperty("response_id", out var id) ? id.GetString() : null;
                if (StallCleanup) return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return Task.CompletedTask;
        }
        public async Task<RealtimeTextFrame> ReceiveAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            if (Chunks.Count == 0) { Waiting.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            var chunk = Chunks.Dequeue(); chunk.Bytes.CopyTo(destination); return new(chunk.Bytes.Length, chunk.End, chunk.Kind);
        }
        public Task CloseAsync(CancellationToken cancellationToken) { CloseCalls++; return StallCleanup ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken) : Task.CompletedTask; }
        public void Abort() => AbortCalls++;
        public void Dispose() => Disposed = true;
    }
}

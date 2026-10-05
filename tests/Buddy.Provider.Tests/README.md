# Provider transport fixtures

Run from the repository root:

```powershell
dotnet run -c Release -r win-x64 --self-contained true --project tests/Buddy.Provider.Tests/Buddy.Provider.Tests.csproj
```

This executable uses synthetic HTTP handlers, synthetic socket frames and one clearly marked fixture token. It never opens a socket or makes an inference request. Assertion output contains labels and counts, not request bodies or authorization values. The SDK may need its normal build cache/configuration permissions.

`ProviderTransportFactory.CreateProduction()` always returns a disconnected transport. It accepts no credential, URI, transport dependency or activation flag. Both experimental components are internal, with friend access limited to this test assembly. HTTP rejects built-in and delegating handlers; injected mock code is trusted and is not a sandbox against a malicious handler implementation. There is no realtime connector or credential reader.

HTTP fixtures cover all four existing request formats and provider-specific headers, fixed HTTPS endpoints, refused redirects and errors without retries, strict complete text, malformed/duplicate JSON, tools/audio/refusals, declared and streamed byte limits, cancellation/deadline, disposal and a response arriving after timeout. Responses are capped at 65,536 bytes; the default deadline is 30 seconds, with a 60-second configuration ceiling. Responses must be JSON with no content encoding.

Realtime fixtures cover text-only out-of-band requests, metadata correlation, stale response IDs, duplicate and conflicting events, split frames, single output item/content indices, agreement among deltas/text-done/final output, unsupported content, byte/message/frame limits, active cancellation and close/abort/dispose cleanup. Defaults are 65,536 bytes per message, 262,144 total bytes, 256 messages, 512 frames and 30 seconds. Each cleanup operation gets a separate 500 ms deadline. Socket implementations must honor cancellation or abort outstanding operations. This is one request per injected socket; no partial text is released before the validated final answer.

This is partial transport implementation, not live provider acceptance. Production routing, secure credential entry/storage, transmitted-content/cost consent, an audited redirect-disabled HTTP connector, a fixed-endpoint realtime handshake, and real provider acceptance remain absent. Realtime protocol support deliberately handles only one text item and rejects new/unsupported response shapes. No audio, physical microphone/headphone checks or realtime voice parity is claimed.

Official API contracts inspected on 2026-10-04:

- [OpenAI chat completion reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)
- [OpenAI realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations) for text output, custom input, out-of-band response metadata, completion and cancellation.
- [OpenAI WebSocket guide](https://developers.openai.com/api/docs/guides/realtime-websocket) for the future connection boundary; no connector is implemented here.
- [Anthropic API overview](https://platform.claude.com/docs/en/api/overview) and [Messages reference](https://platform.claude.com/docs/en/api/messages/create)
- [Gemini key headers](https://ai.google.dev/gemini-api/docs/api-key) and [generateContent reference](https://ai.google.dev/api/generate-content)
- [OpenRouter authentication](https://openrouter.ai/docs/api/reference/authentication)

These references support protocol shape choices only. Model availability, pricing, live authentication and network interoperability were not tested.

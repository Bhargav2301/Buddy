using Buddy.Server;
using Buddy.RegionCompletion;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
async Task<bool> Refused(Func<Task> action) { try { await action().WaitAsync(TimeSpan.FromSeconds(4)); return false; } catch (Exception e) when (e is BuddyException or InvalidOperationException or OperationCanceledException or ArgumentException) { return true; } }
var fixture = RegionFixture.Synthetic();
Check(fixture.Png.SequenceEqual(RegionFixture.Synthetic().Png) && fixture.Png.Length < 2_000_000, "Owned synthetic PNG is deterministic and within service image bounds");
Check(fixture.Origin.Contains("synthetic") && !fixture.Expectation.Question.Contains("blue") && !fixture.Expectation.Question.Contains("orange") && !fixture.Expectation.Question.Contains("6"), "Fixture is explicitly synthetic and question does not leak expected image facts");
using (var compressed = new MemoryStream()) {
    int at = 8; while (at < fixture.Png.Length) { int length = (int)BinaryPrimitives.ReadUInt32BigEndian(fixture.Png.AsSpan(at, 4)); string type = Encoding.ASCII.GetString(fixture.Png, at + 4, 4); if (type == "IDAT") compressed.Write(fixture.Png, at + 8, length); at += 12 + length; }
    compressed.Position = 0; using var zlib = new ZLibStream(compressed, CompressionMode.Decompress); using var decoded = new MemoryStream(); zlib.CopyTo(decoded); byte[] rgb = decoded.ToArray();
    byte[] Pixel(int x, int y) => rgb.AsSpan(y * (PngChart.Width * 3 + 1) + 1 + x * 3, 3).ToArray();
    Check(Pixel(75, 160).SequenceEqual(new byte[] { 230, 125, 35 }) && Pixel(195, 40).SequenceEqual(new byte[] { 35, 90, 210 }) && Pixel(75, 40).All(x => x == 255), "Decoded fixture independently confirms short orange left bar and taller blue right bar");
}
const string usefulImage = "The blue bar B has value 6 and is three times taller than the orange bar A, which has value 2.";
Check(RegionOracle.Image(new(usefulImage), fixture.Expectation).Passed, "Known meaningful image explanation satisfies independent facts");
foreach (var swapped in new[] {
    "The blue bar B has value 2 and is three times taller than the orange bar A, which has value 6.",
    "The orange bar A has value 6 and is taller than the blue bar B, which has value 2.",
    "The blue bar B has value 6 and is taller. The orange bar A has value 2. The blue bar B also has value 2."
}) Check(!RegionOracle.Image(new(swapped), fixture.Expectation).Passed, "Correct keywords with swapped or conflicting object values fail the relation oracle");
Check(RegionOracle.Image(new("The orange bar A has value 2. The blue bar B has value 6 and is three times taller."), fixture.Expectation).Passed, "Correct object/value relationships pass in reversed sentence order");
foreach (var speech in new[] { "The image contains a chart.", "I cannot read this image; please select a clearer area.", "The red bar is taller than the green bar.", "Click the blue bar B with value 6; it is taller than orange bar A with value 2.", fixture.Expectation.Question, usefulImage + " It is a chart. It has colors. Another sentence." })
    Check(!RegionOracle.Image(new(speech), fixture.Expectation).Passed, "Generic/refused/contradictory/action/echo/overlong output cannot masquerade as image completion");
Check(!RegionOracle.Image(new(usefulImage, new("Click B", "invented", "B", "Button")), fixture.Expectation).Passed, "A useful explanation with an invented annotation still fails");
const string usefulResearch = "Rainbows form when sunlight refracts as it enters water droplets and reflects inside them.";
var page = new WebSource("Owned canned source", "https://pages.example.org/rainbows", "Sunlight refracts and reflects in water droplets.");
Check(RegionOracle.Research(new(usefulResearch, [new(page.Title, page.Url)]), [page]).Passed, "Research oracle requires a real fetched public source and query-specific physical mechanism");
foreach (var result in new[] {
    new ReviewedRegionResearchResult("I found some sources about rainbows.", [new(page.Title, page.Url)]),
    new ReviewedRegionResearchResult(usefulResearch, []),
    new ReviewedRegionResearchResult(usefulResearch, [new("invented", "https://invented.example.org")]),
    new ReviewedRegionResearchResult(usefulResearch + " See https://example.org.", [new(page.Title, page.Url)]),
    new ReviewedRegionResearchResult("The fetched excerpts do not establish a reliable answer; which point should I look up more specifically?", [new(page.Title, page.Url)])
}) Check(!RegionOracle.Research(result, [page]).Passed, "Generic/fabricated/missing-source/raw-URL/clarification research is not successful completion");

using (var state = new OwnedState())
using (var log = new ProbeLog()) {
    await state.Select("gemma3:4b");
    var inner = new ModelFixture(usefulImage, usefulResearch);
    using var audit = new ModelAuditHandler(inner, log) { Stage = "region", ExpectedImageSha256 = fixture.Sha256 };
    using var http = new HttpClient(audit) { BaseAddress = new("http://127.0.0.1:11434/") };
    var service = new BuddyService(state.Store, new(http), new NoWeb()) { WebEnabled = false };
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    var result = await ProbeRunner.Region(service, fixture, deadline.Token);
    Check(result.Passed && audit.ImageRequests == 1, "Production Teach completes the owned region case through actual harness path with mocked local inference");
    Check(!string.Join('\n', log.Records).Contains(Convert.ToBase64String(fixture.Png)), "Evidence logs record image hash and count, never image bytes");
    using var sent = JsonDocument.Parse(inner.Bodies.Single());
    Check(sent.RootElement.GetProperty("messages")[1].GetProperty("images").GetArrayLength() == 1 && sent.RootElement.GetProperty("model").GetString() == "gemma3:4b", "Only local vision request contains the explicit owned image");
    var input = sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
    Check(!input.Contains("blue") && !input.Contains("orange") && !input.Contains("value 6"), "Independent visual expectations are not injected into the model input");
    Check(await state.Store.Read(s => s.Conversations.Count + s.Memories.Count + s.Audit.Count) == 0, "Image explanation creates no conversation, memory or audit entry");
}
using (var state = new OwnedState())
using (var log = new ProbeLog()) {
    await state.Select("gemma3:4b");
    using var network = new WebAuditHandler(new PublicTextFixture(), log);
    using var productionWeb = new WebResearch(network); var observed = new ObservedResearch(productionWeb, log);
    using var audit = new ModelAuditHandler(new ModelFixture(usefulImage, usefulResearch), log) { Stage = "research", ExpectedImageSha256 = fixture.Sha256 };
    using var http = new HttpClient(audit) { BaseAddress = new("http://127.0.0.1:11434/") };
    var service = new BuddyService(state.Store, new(http), observed) { WebEnabled = true };
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var result = await ProbeRunner.Research(service, observed, deadline.Token);
    Check(result.Passed && observed.Queries.SequenceEqual([RegionFixture.ReviewedQuery]) && observed.FetchAttempts.Count == 1, "Production research service completes exact reviewed query with actual parser over mocked public HTTP");
    Check(network.Destinations.Count == 2 && Uri.UnescapeDataString(network.Destinations[0].Query[3..]) == RegionFixture.ReviewedQuery, "Transport audit records exact reviewed search bytes and only public HTTPS fetches");
    Check(audit.ImageRequests == 0 && audit.TextRequests == 1 && !string.Join('\n', log.Records).Contains(Convert.ToBase64String(fixture.Png)), "Research model/network audit contains no selected image or audio");
    Check(await Refused(() => observed.Search("model-expanded query", default)), "Audit refuses query expansion rather than silently recording it as consented");
}
foreach (var request in new[] {
    new HttpRequestMessage(HttpMethod.Post, "https://pages.example.org") { Content = new StringContent("canned image bytes must not leave") },
    new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1/private"),
    new HttpRequestMessage(HttpMethod.Get, "http://pages.example.org")
}) {
    using var log = new ProbeLog(); var inner = new PublicTextFixture(); using var audit = new WebAuditHandler(inner, log); using var http = new HttpClient(audit);
    Check(await Refused(() => http.SendAsync(request)) && inner.Calls == 0, "External audit refuses body/private/insecure requests before transport"); request.Dispose();
}
using (var log = new ProbeLog()) {
    var inner = new ModelFixture(usefulImage, usefulResearch); using var audit = new ModelAuditHandler(inner, log); using var http = new HttpClient(audit);
    Check(await Refused(() => http.GetAsync("https://external.example.org/api/chat")) && inner.Bodies.Count == 0, "Local model audit cannot be redirected to an external provider");
}
using (var state = new OwnedState())
using (var log = new ProbeLog()) {
    await state.Select("gemma3:4b"); using var stop = new CancellationTokenSource(); stop.Cancel();
    var inner = new ModelFixture(usefulImage, usefulResearch); using var http = new HttpClient(inner) { BaseAddress = new("http://127.0.0.1:11434/") };
    var service = new BuddyService(state.Store, new(http), new NoWeb());
    Check(await Refused(() => ProbeRunner.Region(service, fixture, stop.Token)) && inner.Bodies.Count == 0, "Cancelled region case never starts local inference");
}
Console.WriteLine($"REGION COMPLETION MOCK CHECKS PASSED: {checks}");

sealed class ModelFixture(string image, string research) : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        var body = await request.Content!.ReadAsStringAsync(ct); Bodies.Add(body);
        string answer = body.Contains("exactReviewedQuery") ? JsonSerializer.Serialize(new { speech = research, supported = true }) : JsonSerializer.Serialize(new { summary = image, steps = Array.Empty<object>() });
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = answer }, done = true, done_reason = "stop" }), Encoding.UTF8, "application/json") };
    }
}
sealed class PublicTextFixture : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        ct.ThrowIfCancellationRequested(); Calls++;
        string html = Calls == 1 ? "<a class='result__a' href='https://pages.example.org/rainbows'>Rainbow optics</a>" : "<html><head><title>Owned canned source</title></head><body><main>Sunlight refracts and reflects in water droplets.</main></body></html>";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") });
    }
}

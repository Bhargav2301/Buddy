using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buddy.Server;

public sealed class BuddyHost : IAsyncDisposable
{
    public const int DefaultPort = 47831;
    private readonly WebApplication app;
    private readonly HttpClient client;
    private readonly X509Certificate2 cert;
    public BuddyService Service { get; }
    public string Fingerprint { get; }
    public int Port { get; }
    private BuddyHost(WebApplication app, BuddyService service, X509Certificate2 cert, HttpClient client, int port)
    { this.app = app; Service = service; this.cert = cert; this.client = client; Port = port; Fingerprint = Convert.ToHexString(SHA256.HashData(cert.RawData)); }

    public static async Task<BuddyHost> Start(string directory, int port = DefaultPort, Uri? engineAddress = null)
    {
        var keyDir = Path.Combine(directory, "keys"); Directory.CreateDirectory(keyDir);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDir), setup => {
            setup.SetApplicationName("Buddy.Local.v1");
            if (OperatingSystem.IsWindows()) setup.ProtectKeysWithDpapi();
        });
        var protector = provider.CreateProtector("Buddy.TLS.v1"); var certPath = Path.Combine(directory, "tls.encrypted");
        X509Certificate2 cert;
        if (File.Exists(certPath)) cert = new X509Certificate2(protector.Unprotect(await File.ReadAllBytesAsync(certPath)), (string?)null, X509KeyStorageFlags.Exportable);
        else
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=Buddy Local PC", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
            var pfx = generated.Export(X509ContentType.Pfx);
            await File.WriteAllBytesAsync(certPath, protector.Protect(pfx));
            cert = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
            CryptographicOperations.ZeroMemory(pfx);
        }
        var store = new StateStore(directory, provider); await store.EnsureSaved();
        var client = new HttpClient { BaseAddress = engineAddress ?? new Uri("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan };
        var service = new BuddyService(store, new OllamaEngine(client));
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => {
            k.Limits.MaxRequestBodySize = 3_000_000;
            k.Limits.MaxConcurrentConnections = 64;
            k.Listen(IPAddress.Any, port, listen => listen.UseHttps(cert, https => https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13));
        });
        builder.Services.AddRateLimiter(options => {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build(); app.UseRateLimiter();
        app.Use(async (ctx, next) => {
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            try
            {
                if (ctx.Request.Path != "/health" && ctx.Request.Path != "/v1/pair")
                {
                    var header = ctx.Request.Headers.Authorization.ToString();
                    var identity = header.StartsWith("Bearer ", StringComparison.Ordinal) ? await store.Authenticate(header[7..]) : null;
                    if (identity is null) throw new BuddyException("UNAUTHORIZED", "Pair this device with your PC again.", 401);
                    ctx.Items["device"] = identity;
                }
                await next(ctx);
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
            catch (BuddyException ex) { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = ex.Status; await ctx.Response.WriteAsJsonAsync(new { error = new { code = ex.Code, message = ex.Message } }); } }
            catch (Exception) { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { error = new { code = "INTERNAL_ERROR", message = "Buddy could not complete this request. Restart the PC app if it persists." } }); } }
        });
        app.MapGet("/health", () => new { app = "Buddy", version = typeof(BuddyHost).Assembly.GetName().Version?.ToString(3) });
        app.MapPost("/v1/pair", async (PairRequest input) => {
            var name = Security.Text(input.Name, 60, "Device name");
            service.Pairing.Redeem(Security.Text(input.Code, 6, "Pairing code"));
            var token = Security.NewToken(); var id = Guid.NewGuid().ToString();
            await store.Update(s => { s.Devices.Add(new(id, name, Security.Hash(token), DateTimeOffset.UtcNow)); return true; });
            return new { token, deviceId = id, name };
        });
        app.MapGet("/v1/status", async (CancellationToken ct) => { var s = await store.Read(s => s); return await service.Engine.Status(s.Model, s.VisionModel, ct); });
        app.MapGet("/v1/conversations", async () => await store.Read(s => s.Conversations.OrderByDescending(c => c.UpdatedAt).Select(c => new { c.Id, c.Title, c.UpdatedAt }).ToList()));
        app.MapPost("/v1/conversations", (NoteRequest input) => service.CreateConversation(input.Title));
        app.MapGet("/v1/conversations/{id}", async (string id) => await store.Read(s => s.Conversations.FirstOrDefault(c => c.Id == Security.Id(id))) ?? throw new BuddyException("NOT_FOUND", "Conversation not found.", 404));
        app.MapDelete("/v1/conversations/{id}", async (string id) => { await service.DeleteConversation(id); return Results.NoContent(); });
        app.MapPost("/v1/chat", async (ChatRequest input, HttpContext ctx) => {
            ctx.Response.ContentType = "application/x-ndjson";
            try { await foreach (var item in service.Chat(input, ctx.RequestAborted)) { await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(item, StateStore.Json) + "\n", ctx.RequestAborted); await ctx.Response.Body.FlushAsync(ctx.RequestAborted); } }
            catch (OperationCanceledException) when (!ctx.RequestAborted.IsCancellationRequested) { await ctx.Response.WriteAsync("{\"type\":\"error\",\"code\":\"CANCELLED\",\"text\":\"The answer was stopped or timed out.\"}\n"); }
            catch (BuddyException ex) { await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new StreamEvent("error", ex.Message, ex.Code), StateStore.Json) + "\n"); }
            catch (Exception) when (!ctx.RequestAborted.IsCancellationRequested) { await ctx.Response.WriteAsync("{\"type\":\"error\",\"code\":\"ENGINE_ERROR\",\"text\":\"The local AI connection failed. Check PC setup.\"}\n"); }
        });
        app.MapPost("/v1/refine", async (RefineRequest input, CancellationToken ct) => new { refinedPrompt = await service.Refine(input.Prompt, ct), engine = "buddy_local" });
        app.MapPost("/v1/agent/plan", (PlanningRequest input, HttpContext ctx, CancellationToken ct) => {
            if (ctx.Items["device"] as string != "desktop") throw new BuddyException("PC_ONLY", "Create computer-control plans on the PC.", 403);
            return service.PlanAgent(input, ct);
        });
        app.MapPost("/v1/guide/start", (PlanningRequest input, CancellationToken ct) => service.PlanGuide(input, ct));
        app.MapGet("/v1/memories", () => store.Read(s => s.Memories));
        app.MapGet("/v1/prompts", () => store.Read(s => s.Prompts));
        foreach (var kind in new[] { "memories", "prompts" })
        {
            app.MapPost("/v1/" + kind, async (NoteRequest input) => {
                var note = new Note(Guid.NewGuid().ToString(), Security.Text(input.Title, 100, "Title"), Security.Text(input.Text, 2000, "Text"));
                return await store.Update(s => { var list = kind == "memories" ? s.Memories : s.Prompts; if (list.Count >= 50) throw new BuddyException("LIMIT", "Remove an older item before adding another."); list.Add(note); return note; });
            });
            app.MapDelete("/v1/" + kind + "/{id}", async (string id) => { await store.Update(s => (kind == "memories" ? s.Memories : s.Prompts).RemoveAll(n => n.Id == Security.Id(id))); return Results.NoContent(); });
        }
        await app.StartAsync(); return new(app, service, cert, client, port);
    }
    public async ValueTask DisposeAsync() { Service.StopAll(); await app.StopAsync(); await app.DisposeAsync(); (Service.Web as IDisposable)?.Dispose(); client.Dispose(); cert.Dispose(); }
}

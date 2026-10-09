using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

[assembly: InternalsVisibleTo("Buddy.ProviderRouting.Tests")]
[assembly: InternalsVisibleTo("Buddy.LocalAgentFollowup.Tests")]
[assembly: InternalsVisibleTo("Buddy.Followup53Boundary.Tests")]

namespace Buddy.Server;

public sealed record ProviderConsent(string Provider, string Model, bool TextSharingAccepted = false, bool PossibleChargesAccepted = false)
{
    public void Validate()
    {
        if (!TextSharingAccepted || !PossibleChargesAccepted)
            throw new InvalidOperationException("Choose text sharing and possible provider charges before configuring a cloud session.");
        _ = ProviderProtocols.Build(Provider, Model, "Validate selection.");
    }
}

public sealed record ProviderQuestionReview(Guid SessionId, Guid RequestId, string Provider, string Model,
    string Text, string TextSha256, DateTimeOffset Expires)
{
    public override string ToString() => $"ProviderQuestionReview {{ Provider = {Provider}, Model = {Model}, payload = [private] }}";
}
public sealed record ProviderRoutingStatus(Guid SessionId, string Provider, string Model, bool Configured,
    bool RequestInFlight, int CompletedResponses, string Detail);

/// <summary>Session-only key, explicit current-question review, no implicit cloud route or persisted consent.</summary>
public sealed class ProviderRoutingSession : IProviderTextTransport, IDisposable
{
    private readonly object gate = new();
    private readonly ProviderHttpTextTransport transport;
    private readonly ProviderConsent consent;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<long> timestamp;
    private readonly CancellationTokenSource lifetime = new();
    private char[] credential;
    private ProviderQuestionReview? pending;
    private long pendingTimestamp;
    private DateTimeOffset lastNow = DateTimeOffset.MinValue;
    private bool disconnected, busy;
    private int completed;
    public Guid SessionId { get; } = Guid.NewGuid();

    private ProviderRoutingSession(ProviderConsent consent, ReadOnlySpan<char> credential, ProviderHttpTextTransport transport, Func<DateTimeOffset>? clock, Func<long>? timestamp)
    {
        this.consent = consent; this.credential = credential.ToArray(); this.transport = transport;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
    }
    private static void Validate(ProviderConsent consent, ReadOnlySpan<char> credential)
    {
        ArgumentNullException.ThrowIfNull(consent); consent.Validate();
        if (credential.Length is < 1 or > 512) throw new InvalidOperationException("Enter a valid provider key.");
        foreach (char c in credential)
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.') throw new InvalidOperationException("Enter a valid provider key.");
    }
    public static ProviderRoutingSession CreateProduction(ProviderConsent consent, ReadOnlySpan<char> credential)
    {
        Validate(consent, credential);
        return new(consent, credential, ProviderHttpTextTransport.CreateProduction(), null, null);
    }
    internal static ProviderRoutingSession CreateForTesting(ProviderConsent consent, ReadOnlySpan<char> credential,
        HttpMessageHandler handler, Func<DateTimeOffset>? clock = null, Func<long>? timestamp = null)
    {
        Validate(consent, credential);
        return new(consent, credential, new ProviderHttpTextTransport(handler), clock, timestamp);
    }
    public ProviderRoutingStatus Status
    {
        get { lock (gate) return new(SessionId, consent.Provider, consent.Model, !disconnected, busy, completed,
            disconnected ? "Disconnected; the session key was cleared." : completed == 0 ? "Configured for reviewed typed text; no successful provider response verified." : "A reviewed text response completed in this session."); }
    }
    public ProviderQuestionReview PrepareQuestion(string question)
    {
        // Do not trim or add history: the review displays the exact text sent, alongside a fixed reply policy.
        if (string.IsNullOrWhiteSpace(question) || question.Length > 8000 || question != question.Trim())
            throw new InvalidOperationException("Review 1–8000 characters of typed text without surrounding whitespace.");
        try { _ = new UTF8Encoding(false, true).GetByteCount(question); }
        catch (EncoderFallbackException) { throw new InvalidOperationException("The question contains invalid Unicode. Edit it before review."); }
        lock (gate)
        {
            RequireActive(); if (busy) throw new InvalidOperationException("Wait for or stop the current provider request.");
            var now = Now(); pendingTimestamp = timestamp();
            return pending = new(SessionId, Guid.NewGuid(), consent.Provider, consent.Model, question,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(question))), now.AddMinutes(2));
        }
    }
    public void CancelReview() { lock (gate) pending = null; }
    // An unreviewed IProviderTextTransport call is deliberately never a network fallback.
    public Task<string> CompleteAsync(string provider, string model, string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<string>(new InvalidOperationException("Review the exact typed text and approve Send to this provider first."));
    }
    /// <summary>Call only from the explicit Send approval for the returned immutable review. One use, two-minute expiry.</summary>
    public async Task<string> CompleteReviewedAsync(ProviderQuestionReview review, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked; char[] key;
        lock (gate)
        {
            RequireActive(); cancellationToken.ThrowIfCancellationRequested();
            var now = Now(); var tick = timestamp();
            if (busy || pending is null || review != pending || review.SessionId != SessionId || now >= review.Expires ||
                tick < pendingTimestamp || Stopwatch.GetElapsedTime(pendingTimestamp, tick) >= TimeSpan.FromMinutes(2))
                throw new InvalidOperationException("The provider review expired or changed. Review the current text again.");
            pending = null; busy = true;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            key = (char[])credential.Clone();
        }
        try
        {
            // HTTP headers require an immutable string; it is never logged/persisted. The char buffers are cleared.
            string answer = await transport.CompleteAsync(consent.Provider, consent.Model, review.Text, new string(key), linked.Token).ConfigureAwait(false);
            lock (gate)
            {
                linked.Token.ThrowIfCancellationRequested(); RequireActive(); completed++;
                return answer;
            }
        }
        finally { Array.Clear(key); linked.Dispose(); lock (gate) busy = false; }
    }
    private void RequireActive() { if (disconnected) throw new InvalidOperationException("This provider session is disconnected."); }
    private DateTimeOffset Now()
    {
        var now = clock();
        if (now < lastNow) { pending = null; throw new InvalidOperationException("The clock changed; review the current text again."); }
        return lastNow = now;
    }
    public void Disconnect()
    {
        lock (gate)
        {
            if (disconnected) return; disconnected = true; pending = null;
            Array.Clear(credential); credential = [];
        }
        try { lifetime.Cancel(); } finally { transport.Dispose(); }
    }
    public void Dispose() => Disconnect();
}

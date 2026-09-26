using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Buddy.Server;

public static class Security
{
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    public static string Text(string? text, int max, string name)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > max) throw new BuddyException("INVALID_INPUT", $"{name} must contain 1–{max} characters.");
        return text;
    }
    public static string Id(string? id)
    {
        if (!Guid.TryParse(id, out var parsed)) throw new BuddyException("INVALID_ID", "A valid identifier is required.");
        return parsed.ToString();
    }
    // Redact common sensitive tokens in text context. This is not image/OCR redaction.
    public static string Redact(string text) => SensitiveTokens.Replace(text, "[redacted]");
    private static readonly Regex SensitiveTokens = new(@"(?i)\b(?:\d[ -]?){13,19}\b|\b[0-9]{4}\s[0-9]{4}\s[0-9]{4}\b|\b[A-Z]{5}[0-9]{4}[A-Z]\b|(?:password|otp|secret|api[_ -]?key)\s*[:=]\s*\S+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
}

public sealed class PairingWindow
{
    private readonly object gate = new();
    private string? code;
    private DateTimeOffset expires;
    private int attempts;
    public (string Code, DateTimeOffset Expires) Open()
    {
        lock (gate) { code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); expires = DateTimeOffset.UtcNow.AddMinutes(5); attempts = 0; return (code, expires); }
    }
    public void Redeem(string candidate)
    {
        lock (gate)
        {
            if (code is null || DateTimeOffset.UtcNow > expires || attempts >= 5) throw new BuddyException("PAIRING_CLOSED", "Open Pair phone on your PC to generate a fresh code.", 403);
            attempts++;
            if (!Security.Equal(code, candidate)) throw new BuddyException("INVALID_CODE", "The pairing code is incorrect.", 403);
            code = null; // one use, never retained in the data store
        }
    }
}

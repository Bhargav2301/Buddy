namespace Buddy.Windows;

// The adapter must validate the same live, writable field on every read/write.
internal interface IVerifiedTextField
{
    string Identity { get; }
    string Read();
    void Write(string expected, string text, CancellationToken ct);
}

internal sealed class GuardedEdit(IVerifiedTextField field, string original)
{
    private readonly string identity = field.Identity;
    private string? replacement;
    private DateTimeOffset appliedAt;
    internal string Original => original;
    internal bool CanUndo(DateTimeOffset now) => replacement is not null && now >= appliedAt && now - appliedAt <= TimeSpan.FromSeconds(30);

    internal void Apply(string text, DateTimeOffset now, CancellationToken ct)
    {
        if (replacement is not null) throw new InvalidOperationException("This replacement has already been applied.");
        ct.ThrowIfCancellationRequested();
        Verify(original);
        ct.ThrowIfCancellationRequested();
        field.Write(original, text, ct);
        // Do not claim success when the host rejected or transformed the replacement.
        Verify(text);
        replacement = text; appliedAt = now;
    }

    internal void Undo(DateTimeOffset now, CancellationToken ct)
    {
        if (!CanUndo(now)) throw new InvalidOperationException("Undo expired. Copy the original instead.");
        ct.ThrowIfCancellationRequested();
        Verify(replacement!);
        ct.ThrowIfCancellationRequested();
        field.Write(replacement!, original, ct);
        Verify(original); replacement = null;
    }

    private void Verify(string expected)
    {
        if (!string.Equals(field.Identity, identity, StringComparison.Ordinal) || !string.Equals(field.Read(), expected, StringComparison.Ordinal))
            throw new InvalidOperationException("The original field changed. Copy the text instead.");
    }
}

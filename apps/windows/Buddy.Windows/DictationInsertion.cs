namespace Buddy.Windows;

internal sealed record DictationInsertion(int Start, int Length)
{
    internal string Replace(string original, string speech)
    {
        if (Start < 0 || Length < 0 || Start > original.Length - Length || original.Length - Length + speech.Length > 20000)
            throw new InvalidOperationException("The insertion range is invalid or too long. Copy the transcript instead.");
        return original[..Start] + speech + original[(Start + Length)..];
    }
    internal static DictationInsertion Verify(string original, string document, string prefix, string selected)
    {
        if (!string.Equals(original, document, StringComparison.Ordinal) || !original.StartsWith(prefix, StringComparison.Ordinal) ||
            prefix.Length + selected.Length > original.Length || !original.AsSpan(prefix.Length, selected.Length).SequenceEqual(selected.AsSpan()))
            throw new InvalidOperationException("The app does not expose an exact text selection. Copy dictation into your field instead.");
        return new(prefix.Length, selected.Length);
    }
}

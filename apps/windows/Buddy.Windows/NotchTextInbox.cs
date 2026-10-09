namespace Buddy.Windows;

internal sealed record NotchTextAttachment(string Id, string Name, string Text, int ByteCount);

internal static class NotchTextInbox
{
    internal const int MaximumBytes = RefinementResourceReader.MaximumBytes;
    internal const int MaximumCharacters = 12000;
    internal static async Task<NotchTextAttachment> Read(string path, CancellationToken token)
    {
        // Reuse Buddy's existing local disk reader: pinned no-follow ancestor
        // handles, regular single-link file, final path/volume verification,
        // local drive type, strict UTF-8 and bounded cancellable content reads.
        RefinementResourceReadResult selected;
        try { selected = await RefinementResourceReader.ReadAsync(path, token).ConfigureAwait(false); }
        catch (InvalidOperationException error) { throw new IOException(error.Message, error); }
        token.ThrowIfCancellationRequested();
        string text = selected.Source.Text;
        if (text.Length > MaximumCharacters) throw new IOException("Choose text with no more than 12,000 characters.");
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new IOException("The selected file contains non-text control data.");
        return new(selected.Source.Id, selected.Source.Title, text, selected.ByteCount);
    }
}

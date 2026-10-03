namespace Buddy.Windows;

// Metadata only, captured before a Buddy surface takes focus. No prompt text is cached.
internal sealed record SummonedField(FieldAnchor Anchor,string Title,long CapturedAt)
{
    internal void Validate(IntPtr intendedWindow,string currentTitle,long now)
    {
        if(intendedWindow!=Anchor.Window||currentTitle!=Title||now-CapturedAt>120000||now<CapturedAt)
            throw new InvalidOperationException("The original prompt field changed or expired. Focus it and use Refine source field again (Ctrl+Alt+R).");
    }
}

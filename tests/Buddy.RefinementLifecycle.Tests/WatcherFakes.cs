// Deliberately fake UI/native surfaces: this compiles the real watcher without WPF
// or an interactive desktop. It exercises async ownership, not UIA capability.
namespace System.Windows.Threading
{
    internal sealed class Dispatcher { public void BeginInvoke(Action action) => action(); }
    internal sealed class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public Dispatcher Dispatcher { get; } = new();
        public event EventHandler? Tick;
        public void Start() { }
        public void Stop() { }
        public void Pulse() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
namespace Buddy.Windows
{
    internal sealed record FieldAnchor(IntPtr Window, string Identity, int Bounds = 0, bool ExplicitInvocation = true);
    internal sealed record FakeEdit(string Original);
    internal sealed record FocusedDraft(FakeEdit Edit, FieldAnchor Anchor);
    internal sealed class Subscription : IDisposable { internal int Disposals; public void Dispose() => Disposals++; }
    internal sealed class FocusedFieldEditor
    {
        internal Func<IntPtr, CancellationToken, Task<FieldAnchor?>> ProbeCall = (window, _) => Task.FromResult<FieldAnchor?>(new(window, "A"));
        internal Func<FieldAnchor, Action, CancellationToken, Task<IDisposable?>> WatchCall = (_, _, _) => Task.FromResult<IDisposable?>(new Subscription());
        internal Task<FieldAnchor?> Probe(IntPtr window, CancellationToken token, bool explicitInvocation = false) => ProbeCall(window, token);
        internal Task<IDisposable?> Watch(FieldAnchor anchor, Action changed, CancellationToken token) => WatchCall(anchor, changed, token);
        internal Task<FocusedDraft> Capture(IntPtr window, CancellationToken token, string expectedIdentity, bool strictFocus) => Task.FromResult(new FocusedDraft(new("A supplied task"), new(window, expectedIdentity)));
    }
    internal sealed class InlinePromptWindow
    {
        internal static readonly List<InlinePromptWindow> Cards = [];
        internal Func<CancellationToken, Task<bool>> Current = _ => Task.FromResult(true);
        internal bool IsMutating => false;
        internal bool ClosedState;
        internal event EventHandler? Closed;
        internal InlinePromptWindow(Buddy.Server.BuddyService service, FocusedFieldEditor editor, FocusedDraft draft, Action voice, string shortcut,
            Buddy.Server.RefineRequest? request = null, Func<bool>? optionsCurrent = null, Action? configureOptions = null, ExternalContextSelection? contextSelection = null) => Cards.Add(this);
        internal bool Reply(string text) => false;
        internal Task Refine() => Task.CompletedTask;
        internal Task<bool> IsCurrent(CancellationToken ct) => Current(ct);
        internal void Reanchor(int bounds) { }
        internal void Show() { }
        internal void Close() { ClosedState = true; Closed?.Invoke(this, EventArgs.Empty); }
        internal void RepeatOldClosed() => Closed?.Invoke(this, EventArgs.Empty);
    }
    internal static class Native
    {
        internal static IntPtr Foreground = new(1);
        internal static IntPtr GetForegroundWindow() => Foreground;
        internal static bool IsOwnWindow(IntPtr window) => false;
        internal static string Label(IntPtr window) => "Synthetic field";
    }
    internal static class InputNative { internal static int GetAsyncKeyState(int code) => 0; }
    internal static class OverlayNative { internal static bool IsFullscreenForeground() => false; }
    internal static class PromptSuggestionPolicy
    {
        internal static bool PrivateMetadata(string app, string role) => false;
        internal static bool ShouldOffer(string text) => true;
    }
}

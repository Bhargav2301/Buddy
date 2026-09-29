using System.Text.Json;
using System.Windows.Automation;

namespace Buddy.Windows;

internal sealed record FieldRule(string[] Processes, string[] Titles, string[] Fields);
internal sealed record FocusedDraft(GuardedEdit Edit, string App, string FieldName);

// No background text reading. Capture is called only for an explicit Refine/dictation invocation.
internal sealed class FocusedFieldEditor(ScreenPerception perception)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly Lazy<FieldRule[]> Rules = new(() => {
        using var stream = typeof(FocusedFieldEditor).Assembly.GetManifestResourceStream("Buddy.Windows.refine-apps.json")!;
        return JsonSerializer.Deserialize<FieldRule[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    });

    internal Task<FocusedDraft> Capture(IntPtr window, CancellationToken ct) => Run(token => {
        perception.Check(window);
        if (Native.GetForegroundWindow() != window) throw new InvalidOperationException("Focus an AI prompt field and press Ctrl+Alt+R.");
        var node = AutomationElement.FocusedElement;
        var current = node.Current;
        string app = InputNative.ProcessName(window), title = Native.Label(window), name = current.Name ?? "";
        if (!Rules.Value.Any(r => r.Processes.Contains(app, StringComparer.OrdinalIgnoreCase) &&
                r.Titles.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)) &&
                r.Fields.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("This field is not in Buddy's supported AI-chat rules. Paste your prompt into Buddy to refine it.");
        var adapter = new AutomationTextField(node, window, perception);
        token.ThrowIfCancellationRequested();
        var original = adapter.Read();
        if (string.IsNullOrWhiteSpace(original) || original.Length > 20000) throw new InvalidOperationException("Enter a prompt of up to 20,000 characters first.");
        return new FocusedDraft(new GuardedEdit(adapter, original), app, name);
    }, ct);

    internal Task Apply(FocusedDraft draft, string text, CancellationToken ct) => Run(token => { draft.Edit.Apply(text, DateTimeOffset.UtcNow, token); return true; }, ct);
    internal Task Undo(FocusedDraft draft, CancellationToken ct) => Run(token => { draft.Edit.Undo(DateTimeOffset.UtcNow, token); return true; }, ct);

    private async Task<T> Run<T>(Func<CancellationToken, T> action, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var token = deadline.Token;
        try {
            await gate.WaitAsync(token);
            var worker = Task.Run(() => { try { token.ThrowIfCancellationRequested(); return action(token); } finally { gate.Release(); } }, CancellationToken.None);
            return await worker.WaitAsync(token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new InvalidOperationException("The app did not respond in time. Check the field before retrying; Copy is available.");
        }
    }

    private sealed class AutomationTextField : IVerifiedTextField
    {
        private readonly AutomationElement node;
        private readonly IntPtr window;
        private readonly ScreenPerception perception;
        private readonly string identity;
        private readonly int processId;
        internal AutomationTextField(AutomationElement node, IntPtr window, ScreenPerception perception)
        {
            this.node = node; this.window = window; this.perception = perception;
            identity = string.Join('.', node.GetRuntimeId()); processId = node.Current.ProcessId;
        }
        public string Identity => window + ":" + string.Join('.', node.GetRuntimeId());
        private ValuePattern Validate()
        {
            perception.Check(window);
            var current = node.Current;
            if (current.ProcessId != processId || string.Join('.', node.GetRuntimeId()) != identity || current.IsPassword || current.IsOffscreen || !current.IsEnabled || current.ControlType != ControlType.Edit)
                throw new InvalidOperationException("The prompt field is no longer available or safe to edit.");
            // Runtime IDs alone are not enough: a tab or window can have been replaced.
            var root = AutomationElement.FromHandle(window); var ancestor = node; bool belongs = false;
            for (int depth = 0; ancestor is not null && depth < 40; depth++) {
                if (Automation.Compare(ancestor, root)) { belongs = true; break; }
                ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
            }
            if (!belongs || !node.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) || pattern is not ValuePattern value || value.Current.IsReadOnly)
                throw new InvalidOperationException("This field cannot be safely replaced. Use Copy instead.");
            return value;
        }
        public string Read() => Validate().Current.Value;
        public void Write(string expected, string text, CancellationToken ct)
        {
            var value = Validate();
            if (!string.Equals(value.Current.Value, expected, StringComparison.Ordinal)) throw new InvalidOperationException("The field changed. Copy the text instead.");
            ct.ThrowIfCancellationRequested();
            value.SetValue(text); // No keystroke, Enter, submit, clipboard or focus action.
        }
    }
}

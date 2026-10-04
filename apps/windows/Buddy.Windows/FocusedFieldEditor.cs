using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows;

namespace Buddy.Windows;

internal sealed record FieldRule(string[] Processes, string[] Titles, string[] Fields);
internal sealed record FieldAnchor(IntPtr Window, string Identity, Rect Bounds, bool ExplicitInvocation = false);
internal sealed record FocusedDraft(GuardedEdit Edit, string App, string FieldName, FieldAnchor Anchor,
    DictationInsertion? Insertion = null, Action? VerifySelection = null, Action? VerifyFocus = null);

// Text capture requires an explicit invocation, or opt-in bounded focused-field events.
internal sealed class FocusedFieldEditor(ScreenPerception perception,Func<AutomationElement,IntPtr,bool>? fixtureEligibility=null)
{
    private bool Eligible(AutomationElement node,IntPtr window,bool explicitInvocation=false)=>fixtureEligibility?.Invoke(node,window)??(explicitInvocation ? SafeRole(node) : Supported(node,window));
    private static bool SafeRole(AutomationElement node) { var c=node.Current; return !c.IsPassword && c.IsEnabled && c.HasKeyboardFocus && !c.IsOffscreen && (c.ControlType==ControlType.Edit || c.ControlType==ControlType.Document); }
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly Lazy<FieldRule[]> Rules = new(() => {
        using var stream = typeof(FocusedFieldEditor).Assembly.GetManifestResourceStream("Buddy.Windows.refine-apps.json")!;
        return JsonSerializer.Deserialize<FieldRule[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    });

    // Eligibility checks only role/name/bounds/pattern support; never Value or text ranges.
    internal Task<FieldAnchor?> Probe(IntPtr window, CancellationToken ct, bool explicitInvocation = false) => Run(token => {
        perception.Check(window, agent:true);
        if (Native.GetForegroundWindow() != window) return null;
        var node = AutomationElement.FocusedElement;
        if (!Eligible(node, window,explicitInvocation)||Buddy.Server.PromptSuggestionPolicy.PrivateMetadata(Native.Label(window),node.Current.Name??"")) return null;
        var adapter = new AutomationTextField(node, window, perception);
        adapter.Validate(); token.ThrowIfCancellationRequested();
        return Anchor(node, window, adapter.Identity) with { ExplicitInvocation=explicitInvocation };
    }, ct);

    internal static bool Supported(AutomationElement node, IntPtr window)
    {
        var c = node.Current;
        return !c.IsPassword && c.IsEnabled && c.HasKeyboardFocus && !c.IsOffscreen && c.ControlType == ControlType.Edit &&
            Rules.Value.Any(r => r.Processes.Contains(InputNative.ProcessName(window), StringComparer.OrdinalIgnoreCase) &&
                r.Titles.Any(t => Native.Label(window).Contains(t, StringComparison.OrdinalIgnoreCase)) &&
                r.Fields.Any(f => (c.Name ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)));
    }
    private static FieldAnchor Anchor(AutomationElement node, IntPtr window, string identity)
    {
        var bounds = node.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || !double.IsFinite(bounds.X + bounds.Y + bounds.Width + bounds.Height) ||
            !WindowCapture.Bounds(window).Contains(bounds))
            throw new InvalidOperationException("The field is not visibly inside the selected app.");
        return new(window, identity, bounds);
    }

    internal Task<FocusedDraft> Capture(IntPtr window, CancellationToken ct, bool dictation = false, string? expectedIdentity = null, bool strictFocus = false, bool explicitInvocation = false) => Run(token => {
        perception.Check(window, agent:true);
        if (Native.GetForegroundWindow() != window) throw new InvalidOperationException("Focus the original editable field and press Ctrl+Alt+R.");
        var node = AutomationElement.FocusedElement;
        var current = node.Current;
        string app = InputNative.ProcessName(window), name = current.Name ?? "";
        if (!Eligible(node, window,explicitInvocation || dictation) || Buddy.Server.PromptSuggestionPolicy.PrivateMetadata(Native.Label(window),name))
            throw new InvalidOperationException("Select the original editable text field first. Private, hidden and non-text controls cannot be refined.");
        var adapter = new AutomationTextField(node, window, perception);
        if (expectedIdentity is not null && adapter.Identity != expectedIdentity) throw new InvalidOperationException("The focused field changed. Invoke Buddy again in the intended field.");
        token.ThrowIfCancellationRequested();
        var original = adapter.Read();
        if ((!dictation && string.IsNullOrWhiteSpace(original)) || original.Length > 20000) throw new InvalidOperationException("Use a field of up to 20,000 characters.");
        var anchor = Anchor(node, window, adapter.Identity) with { ExplicitInvocation=explicitInvocation || dictation };
        if (Native.GetForegroundWindow() != window || !node.Current.HasKeyboardFocus) throw new InvalidOperationException("Focus changed while reading the field. Invoke Buddy again.");
        var originalTitle=Native.Label(window);
        if (!dictation) return new FocusedDraft(new GuardedEdit(adapter, original), app, name, anchor,VerifyFocus:strictFocus?()=>{
            if(Native.GetForegroundWindow()!=window||!node.Current.HasKeyboardFocus||Native.Label(window)!=originalTitle||!Eligible(node,window,explicitInvocation)||Buddy.Server.PromptSuggestionPolicy.PrivateMetadata(originalTitle,node.Current.Name??""))
                throw new InvalidOperationException("The original prompt field lost focus or changed context. Dismiss this suggestion and continue in your intended field.");
        }:null);
        perception.Check(window, agent: true);
        if (!node.TryGetCurrentPattern(TextPattern.Pattern, out var raw) || raw is not TextPattern pattern)
            throw new InvalidOperationException("The app does not expose its caret safely. Use Buddy's draft or voice instead.");
        var range = Selection(pattern);
        var insertion = Insertion(pattern, range, original);
        // Degenerate UIA ranges have no rectangles. The enclosing character gives a
        // verified nearby anchor; empty or invisible text refuses caret placement.
        // https://learn.microsoft.com/dotnet/api/system.windows.automation.text.textpatternrange.getboundingrectangles
        var visible = range.Clone(); visible.ExpandToEnclosingUnit(TextUnit.Character);
        var boxes = visible.GetBoundingRectangles();
        if (boxes.Length == 0 || boxes[0].IsEmpty || !anchor.Bounds.Contains(boxes[0]))
            throw new InvalidOperationException("The caret is not visibly exposed. Use Buddy's draft or voice instead.");
        var rect = boxes[0];
        anchor = anchor with { Bounds = new Rect(insertion.Start == original.Length ? rect.Right : rect.Left, rect.Top, 1, rect.Height) };
        token.ThrowIfCancellationRequested();
        if (Native.GetForegroundWindow() != window || !node.Current.HasKeyboardFocus) throw new InvalidOperationException("Focus changed while reading the caret.");
        return new FocusedDraft(new GuardedEdit(adapter, original), app, name, anchor, insertion, () => {
            var foreground = Native.GetForegroundWindow();
            if (foreground != window && !Native.IsOwnWindow(foreground)) throw new InvalidOperationException("Focus changed. Copy the transcript instead.");
            if (Insertion(pattern, Selection(pattern), original) != insertion) throw new InvalidOperationException("The caret or selection changed. Copy the transcript instead.");
        });
    }, ct);

    private static TextPatternRange Selection(TextPattern pattern)
    {
        var selection = pattern.GetSelection();
        if (selection.Length != 1) throw new InvalidOperationException("Select a single insertion point first.");
        return selection[0];
    }
    private static DictationInsertion Insertion(TextPattern pattern, TextPatternRange range, string original)
    {
        var prefix = pattern.DocumentRange.Clone(); prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
        return DictationInsertion.Verify(original, pattern.DocumentRange.GetText(20001), prefix.GetText(20001), range.GetText(20001));
    }
    internal Task Apply(FocusedDraft draft, string text, CancellationToken ct) => Run(token => { draft.VerifyFocus?.Invoke(); draft.VerifySelection?.Invoke(); token.ThrowIfCancellationRequested(); draft.Edit.Apply(text, DateTimeOffset.UtcNow, token); return true; }, ct);
    internal Task Undo(FocusedDraft draft, CancellationToken ct) => Run(token => { draft.VerifyFocus?.Invoke(); draft.Edit.Undo(DateTimeOffset.UtcNow, token); return true; }, ct);
    internal Task<bool> Matches(FocusedDraft draft,string expected,CancellationToken ct)=>Run(token=>{draft.VerifyFocus?.Invoke();token.ThrowIfCancellationRequested();return draft.Edit.Matches(expected);},ct);
    internal Task<IDisposable?> Watch(FieldAnchor anchor,Action changed,CancellationToken ct)=>Run<IDisposable?>(token=>{
        perception.Check(anchor.Window, agent:true);
        if(Native.GetForegroundWindow()!=anchor.Window)return null;
        var node=AutomationElement.FocusedElement;
        if(!Eligible(node,anchor.Window,anchor.ExplicitInvocation)||Buddy.Server.PromptSuggestionPolicy.PrivateMetadata(Native.Label(anchor.Window),node.Current.Name??"")||new AutomationTextField(node,anchor.Window,perception).Identity!=anchor.Identity)return null;
        token.ThrowIfCancellationRequested();return new FieldEvents(node,changed);
    },ct);
    private sealed class FieldEvents:IDisposable
    {
        private readonly AutomationElement node;private readonly AutomationEventHandler handler;private readonly AutomationPropertyChangedEventHandler valueHandler;private readonly bool textEvents;
        internal FieldEvents(AutomationElement node,Action changed){
            this.node=node;handler=(_,_)=>changed();valueHandler=(_,_)=>changed();
            textEvents=node.TryGetCurrentPattern(TextPattern.Pattern,out _);
            Automation.AddAutomationPropertyChangedEventHandler(node,TreeScope.Element,valueHandler,ValuePattern.ValueProperty);
            try{if(textEvents)Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent,node,TreeScope.Element,handler);}
            catch{Automation.RemoveAutomationPropertyChangedEventHandler(node,valueHandler);throw;}
        }
        public void Dispose(){try{if(textEvents)Automation.RemoveAutomationEventHandler(TextPattern.TextChangedEvent,node,handler);}catch{}try{Automation.RemoveAutomationPropertyChangedEventHandler(node,valueHandler);}catch{}}
    }

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
        internal ValuePattern Validate()
        {
            perception.Check(window, agent:true);
            var current = node.Current;
            if (current.ProcessId != processId || string.Join('.', node.GetRuntimeId()) != identity || current.IsPassword || current.IsOffscreen || !current.IsEnabled || (current.ControlType != ControlType.Edit && current.ControlType != ControlType.Document) || Buddy.Server.PromptSuggestionPolicy.PrivateMetadata(Native.Label(window),current.Name??""))
                throw new InvalidOperationException("The prompt field is no longer available or safe to edit.");
            // Runtime IDs alone are not enough: a tab or window can have been replaced.
            var root = AutomationElement.FromHandle(window); var ancestor = node; bool belongs = false;
            for (int depth = 0; ancestor is not null && depth < 40; depth++) {
                if (Automation.Compare(ancestor, root)) { belongs = true; break; }
                ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
            }
            if (!belongs || !node.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) || pattern is not ValuePattern value || value.Current.IsReadOnly)
                throw new InvalidOperationException("This editor does not expose a writable Windows accessibility value. Buddy cannot verify a replacement here; use Buddy's draft to review and copy text manually. No keys or clipboard paste will be sent.");
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

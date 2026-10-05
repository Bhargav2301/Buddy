using Buddy.Server;
using Buddy.Windows;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class Program
{
    private static int checks;
    private static int sourcesInitialized;
    [STAThread] private static int Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            Defaults(); GlobalStop(); Handoffs(); ProviderReplacement(); ProviderWindows(); SourceRefusals(); LocalAgents();
            Check(sourcesInitialized == 0, "All owned WPF windows remained unshown with no presentation source");
            Console.WriteLine($"ALL {checks} SHARED SURFACE CHECKS PASSED; unshown WPF and injected callbacks only; no desktop input, accounts, pipes, model or network calls.");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { application.Shutdown(); }
    }
    private static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
    private static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new Exception("Missing owner field " + name);
    private static T? Read<T>(MainWindow window, string name) => (T?)Field(name).GetValue(window);
    private static void Set(MainWindow window, string name, object? value) => Field(name).SetValue(window, value);
    private static object? Call(MainWindow window, string name, params object[] args)
    {
        try { return typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
    }
    private static void Watch(Window window) => window.SourceInitialized += (_, _) => { sourcesInitialized++; throw new Exception("This headless fixture must never initialize a window source"); };
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject item) foreach (var descendant in Tree(item)) yield return descendant;
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Done<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
    private static void Refused(Task task, string label)
    {
        try { task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { Check(true, label); return; }
        throw new Exception("FAIL: " + label);
    }
    private static void Defaults()
    {
        using var owner = new Owner(); var window = owner.Window;
        Check(Read<BuddyHost>(window, "host") is null && Read<ProviderRoutingSession>(window, "providerSession") is null, "Unshown startService=false initializes neither local host nor a cloud session");
        Check(Read<object>(window, "localAgentBroker") is null && Read<object>(window, "localAgentPipe") is null, "Startup does not create an agent broker or pipe");
        Check(Read<ShortcutRegistration>(window, "shortcut") is null && Read<ShortcutRegistration>(window, "voiceShortcut") is null, "Unshown startup registers no chat or voice chord");
        var preferences = Read<DesktopPreferences>(window, "desktop")!;
        Check(preferences.NeuralSpeakerId == 85 && preferences.NeuralPreset == "F3" && preferences.ProtectScreenshots && preferences.HeadphonesOnly, "Shared-surface startup preserves supplied speaker and privacy preferences");
        Check(owner.Saves == 0 && !Read<DispatcherTimer>(window, "localAgentRefresh")!.IsEnabled, "Constructing the shared surfaces writes no preferences and starts no agent polling");
        Check(PresentationSource.FromVisual(window) is null && !window.IsVisible, "MainWindow remains unshown without a presentation source");
    }
    private static void GlobalStop()
    {
        using var owner = new Owner(); var window = owner.Window;
        var delayed = new TaskCompletionSource<NotchChatReply>(TaskCreationOptions.RunContinuationsAsynchronously); CancellationToken received = default; int sends = 0;
        using var chat = new NotchChatSession(() => [new("owned", "Owned local model")], (_, ct) => { sends++; received = ct; return delayed.Task; });
        Set(window, "notchChat", chat); chat.SetDraft("Owned pending draft"); var pending = chat.Send();
        Check(chat.Snapshot.Busy && sends == 1, "Notch fixture has one actual session request in flight");
        using var request = new CancellationTokenSource(); using var cloud = new CancellationTokenSource(); using var routine = new CancellationTokenSource();
        Set(window, "request", request); Set(window, "cloudRequest", cloud); Set(window, "routineRequest", routine); Set(window, "desktopActivity", "notch"); Set(window, "voiceHeld", true);
        using var transport = new NeverHttp(); using var provider = TestingProvider(transport); var review = provider.PrepareQuestion("Owned reviewed text"); Set(window, "providerSession", provider);
        var reviewWindow = new ProviderQuestionReviewWindow(review); Watch(reviewWindow); bool reviewClosed = false;
        reviewWindow.Closed += (_, _) => reviewClosed = true; Set(window, "cloudReview", reviewWindow);
        Call(window, "Cancel");
        Check(received.IsCancellationRequested && !chat.Snapshot.Busy, "Actual global Stop cancels and releases the active notch session");
        Check(request.IsCancellationRequested && cloud.IsCancellationRequested && routine.IsCancellationRequested, "Actual global Stop cancels Home, cloud and routine request owners");
        Check(Read<string>(window, "desktopActivity") == "" && !Read<bool>(window, "voiceHeld"), "Global Stop clears surface ownership and pending held-voice intent");
        Check(reviewClosed && Read<ProviderQuestionReviewWindow>(window, "cloudReview") is null, "Global Stop closes and releases an unshown pending cloud review window");
        Check(!Done(pending) && chat.Snapshot.Draft == "Owned pending draft", "Stopped notch request retains its exact draft");
        delayed.SetResult(new("Late ignored answer"));
        Check(chat.Snapshot.Messages.Count == 0 && chat.Snapshot.Status.StartsWith("Stopped.", StringComparison.Ordinal), "Late notch callback cannot append an answer or overwrite Stop status");
        Refused(provider.CompleteReviewedAsync(review), "Global Stop invalidates an outstanding cloud review before transmission");
        Check(transport.Calls == 0 && provider.Status.Configured, "Global Stop sends nothing and preserves the separately configured provider session");
        Set(window, "providerSession", null); Set(window, "cloudRequest", null); Set(window, "request", null); Set(window, "routineRequest", null); Set(window, "notchChat", null);
    }
    private static void Handoffs()
    {
        foreach (string destination in new[] { "home", "cloud-text", "notch", "routine" }) {
            using var owner = new Owner(); var window = owner.Window;
            var delayed = new TaskCompletionSource<NotchChatReply>(TaskCreationOptions.RunContinuationsAsynchronously); CancellationToken received = default;
            using var chat = new NotchChatSession(() => [new("owned", "Owned")], (_, ct) => { received = ct; return delayed.Task; });
            chat.SetDraft("Owned handoff draft"); var pending = chat.Send(); Set(window, "notchChat", chat);
            using var home = new CancellationTokenSource(); using var cloud = new CancellationTokenSource(); using var routine = new CancellationTokenSource();
            Set(window, "request", home); Set(window, "cloudRequest", cloud); Set(window, "routineRequest", routine);
            Call(window, "PrepareDesktopActivity", destination);
            Check(Read<string>(window, "desktopActivity") == destination, "Handoff records its actual owning surface: " + destination);
            Check(home.IsCancellationRequested == (destination != "home") && cloud.IsCancellationRequested == (destination != "cloud-text") && routine.IsCancellationRequested == (destination != "routine"), "Handoff cancels competing owners while retaining its own request: " + destination);
            Check(received.IsCancellationRequested == (destination != "notch") && chat.Snapshot.Busy == (destination == "notch"), "Handoff preserves only the owning notch request: " + destination);
            chat.Stop(); Check(!Done(pending), "Handoff fixture finishes without model callbacks: " + destination); delayed.SetResult(new("Late handoff output"));
            Check(chat.Snapshot.Messages.Count == 0, "Late callback after handoff never enters notch history: " + destination);
            Set(window, "notchChat", null); Set(window, "request", null); Set(window, "cloudRequest", null); Set(window, "routineRequest", null);
        }
    }
    private static void ProviderReplacement()
    {
        using var owner = new Owner(); var window = owner.Window;
        using var transport = new NeverHttp(); using var previous = TestingProvider(transport); var review = previous.PrepareQuestion("Owned old session text");
        using var replacementTransport = new NeverHttp(); using var replacement = TestingProvider(replacementTransport);
        using var pending = new CancellationTokenSource(); Set(window, "cloudRequest", pending); Set(window, "providerSession", previous);
        Call(window, "ReplaceProvider", replacement);
        Check(pending.IsCancellationRequested && !previous.Status.Configured && ReferenceEquals(Read<ProviderRoutingSession>(window, "providerSession"), replacement), "Replacing provider cancels current ownership and disconnects the old session");
        Refused(previous.CompleteReviewedAsync(review), "Old session review cannot survive provider replacement");
        Check(transport.Calls == 0 && replacementTransport.Calls == 0, "Provider replacement performs no connection test or transmission");
        Call(window, "ReplaceProvider", (object)null!);
        Check(Read<ProviderRoutingSession>(window, "providerSession") is null && !replacement.Status.Configured, "Disconnect clears current provider ownership");
        Set(window, "cloudRequest", null);
    }
    private static void ProviderWindows()
    {
        int replacements = 0;
        var setup = new ProviderSetupWindow(() => null, _ => replacements++); Watch(setup);
        try {
            var controls = Tree(setup).ToArray(); var permissions = controls.OfType<CheckBox>().ToArray();
            Check(permissions.Length == 2 && permissions.All(x => x.IsChecked == false), "Cloud setup starts with text sharing and possible charges both unselected");
            Check(controls.OfType<PasswordBox>().Single().SecurePassword.Length == 0 && controls.OfType<TextBox>().Single().Text.Length == 0, "Cloud setup loads no key or model by default");
            Click(controls.OfType<Button>().Single(x => Equals(x.Content, "Configure reviewed text session")));
            Check(replacements == 0 && Tree(setup).OfType<TextBlock>().Any(x => x.Text.StartsWith("Setup was not completed.", StringComparison.Ordinal)), "Unconfigured setup click refuses before replacing a session or connecting");
            Check(PresentationSource.FromVisual(setup) is null && !setup.IsVisible, "Provider setup validation works without showing a window");
        } finally { setup.Close(); }
        using var transport = new NeverHttp(); using var provider = TestingProvider(transport); var review = provider.PrepareQuestion("Owned exact text, with punctuation.");
        var dialog = new ProviderQuestionReviewWindow(review); Watch(dialog);
        try {
            var body = Tree(dialog).OfType<TextBox>().Single();
            Check(body.IsReadOnly && body.Text == review.Text, "Cloud review displays the exact immutable question as read-only text");
            Check(dialog.DialogResult is null && transport.Calls == 0, "Constructing cloud review does not approve or transmit it");
            Check(Tree(dialog).OfType<TextBlock>().Any(x => x.Text == ProviderProtocols.TextPolicy), "Cloud review exposes the exact fixed instruction included in a later send");
        } finally { dialog.Close(); }
    }
    private static void SourceRefusals()
    {
        using var owner = new Owner(); var window = owner.Window; var journal = Read<LocalTaskJournal>(window, "localTasks")!;
        var unknown = new LocalTaskToken(Guid.NewGuid(), "home", 1);
        Check(!(bool)Call(window, "OpenLocalTaskSource", unknown)!, "Unknown task token cannot reopen a shared surface");
        var old = journal.Begin("home", "Owned first"); Set(window, "homeTask", old); Set(window, "homeTaskConversation", "old"); Set(window, "currentId", "new");
        Check(!(bool)Call(window, "OpenLocalTaskSource", old)!, "Home card from a different conversation is refused before Show/Activate");
        var newer = journal.Begin("home", "Owned replacement"); Set(window, "homeTask", newer); Set(window, "homeTaskConversation", "new");
        Check(!(bool)Call(window, "OpenLocalTaskSource", old)!, "Replaced Home task cannot reopen the current request");
        var notch = journal.Begin("notch", "Owned old bar chat"); Set(window, "notchTask", notch); Set(window, "notchSessionId", "older-session");
        Check(!(bool)Call(window, "OpenLocalTaskSource", notch)!, "Notch card refuses when its actual island surface is unavailable");
        var cloud = journal.Begin("cloud-text", "Owned cloud task");
        Check(!(bool)Call(window, "OpenLocalTaskSource", cloud)!, "Cloud card refuses when its originating surface is unavailable");
        var previousCloud = new Window(); var currentCloud = new Window(); Watch(previousCloud); Watch(currentCloud);
        try {
            Set(window, "cloudChat", currentCloud); Set(window, "cloudTask", cloud); Set(window, "cloudTaskWindow", previousCloud);
            Check(!(bool)Call(window, "OpenLocalTaskSource", cloud)!, "Old cloud task cannot activate a newly opened cloud draft window");
            var newerCloud = journal.Begin("cloud-text", "Owned newer cloud task"); Set(window, "cloudTask", newerCloud); Set(window, "cloudTaskWindow", currentCloud);
            Check(!(bool)Call(window, "OpenLocalTaskSource", cloud)!, "Replaced cloud task is refused even when the current draft window matches its new owner");
        } finally { Set(window, "cloudChat", null); Set(window, "cloudTask", null); Set(window, "cloudTaskWindow", null); previousCloud.Close(); currentCloud.Close(); }
        Set(window, "shuttingDown", true);
        Check(!(bool)Call(window, "OpenLocalTaskSource", newer)!, "Shutdown refuses task navigation before any window operation");
    }
    private static void LocalAgents()
    {
        using var owner = new Owner(); var window = owner.Window; var journal = Read<LocalTaskJournal>(window, "localTasks")!;
        var broker = new LocalAgentBroker(); Set(window, "localAgentBroker", broker);
        var first = broker.Pair("Owned fixture A", true); var second = broker.Pair("Owned fixture B", true);
        broker.Receive(first.Token, new(first.SessionId, 1, "started", "owned-task-a", "Private fixture detail A"));
        broker.Receive(second.Token, new(second.SessionId, 1, "started", "owned-task-b", "Private fixture detail B"));
        Call(window, "RefreshLocalAgentTasks");
        var initial = journal.Snapshot.Tasks;
        Check(initial.Count == 2 && initial.All(x => x.Phase == LocalTaskPhase.Running), "Two paired in-memory sessions retain independent running task cards");
        Check(initial.Select(x => x.Source).Distinct().Count() == 2 && initial.All(x => x.Source.StartsWith("agent-", StringComparison.Ordinal)), "Per-session journal sources prevent one paired client from canceling another");
        Check(initial.All(x => !x.Detail.Contains("Private fixture", StringComparison.Ordinal) && !x.Title.Contains("Owned fixture", StringComparison.Ordinal)), "Generic journal labels do not copy paired-client payloads or labels");
        string digest = LocalAgentBroker.RequestDigest(first.SessionId, "owned-task-a", "owned-question", "question", "Private fixture question");
        broker.Receive(first.Token, new(first.SessionId, 2, "question", "owned-task-a", "Private fixture question", "owned-question", digest));
        Call(window, "RefreshLocalAgentTasks");
        Check(journal.Snapshot.Tasks.Count(x => x.Phase == LocalTaskPhase.WaitingForReview) == 1 && journal.Snapshot.Tasks.Count(x => x.Phase == LocalTaskPhase.Running) == 1,
            "Question review on one paired session leaves the other running");
        broker.Receive(second.Token, new(second.SessionId, 2, "completed", "owned-task-b", "Private unverified completion")); Call(window, "RefreshLocalAgentTasks");
        var complete = journal.Snapshot.Tasks.Single(x => x.Phase == LocalTaskPhase.Completed);
        Check(complete.Detail.Contains("reported completion", StringComparison.Ordinal) && complete.Detail.Contains("not verified", StringComparison.Ordinal), "Paired-client completion is labeled reported and unverified");
        Check(journal.Snapshot.Tasks.Count(x => x.Phase == LocalTaskPhase.WaitingForReview) == 1, "Another client's completion does not cancel the outstanding review");
        broker.Disconnect(first.SessionId); Call(window, "RefreshLocalAgentTasks");
        Check(journal.Snapshot.Tasks.Count(x => x.Phase == LocalTaskPhase.Cancelled) == 1 && journal.Snapshot.Tasks.Any(x => x.Token == complete.Token && x.Phase == LocalTaskPhase.Completed), "Disconnect cancels only its session's task display");
        int before = journal.Snapshot.Tasks.Count; Call(window, "RefreshLocalAgentTasks");
        Check(journal.Snapshot.Tasks.Count == before, "Refreshing unchanged paired-session state creates no duplicate cards");
        Check(Read<object>(window, "localAgentPipe") is null && !Read<DispatcherTimer>(window, "localAgentRefresh")!.IsEnabled, "Direct memory-only refresh opens no pipe and starts no polling timer");
        broker.DisconnectAll(); Set(window, "localAgentBroker", null);
    }
    private delegate ProviderRoutingSession TestingFactory(ProviderConsent consent, ReadOnlySpan<char> credential, HttpMessageHandler handler, Func<DateTimeOffset>? clock, Func<long>? timestamp);
    private static ProviderRoutingSession TestingProvider(NeverHttp handler)
    {
        var factory = typeof(ProviderRoutingSession).GetMethod("CreateForTesting", BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<TestingFactory>();
        return factory(new("openai", "owned-test-model", true, true), "owned-fake-key".AsSpan(), handler, null, null);
    }
    private sealed class NeverHttp : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new Exception("A shared-surface fixture unexpectedly reached HTTP"); }
    }
    private sealed class Owner : IDisposable
    {
        internal readonly MainWindow Window;
        internal int Saves;
        internal Owner()
        {
            Window = new(false, new DesktopPreferences { NeuralSpeakerId = 85, NeuralPreset = "F3", VoiceEngine = "piper", HeadphonesOnly = true, ProtectScreenshots = true, HoldToTalk = false, RegionSelectionEnabled = false }, _ => Saves++);
            Watch(Window);
        }
        public void Dispose()
        {
            Set(Window, "shuttingDown", true);
            Read<NotchChatSession>(Window, "notchChat")?.Dispose(); Read<ProviderRoutingSession>(Window, "providerSession")?.Dispose();
            foreach (string field in new[] { "refresh", "foreground", "localAgentRefresh", "preferenceSaveTimer" }) Read<DispatcherTimer>(Window, field)?.Stop();
            (Field("tray").GetValue(Window) as IDisposable)?.Dispose(); Window.Close();
        }
    }
}

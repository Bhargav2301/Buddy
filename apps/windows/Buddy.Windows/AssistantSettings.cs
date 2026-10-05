using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private PracticeWindow? practice;
    private TaskCenter? tasks;
    private void OpenTasks()
    {
        try { tasks?.Open(windowSelection.RequireCurrent().App); }
        catch (InvalidOperationException ex) { status.Text = ex.Message; }
    }
    private void ObserveForegroundTarget()
    {
        ObserveTargetWindow(Native.GetForegroundWindow());
    }
    private void ObserveTargetWindow(IntPtr current)
    {
        windowSelection.Observe(current);
        if (current == IntPtr.Zero || (Native.IsOwnWindow(current) && current != ScreenPerception.PracticeHandle) || !Native.IsSelectableWindow(current)) return;
        try { previousWindow = windowSelection.RequireCurrent().Window; }
        catch (InvalidOperationException) { previousWindow = IntPtr.Zero; }
    }
    private bool keepHomeOpen = true;
    internal void ConfigureLaunch(LaunchDestination destination) => keepHomeOpen = destination != LaunchDestination.Background;
    internal void OpenFromLaunch(LaunchDestination destination)
    {
        if (destination == LaunchDestination.Settings) OpenSettings();
        else if (destination == LaunchDestination.Home) Summon();
        Diagnostics.Write("Desktop navigation: " + destination);
    }
    private void OpenSettings()
    {
        Summon(); NavigateHome("Settings");
    }
    private void StartWorkflow(string mode, string text)
    {
        if (mode == "agent" && runRoutine is not null && desktop.AgentEnabled && RoutineAppOpen.TryGetAlias(text, out var routineAlias)) {
            _ = OpenRoutineApp(text, routineAlias); return;
        }
        ObserveForegroundTarget();
        PrepareDesktopActivity(mode=="knowledge"?"jobs":"assistant"); quick?.Dismiss(); voiceOverlay?.Dismiss();
        if(mode=="knowledge"){
            try { if(tasks is not null)_=tasks.Search(windowSelection.RequireCurrent().App,AssistantIntent.KnowledgeQuery(text)); }
            catch(InvalidOperationException ex) { status.Text=ex.Message; }
            return;
        }
        text=AssistantIntent.ActionQuery(text);
        if (assistant is not null) _ = assistant.Open(mode, text);
    }
    private void ConfigurePtt()
    {
        voiceHeld = false; voiceOverlay?.Cancel(); ptt?.Dispose(); ptt = null;
        // Keep the registered voice chord reserved while the optional hook handles holds.
        // Never install a hook if Windows rejected that chord.
        var choice = voiceShortcut?.Active;
        if (desktop.HoldToTalk && choice is not null) {
            try {
                ptt = new PushToTalkHook(choice.Modifiers, () => Dispatcher.BeginInvoke(new Action(() => OpenQuick(true))),
                    () => { voiceHeld=true;OpenQuick(true,true); },
                    () => Dispatcher.BeginInvoke(new Action(() => {voiceHeld=false;voiceOverlay?.Finish();})));
            } catch (Exception ex) { status.Text = ex.Message; }
        }
        UpdateShortcutHint();
    }
    private void AssistantSettings() => OpenSettingsSection("Guide & Agent");
    private void RouteCompanionAction(string action)
    {
        switch (action) {
            case "Type": case "Talk": OpenQuick(false); break;
            case "Voice": OpenQuick(true); break;
            case "Guide": StartWorkflow("guide", ""); break;
            case "Refine": _ = RefineFocusedField(); break;
            case "Select area": BeginRegionSelection(); break;
            case "Agent": StartWorkflow("agent", ""); break;
            case "Dictate": _ = RefineFocusedField(dictation: true); break;
            case "Home": Summon(); break;
            case "Settings": OpenSettings(); break;
            case "Stop": Cancel(); desktopActivity = ""; island?.Refresh(); break;
        }
    }
    private IslandActivity ReadIslandActivity()
    {
        if (desktopActivity == "routine")
            return new(routineTask, routineStatus, companionState.Current, islandBrainStatus);
        if (desktopActivity == "assistant" && assistant?.IsActive == true)
            return new(string.IsNullOrWhiteSpace(assistant.CurrentTask) ? "Describe what you need" : assistant.CurrentTask, assistant.CurrentStatus, companionState.Current, islandBrainStatus);
        string task = desktopActivity switch { "voice" => "Voice conversation", "talk" or "home" => "Typed conversation", "refine" or "field" => "Refine source field", "region" => "Selected-area guidance", "dictation" => "Source-field dictation", "jobs" => "Local task", _ => "Ready when you are" };
        string state = companionState.Current switch { CompanionMood.Listening => "Listening on this PC", CompanionMood.Speaking => "Speaking", CompanionMood.Thinking => "Thinking", CompanionMood.Looking => "Reading the selected context", CompanionMood.Pointing => "Showing a verified target", CompanionMood.AgentWorking => "Working on an approved step", CompanionMood.Researching => "Researching", CompanionMood.Unsure => "Review needed", CompanionMood.Error => "Needs attention", _ => "Ready - choose Type, Voice, Guide or Refine" };
        return new(task, state, companionState.Current, islandBrainStatus);
    }
    private void StoreIslandMode(string mode)
    {
        if (!FlushPendingPreferences()) throw new InvalidOperationException("Finish saving Settings before changing the bar mode.");
        SavePreferences(desktop with { IslandMode = mode });
    }
    private async Task ShowAudit()
    {
        if (host is null) return;
        var entries = await host.Service.Store.Read(s => s.Audit.TakeLast(20).Reverse().ToList());
        var p = new StackPanel(); p.Children.Add(Text("Recent activity", 24));
        foreach (var e in entries) p.Children.Add(Text($"{e.At:t} · {e.Kind} · {e.Target}\n{e.Result}", 13));
        if (entries.Count == 0) p.Children.Add(Text("No recorded captures or actions."));
        Dialog("Buddy · Activity", p);
    }
    private void ShowPractice()
    {
        if (practice is null || !practice.IsVisible) {
            practice = new PracticeWindow(); practice.SourceInitialized += (_,_) => { previousWindow = new WindowInteropHelper(practice).Handle; };
            practice.Activated += (_,_) => ObserveForegroundTarget();
            practice.Show();
        } else practice.Activate();
    }
}

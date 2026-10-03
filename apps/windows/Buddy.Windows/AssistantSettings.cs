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
    private void OpenTasks() => tasks?.Open(InputNative.ProcessName(previousWindow));
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
        var active = Native.GetForegroundWindow(); if (active != IntPtr.Zero && !Native.IsOwnWindow(active)) previousWindow = active;
        PrepareDesktopActivity(mode=="knowledge"?"jobs":"assistant"); quick?.Dismiss(); voiceOverlay?.Dismiss();
        if(mode=="knowledge"){if(tasks is not null)_=tasks.Search(InputNative.ProcessName(previousWindow),AssistantIntent.KnowledgeQuery(text));return;}
        text=AssistantIntent.ActionQuery(text);
        if (assistant is not null) _ = assistant.Open(mode, text);
    }
    private void ConfigurePtt()
    {
        voiceOverlay?.Cancel(); ptt?.Dispose(); ptt = null;
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
            practice.Show();
        } else practice.Activate();
    }
}

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
        PrepareDesktopActivity("assistant"); quick?.Dismiss(); voiceOverlay?.Dismiss();
        if (assistant is not null) _ = assistant.Open(mode, text);
    }
    private void ConfigurePtt()
    {
        voiceOverlay?.Cancel(); ptt?.Dispose(); ptt = null;
        var choice = ShortcutChoice.Choices.FirstOrDefault(c => c.Label == desktop.Shortcut) ?? ShortcutChoice.Choices[0];
        if (desktop.HoldToTalk) {
            try {
                ptt = new PushToTalkHook(choice.Modifiers, () => Dispatcher.BeginInvoke(new Action(() => OpenQuick(false))),
                    () => {
                        var active = Native.GetForegroundWindow(); if (active != IntPtr.Zero && !Native.IsOwnWindow(active)) previousWindow = active;
                        PrepareDesktopActivity("voice"); quick?.Dismiss(); voiceOverlay?.Open(true);
                    }, () => Dispatcher.BeginInvoke(new Action(() => voiceOverlay?.Finish())));
                shortcut?.Dispose();
            } catch (Exception ex) { status.Text = ex.Message; shortcut?.TrySet(choice); }
        } else shortcut?.TrySet(choice);
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

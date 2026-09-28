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
    private void StartWorkflow(string mode, string text)
    {
        var active = Native.GetForegroundWindow(); if (active != IntPtr.Zero && !Native.IsOwnWindow(active)) previousWindow = active;
        quick?.Dismiss(); voiceOverlay?.Dismiss();
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
                        quick?.Dismiss(); tts?.SpeakAsyncCancelAll(); voiceOverlay?.Open(true);
                    }, () => Dispatcher.BeginInvoke(new Action(() => voiceOverlay?.Finish())));
                shortcut?.Dispose();
            } catch (Exception ex) { status.Text = ex.Message; shortcut?.TrySet(choice); }
        } else shortcut?.TrySet(choice);
        UpdateShortcutHint();
    }
    private void AssistantSettings()
    {
        var p = new StackPanel(); p.Children.Add(Text("Assistant & privacy", 25));
        var web = new CheckBox { Content = "Allow internet research", Foreground = Ink, IsChecked = desktop.AllowWebResearch, Margin = new(0,15,0,8) }; p.Children.Add(web);
        p.Children.Add(Text("Queries go to DuckDuckGo; public HTTPS pages are read without browser cookies. Screenshots stay local. Web access is optional; local chat works offline.", 13, Muted));
        var agent = new CheckBox { Content = "Enable Agent mode on this PC", Foreground = Ink, IsChecked = desktop.AgentEnabled, Margin = new(0,12,0,8) }; p.Children.Add(agent);
        p.Children.Add(Text("Buddy can open allowed apps and work with accessible controls. Review a plan before running it. Each change asks for approval. Esc, Ctrl+Alt+Esc or moving the mouse stops the run. Passwords, elevated apps, terminals and blocked apps are refused.", 13, Muted));
        p.Children.Add(Text("Additional blocked process names (comma separated)", 14)); var blocked = new TextBox { Text = desktop.BlockedApps }; StyleBox(blocked); p.Children.Add(blocked);
        var notice = Text("", 12, Accent); p.Children.Add(notice);
        p.Children.Add(Btn("Save assistant settings", () => {
            try {
                Cancel(); var next = desktop with { AllowWebResearch = web.IsChecked == true, AgentEnabled = agent.IsChecked == true, BlockedApps = blocked.Text.Trim() };
                next.Save(); desktop = next; if (host is not null) { host.Service.WebEnabled = desktop.AllowWebResearch; host.Service.AgentEnabled = desktop.AgentEnabled; }
                notice.Text = "Saved. Use Guide for directions or Agent for a task plan.";
            } catch (Exception ex) { notice.Text = ex.Message; }
        }, true));
        p.Children.Add(Btn("Recent capture and action log", () => _ = ShowAudit()));
        p.Children.Add(Btn("Clear capture and action log", () => { if (host is not null) _ = host.Service.Store.Update(s => { s.Audit.Clear(); return true; }); notice.Text = "Log cleared."; }));
        Dialog("Buddy · Assistant settings", p, 620, 640);
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
            practice.Show();
        } else practice.Activate();
    }
}

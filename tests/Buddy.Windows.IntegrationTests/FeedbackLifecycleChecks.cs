using Buddy.Server;
using Buddy.Windows;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

// Root-only native execution. All windows and preference paths are fixture-owned.
// Region submit is intercepted before capture, inference, microphone or speech.
internal static class FeedbackLifecycleChecks
{
    private static object? Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(value);
    private static object? Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(value,args);
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    { yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var item in Tree(child))yield return item; }
    private static T Named<T>(DependencyObject root,string name) where T:DependencyObject=>Tree(root).OfType<T>().Single(x=>AutomationProperties.GetName(x)==name);
    private static async Task Idle(Window window){window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
    private static DesktopPreferences Persist(DesktopPreferences next,string path,DesktopPreferences previous)
    {
        var method=typeof(DesktopPreferences).GetMethods(BindingFlags.NonPublic|BindingFlags.Instance).Single(m=>m.Name=="Save");
        return method.Invoke(next,method.GetParameters().Length==2?[path,previous]:[path]) as DesktopPreferences ?? DesktopPreferences.Load(path);
    }
    private static void Enter(TextBox editor)
    {
        var source=PresentationSource.FromVisual(editor)??throw new InvalidOperationException("Owned review has no presentation source.");
        var preview=new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent};editor.RaiseEvent(preview);
        if(!preview.Handled)editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
    }
    internal static int Run()
    {
        int count=0,exit=1;var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var owner=new Window{Title="Buddy owned feedback lifecycle fixture",Width=520,Height=260,Content=new TextBox{Text="Owned region source; must not change.",Margin=new(24)}};
        string folder=Path.Combine(Path.GetTempPath(),"Buddy-feedback-native-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        MainWindow? home=null;VoiceOverlayWindow? overlay=null;
        void Check(bool ok,string note){if(!ok)throw new Exception("FAIL: "+note);count++;Console.WriteLine("PASS: "+note);}
        owner.Loaded+=async(_,_)=>{
            try {
                string path=Path.Combine(folder,"desktop.json");
                var initial=new DesktopPreferences{CompanionName="Owned original",NeuralSpeakerId=85,VoiceEngine="piper",Shortcut="Ctrl + Alt + Space",VoiceShortcut="Ctrl + Shift + Space",ReduceMotion=true,AdditionalSettings=new(){["futureFixture"]=JsonSerializer.SerializeToElement("retained")}};
                initial.Save(path);var baseline=DesktopPreferences.Load(path);bool refuse=false,mergeConflict=false;
                home=new MainWindow(false,baseline,mergePreferences:value=>{
                    if(refuse)throw new IOException("Owned persistence refusal");
                    if(mergeConflict){(baseline with{Shortcut="Alt + Shift + Space"}).Save(path);mergeConflict=false;}
                    baseline=Persist(value,path,baseline);return baseline;
                });home.Show();
                home.OpenSettingsSection("General");await Idle(home);
                Named<TextBox>(home,"Companion name").Text="Owned navigation edit";
                Call(home,"ShowSettingsCategory","Add-ons");await Idle(home);
                Check(DesktopPreferences.Load(path).CompanionName=="Owned navigation edit","Changing Settings category flushes the actual edited name instead of resetting it");
                var region=Named<CheckBox>(home,"Enable circle selection");region.IsChecked=true;await Idle(home);
                Check(DesktopPreferences.Load(path).RegionSelectionEnabled,"Actual option toggle is persisted without requiring a hidden Save step");
                var saved=DesktopPreferences.Load(path);
                Check(saved.NeuralSpeakerId==85&&saved.VoiceEngine=="piper"&&saved.Shortcut==initial.Shortcut&&saved.VoiceShortcut==initial.VoiceShortcut&&saved.AdditionalSettings!["futureFixture"].GetString()=="retained","Unrelated voice, shortcut and future fields survive category edits");

                Call(home,"ShowSettingsCategory","General");await Idle(home);refuse=true;
                Named<TextBox>(home,"Companion name").Text="Owned unsaved edit";
                Call(home,"ShowSettingsCategory","Add-ons");await Idle(home);
                Check((string)Field(home,"settingsSection")! == "General" && Named<TextBox>(home,"Companion name").Text=="Owned unsaved edit","A failed save keeps the dirty category and entered value visible");
                Check(Tree(home).OfType<TextBlock>().Any(t=>t.Text.Contains("Owned persistence refusal",StringComparison.Ordinal)),"Persistence failure is shown instead of silently resetting controls");
                refuse=false;Call(home,"ShowSettingsCategory","Add-ons");await Idle(home);
                Check(DesktopPreferences.Load(path).CompanionName=="Owned unsaved edit","Retrying category navigation preserves and commits the original pending edit");

                var registration=new ShortcutRegistration((_,modifiers)=>(modifiers&0xff)==3,_=>{});
                Check(registration.TrySet(ShortcutChoice.Find(initial.Shortcut)),"Owned mock shortcut has a working original binding");
                typeof(MainWindow).GetField("shortcut",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(home,registration);
                Call(home,"ShowSettingsCategory","General");await Idle(home);mergeConflict=true;
                Named<TextBox>(home,"Companion name").Text="Saved with shortcut warning";
                Call(home,"ShowSettingsCategory","Shortcuts");await Idle(home);
                Check((string)Field(home,"settingsSection")! == "Shortcuts"&&DesktopPreferences.Load(path).CompanionName=="Saved with shortcut warning"&&DesktopPreferences.Load(path).Shortcut=="Alt + Shift + Space", "A committed merged shortcut conflict permits navigation to Shortcuts and keeps saved edits");
                Check(registration.Active?.Label==initial.Shortcut&&Tree(home).OfType<TextBlock>().Any(t=>t.Text.Contains("Saved on this PC.",StringComparison.Ordinal)&&t.Text.Contains("in use",StringComparison.Ordinal)),"A saved conflict shows a warning while the prior working shortcut remains active");
                Check(home.FlushPendingPreferences(),"A saved runtime warning is not treated as a failed dirty save");

                var hwnd=new WindowInteropHelper(owner).Handle;ScreenPerception.PracticeHandle=hwnd;
                var preferences=new DesktopPreferences{CaptureOnVoice=false,RegionSelectionEnabled=true,ReadVoiceAnswers=false};
                overlay=new(()=>throw new Exception("Region routing must not reach a service"),()=>throw new Exception("Region routing must not open a conversation"),()=>preferences,_=>{},()=>hwnd,new(()=>preferences),(_,_)=>throw new Exception("Region routing must not execute a workflow"),()=>{},createRecognizer:(_,_)=>throw new Exception("Region routing must not start a microphone"));
                var submitted=new List<string>();overlay.RefinementReply=words=>{submitted.Add(words);return true;};
                RegionLease Region()
                {
                    var bounds=WindowCapture.Bounds(hwnd);var screen=System.Windows.Forms.Screen.FromHandle(hwnd);var monitor=screen.Bounds;
                    var allowed=new PixelBounds(bounds.X,bounds.Y,bounds.Width,bounds.Height);
                    var shape=new RegionShape([new(bounds.X+20,bounds.Y+40),new(bounds.X+160,bounds.Y+40),new(bounds.X+160,bounds.Y+150),new(bounds.X+20,bounds.Y+150)],allowed);
                    return new(new(hwnd,InputNative.ProcessName(hwnd),Security.Redact(Native.Label(hwnd)),screen.DeviceName,new(monitor.X,monitor.Y,monitor.Width,monitor.Height),bounds,OverlayNative.Scale(hwnd),DateTimeOffset.UtcNow,shape),true);
                }
                overlay.OpenRegion(Region());await Idle(overlay);var review=Named<TextBox>(overlay,"Review uncertain transcript");review.Text="Explain this owned area";overlay.Activate();review.Focus();await Idle(overlay);
                Enter(review);await Idle(overlay);
                Check(submitted.SequenceEqual(["Explain this owned area"]),"Enter submits the actual selected-area review text exactly once without capture or inference");
                overlay.OpenRegion(Region());await Idle(overlay);review=Named<TextBox>(overlay,"Review uncertain transcript");review.Text="";overlay.Activate();review.Focus();await Idle(overlay);
                Enter(review);await Idle(overlay);
                Check(submitted.Count==2&&submitted[1]=="Explain the selected area in the current app.","Empty selected-area Enter submits the explicit default explanation request");
                int priorCount = submitted.Count;
                overlay.OpenRegion(Region(), autoExplain:true); overlay.OpenRegion(Region()); await Idle(overlay);
                Check(submitted.Count == priorCount, "A queued auto-explanation cannot submit a replacement region awaiting review");
                overlay.OpenRegion(Region(), autoExplain:true); overlay.Cancel(); review.Text = "Stale canceled question"; await Idle(overlay);
                Check(submitted.Count == priorCount, "Canceled queued region work cannot submit hidden stale text");
                Check(((TextBox)owner.Content).Text=="Owned region source; must not change."&&!overlay.IsListening,"Region submit routing never edits the source or starts listening in this fixture");
                Console.WriteLine($"ALL {count} OWNED FEEDBACK LIFECYCLE CHECKS PASSED; no real model or browser acceptance.");exit=0;
            }catch(Exception ex){Console.Error.WriteLine(ex);}
            finally{
                overlay?.Dispose();if(home is not null){typeof(MainWindow).GetField("shuttingDown",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(home,true);home.Close();}
                ScreenPerception.PracticeHandle=IntPtr.Zero;owner.Close();
                string full=Path.GetFullPath(folder),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("Buddy-feedback-native-",StringComparison.Ordinal)&&Directory.Exists(full))Directory.Delete(full,true);
                app.Shutdown();
            }
        };
        app.Run(owner);return exit;
    }
}

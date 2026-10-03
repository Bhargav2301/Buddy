using System.Windows;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private ShortcutRegistration? regionShortcut;
    private PushToTalkHook? regionHook;
    private RegionSelectionWindow? regionPicker;
    private void ConfigureRegionHook()
    {
        regionPicker?.Cancel(); regionHook?.Dispose(); regionHook=null;
        if(!desktop.RegionSelectionEnabled || regionShortcut?.Active is not { } chord)return;
        try { regionHook=new(chord.Modifiers,()=>Dispatcher.BeginInvoke(new Action(BeginRegionSelection)),
            ()=>Dispatcher.BeginInvoke(new Action(BeginRegionSelection)),()=>Dispatcher.BeginInvoke(new Action(()=>regionPicker?.Finish())),0x52); }
        catch(Exception ex){status.Text="Area shortcut works as a tap; hold detection is unavailable: "+ex.Message;}
    }
    private void BeginRegionSelection() => BeginRegionSelection(false);
    private void BeginRegionSelection(bool explicitOnce)
    {
        if(!desktop.RegionSelectionEnabled&&!explicitOnce){OpenSettingsSection("Add-ons");return;}
        if(regionPicker is not null || assistant is null || voiceOverlay is null)return;
        var selected=Native.GetForegroundWindow();if(selected==IntPtr.Zero||Native.IsOwnWindow(selected))selected=previousWindow;
        try {
            assistant.Perception.Check(selected);
            PrepareDesktopActivity("region"); quick?.Dismiss(); voiceOverlay.Dismiss();
            regionPicker=new(selected,selection=>{
                regionPicker=null;
                if(selection is null){InputNative.SetForegroundWindow(selected);return;}
                try { var lease=new RegionLease(selection,explicitOnce); if(desktop.RegionVoiceAfterSelection)voiceOverlay.Open(false,lease);else voiceOverlay.OpenRegion(lease); }
                catch(Exception ex){status.Text=ex.Message;}
            });
            regionPicker.Show();
        }catch(Exception ex){regionPicker?.Cancel();regionPicker=null;status.Text=ex.Message;}
    }
}

using Buddy.Server;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private BrowserContextReviewWindow? browserContextWindow;
    private void OpenBrowserContext()=>OpenBrowserContext(null);
    private void OpenBrowserContext(FrozenRefinementContext? selected)
    {
        RevokePreparedContext();
        if(browserContextWindow is null)
        {
            var window=new BrowserContextReviewWindow(refinementContext);browserContextWindow=window;
            window.Closed+=(_,_)=>{if(ReferenceEquals(browserContextWindow,window))browserContextWindow=null;};
            window.Show();
        }
        if(selected is not null)browserContextWindow.SetSelection(selected);
        browserContextWindow.Activate();
    }
    private void StopBrowserContext(){browserContextWindow?.Close();browserContextWindow=null;}
}

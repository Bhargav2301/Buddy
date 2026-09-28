using Buddy.Server;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Buddy.Windows;

internal sealed class PracticeWindow : Window
{
    internal readonly TextBox Editor = new() { Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new(0,10,0,10) };
    internal readonly Button Export = new() { Content = "Export example", Padding = new(14) };
    internal readonly TextBlock Result = new() { Text = "Nothing exported yet.", Margin = new(0,10,0,10) };
    private readonly GuidanceOverlay ink = new();
    internal PracticeWindow()
    {
        Title = "Buddy · practice"; Width = 560; Height = 390;
        var p = new StackPanel { Margin = new(24) }; p.Children.Add(new TextBlock { Text = "Hello, my name is Buddy.", FontSize = 25 });
        p.Children.Add(new TextBlock { Text = "This practice window lets you try pointing and Agent actions without changing a real document.", TextWrapping = TextWrapping.Wrap, Margin = new(0,12,0,0) });
        AutomationProperties.SetAutomationId(Editor, "BuddyPracticeEditor"); AutomationProperties.SetName(Editor, "Example text");
        AutomationProperties.SetAutomationId(Export, "BuddyPracticeExport");
        p.Children.Add(Editor); p.Children.Add(Export); p.Children.Add(Result);
        Export.Click += (_,_) => Result.Text = "Example exported — no file was created.";
        var point = new Button { Content = "Show me where Export is", Padding = new(10) }; p.Children.Add(point);
        point.Click += async (_,_) => {
            try {
                var snapshot = await new ScreenPerception(() => new()).Capture(new WindowInteropHelper(this).Handle, CancellationToken.None);
                var element = GroundingResolver.Resolve(snapshot.Context.Elements, "", "Export example", "Button");
                if (element is not null) ink.Draw(snapshot.Window, element, "arrow", "Click Export example");
            } catch (Exception ex) { Result.Text = ex.Message; }
        };
        Content = p; SourceInitialized += (_,_) => ScreenPerception.PracticeHandle = new WindowInteropHelper(this).Handle;
        Closed += (_,_) => { ink.Dispose(); if (ScreenPerception.PracticeHandle == new WindowInteropHelper(this).Handle) ScreenPerception.PracticeHandle = IntPtr.Zero; };
    }
}

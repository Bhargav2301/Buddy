using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Buddy.Server;

namespace Buddy.Windows;

/// <summary>Setup only. The host owns routing and disposes replaced sessions; this window never sends a request.</summary>
internal sealed class ProviderSetupWindow : Window
{
    internal ProviderSetupWindow(Func<ProviderRoutingStatus?> current, Action<ProviderRoutingSession?> replace)
    {
        BuddyTheme.Ensure(); Title = "Buddy · Cloud text setup"; Width = 620; Height = 680;
        MinWidth = 400; MinHeight = 400; Background = BuddyTheme.Canvas; Foreground = BuddyTheme.Ink;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => CaptureProtection.Apply(new WindowInteropHelper(this).Handle);
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Label("Cloud text session", 24));
        panel.Children.Add(Label("Local Ollama stays available. This setup stores a key only in memory until disconnect or exit. Each cloud request requires a separate review of its exact typed text."));
        var state = Label(current()?.Detail ?? "Disconnected. No cloud key or consent has been loaded."); panel.Children.Add(state);
        panel.Children.Add(Label("Provider"));
        var provider = new ComboBox { ItemsSource = ProviderProtocols.Providers, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(provider);
        panel.Children.Add(Label("Exact text-model ID from your provider"));
        var model = new TextBox { MaxLength = 120, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(model);
        panel.Children.Add(Label("API key · session only"));
        var key = new PasswordBox { MaxLength = 512, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(key);
        var sharing = new CheckBox { Content = Label("I agree to send only the typed text I review to this provider, with Buddy's fixed concise-reply instruction."), IsChecked = false };
        var cost = new CheckBox { Content = Label("I understand the provider may charge my account. Buddy does not quote a price or enforce an account spending cap."), IsChecked = false };
        panel.Children.Add(sharing); panel.Children.Add(cost);
        panel.Children.Add(Label("Screenshots, audio, conversation history, files, tools and account data are excluded. Setup performs no connection test and does not claim that the model or key works."));
        panel.Children.Add(BuddyTheme.Button("Configure reviewed text session", () =>
        {
            ProviderRoutingSession? next = null; char[] chars = []; IntPtr buffer = IntPtr.Zero;
            try
            {
                using var secure = key.SecurePassword;
                chars = new char[secure.Length]; buffer = Marshal.SecureStringToGlobalAllocUnicode(secure); Marshal.Copy(buffer, chars, 0, chars.Length);
                next = ProviderRoutingSession.CreateProduction(new(provider.SelectedItem as string ?? "", model.Text.Trim(), sharing.IsChecked == true, cost.IsChecked == true), chars);
                replace(next); next = null; key.Clear(); sharing.IsChecked = false; cost.IsChecked = false;
                state.Text = current()?.Detail ?? "Session configured; a host route is still required.";
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            { state.Text = "Setup was not completed. Check the provider, model, key format and both consent choices."; }
            finally { next?.Dispose(); Array.Clear(chars); if (buffer != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(buffer); }
        }, true));
        panel.Children.Add(BuddyTheme.Button("Disconnect cloud session", () => { replace(null); key.Clear(); state.Text = "Disconnected. Return to local text processing."; }));
        panel.Children.Add(Label("Account connectors", 20));
        foreach (var connector in ConnectorSetup.Statuses)
            panel.Children.Add(Label(connector.Provider + " — " + connector.State + "\n" + connector.RequiredSetup + "\n" + connector.NextStep));
        Closed += (_, _) => key.Clear();
    }
    private static TextBlock Label(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Foreground = BuddyTheme.Ink };
}

internal sealed class ProviderQuestionReviewWindow : Window
{
    internal ProviderQuestionReviewWindow(ProviderQuestionReview review)
    {
        Title = "Buddy · Review cloud text"; Width = 620; Height = 600; MinWidth = 400; MinHeight = 360;
        Background = BuddyTheme.Canvas; Foreground = BuddyTheme.Ink; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => CaptureProtection.Apply(new WindowInteropHelper(this).Handle);
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new TextBlock { Text = "Send to " + review.Provider + " / " + review.Model + "?\nOnly the text below plus Buddy's fixed concise-reply instruction will be sent. Provider charges may apply. Review expires in two minutes.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        var description = new StackPanel(); description.Children.Add(heading);
        description.Children.Add(new Expander { Header = "Read the exact fixed instruction", Margin = new Thickness(0, 0, 0, 12), Content = new ScrollViewer { MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = ProviderProtocols.TextPolicy, TextWrapping = TextWrapping.Wrap } } });
        grid.Children.Add(description);
        var body = new TextBox { Text = review.Text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(body, 1); grid.Children.Add(body);
        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(BuddyTheme.Button("Send this text", () => { DialogResult = true; }, true));
        actions.Children.Add(BuddyTheme.Button("Keep local / Cancel", () => { DialogResult = false; }));
        Grid.SetRow(actions, 2); grid.Children.Add(actions); Content = grid;
    }
}

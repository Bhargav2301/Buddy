using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Buddy.Windows;

internal static class BuddyTheme
{
    private sealed class ColorValue : System.ComponentModel.INotifyPropertyChanged
    {
        private Color value;
        public Color Value { get => value; set { this.value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
    private static readonly Dictionary<SolidColorBrush, ColorValue> colors = [];
    // Mutable shared brushes update already-open surfaces without rebuilding their controls.
    internal static readonly SolidColorBrush Canvas = Make("#F8FAFC"), Surface = Make("#FFFFFF"),
        Ink = Make("#111827"), Muted = Make("#4B5563"), Accent = Make("#20B8A6"),
        Deep = Make("#0F766E"), Soft = Make("#CCFBF1"), Line = Make("#E5E7EB"),
        Risk = Make("#DC2626"), RiskSoft = Make("#FEE2E2"), Warn = Make("#D97706"), OnAccent = Make("#0B1220");
    internal static readonly FontFamily Font = new("Segoe UI Variable, Segoe UI");
    private static bool installed;
    private static string appearance = "System";
    internal static bool ReducedMotion { get; private set; }
    internal static void Ensure()
    {
        if (installed || Application.Current is null) return;
        var resources = Application.Current.Resources;
        foreach (var item in new[] { ("Canvas", Canvas), ("Surface", Surface), ("Ink", Ink), ("Muted", Muted), ("Accent", Accent), ("Deep", Deep), ("Soft", Soft), ("Line", Line), ("Risk", Risk), ("RiskSoft", RiskSoft), ("Warn", Warn), ("OnAccent", OnAccent) }) resources["Buddy." + item.Item1] = item.Item2;
        resources["Buddy.Font"] = Font;
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Buddy;component/Themes/Controls.xaml", UriKind.Relative) });
        installed = true;
        SystemEvents.UserPreferenceChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(appearance, ReducedMotion)));
        SystemParameters.StaticPropertyChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(appearance, ReducedMotion)));
    }
    internal static void Apply(string choice, bool reducedMotion)
    {
        Ensure(); appearance = choice; ReducedMotion = reducedMotion;
        bool dark = choice == "Dark";
        if (choice == "System") {
            try { dark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; } catch { dark = false; }
        }
        Set(Canvas, Color(dark ? "#0B1220" : "#F8FAFC")); Set(Surface, Color(dark ? "#172333" : "#FFFFFF"));
        Set(Ink, Color(dark ? "#F8FAFC" : "#111827")); Set(Muted, Color(dark ? "#B5C2D0" : "#4B5563"));
        Set(Accent, Color("#20B8A6")); Set(OnAccent, Color("#0B1220")); Set(Deep, Color(dark ? "#5EEAD4" : "#0F766E")); Set(Soft, Color(dark ? "#134E4A" : "#CCFBF1"));
        Set(Line, Color(dark ? "#475569" : "#E5E7EB")); Set(Risk, Color(dark ? "#FCA5A5" : "#DC2626")); Set(RiskSoft, Color(dark ? "#4C2028" : "#FEE2E2")); Set(Warn, Color(dark ? "#FCD34D" : "#D97706"));
        if (SystemParameters.HighContrast) {
            foreach (var brush in new[] { Canvas, Surface, Soft, RiskSoft }) Set(brush, SystemColors.WindowColor);
            foreach (var brush in new[] { Ink, Muted, Line }) Set(brush, SystemColors.WindowTextColor);
            foreach (var brush in new[] { Accent, Deep, Risk, Warn }) Set(brush, SystemColors.HighlightColor);
            Set(OnAccent, SystemColors.HighlightTextColor);
        }
    }
    internal static bool Animate => !ReducedMotion && !SystemParameters.HighContrast && SystemParameters.ClientAreaAnimation;
    internal static Button Button(string label, Action action, bool primary = false)
    {
        Ensure();
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 8) };
        if (primary && Application.Current is not null) button.SetResourceReference(FrameworkElement.StyleProperty, "Buddy.PrimaryButton");
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }
    internal static Border Card(UIElement content, double padding = 24) => new() { Child = content, Background = Surface, CornerRadius = new(12), Padding = new(padding), Margin = new(0, 0, 0, 16) };
    private static SolidColorBrush Make(string value)
    {
        var brush = new SolidColorBrush(); var source = new ColorValue { Value = Color(value) }; colors.Add(brush, source);
        System.Windows.Data.BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new System.Windows.Data.Binding(nameof(ColorValue.Value)) { Source = source });
        return brush;
    }
    private static void Set(SolidColorBrush brush, Color value) => colors[brush].Value = value;
    private static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value);
}

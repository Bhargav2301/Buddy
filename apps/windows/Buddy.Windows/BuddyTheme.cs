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
    internal static readonly SolidColorBrush Canvas = Make("#050505"), Surface = Make("#101010"),
        Ink = Make("#F4FCF6"), Muted = Make("#BFD3C6"), Accent = Make("#285B45"),
        Deep = Make("#B5F4D2"), Soft = Make("#222222"), Line = Make("#575757"),
        Risk = Make("#FFCDCA"), RiskSoft = Make("#462B2B"), Warn = Make("#FFE3A1"), OnAccent = Make("#F6FFF9"),
        Raised = Make("#1C1C1C"), Input = Make("#080808"), Hover = Make("#282828"), Pressed = Make("#363636"),
        Secondary = Make("#D7E9DD"), Placeholder = Make("#A9C4B2"), DisabledText = Make("#A7BDAE"), DisabledSurface = Make("#202020"),
        ControlBorder = Make("#A0ADA5"), FocusRing = Make("#C4FFE2"), FocusGap = Make("#080808"),
        ActionHover = Make("#326C52"), ActionPressed = Make("#397859"), ActionBorder = Make("#8DDDB4"), BrandMint = Make("#93DEC6"),
        Positive = Make("#C7F5D9"), WarningSurface = Make("#433820"), ErrorBorder = Make("#FFB8B2"),
        Added = Make("#C8FDDD"), AddedSurface = Make("#244938"), Removed = Make("#FFD5D1"), RemovedSurface = Make("#4B2D30"),
        SelectionText = Make("#102A1E"), SelectionSurface = Make("#BDF5DB");
    internal static readonly FontFamily Font = new("Segoe UI Variable, Segoe UI");
    private static bool installed;
    private static string appearance = "Black";
    internal static bool ReducedMotion { get; private set; }
    internal static bool IsNightMint => appearance == "Night Mint";
    internal static bool IsBlack => appearance == "Black";
    internal static void Ensure()
    {
        if (installed || Application.Current is null) return;
        var resources = Application.Current.Resources;
        foreach (var field in typeof(BuddyTheme).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
            if (field.GetValue(null) is SolidColorBrush brush) resources["Buddy." + field.Name] = brush;
        resources["Buddy.Font"] = Font;
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Buddy;component/Themes/Controls.xaml", UriKind.Relative) });
        installed = true;
        SystemEvents.UserPreferenceChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(appearance, ReducedMotion)));
        SystemParameters.StaticPropertyChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(appearance, ReducedMotion)));
    }
    internal static void Apply(string choice, bool reducedMotion)
    {
        Ensure(); appearance = choice; ReducedMotion = reducedMotion;
        bool dark = choice is "Dark" or "Black";
        if (choice == "System") {
            try { dark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; } catch { dark = false; }
        }
        Set(Canvas, Color(dark ? "#0B1220" : "#F8FAFC")); Set(Surface, Color(dark ? "#172333" : "#FFFFFF"));
        Set(Ink, Color(dark ? "#F8FAFC" : "#111827")); Set(Muted, Color(dark ? "#B5C2D0" : "#4B5563"));
        Set(Accent, Color("#20B8A6")); Set(OnAccent, Color("#0B1220")); Set(Deep, Color(dark ? "#5EEAD4" : "#0F766E")); Set(Soft, Color(dark ? "#134E4A" : "#CCFBF1"));
        Set(Line, Color(dark ? "#475569" : "#E5E7EB")); Set(Risk, Color(dark ? "#FCA5A5" : "#DC2626")); Set(RiskSoft, Color(dark ? "#4C2028" : "#FEE2E2")); Set(Warn, Color(dark ? "#FCD34D" : "#D97706"));
        Set(Raised, Color(dark ? "#223449" : "#EEF3F7")); Set(Input, Surface.Color);
        Set(Hover, Color(dark ? "#2C4359" : "#E5F5F1")); Set(Pressed, Color(dark ? "#375369" : "#CCEBE3"));
        Set(Secondary, Ink.Color); Set(Placeholder, Muted.Color); Set(DisabledText, Muted.Color); Set(DisabledSurface, Raised.Color);
        Set(ControlBorder, Color(dark ? "#91A6BB" : "#607568")); Set(FocusRing, Deep.Color); Set(FocusGap, Surface.Color);
        Set(ActionHover, Color("#38CBB8")); Set(ActionPressed, Color("#5DDFCD")); Set(ActionBorder, Deep.Color);
        Set(Positive, Deep.Color); Set(WarningSurface, Color(dark ? "#433820" : "#FFF3CF")); Set(ErrorBorder, Risk.Color);
        Set(Added, Color(dark ? "#C8FDDD" : "#174C30")); Set(AddedSurface, Color(dark ? "#244938" : "#DCFCE7"));
        Set(Removed, Color(dark ? "#FFD5D1" : "#7C2228")); Set(RemovedSurface, Color(dark ? "#4B2D30" : "#FFE4E6"));
        Set(SelectionSurface, Color("#BDF5DB")); Set(SelectionText, Color("#102A1E"));
        if (choice is "Night Mint" or "Black") {
            foreach (var (brush, value) in new[] {
                (Canvas,"#0D1C16"),(Surface,"#172B23"),(Raised,"#21372E"),(Input,"#10231B"),(Hover,"#2C493B"),(Pressed,"#355B49"),
                (Ink,"#F4FCF6"),(Secondary,"#D7E9DD"),(Muted,"#BFD3C6"),(Placeholder,"#A9C4B2"),(DisabledText,"#A7BDAE"),(DisabledSurface,"#24352C"),
                (Line,"#496256"),(ControlBorder,"#88B59C"),(FocusRing,"#C4FFE2"),(FocusGap,"#10231B"),
                (Accent,"#285B45"),(ActionHover,"#326C52"),(ActionPressed,"#397859"),(OnAccent,"#F6FFF9"),(ActionBorder,"#8DDDB4"),
                (Deep,"#B5F4D2"),(BrandMint,"#93DEC6"),(Positive,"#C7F5D9"),(Soft,"#263F32"),(Warn,"#FFE3A1"),(WarningSurface,"#433820"),
                (Risk,"#FFCDCA"),(RiskSoft,"#462B2B"),(ErrorBorder,"#FFB8B2"),(Added,"#C8FDDD"),(AddedSurface,"#244938"),
                (Removed,"#FFD5D1"),(RemovedSurface,"#4B2D30"),(SelectionText,"#102A1E"),(SelectionSurface,"#BDF5DB") }) Set(brush, Color(value));
        }
        if (choice == "Black") {
            // Keep the readable text and semantic state roles; only the green-tinted base surfaces change.
            foreach (var (brush, value) in new[] {
                (Canvas,"#050505"),(Surface,"#101010"),(Raised,"#1C1C1C"),(Input,"#080808"),
                (Hover,"#282828"),(Pressed,"#363636"),(Soft,"#222222"),(DisabledSurface,"#202020"),
                (Line,"#575757"),(ControlBorder,"#A0ADA5"),(FocusGap,"#080808") }) Set(brush, Color(value));
        }
        if (SystemParameters.HighContrast) {
            foreach (var brush in new[] { Canvas, Surface, Raised, Input, Hover, Pressed, Soft, RiskSoft, WarningSurface, DisabledSurface, FocusGap, AddedSurface, RemovedSurface }) Set(brush, SystemColors.WindowColor);
            foreach (var brush in new[] { Ink, Secondary, Muted, Placeholder, DisabledText, Line, ControlBorder, Deep, Positive, Risk, Warn, ErrorBorder, Added, Removed, FocusRing, ActionBorder }) Set(brush, SystemColors.WindowTextColor);
            foreach (var brush in new[] { Accent, ActionHover, ActionPressed, SelectionSurface }) Set(brush, SystemColors.HighlightColor);
            foreach (var brush in new[] { OnAccent, SelectionText }) Set(brush, SystemColors.HighlightTextColor);
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
    internal static System.Windows.Documents.Run DiffRun(string text, bool added) => new(text) {
        Foreground = added ? Added : Removed, Background = added ? AddedSurface : RemovedSurface,
        TextDecorations = added ? TextDecorations.Underline : TextDecorations.Strikethrough
    };
    private static SolidColorBrush Make(string value)
    {
        var brush = new SolidColorBrush(); var source = new ColorValue { Value = Color(value) }; colors.Add(brush, source);
        System.Windows.Data.BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new System.Windows.Data.Binding(nameof(ColorValue.Value)) { Source = source });
        return brush;
    }
    private static void Set(SolidColorBrush brush, Color value) => colors[brush].Value = value;
    private static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value);
}

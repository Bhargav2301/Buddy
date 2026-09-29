namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private void ShowOnboarding()
    {
        if (host is null) return;
        if (onboarding is not null) { onboarding.Activate(); return; }
        onboarding = new OnboardingWindow(desktop.CompanionName,
            name => SavePreferences(desktop with { CompanionName = string.IsNullOrWhiteSpace(name) ? "Buddy" : name, OnboardingCompleted = true }),
            () => OpenSettingsSection("AI"), ShowPractice, () => OpenQuick(false), () => OpenQuick(true),
            async () => { var selected = await host.Service.Store.Read(s => new { s.Model, s.VisionModel }); return (await host.Service.Engine.Status(selected.Model, selected.VisionModel)).Message; });
        onboarding.Closed += (_, _) => onboarding = null; onboarding.Show();
    }
}

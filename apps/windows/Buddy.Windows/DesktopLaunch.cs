namespace Buddy.Windows;

internal enum LaunchDestination : byte { Home = 1, Settings = 2, Background = 3 }

internal static class DesktopLaunch
{
    internal static LaunchDestination Parse(string[] args) => args.Contains("--settings") ? LaunchDestination.Settings :
        args.Contains("--home") ? LaunchDestination.Home : args.Contains("--background") ? LaunchDestination.Background : LaunchDestination.Home;
}

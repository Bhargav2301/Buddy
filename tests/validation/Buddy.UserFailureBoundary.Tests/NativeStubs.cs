// Only the pure selection policy is linked here. Every test must inject its own
// probe and policy; accidental fallback to any native default fails immediately.
namespace Buddy.Windows;
internal static class InputNative
{
    internal static WindowProbe InspectWindow(IntPtr _) => throw new InvalidOperationException("Native inspection is forbidden in the policy fixture.");
    internal static void CheckDesktopAndElevation(IntPtr _) => throw new InvalidOperationException("Native elevation inspection is forbidden in the policy fixture.");
}
internal static class Native
{
    internal static bool IsOwnWindow(IntPtr _) => throw new InvalidOperationException("Native identity is forbidden in the policy fixture.");
    internal static bool IsSelectableWindow(IntPtr _) => throw new InvalidOperationException("Native selection is forbidden in the policy fixture.");
    internal static void CheckWindow(IntPtr _) => throw new InvalidOperationException("Native policy is forbidden in the policy fixture.");
    internal static IntPtr GetForegroundWindow() => throw new InvalidOperationException("Native foreground access is forbidden in the policy fixture.");
}
internal static class InstalledAppResolver
{
    internal static Task<VerifiedAppLaunch> ResolveAdditionalAsync(string _, CancellationToken __) => throw new InvalidOperationException("App resolution is forbidden in the policy fixture.");
    internal static bool MatchesRegisteredProcess(VerifiedAppLaunch _, string __, string ___) => throw new InvalidOperationException("App process identity is forbidden in the policy fixture.");
    internal static string ProcessPackageFullName(uint _) => throw new InvalidOperationException("Package process inspection is forbidden in the policy fixture.");
    internal static System.Diagnostics.ProcessStartInfo CometStartInfo() => throw new InvalidOperationException("App resolution is forbidden in the policy fixture.");
    internal static void ValidateComet(string _) => throw new InvalidOperationException("App validation is forbidden in the policy fixture.");
    internal static bool MatchesComet(IntPtr _, string __) => throw new InvalidOperationException("App process resolution is forbidden in the policy fixture.");
}
internal sealed record VerifiedAppLaunch(string Alias, string Executable, System.Diagnostics.ProcessStartInfo? Start = null)
{
    internal bool IsPackaged => throw new InvalidOperationException("Package identity is forbidden in the policy fixture.");
    internal bool SameIdentity(VerifiedAppLaunch _) => throw new InvalidOperationException("App identity access is forbidden in the policy fixture.");
    internal Task StartAsync(CancellationToken _) => throw new InvalidOperationException("App launch is forbidden in the policy fixture.");
}

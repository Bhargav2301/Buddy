using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Buddy.Windows;

// Pure binding policy. Each sample is freshly gathered under the native guards;
// no window title, accessibility text, pixels or input appear in this contract.
internal sealed record AppFrameNode(ComputerWindowIdentity Identity, IntPtr Parent, IntPtr Root,
    string ClassName, bool Visible, bool Cloaked, bool AccessAllowed, string Executable, string PackageFullName);
internal sealed record AppFrameSample(DateTimeOffset At, IntPtr Foreground, AppFrameNode Host,
    IReadOnlyList<AppFrameNode> Children, bool Complete, bool TrustedHost);

internal static class AppFrameBinding
{
    internal const int MaximumChildren = 32;
    internal static ComputerFrameBinding? Bind(VerifiedAppLaunch launch, AppFrameSample sample,
        string fixedHostPath, DateTimeOffset now)
    {
        if (launch.Alias != "calculator" || !launch.IsPackaged || launch.AppUserModelId != "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" ||
            !launch.PackageFullName.StartsWith("Microsoft.WindowsCalculator_", StringComparison.Ordinal) ||
            !launch.PackageFullName.EndsWith("__8wekyb3d8bbwe", StringComparison.Ordinal) ||
            !sample.Complete || !sample.TrustedHost || sample.Children.Count > MaximumChildren ||
            sample.At > now || now - sample.At > TimeSpan.FromSeconds(5)) return null;
        var host = sample.Host;
        if (!Valid(host) || host.Parent != IntPtr.Zero || host.Root != host.Identity.Window || sample.Foreground != host.Identity.Window ||
            host.ClassName != "ApplicationFrameWindow" || !host.Identity.App.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) ||
            !host.Executable.Equals(fixedHostPath, StringComparison.OrdinalIgnoreCase)) return null;
        // More than one visible CoreWindow is ambiguous even when only one has
        // the requested basename. Never search grandchildren or another root.
        var candidates = sample.Children.Where(n => n.ClassName == "Windows.UI.Core.CoreWindow" && n.Visible && !n.Cloaked).ToArray();
        if (candidates.Length != 1) return null;
        var child = candidates[0];
        if (!Valid(child) || child.Parent != host.Identity.Window || child.Root != host.Identity.Window ||
            child.Identity.Window == host.Identity.Window || child.Identity.ProcessId == host.Identity.ProcessId ||
            !child.Identity.App.Equals("CalculatorApp", StringComparison.OrdinalIgnoreCase) ||
            !InstalledAppResolver.MatchesRegisteredProcess(launch, child.Executable, child.PackageFullName)) return null;
        return new(host.Identity, child.Identity, launch.PackageFullName, launch.PackageRoot, launch.MainExecutable, launch.AppUserModelId);
    }
    internal static bool Same(ComputerFrameBinding first, ComputerFrameBinding second) => first == second;
    internal static bool Recheck(AppFrameSample sample, Func<IntPtr, bool, AppFrameNode> inspect, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!sample.Complete || sample.Children.Count > MaximumChildren) return false;
        if (inspect(sample.Host.Identity.Window, false) != sample.Host) return false;
        foreach (var child in sample.Children) {
            ct.ThrowIfCancellationRequested();
            if (inspect(child.Identity.Window, true) != child) return false;
        }
        ct.ThrowIfCancellationRequested();
        bool sameHost = inspect(sample.Host.Identity.Window, false) == sample.Host;
        ct.ThrowIfCancellationRequested();
        return sameHost;
    }
    // Both observations must retain the original identity; the later sample
    // cannot repair a changed/reparented/reused window by adopting its identity.
    internal static ComputerFrameBinding? Confirm(VerifiedAppLaunch launch, AppFrameSample first, AppFrameSample second,
        string fixedHostPath, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (second.At < first.At) return null;
        var before = Bind(launch, first, fixedHostPath, now);
        var after = Bind(launch, second, fixedHostPath, now);
        ct.ThrowIfCancellationRequested();
        return before is not null && after is not null && Same(before, after) ? after : null;
    }
    internal static bool MatchesPackage(ComputerFrameBinding frame, VerifiedAppLaunch launch) =>
        launch.Alias == "calculator" && launch.IsPackaged && frame.PackageFullName == launch.PackageFullName &&
        frame.AppUserModelId == launch.AppUserModelId &&
        frame.PackageRoot.Equals(launch.PackageRoot, StringComparison.OrdinalIgnoreCase) &&
        frame.MainExecutable.Equals(launch.MainExecutable, StringComparison.OrdinalIgnoreCase);
    private static bool Valid(AppFrameNode node) => node.Visible && !node.Cloaked && node.AccessAllowed &&
        node.Identity.Window != IntPtr.Zero && node.Identity.ProcessId != 0 && node.Identity.ThreadId != 0 && node.Identity.ProcessStarted > 0;
}

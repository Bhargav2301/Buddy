using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Buddy.Windows;

// Identity-only evidence for one restoration target. It is never a completed
// foreground observation. The ordinary AppFrameBinding success policy is unchanged.
internal sealed record CalculatorActivationNode(AppFrameNode Node, bool CloakKnown);
internal sealed record CalculatorActivationSample(DateTimeOffset At, CalculatorActivationNode Root,
    IReadOnlyList<CalculatorActivationNode> Children, bool Complete, bool TrustedHost, bool Minimized);

internal static class CalculatorTargetBinding
{
    internal static bool Recheck(CalculatorActivationSample sample,
        Func<IntPtr, bool, CalculatorActivationNode> read,
        Func<IntPtr, IReadOnlyList<IntPtr>> children, Func<IntPtr, bool> minimized, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!sample.Complete) return false;
        IntPtr root = sample.Root.Node.Identity.Window;
        bool packagedRoot = sample.Root.Node.ClassName != "ApplicationFrameWindow";
        var handles = sample.Children.Select(child => child.Node.Identity.Window).ToArray();
        bool SameRoot() => read(root, packagedRoot) == sample.Root && minimized(root) == sample.Minimized;
        if (!SameRoot() || !children(root).SequenceEqual(handles)) return false;
        foreach (var child in sample.Children) {
            ct.ThrowIfCancellationRequested();
            if (read(child.Node.Identity.Window, true) != child) return false;
        }
        // The last child read can outlive earlier host/ambiguity evidence.
        // Repeat the complete inventory and root state before granting authority.
        ct.ThrowIfCancellationRequested();
        return children(root).SequenceEqual(handles) && SameRoot();
    }
    internal static CalculatorWindowTarget? Bind(VerifiedAppLaunch launch, uint processId, long started,
        CalculatorActivationSample sample, string fixedHost, DateTimeOffset now)
    {
        if (launch.Alias != "calculator" || !launch.IsPackaged || processId == 0 || started <= 0 ||
            launch.AppUserModelId != "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" ||
            !launch.PackageFullName.StartsWith("Microsoft.WindowsCalculator_", StringComparison.Ordinal) ||
            !launch.PackageFullName.EndsWith("__8wekyb3d8bbwe", StringComparison.Ordinal) ||
            !sample.Complete || sample.At > now || now - sample.At > TimeSpan.FromSeconds(5) ||
            sample.Children.Count > AppFrameBinding.MaximumChildren) return null;
        var root = sample.Root.Node;
        if (!Known(sample.Root) || !root.Visible || root.Cloaked || root.Parent != IntPtr.Zero || root.Root != root.Identity.Window) return null;
        bool Matches(AppFrameNode node) => node.Identity.ProcessId == processId && node.Identity.ProcessStarted == started &&
            node.Identity.App.Equals("CalculatorApp", StringComparison.OrdinalIgnoreCase) &&
            InstalledAppResolver.MatchesRegisteredProcess(launch, node.Executable, node.PackageFullName);
        if (Matches(root)) return sample.Children.Count == 0 ? new(root.Identity, root.Identity, null) : null;
        if (!sample.TrustedHost || root.ClassName != "ApplicationFrameWindow" ||
            !root.Identity.App.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) ||
            !root.Executable.Equals(fixedHost, StringComparison.OrdinalIgnoreCase) || root.Identity.ProcessId == processId) return null;
        // Hidden siblings count too: minimization never makes an ambiguous frame safe.
        var core = sample.Children.Where(c => c.Node.ClassName == "Windows.UI.Core.CoreWindow").ToArray();
        if (core.Length != 1) return null;
        var child = core[0].Node;
        if (!Known(core[0]) || !Matches(child) || child.Parent != root.Identity.Window || child.Root != root.Identity.Window ||
            child.Identity.Window == root.Identity.Window ||
            !sample.Minimized && (!child.Visible || child.Cloaked)) return null;
        var frame = new ComputerFrameBinding(root.Identity, child.Identity, launch.PackageFullName,
            launch.PackageRoot, launch.MainExecutable, launch.AppUserModelId);
        return new(root.Identity, child.Identity, frame);
    }
    private static bool Known(CalculatorActivationNode node) => node.CloakKnown && node.Node.AccessAllowed &&
        node.Node.Identity.Window != IntPtr.Zero && node.Node.Identity.ProcessId != 0 &&
        node.Node.Identity.ThreadId != 0 && node.Node.Identity.ProcessStarted > 0;
}

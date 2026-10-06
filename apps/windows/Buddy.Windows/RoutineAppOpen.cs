using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Buddy.Server;

namespace Buddy.Windows;

internal static class RoutineAppOpen
{
    private static int running;
    internal static bool TryGetAlias(string query, out string alias)
    {
        alias = "";
        if (string.IsNullOrWhiteSpace(query) || query.Length > 100) return false;
        if (AppLaunchIntent.Exact(query) is not { IsApp: true, Supported: true } intent) return false;
        alias = intent.Destination;
        return BoundedComputerUse.AllowedAlias(alias);
    }

    internal static async Task<ComputerUseResult> RunAsync(string query, Func<bool> agentEnabled, Func<string> blockedApps, CancellationToken ct,
        IComputerUseBackend? backend = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryGetAlias(query, out string alias)) throw new InvalidOperationException("This request needs the existing plan and approval workflow.");
        if (!agentEnabled()) throw new InvalidOperationException("Enable Agent in Settings before requesting an app launch.");
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) throw new InvalidOperationException("An app launch is already in progress; stop or wait for it first.");
        var controller = new BoundedComputerUse(backend ?? new WindowsRoutineAppBackend(agentEnabled, blockedApps));
        try { return await controller.RunAsync(alias, ct); }
        finally {
            if (controller.Settlement.IsCompleted) Volatile.Write(ref running, 0);
            else _ = ReleaseAfter(controller.Settlement);
        }
    }
    // Only the existing reviewed-action path calls this method. Keep the
    // original task binding at the effect boundary as well as during planning.
    internal static Task<ComputerUseResult> RunApprovedAsync(string query, AssistantAction action,
        Func<bool> agentEnabled, Func<string> blockedApps, CancellationToken ct, IComputerUseBackend? backend = null)
    {
        ct.ThrowIfCancellationRequested();
        var bound = ActionPolicy.Validate(new("Approved app launch", [action]), query).Actions!.Single();
        if (bound.Kind != "open" || !BoundedComputerUse.AllowedAlias(bound.Value))
            throw new InvalidOperationException("This is not a supported installed-app launch.");
        return RunAsync("Open " + AppLaunchIntent.DisplayName(bound.Value), agentEnabled, blockedApps, ct, backend);
    }
    private static async Task ReleaseAfter(Task settlement)
    {
        try { await settlement.ConfigureAwait(false); }
        finally { Volatile.Write(ref running, 0); }
    }
}

// Actual Windows implementation. Observation is metadata only, not a screenshot,
// UIA result, screenshot interpretation, or general computer-use capability.
internal sealed partial class WindowsRoutineAppBackend(Func<bool> agentEnabled, Func<string> blockedApps) : IComputerUseBackend
{
    private sealed record Prepared(ComputerObservation Checkpoint, WindowSelection Selection, string Alias, VerifiedAppLaunch Start);
    private Prepared? prepared;
    private ComputerDispatch? dispatched;
    private int dispatchClaimed;
    private CalculatorWindowTarget? calculatorTarget;

    public Task<ComputerObservation> ObserveAsync(Guid requestId, CancellationToken ct) => Task.Run(() => {
        ct.ThrowIfCancellationRequested(); EnsureEnabled();
        return Observe(requestId, Guid.NewGuid(), ct).Observation;
    }, ct);

    public Task<ComputerObservation> CheckpointAsync(ComputerObservation observation, string alias, CancellationToken ct) => Task.Run(async () => {
        ct.ThrowIfCancellationRequested(); EnsureEnabled();
        if (!BoundedComputerUse.AllowedAlias(alias)) throw new InvalidOperationException("Unsupported routine app.");
        CheckBlocked(alias);
        var start = await ResolveAsync(alias, ct); ct.ThrowIfCancellationRequested();
        var current = Observe(observation.RequestId, observation.Id, ct);
        if (current.Observation.Window != observation.Window) throw new InvalidOperationException("The foreground window changed before launch; make a fresh request.");
        prepared = new(current.Observation, current.Selection, alias, start);
        return current.Observation;
    }, ct);

    public Task<ComputerDispatch> DispatchAsync(ComputerObservation checkpoint, string alias, CancellationToken ct) => Task.Run(async () => {
        ct.ThrowIfCancellationRequested(); EnsureEnabled();
        if (dispatched is not null || prepared is not { } plan || plan.Checkpoint != checkpoint || plan.Alias != alias)
            throw new InvalidOperationException("No matching one-use launch checkpoint exists.");
        if (Interlocked.CompareExchange(ref dispatchClaimed, 1, 0) != 0)
            throw new InvalidOperationException("This launch checkpoint was already consumed; no launch will be repeated.");
        // Resolve again rather than trusting an earlier file/publisher observation.
        var currentStart = await ResolveAsync(alias, ct);
        if (!currentStart.SameIdentity(plan.Start))
            throw new InvalidOperationException("The installed app location changed; no launch ran.");
        // Classic executables retain the existing exact file/ancestor lease.
        // Store apps are activated only by Windows' signed registration: Buddy
        // never executes their payload directly or changes WindowsApps ACLs.
        using var executable = currentStart.IsPackaged ? null : ExecutableLease.Acquire(currentStart.Executable, ct);
        // Re-resolve the same complete identity immediately before dispatch.
        // A package update/removal or classic executable replacement is not
        // silently adopted by a previously reviewed checkpoint.
        var pinned = await ResolveAsync(alias, ct);
        if (!currentStart.SameIdentity(pinned)) throw new InvalidOperationException("The app registration or executable identity changed; no launch ran.");
        plan.Selection.Validate(); CheckWindow(plan.Selection); EnsureEnabled(); CheckBlocked(alias);
        if (Native.GetForegroundWindow() != plan.Selection.Window) throw new InvalidOperationException("The foreground window changed; no launch ran.");
        var age = DateTimeOffset.UtcNow - checkpoint.At;
        if (age < TimeSpan.Zero || age > ComputerUseLimits.Default.ObservationAge)
            throw new InvalidOperationException("The launch checkpoint expired during app verification; make a fresh request.");
        ct.ThrowIfCancellationRequested();
        // Set the receipt before the effect: even an exception cannot authorize replay.
        dispatched = new(checkpoint.RequestId, checkpoint.Id, alias);
        if (alias == "calculator") calculatorTarget = await ActivateCalculatorAsync(pinned, plan.Selection, ct);
        else await pinned.StartAsync(ct);
        ct.ThrowIfCancellationRequested();
        return dispatched;
    }, ct);

    public Task<ComputerVerification> VerifyAsync(ComputerObservation before, ComputerDispatch dispatch, CancellationToken ct) => Task.Run(async () => {
        ct.ThrowIfCancellationRequested(); EnsureEnabled();
        if (dispatched != dispatch || prepared is not { } plan || dispatch.RequestId != before.RequestId ||
            dispatch.ObservationId != before.Id || dispatch.Alias != plan.Alias)
            throw new InvalidOperationException("The verification request does not match a dispatched launch.");
        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !Visible(foreground)) return new ComputerVerification(false, null, "Waiting for the requested app window.");
        var selection = WindowSelection.Capture(foreground);
        if (dispatch.Alias == "calculator" && (calculatorTarget is null || foreground != calculatorTarget.Root.Window))
            return new ComputerVerification(false, null, "The requested app window changed during verification.");
        if (dispatch.Alias == "calculator" && plan.Start.IsPackaged && selection.App.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) {
            var frame = await BindCalculatorFrameAsync(plan.Start, foreground, ct);
            ct.ThrowIfCancellationRequested(); EnsureEnabled();
            if (frame is null) return new ComputerVerification(false, null, "The Calculator frame and signed app process could not be bound to this foreground window.");
            if (calculatorTarget?.Frame != frame) return new ComputerVerification(false, null, "The requested app window changed during verification.");
            return new ComputerVerification(true, new(before.RequestId, Guid.NewGuid(), DateTimeOffset.UtcNow, frame.Child, Frame: frame), "Requested Calculator frame and app process verified.");
        }
        if (!BoundedComputerUse.MatchesAppName(dispatch.Alias, selection.App)) return new ComputerVerification(false, null, "The foreground app does not match the request.");
        CheckWindow(selection); CheckBlocked(dispatch.Alias);
        string path = ProcessPath(selection.ProcessId);
        bool matches = plan.Start.IsPackaged
            ? InstalledAppResolver.MatchesRegisteredProcess(plan.Start, path, InstalledAppResolver.ProcessPackageFullName(selection.ProcessId))
            : dispatch.Alias is "comet" or "spotify"
                ? path.Equals(plan.Start.Executable, StringComparison.OrdinalIgnoreCase)
                : MatchesBuiltin(dispatch.Alias, path, plan.Start.Executable);
        if (!matches) return new ComputerVerification(false, null, "The app executable identity could not be verified.");
        if (dispatch.Alias is "calculator" or "comet" or "camera" or "spotify") {
            using var executable = plan.Start.IsPackaged ? null : ExecutableLease.Acquire(path, ct);
            var installed = await ResolveAsync(dispatch.Alias, ct);
            if (!installed.SameIdentity(plan.Start)) return new ComputerVerification(false, null, "The installed app identity changed after launch.");
            if (plan.Start.IsPackaged && !InstalledAppResolver.MatchesRegisteredProcess(installed, path, InstalledAppResolver.ProcessPackageFullName(selection.ProcessId)))
                return new ComputerVerification(false, null, "The foreground process is not the same signed package's main application.");
        }
        selection.Validate(); ct.ThrowIfCancellationRequested();
        if (dispatch.Alias == "calculator" && (calculatorTarget?.Frame is not null || Identity(selection) != calculatorTarget?.App))
            return new ComputerVerification(false, null, "The requested app window changed during verification.");
        if (Native.GetForegroundWindow() != foreground || !Visible(foreground)) return new ComputerVerification(false, null, "The requested app window changed during verification.");
        return new ComputerVerification(true, new(before.RequestId, Guid.NewGuid(), DateTimeOffset.UtcNow, Identity(selection)), "Requested foreground app verified.");
    }, ct);

    // Read-only consumer boundary. Never silently adopts a new package version,
    // root, child, PID/thread or process creation time from a previous result.
    internal static Task<bool> ValidateFrameAsync(ComputerFrameBinding frame, Func<bool> agentEnabled,
        Func<string> blockedApps, CancellationToken ct) => Task.Run(async () => {
        // Native trust calls cannot be hard-cancelled. Keep them off the UI
        // dispatcher, and await this entire operation's settlement before the
        // owning action gate is released; do not abandon it with WaitAsync.
        ct.ThrowIfCancellationRequested();
        var backend = new WindowsRoutineAppBackend(agentEnabled, blockedApps);
        backend.EnsureEnabled();
        var expected = await ResolveAsync("calculator", ct);
        ct.ThrowIfCancellationRequested(); backend.EnsureEnabled();
        if (!AppFrameBinding.MatchesPackage(frame, expected)) return false;
        var fresh = await backend.BindCalculatorFrameAsync(expected, frame.Host.Window, ct);
        ct.ThrowIfCancellationRequested(); backend.EnsureEnabled();
        return fresh is not null && AppFrameBinding.Same(frame, fresh);
    }, ct);

    // Diagnostic entry for one caller-supplied, already known frame HWND. It
    // resolves and observes metadata only; it cannot dispatch or create a receipt.
    internal async Task<ComputerFrameBinding?> ReadOnlyCalculatorFrameAsync(IntPtr knownRoot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); EnsureEnabled();
        var expected = await ResolveAsync("calculator", ct);
        return await BindCalculatorFrameAsync(expected, knownRoot, ct);
    }

    private async Task<ComputerFrameBinding?> BindCalculatorFrameAsync(VerifiedAppLaunch expected, IntPtr root, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); EnsureEnabled(); CheckBlocked("calculator");
        if (root == IntPtr.Zero || Native.GetForegroundWindow() != root) return null;
        var first = CaptureFrame(root, ct);
        // The lease verifies hash membership, Windows chain/publisher and pins
        // exact host bytes until after the final frame/child/package checks.
        using var hostLease = FixedFrameHostTrust.Acquire(first.Host.Executable, ct);
        first = first with { TrustedHost = true };
        var binding = AppFrameBinding.Bind(expected, first, FixedFrameHostTrust.ExpectedPath, DateTimeOffset.UtcNow);
        if (binding is null) return null;
        var current = await ResolveAsync("calculator", ct);
        ct.ThrowIfCancellationRequested(); EnsureEnabled();
        if (!current.SameIdentity(expected)) return null;
        var second = CaptureFrame(root, ct) with { TrustedHost = true };
        var refreshed = AppFrameBinding.Confirm(expected, first, second, FixedFrameHostTrust.ExpectedPath, DateTimeOffset.UtcNow, ct);
        ct.ThrowIfCancellationRequested(); EnsureEnabled(); CheckBlocked("calculator");
        return refreshed is not null &&
            Native.GetForegroundWindow() == root ? refreshed : null;
    }

    private AppFrameSample CaptureFrame(IntPtr root, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); EnsureEnabled();
        var started = DateTimeOffset.UtcNow;
        var host = FrameNode(root, false, ct);
        var children = new List<AppFrameNode>(); var seen = new HashSet<IntPtr>();
        IntPtr child = FrameGetWindow(root, 5); // GW_CHILD, then direct siblings only.
        bool complete = true;
        while (child != IntPtr.Zero) {
            ct.ThrowIfCancellationRequested();
            if (seen.Count == AppFrameBinding.MaximumChildren || !seen.Add(child)) { complete = false; break; }
            string kind = FrameClass(child);
            if (kind == "Windows.UI.Core.CoreWindow") children.Add(FrameNode(child, true, ct));
            child = FrameGetWindow(child, 2); // GW_HWNDNEXT; never descend or scan top-level windows.
        }
        var sample = new AppFrameSample(started, Native.GetForegroundWindow(), host, children.AsReadOnly(), complete, false);
        // Re-read full host metadata after sibling inspection, then the child
        // identities/topology, then the host again. A live process alone does
        // not establish that its window stayed visible, direct and unchanged.
        complete = AppFrameBinding.Recheck(sample, (window, packaged) => FrameNode(window, packaged, ct), ct);
        ct.ThrowIfCancellationRequested();
        return sample with { Foreground = Native.GetForegroundWindow(), Complete = complete };
    }

    private AppFrameNode FrameNode(IntPtr window, bool packaged, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var selected = WindowSelection.Capture(window); CheckWindow(selected);
        string path = ProcessPath(selected.ProcessId);
        string package = packaged ? InstalledAppResolver.ProcessPackageFullName(selected.ProcessId) : "";
        bool visible = Visible(window);
        bool cloakKnown = DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0;
        string kind = FrameClass(window);
        IntPtr parent = FrameGetParent(window), root = FrameGetAncestor(window, 2);
        selected.Validate(); ct.ThrowIfCancellationRequested();
        // Identity and relationship are independent: a still-live child can be
        // reparented without changing PID/start time. Re-read the relationship.
        if (FrameGetParent(window) != parent || FrameGetAncestor(window, 2) != root || FrameClass(window) != kind ||
            !Visible(window) || DwmGetWindowAttribute(window, 14, out int finalCloak, sizeof(int)) != 0 || finalCloak != cloaked)
            visible = false;
        return new(Identity(selected), parent, root, kind, visible && cloakKnown, cloaked != 0, true, path, package);
    }
    private static string FrameClass(IntPtr window)
    {
        var text = new StringBuilder(128);
        return FrameGetClassName(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }
    [DllImport("user32.dll", EntryPoint = "GetWindow", ExactSpelling = true)] private static extern IntPtr FrameGetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetParent", ExactSpelling = true)] private static extern IntPtr FrameGetParent(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetAncestor", ExactSpelling = true)] private static extern IntPtr FrameGetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int FrameGetClassName(IntPtr window, StringBuilder value, int maximum);
    [DllImport("dwmapi.dll", ExactSpelling = true)] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);

    private (ComputerObservation Observation, WindowSelection Selection) Observe(Guid requestId, Guid observationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IntPtr window = Native.GetForegroundWindow();
        var selection = WindowSelection.Capture(window); CheckWindow(selection);
        if (!Visible(window) || Native.GetForegroundWindow() != window) throw new InvalidOperationException("No stable visible foreground window was observed; focus the intended window and retry.");
        selection.Validate(); ct.ThrowIfCancellationRequested();
        return (new(requestId, observationId, DateTimeOffset.UtcNow, Identity(selection)), selection);
    }
    private void CheckWindow(WindowSelection selection)
    {
        InputNative.CheckDesktopAndElevation(selection.Window);
        Native.CheckWindow(selection.Window);
        CheckBlocked(selection.App);
    }
    private void CheckBlocked(string app)
    {
        if ((blockedApps() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(blocked => app.Contains(blocked, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This app is in your privacy blocklist; no automatic launch or inspection is available.");
    }
    private void EnsureEnabled() { if (!agentEnabled()) throw new InvalidOperationException("Agent was disabled; the app operation stopped."); }
    private static ComputerWindowIdentity Identity(WindowSelection value) => new(value.Window, value.ProcessId, value.ThreadId, value.ProcessStarted, value.App);
    private static bool Visible(IntPtr window) => IsWindowVisible(window) && !IsIconic(window) && GetWindowRect(window, out var r) && r.Right > r.Left && r.Bottom > r.Top;

    private static async Task<VerifiedAppLaunch> ResolveAsync(string alias, CancellationToken ct)
    {
        // Calculator's System32 launcher may be catalog-signed rather than
        // embedded-signed. Use its exact signed OS package registration, with the
        // same package/main-process postconditions as the other packaged apps.
        // There is no weakened signature fallback or direct payload execution.
        if (alias is "calculator" or "camera" or "spotify") return await InstalledAppResolver.ResolveAdditionalAsync(alias, ct);
        if (alias == "comet") {
            var comet = InstalledAppResolver.CometStartInfo(); return new(alias, comet.FileName, comet);
        }
        string path = alias switch {
            "notepad" => Path.Combine(Environment.SystemDirectory, "notepad.exe"),
            "explorer" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            _ => throw new InvalidOperationException("Unsupported routine app.")
        };
        VerifyMicrosoftFile(path);
        return new(alias, path, new(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! });
    }
    private static bool MatchesBuiltin(string alias, string actual, string launchPath)
    {
        if (actual.Equals(launchPath, StringComparison.OrdinalIgnoreCase)) { VerifyMicrosoftFile(actual); return true; }
        // Windows 11's signed Notepad launcher can hand off to installed packaged
        // Notepad. Calculator uses its separately verified package registration.
        string family = alias == "notepad" ? "Microsoft.WindowsNotepad_" : "";
        if (family.Length == 0) return false;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
        if (!actual.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        string relative = actual[root.Length..]; int separator = relative.IndexOf(Path.DirectorySeparatorChar);
        if (separator <= 0 || !relative[..separator].StartsWith(family, StringComparison.OrdinalIgnoreCase) ||
            !BoundedComputerUse.MatchesAppName(alias, Path.GetFileNameWithoutExtension(actual))) return false;
        VerifyMicrosoftFile(actual); return true;
    }
    private static void VerifyMicrosoftFile(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new InvalidOperationException("The supported Windows app was not found at its fixed installed location.");
        for (FileSystemInfo? entry = new FileInfo(path); entry is not null; entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The app path contains a link; launch or verification refused.");
        if (!TrustedSignature(path)) throw new InvalidOperationException("Windows app publisher verification failed.");
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        string signer = certificate.GetNameInfo(X509NameType.SimpleName, false);
        if (signer is not ("Microsoft Windows" or "Microsoft Corporation" or "Microsoft Windows Publisher"))
            throw new InvalidOperationException("The Windows app has an unexpected publisher.");
    }
    private static string ProcessPath(uint pid)
    {
        IntPtr process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) throw new InvalidOperationException("The app executable could not be inspected.");
        try {
            var text = new StringBuilder(32_768); uint length = (uint)text.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, text, ref length)) throw new InvalidOperationException("The app executable identity could not be checked.");
            return text.ToString();
        } finally { CloseHandle(process); }
    }

    // Pinning protects the check/open interval as well as the interval between
    // publisher validation and CreateProcess. No caller supplies an executable path.
    internal sealed class ExecutableLease : IDisposable
    {
        private readonly List<SafeFileHandle> handles = [];
        internal static ExecutableLease Acquire(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var lease = new ExecutableLease();
            try {
                if (path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path[3..].Contains(':'))
                    throw new InvalidOperationException("The verified app must use an ordinary local drive path.");
                string root = path[..3];
                if (GetDriveTypeW(root) is not (2 or 3 or 5 or 6)) throw new InvalidOperationException("The app must be installed on a local drive.");
                string[] parts = path[3..].Split('\\'); string current = root; uint? volume = null;
                for (int i = -1; i < parts.Length; i++) {
                    ct.ThrowIfCancellationRequested();
                    if (i >= 0) {
                        if (parts[i].Length == 0 || parts[i] is "." or "..") throw new InvalidOperationException("Unexpected app path component.");
                        current = Path.Combine(current, parts[i]);
                    }
                    bool directory = i < parts.Length - 1;
                    var handle = CreateFileW("\\\\?\\" + current, directory ? 0x80u : 0x80000000u, 1, IntPtr.Zero, 3,
                        0x00200000u | (directory ? 0x02000000u : 0), IntPtr.Zero);
                    lease.handles.Add(handle);
                    if (handle.IsInvalid || GetFileType(handle) != 1 || !GetFileInformationByHandle(handle, out var info) ||
                        (info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory)
                        throw new InvalidOperationException("The installed app path could not be locked and verified without following links.");
                    volume ??= info.VolumeSerial;
                    if (volume != info.VolumeSerial) throw new InvalidOperationException("The app path changed volumes during verification.");
                    var resolved = new StringBuilder(32_768);
                    uint length = GetFinalPathNameByHandleW(handle, resolved, (uint)resolved.Capacity, 0);
                    if (length == 0 || length >= resolved.Capacity || !resolved.ToString().Equals("\\\\?\\" + current, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The installed app resolved through an unexpected path alias.");
                }
                ct.ThrowIfCancellationRequested(); return lease;
            } catch { lease.Dispose(); throw; }
        }
        public void Dispose() { for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); handles.Clear(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct HandleInformation {
        internal uint Attributes; internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        internal uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInformation info);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(SafeFileHandle file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint GetDriveTypeW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);

    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rectangle rect);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileTrust { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData { public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File; public uint StateAction; public IntPtr State, Url; public uint Flags, Context; }
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    private static bool TrustedSignature(string path)
    {
        var file = new FileTrust { Size = (uint)Marshal.SizeOf<FileTrust>(), Path = path };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileTrust>()); Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer, StateAction = 1, Flags = 0x1000 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try { return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0; }
        finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); Marshal.DestroyStructure<FileTrust>(pointer); Marshal.FreeHGlobal(pointer); }
    }
}

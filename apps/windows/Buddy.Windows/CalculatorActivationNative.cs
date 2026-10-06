using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Buddy.Windows;

internal sealed partial class WindowsRoutineAppBackend
{
    private async Task<CalculatorWindowTarget> ActivateCalculatorAsync(VerifiedAppLaunch launch, WindowSelection source, CancellationToken ct)
    {
        using var activation = new CalculatorActivationSession(this, launch, source);
        activation.Activate(ct);
        return await new CalculatorWindowActivation(activation).RunAsync(ct);
    }

    // One owned operation: the OS activation receipt selects a process; bounded
    // metadata observations select exactly one of its windows. No process-name
    // search, old foreground receipt, second launch or focus workaround is used.
    private sealed class CalculatorActivationSession(WindowsRoutineAppBackend owner, VerifiedAppLaunch launch,
        WindowSelection source) : ICalculatorWindowActivation, IDisposable
    {
        private uint? inputTick;
        private bool invalid;
        private uint pid;
        private long started;
        private IntPtr process;
        private IDisposable? hostLease;

        internal void Activate(CancellationToken ct)
        {
            inputTick = InputTick();
            Guard(null, ct);
            launch.RequireRegistration();
            if (launch.Alias != "calculator" || launch.AppUserModelId != "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App") throw Refused();
            // No await between COM initialization and teardown: both belong to
            // this worker thread. A pending native call retains operation ownership.
            int initialized = CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
            if (initialized < 0) throw Refused();
            ICalculatorActivationManager? manager = null;
            try {
                var clsid = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
                var iid = typeof(ICalculatorActivationManager).GUID;
                if (CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out manager) < 0 || manager is null) throw Refused();
                Guard(null, ct);
                if (Native.GetForegroundWindow() != source.Window) throw Refused();
                // COM server setup may take time. It cannot extend the original
                // launch checkpoint; recheck its age at the actual effect boundary.
                if (owner.prepared is not { } plan) throw Refused();
                var age = DateTimeOffset.UtcNow - plan.Checkpoint.At;
                if (age < TimeSpan.Zero || age > ComputerUseLimits.Default.ObservationAge) throw Refused();
                ct.ThrowIfCancellationRequested();
                // AO_NOERRORUI. No arguments and no alternate launcher on failure.
                if (manager.ActivateApplication(launch.AppUserModelId, null, 2, out pid) < 0 || pid == 0) throw Refused();
                Guard(null, ct);
                process = OpenProcess(0x1000 | 0x00100000, false, pid); // query + synchronize only
                if (process == IntPtr.Zero || !ActivationGetProcessTimes(process, out started, out _, out _, out _) || started <= 0) throw Refused();
                CheckProcess(); Guard(null, ct);
            } finally {
                if (manager is not null) Marshal.ReleaseComObject(manager);
                CoUninitialize();
            }
        }

        public void Guard(CalculatorWindowTarget? target, CancellationToken ct)
        {
            try {
                ct.ThrowIfCancellationRequested();
                if (invalid || inputTick is null || InputTick() != inputTick) throw Refused();
                owner.EnsureEnabled(); owner.CheckBlocked("calculator");
                source.Validate(); owner.CheckWindow(source);
                if (process != IntPtr.Zero) CheckProcess();
                if (target is not null) {
                    var foreground = Native.GetForegroundWindow();
                    if (foreground != source.Window && foreground != target.Root.Window) throw Refused();
                    CheckIdentity(target.Root); CheckIdentity(target.App);
                    if (FrameGetAncestor(target.Root.Window, 2) != target.Root.Window || FrameGetParent(target.Root.Window) != IntPtr.Zero ||
                        target.Frame is not null && (FrameGetParent(target.App.Window) != target.Root.Window || FrameGetAncestor(target.App.Window, 2) != target.Root.Window)) throw Refused();
                }
                ct.ThrowIfCancellationRequested();
                if (InputTick() != inputTick) throw Refused();
            } catch { invalid = true; throw; }
        }

        public async Task<CalculatorWindowView?> ObserveAsync(CalculatorWindowTarget? expected, CancellationToken ct)
        {
            Guard(expected, ct);
            var fresh = await ResolveAsync("calculator", ct);
            Guard(expected, ct);
            if (!launch.SameIdentity(fresh)) throw Refused();
            var candidates = Candidates(ct);
            if (candidates.Count == 0) return null;
            if (candidates.Count != 1) throw Refused();
            IntPtr root = candidates[0];
            if (FrameClass(root) == "ApplicationFrameWindow" && hostLease is null)
                hostLease = FixedFrameHostTrust.Acquire(ProcessPath(WindowSelection.Capture(root).ProcessId), ct);
            Guard(expected, ct);
            var first = Capture(root, ct);
            var target = CalculatorTargetBinding.Bind(launch, pid, started, first, FixedFrameHostTrust.ExpectedPath, DateTimeOffset.UtcNow);
            if (target is null && expected is null) return null; // not ready yet; no restoration authority
            if (target is null || expected is not null && target != expected) throw Refused();
            var second = Capture(root, ct);
            var confirmed = CalculatorTargetBinding.Bind(launch, pid, started, second, FixedFrameHostTrust.ExpectedPath, DateTimeOffset.UtcNow);
            if (target != confirmed || first.Root != second.Root || !first.Children.SequenceEqual(second.Children) || first.Minimized != second.Minimized) throw Refused();
            Guard(target, ct);
            return new(target, second.Minimized, !second.Minimized && Visible(root), Native.GetForegroundWindow() == root);
        }

        public void Restore(CalculatorWindowTarget target, CancellationToken ct)
        {
            BeforeEffect(target, ct);
            if (!IsIconic(target.Root.Window)) return; // already restored; never minimize or toggle
            Guard(target, ct);
            _ = ActivationShowWindow(target.Root.Window, 9); // SW_RESTORE; BOOL is PRIOR visibility, not success
            Guard(target, ct);
        }
        public bool Foreground(CalculatorWindowTarget target, CancellationToken ct)
        {
            BeforeEffect(target, ct);
            if (IsIconic(target.Root.Window) || !Visible(target.Root.Window)) throw Refused();
            Guard(target, ct);
            bool accepted = InputNative.SetForegroundWindow(target.Root.Window);
            Guard(target, ct);
            return accepted;
        }
        private void BeforeEffect(CalculatorWindowTarget target, CancellationToken ct)
        {
            Guard(target, ct); launch.RequireRegistration();
            var sample = Capture(target.Root.Window, ct);
            if (CalculatorTargetBinding.Bind(launch, pid, started, sample, FixedFrameHostTrust.ExpectedPath, DateTimeOffset.UtcNow) != target) throw Refused();
            Guard(target, ct);
        }
        private void CheckIdentity(ComputerWindowIdentity identity)
        {
            var selection = WindowSelection.Capture(identity.Window);
            if (Identity(selection) != identity) throw Refused();
            owner.CheckWindow(selection);
        }
        private void CheckProcess()
        {
            var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
            if (ActivationWaitForSingleObject(process, 0) != 258 ||
                !ActivationGetProcessTimes(process, out long creation, out _, out _, out _) || creation != started ||
                !QueryFullProcessImageNameW(process, 0, path, ref length) ||
                !InstalledAppResolver.MatchesRegisteredProcess(launch, path.ToString(), InstalledAppResolver.ProcessPackageFullName(pid))) throw Refused();
        }
        private List<IntPtr> Candidates(CancellationToken ct)
        {
            var found = new List<IntPtr>(); int count = 0; Exception? failure = null;
            var clock = Stopwatch.StartNew();
            bool complete = ActivationEnumWindows((window, unused) => {
                try {
                    ct.ThrowIfCancellationRequested();
                    if (++count > 256 || clock.Elapsed > TimeSpan.FromSeconds(1)) throw Refused();
                    _ = ActivationGetWindowThreadProcessId(window, out uint rootPid);
                    bool candidate = rootPid == pid;
                    if (!candidate && FrameClass(window) == "ApplicationFrameWindow")
                        foreach (var child in CoreChildren(window, ct)) {
                            _ = ActivationGetWindowThreadProcessId(child, out uint childPid);
                            candidate |= childPid == pid;
                        }
                    if (candidate) found.Add(window);
                    return true;
                } catch (Exception ex) { failure = ex; return false; }
            }, IntPtr.Zero);
            if (failure is not null) throw failure;
            if (!complete) throw Refused();
            Guard(null, ct); return found;
        }
        private static List<IntPtr> CoreChildren(IntPtr root, CancellationToken ct)
        {
            var result = new List<IntPtr>(); var seen = new HashSet<IntPtr>();
            for (IntPtr child = FrameGetWindow(root, 5); child != IntPtr.Zero; child = FrameGetWindow(child, 2)) {
                ct.ThrowIfCancellationRequested();
                if (seen.Count == AppFrameBinding.MaximumChildren || !seen.Add(child)) throw Refused();
                if (FrameClass(child) == "Windows.UI.Core.CoreWindow") result.Add(child);
            }
            return result;
        }
        private CalculatorActivationSample Capture(IntPtr root, CancellationToken ct)
        {
            var at = DateTimeOffset.UtcNow; bool minimized = IsIconic(root);
            bool framed = FrameClass(root) == "ApplicationFrameWindow";
            var host = Node(root, !framed, ct);
            var handles = CoreChildren(root, ct);
            var children = handles.Select(child => Node(child, true, ct)).ToArray();
            var sample = new CalculatorActivationSample(at, host, children, true, hostLease is not null, minimized);
            bool complete = CalculatorTargetBinding.Recheck(sample, (window, packaged) => Node(window, packaged, ct),
                window => CoreChildren(window, ct), IsIconic, ct);
            return sample with { Complete = complete };
        }
        private CalculatorActivationNode Node(IntPtr window, bool packaged, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var selection = WindowSelection.Capture(window); owner.CheckWindow(selection);
            string path = ProcessPath(selection.ProcessId);
            string package = packaged ? InstalledAppResolver.ProcessPackageFullName(selection.ProcessId) : "";
            string kind = FrameClass(window); IntPtr parent = FrameGetParent(window), root = FrameGetAncestor(window, 2);
            bool visible = IsWindowVisible(window);
            bool known = DwmGetWindowAttribute(window, 14, out int cloak, sizeof(int)) == 0;
            selection.Validate(); ct.ThrowIfCancellationRequested();
            bool stable = FrameGetParent(window) == parent && FrameGetAncestor(window, 2) == root && FrameClass(window) == kind &&
                IsWindowVisible(window) == visible && DwmGetWindowAttribute(window, 14, out int finalCloak, sizeof(int)) == 0 && finalCloak == cloak;
            return new(new(Identity(selection), parent, root, kind, visible, cloak != 0, stable, path, package), known);
        }
        private static uint InputTick()
        {
            var info = new ActivationLastInput { Size = (uint)Marshal.SizeOf<ActivationLastInput>() };
            if (!ActivationGetLastInputInfo(ref info)) throw Refused();
            return info.Tick;
        }
        private static InvalidOperationException Refused() => new("Calculator activation could not be safely completed.");
        public void Dispose() { hostLease?.Dispose(); if (process != IntPtr.Zero) { CloseHandle(process); process = IntPtr.Zero; } }
    }

    // IID/CLSID and vtable from Microsoft's shobjidl_core.h. Only the first
    // application activation method is exposed; file/protocol activation is absent.
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICalculatorActivationManager {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
    }
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out ICalculatorActivationManager? manager);
    private delegate bool ActivationEnumCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "EnumWindows")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ActivationEnumWindows(ActivationEnumCallback callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint ActivationGetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", EntryPoint = "ShowWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ActivationShowWindow(IntPtr window, int command);
    [StructLayout(LayoutKind.Sequential)] private struct ActivationLastInput { internal uint Size, Tick; }
    [DllImport("user32.dll", EntryPoint = "GetLastInputInfo")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ActivationGetLastInputInfo(ref ActivationLastInput info);
    [DllImport("kernel32.dll", EntryPoint = "GetProcessTimes")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ActivationGetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject")] private static extern uint ActivationWaitForSingleObject(IntPtr handle, uint milliseconds);
}

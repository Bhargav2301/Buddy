using Buddy.Windows;

internal static class CalculatorTargetBindingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        const string hostPath = @"C:\Windows\System32\ApplicationFrameHost.exe";
        const string rootPath = @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2607.0.0_x64__8wekyb3d8bbwe";
        var launch = new VerifiedAppLaunch("calculator", rootPath + @"\CalculatorApp.exe",
            PackageFullName: "Microsoft.WindowsCalculator_11.2607.0.0_x64__8wekyb3d8bbwe",
            AppUserModelId: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", PackageRoot: rootPath, MainExecutable: rootPath + @"\CalculatorApp.exe");
        var host = new CalculatorActivationNode(new(new(new(10), 20, 30, 40, "ApplicationFrameHost"), IntPtr.Zero, new(10), "ApplicationFrameWindow", true, false, true, hostPath, ""), true);
        var child = new CalculatorActivationNode(new(new(new(11), 21, 31, 41, "CalculatorApp"), new(10), new(10), "Windows.UI.Core.CoreWindow", false, true, true, launch.MainExecutable, launch.PackageFullName), true);
        var sample = new CalculatorActivationSample(now, host, [child], true, true, true);
        CalculatorWindowTarget? Bind(CalculatorActivationSample value, uint pid = 21, long start = 41) => CalculatorTargetBinding.Bind(launch, pid, start, value, hostPath, now);
        var target = Bind(sample);
        check(target is { Frame: not null } && target.Root == host.Node.Identity && target.App == child.Node.Identity, "Minimized frame permits identity-only binding of hidden/cloaked exact activated child");
        check(AppFrameBinding.Bind(launch, new(now, host.Node.Identity.Window, host.Node, [child.Node], true, true), hostPath, now) is null,
            "Minimized restoration identity never relaxes strict foreground success binding");
        check(Bind(sample, 22) is null && Bind(sample, 21, 42) is null, "OS returned PID and held process creation time are mandatory");
        foreach (var altered in new[] {
            sample with { Complete = false }, sample with { TrustedHost = false }, sample with { Minimized = false },
            sample with { At = now.AddSeconds(1) }, sample with { At = now.AddSeconds(-6) },
            sample with { Children = [] }, sample with { Children = [child, child] },
            sample with { Root = host with { CloakKnown = false } },
            sample with { Root = host with { Node = host.Node with { Visible = false } } },
            sample with { Root = host with { Node = host.Node with { Cloaked = true } } },
            sample with { Root = host with { Node = host.Node with { AccessAllowed = false } } },
            sample with { Root = host with { Node = host.Node with { Executable = @"C:\other\ApplicationFrameHost.exe" } } },
            sample with { Root = host with { Node = host.Node with { Parent = new(1) } } },
            sample with { Root = host with { Node = host.Node with { Root = new(1) } } },
            sample with { Root = host with { Node = host.Node with { ClassName = "OtherFrame" } } },
            sample with { Children = [child with { CloakKnown = false }] },
            sample with { Children = [child with { Node = child.Node with { AccessAllowed = false } }] },
            sample with { Children = [child with { Node = child.Node with { Parent = new(5) } }] },
            sample with { Children = [child with { Node = child.Node with { Root = new(5) } }] },
            sample with { Children = [child with { Node = child.Node with { PackageFullName = "OtherPackage" } }] },
            sample with { Children = [child with { Node = child.Node with { Executable = rootPath + @"\Other.exe" } }] },
            sample with { Children = [child with { Node = child.Node with { ClassName = "OtherWindow" } }] },
            sample with { Children = [child with { Node = child.Node with { Identity = child.Node.Identity with { ProcessId = 22 } } }] },
            sample with { Children = [child with { Node = child.Node with { Identity = child.Node.Identity with { ProcessStarted = 42 } } }] },
            sample with { Children = [child with { Node = child.Node with { Identity = child.Node.Identity with { ThreadId = 0 } } }] },
            sample with { Children = [child with { Node = child.Node with { Identity = child.Node.Identity with { Window = IntPtr.Zero } } }] }
        }) check(Bind(altered) is null, "Restoration refuses incomplete, stale, cloaked-root, ambiguous or replaced identity evidence");
        var restoredChild = child with { Node = child.Node with { Visible = true, Cloaked = false } };
        check(Bind(sample with { Minimized = false, Children = [restoredChild] }) == target, "Same exact identity survives ordinary restore visibility change");
        var hiddenSibling = child with { Node = child.Node with { Identity = child.Node.Identity with { Window = new(12), ProcessId = 22 } } };
        check(Bind(sample with { Children = [restoredChild, hiddenSibling] }) is null, "Hidden foreign CoreWindow sibling remains ambiguous");
        var direct = restoredChild with { Node = restoredChild.Node with { Parent = IntPtr.Zero, Root = restoredChild.Node.Identity.Window } };
        var directSample = sample with { Root = direct, Children = [], TrustedHost = false, Minimized = false };
        check(Bind(directSample) is { Frame: null } directTarget && directTarget.Root == directTarget.App, "New direct Calculator window binds only to activation receipt process");
        check(Bind(directSample with { Children = [child] }) is null, "Direct Calculator does not silently adopt frame children");
        check(CalculatorTargetBinding.Bind(launch with { Alias = "camera" }, 21, 41, sample, hostPath, now) is null,
            "Restoration capability cannot be reused for another app alias");
        foreach (string mutation in new[] { "none", "cloak", "hidden", "parent", "root", "minimized", "sibling", "child" }) {
            var currentRoot = host; var currentChild = child; bool currentMinimized = true;
            IReadOnlyList<IntPtr> inventory = [child.Node.Identity.Window];
            CalculatorActivationNode Read(IntPtr window, bool packaged) {
                if (window == host.Node.Identity.Window) return currentRoot;
                var result = currentChild;
                switch (mutation) {
                    case "cloak": currentRoot = host with { Node = host.Node with { Cloaked = true } }; break;
                    case "hidden": currentRoot = host with { Node = host.Node with { Visible = false } }; break;
                    case "parent": currentRoot = host with { Node = host.Node with { Parent = new(99) } }; break;
                    case "root": currentRoot = host with { Node = host.Node with { Root = new(99) } }; break;
                    case "minimized": currentMinimized = false; break;
                    case "sibling": inventory = [child.Node.Identity.Window, new(99)]; break;
                    case "child": result = child with { Node = child.Node with { Parent = new(99) } }; break;
                }
                return result;
            }
            bool valid = CalculatorTargetBinding.Recheck(sample, Read, _ => inventory, _ => currentMinimized, default);
            check(valid == (mutation == "none"), "Within-sample restoration recheck catches change during final child read: " + mutation);
        }
    }
}

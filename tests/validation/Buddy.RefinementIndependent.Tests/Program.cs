using Buddy.Server;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static partial class Program
{
    private static int assertions, cases;
    private static readonly List<string> Failures = [];
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--assess", var path]) return CorpusReview.Assess(path);
        if (args is ["--regressions"]) { PrintSources(); await RegressionCases(); return Summary(); }
        if (args.Length != 0) throw new ArgumentException("No arguments runs isolated injected tests. --assess reads canned receipts only. There is no live mode.");
        PrintSources();
        await ServiceCases(); await RequestCases(); FieldCases(); BudgetCases(); CorpusReview.SelfTests(Check);
        return Summary();
    }
    private static int Summary()
    {
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", cases, assertions, failures = Failures,
            actualModels = false, native = false, network = false, installedProfile = false, fieldWrites = "synthetic adapters only" }));
        return Failures.Count == 0 ? 0 : 1;
    }
    private static void Check(bool value, string label) { assertions++; if (!value) throw new InvalidOperationException(label); }
    private static async Task Case(string name, Func<Task> body)
    {
        cases++;
        try { await body().WaitAsync(TimeSpan.FromSeconds(12)); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Failures.Add(name + ": " + ex.GetType().Name + ": " + ex.Message); Console.WriteLine("FAIL " + Failures[^1]); }
    }
    private static void SyncCase(string name, Action body) => Case(name, () => { body(); return Task.CompletedTask; }).GetAwaiter().GetResult();
    private static async Task<T> Throws<T>(Func<Task> body) where T : Exception
    { try { await body(); } catch (T ex) { assertions++; return ex; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void PrintSources()
    {
        var root = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "BuddySourceRoot").Value!;
        var files = Directory.GetFiles(Path.Combine(root, "services", "Buddy.Server"), "*.cs")
            .Concat(new[] { "GuardedEdit.cs", "RefinementRequest.cs" }.Select(f => Path.Combine(root, "apps", "windows", "Buddy.Windows", f)))
            .Order(StringComparer.Ordinal).Select(p => new { path = Path.GetRelativePath(root, p), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant() });
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "source_receipt", root, sourceFiles = files, scope = "linked production source; not exact installed DLL or live app acceptance" }));
    }
}

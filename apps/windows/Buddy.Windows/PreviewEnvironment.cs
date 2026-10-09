namespace Buddy.Windows;

// A preview owns separate data, activation and listener namespaces. It cannot install itself.
internal static class PreviewEnvironment
{
    internal static bool Enabled { get; private set; }
    internal static void Configure(string[] args) => Enabled = args.Contains("--preview") || File.Exists(Path.Combine(AppContext.BaseDirectory, ".buddy-preview"));
    internal static string DataDirectory => Enabled
        ? Path.Combine(AppContext.BaseDirectory, "preview-data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buddy");
    internal static string Suffix => Enabled ? ".Preview" : "";
}

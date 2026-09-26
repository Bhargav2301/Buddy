using Buddy.Windows;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Buddy.Windows.PackageChecks <extracted Windows package>");
    return 2;
}
try
{
    PackageVerifier.Verify(Path.GetFullPath(args[0]));
    Console.WriteLine("PASS: Windows x64 host/native binaries, WPF implementation types, managed type-reference dependencies, no reference assemblies.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex.Message);
    return 1;
}

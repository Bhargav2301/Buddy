using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Buddy.Windows;

internal static class PackageVerifier
{
    internal static void Verify(string folder)
    {
        string[] required = ["Buddy.exe", "Buddy.dll", "Buddy.Server.dll", "Buddy.runtimeconfig.json", "Buddy.deps.json",
            "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll",
            "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "D3DCompiler_47_cor3.dll", "vcruntime140_cor3.dll", "clrjit.dll",
            "Tesseract.dll", "x64/tesseract50.dll", "x64/leptonica-1.82.0.dll", "tessdata/eng.traineddata", "Assets/Branding/Buddy.png", "Assets/Branding/Buddy.ico"];
        foreach (var name in required) RequireFile(folder, name);
        foreach(var name in new[]{"whisper.dll","ggml-whisper.dll","ggml-base-whisper.dll","ggml-cpu-whisper.dll"}){
            var relative="runtimes/win-x64/"+name;RequireFile(folder,relative);
            using var native=File.OpenRead(Path.Combine(folder,relative));using var header=new PEReader(native);
            if(header.PEHeaders.CoffHeader.Machine!=Machine.Amd64)throw new InvalidDataException("Invalid Whisper x64 runtime: "+name);
        }
        foreach(var name in new[]{"whisper.dll","ggml-whisper.dll","ggml-base-whisper.dll","ggml-cpu-whisper.dll"}){
            var relative="runtimes/win-x64/"+name;RequireFile(folder,relative);
            using var native=File.OpenRead(Path.Combine(folder,relative));using var header=new PEReader(native);
            if(header.PEHeaders.CoffHeader.Machine!=Machine.Amd64)throw new InvalidDataException("Invalid Whisper x64 runtime: "+name);
        }
        foreach (var asset in new[] { "1cced", "2f2b4", "5d71e", "785ba", "91dcc", "9a20f", "a9b00", "ab83a", "fc18a" })
        {
            var name = "Assets/Figma/" + asset + ".svg"; RequireFile(folder, name);
            if (new FileInfo(Path.Combine(folder, name)).Length == 0) throw new InvalidDataException("Empty Buddy design asset: " + name);
        }
        foreach (var name in new[] { "Buddy.exe", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "x64/tesseract50.dll", "x64/leptonica-1.82.0.dll" })
        {
            using var stream = File.OpenRead(Path.Combine(folder, name));
            using var pe = new PEReader(stream);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                throw new InvalidDataException(name + " is not the required Windows x64 binary. Extract the complete updated ZIP.");
        }
        RequireType(folder, "WindowsBase.dll", "System.Windows", "DependencyObject");
        RequireType(folder, "PresentationFramework.dll", "System.Windows", "Application");
        RequireType(folder, "PresentationCore.dll", "System.Windows", "UIElement");
        using (var ocrData = File.OpenRead(Path.Combine(folder, "tessdata", "eng.traineddata")))
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ocrData)) != "7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2")
                throw new InvalidDataException("The bundled OCR model is incomplete or has changed. Extract a verified Buddy package.");

        var pending = new Queue<string>(new[] { "Buddy.dll", "Buddy.Server.dll", "QRCoder.dll", "System.Speech.dll" });
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out var name))
        {
            if (!visited.Add(name)) continue;
            RequireFile(folder, name);
            using var stream = File.OpenRead(Path.Combine(folder, name));
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) throw new InvalidDataException(name + " is not a managed runtime assembly.");
            var metadata = pe.GetMetadataReader();
            // Framework compatibility facades can forward unused legacy types to
            // optional packages. Follow actual type references, not those unused
            // exported-type forwarders (for example legacy SQL client classes).
            var references = new HashSet<AssemblyReferenceHandle>();
            foreach (var handle in metadata.TypeReferences)
            {
                var scope = metadata.GetTypeReference(handle).ResolutionScope;
                if (scope.Kind == HandleKind.AssemblyReference) references.Add((AssemblyReferenceHandle)scope);
            }
            foreach (var handle in references)
            {
                var dependency = metadata.GetString(metadata.GetAssemblyReference(handle).Name) + ".dll";
                RequireFile(folder, dependency);
                pending.Enqueue(dependency);
            }
            foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = metadata.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var parent = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference) continue;
                var type = metadata.GetTypeReference((TypeReferenceHandle)parent);
                if (metadata.StringComparer.Equals(type.Namespace, "System.Runtime.CompilerServices") &&
                    metadata.StringComparer.Equals(type.Name, "ReferenceAssemblyAttribute"))
                    throw new InvalidDataException(name + " is a reference assembly, not a runnable implementation.");
            }
        }
    }

    private static void RequireFile(string folder, string name)
    {
        if (!File.Exists(Path.Combine(folder, name)))
            throw new FileNotFoundException("Missing " + name + ". Choose Extract All on the updated Windows ZIP, then run Buddy from the extracted folder.", name);
    }

    private static void RequireType(string folder, string file, string ns, string name)
    {
        RequireFile(folder, file);
        using var stream = File.OpenRead(Path.Combine(folder, file));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (metadata.StringComparer.Equals(type.Namespace, ns) && metadata.StringComparer.Equals(type.Name, name)) return;
        }
        throw new InvalidDataException(file + " is the wrong Windows runtime component. Download and extract the updated Buddy Windows ZIP.");
    }
}

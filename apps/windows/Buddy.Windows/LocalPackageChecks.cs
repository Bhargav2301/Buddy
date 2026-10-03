using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace Buddy.Windows;

internal static class LocalPackageChecks
{
    internal static void CompareSavedData(string before,string after)
    {
        JsonObject Read(string folder){
            var provider=DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder,"keys")),setup=>{setup.SetApplicationName("Buddy.Local.v1");setup.DisableAutomaticKeyGeneration();});
            var bytes=provider.CreateProtector("Buddy.State.v1").Unprotect(File.ReadAllBytes(Path.Combine(folder,"buddy.v1.encrypted")));
            try{return JsonNode.Parse(bytes)!.AsObject();}finally{CryptographicOperations.ZeroMemory(bytes);}
        }
        var original=Read(before);var current=Read(after);var beforeVersion=original["schemaVersion"]!.GetValue<int>();var afterVersion=current["schemaVersion"]!.GetValue<int>();
        foreach(var name in beforeVersion<3&&afterVersion==3?new[]{"schemaVersion","jobs","knowledge"}:new[]{"schemaVersion"}){original.Remove(name);current.Remove(name);}
        bool preserved=JsonNode.DeepEquals(original,current);
        Console.WriteLine(JsonSerializer.Serialize(new{result=preserved?"passed":"changed",schemaBefore=beforeVersion,schemaAfter=afterVersion,existingFieldsPreserved=preserved,printedPrivateValues=false,wroteData=false}));
        if(!preserved)throw new InvalidDataException("Existing saved fields differ; inspect the retained backup before claiming preservation.");
    }
    internal static async Task Voice()
    {
        using var voice = new NeuralSpeechSynthesizer();
        var text = "I'm ready. I can open Comet Browser after you approve the plan. Press Control plus Shift plus Space whenever you'd like to talk.";
        var audio = await voice.Render(text, 85, -1, default, "f3");
        try {
            var hash = Convert.ToHexString(SHA256.HashData(audio));
            if (hash != "714B6A2A09C80757B4CC5010D59B67B7B4F46E17E467A95BBDE849BC4BB3D06D") throw new InvalidDataException("The installed F3 voice differs from the approved audition.");
            Console.WriteLine(JsonSerializer.Serialize(new { result = "passed", engine = "Piper local CPU", voice = "F3", sha256 = hash, playedAudio = false, savedAudio = false }));
        } finally { Array.Clear(audio); }
    }
    internal static void Branding()
    {
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "Buddy.exe")) ?? throw new InvalidDataException("Executable icon is missing.");
        using var bitmap = icon.ToBitmap(); int mint = 0;
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) { var c = bitmap.GetPixel(x, y); if (c.A > 100 && c.G > c.R * 1.2 && c.G > c.B && c.G > 100) mint++; }
        if (mint < 20) throw new InvalidDataException("Executable does not contain the supplied mint character icon.");
        using var frames = new BinaryReader(File.OpenRead(AppBranding.Asset("Buddy.ico")));
        if (frames.ReadUInt16() != 0 || frames.ReadUInt16() != 1 || frames.ReadUInt16() != 7) throw new InvalidDataException("The multi-size application icon is invalid.");
        Console.WriteLine(JsonSerializer.Serialize(new { result = "passed", embeddedExecutableIcon = true, iconFrames = 7, mintPixels = mint, windowIcon = AppBranding.WindowIcon.Width > 0 }));
    }
}

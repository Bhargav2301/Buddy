using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;

namespace Buddy.Windows;

internal static class TeachingMemory
{
    internal static TeachingMemoryStore Open()=>new(PreviewEnvironment.DataDirectory,
        DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(PreviewEnvironment.DataDirectory,"keys")),b=>b.SetApplicationName("Buddy.Local.v1")));
}

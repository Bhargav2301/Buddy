using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Buddy.Server;

public sealed class StateStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;
    private readonly IDataProtector protector;
    private BuddyState state;
    public StateStore(string directory, IDataProtectionProvider provider)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "buddy.v1.encrypted");
        protector = provider.CreateProtector("Buddy.State.v1");
        // Fail closed on corruption: never silently overwrite a user's history.
        state = File.Exists(path) ? JsonSerializer.Deserialize<BuddyState>(protector.Unprotect(File.ReadAllBytes(path)), Json)! : new();
        if (state is null || state.SchemaVersion > 3) throw new InvalidOperationException("This Buddy data was created by a newer version. Update Buddy before opening it.");
        // Older applications must refuse the new job/knowledge data instead of silently dropping it.
        state.SchemaVersion = 3;
    }
    public async Task<T> Read<T>(Func<BuddyState, T> fn)
    {
        await gate.WaitAsync();
        try { return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(fn(state), Json), Json)!; }
        finally { gate.Release(); }
    }
    public async Task<T> Update<T>(Func<BuddyState, T> fn)
    {
        await gate.WaitAsync();
        try
        {
            var next = JsonSerializer.Deserialize<BuddyState>(JsonSerializer.Serialize(state, Json), Json)!;
            var result = fn(next);
            var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(next, Json));
            var temporary = path + ".new";
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, path, true);
            state = next;
            return result;
        }
        finally { gate.Release(); }
    }
    public Task<string?> Authenticate(string token) => Read(s => Security.Equal(token, s.DesktopToken) ? "desktop" : s.Devices.FirstOrDefault(d => Security.Equal(d.TokenHash, Security.Hash(token)))?.Id);
    public async Task EnsureSaved() => await Update(s => true);
}

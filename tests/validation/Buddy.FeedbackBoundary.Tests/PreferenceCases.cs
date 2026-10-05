using Buddy.Windows;
using System.Text.Json;

internal static class PreferenceCases
{
    internal static async Task Run(Func<string,Func<Task>,Task> test, Action<bool,string> require)
    {
        await test("preferences: stale island writer preserves newer explicit voice choice", () => Owned(path => {
            var original = new DesktopPreferences { VoiceEngine="piper", NeuralSpeakerId=60, IslandMode="Hidden", AdditionalSettings=new(){["futureFlag"]=JsonSerializer.SerializeToElement("preserve")} }; original.Save(path);
            var voiceWindow = DesktopPreferences.Load(path); var islandWindow = DesktopPreferences.Load(path);
            Save(voiceWindow with { NeuralSpeakerId=13, VoiceName="Owned selected voice" },path,voiceWindow);
            var effective=Save(islandWindow with { IslandMode="Expanded" },path,islandWindow);
            var final = DesktopPreferences.Load(path);
            require(final.NeuralSpeakerId==13 && final.VoiceName=="Owned selected voice" && final.IslandMode=="Expanded", "Stale unrelated preferences replaced a newer voice choice.");
            require(final.AdditionalSettings!["futureFlag"].GetString()=="preserve", "Unknown extension settings must survive independent updates.");
            require(effective.NeuralSpeakerId==13 && effective.VoiceName=="Owned selected voice" && effective.IslandMode=="Expanded", "The save result must expose effective merged preferences to refresh the caller.");
            return Task.CompletedTask;
        }));
        await test("preferences: distinct stale extension additions preserve both values", () => Owned(path => {
            new DesktopPreferences().Save(path); var a=DesktopPreferences.Load(path); var b=DesktopPreferences.Load(path);
            Save(a with { AdditionalSettings=new(){["ownedA"]=JsonSerializer.SerializeToElement("A")} },path,a);
            Save(b with { AdditionalSettings=new(){["ownedB"]=JsonSerializer.SerializeToElement("B")} },path,b);
            var final=DesktopPreferences.Load(path);
            require(final.AdditionalSettings is { Count:2 } && final.AdditionalSettings["ownedA"].GetString()=="A" && final.AdditionalSettings["ownedB"].GetString()=="B", "A stale extension dictionary erased another writer's unrelated value.");
            return Task.CompletedTask;
        }));
        await test("preferences: parallel same-path writers have no temporary-file collision or lost independent fields", () => Owned(async path => {
            new DesktopPreferences().Save(path);
            var snapshots=Enumerable.Range(0,8).Select(_=>DesktopPreferences.Load(path)).ToArray();
            var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writers=snapshots.Select((snapshot,i)=>Task.Run(async()=>{
                await start.Task;
                try { Save(snapshot with { AdditionalSettings=new(){["writer"+i]=JsonSerializer.SerializeToElement(i)} },path,snapshot); return (Exception?)null; }
                catch(Exception ex){return ex;}
            })).ToArray();
            start.SetResult(); var errors=await Task.WhenAll(writers).WaitAsync(TimeSpan.FromSeconds(10));
            require(errors.All(e=>e is null), "Concurrent preference writers failed: " + string.Join(",",errors.Where(e=>e is not null).Select(e=>e!.GetType().Name)));
            var final=DesktopPreferences.Load(path);
            require(Enumerable.Range(0,8).All(i=>final.AdditionalSettings?.TryGetValue("writer"+i,out var value)==true && value.GetInt32()==i), "Concurrent unrelated extension changes were lost.");
        }));
        await test("preferences: successive unrelated editor changes preserve a concurrently updated checkbox", () => Owned(path => {
            var displayed = new DesktopPreferences { ReduceMotion = false, NeuralSpeakerId = 85 }; displayed.Save(path);
            var current = Save(displayed with { ReduceMotion = true }, path, displayed);
            var firstControls = displayed with { CompanionName = "First edit" };
            current = Save(DesktopPreferences.ApplyChanges(current, displayed, firstControls), path, current);
            var secondControls = firstControls with { CompanionName = "Second edit" };
            current = Save(DesktopPreferences.ApplyChanges(current, firstControls, secondControls), path, current);
            require(current.ReduceMotion && current.NeuralSpeakerId == 85 && current.CompanionName == "Second edit", "A second unrelated edit reverted a concurrently preserved option.");
            return Task.CompletedTask;
        }));
        foreach(string contents in new[]{"{bad json", "{\"SchemaVersion\":999,\"VoiceName\":\"future\"}"})
        await test("preferences: corrupt or future-version file is preserved", () => Owned(path => {
            File.WriteAllText(path,contents); bool refused=false;
            try { new DesktopPreferences { VoiceName="must not overwrite" }.Save(path); } catch(Exception ex) when(ex is JsonException or InvalidDataException) { refused=true; }
            require(refused && File.ReadAllText(path)==contents, "Invalid/newer preferences must not be reset to defaults."); return Task.CompletedTask;
        }));
    }
    private static DesktopPreferences Save(DesktopPreferences next,string path,DesktopPreferences previous)
    {
        // Keep the original one-argument API available to reproduce the baseline;
        // repaired builds receive the explicit source snapshot for changed-field merging.
        var method=typeof(DesktopPreferences).GetMethods(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Single(m=>m.Name=="Save");
        try { return method.Invoke(next,method.GetParameters().Length==2?[path,previous]:[path]) as DesktopPreferences ?? DesktopPreferences.Load(path); }
        catch(System.Reflection.TargetInvocationException ex) when(ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw; }
    }
    private static async Task Owned(Func<string,Task> action)
    {
        string folder=Path.Combine(Path.GetTempPath(),"Buddy-feedback-settings-"+Guid.NewGuid()); Directory.CreateDirectory(folder);
        try { await action(Path.Combine(folder,"desktop.json")); }
        finally { SourceReceipt.DeleteOwned(folder,"Buddy-feedback-settings-"); }
    }
}

namespace Buddy.Windows
{
    internal static class PreviewEnvironment
    {
        internal static string DataDirectory => throw new InvalidOperationException("QA fixtures must supply an explicit owned path; installed profiles are prohibited.");
    }
}

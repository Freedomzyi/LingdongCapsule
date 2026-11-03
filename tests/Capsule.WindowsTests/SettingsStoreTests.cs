using Capsule.App;
using System.IO;
using System.Text.Json;

internal static class SettingsStoreTests
{
    internal static void Run()
    {
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","settings-tests",Guid.NewGuid().ToString("N"));var path=Path.Combine(root,"settings.json");
        var fresh=Settings.LoadFrom(path);if(fresh.RecoveryMessage.Length!=0 || fresh.Width!=280 || Directory.Exists(root))throw new Exception("First-run defaults incorrectly warned or wrote data");
        Directory.CreateDirectory(root);
        File.WriteAllText(path,"{\"Width\":195,\"CompactHeight\":41,\"MediaFirst\":true,\"HardwareFirst\":true,\"WeatherCity\":\"测试城市\",\"PluginPreferences\":{\"fixture.clock\":{\"label\":\"保留\"}},\"FutureOption\":{\"value\":42}}");
        var legacy=Settings.LoadFrom(path);
        if(legacy.Width!=195 || legacy.CompactHeight!=41 || legacy.WeatherCity!="测试城市" || legacy.ComponentOrder[0]!="media" || legacy.PluginPreferences["fixture.clock"]["label"]!="保留")throw new Exception("Legacy settings migration lost user choices");
        legacy.SaveTo(path);legacy.Width=243;legacy.SaveTo(path);
        var current=Settings.LoadFrom(path);var prior=Settings.LoadFrom(path+".backup");
        if(current.Width!=243 || prior.Width!=195 || current.AdditionalSettings?["FutureOption"].GetProperty("value").GetInt32()!=42 || File.ReadAllText(path).Contains("RecoveryMessage"))throw new Exception("Atomic save/backup or extension preservation failed");
        var validBackup=File.ReadAllText(path+".backup");File.WriteAllText(path,"{broken primary");
        var recovered=Settings.LoadFrom(path);if(recovered.Width!=195 || recovered.RecoveryMessage.Length==0 || File.ReadAllText(path)!="{broken primary")throw new Exception("Backup recovery mutated original or lost warning");
        recovered.Width=250;recovered.SaveTo(path);
        var rejected=Directory.GetFiles(root,"settings.json.rejected-*.json");
        if(rejected.Length!=1 || File.ReadAllText(rejected[0])!="{broken primary" || File.ReadAllText(path+".backup")!=validBackup || Settings.LoadFrom(path).Width!=250)throw new Exception("Saving recovered settings destroyed corrupt evidence or valid backup");
        var future="{\"SchemaVersion\":2,\"Width\":299,\"FutureOption\":\"keep\"}";File.WriteAllText(path,future);
        var newer=Settings.LoadFrom(path);var blocked=false;try{newer.SaveTo(path);}catch(InvalidOperationException){blocked=true;}
        if(!blocked || newer.RecoveryMessage.Length==0 || File.ReadAllText(path)!=future)throw new Exception("Older build overwrote future settings");
        blocked=false;try{legacy.SaveTo(path);}catch(InvalidOperationException){blocked=true;}
        if(!blocked || File.ReadAllText(path)!=future)throw new Exception("Future file arriving after load was overwritten");
        File.WriteAllText(path,"{\"Width\":222}");
        using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            blocked=false;try{legacy.SaveTo(path);}catch(IOException){blocked=true;}if(!blocked)throw new Exception("Locked settings unexpectedly replaced");
        }
        if(Settings.LoadFrom(path).Width!=222 || Directory.GetFiles(root,"*.tmp-*").Length!=0)throw new Exception("Failed save changed original or left temporary files");
        File.WriteAllText(path,"null");File.WriteAllText(path+".backup","{broken backup");
        var fallback=Settings.LoadFrom(path);if(fallback.Width!=280 || fallback.RecoveryMessage.Length==0 || File.ReadAllText(path)!="null")throw new Exception("Double-corrupt fallback was silent or destructive");
        Console.WriteLine("PASS Legacy migration, atomic backup/recovery, corrupt-file preservation, future-version guard and save-failure cleanup");
    }
}

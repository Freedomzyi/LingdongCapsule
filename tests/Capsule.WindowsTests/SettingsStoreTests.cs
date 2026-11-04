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
        // Reproduce the actual profile's filesystem policy without opening or copying
        // personal settings. Only unique, generated files below our own directory.
        var local=Path.Combine(Settings.Folder,"storage-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(local);
        var localPath=Path.Combine(local,"owned-settings.json");var owned=new Settings{Width=195};owned.SaveTo(localPath);
        var encrypted=File.GetAttributes(localPath).HasFlag(FileAttributes.Encrypted);
        var access=new FileInfo(localPath).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);if(encrypted){access.SetAccessRuleProtection(true,true);new FileInfo(localPath).SetAccessControl(access);}
        var originalAccess=new FileInfo(localPath).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access);
        owned.Width=250;owned.SaveTo(localPath);owned.Width=280;owned.SaveTo(localPath);
        if(encrypted && (new FileInfo(localPath).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access)!=originalAccess || new FileInfo(localPath+".backup").GetAccessControl(System.Security.AccessControl.AccessControlSections.Access).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access)!=originalAccess))throw new Exception("Filesystem fallback changed protected primary/backup access rules");
        if(Settings.LoadFrom(localPath).Width!=280 || Settings.LoadFrom(localPath+".backup").Width!=250)throw new Exception("Actual local profile filesystem cannot save or retain previous preferences");
        var original=File.ReadAllBytes(localPath);var oldBackup=File.ReadAllBytes(localPath+".backup");
        using(var held=new FileStream(localPath+".backup",FileMode.Open,FileAccess.Read,FileShare.None))
        {
            blocked=false;try{owned.SaveTo(localPath);}catch(IOException){blocked=true;}
            if(!blocked || !File.ReadAllBytes(localPath).SequenceEqual(original))throw new Exception("Locked backup modified actual-profile primary settings");
        }
        if(!File.ReadAllBytes(localPath+".backup").SequenceEqual(oldBackup) || Directory.GetFiles(local,"*.tmp-*").Length>0 || Directory.GetFiles(local,"*.backup-*").Length>0)throw new Exception("Filesystem save refusal changed backup or retained transaction files");
        var attributes=File.GetAttributes(localPath);File.SetAttributes(localPath,attributes|FileAttributes.ReadOnly);
        try{blocked=false;try{owned.SaveTo(localPath);}catch(UnauthorizedAccessException){blocked=true;}if(!blocked)throw new Exception("EFS rename bypassed a read-only settings file");}
        finally{File.SetAttributes(localPath,attributes);}
        if(!File.ReadAllBytes(localPath).SequenceEqual(original))throw new Exception("Read-only refusal modified the primary settings");
        Console.WriteLine("PASS Actual profile filesystem save/reload/previous backup and locked-backup rollback; encrypted="+File.GetAttributes(localPath).HasFlag(FileAttributes.Encrypted)+"; generated-only evidence: "+local);
    }
}

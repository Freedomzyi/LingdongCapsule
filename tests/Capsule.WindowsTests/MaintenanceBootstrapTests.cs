using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Capsule.App;

internal static class MaintenanceBootstrapTests
{
    internal static async Task RunAsync()
    {
        var root=Path.GetFullPath(Path.Combine("artifacts","maintenance-bootstrap-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var files=new[]{"LingdongCapsule.exe","LingdongCapsule.dll","coreclr.dll","Uninstall.ps1","MaintenanceCleanup.ps1"};
        foreach(var name in files)File.WriteAllText(Path.Combine(root,name),"Own fixture "+name);
        File.WriteAllText(Path.Combine(root,"unowned-user-file.txt"),"Must never be copied or deleted");
        void Manifest(IEnumerable<string> paths)=>File.WriteAllText(Path.Combine(root,"installed-files.json"),JsonSerializer.Serialize(paths));
        Manifest(files);var stage=MaintenanceBootstrap.PrepareUninstaller(root);
        if(MaintenanceBootstrap.ReadTarget(stage)!=root || File.Exists(Path.Combine(stage,"unowned-user-file.txt")))throw new Exception("Staging lost identity or copied user data");
        foreach(var name in files)if(File.ReadAllText(Path.Combine(stage,name))!=File.ReadAllText(Path.Combine(root,name)))throw new Exception("Maintenance stage bytes differ");
        async Task<int> Cleanup(string directory,int pid,long ticks)
        {
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.GetFullPath("MaintenanceCleanup.ps1"),"-Root",directory,"-WaitPid",pid.ToString(),"-StartedTicks",ticks.ToString()})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;var error=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();await error;await output;return process.ExitCode;
        }
        using(var self=Process.GetCurrentProcess())if(await Cleanup(stage,self.Id,self.StartTime.ToUniversalTime().Ticks)==0 || !Directory.Exists(stage))throw new Exception("Cleanup accepted unrelated process identity");
        if(await Cleanup(root,int.MaxValue,0)==0 || !File.Exists(Path.Combine(root,"unowned-user-file.txt")))throw new Exception("Cleanup escaped owned temp scope");
        if(await Cleanup(stage,int.MaxValue,0)!=0 || Directory.Exists(stage))throw new Exception("Owned stage was not removed after process absence");
        foreach(var bad in new[]{files.Concat(new[]{"../escape"}),files.Concat(new[]{"LingdongCapsule.exe"}),files.Concat(new[]{"installed-files.json"})})
        {Manifest(bad);try{MaintenanceBootstrap.PrepareUninstaller(root);throw new Exception("Invalid stage manifest accepted");}catch(IOException){}}
        Manifest(files);var before=Directory.GetDirectories(MaintenanceBootstrap.CacheRoot).Length;
        using(var locked=new FileStream(Path.Combine(root,"LingdongCapsule.exe"),FileMode.Open,FileAccess.Read,FileShare.None))
            try{MaintenanceBootstrap.PrepareUninstaller(root);throw new Exception("Locked own fixture was copied");}catch(IOException){}
        if(Directory.GetDirectories(MaintenanceBootstrap.CacheRoot).Length!=before || !File.Exists(Path.Combine(root,"unowned-user-file.txt")))throw new Exception("Failed preparation leaked a stage or changed original files");
        Console.WriteLine("PASS manifest-scoped maintenance staging, byte identity, user-file exclusion, cleanup process/path guards, traversal/duplicate rejection and failed-copy cleanup; fake executable files never launched; evidence: "+root);
    }
}

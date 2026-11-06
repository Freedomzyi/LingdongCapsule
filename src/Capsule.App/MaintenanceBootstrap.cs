using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Capsule.App;

internal static class MaintenanceBootstrap
{
    private sealed record StageIdentity(string Id,string Target);
    internal static string CacheRoot=>Path.Combine(Path.GetTempPath(),"LingdongCapsule-maintenance");
    private static string SafePath(string path)
    {
        var full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if(!Path.IsPathFullyQualified(path) || full==Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar) || full.StartsWith(@"\\"))throw new IOException("维护目录无效。");
        for(var cursor=full;!string.IsNullOrEmpty(cursor);cursor=Path.GetDirectoryName(cursor))
            if((File.Exists(cursor) || Directory.Exists(cursor)) && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))throw new IOException("维护目录不能经过链接。");
        return full;
    }
    internal static string PrepareUninstaller(string directory)
    {
        var source=SafePath(directory);var manifest=SafePath(Path.Combine(source,"installed-files.json"));
        if(!File.Exists(manifest) || new FileInfo(manifest).Length>4194304)throw new IOException("没有找到有效的安装清单。便携版请退出后移除解压目录。");
        var files=JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? [];
        if(files.Length is 0 or >50000 || !files.Contains("LingdongCapsule.exe",StringComparer.OrdinalIgnoreCase))throw new IOException("安装清单无效。");
        var checkedFiles=new List<(string Source,string Relative)>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var relative in files)
        {
            if(string.IsNullOrWhiteSpace(relative) || relative.Equals("installed-files.json",StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('\\','/').Any(part=>part is ".." or "." or "") || !seen.Add(relative))throw new IOException("安装清单包含无效路径。");
            var file=SafePath(Path.Combine(source,relative));if(!file.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !File.Exists(file))throw new IOException("安装文件缺失，请先修复安装。");checkedFiles.Add((file,relative));
        }
        foreach(var required in new[]{"LingdongCapsule.dll","coreclr.dll","Uninstall.ps1","MaintenanceCleanup.ps1"})if(!seen.Contains(required))throw new IOException("维护文件不完整，请先修复安装。");
        var id=Guid.NewGuid().ToString("N");var destination=SafePath(Path.Combine(CacheRoot,id));Directory.CreateDirectory(destination);
        // Only files from the validated application manifest are staged; user files stay put.
        try
        {
            foreach(var (file,relative) in checkedFiles)
            {
                var copy=Path.Combine(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(file,copy,false);
                using var original=File.OpenRead(file);using var staged=File.OpenRead(copy);
                if(!SHA256.HashData(original).SequenceEqual(SHA256.HashData(staged)))throw new IOException("维护文件校验失败，请重试。");
            }
            File.WriteAllText(Path.Combine(destination,".capsule-maintenance.json"),JsonSerializer.Serialize(new StageIdentity(id,source)));
            return destination;
        }
        catch{TryRemoveFailedStage(destination);throw;}
    }
    private static void TryRemoveFailedStage(string directory)
    {
        try
        {
            var root=SafePath(directory);
            if(!string.Equals(Path.GetDirectoryName(root),SafePath(CacheRoot),StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(root),"N",out _))return;
            var pending=new Stack<string>();pending.Push(root);
            while(pending.Count>0)foreach(var entry in Directory.EnumerateFileSystemEntries(pending.Pop())){var attributes=File.GetAttributes(entry);if(attributes.HasFlag(FileAttributes.ReparsePoint))return;if(attributes.HasFlag(FileAttributes.Directory))pending.Push(entry);}
            Directory.Delete(root,true);
        }
        catch(IOException){ /* Retain only the owned stage if cleanup cannot finish. */ }
        catch(UnauthorizedAccessException){ }
    }
    internal static string ReadTarget(string stage)
    {
        var root=SafePath(stage);var parent=SafePath(CacheRoot);
        if(!string.Equals(Path.GetDirectoryName(root),parent,StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(root),"N",out _))throw new IOException("卸载维护目录无效。");
        var file=SafePath(Path.Combine(root,".capsule-maintenance.json"));if(new FileInfo(file).Length>16384)throw new IOException("维护凭据无效。");
        var identity=JsonSerializer.Deserialize<StageIdentity>(File.ReadAllText(file)) ?? throw new IOException("维护凭据缺失。");
        if(identity.Id!=Path.GetFileName(root))throw new IOException("维护凭据不匹配。");
        return SafePath(identity.Target);
    }
    internal static void Launch(string stage)
    {
        _=ReadTarget(stage);var start=new ProcessStartInfo(Path.Combine(stage,"LingdongCapsule.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage};start.ArgumentList.Add("--uninstall-staged");
        using var process=Process.Start(start) ?? throw new IOException("无法打开卸载界面。");
    }
    internal static void ScheduleCleanup(string stage)
    {
        _=ReadTarget(stage);
        var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(stage,"MaintenanceCleanup.ps1"),"-Root",stage,"-WaitPid",Environment.ProcessId.ToString(),"-StartedTicks",Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString()})start.ArgumentList.Add(arg);
        using var process=Process.Start(start);
    }
}

using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Capsule.App;

internal sealed record PluginPackageInfo(string Id,string Name,string Version,string Entry);
internal sealed record InstalledPlugin(PluginPackageInfo Info,string Directory,string EntryFile);
internal static class PluginPackage
{
    private static string Relative(string name)
    {
        if(string.IsNullOrWhiteSpace(name) || name.Contains('\\') || Path.IsPathRooted(name))throw new InvalidDataException("插件包路径无效。");
        var parts=name.TrimEnd('/').Split('/');
        foreach(var part in parts)
        {
            if(part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("插件包包含越界或无效路径。");
            var stem=part.Split('.')[0].ToUpperInvariant();if(stem is "CON" or "PRN" or "AUX" or "NUL" || Regex.IsMatch(stem,@"^(COM|LPT)[1-9]$"))throw new InvalidDataException("插件包包含系统保留文件名。");
        }
        return string.Join(Path.DirectorySeparatorChar,parts);
    }
    public static InstalledPlugin Install(string archive,string root)
    {
        using var zip=ZipFile.OpenRead(archive);
        if(zip.Entries.Count is <1 or >256)throw new InvalidDataException("插件包文件数量超限。");
        long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            var path=Relative(entry.FullName);if(!names.Add(path))throw new InvalidDataException("插件包文件名重复。");
            if(path.Equals(".capsule-install.json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("插件包占用安装凭据名称。");
            if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("插件包不支持符号链接。");
            total+=entry.Length;if(entry.Length>16*1024*1024 || total>64*1024*1024)throw new InvalidDataException("插件包展开体积超限。");
        }
        var manifest=zip.GetEntry("plugin.json") ?? throw new InvalidDataException("缺少 plugin.json。");
        if(manifest.Length>16384)throw new InvalidDataException("插件清单过大。");
        PluginPackageInfo info;using(var stream=manifest.Open())info=JsonSerializer.Deserialize<PluginPackageInfo>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidDataException("插件清单为空。");
        if(!Regex.IsMatch(info.Id ?? "",@"^[a-z][a-z0-9.-]{2,79}$") || string.IsNullOrWhiteSpace(info.Name) || info.Name.Length>80 || !Version.TryParse(info.Version,out _))throw new InvalidDataException("插件清单身份无效。");
        var entryPath=Relative(info.Entry);if(!entryPath.EndsWith(".dll",StringComparison.OrdinalIgnoreCase) || !names.Contains(entryPath))throw new InvalidDataException("插件入口不存在或不是 DLL。");
        var actualEntry=zip.Entries.First(item=>string.Equals(Relative(item.FullName),entryPath,StringComparison.OrdinalIgnoreCase));
        if(actualEntry.FullName.EndsWith('/') || actualEntry.Length==0)throw new InvalidDataException("插件入口必须是非空文件。");
        var basePath=Path.GetFullPath(root);Directory.CreateDirectory(basePath);
        if(File.GetAttributes(basePath).HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("插件根目录不能是重解析点。");
        var target=Path.Combine(basePath,"package-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllText(Path.Combine(target,".capsule-install.json"),JsonSerializer.Serialize(new{schema=1,root=basePath,folder=Path.GetFileName(target)}));
            var buffer=new byte[8192];long expanded=0;
            foreach(var entry in zip.Entries)
            {
                var destination=Path.GetFullPath(Path.Combine(target,Relative(entry.FullName)));
                if(!destination.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("解压目标越界。");
                if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(destination);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input=entry.Open();using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);int count;long length=0;
                while((count=input.Read(buffer,0,buffer.Length))>0){length+=count;expanded+=count;if(length>16*1024*1024 || expanded>64*1024*1024)throw new InvalidDataException("实际展开体积超限。");output.Write(buffer,0,count);}
            }
            return new(info,target,Path.Combine(target,entryPath));
        }
        catch(Exception installError)
        {
            try
            {
                if(!Uninstall(Path.Combine(target,entryPath),basePath) && Path.GetFullPath(target).StartsWith(basePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && !File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))Directory.Delete(target,false);
            }
            catch(Exception cleanupError) when(cleanupError is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
            {throw new AggregateException("安装失败，残留包未能清理；可在已安装目录中检查。",installError,cleanupError);}
            throw;
        }
    }
    public static string? ManagedDirectory(string entryFile,string root)
    {
        var basePath=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);var entry=Path.GetFullPath(entryFile);
        if(!entry.StartsWith(basePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return null;
        var relative=Path.GetRelativePath(basePath,entry);var folder=relative.Split(Path.DirectorySeparatorChar)[0];
        if(!Regex.IsMatch(folder,@"^package-[a-f0-9]{32}$"))return null;
        var directory=Path.Combine(basePath,folder);var receipt=Path.Combine(directory,".capsule-install.json");
        if(!File.Exists(receipt) || File.GetAttributes(basePath).HasFlag(FileAttributes.ReparsePoint) || File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))return null;
        if(File.GetAttributes(receipt).HasFlag(FileAttributes.ReparsePoint) || new FileInfo(receipt).Length>16384)return null;
        using var json=JsonDocument.Parse(File.ReadAllText(receipt));var data=json.RootElement;
        return data.GetProperty("schema").GetInt32()==1 && string.Equals(data.GetProperty("root").GetString()?.TrimEnd(Path.DirectorySeparatorChar),basePath,StringComparison.OrdinalIgnoreCase) && data.GetProperty("folder").GetString()==folder ? directory : null;
    }
    public static bool Uninstall(string entryFile,string root)
    {
        var directory=ManagedDirectory(entryFile,root);if(directory==null)return false;
        var pending=new Stack<string>();pending.Push(directory);var files=new List<string>();var directories=new List<string>();
        while(pending.Count>0)
        {
            foreach(var item in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes=File.GetAttributes(item);if(attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("卸载目录包含重解析点，已停止删除。");
                if(attributes.HasFlag(FileAttributes.Directory)){pending.Push(item);directories.Add(item);}else files.Add(item);
            }
        }
        var receipt=Path.Combine(directory,".capsule-install.json");
        foreach(var file in files.Where(file=>!string.Equals(file,receipt,StringComparison.OrdinalIgnoreCase)))File.Delete(file);
        foreach(var child in directories.OrderByDescending(path=>path.Length))Directory.Delete(child,false);
        File.Delete(receipt);Directory.Delete(directory,false);return true;
    }
}

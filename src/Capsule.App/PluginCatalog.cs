using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Capsule.App;

internal sealed record PluginCatalogEntry(string EntryFile,string Name,string Id,string Version,bool Enabled,bool PendingRemoval,string Error)
{
    public bool CanEnable=>!Enabled && !PendingRemoval && Error.Length==0;
}

internal static class PluginCatalog
{
    private static bool IsLink(string path)=>File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
    internal static InstalledPlugin Inspect(string entry,string root)
    {
        var directory=PluginPackage.ManagedDirectory(entry,root) ?? throw new InvalidDataException("不属于胶囊管理的安装包。");
        var manifest=Path.Combine(directory,"plugin.json");
        if(IsLink(manifest))throw new InvalidDataException("插件清单不能是链接。");
        using var input=new FileStream(manifest,FileMode.Open,FileAccess.Read,FileShare.Read);
        var bytes=new byte[16385];var count=0;
        while(count<bytes.Length){var read=input.Read(bytes,count,bytes.Length-count);if(read==0)break;count+=read;}
        if(count==bytes.Length)throw new InvalidDataException("插件清单过大。");
        var info=JsonSerializer.Deserialize<PluginPackageInfo>(bytes.AsSpan(0,count),new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidDataException("插件清单为空。");
        if(!Regex.IsMatch(info.Id ?? "",@"^[a-z][a-z0-9.-]{2,79}$") || string.IsNullOrWhiteSpace(info.Name) || info.Name.Length>80 || !Version.TryParse(info.Version,out _) || string.IsNullOrWhiteSpace(info.Entry) || info.Entry.Contains('\\') || Path.IsPathRooted(info.Entry))throw new InvalidDataException("插件清单身份或入口无效。");
        var current=directory;
        foreach(var part in info.Entry.Split('/'))
        {
            if(part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("插件入口路径无效。");
            current=Path.Combine(current,part);if(IsLink(current))throw new InvalidDataException("插件入口路径包含链接。");
        }
        if(!current.EndsWith(".dll",StringComparison.OrdinalIgnoreCase) || !File.Exists(current) || new FileInfo(current).Length==0)throw new InvalidDataException("插件入口 DLL 不存在。");
        return new(info,directory,Path.GetFullPath(current));
    }
    internal static PluginCatalogEntry[] Read(string root,IEnumerable<string> enabled,IEnumerable<string> pending)
    {
        var rows=new List<PluginCatalogEntry>();
        var active=new HashSet<string>(enabled,StringComparer.OrdinalIgnoreCase);var removals=new HashSet<string>(pending,StringComparer.OrdinalIgnoreCase);
        try
        {
            if(!Directory.Exists(root))return [];
            if(IsLink(root))throw new InvalidDataException("插件目录不能是链接。");
            var inspected=0;
            foreach(var directory in Directory.EnumerateDirectories(root))
            {
                if(inspected++>=512){rows.Add(new("","安装目录数量超限","","",false,false,"最多检查 512 个安装目录。"));break;}
                var probe=Path.Combine(directory,"plugin.json");
                try
                {
                    // Validate ownership before parsing any package metadata.
                    if(PluginPackage.ManagedDirectory(probe,root)==null)continue;
                    try
                    {
                        var installed=Inspect(probe,root);var entry=installed.EntryFile;
                        rows.Add(new(entry,installed.Info.Name,installed.Info.Id,installed.Info.Version,active.Contains(entry),removals.Contains(entry),""));
                    }
                    catch(Exception error) when(IsMetadataFailure(error))
                    {
                        var related=active.Concat(removals).FirstOrDefault(path=>path.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                        var safe=related ?? probe;rows.Add(new(safe,"损坏的插件包",Path.GetFileName(directory),"",related!=null && active.Contains(related),related!=null && removals.Contains(related),"无法读取组件："+error.GetType().Name));
                    }
                }
                catch(Exception error) when(IsMetadataFailure(error)){rows.Add(new("","安装凭据无效",Path.GetFileName(directory),"",false,false,"不能安全管理此目录："+error.GetType().Name));}
            }
        }
        catch(Exception error) when(IsMetadataFailure(error)){rows.Add(new("","插件目录不可读","","",false,false,error.GetType().Name));}
        return rows.OrderBy(row=>row.Name,StringComparer.CurrentCulture).ThenBy(row=>row.Version).ToArray();
    }
    private static bool IsMetadataFailure(Exception error)=>error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException;
}

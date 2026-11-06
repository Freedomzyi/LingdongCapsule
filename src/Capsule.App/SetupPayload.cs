using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Capsule.App;

internal sealed class SetupPayload : IDisposable
{
    internal string Root{get;}
    internal string DirectoryPath=>Path.Combine(Root,"payload");
    private static string Cache=>Path.Combine(Path.GetTempPath(),"LingdongCapsule-setup");
    private SetupPayload(string root)=>Root=root;
    internal static SetupPayload Extract(Stream stream,string expectedHash)
    {
        if(!stream.CanSeek || expectedHash.Length!=64 || !Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new IOException("安装包校验失败，请重新下载。");
        stream.Position=0;using var zip=new ZipArchive(stream,ZipArchiveMode.Read,true);
        var entries=new List<(ZipArchiveEntry Entry,string Relative)>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var entry in zip.Entries)
        {
            var name=entry.FullName;
            if(Path.IsPathRooted(name) || name.Contains(':') || name.Contains('\\') || name.Split('/').Any(part=>part is ".." or ".") || !names.Add(name) || ((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("安装包包含无效路径。");
            total+=entry.Length;if(names.Count>4096 || total>1024L*1024*1024)throw new IOException("安装包超过安全限制。");
            entries.Add((entry,name));
        }
        foreach(var required in new[]{"LingdongCapsule.exe","LingdongCapsule.dll","coreclr.dll","Install.ps1","MaintenanceCleanup.ps1","BUILD-INFO.json"})if(!names.Contains(required))throw new IOException("安装包文件不完整。");
        var root=Path.GetFullPath(Path.Combine(Cache,Guid.NewGuid().ToString("N")));CheckPath(root);var payload=new SetupPayload(root);
        try
        {
            Directory.CreateDirectory(payload.DirectoryPath);
            foreach(var (entry,relative) in entries)
            {
                var file=Path.GetFullPath(Path.Combine(payload.DirectoryPath,relative));
                if(!file.StartsWith(payload.DirectoryPath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("安装包文件越界。");
                CheckPath(file);if(relative.EndsWith('/')){Directory.CreateDirectory(file);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);using var input=entry.Open();using var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);input.CopyTo(output);
            }
            return payload;
        }
        catch{payload.Dispose();throw;}
    }
    private static void CheckPath(string path)
    {
        for(var cursor=path;!string.IsNullOrEmpty(cursor);cursor=Path.GetDirectoryName(cursor))if((File.Exists(cursor) || Directory.Exists(cursor)) && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))throw new IOException("安装暂存路径不能经过链接。");
    }
    public void Dispose()
    {
        try
        {
            var full=Path.GetFullPath(Root);if(!string.Equals(Path.GetDirectoryName(full),Path.GetFullPath(Cache),StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(full),"N",out _) || !Directory.Exists(full))return;
            CheckPath(full);var pending=new Stack<string>();pending.Push(full);
            while(pending.Count>0)foreach(var entry in Directory.EnumerateFileSystemEntries(pending.Pop())){CheckPath(entry);if(Directory.Exists(entry))pending.Push(entry);}
            Directory.Delete(full,true);
        }
        catch(IOException){ /* Only the bounded owned cache may remain after an I/O failure. */ }
        catch(UnauthorizedAccessException){ }
    }
}

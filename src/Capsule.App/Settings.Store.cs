using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.App;

public sealed partial class Settings
{
    public int SchemaVersion{get;set;}=1;
    [JsonExtensionData]public Dictionary<string,JsonElement>? AdditionalSettings{get;set;}
    [JsonIgnore]public string RecoveryMessage{get;private set;}="";
    private static Settings ReadFile(string path)
    {
        var file=new FileInfo(path);if(file.Length>2*1024*1024)throw new InvalidDataException("配置文件超限。");
        var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new InvalidDataException("配置文件为空。");
        if(settings.SchemaVersion<1)throw new InvalidDataException("配置版本无效。");
        settings.Normalize();return settings;
    }
    private static bool Recoverable(Exception error)=>error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
    internal static Settings LoadFrom(string path)
    {
        try
        {
            var value=ReadFile(path);
            if(value.SchemaVersion>1)value.RecoveryMessage="设置来自更新版本；当前版本只读使用，不会覆盖原配置。请使用新版程序保存。";
            return value;
        }
        catch(Exception error) when(Recoverable(error))
        {
            try
            {
                var backup=ReadFile(path+".backup");
                backup.RecoveryMessage="主配置无法读取，已使用上次有效备份。原文件保留，保存时会另行保留损坏文件。";
                if(backup.SchemaVersion>1)backup.RecoveryMessage+="备份来自更新版本，当前版本禁止覆盖。";
                return backup;
            }
            catch(Exception backupError) when(Recoverable(backupError))
            {
                var fallback=new Settings();fallback.Normalize();
                if(error is not(FileNotFoundException or DirectoryNotFoundException) || backupError is not(FileNotFoundException or DirectoryNotFoundException))fallback.RecoveryMessage="配置与备份无法读取，暂用默认设置；原文件未改动，保存时会另行保留损坏文件。";
                return fallback;
            }
        }
    }
    internal void SaveTo(string path)
    {
        if(SchemaVersion>1)throw new InvalidOperationException("设置来自更新版本，已拒绝覆盖。请用新版程序保存。");
        var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary=full+".tmp-"+Guid.NewGuid().ToString("N");
        try
        {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(this,new JsonSerializerOptions{WriteIndented=true});
            if(bytes.Length>2*1024*1024)throw new InvalidDataException("配置文件超限，未覆盖原配置。");
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes);output.Flush(true);}
            if(!File.Exists(full)){File.Move(temporary,full);return;}
            var valid=false;
            try{var current=ReadFile(full);if(current.SchemaVersion>1)throw new InvalidOperationException("磁盘配置已由更新版本写入，拒绝覆盖。");valid=true;}
            catch(Exception error) when(Recoverable(error)){ }
            if(valid)ReplaceWithBackup(temporary,full);
            else
            {
                var rejected=full+".rejected-"+Guid.NewGuid().ToString("N")+".json";
                File.Move(full,rejected);
                try{File.Move(temporary,full);}catch{File.Move(rejected,full);throw;}
            }
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static void ReplaceWithBackup(string temporary,string full)
    {
        if(File.GetAttributes(full).HasFlag(FileAttributes.Encrypted)){RenameWithBackup(temporary,full);return;}
        try{File.Replace(temporary,full,full+".backup",true);}
        catch(IOException error) when((error.HResult & 0xffff)==1175){RenameWithBackup(temporary,full);}
    }
    private static void RenameWithBackup(string temporary,string full)
    {
        var attributes=File.GetAttributes(full);
        if(attributes.HasFlag(FileAttributes.ReadOnly))throw new UnauthorizedAccessException("配置文件只读，无法保存。");
        var backup=full+".backup";
        // Same-directory rename avoids ReplaceFile's EFS/metadata merge failure.
        // Only the documented 1175 failure can enter this path after ReplaceFile.
        var staged=full+".backup-stage-"+Guid.NewGuid().ToString("N");
        var previous=full+".backup-prior-"+Guid.NewGuid().ToString("N");
        byte[]? previousAccess=null;
        var movedPrevious=false;var published=false;var committed=false;var restored=false;
        try
        {
            File.Copy(full,staged);
            if(attributes.HasFlag(FileAttributes.Encrypted))
            {
                foreach(var target in new[]{staged,temporary})if(!File.GetAttributes(target).HasFlag(FileAttributes.Encrypted))File.Encrypt(target);
            }
            var descriptor=new FileInfo(full).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
            foreach(var target in new[]{staged,temporary})
                SetFileAccess(target,descriptor);
            if(File.Exists(backup))
            {
                if(File.GetAttributes(backup).HasFlag(FileAttributes.ReadOnly))throw new UnauthorizedAccessException("配置备份只读，无法保存。");
                previousAccess=new FileInfo(backup).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
                File.Move(backup,previous);movedPrevious=true;SetFileAccess(previous,previousAccess);
            }
            File.Move(staged,backup);published=true;
            // This host reapplies parent inheritance on rename to a new filename.
            // Restore the saved ACL before the primary file is committed.
            SetFileAccess(backup,descriptor);
            File.Move(temporary,full,true);committed=true;
        }
        catch
        {
            if(published)File.Move(backup,staged,true);
            if(movedPrevious){File.Move(previous,backup);SetFileAccess(backup,previousAccess!);}
            restored=true;throw;
        }
        finally
        {
            // A rollback error retains recovery files instead of deleting evidence.
            if(committed || restored)
                foreach(var file in new[]{staged,previous})try{if(File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private static void SetFileAccess(string path,byte[] descriptor)
    {
        var access=new System.Security.AccessControl.FileSecurity();access.SetSecurityDescriptorBinaryForm(descriptor,System.Security.AccessControl.AccessControlSections.Access);
        new FileInfo(path).SetAccessControl(access);
    }
}

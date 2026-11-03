using Capsule.App;
using Capsule.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

internal static class ToastImageTests
{
    internal static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);var path=Path.Combine(root,"owned-"+Guid.NewGuid().ToString("N")+".db");var notice=new Notice("owned.fixture","Fixture","自写标题","自写正文",42,DateTimeOffset.UtcNow);
        var picture=Path.Combine(root,"owned-avatar");
        var pixel=System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{0,120,0,255},4);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pixel));using(var output=File.Create(picture))png.Save(output);
        var xml="<toast><visual><binding template=\"ToastGeneric\"><text>自写标题</text><text>自写正文</text><image placement=\"appLogoOverride\" hint-crop=\"circle\" src=\""+System.Security.SecurityElement.Escape(picture)+"\"/></binding></visual></toast>";
        var parsed=ToastImageStore.Parse(Encoding.UTF8.GetBytes(xml),notice);if(parsed?.Source!=picture || !parsed.Avatar)throw new Exception("Toast avatar XML did not preserve source/crop");
        using(var images=new NoticeImages())if(await images.ResolveAsync(notice,systemAvatar:parsed.Source) is not {System:true,Avatar:true,Bitmap.IsFrozen:true})throw new Exception("System avatar source did not enter bounded decoded-image path");
        var utf16=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray();if(ToastImageStore.Parse(utf16,notice)==null)throw new Exception("UTF16 toast payload rejected");
        if(ToastImageStore.Parse(Encoding.UTF8.GetBytes(xml),notice with{Body="其他正文"})!=null || ToastImageStore.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE toast [<!ENTITY x SYSTEM 'file:///C:/missing'>]>"+xml),notice)!=null)throw new Exception("Body mismatch or DTD accepted");
        if(ToastImageStore.Parse(Encoding.UTF8.GetBytes(xml.Replace(picture,@"\\invalid\share\photo")),notice)!=null || ToastImageStore.Parse(Encoding.UTF8.GetBytes(xml.Replace(picture,"http://example.invalid/photo")),notice)!=null)throw new Exception("Unsafe system avatar URI accepted");
        using var fixture=new Database(path);
        fixture.Exec("CREATE TABLE NotificationHandler(RecordId INTEGER PRIMARY KEY,PrimaryId TEXT COLLATE NOCASE);CREATE TABLE Notification(Id INTEGER PRIMARY KEY,HandlerId INTEGER,Type TEXT,Payload BLOB,ArrivalTime INTEGER);INSERT INTO NotificationHandler VALUES(1,'owned.fixture');");
        fixture.Exec("INSERT INTO Notification VALUES(42,1,'toast',CAST('"+xml.Replace("'","''")+"' AS BLOB),"+notice.CreatedAt.UtcDateTime.ToFileTimeUtc()+");");
        string Hash(){using var data=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return Convert.ToHexString(SHA256.HashData(data));}
        var before=Hash();var reader=new ToastImageStore(path);
        if((await reader.FindAsync(notice,default))?.Source!=picture)throw new Exception("Read-only SQLite identity query did not locate own toast");
        foreach(var wrong in new[]{notice with{Id=43},notice with{SourceId="owned.fixture' OR 1=1--"},notice with{CreatedAt=notice.CreatedAt.AddMinutes(-1)},notice with{Title="错标题"}})
            if(await reader.FindAsync(wrong,default)!=null)throw new Exception("Mismatched toast identity read an avatar");
        if(Hash()!=before)throw new Exception("Avatar reader modified the notification database");
        using var canceled=new CancellationTokenSource();canceled.Cancel();var rejected=false;try{await reader.FindAsync(notice,canceled.Token);}catch(OperationCanceledException){rejected=true;}if(!rejected)throw new Exception("Canceled avatar lookup ignored cancellation");
        using(var incomplete=new Database(Path.Combine(root,"unsupported-"+Guid.NewGuid().ToString("N")+".db")))if(await new ToastImageStore(incomplete.Path).FindAsync(notice,default)!=null)throw new Exception("Unsupported notification schema was accepted");
        Console.WriteLine("PASS Owned notification SQLite read-only ID/app/time/text identity, UTF8/UTF16 XML, bad source/DTD rejection, cancellation and unchanged DB bytes");
    }
    private sealed class Database:IDisposable
    {
        private IntPtr handle;
        internal string Path{get;}
        internal Database(string path){Path=path;if(Open(path,out handle,6,IntPtr.Zero)!=0)throw new IOException("Owned fixture SQLite open failed");}
        internal void Exec(string sql){var result=Execute(handle,sql,IntPtr.Zero,IntPtr.Zero,out var error);if(error!=IntPtr.Zero)Free(error);if(result!=0)throw new IOException("Owned fixture SQLite SQL failed");}
        public void Dispose(){if(handle!=IntPtr.Zero){Close(handle);handle=IntPtr.Zero;}}
        [DllImport("winsqlite3.dll",EntryPoint="sqlite3_open_v2",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)]string file,out IntPtr db,int flags,IntPtr vfs);
        [DllImport("winsqlite3.dll",EntryPoint="sqlite3_exec",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern int Execute(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr context,out IntPtr error);
        [DllImport("winsqlite3.dll",EntryPoint="sqlite3_close",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern int Close(IntPtr db);
        [DllImport("winsqlite3.dll",EntryPoint="sqlite3_free",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern void Free(IntPtr pointer);
    }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Capsule.Core;

namespace Capsule.App;

internal sealed class ToastImageStore
{
    internal sealed record Image(string Source,bool Avatar);
    private readonly string path;
    private static readonly SemaphoreSlim readers=new(2);
    internal ToastImageStore(string? file=null)=>path=file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","Windows","Notifications","wpndatabase.db");
    internal async Task<Image?> FindAsync(Notice notice,CancellationToken token)
    {
        if(notice.Id==0 || notice.CreatedAt==default || notice.SourceId.Length is <1 or >512)return null;
        await readers.WaitAsync(token);
        try
        {
            for(var attempt=0;attempt<3;attempt++)
            {
                token.ThrowIfCancellationRequested();
                var image=await Task.Run(()=>Read(notice,token),token);if(image!=null)return image;
                if(attempt<2)await Task.Delay(attempt==0 ? 300 : 700,token);
            }
            return null;
        }
        finally{readers.Release();}
    }
    private Image? Read(Notice notice,CancellationToken token)
    {
        IntPtr db=IntPtr.Zero,statement=IntPtr.Zero;
        var elapsed=System.Diagnostics.Stopwatch.StartNew();Native.Interrupt progress=_=>token.IsCancellationRequested || elapsed.ElapsedMilliseconds>200 ? 1 : 0;
        try
        {
            if(!File.Exists(path) || Native.Open(path,out db,1,IntPtr.Zero)!=0)return null;
            Native.BusyTimeout(db,100);
            Native.Progress(db,1000,progress,IntPtr.Zero);
            const string query="SELECT n.Payload,n.ArrivalTime FROM Notification n JOIN NotificationHandler h ON h.RecordId=n.HandlerId WHERE n.Id IN (?1,?2) AND h.PrimaryId=?3 AND n.ArrivalTime BETWEEN ?4 AND ?5 AND n.Type IN ('toast','toastCondensed') AND length(n.Payload) BETWEEN 1 AND 65536 LIMIT 1";
            if(Native.Prepare(db,query,-1,out statement,IntPtr.Zero)!=0)return null;
            var expected=notice.CreatedAt.UtcDateTime.ToFileTimeUtc();var tolerance=5*TimeSpan.TicksPerSecond;
            if(Native.BindInt(statement,1,notice.Id)!=0 || Native.BindInt(statement,2,unchecked((int)notice.Id))!=0 || Native.BindText(statement,3,notice.SourceId,-1,new IntPtr(-1))!=0 || Native.BindInt(statement,4,Math.Max(0,expected-tolerance))!=0 || Native.BindInt(statement,5,expected+tolerance)!=0 || Native.Step(statement)!=100)return null;
            var arrived=Native.Int64(statement,1);
            if(arrived<=0 || Math.Abs((new DateTimeOffset(DateTime.FromFileTimeUtc(arrived))-notice.CreatedAt).TotalSeconds)>5)return null;
            var length=Native.Bytes(statement,0);var pointer=Native.Blob(statement,0);if(length is <1 or >65536 || pointer==IntPtr.Zero)return null;
            var bytes=new byte[length];Marshal.Copy(pointer,bytes,0,length);return Parse(bytes,notice);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or ArgumentException or XmlException){return null;}
        finally{if(statement!=IntPtr.Zero)Native.Finalize(statement);if(db!=IntPtr.Zero)Native.Close(db);GC.KeepAlive(progress);}
    }
    internal static Image? Parse(byte[] payload,Notice notice)
    {
        if(payload.Length is <1 or >65536)return null;
        try
        {
            using var input=new MemoryStream(payload,false);using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536});var xml=XDocument.Load(reader);
            if(xml.Root?.Name.LocalName!="toast")return null;
            var binding=xml.Descendants().FirstOrDefault(element=>element.Name.LocalName=="binding" && (string?)element.Attribute("template")=="ToastGeneric");if(binding==null)return null;
            var texts=binding.Elements().Where(element=>element.Name.LocalName=="text").Select(element=>element.Value).ToArray();
            static string Text(string value)=>value.Replace("\r\n","\n").Replace('\r','\n').Trim();
            if(texts.Length==0 || Text(texts[0])!=Text(notice.Title) || Text(string.Join("\n",texts.Skip(1)))!=Text(notice.Body))return null;
            foreach(var image in binding.Elements().Where(element=>element.Name.LocalName=="image" && string.Equals((string?)element.Attribute("placement"),"appLogoOverride",StringComparison.OrdinalIgnoreCase)))
            {
                var source=(string?)image.Attribute("src");if(string.IsNullOrWhiteSpace(source) || source.Length>4096)continue;
                var avatar=string.Equals((string?)image.Attribute("hint-crop"),"circle",StringComparison.OrdinalIgnoreCase);
                if(Uri.TryCreate(source,UriKind.Absolute,out var uri))
                {
                    if(uri.Scheme==Uri.UriSchemeHttps && uri.UserInfo.Length==0)return new(uri.AbsoluteUri,avatar);
                    if(uri.IsFile && !uri.IsUnc)source=uri.LocalPath;
                }
                if(!Path.IsPathFullyQualified(source) || source.StartsWith(@"\\",StringComparison.Ordinal))continue;
                return new(source,avatar);
            }
        }
        catch(Exception error) when(error is XmlException or ArgumentException or InvalidOperationException){ }
        return null;
    }
    private static class Native
    {
        private const string Dll="winsqlite3.dll";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int Interrupt(IntPtr unused);
        [DllImport(Dll,EntryPoint="sqlite3_progress_handler",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern void Progress(IntPtr db,int instructions,Interrupt callback,IntPtr context);
        [DllImport(Dll,EntryPoint="sqlite3_open_v2",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)]string file,out IntPtr db,int flags,IntPtr vfs);
        [DllImport(Dll,EntryPoint="sqlite3_close",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int Close(IntPtr db);
        [DllImport(Dll,EntryPoint="sqlite3_busy_timeout",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int BusyTimeout(IntPtr db,int milliseconds);
        [DllImport(Dll,EntryPoint="sqlite3_prepare_v2",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int Prepare(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int bytes,out IntPtr statement,IntPtr tail);
        [DllImport(Dll,EntryPoint="sqlite3_bind_int64",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int BindInt(IntPtr statement,int index,long value);
        [DllImport(Dll,EntryPoint="sqlite3_bind_text",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int BindText(IntPtr statement,int index,[MarshalAs(UnmanagedType.LPUTF8Str)]string text,int bytes,IntPtr destructor);
        [DllImport(Dll,EntryPoint="sqlite3_step",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int Step(IntPtr statement);
        [DllImport(Dll,EntryPoint="sqlite3_column_int64",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern long Int64(IntPtr statement,int column);
        [DllImport(Dll,EntryPoint="sqlite3_column_bytes",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int Bytes(IntPtr statement,int column);
        [DllImport(Dll,EntryPoint="sqlite3_column_blob",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern IntPtr Blob(IntPtr statement,int column);
        [DllImport(Dll,EntryPoint="sqlite3_finalize",CallingConvention=CallingConvention.Cdecl),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]internal static extern int Finalize(IntPtr statement);
    }
}

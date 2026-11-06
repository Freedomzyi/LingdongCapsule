using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Capsule.App;

internal static class SetupPayloadTests
{
    internal static void Run()
    {
        byte[] Archive(string? extra=null)
        {
            using var bytes=new MemoryStream();using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))
                foreach(var name in new[]{"LingdongCapsule.exe","LingdongCapsule.dll","coreclr.dll","Install.ps1","MaintenanceCleanup.ps1","BUILD-INFO.json"}.Concat(extra==null ? [] : new[]{extra}))
                {using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write("Owned payload fixture");}
            return bytes.ToArray();
        }
        var good=Archive();var hash=Convert.ToHexString(SHA256.HashData(good));string root;
        using(var input=new MemoryStream(good))using(var payload=SetupPayload.Extract(input,hash))
        {root=payload.Root;if(!File.Exists(Path.Combine(payload.DirectoryPath,"Install.ps1")))throw new Exception("Setup payload did not extract");}
        if(Directory.Exists(root))throw new Exception("Disposed setup payload remains on disk");
        foreach(var path in new[]{"../escape","/absolute","C:/absolute","docs/../../escape","bad\\path","LingdongCapsule.EXE"})
        {
            var bad=Archive(path);try{using var input=new MemoryStream(bad);using var payload=SetupPayload.Extract(input,Convert.ToHexString(SHA256.HashData(bad)));throw new Exception("Unsafe setup entry accepted: "+path);}catch(IOException){}
        }
        try{using var input=new MemoryStream(good);using var payload=SetupPayload.Extract(input,new string('0',64));throw new Exception("Wrong setup payload hash accepted");}catch(IOException){}
        Console.WriteLine("PASS setup payload pinned hash, extraction/disposal, traversal/root/colon/backslash and duplicate entry rejection; owned fake executable bytes never run");
    }
}

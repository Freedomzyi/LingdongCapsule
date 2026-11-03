using Capsule.App;
using System.IO;
using System.IO.Compression;

internal static class PluginPackageTests
{
    internal static void Run(string root,string fixture)
    {
        var cases=new Dictionary<string,Action<ZipArchive>>
        {
            ["absolute"]=zip=>Write(zip,"C:/outside.dll","x"),
            ["backslash"]=zip=>Write(zip,"..\\outside.dll","x"),
            ["nested-traversal"]=zip=>Write(zip,"nested/../../outside.dll","x"),
            ["duplicate-case"]=zip=>Write(zip,"FIXTURE.DLL","x"),
            ["reserved-device"]=zip=>Write(zip,"NUL.txt","x"),
            ["receipt-spoof"]=zip=>Write(zip,".capsule-install.json","{}"),
            ["symlink"]=zip=>{var entry=zip.CreateEntry("link");entry.ExternalAttributes=unchecked((0xA000|0x1FF)<<16);},
            ["too-many-files"]=zip=>{for(var index=0;index<257;index++)Write(zip,$"extra-{index}.txt","");},
            ["oversized-file"]=zip=>{using var output=zip.CreateEntry("large.bin").Open();var block=new byte[1024*1024];for(var index=0;index<17;index++)output.Write(block);}
        };
        foreach(var item in cases)
        {
            var archive=Path.Combine(root,item.Key+".zip");var destination=Path.Combine(root,"rejected-"+item.Key);
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){Write(zip,"plugin.json","{\"id\":\"fixture.clock\",\"name\":\"Test\",\"version\":\"0.1.0\",\"entry\":\"fixture.dll\"}");zip.CreateEntryFromFile(fixture,"fixture.dll");item.Value(zip);}
            var rejected=false;try{PluginPackage.Install(archive,destination);}catch(InvalidDataException){rejected=true;}
            if(!rejected || Directory.Exists(destination))throw new Exception("Package validation wrote destination or accepted: "+item.Key);
        }
        var directoryEntry=Path.Combine(root,"directory-entry.zip");
        using(var zip=ZipFile.Open(directoryEntry,ZipArchiveMode.Create)){Write(zip,"plugin.json","{\"id\":\"fixture.clock\",\"name\":\"Test\",\"version\":\"0.1.0\",\"entry\":\"fixture.dll\"}");zip.CreateEntry("fixture.dll/");}
        var directoryRejected=false;try{PluginPackage.Install(directoryEntry,Path.Combine(root,"directory-entry-target"));}catch(InvalidDataException){directoryRejected=true;}
        if(!directoryRejected)throw new Exception("Directory accepted as plugin entry");
        var collision=Path.Combine(root,"extract-collision.zip");var collisionRoot=Path.Combine(root,"extract-collision");
        using(var zip=ZipFile.Open(collision,ZipArchiveMode.Create)){Write(zip,"plugin.json","{\"id\":\"fixture.clock\",\"name\":\"Test\",\"version\":\"0.1.0\",\"entry\":\"fixture.dll\"}");zip.CreateEntryFromFile(fixture,"fixture.dll");Write(zip,"folder","file blocks directory");Write(zip,"folder/child.txt","must not survive");}
        var extractFailed=false;try{PluginPackage.Install(collision,collisionRoot);}catch(IOException){extractFailed=true;}
        if(!extractFailed || Directory.EnumerateDirectories(collisionRoot).Any())throw new Exception("Mid-extraction failure left an unmanaged partial package");
        Console.WriteLine("PASS Mid-extraction failure cleans files through ownership receipt and preserves original IO failure");
        Console.WriteLine("PASS Plugin package malicious-input matrix rejects before destination creation");
    }
    private static void Write(ZipArchive zip,string name,string text){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(text);}
}

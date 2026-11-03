using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Capsule.App;

internal sealed record MarketPlugin(string Id,string Name,string Version,string Description,string DownloadUrl,string Sha256,string MinimumHostVersion,string Author,string License,string Category);
internal sealed record MarketCatalog(int Schema,MarketPlugin[] Packages);

internal sealed class PluginMarket(HttpClient http)
{
    internal static readonly Uri CatalogUri=new("https://raw.githubusercontent.com/Freedomzyi/LingdongCapsule/main/plugins/catalog.json");
    internal static Version VersionOf(string value)
    {
        if(value==null || value.Length>32 || !Version.TryParse(value,out var version))throw new InvalidDataException("组件版本无效。");
        return new(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision));
    }
    private static bool RepositoryUrl(Uri uri)=>uri.Scheme=="https" && uri.IsDefaultPort && uri.UserInfo.Length==0 && uri.Fragment.Length==0 &&
        ((uri.Host=="github.com" && uri.AbsolutePath.StartsWith("/Freedomzyi/LingdongCapsule/releases/download/",StringComparison.Ordinal)) ||
         (uri.Host=="raw.githubusercontent.com" && uri.AbsolutePath.StartsWith("/Freedomzyi/LingdongCapsule/",StringComparison.Ordinal)));
    private static bool RedirectUrl(Uri uri)=>RepositoryUrl(uri) || uri.Scheme=="https" && uri.IsDefaultPort && uri.UserInfo.Length==0 && uri.Fragment.Length==0 && uri.Host=="release-assets.githubusercontent.com";
    internal async Task<MarketPlugin[]> ReadAsync(CancellationToken token)
    {
        var bytes=await FetchAsync(CatalogUri,256*1024,false,token);
        var catalog=JsonSerializer.Deserialize<MarketCatalog>(bytes,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidDataException("市场目录为空。");
        if(catalog.Schema!=1 || catalog.Packages==null || catalog.Packages.Length>100)throw new InvalidDataException("市场目录格式或数量无效。");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in catalog.Packages)
        {
            if(item==null || item.Id==null || !Regex.IsMatch(item.Id,@"^[a-z][a-z0-9.-]{2,79}$") || !ids.Add(item.Id) || !Text(item.Name,80) || !Text(item.Description,500) || !Text(item.Author,80) || !Text(item.License,80) || !Text(item.Category,40) || !Regex.IsMatch(item.Sha256 ?? "",@"^[a-fA-F0-9]{64}$") || item.DownloadUrl==null || item.DownloadUrl.Length>2048 || !Uri.TryCreate(item.DownloadUrl,UriKind.Absolute,out var uri) || !RepositoryUrl(uri))throw new InvalidDataException("市场组件身份、来源或校验值无效。");
            _=VersionOf(item.Version);_=VersionOf(item.MinimumHostVersion);
        }
        return catalog.Packages;
    }
    private static bool Text(string value,int limit)=>!string.IsNullOrWhiteSpace(value) && value.Length<=limit;
    internal async Task<string> DownloadAsync(MarketPlugin package,string folder,Version host,CancellationToken token)
    {
        if(VersionOf(package.MinimumHostVersion)>VersionOf(host.ToString()))throw new InvalidOperationException("此组件需要更新版本的灵动胶囊。");
        if(!Uri.TryCreate(package.DownloadUrl,UriKind.Absolute,out var uri) || !RepositoryUrl(uri) || package.Sha256 is not {Length:64} digest || !Regex.IsMatch(digest,@"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("组件下载来源或校验值无效。");
        var bytes=await FetchAsync(uri,65*1024*1024,true,token);
        if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),Convert.FromHexString(digest)))throw new InvalidDataException("下载校验不一致，未安装组件。");
        using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))
        {
            var manifest=zip.GetEntry("plugin.json") ?? throw new InvalidDataException("下载包缺少清单。");
            if(manifest.Length>16384)throw new InvalidDataException("下载包清单超限。");
            using var stream=manifest.Open();using var reader=new StreamReader(stream);var text=new char[16385];var count=await reader.ReadBlockAsync(text,token);
            if(count==text.Length)throw new InvalidDataException("下载包实际清单超限。");
            var identity=JsonSerializer.Deserialize<PluginPackageInfo>(text.AsSpan(0,count),new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidDataException("下载包清单为空。");
            if(identity.Id!=package.Id || VersionOf(identity.Version)!=VersionOf(package.Version))throw new InvalidDataException("下载包身份或版本与市场不一致，未安装。");
        }
        token.ThrowIfCancellationRequested();Directory.CreateDirectory(folder);
        if(File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("下载目录不能是链接。");
        var path=Path.Combine(Path.GetFullPath(folder),"market-"+Guid.NewGuid().ToString("N")+".zip");
        try{await File.WriteAllBytesAsync(path,bytes,token);return path;}
        catch{if(File.Exists(path))File.Delete(path);throw;}
    }
    private async Task<byte[]> FetchAsync(Uri uri,int maximum,bool asset,CancellationToken token)
    {
        for(var hop=0;hop<6;hop++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("LingdongCapsule/0.1.0");
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location=response.Headers.Location ?? throw new InvalidDataException("下载重定向缺少目标。");
                uri=location.IsAbsoluteUri ? location : new Uri(uri,location);
                if(!asset || !RedirectUrl(uri))throw new InvalidDataException("市场请求重定向到未允许的来源。");continue;
            }
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException("市场响应体积超限。");
            using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16384];int count;
            while((count=await input.ReadAsync(buffer,token))>0){if(output.Length+count>maximum)throw new InvalidDataException("市场响应实际体积超限。");output.Write(buffer,0,count);}
            return output.ToArray();
        }
        throw new InvalidDataException("市场请求重定向次数过多。");
    }
}

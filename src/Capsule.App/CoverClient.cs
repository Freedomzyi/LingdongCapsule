using Capsule.Core;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace Capsule.App;

internal sealed class CoverClient : IDisposable
{
    private readonly LyricsClient search;
    private readonly NoticeImages images;
    internal CoverClient(HttpClient? metadata=null,HttpClient? pictures=null){search=new(metadata);images=new(pictures);}
    internal static Uri? NormalizeUrl(string value)
    {
        if(value.Length is <1 or >4096 || !Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.UserInfo.Length>0 || uri.Scheme is not("https" or "http"))return null;
        var host=uri.IdnHost;
        if(!new[]{"gtimg.cn","music.126.net","qpic.cn"}.Any(domain=>host.Equals(domain,StringComparison.OrdinalIgnoreCase) || host.EndsWith("."+domain,StringComparison.OrdinalIgnoreCase)))return null;
        var builder=new UriBuilder(uri){Scheme="https",Port=-1};
        builder.Path=builder.Path.Replace("R800x800M000","R300x300M000",StringComparison.Ordinal);
        if(host.EndsWith("music.126.net",StringComparison.OrdinalIgnoreCase) && !builder.Query.Contains("param=",StringComparison.OrdinalIgnoreCase))builder.Query=builder.Query.TrimStart('?')+(builder.Query.Length>1 ? "&" : "")+"param=300y300";
        return builder.Uri;
    }
    internal async Task<BitmapSource?> FetchAsync(MediaSnapshot track,CancellationToken token)
    {
        if(!track.Available || track.IsVideo || !track.Artist.Any(char.IsLetterOrDigit) || !track.Title.Any(char.IsLetterOrDigit) || track.Title=="未知曲目")return null;
        var net=track.SourceId.Contains("netease",StringComparison.OrdinalIgnoreCase) || track.SourceId.Contains("cloudmusic",StringComparison.OrdinalIgnoreCase);
        foreach(var provider in net ? new[]{"netease","netease-official","tencent","qq-official"} : new[]{"tencent","netease","netease-official","qq-official"})
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var url=NormalizeUrl(await search.CoverUrlAsync(provider,track,token));if(url==null)continue;
                var image=await images.ResolveAsync(new("cover","","","",Icon:url.AbsoluteUri),token);
                if(image!=null)return image.Bitmap;
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch(Exception error) when(error is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or FormatException or System.IO.IOException or System.IO.InvalidDataException or TaskCanceledException){ }
        }
        token.ThrowIfCancellationRequested();return null;
    }
    public void Dispose(){search.Dispose();images.Dispose();}
}

using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Capsule.Core;

namespace Capsule.App;

internal sealed class NoticeImages : IDisposable
{
    internal sealed record Image(BitmapSource Bitmap,bool Avatar,bool System=false);
    private readonly ConditionalWeakTable<Notice,Image> images=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly HttpClient client;
    private bool disposed;
    internal NoticeImages(HttpClient? http=null)=>client=http ?? new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,Credentials=null}){Timeout=TimeSpan.FromSeconds(3)};
    internal Image? Get(Notice notice)=>images.TryGetValue(notice,out var result) ? result : null;
    internal async Task<Image?> ResolveAsync(Notice notice,CancellationToken cancellationToken=default,string? systemAvatar=null,string? systemIcon=null)
    {
        if(disposed)return null;
        if(Get(notice) is {} cached)return cached;
        foreach(var (spec,avatar,system) in new[]{(notice.Avatar ?? systemAvatar,true,notice.Avatar==null && systemAvatar!=null),(notice.Icon ?? systemIcon,false,notice.Icon==null && systemIcon!=null)})
        {
            if(string.IsNullOrWhiteSpace(spec))continue;
            try
            {
                if(cancellationToken.IsCancellationRequested)return null;
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var bytes=await ReadAsync(spec,timeout.Token);if(bytes==null || disposed || timeout.IsCancellationRequested)continue;
                var bitmap=await Task.Run(()=>Decode(bytes),timeout.Token);if(bitmap==null || disposed || timeout.IsCancellationRequested)continue;
                var result=new Image(bitmap,avatar,system);images.Remove(notice);images.Add(notice,result);return result;
            }
            catch(Exception){ /* Invalid images fall through without dropping the notification. */ }
        }
        return null;
    }
    private async Task<byte[]?> ReadAsync(string spec,CancellationToken token)
    {
        const int limit=4*1024*1024;
        if(spec.Length>NoticeContent.MaxImageSpec)return null;
        if(spec.StartsWith("data:image/",StringComparison.OrdinalIgnoreCase))
        {
            var comma=spec.IndexOf(',');if(comma<0 || !spec[..comma].EndsWith(";base64",StringComparison.OrdinalIgnoreCase) || comma>128)return null;
            var bytes=Convert.FromBase64String(spec[(comma+1)..]);return bytes.Length<=limit ? bytes : null;
        }
        Stream stream;
        HttpResponseMessage? response=null;
        if(Uri.TryCreate(spec,UriKind.Absolute,out var uri) && uri.Scheme==Uri.UriSchemeHttps)
        {
            if(uri.UserInfo.Length>0)return null;
            response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength>limit){response.Dispose();return null;}
            try{stream=await response.Content.ReadAsStreamAsync(token);}catch{response.Dispose();throw;}
        }
        else
        {
            var path=uri?.IsFile==true ? uri.LocalPath : spec;
            if(!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\",StringComparison.Ordinal) || uri?.IsUnc==true)return null;
            stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        }
        using(response)
        using(stream)
        using(var output=new MemoryStream())
        {
            var buffer=new byte[16384];int count;
            while((count=await stream.ReadAsync(buffer,token))>0){if(output.Length+count>limit)return null;output.Write(buffer,0,count);}
            return output.ToArray();
        }
    }
    private static BitmapSource? Decode(byte[] bytes)
    {
        using var input=new MemoryStream(bytes,false);
        var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
        var frame=decoder.Frames[0];if(frame.PixelWidth<=0 || frame.PixelHeight<=0 || (long)frame.PixelWidth*frame.PixelHeight>16000000)return null;
        input.Position=0;var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=96;bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    public void Dispose(){if(disposed)return;disposed=true;lifetime.Cancel();client.Dispose();lifetime.Dispose();images.Clear();}
}

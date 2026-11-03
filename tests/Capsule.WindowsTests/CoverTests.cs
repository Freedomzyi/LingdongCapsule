using Capsule.App;
using Capsule.Core;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class CoverTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        internal List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests.Add(request.RequestUri!.AbsoluteUri);return send(request,token);}
    }
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
    private static byte[] Pixels(byte red)
    {
        var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,red,255},4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
    }
    private static HttpResponseMessage Picture(byte red)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Pixels(red))};
    internal static async Task RunAsync()
    {
        if(CoverClient.NormalizeUrl("http://p1.music.126.net/a.jpg")?.AbsoluteUri!="https://p1.music.126.net/a.jpg?param=300y300" || CoverClient.NormalizeUrl("https://evil.example/a.jpg")!=null || CoverClient.NormalizeUrl("https://music.126.net.evil.example/a.jpg")!=null || CoverClient.NormalizeUrl("https://user:pass@y.gtimg.cn/a.jpg")!=null)throw new Exception("Cover CDN and HTTPS rules failed");
        var track=new MediaSnapshot("Owned title","Owned artist",true,true,Duration:TimeSpan.FromSeconds(30),SourceId:"qqmusic.exe");
        using(var metadata=new HttpClient(new Handler((request,_)=>Task.FromResult(request.RequestUri!.AbsolutePath.Contains("tencent") ? Json(new{data=new[]{new{id=1,song=track.Title,singer=track.Artist,interval="30",cover="https://y.gtimg.cn/bad.jpg"}}}) : Json(new{data=new[]{new{id=2,song=track.Title,singer=track.Artist,interval="30",cover="https://p1.music.126.net/good.jpg"}}})))))
        using(var pictures=new HttpClient(new Handler((request,_)=>Task.FromResult(request.RequestUri!.AbsolutePath.Contains("bad") ? new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("not an image")} : Picture(200)))))
        using(var client=new CoverClient(metadata,pictures))
        {
            if(await client.FetchAsync(track,default) is not {IsFrozen:true})throw new Exception("Bad first cover did not fall back to matched second provider");
            if(await client.FetchAsync(track with{IsVideo=true},default)!=null)throw new Exception("Video triggered music cover search");
        }
        await TestIslandAsync();
        await TestOfficialAsync(track);
        Console.WriteLine("PASS Matched cover sources, HTTPS/CDN filtering, malformed-image fallback and real island late-result isolation");
    }
    private static async Task TestOfficialAsync(MediaSnapshot track)
    {
        using var netHandler=new Handler((request,_)=>Task.FromResult(request.RequestUri!.Host=="api.vkeys.cn" ? Json(new{data=Array.Empty<object>()}) : request.Method==HttpMethod.Post ? Json(new{result=new{songs=new[]{new{id=42,name=track.Title,artists=new[]{new{name=track.Artist}},duration=30000}}}}) : Json(new{songs=new[]{new{id=42,album=new{picUrl="https://p1.music.126.net/official.jpg"}}}})));
        using var netHttp=new HttpClient(netHandler);using var netPictures=new HttpClient(new Handler((_,_)=>Task.FromResult(Picture(140))));using var net=new CoverClient(netHttp,netPictures);
        if(await net.FetchAsync(track with{SourceId="cloudmusic.exe"},default)==null || netHandler.Requests.Count!=3 || !netHandler.Requests[0].Contains("/netease?"))throw new Exception("Official Netease cover or player-specific priority failed");
        var mismatch=false;
        using var qqHandler=new Handler((request,_)=>
        {
            var uri=request.RequestUri!;
            if(uri.Host=="api.vkeys.cn")return Task.FromResult(Json(new{data=Array.Empty<object>()}));
            if(uri.Host=="music.163.com")return Task.FromResult(Json(new{result=new{songs=Array.Empty<object>()}}));
            if(uri.AbsolutePath.Contains("smartbox"))return Task.FromResult(Json(new{data=new{song=new{itemlist=new[]{new{name=track.Title,singer=track.Artist,mid="OwnedMid"}}}}}));
            return Task.FromResult(Json(new{data=new[]{new{name=track.Title,singer=new[]{new{name=track.Artist}},interval=mismatch ? 90 : 30,album=new{mid="OwnedAlbum"}}}}));
        });
        using var qqHttp=new HttpClient(qqHandler);using var pictureHandler=new Handler((request,_)=>{if(!request.RequestUri!.AbsolutePath.EndsWith("T002R300x300M000OwnedAlbum.jpg"))throw new Exception("QQ album identifier did not map to expected CDN image");return Task.FromResult(Picture(150));});using var qqPictures=new HttpClient(pictureHandler);using var qq=new CoverClient(qqHttp,qqPictures);
        if(await qq.FetchAsync(track,default)==null || pictureHandler.Requests.Count!=1)throw new Exception("QQ official album fallback failed");
        mismatch=true;if(await qq.FetchAsync(track,default)!=null || pictureHandler.Requests.Count!=1)throw new Exception("Wrong-duration QQ album reached image download");
    }
    private static async Task TestIslandAsync()
    {
        using var publisher=await OwnedMediaPublisher.StartAsync();publisher.SetMetadata("Owned cover first",false);
        var firstImage=new TaskCompletionSource();var delayed=new TaskCompletionSource();
        using var metadata=new HttpClient(new Handler((_,_)=>Task.FromResult(Json(new{data=new[]{new{id=1,song=publisher.Title,singer="Capsule test artist",interval="30",cover="https://y.gtimg.cn/"+(publisher.Title.EndsWith("first") ? "first" : "second")+".jpg"}}}))));
        using var pictures=new HttpClient(new Handler(async(request,_)=>{if(request.RequestUri!.AbsolutePath.Contains("first")){firstImage.TrySetResult();await delayed.Task;return Picture(20);}return Picture(220);}));
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();
        var settings=new Settings{NotificationsEnabled=false,LyricsEnabled=false,ShowSpectrum=false,ManualMediaSource="LingdongCapsule.Owned.bilibili"};
        var window=new MainWindow(suppressOnboarding:true,initialSettings:settings,coverMetadata:metadata,coverPictures:pictures){ShowActivated=false};
        try
        {
            window.Show();await firstImage.Task.WaitAsync(TimeSpan.FromSeconds(10));publisher.SetMetadata("Owned cover second",false);
            for(var attempt=0;attempt<60 && window.EffectiveCover==null;attempt++)await Task.Delay(100);
            var cover=window.EffectiveCover ?? throw new Exception("Current native track did not acquire a network cover");
            var converted=new FormatConvertedBitmap(cover,PixelFormats.Bgra32,null,0);var pixel=new byte[4];converted.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);if(pixel[2]!=220)throw new Exception("Current cover is not the second track's generated pixels");
            delayed.TrySetResult();await Task.Delay(300);
            if(!ReferenceEquals(cover,window.EffectiveCover))throw new Exception("Old image replaced newer track artwork");
            if(((System.Windows.Controls.Image)window.FindName("CoverArt")).Source!=cover)throw new Exception("Resolved cover did not reach actual WPF artwork");
            settings.OnlineCoversEnabled=false;if(ReferenceEquals(window.EffectiveCover,cover))throw new Exception("Disabling online artwork retained the downloaded cover");
            settings.MediaEnabled=false;
            if(window.EffectiveCover!=null)throw new Exception("Disabled media retained visible artwork");
        }
        finally{delayed.TrySetResult();window.Close();}
    }
    internal static async Task RunLiveAsync()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));using var client=new CoverClient();
        var cover=await client.FetchAsync(new("晴天","周杰伦",true,true,Duration:TimeSpan.FromSeconds(269),SourceId:"qqmusic.exe"),timeout.Token);
        if(cover==null || !cover.IsFrozen || cover.PixelWidth<=0)throw new Exception("Public cover lookup/download did not return a decodable thumbnail");
        Console.WriteLine("PASS Public track metadata resolved and downloaded a frozen thumbnail; dimensions="+cover.PixelWidth+"x"+cover.PixelHeight+"; no image file or player state written");
    }
}

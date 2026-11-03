using Capsule.App;
using Capsule.Core;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

internal static class LyricsTests
{
    private const string Lrc="[00:01.00]Own first test line\n[00:03.00]Own second test line";
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply) : HttpMessageHandler
    {
        internal List<string> Paths{get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Paths.Add(request.RequestUri!.Host+request.RequestUri.AbsolutePath);return Task.FromResult(reply(request));}
    }
    private sealed class SlowHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started{get;}=new();internal int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests++;Started.TrySetResult();await Task.Delay(30000,token);return Json(new{});}
    }
    private static HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data))};
    internal static async Task RunAsync()
    {
        var track=new MediaSnapshot("Test song","Test artist",true,true,Duration:TimeSpan.FromSeconds(269),SourceId:"qqmusic.exe");
        using(var handler=new Handler(request=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("search/song"))return Json(new{data=new object[]{new{song="Test song",singer="Test artist"},new{id=1,song="Test song",singer="Test artist",interval="4分29秒",mid="TestMid0001"}}});
            if(path.Contains("/tencent/lyric"))return Json(new{data=new{lrc="[00:00.00]  ",trans="",yrc=""}});
            if(request.RequestUri.Host=="c.y.qq.com" && path.Contains("query_lyric")){if(request.Headers.Referrer?.Host!="y.qq.com" || !request.RequestUri.Query.Contains("TestMid0001"))throw new Exception("QQ mid/referrer was not preserved");return Json(new{code=0,lyric=Lrc,trans="[00:01.00]自写测试译文"});}
            throw new Exception("Unexpected lyric source before QQ fallback: "+path);
        }))
        using(var http=new HttpClient(handler))using(var lyrics=new LyricsClient(http))
        {
            var result=await lyrics.FetchAsync(track,default);
            if(result.Source!="QQ 音乐" || result.Lines.Count!=2 || result.Lines[0].Translation!="自写测试译文" || handler.Paths.Count!=3)throw new Exception("QQ relay failure did not fall back using matched mid");
            if((await lyrics.FetchAsync(track with{Artist=""},default)).Lines.Count!=0 || handler.Paths.Count!=3 || lyrics.Status!="等待音乐")throw new Exception("Unavailable lyric identity retained a previous connected status or made a request");
        }
        using(var handler=new Handler(request=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(request.RequestUri.Host=="api.vkeys.cn")return new(HttpStatusCode.ServiceUnavailable);
            if(path.Contains("smartbox"))return Json(new{data=new{song=new{itemlist=new[]{new{name="Test song",singer="Test artist",mid="TestMid0001"}}}}});
            if(path.Contains("play_single"))return Json(new{data=new[]{new{name="Test song",interval=269,singer=new[]{new{name="Test artist"}}}}});
            if(path.Contains("query_lyric"))return Json(new{code=0,lyric=Convert.ToBase64String(Encoding.UTF8.GetBytes(Lrc)),trans=""});
            throw new Exception("Unexpected QQ discovery fallback");
        }))
        using(var http=new HttpClient(handler))using(var lyrics=new LyricsClient(http))
        {
            var result=await lyrics.FetchAsync(track,default);if(result.Source!="QQ 音乐" || result.Lines.Count!=2 || handler.Paths.Count!=4)throw new Exception("QQ search/detail/base64 fallback failed");
        }
        using(var handler=new Handler(request=>
        {
            if(request.RequestUri!.Host=="api.vkeys.cn")return Json(new{data=Array.Empty<object>()});
            if(request.RequestUri.Host=="music.163.com")
            {
                if(request.Headers.Referrer?.Host!="music.163.com")throw new Exception("Missing Netease referrer");
                return request.Method==HttpMethod.Post ? Json(new{result=new{songs=new[]{new{id=2,name="Test song",artists=new[]{new{name="Test artist"}},duration=269000}}}}) : Json(new{lrc=new{lyric=Lrc},tlyric=new{lyric=""}});
            }
            throw new Exception("Netease priority fell through to another provider");
        }))
        using(var http=new HttpClient(handler))using(var lyrics=new LyricsClient(http))
        {
            var result=await lyrics.FetchAsync(track with{SourceId="cloudmusic.exe"},default);
            if(result.Source!="网易云" || result.Lines.Count!=2 || handler.Paths.Count!=3 || !handler.Paths[0].EndsWith("/netease"))throw new Exception("Netease provider ordering incorrect");
        }
        using(var handler=new Handler(request=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.Contains("tencent/search"))return Json(new{data=new[]{new{id=1,song="Test song",singer="Test artist",interval="6分00秒",mid="WrongMid"}}});
            if(path.Contains("smartbox"))return Json(new{data=new{song=new{itemlist=new[]{new{name="Test song",singer="Test artist",mid="OtherMid"}}}}});
            if(path.Contains("play_single"))return Json(new{data=new[]{new{name="Test song",interval=360}}});
            if(request.RequestUri.Host=="api.vkeys.cn")return Json(new{data=Array.Empty<object>()});
            if(request.RequestUri.Host=="music.163.com")return Json(new{result=new{songs=Array.Empty<object>()}});
            if(request.RequestUri.Host=="lrclib.net")return Json(new{syncedLyrics=Lrc});
            throw new Exception("A duration-mismatched candidate reached its lyric endpoint");
        }))
        using(var http=new HttpClient(handler))using(var lyrics=new LyricsClient(http))
        {
            var result=await lyrics.FetchAsync(track,default);if(result.Source!="LRCLIB")throw new Exception("Duration mismatch did not preserve source fallback");
        }
        using(var handler=new Handler(request=>
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.Contains("tencent/search"))return new(HttpStatusCode.OK){Content=new StringContent(new string('x',2*1024*1024+1))};
            if(path.Contains("smartbox"))return Json(new{data=new{song=new{itemlist=Array.Empty<object>()}}});
            if(request.RequestUri.Host=="api.vkeys.cn")return Json(new{data=Array.Empty<object>()});
            if(request.RequestUri.Host=="music.163.com")return Json(new{result=new{songs=Array.Empty<object>()}});
            if(request.RequestUri.Host=="lrclib.net")return Json(new{syncedLyrics=Lrc});
            throw new Exception("Unexpected bounded fallback source");
        }))using(var http=new HttpClient(handler))using(var lyrics=new LyricsClient(http))
        {
            if((await lyrics.FetchAsync(track,default)).Source!="LRCLIB")throw new Exception("Oversized lyric response prevented remaining provider fallback");
        }
        using(var slow=new SlowHandler())using(var http=new HttpClient(slow))using(var lyrics=new LyricsClient(http))using(var cancel=new CancellationTokenSource())
        {
            var pending=lyrics.FetchAsync(track,cancel.Token);await slow.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();var canceled=false;
            try{await pending;}catch(OperationCanceledException){canceled=true;}
            if(!canceled || slow.Requests!=1)throw new Exception("Canceled lyric request tried other providers");
        }
        Console.WriteLine("PASS Empty/oversized relay fallback, QQ mid/discovery/base64, Netease priority, duration/version rejection and in-flight cancellation");
    }
    internal static async Task RunLiveAsync()
    {
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};using var lyrics=new LyricsClient(http);
        var result=await lyrics.QqOfficialAsync(new("晴天","周杰伦",true,true,Duration:TimeSpan.FromSeconds(269)),"",default);
        if(result.Lines.Count<2)throw new Exception("QQ official live lookup returned no synchronized lyrics");
        Console.WriteLine($"PASS Live QQ official search/detail/lyrics: lines={result.Lines.Count}, source={result.Source}; no player session was controlled");
    }
}

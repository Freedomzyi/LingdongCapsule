using Capsule.App;
using Capsule.Core;
using Msp.Core;
using System.IO;
using System.Text.Json;

internal static class MspTests
{
    internal static async Task RunAsync()
    {
        var directory=Path.Combine(Environment.CurrentDirectory,"artifacts","msp-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var imageSpec=await TestContentAsync(directory);
        Notice? received=null;string? command=null;
        using var bridge=new MspBridge(notice=>{received=notice;return Task.FromResult(true);},()=>Task.FromResult(new MediaSnapshot("Test track","Test artist",true,true)),action=>{command=action;return Task.FromResult(true);});
        if(!bridge.Start(directory))throw new Exception(bridge.Error);
        var client=new MspNode("capsule-test-peer",nodesDir:directory);
        var events=new System.Collections.Concurrent.ConcurrentQueue<string>();
        client.On("media.**",signal=>events.Enqueue(signal.Path+signal.Payload.GetRawText()));
        client.On("notify.toast.raised",signal=>events.Enqueue(signal.Path+signal.Payload.GetRawText()));
        try
        {
            client.DeclareSignal("notify.capsule.fixture");
            client.Start();string? ping=null;
            for(var attempt=0;attempt<30;attempt++)
            {
                try{ping=JsonSerializer.Serialize((await client.CallAsync("lingdongcapsule","ping",new{})).Unwrap());if(ping.Contains("lingdongcapsule"))break;}catch(Exception){ }
                await Task.Delay(200);
            }
            if(ping==null || !ping.Contains("lingdongcapsule"))throw new Exception("MSP peers did not exchange ping");
            bridge.PublishMedia(new("A","Artist",true,true));bridge.PublishMedia(new("B","Artist",false,true));
            bridge.PublishNotice(new("fixture","Test app","secret title","secret body"),privatePreview:true);
            for(var attempt=0;attempt<30 && events.Count<3;attempt++)await Task.Delay(100);
            if(!events.Any(item=>item.StartsWith("media.track.changed")) || !events.Any(item=>item.StartsWith("media.playback.changed")) || !events.Any(item=>item.StartsWith("notify.toast.raised")) || events.Any(item=>item.Contains("secret")))throw new Exception("MSP event delivery or privacy redaction failed");
            using(var imageResolver=new NoticeImages())
            {
                var image=await imageResolver.ResolveAsync(new("fixture","Fixture","Pixels","",Avatar:imageSpec));
                bridge.PublishNotice(new("fixture","Fixture","Public pixels","",Avatar:@"C:\private\avatar"),false,image);
                for(var attempt=0;attempt<30 && !events.Any(item=>item.Contains("Public pixels"));attempt++)await Task.Delay(100);
                var encoded=events.FirstOrDefault(item=>item.Contains("Public pixels"));
                if(encoded==null || !encoded.Contains("image/png") || !encoded.Contains("avatar") || encoded.Contains("private"))throw new Exception("Image event lost bytes/role or leaked source path");
                bridge.PublishNotice(new("fixture","Fixture","secret pixels","secret body"),true,image);
                for(var attempt=0;attempt<30 && events.Count<5;attempt++)await Task.Delay(100);
                var masked=events.Last();
                if(masked.Contains("secret") || masked.Contains("image/png"))throw new Exception("Privacy event leaked image content");
            }
            await client.CallAsync("lingdongcapsule","notify",new{title="MSP test",body="Independent local fixture"});
            if(received?.Title!="MSP test")throw new Exception("MSP notification did not reach adapter");
            client.Publish("notify.capsule.fixture",new{title="Subscribed fixture",body="Broadcast test only"});
            for(var attempt=0;attempt<30 && received?.Title!="Subscribed fixture";attempt++)await Task.Delay(100);
            if(received?.Title!="Subscribed fixture" || received.SourceId!="msp:capsule-test-peer")throw new Exception("MSP subscription source identity was lost");
            var blocks=new[]{new{type="image",data=imageSpec[(imageSpec.IndexOf(',')+1)..],mimeType="image/png",annotations=new{role="portrait"}}};
            await client.CallAsync("lingdongcapsule","notify",new{title="Image tool",body="",content=blocks});
            if(received?.Avatar!=imageSpec)throw new Exception("MSP tool discarded image content or role annotations");
            client.Publish("notify.capsule.fixture",new{title="Image broadcast",content=blocks});
            for(var attempt=0;attempt<30 && received?.Title!="Image broadcast";attempt++)await Task.Delay(100);
            if(received?.Avatar!=imageSpec)throw new Exception("MSP subscription discarded avatar content");
            await client.CallAsync("lingdongcapsule","media_control",new{action="prev"});if(command!="previous")throw new Exception("Media alias not normalized");
            command=null;await client.CallAsync("lingdongcapsule","media_control",new{action="invalid"});if(command!=null)throw new Exception("Unlisted media command reached controller");
            var status=JsonSerializer.Serialize((await client.CallAsync("lingdongcapsule","media_status",new{})).Unwrap());if(!status.Contains("Test track"))throw new Exception("Media status missing");
            Console.WriteLine("PASS Independent MSP peers exchange ping, notification, media status and allowlisted control");
        }
        finally{client.Stop();}
        await TestIslandAsync(Path.Combine(directory,"island"),imageSpec);
    }
    private static async Task<string> TestContentAsync(string directory)
    {
        var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(2,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{0,0,255,255,0,255,0,255},8);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);
        var spec="data:image/png;base64,"+Convert.ToBase64String(stream.ToArray());
        var parsed=NoticeContent.Parse(JsonSerializer.SerializeToElement(new{content=new object[]{new{type="resource_link",uri="icon",annotations=new{role="icon"}},new{type="image",data=spec[(spec.IndexOf(',')+1)..],mimeType="image/png",annotations=new{role="AVATAR"}},new{type="text",text="Content fallback"},new{type="audio",data="ignored"}}}));
        if(parsed?.Avatar!=spec || parsed.Icon!="icon" || parsed.Title!="Content fallback" || parsed.Body!="Content fallback")throw new Exception("Content role precedence or text fallback failed");
        if(NoticeContent.Parse(JsonSerializer.SerializeToElement(new{content=Enumerable.Range(0,65).Select(_=>new{type="text",text="x"})}))!=null || NoticeContent.Parse(JsonSerializer.SerializeToElement(new{title=new string('x',4097)}))!=null)throw new Exception("Content count/text bounds not enforced");
        using var images=new NoticeImages();
        var notice=new Notice("fixture","Fixture","Image","",Avatar:spec,Icon:"invalid");
        var avatar=await images.ResolveAsync(notice);if(avatar?.Avatar!=true || !avatar.Bitmap.IsFrozen)throw new Exception("Inline avatar did not decode into a frozen image");
        var icon=await images.ResolveAsync(notice with{Avatar="data:image/png;base64,broken",Icon=spec});if(icon?.Avatar!=false)throw new Exception("Malformed avatar did not fall back to icon");
        var path=Path.Combine(directory,"extensionless-image");await File.WriteAllBytesAsync(path,stream.ToArray());
        if(await images.ResolveAsync(notice with{Avatar=null,Icon=new Uri(path).AbsoluteUri}) is not {Avatar:false})throw new Exception("Local resource URI was not decoded by content");
        if(await images.ResolveAsync(notice with{Avatar="http://example.invalid/image",Icon=@"\\invalid\share\image"})!=null)throw new Exception("Non-HTTPS or network-share image was loaded");
        using(var remote=new NoticeImages(new System.Net.Http.HttpClient(new ImageHandler((_,_)=>Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.ByteArrayContent(stream.ToArray())})))))
            if(await remote.ResolveAsync(notice with{Avatar="https://example.invalid/image"}) is not {Avatar:true})throw new Exception("HTTPS image response did not decode");
        using(var oversize=new NoticeImages(new System.Net.Http.HttpClient(new ImageHandler((_,_)=>{var content=new System.Net.Http.ByteArrayContent([]);content.Headers.ContentLength=4*1024*1024+1;return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=content});}))))
            if(await oversize.ResolveAsync(notice with{Avatar="https://example.invalid/image",Icon=null})!=null)throw new Exception("Oversized image response was accepted");
        var entered=new TaskCompletionSource();
        using(var pending=new NoticeImages(new System.Net.Http.HttpClient(new ImageHandler(async(_,token)=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);return new System.Net.Http.HttpResponseMessage();}))))
        using(var cancel=new CancellationTokenSource())
        {
            var task=pending.ResolveAsync(notice with{Avatar="https://example.invalid/image",Icon=null},cancel.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();
            if(await task.WaitAsync(TimeSpan.FromSeconds(2))!=null)throw new Exception("Canceled image returned a bitmap");
        }
        images.Dispose();if(await images.ResolveAsync(notice)!=null)throw new Exception("Disposed image resolver retained access");
        Console.WriteLine("PASS Content roles/text/count bounds, inline/extensionless images, malformed-avatar fallback and shutdown");return spec;
    }
    private sealed class ImageHandler(Func<System.Net.Http.HttpRequestMessage,CancellationToken,Task<System.Net.Http.HttpResponseMessage>> send):System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);
    }
    private static async Task TestIslandAsync(string directory,string imageSpec)
    {
        Directory.CreateDirectory(directory);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();Design.ApplySystemTheme();
        var settings=new Settings{MspEnabled=true,MediaEnabled=false,NotificationsEnabled=true};
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:settings,localMspDirectory:directory);
        var peer=new MspNode("island-test-peer",nodesDir:directory);
        var broadcasts=new System.Collections.Concurrent.ConcurrentQueue<JsonElement>();peer.On("notify.toast.raised",signal=>broadcasts.Enqueue(signal.Payload.Clone()));
        try
        {
            window.Show();peer.Start();bool reached=false;
            for(var attempt=0;attempt<30;attempt++)
            {
                try{var ping=JsonSerializer.Serialize((await peer.CallAsync("lingdongcapsule","ping",new{})).Unwrap());if(ping.Contains("lingdongcapsule")){reached=true;break;}}catch(Exception){ }
                await Task.Delay(200);
            }
            if(!reached)throw new Exception("Actual island MSP node was not discovered");
            await peer.CallAsync("lingdongcapsule","notify",new{title="Island MSP fixture",body="Local test only"});
            if(window.LiveState.Notification?.Title!="Island MSP fixture")throw new Exception("Peer notification did not reach actual island state");
            await peer.CallAsync("lingdongcapsule","notify",new{title="Island avatar",body="Local generated pixels",content=new[]{new{type="image",data=imageSpec[(imageSpec.IndexOf(',')+1)..],mimeType="image/png",annotations=new{role="avatar"}}}});
            var icon=(System.Windows.Controls.Image)window.FindName("NoticeIcon");var frame=(System.Windows.FrameworkElement)window.FindName("NoticeIconFrame");
            for(var attempt=0;attempt<30 && frame.Width!=settings.NotificationIconSize+5;attempt++)await Task.Delay(100);
            if(frame.Width!=settings.NotificationIconSize+5 || icon.Source is not System.Windows.Media.Imaging.BitmapSource || icon.Clip is not System.Windows.Media.RectangleGeometry clip || clip.RadiusX!=frame.Width/2 || icon.Stretch!=System.Windows.Media.Stretch.UniformToFill)throw new Exception("Actual island did not render avatar size, crop and circular clip");
            if(!broadcasts.IsEmpty)throw new Exception("Default-off broadcast emitted or incoming MSP was echoed");
            settings.MspPublishEvents=true;settings.PrivatePreview=false;
            window.AcceptNotification(new("mspaint.exe","Owned fixture","UI public image","",Avatar:imageSpec),DateTimeOffset.Now);
            for(var attempt=0;attempt<30 && broadcasts.IsEmpty;attempt++)await Task.Delay(100);
            if(!broadcasts.TryDequeue(out var outbound) || outbound.GetProperty("title").GetString()!="UI public image" || outbound.GetProperty("content").GetArrayLength()!=1)throw new Exception("Actual island failed to publish resolved image or mistook mspaint for MSP");
            settings.PrivatePreview=true;
            window.AcceptNotification(new("fixture","Owned fixture","UI hidden image","hidden",Avatar:imageSpec),DateTimeOffset.Now);
            for(var attempt=0;attempt<30 && broadcasts.IsEmpty;attempt++)await Task.Delay(100);
            if(!broadcasts.TryDequeue(out var privateEvent) || privateEvent.GetProperty("title").GetString()!="" || privateEvent.GetProperty("content").GetArrayLength()!=0)throw new Exception("Actual island private broadcast exposed image or text");
            settings.QuietUntil=DateTimeOffset.UtcNow.AddMinutes(30);
            var muted=JsonSerializer.Serialize((await peer.CallAsync("lingdongcapsule","notify",new{title="Should be muted",body=""})).Unwrap());
            if(window.LiveState.Notification!=null || !muted.Contains("false"))throw new Exception("MSP notification bypassed quiet preference");
            Console.WriteLine("PASS Actual island receives MSP notification and rejects it during quiet period");
        }
        finally{peer.Stop();window.Close();}
    }
}

using Capsule.Core;
using Msp.Core;
using System.Text.Json;

namespace Capsule.App;

internal sealed class MspBridge : IDisposable
{
    private MspNode? node;
    private int noticeSequence;
    private MediaSnapshot? previousMedia;
    private readonly Func<Notice,Task<bool>> notify;
    private readonly Func<Task<MediaSnapshot>> readMedia;
    private readonly Func<string,Task<bool>> control;
    public bool Enabled=>node!=null;
    internal int Generation{get;private set;}
    public string Error{get;private set;}="";
    public MspBridge(Func<Notice,Task<bool>> notify,Func<Task<MediaSnapshot>> readMedia,Func<string,Task<bool>> control){this.notify=notify;this.readMedia=readMedia;this.control=control;}
    public bool Start(string directory)
    {
        if(node!=null)return true;
        var role=new Preset(Name:"LOCAL_SOFTWARE",Discovery:new HashSet<string>{"directory"},Presence:new HashSet<string>{"directory"},Transports:new HashSet<string>{"tcp"},Patterns:new HashSet<string>{"msp/request-reply","msp/publish-subscribe"},PrimaryTransport:"tcp",Capabilities:new HashSet<string>{"pub","sub","req","rep"},ConnectPolicy:"eager");
        var candidate=new MspNode("lingdongcapsule",role,directory);
        try
        {
            foreach(var path in new[]{"notify.toast.raised","notify.lingdongcapsule.reminder.raised","media.track.changed","media.playback.changed"})candidate.DeclareSignal(path);
            candidate.On("notify.**",signal=>
            {
                if(signal.Source=="lingdongcapsule")return;
                _=ReceiveSignalAsync(signal.Source ?? "MSP",signal.Payload.Clone());
            });
            candidate.Tool("ping",()=>new{node="lingdongcapsule",active=true},"胶囊节点健康检查");
            candidate.Tool("notify",(Func<string,string,List<JsonElement>?,Task<object>>)NotifyAsync,"在胶囊显示通知；content 支持 text、image、resource_link，annotations.role=avatar/icon；遵守来源、隐私与安静设置");
            candidate.Tool("media_status",(Func<Task<object>>)(async()=>{var media=await readMedia();return new{active=media.Available,playing=media.Playing,title=media.Title,artist=media.Artist};}),"读取当前媒体状态");
            candidate.Tool("media_control",(Func<string,Task<object>>)(async action=>
            {
                var normalized=action?.Trim().ToLowerInvariant() ?? "";if(normalized=="prev")normalized="previous";
                if(normalized is not ("toggle" or "play" or "pause" or "previous" or "next"))return new{ok=false,error="不支持的媒体动作"};
                return new{ok=await control(normalized)};
            }),"控制播放、暂停、切歌");
            candidate.Start();node=candidate;Generation++;previousMedia=null;Error="";return true;
        }
        catch(Exception error){try{candidate.Stop();}catch{ }Error="MSP 启动失败："+error.GetType().Name;return false;}
    }
    public void PublishMedia(MediaSnapshot current)
    {
        var previous=previousMedia;previousMedia=current;if(node==null || previous==null)return;
        if(previous.Title!=current.Title || previous.Artist!=current.Artist || previous.SourceId!=current.SourceId)Publish("media.track.changed",new{title=current.Title,artist=current.Artist,app=current.SourceId,playing=current.Playing});
        if(previous.Playing!=current.Playing || previous.Available!=current.Available)Publish("media.playback.changed",new{active=current.Available,playing=current.Playing,title=current.Title,artist=current.Artist});
    }
    public void PublishNotice(Notice notice,bool privatePreview,NoticeImages.Image? image=null)
    {
        if(notice.SourceId.Equals("msp",StringComparison.OrdinalIgnoreCase) || notice.SourceId.StartsWith("msp:",StringComparison.OrdinalIgnoreCase))return;
        var content=new List<object>();
        if(!privatePreview && image!=null)
        {
            try
            {
                // Publish decoded thumbnail bytes; never send local paths or remote image URLs.
                var bitmap=image.Bitmap;
                if(bitmap.PixelWidth>0 && bitmap.PixelHeight>0 && (long)bitmap.PixelWidth*bitmap.PixelHeight<=65536)
                {
                    var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var buffer=new System.IO.MemoryStream();png.Save(buffer);
                    if(buffer.Length<=256*1024)content.Add(new{type="image",mimeType="image/png",data=Convert.ToBase64String(buffer.ToArray()),annotations=new{role=image.Avatar ? "avatar" : "icon"}});
                }
            }
            catch(Exception){ /* Preserve the text event if a thumbnail cannot be encoded. */ }
        }
        Publish(notice.SourceId=="self" ? "notify.lingdongcapsule.reminder.raised" : "notify.toast.raised",new{app=notice.App,source=notice.SourceId,title=privatePreview ? "" : notice.Title,body=privatePreview ? "" : notice.Body,content});
    }
    private void Publish(string path,object payload){try{node?.Publish(path,payload);}catch(Exception error){Error="MSP 广播失败："+error.GetType().Name;}}
    private async Task<object> NotifyAsync(string title="",string body="",List<JsonElement>? content=null)
    {
        var parsed=NoticeContent.Parse(JsonSerializer.SerializeToElement(new{title,body,content}));
        if(parsed==null)return new{ok=false,error="通知内容无效或超限"};
        return new{ok=await notify(new("msp","MSP",parsed.Title,parsed.Body,unchecked((uint)Interlocked.Increment(ref noticeSequence)),Avatar:parsed.Avatar,Icon:parsed.Icon))};
    }
    private async Task ReceiveSignalAsync(string peer,JsonElement payload)
    {
        try
        {
            if(payload.ValueKind!=JsonValueKind.Object)return;
            var parsed=NoticeContent.Parse(payload);if(parsed==null || peer.Length>128)return;
            await notify(new("msp:"+peer,peer,parsed.Title,parsed.Body,unchecked((uint)Interlocked.Increment(ref noticeSequence)),Avatar:parsed.Avatar,Icon:parsed.Icon));
        }
        catch(Exception){ /* A stale UI dispatcher or invalid peer message must not stop the node. */ }
    }
    public void Dispose(){var active=node;node=null;Generation++;previousMedia=null;try{active?.Stop();}catch(Exception error){Error="MSP 停止失败："+error.GetType().Name;}}
}

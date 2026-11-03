using Capsule.Core;
using Msp.Core;

namespace Capsule.App;

internal sealed class MspBridge : IDisposable
{
    private MspNode? node;
    private readonly Func<Notice,Task<bool>> notify;
    private readonly Func<Task<MediaSnapshot>> readMedia;
    private readonly Func<string,Task<bool>> control;
    public bool Enabled=>node!=null;
    public string Error{get;private set;}="";
    public MspBridge(Func<Notice,Task<bool>> notify,Func<Task<MediaSnapshot>> readMedia,Func<string,Task<bool>> control){this.notify=notify;this.readMedia=readMedia;this.control=control;}
    public bool Start(string directory)
    {
        if(node!=null)return true;
        var role=new Preset(Name:"LOCAL_SOFTWARE",Discovery:new HashSet<string>{"directory"},Presence:new HashSet<string>{"directory"},Transports:new HashSet<string>{"tcp"},Patterns:new HashSet<string>{"msp/request-reply","msp/publish-subscribe"},PrimaryTransport:"tcp",Capabilities:new HashSet<string>{"pub","sub","req","rep"},ConnectPolicy:"eager");
        var candidate=new MspNode("lingdongcapsule",role,directory);
        try
        {
            candidate.Tool("ping",()=>new{node="lingdongcapsule",active=true},"胶囊节点健康检查");
            candidate.Tool("notify",(Func<string,string,Task<object>>)(async(title,body)=>
            {
                title??="";body??="";if(title.Length>4096 || body.Length>16384)return new{ok=false,error="通知长度超限"};
                return new{ok=await notify(new("msp","MSP",title,body))};
            }),"在胶囊显示通知；遵守来源、隐私与安静设置");
            candidate.Tool("media_status",(Func<Task<object>>)(async()=>{var media=await readMedia();return new{active=media.Available,playing=media.Playing,title=media.Title,artist=media.Artist};}),"读取当前媒体状态");
            candidate.Tool("media_control",(Func<string,Task<object>>)(async action=>
            {
                var normalized=action?.Trim().ToLowerInvariant() ?? "";if(normalized=="prev")normalized="previous";
                if(normalized is not ("toggle" or "play" or "pause" or "previous" or "next"))return new{ok=false,error="不支持的媒体动作"};
                return new{ok=await control(normalized)};
            }),"控制播放、暂停、切歌");
            candidate.Start();node=candidate;Error="";return true;
        }
        catch(Exception error){try{candidate.Stop();}catch{ }Error="MSP 启动失败："+error.GetType().Name;return false;}
    }
    public void Dispose(){var active=node;node=null;try{active?.Stop();}catch(Exception error){Error="MSP 停止失败："+error.GetType().Name;}}
}

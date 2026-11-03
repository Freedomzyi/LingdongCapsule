using Capsule.Core;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Control;

namespace Capsule.App;

public sealed class MediaService : IDisposable
{
    private readonly Func<Task<GlobalSystemMediaTransportControlsSessionManager>> requestManager;
    private readonly Func<GlobalSystemMediaTransportControlsSession,Task<GlobalSystemMediaTransportControlsSessionMediaProperties>> readProperties;
    private readonly Func<string,string,bool?> windowEvidence;
    private readonly StaleMediaSessions validity=new();
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private GlobalSystemMediaTransportControlsSessionMediaProperties? cachedProperties;
    private CancellationTokenSource requests=new(),coverRequest=new();
    private volatile bool metadataDirty=true,playbackDirty=true,sessionDirty=true;
    private int generation,metadataEpoch,reading,coverWorking;
    private bool enabled=true,disposed,trackedPaused;
    private string sourceKind="other",manualSource="",coverKey="";
    private long lastRead,propertiesAt,positionAt,coverAt;
    private TimeSpan position,duration;
    private MediaSnapshot snapshot=new("","",false,false);
    public BitmapImage? Cover{get;private set;}
    public string SourceId=>snapshot.Available ? snapshot.SourceId : "";
    public string Status{get;private set;}="媒体尚未连接";
    public TimeSpan Duration=>duration;
    internal int PollMilliseconds{get;private set;}=1000;
    internal event Action? Changed;
    public MediaService():this(null,null,null){ }
    internal MediaService(Func<Task<GlobalSystemMediaTransportControlsSessionManager>>? request,Func<GlobalSystemMediaTransportControlsSession,Task<GlobalSystemMediaTransportControlsSessionMediaProperties>>? properties,Func<string,string,bool?>? evidence)
    {
        requestManager=request ?? (()=>GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask());
        readProperties=properties ?? (target=>target.TryGetMediaPropertiesAsync().AsTask());windowEvidence=evidence ?? MediaWindowEvidence.Matches;
    }
    public TimeSpan Position
    {
        get
        {
            var extra=snapshot.Playing ? Math.Min(Math.Max(0,Environment.TickCount64-positionAt),long.MaxValue/TimeSpan.TicksPerMillisecond)*TimeSpan.TicksPerMillisecond : 0;
            var ticks=position.Ticks>long.MaxValue-extra ? long.MaxValue : position.Ticks+extra;
            return TimeSpan.FromTicks(Math.Clamp(ticks,0,duration.Ticks>0 ? duration.Ticks : long.MaxValue));
        }
    }
    public IReadOnlyDictionary<string,string> Sources
    {
        get
        {
            var result=new Dictionary<string,string>();if(manager==null)return result;
            try{foreach(var item in manager.GetSessions())try{var id=item.SourceAppUserModelId;if(!string.IsNullOrWhiteSpace(id))result.TryAdd(id,Path.GetFileNameWithoutExtension(id));}catch(Exception){ }}catch(Exception){ }
            return result;
        }
    }
    public void Configure(bool active,string kind,string selected)
    {
        if(disposed || enabled==active && sourceKind==kind && manualSource==selected)return;
        enabled=active;sourceKind=kind;manualSource=selected;Restart();Disconnect();Status=active ? "正在连接媒体" : "媒体已关闭";
    }
    internal void Invalidate(){if(disposed)return;Restart();}
    private void Restart()
    {
        generation++;requests.Cancel();requests.Dispose();requests=new();metadataDirty=playbackDirty=sessionDirty=true;validity.Clear();
    }
    private static void Safe(Action action){try{action();}catch(Exception){ }}
    private void Attach(GlobalSystemMediaTransportControlsSession? next)
    {
        if(ReferenceEquals(session,next))return;
        if(session is {} previous){Safe(()=>previous.PlaybackInfoChanged-=OnPlayback);Safe(()=>previous.MediaPropertiesChanged-=OnProperties);Safe(()=>previous.TimelinePropertiesChanged-=OnTimeline);}
        session=next;cachedProperties=null;metadataDirty=true;
        if(next!=null){next.PlaybackInfoChanged+=OnPlayback;next.MediaPropertiesChanged+=OnProperties;next.TimelinePropertiesChanged+=OnTimeline;}
    }
    private void OnPlayback(GlobalSystemMediaTransportControlsSession sender,PlaybackInfoChangedEventArgs args){playbackDirty=true;Changed?.Invoke();}
    private void OnProperties(GlobalSystemMediaTransportControlsSession sender,MediaPropertiesChangedEventArgs args){metadataDirty=true;Interlocked.Increment(ref metadataEpoch);Changed?.Invoke();}
    private void OnTimeline(GlobalSystemMediaTransportControlsSession sender,TimelinePropertiesChangedEventArgs args){playbackDirty=true;}
    private void OnCurrent(GlobalSystemMediaTransportControlsSessionManager sender,CurrentSessionChangedEventArgs args){sessionDirty=true;Changed?.Invoke();}
    private void OnSessions(GlobalSystemMediaTransportControlsSessionManager sender,SessionsChangedEventArgs args){sessionDirty=true;Changed?.Invoke();}
    private void ClearSnapshot(){if(!disposed)coverRequest.Cancel();snapshot=new("","",false,false);Cover=null;coverKey="";duration=position=TimeSpan.Zero;positionAt=Environment.TickCount64;}
    private void Disconnect()
    {
        Attach(null);if(manager is {} previous){Safe(()=>previous.CurrentSessionChanged-=OnCurrent);Safe(()=>previous.SessionsChanged-=OnSessions);}manager=null;
        coverRequest.Cancel();ClearSnapshot();trackedPaused=false;PollMilliseconds=1000;
    }
    public void Dispose(){if(disposed)return;disposed=true;generation++;requests.Cancel();Disconnect();requests.Dispose();coverRequest.Dispose();}
    private GlobalSystemMediaTransportControlsSession[] Candidates()
    {
        if(manager==null)return [];
        var current=manager.GetCurrentSession();var items=new List<(GlobalSystemMediaTransportControlsSession Session,bool Current,bool Playing)>();
        foreach(var item in manager.GetSessions())
        {
            try
            {
                if(!MediaSessionPolicy.Matches(item.SourceAppUserModelId,sourceKind,manualSource))continue;
                var status=item.GetPlaybackInfo().PlaybackStatus;if(status==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)continue;
                items.Add((item,ReferenceEquals(item,current) || item.Equals(current),status==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing));
            }
            catch(Exception){ }
        }
        return items.OrderByDescending(item=>item.Playing).ThenByDescending(item=>item.Current).Select(item=>item.Session).ToArray();
    }
    public async Task<MediaSnapshot> ReadAsync()
    {
        if(disposed || !enabled){ClearSnapshot();return snapshot;}
        if(Interlocked.Exchange(ref reading,1)!=0)return snapshot;
        var lease=generation;var token=requests.Token;
        try
        {
            var now=Environment.TickCount64;
            if(manager!=null && !trackedPaused && !sessionDirty && !playbackDirty && !metadataDirty && now-lastRead<5000 && !(snapshot.Available && Cover==null && now-coverAt>=1000))return snapshot;
            lastRead=now;sessionDirty=playbackDirty=false;
            if(manager==null)
            {
                var acquired=await requestManager().WaitAsync(TimeSpan.FromSeconds(5),token);if(disposed || lease!=generation)return snapshot;
                manager=acquired;manager.CurrentSessionChanged+=OnCurrent;manager.SessionsChanged+=OnSessions;
            }
            var candidates=Candidates();var ignored=false;string? failed=null;
            foreach(var candidate in candidates)
            {
                try
                {
                    Attach(candidate);var epoch=metadataEpoch;
                    var props=cachedProperties;var fetched=false;
                    if(metadataDirty || props==null || now-propertiesAt>10000 || Cover==null && now-propertiesAt>=1000)
                    {
                        metadataDirty=false;props=await readProperties(candidate).WaitAsync(TimeSpan.FromSeconds(5),token);fetched=true;
                    }
                    if(disposed || lease!=generation)return snapshot;
                    if(epoch!=metadataEpoch){metadataDirty=true;return snapshot;}
                    var playback=candidate.GetPlaybackInfo();var playing=playback.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;var title=string.IsNullOrWhiteSpace(props.Title) ? "未知曲目" : props.Title;
                    var id=candidate.SourceAppUserModelId;trackedPaused=StaleMediaSessions.Tracked(id) && !playing;
                    var stale=validity.IsStale(id,title,playing,trackedPaused ? windowEvidence(id,title) : null,now);
                    PollMilliseconds=trackedPaused && !stale ? 250 : 1000;
                    if(stale){ignored=true;continue;}
                    var artist=props.Artist ?? "";
                    var video=playback.PlaybackType==MediaPlaybackType.Video || props.PlaybackType==MediaPlaybackType.Video;
                    if(!video && artist.Length==0 && snapshot.SourceId==id && snapshot.Title==title)artist=snapshot.Artist;
                    video|=playback.PlaybackType!=MediaPlaybackType.Music && props.PlaybackType!=MediaPlaybackType.Music && artist.Length==0;
                    if(video)artist="";
                    var timeline=candidate.GetTimelineProperties();var length=timeline.EndTime>TimeSpan.Zero ? timeline.EndTime : timeline.MaxSeekTime;length=length>TimeSpan.Zero ? length : TimeSpan.Zero;
                    var at=timeline.Position<TimeSpan.Zero ? TimeSpan.Zero : timeline.Position;
                    var age=(DateTimeOffset.UtcNow-timeline.LastUpdatedTime).TotalMilliseconds;
                    if(playing && age is >=0 and <30000 && at.Ticks<long.MaxValue-(long)(age*TimeSpan.TicksPerMillisecond))at+=TimeSpan.FromMilliseconds(age);
                    if(length>TimeSpan.Zero && at>length)at=length;
                    if(disposed || lease!=generation || epoch!=metadataEpoch)return snapshot;
                    cachedProperties=props;if(fetched)propertiesAt=now;position=at;duration=length;positionAt=Environment.TickCount64;
                    var key=id+"|"+title+"|"+artist+"|"+props.AlbumTitle+"|"+length.Ticks;
                    if(key!=coverKey){coverRequest.Cancel();coverRequest.Dispose();coverRequest=CancellationTokenSource.CreateLinkedTokenSource(token);coverKey=key;Cover=null;coverAt=0;}
                    snapshot=new(title,artist,playing,true,props.AlbumTitle,length,id,video);Status="系统媒体已连接";
                    if((Cover==null && now-coverAt>=1000 || now-coverAt>=5000) && Interlocked.CompareExchange(ref coverWorking,1,0)==0){coverAt=now;_=LoadCoverAsync(props,key,lease,epoch,coverRequest.Token);}
                    return snapshot;
                }
                catch(OperationCanceledException) when(token.IsCancellationRequested){return snapshot;}
                catch(Exception error){failed=error.GetType().Name;Attach(null);metadataDirty=true;}
            }
            if(!ignored){Attach(null);trackedPaused=false;PollMilliseconds=1000;}
            ClearSnapshot();Status=failed!=null ? "媒体信息暂不可用（"+failed+"）" : ignored ? "已忽略残留媒体会话" : "未发现可用系统媒体会话";return snapshot;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){return snapshot;}
        catch(Exception error){if(!disposed && lease==generation){Disconnect();Status="媒体暂不可用（"+error.GetType().Name+"）";}return snapshot;}
        finally{Volatile.Write(ref reading,0);}
    }
    private async Task LoadCoverAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props,string key,int lease,int epoch,CancellationToken token)
    {
        try
        {
            if(props.Thumbnail==null)return;
            using var data=await props.Thumbnail.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3),token);if(data.Size>8*1024*1024)return;
            using var stream=data.AsStreamForRead();var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=96;image.StreamSource=stream;image.EndInit();image.Freeze();
            if(!disposed && !token.IsCancellationRequested && lease==generation && epoch==metadataEpoch && coverKey==key){Cover=image;Changed?.Invoke();}
        }
        catch(Exception){ }
        finally{Volatile.Write(ref coverWorking,0);}
    }
    public async Task<bool> ControlAsync(string action)
    {
        if(disposed || !enabled || !snapshot.Available || action is not("previous" or "next" or "toggle" or "play" or "pause"))return false;
        var target=session;var lease=generation;if(target==null)return false;
        try
        {
            if(action is "previous" or "next")
            {
                var changed=await (action=="previous" ? target.TrySkipPreviousAsync() : target.TrySkipNextAsync()).AsTask().WaitAsync(TimeSpan.FromSeconds(5),requests.Token);
                return changed && !disposed && enabled && lease==generation && ReferenceEquals(session,target);
            }
            var play=action=="play" || action!="pause" && target.GetPlaybackInfo().PlaybackStatus!=GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            for(var attempt=0;attempt<3;attempt++)
            {
                if(disposed || !enabled || lease!=generation || !ReferenceEquals(session,target))return false;
                await (play ? target.TryPlayAsync() : target.TryPauseAsync()).AsTask().WaitAsync(TimeSpan.FromSeconds(5),requests.Token);await Task.Delay(400,requests.Token);
                if(lease!=generation || disposed)return false;
                if((target.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)==play){playbackDirty=true;return true;}
            }
        }
        catch(Exception){ }
        return false;
    }
    public async Task<bool> SeekAsync(double ratio,string? expectedTitle=null)
    {
        if(disposed || !enabled || !snapshot.Available || !double.IsFinite(ratio) || duration<=TimeSpan.Zero || session==null)return false;
        var target=session;var lease=generation;
        try
        {
            var props=await readProperties(target).WaitAsync(TimeSpan.FromSeconds(5),requests.Token);
            if(lease!=generation || disposed || !ReferenceEquals(target,session) || expectedTitle!=null && props.Title!=expectedTitle || props.Title!=snapshot.Title || props.Artist!=snapshot.Artist && !snapshot.IsVideo)return false;
            var timeline=target.GetTimelineProperties();var total=timeline.EndTime>TimeSpan.Zero ? timeline.EndTime : timeline.MaxSeekTime;if(total<=TimeSpan.Zero)return false;
            var ticks=(long)(total.Ticks*(decimal)Math.Clamp(ratio,0,1));
            var changed=await target.TryChangePlaybackPositionAsync(ticks).AsTask().WaitAsync(TimeSpan.FromSeconds(5),requests.Token);playbackDirty=true;return !disposed && enabled && lease==generation && ReferenceEquals(session,target) && changed;
        }
        catch(Exception){return false;}
    }
}

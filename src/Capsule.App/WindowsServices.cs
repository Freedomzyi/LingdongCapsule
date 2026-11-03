using Capsule.Core;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using System.IO;
using System.Windows.Media.Imaging;
namespace Capsule.App;

public sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private string coverKey = "";
    private volatile bool metadataDirty = true;
    private volatile bool playbackDirty = true, sessionDirty = true;
    private MediaSnapshot lastSnapshot = new("", "", false, false);
    private DateTimeOffset lastRead;
    private GlobalSystemMediaTransportControlsSessionMediaProperties? cachedProperties;
    private DateTimeOffset propertiesReadAt;
    private TimeSpan positionAtRead, duration;
    private DateTimeOffset positionReadAt;
    private bool enabled=true;
    private string sourceKind="other",manualSource="";
    public void Configure(bool active,string kind,string selected)
    {
        if(enabled==active && sourceKind==kind && manualSource==selected)return;
        enabled=active;sourceKind=kind;manualSource=selected;sessionDirty=true;
    }
    public IReadOnlyDictionary<string,string> Sources=>manager?.GetSessions().Select(item=>item.SourceAppUserModelId).Distinct().ToDictionary(id=>id,id=>System.IO.Path.GetFileNameWithoutExtension(id)) ?? [];
    private GlobalSystemMediaTransportControlsSession? SelectSession()
    {
        if(manager is null)return null;
        var all=manager.GetSessions();
        if(manualSource.Length>0)return all.FirstOrDefault(item=>item.SourceAppUserModelId.Equals(manualSource,StringComparison.OrdinalIgnoreCase));
        bool Matches(GlobalSystemMediaTransportControlsSession item)
        {
            var id=item.SourceAppUserModelId.ToLowerInvariant();
            return sourceKind switch
            {
                "qqmusic"=>id.Contains("qqmusic"),"netease"=>id.Contains("cloudmusic") || id.Contains("netease"),
                "browser"=>new[]{"chrome","msedge","firefox","brave","opera"}.Any(id.Contains),
                "other"=>true,_=>id.Contains(sourceKind,StringComparison.OrdinalIgnoreCase)
            };
        }
        var candidates=all.Where(Matches).ToArray();
        var current=manager.GetCurrentSession();
        if(current is not null && Matches(current) && current.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)return current;
        return candidates.FirstOrDefault(item=>item.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) ?? (current is not null && Matches(current) ? current : candidates.FirstOrDefault());
    }
    public TimeSpan Duration => duration;
    public TimeSpan Position
    {
        get
        {
            var value = positionAtRead + (lastSnapshot.Playing ? DateTimeOffset.UtcNow - positionReadAt : TimeSpan.Zero);
            return TimeSpan.FromMilliseconds(Math.Clamp(value.TotalMilliseconds, 0, duration > TimeSpan.Zero ? duration.TotalMilliseconds : double.MaxValue));
        }
    }
    public BitmapImage? Cover { get; private set; }
    public string SourceId => session?.SourceAppUserModelId ?? "";
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) { playbackDirty = true; }
    private void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) { sessionDirty = true; }
    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) { sessionDirty = true; }
    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) { metadataDirty = true; }
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) { playbackDirty = true; }
    private void Attach(GlobalSystemMediaTransportControlsSession? next)
    {
        if (ReferenceEquals(session, next)) return;
        if (session is not null) { session.PlaybackInfoChanged -= OnPlaybackChanged; session.MediaPropertiesChanged -= OnPropertiesChanged; session.TimelinePropertiesChanged-=OnTimelineChanged; }
        session = next;
        metadataDirty = true; cachedProperties = null;
        if (session is not null) { session.PlaybackInfoChanged += OnPlaybackChanged; session.MediaPropertiesChanged += OnPropertiesChanged; session.TimelinePropertiesChanged+=OnTimelineChanged; }
    }
    public void Dispose() { Attach(null); if (manager is not null) { manager.CurrentSessionChanged -= OnSessionChanged; manager.SessionsChanged -= OnSessionsChanged; } manager = null; }
    public string Status { get; private set; } = "媒体尚未连接";
    public async Task<MediaSnapshot> ReadAsync()
    {
        try
        {
            if(!enabled){if(manager is not null)Dispose();Cover=null;duration=TimeSpan.Zero;Status="媒体已关闭";return lastSnapshot=new("","",false,false);}
            if (manager is not null && !sessionDirty && !playbackDirty && !metadataDirty && DateTimeOffset.UtcNow - lastRead < TimeSpan.FromSeconds(5)) return lastSnapshot;
            lastRead = DateTimeOffset.UtcNow; sessionDirty = false; playbackDirty = false;
            if (manager is null)
            {
                manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                manager.CurrentSessionChanged += OnSessionChanged; manager.SessionsChanged += OnSessionsChanged;
            }
            Attach(SelectSession());
            if (session is null) { metadataDirty = false;duration=TimeSpan.Zero;positionAtRead=TimeSpan.Zero; Status = "未发现系统媒体会话"; Cover = null; coverKey = ""; return lastSnapshot = new("", "", false, false); }
            var target = session;
            if (metadataDirty || cachedProperties is null || DateTimeOffset.UtcNow - propertiesReadAt > TimeSpan.FromSeconds(10))
            {
                metadataDirty = false;
                cachedProperties = await target.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                propertiesReadAt = DateTimeOffset.UtcNow;
            }
            var props = cachedProperties;
            var playing = target.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var timeline=target.GetTimelineProperties();
            duration=timeline.EndTime>TimeSpan.Zero ? timeline.EndTime : timeline.MaxSeekTime;
            positionAtRead=timeline.Position;
            positionReadAt=Math.Abs((DateTimeOffset.UtcNow-timeline.LastUpdatedTime).TotalSeconds)<30 ? timeline.LastUpdatedTime : DateTimeOffset.UtcNow;
            var key = target.SourceAppUserModelId + "|" + props.Title + "|" + props.Artist;
            if (key != coverKey)
            {
                coverKey = key; Cover = null;
                try
                {
                    if (props.Thumbnail is not null && !(target.SourceAppUserModelId.Contains("potplayer",StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(props.Artist)))
                    {
                        using var data = await props.Thumbnail.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                        if (data.Size <= 8 * 1024 * 1024)
                        {
                            using var stream = data.AsStreamForRead();
                            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 96; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Cover = bitmap;
                        }
                    }
                }
                catch { /* Missing or malformed cover art does not invalidate a media session. */ }
            }
            Status = "系统媒体已连接";
            return lastSnapshot = new(string.IsNullOrWhiteSpace(props.Title) ? "未知曲目" : props.Title, props.Artist, playing, true, props.AlbumTitle, duration, target.SourceAppUserModelId);
        }
        catch (Exception e) { Status = "媒体暂不可用（" + e.GetType().Name + "）"; Dispose(); Cover = null; coverKey = ""; return new("", "", false, false); }
    }
    public async Task<bool> ControlAsync(string action)
    {
        if(action is not ("previous" or "next" or "toggle" or "play" or "pause"))return false;
        try
        {
            var current = session;
            if (current is null) return false;
            if (action == "previous") return await current.TrySkipPreviousAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (action == "next") return await current.TrySkipNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var shouldPlay = action=="play" || action!="pause" && current.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            // Some players reject a command while switching tracks. Retry the same explicit
            // intent, never toggle again (which could undo an already completed operation).
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var operation = shouldPlay ? current.TryPlayAsync() : current.TryPauseAsync();
                await operation.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(400);
                var nowPlaying = current.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (nowPlaying == shouldPlay) return true;
            }
            return false;
        }
        catch { return false; }
    }
    public async Task<bool> SeekAsync(double ratio,string? expectedTitle=null)
    {
        try
        {
            var target=session;
            if(target is null || duration<=TimeSpan.Zero)return false;
            if(expectedTitle is not null && (await target.TryGetMediaPropertiesAsync()).Title!=expectedTitle)return false;
            var timeline=target.GetTimelineProperties();
            var total=timeline.EndTime>TimeSpan.Zero ? timeline.EndTime : timeline.MaxSeekTime;
            if(total<=TimeSpan.Zero)return false;
            var result=await target.TryChangePlaybackPositionAsync((long)(total.Ticks*Math.Clamp(ratio,0,1))).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            playbackDirty=true;return result;
        }
        catch{return false;}
    }
}

public sealed class NotificationService : IDisposable
{
    private readonly string? sourceCache;
    public NotificationService(bool persistSources=true,string? cacheFile=null)
    {
        sourceCache=persistSources ? cacheFile ?? Path.Combine(Settings.Folder,"notification-sources.json") : null;
        if(sourceCache==null)return;
        try
        {
            var file=new FileInfo(sourceCache);if(!file.Exists || file.Length>512*1024)return;
            var names=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(sourceCache));
            if(names!=null)foreach(var entry in names.Take(256))if(!string.IsNullOrWhiteSpace(entry.Key) && entry.Key.Length<=512 && entry.Value!=null && entry.Value.Length<=512)Sources[entry.Key]=entry.Value;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){ }
    }
    internal void PersistSourceNames()
    {
        if(sourceCache==null)return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sourceCache))!);
            File.WriteAllText(sourceCache+".tmp",System.Text.Json.JsonSerializer.Serialize(Sources.TakeLast(256).ToDictionary(entry=>entry.Key,entry=>entry.Value)));
            File.Move(sourceCache+".tmp",sourceCache,true);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){ }
    }
    private readonly Dictionary<string,BitmapSource?> logos=new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;
    public BitmapSource? Logo(string source)=>logos.GetValueOrDefault(source);
    private async Task LoadLogoAsync(Windows.ApplicationModel.AppInfo app,string source)
    {
        if(logos.Count>=48)logos.Remove(logos.Keys.First());logos[source]=null;
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var stream=await app.DisplayInfo.GetLogo(new Windows.Foundation.Size(96,96)).OpenReadAsync().AsTask(timeout.Token);
            if(disposed || stream.Size==0 || stream.Size>4*1024*1024)return;
            using var input=stream.AsStreamForRead();
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=96;bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();
            if(!disposed && logos.ContainsKey(source))logos[source]=bitmap;
        }
        catch(Exception){ /* Missing app logos use our own fallback icon. */ }
    }
    private readonly Dictionary<string, DateTimeOffset> seen = [];
    private UserNotificationListener? listener;
    private bool baseline;
    private bool subscribed, eventAvailable, hadAccess;
    private int dirty = 1;
    private DateTimeOffset lastPoll;
    private void OnChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args) => Interlocked.Exchange(ref dirty, 1);
    public void Dispose() { disposed=true;if (subscribed && eventAvailable && listener is not null) listener.NotificationChanged -= OnChanged;logos.Clear(); }
    public string Status { get; private set; } = "通知监听未启用";
    public Dictionary<string, string> Sources { get; } = [];
    public async Task<bool> RequestAsync()
    {
        try
        {
            listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            Status = access == UserNotificationListenerAccessStatus.Allowed ? "通知已授权" : "通知未获授权；可在 Windows 设置中允许";
            baseline = false;
            return access == UserNotificationListenerAccessStatus.Allowed;
        }
        catch (Exception e) { Status = "通知接入不可用（" + e.GetType().Name + "）；请使用带应用身份的版本"; return false; }
    }
    public async Task<List<Notice>> ReadAsync(HashSet<string> muted)
    {
        List<Notice> notices = [];
        if(disposed)return notices;
        try
        {
            listener ??= UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) { hadAccess = false; Status = "通知未获授权"; return notices; }
            if (!hadAccess) { ResetBaseline(); hadAccess = true; }
            if (!subscribed)
            {
                subscribed = true;
                try { listener.NotificationChanged += OnChanged; eventAvailable = true; } catch { eventAvailable = false; }
            }
            var changed = Interlocked.Exchange(ref dirty, 0) != 0;
            if (baseline && !changed && DateTimeOffset.UtcNow - lastPoll < TimeSpan.FromSeconds(2)) return notices;
            lastPoll = DateTimeOffset.UtcNow;
            var items = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var live = new HashSet<string>();var sourceNamesChanged=false;
            foreach (var n in items.OrderBy(n => n.CreationTime))
            {
                try
                {
                    var source = n.AppInfo.AppUserModelId;
                    var key = source + ":" + n.Id;
                    live.Add(key); if (!Sources.ContainsKey(source)){Sources[source] = n.AppInfo.DisplayInfo.DisplayName;sourceNamesChanged=true;}
                    var fresh = !seen.TryGetValue(key, out var previous) || previous != n.CreationTime;
                    seen[key] = n.CreationTime;
                    if (!baseline || !fresh || muted.Contains(source)) continue;
                    var text = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                    if (text is null) continue;
                    notices.Add(new(source, Sources[source], text.FirstOrDefault()?.Text ?? "新通知", string.Join("\n", text.Skip(1).Select(t => t.Text)),n.Id,n.CreationTime));
                    if(!logos.ContainsKey(source))_=LoadLogoAsync(n.AppInfo,source);
                }
                catch { /* An invalid app notification must not break the batch. */ }
            }
            foreach (var key in seen.Keys.Where(k => !live.Contains(k)).ToArray()) seen.Remove(key);
            if(sourceNamesChanged)PersistSourceNames();
            baseline = true; Status = "通知监听正常";
        }
        catch (Exception e) { Status = "通知读取失败（" + e.GetType().Name + "）"; }
        return notices;
    }
    public void ResetBaseline() { baseline = false; seen.Clear(); Interlocked.Exchange(ref dirty, 1); }
}

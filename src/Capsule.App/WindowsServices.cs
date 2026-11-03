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
    private volatile bool metadataDirty=true;
    private GlobalSystemMediaTransportControlsSessionMediaProperties? cachedProperties;
    private DateTimeOffset propertiesReadAt;
    public BitmapImage? Cover { get; private set; }
    public string SourceId => session?.SourceAppUserModelId ?? "";
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) { }
    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) { metadataDirty=true; }
    private void Attach(GlobalSystemMediaTransportControlsSession? next)
    {
        if (ReferenceEquals(session,next)) return;
        if (session is not null) { session.PlaybackInfoChanged-=OnPlaybackChanged; session.MediaPropertiesChanged-=OnPropertiesChanged; }
        session=next;
        metadataDirty=true;cachedProperties=null;
        if (session is not null) { session.PlaybackInfoChanged+=OnPlaybackChanged; session.MediaPropertiesChanged+=OnPropertiesChanged; }
    }
    public void Dispose() { Attach(null); manager=null; }
    public string Status { get; private set; } = "媒体尚未连接";
    public async Task<MediaSnapshot> ReadAsync()
    {
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Attach(manager.GetCurrentSession());
            if (session is null) { Status = "未发现系统媒体会话"; Cover=null; coverKey=""; return new("", "", false, false); }
            var target = session;
            if(metadataDirty || cachedProperties is null || DateTimeOffset.UtcNow-propertiesReadAt>TimeSpan.FromSeconds(10))
            {
                metadataDirty=false;
                cachedProperties=await target.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                propertiesReadAt=DateTimeOffset.UtcNow;
            }
            var props=cachedProperties;
            var playing = target.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var key = target.SourceAppUserModelId + "|" + props.Title + "|" + props.Artist;
            if (key != coverKey)
            {
                coverKey=key; Cover=null;
                try
                {
                    if (props.Thumbnail is not null)
                    {
                        using var data = await props.Thumbnail.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                        if (data.Size <= 8 * 1024 * 1024)
                        {
                            using var stream = data.AsStreamForRead();
                            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth=96; bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); Cover=bitmap;
                        }
                    }
                }
                catch { /* Missing or malformed cover art does not invalidate a media session. */ }
            }
            Status = "系统媒体已连接";
            return new(string.IsNullOrWhiteSpace(props.Title) ? "未知曲目" : props.Title, props.Artist, playing, true);
        }
        catch (Exception e) { Status = "媒体暂不可用（" + e.GetType().Name + "）"; Dispose(); Cover=null; coverKey=""; return new("", "", false, false); }
    }
    public async Task<bool> ControlAsync(string action)
    {
        try
        {
            var current = manager?.GetCurrentSession();
            if (current is null) return false;
            if (action == "previous") return await current.TrySkipPreviousAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (action == "next") return await current.TrySkipNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var shouldPlay = current.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            // Some players reject a command while switching tracks. Retry the same explicit
            // intent, never toggle again (which could undo an already completed operation).
            for (var attempt=0;attempt<3;attempt++)
            {
                var operation=shouldPlay ? current.TryPlayAsync() : current.TryPauseAsync();
                await operation.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(400);
                var nowPlaying=current.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (nowPlaying==shouldPlay) return true;
            }
            return false;
        }
        catch { return false; }
    }
}

public sealed class NotificationService : IDisposable
{
    private readonly Dictionary<string, DateTimeOffset> seen = [];
    private UserNotificationListener? listener;
    private bool baseline;
    private bool subscribed,eventAvailable,hadAccess;
    private int dirty=1;
    private DateTimeOffset lastPoll;
    private void OnChanged(UserNotificationListener sender,UserNotificationChangedEventArgs args)=>Interlocked.Exchange(ref dirty,1);
    public void Dispose(){if(subscribed && eventAvailable && listener is not null)listener.NotificationChanged-=OnChanged;}
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
        try
        {
            listener ??= UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) { hadAccess=false;Status = "通知未获授权"; return notices; }
            if(!hadAccess){ResetBaseline();hadAccess=true;}
            if(!subscribed)
            {
                subscribed=true;
                try{listener.NotificationChanged+=OnChanged;eventAvailable=true;}catch{eventAvailable=false;}
            }
            var changed=Interlocked.Exchange(ref dirty,0)!=0;
            if(baseline && !changed && DateTimeOffset.UtcNow-lastPoll<TimeSpan.FromSeconds(eventAvailable ? 15 : 2))return notices;
            lastPoll=DateTimeOffset.UtcNow;
            var items = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var live = new HashSet<string>();
            foreach (var n in items.OrderBy(n => n.CreationTime))
            {
                try
                {
                    var source = n.AppInfo.AppUserModelId;
                    var key = source + ":" + n.Id;
                    live.Add(key); Sources[source] = n.AppInfo.DisplayInfo.DisplayName;
                    var fresh = !seen.TryGetValue(key, out var previous) || previous != n.CreationTime;
                    seen[key] = n.CreationTime;
                    if (!baseline || !fresh || muted.Contains(source)) continue;
                    var text = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                    if (text is null) continue;
                    notices.Add(new(source, Sources[source], text.FirstOrDefault()?.Text ?? "新通知", string.Join("\n", text.Skip(1).Select(t => t.Text))));
                }
                catch { /* An invalid app notification must not break the batch. */ }
            }
            foreach (var key in seen.Keys.Where(k => !live.Contains(k)).ToArray()) seen.Remove(key);
            baseline = true; Status = "通知监听正常";
        }
        catch (Exception e) { Status = "通知读取失败（" + e.GetType().Name + "）"; }
        return notices;
    }
    public void ResetBaseline() { baseline = false; seen.Clear();Interlocked.Exchange(ref dirty,1); }
}

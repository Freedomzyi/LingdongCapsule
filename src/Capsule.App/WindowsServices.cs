using Capsule.Core;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
namespace Capsule.App;

public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    public string Status { get; private set; } = "媒体尚未连接";
    public async Task<MediaSnapshot> ReadAsync()
    {
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            session = manager.GetCurrentSession();
            if (session is null) { Status = "未发现系统媒体会话"; return new("", "", false, false); }
            var target = session;
            var props = await target.TryGetMediaPropertiesAsync();
            var playing = target.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Status = "系统媒体已连接";
            return new(string.IsNullOrWhiteSpace(props.Title) ? "未知曲目" : props.Title, props.Artist, playing, true);
        }
        catch (Exception e) { Status = "媒体暂不可用（" + e.GetType().Name + "）"; manager = null; session = null; return new("", "", false, false); }
    }
    public async Task<bool> ControlAsync(string action)
    {
        try
        {
            var current = manager?.GetCurrentSession();
            if (current is null) return false;
            return action switch { "previous" => await current.TrySkipPreviousAsync(), "next" => await current.TrySkipNextAsync(), _ => await current.TryTogglePlayPauseAsync() };
        }
        catch { return false; }
    }
}

public sealed class NotificationService
{
    private readonly Dictionary<string, DateTimeOffset> seen = [];
    private UserNotificationListener? listener;
    private bool baseline;
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
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) { Status = "通知未获授权"; return notices; }
            var items = await listener.GetNotificationsAsync(NotificationKinds.Toast);
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
    public void ResetBaseline() { baseline = false; seen.Clear(); }
}

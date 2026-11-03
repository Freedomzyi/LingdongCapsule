using Capsule.Core;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using System.IO;
using System.Windows.Media.Imaging;
namespace Capsule.App;

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
    private sealed class LogoEntry{public BitmapSource? Image;public bool Loading=true;public long At=Environment.TickCount64;}
    private readonly Dictionary<string,LogoEntry> logos=new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource logoLifetime=new();
    private bool disposed;
    public BitmapSource? Logo(string source)=>logos.GetValueOrDefault(source)?.Image;
    private async Task LoadLogoAsync(Windows.ApplicationModel.AppInfo app,string source)
    {
        if(disposed)return;
        if(logos.TryGetValue(source,out var existing) && (existing.Loading || existing.Image!=null || Environment.TickCount64-existing.At<300000))return;
        if(logos.Count>=48)logos.Remove(logos.Keys.First());var entry=new LogoEntry();logos[source]=entry;
        BitmapSource? result=null;
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(logoLifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var stream=await app.DisplayInfo.GetLogo(new Windows.Foundation.Size(96,96)).OpenReadAsync().AsTask(timeout.Token);
            if(disposed || stream.Size==0 || stream.Size>4*1024*1024)throw new InvalidDataException();
            using var input=stream.AsStreamForRead();
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=96;bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();
            result=bitmap;
        }
        catch(Exception){ /* Missing WinRT logos continue through the Shell. */ }
        if(result==null && !disposed)
        {
            try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(logoLifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(3));result=await ApplicationIcons.ReadAsync(source,timeout.Token).WaitAsync(timeout.Token);}catch(Exception){ }
        }
        if(!disposed && logos.TryGetValue(source,out var current) && ReferenceEquals(current,entry)){entry.Image=result;entry.Loading=false;entry.At=Environment.TickCount64;}
    }
    private readonly Dictionary<string, DateTimeOffset> seen = [];
    private UserNotificationListener? listener;
    private bool baseline;
    private bool subscribed, eventAvailable, hadAccess;
    internal bool HasAccess=>hadAccess && !disposed;
    private int dirty = 1;
    private DateTimeOffset lastPoll;
    private void OnChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args) => Interlocked.Exchange(ref dirty, 1);
    public void Dispose() { if(disposed)return;disposed=true;logoLifetime.Cancel();if (subscribed && eventAvailable && listener is not null) listener.NotificationChanged -= OnChanged;logos.Clear();logoLifetime.Dispose(); }
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
                    _=LoadLogoAsync(n.AppInfo,source);
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

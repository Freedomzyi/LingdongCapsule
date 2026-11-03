using Capsule.App;
using System.IO;
using Windows.UI.Notifications.Management;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using Capsule.Core;
using Microsoft.Win32;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
        var task = RunAsync(args);
        task.ContinueWith(_ => dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background));
        System.Windows.Threading.Dispatcher.Run();
        task.GetAwaiter().GetResult();
    }
    private static async Task RunAsync(string[] args)
    {
        // Default probe is read-only. Interactive scenarios must be explicitly selected.
        var media = new MediaService();
        var snapshot = await media.ReadAsync();
        Console.WriteLine($"Media: {media.Status}; available={snapshot.Available}; playing={snapshot.Playing}");
        var current = (await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync()).GetCurrentSession();
        if (current is not null)
        {
            var p = current.GetPlaybackInfo(); var c = p.Controls;
            Console.WriteLine($"Playback={p.PlaybackStatus}; play={c.IsPlayEnabled}; pause={c.IsPauseEnabled}; toggle={c.IsPlayPauseToggleEnabled}; next={c.IsNextEnabled}");
            if (args.Contains("--toggle-once"))
            {
                var result = await current.TryTogglePlayPauseAsync(); await Task.Delay(1500);
                Console.WriteLine($"Direct toggle result={result}; observed={current.GetPlaybackInfo().PlaybackStatus}");
            }
        }
        try
        {
            var listener = UserNotificationListener.Current;
            Console.WriteLine($"Notification access: {listener.GetAccessStatus()}");
            if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
            {
                var items = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
                Console.WriteLine($"Notification read succeeded; count={items.Count}");
            }
        }
        catch (Exception ex) { Console.WriteLine($"Notification unavailable: {ex.GetType().Name}; HRESULT=0x{ex.HResult:X8}"); }

        if (args.Contains("--startup-cycle"))
        {
            const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var key = Registry.CurrentUser.CreateSubKey(run);
            var previous = key.GetValue("LingdongCapsule");
            try
            {
                StartupService.SetEnabled(true);
                var command = key.GetValue("LingdongCapsule") as string;
                if (!StartupService.IsEnabled || command is null || !command.Contains(Environment.ProcessPath!) || !command.EndsWith(" --startup")) throw new Exception("Startup command incorrect");
                StartupService.SetEnabled(false);
                if (StartupService.IsEnabled) throw new Exception("Startup entry was not removed");
                Console.WriteLine("PASS Own per-user startup entry enabled and disabled; previous value restored in finally");
            }
            finally { if (previous is null) key.DeleteValue("LingdongCapsule", false); else key.SetValue("LingdongCapsule", previous); }
        }

        if (args.Contains("--toast") || args.Contains("--window-scenario"))
        {
            Capsule.App.MainWindow? window = null;
            if (args.Contains("--window-scenario"))
            {
                var app = new Capsule.App.App(); app.InitializeComponent();
                window = new Capsule.App.MainWindow(suppressOnboarding: true); window.Show();
                await Task.Delay(1600);
            }
            const string id = "LingdongCapsule.IntegrationTests";
            const string registryPath = @"Software\Classes\AppUserModelId\LingdongCapsule.IntegrationTests";
            // Own dedicated test identity only; no impersonation of installed applications.
            using var existing = Registry.CurrentUser.OpenSubKey(registryPath);
            var created = existing is null;
            var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LingdongCapsule Integration Tests.lnk");
            if (File.Exists(shortcut)) throw new Exception("Test shortcut already exists; refusing to overwrite it");
            if (created)
            {
                using var key = Registry.CurrentUser.CreateSubKey(registryPath);
                key.SetValue("DisplayName", "灵动胶囊 · 系统链路测试");
                key.SetValue("CustomActivator", "{B66EE1D4-59B7-4D94-B6D6-C4D33DC2A147}");
            }
            try
            {
                TestShortcut.Create(shortcut, id);
                var service = new NotificationService();
                var initial = await service.ReadAsync([]);
                if (initial.Count != 0) throw new Exception("Startup must not replay historical notifications");
                var xml = new XmlDocument();
                xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>Capsule integration test</text><text>Own local test notification. No personal data.</text></binding></visual></toast>");
                var notifier = ToastNotificationManager.CreateToastNotifier(id);
                var toast = new ToastNotification(xml) { Tag = "capsule-test", Group = "integration" };
                notifier.Show(toast);
                var found = false;
                for (var i = 0; i < 10; i++)
                {
                    await Task.Delay(1000);
                    var received = await service.ReadAsync([]);
                    if (received.Any(n => n.SourceId == id && n.Title == "Capsule integration test")) { found = true; break; }
                }
                if (!found) throw new Exception("New OS notification was not received by the application adapter");
                var repeated = await service.ReadAsync([]);
                if (repeated.Any(n => n.SourceId == id)) throw new Exception("Repeated poll replayed the same notification");
                Console.WriteLine("PASS Real Windows toast received once through NotificationService");
                if (window is not null)
                {
                    async Task Until(Func<bool> condition, string failure)
                    {
                        for (var i = 0; i < 30; i++) { if (condition()) return; await Task.Delay(200); }
                        throw new Exception(failure);
                    }
                    void ClickIsland()
                    {
                        var shell = (System.Windows.Controls.Border)window.FindName("Shell");
                        shell.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent });
                    }
                    await Until(() => window.LiveState.Notification?.SourceId == id, "Real toast did not reach the WPF window");
                    ClickIsland();
                    if (!window.LiveState.Expanded || ((System.Windows.Controls.TextBlock)window.FindName("NoticeBody")).Text.Length == 0) throw new Exception("WPF notification expansion failed");
                    Console.WriteLine("PASS Actual WPF window received and expanded real OS notification");
                    var before = await media.ReadAsync();
                    if (!media.SourceId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase)) throw new Exception("QQ Music required for window scenario");
                    if (!before.Playing) { if (!await media.ControlAsync("toggle")) throw new Exception("Play failed"); }
                    await Until(() => window.LiveState.Media.Playing, "WPF did not observe play");
                    if (!await media.ControlAsync("toggle")) throw new Exception("Pause failed");
                    await Until(() => !window.LiveState.Media.Playing, "WPF did not observe pause"); ClickIsland();
                    if (window.LiveState.Mode != IslandMode.Clock || ((System.Windows.Controls.TextBlock)window.FindName("Clock")).Visibility != System.Windows.Visibility.Visible) throw new Exception("WPF did not return to visible clock");
                    Console.WriteLine("PASS Actual WPF pause during real notification returns to visible clock");
                    if (!await media.ControlAsync("toggle")) throw new Exception("Resume original track failed");
                    toast = new ToastNotification(xml) { Tag = "capsule-next", Group = "integration" }; notifier.Show(toast);
                    await Until(() => window.LiveState.Notification?.SourceId == id, "Second real toast missing"); ClickIsland();
                    if (!await media.ControlAsync("next")) throw new Exception("Next failed");
                    await Until(() => window.LiveState.Media.Title != before.Title, "WPF media metadata did not update during notification");
                    var expected = window.LiveState.Media.Title;
                    ClickIsland();
                    if (window.LiveState.Notification is not null || window.LiveState.Media.Title != expected) throw new Exception("WPF next-track recovery failed");
                    Console.WriteLine("PASS Actual WPF notification overlay retained new QQ Music track and recovered correctly");
                }
                if (args.Contains("--hold-toast"))
                {
                    for (var i = 0; i < 4; i++) { toast = new ToastNotification(xml) { Tag = "capsule-test-" + i, Group = "integration" }; notifier.Show(toast); await Task.Delay(3000); }
                }
                notifier.Hide(toast);
                ToastNotificationManager.History.Clear(id);
            }
            finally { try { ToastNotificationManager.History.Clear(id); } catch (Exception ex) { Console.WriteLine("Test notification cleanup: " + ex.GetType().Name); } if (File.Exists(shortcut)) File.Delete(shortcut); if (created) Registry.CurrentUser.DeleteSubKey(registryPath, false); window?.Close(); }
        }

        if (args.Contains("--qq-scenario"))
        {
            var state = new IslandState();
            var original = await media.ReadAsync();
            if (!original.Available) throw new Exception("A real media session is required");
            if (!media.SourceId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase)) throw new Exception("The active media session is not QQ Music");
            try
            {
                if (!original.Playing) { if (!await media.ControlAsync("toggle")) throw new Exception("Play command rejected"); }
                await Task.Delay(1500);
                var playing = await media.ReadAsync();
                if (!playing.Playing) throw new Exception("Player did not enter Playing state");
                Console.WriteLine("PASS Real media play command and observed Playing state");
                state.UpdateMedia(playing);
                state.Show(new("test", "Integration", "Notification overlay", ""), DateTimeOffset.UtcNow);
                if (!await media.ControlAsync("next")) throw new Exception("Next command rejected");
                MediaSnapshot changed = playing;
                for (var i = 0; i < 8; i++) { await Task.Delay(700); changed = await media.ReadAsync(); if (changed.Title != playing.Title) break; }
                if (changed.Title == playing.Title) throw new Exception("Next command did not produce new track metadata");
                state.UpdateMedia(changed);
                if (state.Mode != IslandMode.Notification) throw new Exception("Media replaced active notification");
                state.Dismiss();
                if (state.Mode != (changed.Playing ? IslandMode.Media : IslandMode.Clock) || state.Media.Title != changed.Title) throw new Exception("Track recovery failed");
                Console.WriteLine("PASS Real QQ Music next-track command, current metadata and correct media/clock recovery");
                if (!changed.Playing)
                {
                    if (!await media.ControlAsync("toggle")) throw new Exception("New track could not enter Playing state for pause scenario");
                    state.UpdateMedia(await media.ReadAsync());
                }
                state.Show(new("test", "Integration", "Pause overlay", ""), DateTimeOffset.UtcNow);
                if (!await media.ControlAsync("toggle")) throw new Exception("Pause command rejected");
                await Task.Delay(1200);
                var paused = await media.ReadAsync();
                if (paused.Playing) throw new Exception("Player did not pause");
                state.UpdateMedia(paused); state.Dismiss();
                if (state.Mode != IslandMode.Clock || !state.Media.Available) throw new Exception("Pause recovery failed");
                Console.WriteLine("PASS Real pause during overlay returns clock with media session available");
            }
            finally
            {
                var last = await media.ReadAsync();
                if (original.Playing != last.Playing && last.Available) await media.ControlAsync("toggle");
            }
        }
    }
}

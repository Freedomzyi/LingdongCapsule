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
        if(args.Contains("--native-toast-test")){await NativeToastTests.RunAsync();return;}
        if(args.Contains("--font-test")){await FontTests.RunAsync(args);return;}
        if(args.Contains("--input-routing-test")){await InputRoutingTests.RunAsync();return;}
        if(args.Contains("--diagnostics-test")){await DiagnosticsTests.RunAsync();return;}
        if(args.Contains("--settings-audit") || args.Contains("--settings-sync-test")){await SettingsSyncTests.RunAsync(args.Contains("--settings-audit"));return;}
        if(args.Contains("--scale-test")){await ScaleTests.RunAsync();return;}
        if(args.Contains("--welcome-test")){await WelcomeTests.RunAsync();return;}
        if(args.Contains("--timeline-test")){await TimelineTests.RunAsync();return;}
        if(args.Contains("--instance-test") || args.Contains("--instance-peer")){await InstanceTests.RunAsync(args);return;}
        if(args.Contains("--placement-test")){await PlacementTests.RunAsync(args);return;}
        if(args.Contains("--cover-live")){await CoverTests.RunLiveAsync();return;}
        if(args.Contains("--cover-test")){await CoverTests.RunAsync();return;}
        if(args.Contains("--builtin-test")){await BuiltinTests.RunAsync();return;}
        if(args.Contains("--resource-test")){await ResourceProbe.RunAsync(args);return;}
        if(args.Contains("--media-test")){await MediaTests.RunAsync();return;}
        if(args.Contains("--lyrics-test")){await LyricsTests.RunAsync();return;}
        if(args.Contains("--lyrics-live")){await LyricsTests.RunLiveAsync();return;}
        if(args.Contains("--market-live")){await MarketTests.RunLiveAsync(args.Contains("--download"));return;}
        if(args.Contains("--market-test")){await MarketTests.RunAsync();return;}
        if(args.Contains("--material-test")){await MaterialTests.RunAsync();return;}
        if(args.Contains("--release-test")){await ReleaseTests.RunAsync();return;}
        if(args.Contains("--solo-test")){await SoloTests.RunAsync();return;}
        if(args.Contains("--msp-test")){await MspTests.RunAsync();return;}
        if(args.Contains("--plugin-test")){await PluginTests.RunAsync();return;}
        if(args.Contains("--overview-test")){await OverviewTests.RunAsync(args);return;}
        if(args.Contains("--notification-test")){await NotificationTests.RunAsync(args);return;}
        if(args.Contains("--shortcut-test")){await ShortcutTests.RunAsync();return;}
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

        if(args.Contains("--audio-probe"))
        {
            using var audio=new AudioService();
            var volume=audio.Volume;
            if(!audio.HasDevice || !audio.SetVolume(volume))throw new Exception("Default output volume interface unavailable");
            audio.Tick(true);await Task.Delay(2200);
            var bands=audio.Bands;
            Console.WriteLine($"Audio live: connected={audio.Status}; fiveBands={bands.Length==5}; signal={bands.Any(value=>value>0)}; normalized={bands.All(value=>double.IsFinite(value)&&value>=0&&value<=1)}");
            if(bands.Length!=5 || bands.Any(value=>!double.IsFinite(value)||value<0||value>1))throw new Exception("Spectrum output invalid");
        }
        if(args.Contains("--render-media"))
        {
            var renderAt=Array.IndexOf(args,"--render-media");
            var directory=renderAt+1<args.Length ? Path.GetFullPath(args[renderAt+1]) : throw new Exception("A render output directory is required");
            Directory.CreateDirectory(directory);
            var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();Design.ApplySystemTheme();
            var window=new Capsule.App.MainWindow(suppressOnboarding:true);
            var initial=await media.ReadAsync();var initialPosition=media.Position;var changedPlayback=false;var changedPosition=false;
            try
            {
                if(!initial.Playing){if(!await media.ControlAsync("toggle"))throw new Exception("Media could not play for lyric rendering");changedPlayback=true;}
                window.Show();
                for(var attempt=0;attempt<100 && window.CurrentLyrics.Lines.Count==0;attempt++)await Task.Delay(400);
                if(window.CurrentLyrics.Lines.Count==0)throw new Exception("Actual window did not load lyrics");
                var lyric=(LyricView)window.FindName("LiveLyrics");
                if(lyric.FrameProvider?.Invoke().Line is not {Text.Length:>0})
                {
                    var line=window.CurrentLyrics.Lines.First(row=>row.Text.Length>0);
                    changedPosition=await media.SeekAsync(Math.Clamp((line.StartMs+600)/Math.Max(1,initial.Duration.TotalMilliseconds),0,1),initial.Title);
                    for(var attempt=0;attempt<30 && lyric.FrameProvider?.Invoke().Line is not {Text.Length:>0};attempt++)await Task.Delay(200);
                }
                if(lyric.Visibility!=System.Windows.Visibility.Visible)throw new Exception("Live lyric view was not visible");
                await Task.Delay(600);window.UpdateLayout();
                void Capture(string name)
                {
                    var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output=File.Create(Path.Combine(directory,name));encoder.Save(output);
                }
                Capture("lyrics-compact.png");
                var shell=(System.Windows.Controls.Border)window.FindName("Shell");
                shell.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.UIElement.MouseLeftButtonUpEvent});
                await Task.Delay(600);window.UpdateLayout();Capture("media-expanded.png");
                if(((System.Windows.Controls.Slider)window.FindName("SeekBar")).Maximum!=1 || window.ActualHeight<150)throw new Exception("Expanded timeline layout invalid");
                Console.WriteLine($"PASS Actual WPF lyric and expanded media rendered; lines={window.CurrentLyrics.Lines.Count}; wordTimed={window.CurrentLyrics.HasWordTiming}; height={window.ActualHeight:0}");
            }
            finally
            {
                var latest=await media.ReadAsync();
                if(latest.Title==initial.Title && latest.SourceId==initial.SourceId)
                {
                    if(changedPosition && initial.Duration>TimeSpan.Zero)await media.SeekAsync(initialPosition.TotalSeconds/initial.Duration.TotalSeconds,initial.Title);
                    if(changedPlayback && latest.Playing)await media.ControlAsync("toggle");
                }
                window.Close();
            }
        }
        if(args.Contains("--lyrics-live"))
        {
            using var client=new LyricsClient();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(50));
            var document=await client.FetchAsync(snapshot,timeout.Token);
            Console.WriteLine($"Lyrics live: source={document.Source}; lines={document.Lines.Count}; wordTimed={document.HasWordTiming}; status={client.Status}");
            if(document.Lines.Count==0)throw new Exception("Current media has no matched lyrics; parity not verified");
        }
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
                var app = new Capsule.App.App{SuppressStartupForTests=true}; app.InitializeComponent();
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
                var service = new NotificationService(persistSources:false);
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
                    if (window.LiveState.Mode != IslandMode.Clock || ((System.Windows.FrameworkElement)window.FindName("Clock")).Visibility != System.Windows.Visibility.Visible) throw new Exception("WPF did not return to visible clock");
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

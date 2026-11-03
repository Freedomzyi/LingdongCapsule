using Capsule.App;
using Capsule.Core;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class NotificationTests
{
    public static async Task RunAsync(string[] args)
    {
        var index=Array.IndexOf(args,"--notification-test");var output=index+1<args.Length ? Path.GetFullPath(args[index+1]) : throw new Exception("Provide an output directory");Directory.CreateDirectory(output);
        var chimes=Enumerable.Range(1,14).Select(NotificationSound.Chime).ToArray();
        if(chimes.Select(bytes=>Convert.ToHexString(SHA256.HashData(bytes))).Distinct().Count()!=14 || chimes.Any(bytes=>System.Text.Encoding.ASCII.GetString(bytes,0,4)!="RIFF" || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40,4))!=bytes.Length-44))throw new Exception("Original chime catalog invalid");
        var soundFile=Path.Combine(output,"sound-test.wav");File.WriteAllBytes(soundFile,chimes[0]);
        if(!NotificationSound.ValidFile(soundFile,out _) || !await NotificationSound.ValidateDurationAsync(soundFile))throw new Exception("Valid short audio rejected");
        var longFile=Path.Combine(output,"long-test.wav");var longWave=new byte[44+44100*2*6];chimes[0].AsSpan(0,44).CopyTo(longWave);BinaryPrimitives.WriteInt32LittleEndian(longWave.AsSpan(4,4),longWave.Length-8);BinaryPrimitives.WriteInt32LittleEndian(longWave.AsSpan(40,4),longWave.Length-44);File.WriteAllBytes(longFile,longWave);
        if(await NotificationSound.ValidateDurationAsync(longFile))throw new Exception("Overlong custom audio accepted");
        using(var sound=new NotificationSound())
        {
            var config=new Settings{NotificationSoundKey="custom",NotificationSoundPath=soundFile,NotificationSoundVolume=0};
            if(!await sound.PlayAsync(config,preview:true) || sound.PlayCount!=1)throw new Exception("Muted playback pipeline failed");
            config.NotificationSoundEnabled=true;config.QuietUntil=DateTimeOffset.UtcNow.AddMinutes(30);
            if(await sound.PlayAsync(config) || sound.PlayCount!=1)throw new Exception("Quiet preference allowed sound playback");
        }
        Console.WriteLine("PASS 14 original WAV chimes, duration rejection and real media playback at zero volume");
        var app=new Capsule.App.App();app.InitializeComponent();Design.ApplySystemTheme();
        var settings=new Settings{MediaEnabled=false,PrivatePreview=false,NotificationSoundEnabled=true,NotificationSoundKey="custom",NotificationSoundPath=soundFile,NotificationSoundVolume=0};
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:settings);
        try
        {
            window.Show();await Task.Delay(1400);
            var shell=(Border)window.FindName("Shell");uint id=100;
            void Click()=>shell.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
            void Capture(string name)
            {
                window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(window);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name));png.Save(file);
            }
            const string source="LingdongCapsule.LayoutTest";
            for(var mode=0;mode<3;mode++)
            {
                settings.NotificationMode=mode;
                if(!window.AcceptNotification(new(source,"灵动胶囊","通知展示验证","这是一条用于检查裁切与排版的独立测试通知。",++id),DateTimeOffset.Now))throw new Exception("Own layout notification rejected");
                await Task.Delay(650);Capture($"notification-mode-{mode}.png");
                var title=(TextBlock)window.FindName("NoticeTitle");var body=(TextBlock)window.FindName("NoticeBody");var appName=(TextBlock)window.FindName("NoticeApp");var right=(FrameworkElement)window.FindName("NoticeRight");
                if(title.Visibility!=Visibility.Visible || body.Text.Length==0 || appName.Visibility!=(mode==2 ? Visibility.Visible : Visibility.Collapsed) || right.Visibility!=(mode==0 ? Visibility.Collapsed : Visibility.Visible) || window.ActualWidth>settings.Width+.5)throw new Exception("Notification mode layout invalid");
            }
            if(window.LiveState.GroupCount!=3 || window.NoticeSoundPlayCount!=1)throw new Exception($"Same-source grouping/sound dedup failed: count={window.LiveState.GroupCount}; sounds={window.NoticeSoundPlayCount}");
            Click();await Task.Delay(650);Capture("notification-reader.png");
            if(!window.LiveState.Expanded || window.ActualHeight>162.5)throw new Exception("Notification reader height invalid");
            settings.PrivatePreview=true;
            window.AcceptNotification(new(source,"灵动胶囊","隐藏标题","隐藏正文",++id),DateTimeOffset.Now);await Task.Delay(650);Capture("notification-private.png");
            if(((FrameworkElement)window.FindName("NoticeContent")).Visibility!=Visibility.Collapsed || ((FrameworkElement)window.FindName("NoticePrivate")).Visibility!=Visibility.Visible)throw new Exception("Private preview exposes content");
            Click();if(!window.LiveState.Expanded)throw new Exception("Private preview did not expand");Click();
            settings.QuietUntil=DateTimeOffset.UtcNow.AddMinutes(30);
            if(window.AcceptNotification(new(source,"App","Muted","",++id),DateTimeOffset.Now) || window.LiveState.Notification!=null)throw new Exception("Quiet period did not reject incoming notice");
            settings.QuietUntil=default;settings.MutedSources.Add(source);
            if(window.AcceptNotification(new(source,"App","Muted source","",++id),DateTimeOffset.Now))throw new Exception("Muted source accepted");
            settings.MutedSources.Clear();settings.NotificationSoundEnabled=false;
            window.AcceptNotification(new(source,"App","Swipe","",++id),DateTimeOffset.Now);var swiped=window.LiveState.Notification;
            if(window.TrySwipeNotification(swiped,11.9) || window.LiveState.Notification==null)throw new Exception("Subthreshold swipe dismissed notification");
            window.AcceptNotification(new(source,"App","Replacement","",++id),DateTimeOffset.Now);
            if(window.TrySwipeNotification(swiped,30) || window.LiveState.Notification==null)throw new Exception("Old gesture dismissed replacement notification");
            settings.SwipeNotifications=false;if(window.TrySwipeNotification(window.LiveState.Notification,30))throw new Exception("Disabled swipe dismissed notification");
            settings.SwipeNotifications=true;if(!window.TrySwipeNotification(window.LiveState.Notification,12) || window.LiveState.Notification!=null)throw new Exception("Threshold swipe did not dismiss notification");
            Console.WriteLine("PASS Actual WPF three notification modes, grouped sound, reader, privacy, quiet/source filters and identity-safe swipe threshold");
        }
        finally{window.Close();}
    }
}

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capsule.App;
using Capsule.Core;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

internal static class NativeToastTests
{
    internal static async Task RunAsync()
    {
        if(UserNotificationListener.Current.GetAccessStatus()!=UserNotificationListenerAccessStatus.Allowed)
            throw new InvalidOperationException("Native toast test requires existing notification access; no permission request was issued.");
        var suffix=Guid.NewGuid().ToString("N");
        var id="LingdongCapsule.NativeTest."+suffix;
        var registryPath=@"Software\Classes\AppUserModelId\"+id;
        var shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Capsule Native Test "+suffix+".lnk");
        var root=Path.GetFullPath(Path.Combine("artifacts","native-toast-tests",suffix));Directory.CreateDirectory(root);
        var imagePath=Path.Combine(root,"own-avatar.png");
        var bitmap=BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,Enumerable.Range(0,32*32).SelectMany(_=>new byte[]{220,100,40,255}).ToArray(),128);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(imagePath))encoder.Save(output);
        if(File.Exists(shortcut))throw new IOException("Refusing existing test shortcut");
        using(var existing=Registry.CurrentUser.OpenSubKey(registryPath))if(existing!=null)throw new IOException("Refusing existing test identity");
        ToastNotifier? notifier=null;ToastNotification? toast=null;
        try
        {
            using(var key=Registry.CurrentUser.CreateSubKey(registryPath)){key.SetValue("DisplayName","Capsule native test");key.SetValue("CustomActivator","{B66EE1D4-59B7-4D94-B6D6-C4D33DC2A147}");}
            TestShortcut.Create(shortcut,id);
            using var service=new NotificationService(persistSources:false);
            // Mute existing other identities before adapting text. Only own generated content is asserted/logged.
            async Task<HashSet<string>> MutedAsync()
            {
                var muted=new HashSet<string>();
                foreach(var item in await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast))
                    try{var source=item.AppInfo.AppUserModelId;if(source!=id)muted.Add(source);}catch(Exception){ /* Windows can retain notifications without resolvable AppInfo; adapter skips these too. */ }
                return muted;
            }
            await service.ReadAsync(await MutedAsync());
            var xml=new XmlDocument();
            var uri=System.Security.SecurityElement.Escape(new Uri(imagePath).AbsoluteUri);
            xml.LoadXml($"<toast><visual><binding template='ToastGeneric'><text>Capsule own native test</text><text>Generated test data only.</text><image placement='appLogoOverride' hint-crop='circle' src='{uri}'/></binding></visual><audio silent='true'/></toast>");
            notifier=ToastNotificationManager.CreateToastNotifier(id);toast=new ToastNotification(xml){Tag="own-image",Group="capsule-test"};notifier.Show(toast);
            Notice? found=null;
            for(var i=0;i<30 && found==null;i++){await Task.Delay(500);found=(await service.ReadAsync(await MutedAsync())).FirstOrDefault(n=>n.SourceId==id);}
            if(found==null)throw new Exception("Own real Windows toast did not reach NotificationService; settings="+notifier.Setting);
            var image=await new ToastImageStore().FindAsync(found,CancellationToken.None);
            if(image==null || !image.Avatar || Path.GetFullPath(image.Source)!=imagePath)throw new Exception("Own system toast image row did not match identity/id/time/text");
            using var images=new NoticeImages();var decoded=await images.ResolveAsync(found,systemAvatar:image.Source);
            if(decoded==null || !decoded.Avatar)throw new Exception("Own native avatar failed bounded image decode");
            var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var settings=new Settings{MediaEnabled=false,NotificationsEnabled=true,SystemNotificationImagesEnabled=true,PrivatePreview=false,NotificationSoundEnabled=false,MotionMs=150,MutedSources=await MutedAsync()};
            var window=new MainWindow(suppressOnboarding:true,initialSettings:settings,localPluginDirectory:Path.Combine(root,"plugins"),pluginSettingsWriter:()=>{}){ShowActivated=false};
            try
            {
                window.Show();await Task.Delay(1000);
                // The same accepted real Notice enters the normal resolver; no bitmap is injected.
                if(!window.AcceptNotification(found,DateTimeOffset.Now))throw new Exception("Own native notice was rejected by island");
                var icon=(System.Windows.Controls.Image)window.FindName("NoticeIcon");
                var frame=(Border)window.FindName("NoticeIconFrame");
                bool HasAvatar()
                {
                    if(icon.Clip is not RectangleGeometry clip || clip.RadiusX<=10 || icon.Source is not BitmapSource pixels)return false;
                    var converted=new FormatConvertedBitmap(pixels,PixelFormats.Bgra32,null,0);var pixel=new byte[4];converted.CopyPixels(new Int32Rect(converted.PixelWidth/2,converted.PixelHeight/2,1,1),pixel,4,0);
                    return pixel[0]==220 && pixel[1]==100 && pixel[2]==40 && pixel[3]==255;
                }
                for(var i=0;i<30 && !HasAvatar();i++)await Task.Delay(100);
                if(window.LiveState.Notification?.SourceId!=id || !HasAvatar() || frame.Visibility!=Visibility.Visible)throw new Exception("Real system avatar did not reach actual island");
                var trace=new List<object>();var settled=0;
                for(var attempt=0;attempt<40 && settled<3;attempt++)
                {
                    window.UpdateLayout();var bounds=frame.TransformToAncestor(window).TransformBounds(new Rect(frame.RenderSize));
                    var target=(double)typeof(MainWindow).GetField("desiredHeight",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                    trace.Add(new{windowHeight=window.ActualHeight,windowWidth=window.ActualWidth,targetHeight=target,frameTop=bounds.Top,frameBottom=bounds.Bottom,frameHeight=frame.ActualHeight,scale=window.ActiveVisualScale,dpi=VisualTreeHelper.GetDpi(window).DpiScaleY});
                    settled=Math.Abs(window.ActualHeight-target)<.5 && bounds.Top>=-.5 && bounds.Bottom<=window.ActualHeight+.5 ? settled+1 : 0;
                    if(settled<3)await Task.Delay(50);
                }
                File.WriteAllText(Path.Combine(root,"geometry.json"),JsonSerializer.Serialize(trace));
                if(settled<3)throw new Exception("Native avatar did not reach stable unclipped target geometry");
                if(window.LiveState.Notification?.SourceId!=id || !HasAvatar())throw new Exception("Own notice changed before screenshot; no other content was captured");
                var rendered=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);rendered.Render(window);
                var screenshot=new PngBitmapEncoder();screenshot.Frames.Add(BitmapFrame.Create(rendered));using(var output=File.Create(Path.Combine(root,"own-native-island.png")))screenshot.Save(output);
                var render=typeof(MainWindow).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
                settings.SystemNotificationImagesEnabled=false;render.Invoke(window,null);
                if(HasAvatar() || icon.Clip is not RectangleGeometry fallback || fallback.RadiusX!=4)throw new Exception("Disabled system image retained avatar");
                settings.SystemNotificationImagesEnabled=true;render.Invoke(window,null);
                if(!HasAvatar())throw new Exception("System avatar did not recover after re-enable");
                settings.PrivatePreview=true;render.Invoke(window,null);
                if(frame.Visibility!=Visibility.Collapsed)throw new Exception("Private preview exposed native avatar");
            }
            finally{window.Close();}
            File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new{passed=true,notificationAdapter=true,systemImageRow=true,avatarDecoded=true,actualIsland=true,disableFallback=true,privatePreview=true,scope="Own generated toast only; real adapter Notice supplied to actual island resolver; no physical interaction or third-party notification acceptance"}));
            Console.WriteLine("PASS native Windows toast -> adapter -> exact system image row -> actual island avatar, disable fallback and private preview; evidence: "+root);
        }
        finally
        {
            if(toast!=null)notifier?.Hide(toast);
            ToastNotificationManager.History.Clear(id);
            if(File.Exists(shortcut))File.Delete(shortcut);
            Registry.CurrentUser.DeleteSubKey(registryPath,false);
        }
    }
}

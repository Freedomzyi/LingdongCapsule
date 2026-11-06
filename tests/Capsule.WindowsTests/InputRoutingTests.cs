using System.Reflection;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Capsule.App;
using Capsule.Core;

internal static class InputRoutingTests
{
    internal static async Task RunAsync()
    {
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var settings=new Settings{MediaEnabled=false,NotificationsEnabled=false,OnlineCoversEnabled=false,HardwareEnabled=true,ExpandedMediaControls=true,ReturnToPlayer=true};
        var window=new MainWindow(suppressOnboarding:true,initialSettings:settings,pluginSettingsWriter:()=>{}){ShowActivated=false};
        try
        {
            window.Show();await Task.Delay(300);
            ((DispatcherTimer)typeof(MainWindow).GetField("timer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Stop();
            var render=typeof(MainWindow).GetMethod("Render",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
            var shell=(Border)window.FindName("Shell");var media=(FrameworkElement)window.FindName("MediaPanel");var controls=(FrameworkElement)window.FindName("Controls");
            void Refresh(){render.Invoke(window,null);window.UpdateLayout();}
            var firstSeekTrack=new MediaSnapshot("Owned same title","First artist",true,true,"First album",TimeSpan.FromSeconds(30),"owned-player");
            window.LiveState.UpdateMedia(firstSeekTrack);Refresh();
            var seekBar=(Slider)window.FindName("SeekBar");
            seekBar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=seekBar});
            var seekField=typeof(MainWindow).GetField("seekTrack",BindingFlags.Instance|BindingFlags.NonPublic)!;
            if(seekField.GetValue(window) is not MediaSnapshot captured || !captured.SameTrackAs(firstSeekTrack))throw new Exception("Actual seek mouse route did not capture the starting track");
            window.LiveState.UpdateMedia(firstSeekTrack with{Artist="Second artist"});
            var unexpectedSeekDialog=false;var dialogGuard=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
            dialogGuard.Tick+=(_,_)=>{foreach(var dialog in app.Windows.OfType<Window>().Where(item=>item.Title=="无法定位播放进度").ToArray()){unexpectedSeekDialog=true;dialog.Close();}};
            dialogGuard.Start();
            try{await ((Task)typeof(MainWindow).GetMethod("CommitSeekAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!).WaitAsync(TimeSpan.FromSeconds(5));}
            finally{dialogGuard.Stop();}
            if(unexpectedSeekDialog || seekField.GetValue(window)!=null || (bool)typeof(MainWindow).GetField("seeking",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!)throw new Exception("Same-title replacement did not cancel and release the actual WPF drag target");
            Console.WriteLine("PASS Actual WPF seek mouse route captures full track and cancels same-title artist replacement before control dispatch");
            void MouseUp(FrameworkElement source,bool right)
            {
                var args=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,right ? MouseButton.Right : MouseButton.Left){RoutedEvent=right ? UIElement.MouseRightButtonUpEvent : UIElement.MouseLeftButtonUpEvent,Source=source};
                shell.RaiseEvent(args);
            }
            void CheckPage(string name)
            {
                var page=app.Windows.OfType<SettingsWindow>().Single();page.UpdateLayout();
                if(!Descendants<CheckBox>(page).Any(check=>System.Windows.Automation.AutomationProperties.GetName(check)==name))throw new Exception("Context right click opened wrong settings page: "+name);
            }
            window.LiveState.UpdateMedia(new("Owned input fixture","",true,true));Refresh();
            MouseUp(media,false);if(controls.Visibility==Visibility.Visible)throw new Exception("Cover-launch mode also expanded on media left click");
            MouseUp(media,true);if(controls.Visibility!=Visibility.Visible || app.Windows.OfType<SettingsWindow>().Any())throw new Exception("Media right click did not expand in cover-launch mode");
            MouseUp(media,false);if(controls.Visibility!=Visibility.Visible)throw new Exception("Expanded media blank left click unexpectedly collapsed panel");
            MouseUp(media,true);CheckPage("媒体控制");
            var page=app.Windows.OfType<SettingsWindow>().Single();page.WindowState=WindowState.Minimized;
            MouseUp(media,true);if(page.WindowState!=WindowState.Normal || app.Windows.OfType<SettingsWindow>().Count()!=1)throw new Exception("Context settings duplicated/minimized instead of restoring");page.Close();
            typeof(MainWindow).GetField("controlsOpen",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,false);
            settings.ExpandedMediaControls=false;Refresh();MouseUp(media,false);MouseUp(media,true);CheckPage("媒体控制");app.Windows.OfType<SettingsWindow>().Single().Close();
            settings.ExpandedMediaControls=true;Refresh();if(controls.Visibility==Visibility.Visible)throw new Exception("Disabled expansion retained a latent open panel");
            settings.ExpandedMediaControls=true;settings.ReturnToPlayer=false;Refresh();MouseUp(media,false);
            if(controls.Visibility!=Visibility.Visible)throw new Exception("Media left click did not expand with cover launch off");
            typeof(MainWindow).GetField("controlsOpen",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,false);
            settings.ReturnToPlayer=true;window.LiveState.UpdateMedia(new("Owned paused fixture","",false,true));Refresh();MouseUp((FrameworkElement)window.FindName("ClockTime"),false);
            if(controls.Visibility!=Visibility.Visible)throw new Exception("Paused clock lost media expansion entry");
            typeof(MainWindow).GetField("controlsOpen",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,false);
            window.LiveState.UpdateMedia(new("","",false,false));Refresh();
            MouseUp((FrameworkElement)window.FindName("ClockTime"),true);CheckPage("待机模式");app.Windows.OfType<SettingsWindow>().Single().Close();
            MouseUp((FrameworkElement)window.FindName("CpuUsage"),true);CheckPage("待机模式");app.Windows.OfType<SettingsWindow>().Single().Close();
            window.OpenOverview();Refresh();var overview=(FrameworkElement)window.FindName("OverviewHost");
            if(overview.Visibility!=Visibility.Visible)throw new Exception("Own overview did not open");MouseUp(overview,true);
            if(overview.Visibility!=Visibility.Collapsed || app.Windows.OfType<SettingsWindow>().Any())throw new Exception("Overview right click opened settings instead of collapsing");
            window.LiveState.UpdateMedia(new("Own media switch","Own artist",true,true));Refresh();
            var evidence=Path.Combine(Environment.CurrentDirectory,"artifacts","input-routing-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
            void Capture(string name){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*2),(int)Math.Ceiling(window.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(evidence,name+".png"));png.Save(output);}
            await Task.Delay(550);Capture("media-compact-no-switch");
            var information=(Button)window.FindName("InformationSwitch");
            if(information.Content is not PanelSwitchIcon switchGlyph || information.Width<28 || information.Height<28)
                throw new Exception("Panel switch lacks vector glyph or comfortable hit target");
            if(information.Visibility!=Visibility.Collapsed)throw new Exception("Compact playing media exposed information switch before click");
            MouseUp(shell,false);await Task.Delay(550);Capture("media-information-switch");
            if(information.Visibility!=Visibility.Visible || !Equals(information.ToolTip,"系统信息"))throw new Exception("Playing media has no discoverable information switch");
            if(switchGlyph.Media)throw new Exception("Media panel has the wrong destination glyph");
            information.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(550);Capture("information-return-switch");
            if(overview.Visibility!=Visibility.Visible || !Equals(information.ToolTip,"返回媒体") || window.LiveState.Media.Title!="Own media switch")throw new Exception("Information switch stopped/replaced media or failed to show overview");
            if(!switchGlyph.Media)throw new Exception("Overview lacks the return-to-media vector glyph");
            var overviewBounds=overview.TransformToAncestor(shell).TransformBounds(new Rect(overview.RenderSize));
            if(Math.Abs(overviewBounds.Left-(shell.ActualWidth-overviewBounds.Right))>1)throw new Exception("Overview is not horizontally centered in shell");
            var buttonBounds=information.TransformToAncestor(shell).TransformBounds(new Rect(information.RenderSize));
            if(overviewBounds.Top<buttonBounds.Bottom)throw new Exception("Information header overlaps centered overview content");
            if(overview is ContentControl {Content:OverviewView view} && !view.ContentFits)throw new Exception("Centered overview content clipped below header");
            information.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(overview.Visibility!=Visibility.Collapsed || media.Visibility!=Visibility.Visible)throw new Exception("Information switch did not return to live media");
            if(information.Visibility!=Visibility.Collapsed)throw new Exception("Return to compact media left switch visible");
            settings.ExpandedMediaControls=false;settings.ReturnToPlayer=false;Refresh();MouseUp(media,false);
            if(information.Visibility!=Visibility.Visible || controls.Visibility==Visibility.Visible)throw new Exception("Click with inline controls did not reveal only information switch");
            settings.StandbyActive=true;settings.StandbyScene=1;Refresh();
            if(information.Visibility!=Visibility.Collapsed)throw new Exception("Standby clock leaked stale media information switch");
            settings.StandbyScene=2;Refresh();
            if(information.Visibility!=Visibility.Collapsed)throw new Exception("Blank standby leaked stale media information switch");
            settings.StandbyActive=false;Refresh();
            settings.OverviewEnabled=false;Refresh();if(information.Visibility!=Visibility.Collapsed)throw new Exception("Information switch ignored disabled overview component");
            Console.WriteLine("PASS actual playing-media information switch, live-state preservation, return and component enable boundary; evidence: "+evidence);
            Console.WriteLine("PASS actual WPF source-based right-click expansion/settings, single-window restore, left-click mode, paused clock and overview collapse; routed events, no physical input or real player control");
        }
        finally{window.Close();}
        await PendingControlCloseAsync(app);
    }
    private static async Task PendingControlCloseAsync(Capsule.App.App app)
    {
        const string id="LingdongCapsule.Owned.close-control";using var publisher=await OwnedMediaPublisher.StartAsync(id);
        var settings=new Settings{MediaEnabled=true,NotificationsEnabled=false,LyricsEnabled=false,OnlineCoversEnabled=false,ShowSpectrum=false,ManualMediaSource=id};
        var main=new MainWindow(suppressOnboarding:true,initialSettings:settings,pluginSettingsWriter:()=>{}){ShowActivated=false};
        var lateFeedback=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>{foreach(var dialog in app.Windows.OfType<Window>().Where(item=>item.Title=="播放器未响应" || item.Title=="无法定位播放进度").ToArray()){lateFeedback=true;dialog.Close();}};
        try
        {
            main.Show();for(var attempt=0;attempt<40 && main.LiveState.Media.Title!=publisher.Title;attempt++)await Task.Delay(100);
            if(main.LiveState.Media.Title!=publisher.Title)throw new Exception("Owned media did not reach close-control fixture");
            var play=(Button)main.FindName("Play");timer.Start();play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(play.IsEnabled)throw new Exception("Media control fixture did not enter pending state");
            main.Close();await Task.Delay(650);
            if(lateFeedback)throw new Exception("Closing island with native control pending opened late player failure dialog");
            Console.WriteLine("PASS owned native media control pending at close returns without late feedback; no real player or master volume controlled");
        }
        finally{timer.Stop();if(main.IsVisible)main.Close();}
    }    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

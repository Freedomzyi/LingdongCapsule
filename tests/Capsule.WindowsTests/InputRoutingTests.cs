using System.Reflection;
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
            Console.WriteLine("PASS actual WPF source-based right-click expansion/settings, single-window restore, left-click mode, paused clock and overview collapse; routed events, no physical input or real player control");
        }
        finally{window.Close();}
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

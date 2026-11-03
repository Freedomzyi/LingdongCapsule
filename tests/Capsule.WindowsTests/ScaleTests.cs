using Capsule.App;
using Capsule.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class ScaleTests
{
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    internal static async Task RunAsync()
    {
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","scale-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var path=Path.Combine(root,"settings.json");
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var invalid=new Settings{VisualScale=double.NaN};invalid.Normalize();Check(invalid.VisualScale==1,"Non-finite visual ratio was not normalized");invalid.VisualScale=.1;invalid.Normalize();Check(invalid.VisualScale==.5,"Visual ratio minimum was not enforced");
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var settings=new Settings{MediaEnabled=false,NotificationsEnabled=false,Width=280,VisualScale=2,ThemeMode=0};
        var island=new MainWindow(suppressOnboarding:true,initialSettings:settings){ShowActivated=false};
        var render=typeof(MainWindow).GetMethod("Render",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,Type.EmptyTypes,null)!;
        void Render(){render.Invoke(island,null);}
        void Capture(Window window,string name)
        {
            window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(root,name+".png"));encoder.Save(output);
        }
        try
        {
            island.Show();await Task.Delay(650);island.UpdateLayout();
            var time=(TextBlock)island.FindName("ClockTime");var timeBounds=time.TransformToAncestor(island).TransformBounds(new Rect(new Point(),time.RenderSize));
            Check(Math.Abs(timeBounds.Width-time.ActualWidth*2)<1 && Math.Abs(island.ActualHeight-70)<1,"Clock contents did not scale with actual island");Capture(island,"clock-2x-dark");
            var wake=(Button)island.FindName("WakeButton");wake.Visibility=Visibility.Visible;island.UpdateLayout();
            var visibility=typeof(MainWindow).GetField("visibilityResult",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;visibility.SetValue(island,new IslandVisibilityResult(true,true,true));
            var handle=new WindowInteropHelper(island).Handle;
            IntPtr Coordinates(Point point)=>new(((long)(ushort)(short)point.Y<<16)|(ushort)(short)point.X);
            Check(SendMessage(handle,0x0084,IntPtr.Zero,Coordinates(wake.PointToScreen(new Point(30,18))))!=new IntPtr(-1),"Scaled outer part of wake button incorrectly passed native input through");
            Check(SendMessage(handle,0x0084,IntPtr.Zero,Coordinates(island.PointToScreen(new Point(2,5))))==new IntPtr(-1),"Outside scaled wake button did not pass native input through");
            visibility.SetValue(island,default(IslandVisibilityResult));wake.Visibility=Visibility.Collapsed;
            var notice=new Notice("owned.scale","Owned scale","Owned title","Owned body",1);settings.SwipeNotifications=true;island.LiveState.Show(notice,DateTimeOffset.Now);Render();await Task.Delay(550);
            Check(!island.TrySwipeNotification(notice,11) && island.TrySwipeNotification(notice,12),"Scaled notification changed logical swipe threshold");
            settings.VisualScale=100;island.OpenOverview();await Task.Delay(600);island.UpdateLayout();
            var primary=System.Windows.Forms.Screen.PrimaryScreen!;
            Check(GetWindowRect(handle,out var bounds) && bounds.Left>=primary.WorkingArea.Left && bounds.Top>=primary.WorkingArea.Top && bounds.Right<=primary.WorkingArea.Right && bounds.Bottom<=primary.WorkingArea.Bottom && settings.VisualScale==100 && island.ActiveVisualScale<100,"Huge scale escaped screen or overwrote preferred ratio");Capture(island,"overview-screen-fit");
            settings.VisualScale=1;island.SetIslandHidden(true);island.SetIslandHidden(false);Render();await Task.Delay(550);
            using var shortcuts=new GlobalShortcuts(settings);
            var page=new SettingsWindow(settings,()=>Task.FromResult("Owned test"),()=>{settings.SaveTo(path);Render();},()=>{},_=>{},()=>{},()=>new Dictionary<string,string>(),shortcuts,_=>{},()=>[],_=>{},_=>{},()=>[],_=>{},(_,_)=>false,()=>[],(_,_)=>{}){ShowActivated=false};
            try
            {
                page.Show();page.UpdateLayout();Button Control(string name)=>Descendants<Button>(page).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)==name);
                Control("增大视觉比例").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(settings.VisualScale==1.05 && Settings.LoadFrom(path).VisualScale==1.05,"Scale step did not persist to existing settings");
                Control("还原视觉比例").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for(var count=0;count<10;count++)Control("减小视觉比例").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(settings.VisualScale==.5 && !Control("减小视觉比例").IsEnabled && Settings.LoadFrom(path).VisualScale==.5,"Scale minimum/restore control lost correct state");
                Control("增大视觉比例").BringIntoView();await Task.Delay(180);Capture(page,"settings-scale");
                Control("还原视觉比例").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(settings.VisualScale==1,"Scale reset did not restore standard geometry");
                Control("还原*全局上限／播放宽度").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(settings.Width==195 && Settings.LoadFrom(path).Width==195,"Global width reset did not use canonical 195 or persist it");
                var motion=Descendants<Slider>(page).Single(item=>item.Minimum==150 && item.Maximum==900);
                Check(motion.TickFrequency==50,"Motion control does not follow the canonical 50ms step");
                motion.Value=650;Control("还原*收放时长").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(settings.MotionMs==450,"Motion reset did not preserve canonical default");
                void Tab(string text){Descendants<Button>(page).Single(item=>Equals(item.Content,text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));page.UpdateLayout();}
                Tab("通用设置");var icon=Descendants<Slider>(page).Single(item=>item.Minimum==18);Check(icon.Maximum==195,"Icon slider retained stale maximum after width reset");icon.Value=100;
                Tab("个性化中心");Descendants<Slider>(page).Single(item=>item.Minimum==80 && item.Maximum==300).Value=80;
                Tab("通用设置");Check(icon.Maximum==80 && icon.Value==80 && settings.NotificationIconSize==80 && Settings.LoadFrom(path).NotificationIconSize==80,"Narrower width failed to synchronize icon UI, model and saved setting");
            }
            finally{page.Close();}
        }
        finally{island.Close();}
        Console.WriteLine("PASS actual clock scale, native wake hit bounds, logical swipe, expanded screen fit and settings step/reset/persistence; evidence: "+root);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

using Capsule.App;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

internal static class PlacementTests
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")]private static extern bool AreDpiAwarenessContextsEqual(IntPtr left,IntPtr right);
    internal static async Task RunAsync(string[] args)
    {
        var index=Array.IndexOf(args,"--placement-test");var directory=index+1<args.Length ? Path.GetFullPath(args[index+1]) : throw new ArgumentException("Provide an evidence directory");Directory.CreateDirectory(directory);
        using var publisher=await OwnedMediaPublisher.StartAsync("LingdongCapsule.Owned.geometry");
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();
        var settings=new Settings{MediaEnabled=true,NotificationsEnabled=false,LyricsEnabled=false,OnlineCoversEnabled=false,ShowSpectrum=false,ManualMediaSource="LingdongCapsule.Owned.geometry"};
        var window=new MainWindow(suppressOnboarding:true,initialSettings:settings){ShowActivated=false};
        var render=typeof(MainWindow).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
        var observations=new List<object>();
        try
        {
            window.Show();var handle=new WindowInteropHelper(window).Handle;
            if(!AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(handle),new IntPtr(-4)))throw new Exception("Integration host is not using product PerMonitorV2 awareness");
            for(var attempt=0;attempt<50 && !window.LiveState.Media.Available;attempt++)await Task.Delay(100);
            if(!window.LiveState.Media.Available)throw new Exception("Owned media did not reach placement fixture");
            foreach(var monitor in System.Windows.Forms.Screen.AllScreens)
            foreach(var scale in new[]{.5,1,1.5,2})foreach(var shape in new[]{0,1})foreach(var width in new[]{80d,195d,300d})foreach(var height in new[]{32d,35d,60d})
            {
                settings.MonitorId=monitor.DeviceName;settings.IslandShape=shape;settings.Width=width;settings.CompactHeight=height;settings.VisualScale=scale;
                render.Invoke(window,null);MonitorPlacement.Center(handle,settings.MonitorId,shape==0 ? 0 : 8*window.ActiveVisualScale);await Task.Delay(550);window.UpdateLayout();
                if(!GetWindowRect(handle,out var rect))throw new Exception("Cannot read owned window geometry");
                var dpi=GetDpiForWindow(handle);var work=monitor.WorkingArea;var actualWidth=rect.Right-rect.Left;var actualHeight=rect.Bottom-rect.Top;
                var expectedLeft=work.Left+Math.Max(0,work.Width-actualWidth)/2;
                var effective=window.ActiveVisualScale;var expectedTop=work.Top+(int)Math.Round((shape==0 ? 0 : 8*effective)*dpi/96d);
                var expectedWidth=Math.Min(width,MonitorPlacement.WidthLimit(handle,monitor.DeviceName)/effective)*effective*dpi/96;
                var shell=(FrameworkElement)window.FindName("Shell");var bounds=shell.TransformToAncestor(window).TransformBounds(new System.Windows.Rect(0,0,shell.ActualWidth,shell.ActualHeight));
                if(Math.Abs(rect.Left-expectedLeft)>2 || Math.Abs(rect.Top-expectedTop)>2 || rect.Left<work.Left || rect.Right>work.Right || Math.Abs(actualWidth-expectedWidth)>2 || Math.Abs(actualHeight-height*effective*dpi/96)>2 || Math.Abs(bounds.Width-shell.ActualWidth*effective)>1 || Math.Abs(bounds.Height-shell.ActualHeight*effective)>1 || settings.VisualScale!=scale || settings.Width!=width || settings.MonitorId!=monitor.DeviceName)throw new Exception("Owned island escaped target monitor, did not scale content or lost preferred geometry");
                observations.Add(new{monitor=monitor.DeviceName,dpi,shape,requestedScale=scale,effectiveScale=effective,requestedWidth=width,requestedHeight=height,left=rect.Left,top=rect.Top,width=actualWidth,height=actualHeight,workLeft=work.Left,workTop=work.Top,workWidth=work.Width,workHeight=work.Height});
            }
            settings.MonitorId="owned.nonexistent.monitor";render.Invoke(window,null);MonitorPlacement.Center(handle,settings.MonitorId,8);await Task.Delay(550);
            if(settings.MonitorId!="owned.nonexistent.monitor")throw new Exception("Missing-monitor fallback overwrote user selection");
            var primary=System.Windows.Forms.Screen.PrimaryScreen ?? throw new Exception("No primary monitor available");
            if(!GetWindowRect(handle,out var fallback) || Math.Abs(fallback.Left-(primary.WorkingArea.Left+(primary.WorkingArea.Width-(fallback.Right-fallback.Left))/2))>2)throw new Exception("Missing-monitor fallback did not center on primary screen");
        }
        finally{window.Close();await File.WriteAllTextAsync(Path.Combine(directory,"placement.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine("PASS Actual PerMonitorV2 island geometry on currently attached monitors; "+observations.Count+" cases. Other DPI scales, unplug and physical input remain separate gates.");
    }
}

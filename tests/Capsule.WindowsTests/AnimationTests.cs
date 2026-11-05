using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Capsule.App;
using Capsule.Core;

internal static class AnimationTests
{
    [StructLayout(LayoutKind.Sequential)]private struct WindowPosition{public IntPtr Window,After;public int X,Y,Width,Height;public uint Flags;}
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    internal static async Task RunAsync()
    {
        var root=Path.GetFullPath(Path.Combine("artifacts","animation-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var settings=new Settings{MediaEnabled=false,NotificationsEnabled=false,PrivatePreview=false,MotionMs=450,OnlineCoversEnabled=false};
        var window=new MainWindow(suppressOnboarding:true,initialSettings:settings,localPluginDirectory:Path.Combine(root,"plugins"),pluginSettingsWriter:()=>{}){ShowActivated=false};
        var trace=new List<object>();
        try
        {
            window.Show();await Task.Delay(300);((DispatcherTimer)typeof(MainWindow).GetField("timer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Stop();
            settings.NotificationsEnabled=true;var watch=Stopwatch.StartNew();
            for(var i=0;i<22;i++)
            {
                window.AcceptNotification(new("owned.animation","Own animation fixture",i%2==0 ? "Short" : new string('W',80),"Generated test only",(uint)(i+1)),DateTimeOffset.Now);
                await Task.Delay(50);window.UpdateLayout();
                trace.Add(new{seconds=watch.Elapsed.TotalSeconds,height=window.PresentedHeight,actualHeight=window.ActualHeight,width=window.PresentedWidth});
                if(watch.Elapsed.TotalMilliseconds>750 && Math.Abs(window.PresentedHeight-55)>.01)throw new Exception("Width-only notification updates restarted or stalled height transition");
            }
            await Task.Delay(settings.MotionMs+100);window.UpdateLayout();
            if(Math.Abs(window.PresentedHeight-55)>.01 || Math.Abs(window.ActualHeight-55)>.5)throw new Exception("Final height did not commit after animation completion");
            var render=typeof(MainWindow).GetMethod("Render",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
            window.LiveState.Dismiss();render.Invoke(window,null);await Task.Delay(550);window.UpdateLayout();
            if(Math.Abs(window.Height-settings.CompactHeight)>.01 || Math.Abs(window.ActualHeight-settings.CompactHeight)>.5)throw new Exception("Collapse did not commit compact height");
            async Task Collapse(string label,Action open,Action close)
            {
                open();await Task.Delay(550);window.UpdateLayout();var fromWidth=window.PresentedWidth;var fromHeight=window.PresentedHeight;
                var observed=false;var nativeSamples=0;var worstNative=0d;var toWidth=0d;var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
                IntPtr Observe(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
                {
                    if(observed && message==0x0047){var frame=Marshal.PtrToStructure<WindowPosition>(lp);if((frame.Flags&1)==0){var x=(frame.Width/dpi-toWidth)/(fromWidth-toWidth);var y=(frame.Height/dpi-settings.CompactHeight)/(fromHeight-settings.CompactHeight);if(double.IsFinite(x) && double.IsFinite(y)){worstNative=Math.Max(worstNative,Math.Abs(x-y));nativeSamples++;}}}
                    return IntPtr.Zero;
                }
                var source=System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(window).Handle)!;source.AddHook(Observe);
                var frameTimes=new List<double>();var frameWatch=Stopwatch.StartNew();
                TimeSpan? lastRenderingTime=null;var duplicateFrames=0;
                void Frame(object? sender,EventArgs args){if(args is System.Windows.Media.RenderingEventArgs frame){if(lastRenderingTime==frame.RenderingTime){duplicateFrames++;return;}lastRenderingTime=frame.RenderingTime;}frameTimes.Add(frameWatch.Elapsed.TotalMilliseconds);}
                System.Windows.Media.CompositionTarget.Rendering+=Frame;
                close();
                var incoming=(FrameworkElement)typeof(MainWindow).GetField("compactLayout",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                if(incoming.Opacity>.1)throw new Exception(label+" incoming compact content flashed before fade started");
                toWidth=(double)typeof(MainWindow).GetField("desiredWidth",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;observed=true;
                var samples=0;var worst=0d;var deadline=Stopwatch.StartNew();
                while(window.SizeTransitionActive && deadline.ElapsedMilliseconds<1000)
                {
                    await Task.Delay(15);var x=(window.PresentedWidth-toWidth)/(fromWidth-toWidth);var y=(window.PresentedHeight-settings.CompactHeight)/(fromHeight-settings.CompactHeight);
                    if(double.IsFinite(x) && double.IsFinite(y)){worst=Math.Max(worst,Math.Abs(x-y));samples++;}
                    trace.Add(new{label,seconds=deadline.Elapsed.TotalSeconds,width=window.PresentedWidth,height=window.PresentedHeight,hostWidth=window.Width,hostHeight=window.Height,actualHeight=window.ActualHeight});
                }
                System.Windows.Media.CompositionTarget.Rendering-=Frame;
                observed=false;source.RemoveHook(Observe);window.UpdateLayout();
                if(incoming.Opacity<.99)throw new Exception(label+" incoming compact content failed to finish fade");
                var gaps=frameTimes.Zip(frameTimes.Skip(1),(a,b)=>b-a).Order().ToArray();
                trace.Add(new{label,duplicateFrames,frameCount=frameTimes.Count,p95FrameGapMs=gaps.Length>0 ? gaps[(int)Math.Floor((gaps.Length-1)*.95)] : 0,maxFrameGapMs=gaps.Length>0 ? gaps[^1] : 0,framesOver33Ms=gaps.Count(gap=>gap>33.34),scope="UI composition callback intervals on own test window, not physical presentation or input latency"});
                trace.Add(new{label,nativeSamples,worstNative,samples,worst});
                if(window.SizeTransitionActive || Math.Abs(window.Height-settings.CompactHeight)>.01 || Math.Abs(window.ActualHeight-settings.CompactHeight)>.5 || samples<5 || worst>.08 || nativeSamples<1 || nativeSamples>2 || worstNative>.025)throw new Exception(label+" collapse stalled or width/height progress diverged: "+worst+"; native="+worstNative);
            }
            var dismiss=typeof(MainWindow).GetMethod("Dismiss",BindingFlags.Instance|BindingFlags.NonPublic)!;
            await Collapse("overview",()=>window.OpenOverview(),()=>dismiss.Invoke(window,null));
            await Collapse("expanded-media",()=>{window.LiveState.UpdateMedia(new("Own transition","Own artist",true,true));typeof(MainWindow).GetField("controlsOpen",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);render.Invoke(window,null);},()=>{window.LiveState.UpdateMedia(new("","",false,false));dismiss.Invoke(window,null);});
            window.LiveState.UpdateMedia(new("","",false,false));window.OpenOverview();await Task.Delay(550);
            dismiss.Invoke(window,null);await Task.Delay(85);window.UpdateLayout();
            var surface=(FrameworkElement)window.FindName("IslandSurface");
            var surfaceBounds=surface.TransformToAncestor(window).TransformBounds(new Rect(surface.RenderSize));
            if(Math.Abs(surfaceBounds.Left-(window.ActualWidth-surfaceBounds.Right))>1 || Math.Abs(surfaceBounds.Top)>.5)throw new Exception("Stable host shifted animated surface away from centered top anchor");
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*2),(int)Math.Ceiling(window.ActualHeight*2),192,192,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(root,"collapse-centered-surface.png")))png.Save(file);
            var ownHandle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
            IntPtr Hit(Point point){var screen=window.PointToScreen(point);var packed=((long)(ushort)(short)screen.Y<<16)|(ushort)(short)screen.X;return SendMessage(ownHandle,0x0084,IntPtr.Zero,new IntPtr(packed));}
            if(Hit(new Point(1,window.ActualHeight-1))!=new IntPtr(-1))throw new Exception("Transparent host margin intercepts input during collapse");
            if(Hit(new Point(window.ActualWidth/2,surfaceBounds.Height/2))==new IntPtr(-1))throw new Exception("Animated visible surface lost native input during collapse");
            var beforeReverse=window.PresentedWidth;var beforeReverseHeight=window.PresentedHeight;
            window.OpenOverview();
            if(Math.Abs(window.PresentedWidth-beforeReverse)>.01 || Math.Abs(window.PresentedHeight-beforeReverseHeight)>.01)throw new Exception("Reversing collapse jumped to host dimensions instead of visible surface");
            await Task.Delay(550);window.UpdateLayout();
            if(window.SizeTransitionActive || Math.Abs(window.PresentedWidth-window.Width)>.5 || Math.Abs(window.PresentedHeight-window.Height)>.5)throw new Exception("Reversed transition failed to settle host to visible surface");
            Console.WriteLine("PASS stable host transparent margins pass native input, visible surface receives input, top center anchor and reverse transition retain visible geometry");
            Console.WriteLine("PASS overview and expanded-media collapse retain coordinated geometry on composition frames, complete on deadline and stop their render subscription");
            Console.WriteLine("PASS actual notification width churn preserves independent height deadline, final geometry and collapse; own WPF fixtures only; evidence: "+root);
        }
        finally{File.WriteAllText(Path.Combine(root,"geometry.json"),JsonSerializer.Serialize(trace));window.Close();}
    }
}

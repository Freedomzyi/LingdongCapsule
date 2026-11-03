using Capsule.App;
using System.Windows;
using System.Windows.Media;
using Capsule.Core;

internal static class MaterialTests
{
    internal static async Task RunAsync()
    {
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();
        var background=new Window{Width=240,Height=160,Left=80,Top=100,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brushes.Red,ShowInTaskbar=false,ShowActivated=false,Topmost=true};
        var overlay=new Window{Width=120,Height=70,Left=120,Top=140,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Lime,ShowInTaskbar=false,ShowActivated=false,Topmost=true};
        try
        {
            background.Show();overlay.Show();await Task.Delay(500);
            var point=overlay.PointToScreen(new Point(30,30));
            using var capture=new System.Drawing.Bitmap(1,1);
            using(var graphics=System.Drawing.Graphics.FromImage(capture))graphics.CopyFromScreen((int)point.X,(int)point.Y,0,0,new System.Drawing.Size(1,1),System.Drawing.CopyPixelOperation.SourceCopy);
            var pixel=capture.GetPixel(0,0);
            Console.WriteLine($"DIAGNOSTIC SRCCOPY owned overlay pixel RGB={pixel.R},{pixel.G},{pixel.B}; excludesOverlay={pixel.R>200 && pixel.G<40}");
            var width=280;var height=70;var margin=32;var sw=width+margin*2;var sh=height+margin*2;var source=new byte[sw*sh*4];
            for(var y=0;y<sh;y++)for(var x=0;x<sw;x++){var i=(y*sw+x)*4;source[i]=(byte)(x*7%256);source[i+1]=(byte)(y*11%256);source[i+2]=(byte)((x/20+y/20)%2*220);source[i+3]=255;}
            var folder=System.IO.Path.Combine("artifacts","verification","material");System.IO.Directory.CreateDirectory(folder);
            foreach(var acrylic in new[]{false,true})
            {
                var pixels=GlassMaterial.Compose(source,sw,sh,width,height,margin,35,35,new(),acrylic,false);
                var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file=System.IO.File.Create(System.IO.Path.Combine(folder,acrylic ? "acrylic-synthetic.png" : "glass-synthetic.png"));encoder.Save(file);
            }
            Console.WriteLine("PASS Synthetic material pixels encoded through WPF; desktop diagnostic is not backend acceptance");
            overlay.Close();
            var settings=new Settings{MaterialKind=2,NotificationsEnabled=false,MediaEnabled=false,ShowInCapture=true,BackgroundOpacity=100};
            var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:settings);
            try
            {
                window.Show();await Task.Delay(500);window.Top=160;await Task.Delay(100);var location=window.PointToScreen(new Point());
                var backgroundScale=VisualTreeHelper.GetDpi(background);
                background.Left=(location.X-80)/backgroundScale.DpiScaleX;background.Top=Math.Max(0,location.Y-10)/backgroundScale.DpiScaleY;background.Width=500;background.Height=400;
                await Task.Delay(800);
                for(var attempt=0;attempt<40 && !window.MaterialHasFrame;attempt++)await Task.Delay(100);
                if(!window.MaterialHasFrame || !window.MaterialRunning)throw new Exception("Actual island did not produce glass background");
                var actual=window.MaterialFrame!;var data=new byte[actual.PixelWidth*actual.PixelHeight*4];actual.CopyPixels(data,actual.PixelWidth*4,0);
                if(data.Where((_,i)=>i%4==2).Average(b=>(int)b)<150 || data.Where((_,i)=>i%4==1).Average(b=>(int)b)>80)throw new Exception($"Actual island background mismatch: R={data.Where((_,i)=>i%4==2).Average(b=>(int)b)}, G={data.Where((_,i)=>i%4==1).Average(b=>(int)b)}");
                window.UpdateLayout();var rendered=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);rendered.Render(window);
                var actualEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder();actualEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rendered));using(var file=System.IO.File.Create(System.IO.Path.Combine(folder,"actual-island-glass.png")))actualEncoder.Save(file);
                var render=typeof(Capsule.App.MainWindow).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
                settings.MaterialKind=1;render.Invoke(window,null);
                for(var attempt=0;attempt<40 && !window.MaterialHasFrame;attempt++)await Task.Delay(100);
                if(!window.MaterialHasFrame || !window.MaterialStatus.Contains("亚克力"))throw new Exception("Actual acrylic background failed");
                settings.VisualScale=2;render.Invoke(window,null);await Task.Delay(650);
                for(var attempt=0;attempt<40 && (!window.MaterialHasFrame || Math.Abs(window.MaterialFrame!.PixelWidth-window.ActualWidth*VisualTreeHelper.GetDpi(window).DpiScaleX*.75)>3);attempt++)await Task.Delay(100);
                if(!window.MaterialHasFrame || Math.Abs(window.MaterialFrame!.PixelWidth-window.ActualWidth*VisualTreeHelper.GetDpi(window).DpiScaleX*.75)>3)throw new Exception("Scaled material did not refresh at actual physical window size");
                window.SetIslandHidden(true);await Task.Delay(700);
                if(window.MaterialRunning || window.MaterialHasFrame)throw new Exception("Hidden island retained capture or stale async frame");
                window.SetIslandHidden(false);
                for(var attempt=0;attempt<40 && !window.MaterialHasFrame;attempt++)await Task.Delay(100);
                if(!window.MaterialHasFrame)throw new Exception("Unhidden island did not resume material");
                settings.MaterialKind=0;render.Invoke(window,null);
                if(window.MaterialRunning || window.MaterialHasFrame || ((System.Windows.Controls.Border)window.FindName("Shell")).Background is not SolidColorBrush)throw new Exception("Solid background did not stop and release material");
                Console.WriteLine("PASS Actual island glass/acrylic, underlying pixels, hide cancellation, resume and solid cleanup");
                var cadence=new AnimationCadence{FramesPerSecond=30};var count=0;
                for(var tick=0;tick<144;tick++)if(cadence.Advance(TimeSpan.FromSeconds(tick/144d)))count++;
                if(count>30 || count<25)throw new Exception("Dynamic content frame cap failed");
                Console.WriteLine("PASS Dynamic content cadence caps 144 input ticks to at most 30 redraws");
            }
            finally{window.Close();}
        }
        finally{overlay.Close();background.Close();}
    }
}

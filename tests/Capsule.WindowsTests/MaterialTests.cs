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
        }
        finally{overlay.Close();background.Close();}
    }
}

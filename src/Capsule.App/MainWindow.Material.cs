using Capsule.Core;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Capsule.App;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]private struct MaterialRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out MaterialRect rect);
    private readonly DispatcherTimer materialTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private MagnifiedBackdrop? materialBackdrop;
    private ImageBrush? materialBrush;
    private bool materialBusy;
    private int materialGeneration,materialMode;
    private long materialRetryAt;
    internal string MaterialStatus{get;private set;}="实体背景";
    internal bool MaterialRunning=>materialTimer.IsEnabled;
    internal bool MaterialHasFrame=>materialBrush!=null;
    internal BitmapSource? MaterialFrame=>materialBrush?.ImageSource as BitmapSource;
    private void InitializeMaterial()
    {
        materialTimer.Tick+=async(_,_)=>await RefreshMaterialAsync();
        Closed+=(_,_)=>{materialGeneration++;materialTimer.Stop();materialBackdrop?.Dispose();materialBackdrop=null;materialBrush=null;};
    }
    private void UpdateMaterial()
    {
        LiveLyrics.FrameRate=Spectrum.FrameRate=CoverRing.FrameRate=settings.Glass.FramesPerSecond;
        if(materialMode!=settings.MaterialKind)
        {
            materialMode=settings.MaterialKind;materialGeneration++;materialBrush=null;materialRetryAt=0;
            materialBackdrop?.Dispose();materialBackdrop=null;
            appliedBackgroundAlpha=null;ApplyIslandTheme();
        }
        var enabled=IsLoaded && !closing && settings.MaterialKind!=0 && !visibilityResult.PassThrough;
        materialTimer.Interval=TimeSpan.FromMilliseconds(settings.Glass.CaptureMilliseconds);
        materialTimer.IsEnabled=enabled;
        if(!enabled)
        {
            if(materialBackdrop!=null || materialBrush!=null){materialGeneration++;materialBackdrop?.Dispose();materialBackdrop=null;materialBrush=null;appliedBackgroundAlpha=null;ApplyIslandTheme();}
            MaterialStatus=settings.MaterialKind==0 ? "实体背景" : "材质已暂停";
        }
        if(materialBrush!=null)Shell.Background=materialBrush;
    }
    private async Task RefreshMaterialAsync()
    {
        if(materialBusy || closing || settings.MaterialKind==0 || visibilityResult.PassThrough || Environment.TickCount64<materialRetryAt)return;
        materialBusy=true;var generation=materialGeneration;
        try
        {
            var handle=new WindowInteropHelper(this).Handle;
            if(handle==IntPtr.Zero || !GetWindowRect(handle,out var rect))return;
            var dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
            var width=rect.Right-rect.Left;var height=rect.Bottom-rect.Top;var margin=(int)Math.Ceiling(32*dpi);
            materialBackdrop??=new MagnifiedBackdrop(handle);
            var source=materialBackdrop.Capture(rect.Left-margin,rect.Top-margin,width+margin*2,height+margin*2);
            MaterialStatus=materialBackdrop.Status;
            if(source==null){ClearMaterialFrame();return;}
            var options=settings.Glass;var acrylic=settings.MaterialKind==1;var dark=Design.IsDark;
            var alpha=settings.BackgroundOpacity/100;var corners=Shell.CornerRadius;
            var quality=settings.GlassQuality switch{0=>.5,2=>1d,_=>.75};
            var result=await Task.Run(()=>
            {
                var w=Math.Max(1,(int)Math.Round(width*quality));var h=Math.Max(1,(int)Math.Round(height*quality));var m=Math.Max(1,(int)Math.Round(margin*quality));
                var sw=width+margin*2;var sh=height+margin*2;var tw=w+m*2;var th=h+m*2;var reduced=new byte[checked(tw*th*4)];
                for(var y=0;y<th;y++)for(var x=0;x<tw;x++)
                {
                    var sx=Math.Clamp((int)Math.Round(margin+(x-m)/quality),0,sw-1);var sy=Math.Clamp((int)Math.Round(margin+(y-m)/quality),0,sh-1);
                    Buffer.BlockCopy(source,(sy*sw+sx)*4,reduced,(y*tw+x)*4,4);
                }
                var pixels=GlassMaterial.Compose(reduced,tw,th,w,h,m,corners.TopLeft,corners.BottomLeft,options,acrylic,dark,alpha,dpi*quality);
                var image=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4);image.Freeze();return image;
            });
            if(closing || generation!=materialGeneration || visibilityResult.PassThrough)return;
            materialBrush=new ImageBrush(result){Stretch=Stretch.Fill};materialBrush.Freeze();Shell.Background=materialBrush;
            MaterialStatus=acrylic ? "亚克力背景正常" : "液态玻璃背景正常";
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or ExternalException)
        {MaterialStatus="材质不可用，已回退实体背景（"+error.GetType().Name+"）";ClearMaterialFrame();}
        finally{materialBusy=false;}
    }
    private void ClearMaterialFrame()
    {
        materialBrush=null;appliedBackgroundAlpha=null;ApplyIslandTheme();
        materialRetryAt=Environment.TickCount64+5000;materialBackdrop?.Dispose();materialBackdrop=null;
        // Capability failures retry slowly; explicit setting changes restore rate.
        materialTimer.Interval=TimeSpan.FromSeconds(5);
    }
}

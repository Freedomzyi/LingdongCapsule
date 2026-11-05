using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Capsule.App;

public partial class MainWindow
{
    private double widthFrom,heightFrom,surfaceWidth=104,surfaceHeight=35;
    private long widthStarted,heightStarted;
    private bool widthMoving,heightMoving,sizeTransitionActive;
    private TimeSpan? lastSizeFrame;
    private MonitorPlacement.ResizeTarget? resizeTarget;
    private readonly TranslateTransform wakeTransitionOffset=new();
    internal bool SizeTransitionActive=>sizeTransitionActive;
    internal double PresentedWidth=>sizeTransitionActive ? surfaceWidth : Width;
    internal double PresentedHeight=>sizeTransitionActive ? surfaceHeight : Height;
    private double TransitionTop=>Environment.GetCommandLineArgs().Any(arg=>arg is "--inspect" or "--render-media") ? 80 : settings.IslandShape==0 ? 0 : 8*ActiveVisualScale;
    private void SetSurfaceSize(double width,double height)
    {
        surfaceWidth=width;surfaceHeight=height;
        var scale=Math.Max(.01,islandScale.ScaleX);
        IslandSurface.Width=width/scale;IslandSurface.Height=height/scale;
        WakeButton.RenderTransform=wakeTransitionOffset;
        wakeTransitionOffset.Y=WakeButton.VerticalAlignment==VerticalAlignment.Center ? (height-Height)/2/scale : 0;
    }
    private void Animate(double width,double height)
    {
        var changeWidth=desiredWidth!=width;var changeHeight=desiredHeight!=height;
        if(!changeWidth && !changeHeight){if(!sizeTransitionActive)SetSurfaceSize(width,height);return;}
        var currentWidth=PresentedWidth;var currentHeight=PresentedHeight;
        desiredWidth=width;desiredHeight=height;
        var now=Stopwatch.GetTimestamp();
        if(changeWidth){widthFrom=currentWidth;widthStarted=now;widthMoving=true;}
        if(changeHeight){heightFrom=currentHeight;heightStarted=now;heightMoving=true;}
        if(!sizeTransitionActive)
        {
            resizeTarget=MonitorPlacement.CaptureResizeTarget(new WindowInteropHelper(this).Handle,settings.MonitorId);
            sizeTransitionActive=true;lastSizeFrame=null;
            CompositionTarget.Rendering+=OnSizeFrame;
        }
        SetSurfaceSize(currentWidth,currentHeight);
        // Grow the transparent host at most once per new target. Its animated child
        // remains centered; no synchronous HWND resize is needed on each frame.
        MonitorPlacement.ResizeAndCenter(new WindowInteropHelper(this).Handle,resizeTarget,TransitionTop,Math.Max(Width,width),Math.Max(Height,height));
    }
    private void OnSizeFrame(object? sender,EventArgs args)
    {
        if(closing){StopSizeTransition();return;}
        if(args is RenderingEventArgs frame){if(lastSizeFrame==frame.RenderingTime)return;lastSizeFrame=frame.RenderingTime;}
        double Step(double from,double target,long started,out bool moving)
        {
            var progress=Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds/settings.MotionMs,0,1);
            moving=progress<1;return from+(target-from)*(1-Math.Pow(1-progress,3));
        }
        SetSurfaceSize(widthMoving ? Step(widthFrom,desiredWidth,widthStarted,out widthMoving) : surfaceWidth,
            heightMoving ? Step(heightFrom,desiredHeight,heightStarted,out heightMoving) : surfaceHeight);
        if(!widthMoving && !heightMoving)
        {
            if(!MonitorPlacement.ResizeAndCenter(new WindowInteropHelper(this).Handle,resizeTarget,TransitionTop,desiredWidth,desiredHeight)){Width=desiredWidth;Height=desiredHeight;}
            StopSizeTransition();SetSurfaceSize(desiredWidth,desiredHeight);
        }
    }
    private void StopSizeTransition()
    {
        CompositionTarget.Rendering-=OnSizeFrame;
        sizeTransitionActive=widthMoving=heightMoving=false;lastSizeFrame=null;resizeTarget=null;
    }
}
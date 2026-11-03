namespace Capsule.Core;

public readonly record struct PixelBounds(int Left,int Top,int Width,int Height);
public readonly record struct PixelPoint(int X,int Y);
public static class MonitorGeometry
{
    public static double VisualScale(double preferred,int widthPixels,int heightPixels,uint dpi)
    {
        if(dpi==0)dpi=96;
        preferred=double.IsFinite(preferred) ? Math.Max(.5,preferred) : 1;
        // Keep the minimum logical width and the largest expanded panel reachable.
        var width=WidthLimit(widthPixels,dpi)/80;
        var height=Math.Max(1,Math.Max(0,heightPixels)*96d/dpi-8)/286;
        return Math.Min(preferred,Math.Min(width,height));
    }
    public static double WidthLimit(int pixels,uint dpi,double sideMargin=8)
    {
        if(dpi==0)dpi=96;
        return Math.Max(1,Math.Max(0,pixels)*96d/dpi-2*(double.IsFinite(sideMargin) ? Math.Max(0,sideMargin) : 8));
    }
    public static PixelPoint Center(PixelBounds area,int width,int height,uint dpi,double topDip)
    {
        if(dpi==0)dpi=96;
        var x=(long)area.Left+Math.Max(0,(long)area.Width-Math.Max(0,width))/2;
        var room=Math.Max(0,(long)area.Height-Math.Max(0,height));
        var offset=double.IsFinite(topDip) ? Math.Clamp(topDip*dpi/96,0,room) : 0;
        var y=(long)area.Top+(long)Math.Round(offset);
        return new((int)Math.Clamp(x,int.MinValue,int.MaxValue),(int)Math.Clamp(y,int.MinValue,int.MaxValue));
    }
}

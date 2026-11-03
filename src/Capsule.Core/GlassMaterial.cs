namespace Capsule.Core;

// Independent CPU material compositor. Input/output are tightly packed opaque BGRA,
// never screenshots on disk. Capture and scheduling belong to the Windows host.
public sealed record GlassOptions
{
    public int Blur { get; init; } = 2;
    public int Refraction { get; init; } = 2;
    public int Dispersion { get; init; } = 2;
    public int Rim { get; init; } = 2;
    public int Thickness { get; init; } = 2;
    public int Depth { get; init; } = 2;
    public int Saturation { get; init; } = 2;
    public int Frost { get; init; } = 2;
    public int Brightness { get; init; } = 2;
    public int Capture { get; init; } = 2;
    public int FrameRate { get; init; } = 2;
    public int Zoom { get; init; }
    private static int L(int level) => Math.Clamp(level, 0, 4);
    public GlassOptions Normalized()=>this with{Blur=L(Blur),Refraction=L(Refraction),Dispersion=L(Dispersion),Rim=L(Rim),Thickness=L(Thickness),Depth=L(Depth),Saturation=L(Saturation),Frost=L(Frost),Brightness=L(Brightness),Capture=L(Capture),FrameRate=L(FrameRate),Zoom=L(Zoom)};
    public int CaptureMilliseconds => new[] { 1000, 500, 250, 160, 60 }[L(Capture)];
    public int FramesPerSecond => new[] { 30, 60, 90, 120, 144 }[L(FrameRate)];
    internal int BlurRadius => new[] { 0, 2, 4, 6, 10 }[L(Blur)];
    internal double Bend => L(Refraction) * 6d;
    internal double ColorSpread => L(Dispersion) * .09;
    internal double EdgeLight => L(Rim) * .10;
    internal double EdgeWidth => new[] { 6d, 10, 14, 18, 24 }[L(Thickness)];
    internal double Lens => L(Depth) * .2;
    internal double Colorfulness => new[] { .60, .90, 1.22, 1.50, 1.80 }[L(Saturation)];
    internal double Mist => L(Frost) * .04;
    internal double Exposure => new[] { .80, .90, 1d, 1.08, 1.15 }[L(Brightness)];
    internal double Magnification => new[] { 1d, 1.08, 1.18, 1.32, 1.50 }[L(Zoom)];
}

public static class GlassMaterial
{
    public static byte[] Compose(byte[] backdrop, int sourceWidth, int sourceHeight,
        int width, int height, int margin, double topRadius, double bottomRadius,
        GlassOptions options, bool acrylic, bool dark, double opacity = 1, double pixelsPerDip = 1)
    {
        ArgumentNullException.ThrowIfNull(backdrop);ArgumentNullException.ThrowIfNull(options);
        if(width<1 || height<1 || width>4096 || height>4096 || margin<0 || margin>512 || sourceWidth!=width+margin*2 || sourceHeight!=height+margin*2 || (long)sourceWidth*sourceHeight>4_000_000 || backdrop.LongLength!=(long)sourceWidth*sourceHeight*4)
            throw new ArgumentException("Invalid bounded BGRA backdrop dimensions.");
        if(!double.IsFinite(pixelsPerDip) || pixelsPerDip<=0 || pixelsPerDip>8 || !double.IsFinite(topRadius) || !double.IsFinite(bottomRadius) || !double.IsFinite(opacity))throw new ArgumentException("Invalid material geometry.");
        var blurred=Blur(backdrop,sourceWidth,sourceHeight,(int)Math.Round((acrylic ? 14 : options.BlurRadius)*pixelsPerDip));
        var output=new byte[checked(width*height*4)];var scale=pixelsPerDip;
        var rTop=Math.Clamp(topRadius*scale,0,Math.Min(width,height)/2d);var rBottom=Math.Clamp(bottomRadius*scale,0,Math.Min(width,height)/2d);
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var px=x+.5-width/2d;var py=y+.5-height/2d;
            var radius=py<0 ? rTop : rBottom;
            var qx=Math.Abs(px)-(width/2d-radius);var qy=Math.Abs(py)-(height/2d-radius);
            var ox=Math.Max(0,qx);var oy=Math.Max(0,qy);var length=Math.Sqrt(ox*ox+oy*oy);
            var distance=length+Math.Min(Math.Max(qx,qy),0)-radius;
            double nx,ny;
            if(length>.0001){nx=ox/length*Math.Sign(px);ny=oy/length*Math.Sign(py);}
            else if(qx>qy){nx=Math.Sign(px);ny=0;}else{nx=0;ny=Math.Sign(py);}
            var inward=Math.Max(0,-distance)/scale;
            var edge=acrylic ? 0 : Math.Pow(Math.Clamp(1-inward/options.EdgeWidth,0,1),2);
            var bend=edge*options.Bend*scale;
            var lens=acrylic ? 1 : options.Magnification*(1+options.Lens*.025);
            var sx=margin+(width-1)/2d+px/lens-nx*bend;var sy=margin+(height-1)/2d+py/lens-ny*bend;
            var blue=Sample(blurred,sourceWidth,sourceHeight,sx-nx*bend*options.ColorSpread,sy-ny*bend*options.ColorSpread,0);
            var green=Sample(blurred,sourceWidth,sourceHeight,sx,sy,1);
            var red=Sample(blurred,sourceWidth,sourceHeight,sx+nx*bend*options.ColorSpread,sy+ny*bend*options.ColorSpread,2);
            var gray=red*.2126+green*.7152+blue*.0722;
            var saturation=acrylic ? .85 : options.Colorfulness;
            // Acrylic opacity controls its tint coat over a complete blurred backdrop,
            // rather than revealing an unblurred desktop through the whole surface.
            var mist=acrylic ? (140d/255)*Math.Clamp(opacity,0,1) : options.Mist;
            var tint=dark ? 20d : 245d;var light=acrylic ? 0 : options.EdgeLight*Math.Exp(-inward/1.6)*(.4+.6*Math.Max(0,-nx*.5-ny*.86))*255;
            var offset=(y*width+x)*4;
            double Convert(double channel)=>((gray+(channel-gray)*saturation)*(acrylic ? 1 : options.Exposure))*(1-mist)+tint*mist+light;
            output[offset]=(byte)Math.Clamp(Math.Round(Convert(blue)),0,255);output[offset+1]=(byte)Math.Clamp(Math.Round(Convert(green)),0,255);output[offset+2]=(byte)Math.Clamp(Math.Round(Convert(red)),0,255);
            output[offset+3]=acrylic ? (byte)255 : (byte)Math.Round(Math.Clamp(opacity,0,1)*255);
        }
        return output;
    }
    private static double Sample(byte[] pixels,int width,int height,double x,double y,int channel)
    {
        x=Math.Clamp(x,0,width-1d);y=Math.Clamp(y,0,height-1d);var left=(int)x;var top=(int)y;var right=Math.Min(width-1,left+1);var bottom=Math.Min(height-1,top+1);var fx=x-left;var fy=y-top;
        var a=pixels[(top*width+left)*4+channel]*(1-fx)+pixels[(top*width+right)*4+channel]*fx;
        var b=pixels[(bottom*width+left)*4+channel]*(1-fx)+pixels[(bottom*width+right)*4+channel]*fx;return a*(1-fy)+b*fy;
    }
    private static byte[] Blur(byte[] source,int width,int height,int radius)
    {
        if(radius==0)return source;radius=Math.Clamp(radius,1,112);
        var temporary=new byte[source.Length];var result=new byte[source.Length];
        // Running sums: bounded cost per pixel regardless of radius, clamped edges.
        for(var y=0;y<height;y++)for(var channel=0;channel<3;channel++)
        {
            var sum=0;for(var k=-radius;k<=radius;k++)sum+=source[(y*width+Math.Clamp(k,0,width-1))*4+channel];
            for(var x=0;x<width;x++){temporary[(y*width+x)*4+channel]=(byte)(sum/(radius*2+1));sum-=source[(y*width+Math.Clamp(x-radius,0,width-1))*4+channel];sum+=source[(y*width+Math.Clamp(x+radius+1,0,width-1))*4+channel];}
        }
        for(var x=0;x<width;x++)for(var channel=0;channel<3;channel++)
        {
            var sum=0;for(var k=-radius;k<=radius;k++)sum+=temporary[(Math.Clamp(k,0,height-1)*width+x)*4+channel];
            for(var y=0;y<height;y++){result[(y*width+x)*4+channel]=(byte)(sum/(radius*2+1));sum-=temporary[(Math.Clamp(y-radius,0,height-1)*width+x)*4+channel];sum+=temporary[(Math.Clamp(y+radius+1,0,height-1)*width+x)*4+channel];}
        }
        return result;
    }
}

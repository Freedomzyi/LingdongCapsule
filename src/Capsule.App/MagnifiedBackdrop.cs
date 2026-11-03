using System.Runtime.InteropServices;

namespace Capsule.App;

// Optional, capability-tested backend. Never fall back to self-capturing GDI.
// All operations and the callback belong to the creating UI thread.
internal sealed class MagnifiedBackdrop : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct Header{public uint Width,Height;public Guid Format;public uint Stride,Offset;public nuint Bytes;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool Filter(IntPtr window,IntPtr source,Header sourceHeader,IntPtr destination,Header destinationHeader,NativeRect full,NativeRect clipped,IntPtr dirty);
    [DllImport("Magnification.dll")] private static extern bool MagInitialize();
    [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] private static extern bool MagSetWindowFilterList(IntPtr window,uint mode,int count,IntPtr[] excluded);
    [DllImport("Magnification.dll")] private static extern bool MagSetImageScalingCallback(IntPtr window,Filter? callback);
    [DllImport("Magnification.dll")] private static extern bool MagSetWindowSource(IntPtr window,NativeRect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extended,string type,string? title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr window,uint key,byte alpha,uint flags);
    private readonly int thread=Environment.CurrentManagedThreadId;
    private readonly Filter filter;
    private IntPtr host,child;
    private bool initialized,disposed;
    private byte[]? pixels;
    private int wantedWidth,wantedHeight;
    internal string Status{get;private set;}="尚未抓取";
    internal MagnifiedBackdrop(IntPtr excluded)
    {
        filter=Receive;
        try
        {
            if(!MagInitialize())throw new InvalidOperationException("放大镜服务不可用");initialized=true;
            host=CreateWindowEx(0x080800A0,"STATIC","Capsule backdrop",0x90000000,-32000,-32000,8,8,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            if(host==IntPtr.Zero || !SetLayeredWindowAttributes(host,0,1,2))throw new InvalidOperationException("背景宿主不可用");
            child=CreateWindowEx(0,"Magnifier",null,0x50000000,0,0,8,8,host,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            if(child==IntPtr.Zero || !MagSetWindowFilterList(child,0,2,[excluded,host]) || !MagSetImageScalingCallback(child,filter))throw new InvalidOperationException("背景排除或回调不可用");
            Status="已初始化，等待图像";
        }
        catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception){Status=error.Message;Dispose();}
    }
    internal byte[]? Capture(int left,int top,int width,int height)
    {
        VerifyThread();if(disposed)return null;
        if(width<1 || height<1 || width>4096 || height>4096 || (long)width*height>4_000_000)throw new ArgumentException("背景区域过大");
        var desktop=System.Windows.Forms.SystemInformation.VirtualScreen;
        var clippedLeft=Math.Clamp(left,desktop.Left,desktop.Right-1);var clippedTop=Math.Clamp(top,desktop.Top,desktop.Bottom-1);
        var clippedRight=Math.Clamp(checked(left+width),clippedLeft+1,desktop.Right);var clippedBottom=Math.Clamp(checked(top+height),clippedTop+1,desktop.Bottom);
        wantedWidth=clippedRight-clippedLeft;wantedHeight=clippedBottom-clippedTop;pixels=null;
        SetWindowPos(child,IntPtr.Zero,0,0,wantedWidth,wantedHeight,0x0014);
        var rect=new NativeRect{Left=clippedLeft,Top=clippedTop,Right=clippedRight,Bottom=clippedBottom};
        MagSetWindowSource(child,rect);
        if(pixels==null)
        {
            // Some drivers don't composite an off-screen magnifier. The tiny
            // alpha host never activates or receives input and excludes itself.
            SetWindowPos(host,IntPtr.Zero,clippedLeft,clippedTop,wantedWidth,wantedHeight,0x0014);
            MagSetWindowSource(child,rect);
        }
        Status=pixels==null ? "当前显示环境未提供背景图像" : "背景抓取正常";
        if(pixels==null || wantedWidth==width && wantedHeight==height)return pixels;
        var padded=new byte[checked(width*height*4)];
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var sx=Math.Clamp(left+x-clippedLeft,0,wantedWidth-1);var sy=Math.Clamp(top+y-clippedTop,0,wantedHeight-1);
            Buffer.BlockCopy(pixels,(sy*wantedWidth+sx)*4,padded,(y*width+x)*4,4);
        }
        return padded;
    }
    private bool Receive(IntPtr window,IntPtr source,Header header,IntPtr destination,Header output,NativeRect full,NativeRect clipped,IntPtr dirty)
    {
        try
        {
            if(disposed || source==IntPtr.Zero || header.Width!=wantedWidth || header.Height!=wantedHeight || header.Stride<(long)wantedWidth*4 || header.Offset>header.Bytes || (ulong)header.Offset+(ulong)(wantedHeight-1)*header.Stride+(ulong)wantedWidth*4>(ulong)header.Bytes)return false;
            var frame=new byte[checked(wantedWidth*wantedHeight*4)];
            for(var row=0;row<wantedHeight;row++)Marshal.Copy(source+checked((int)(header.Offset+row*(long)header.Stride)),frame,row*wantedWidth*4,wantedWidth*4);
            for(var i=3;i<frame.Length;i+=4)frame[i]=255;
            pixels=frame;return true;
        }
        catch(Exception error) when(error is OverflowException or ArgumentException){Status="背景图像格式无效";return false;}
    }
    private void VerifyThread(){if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("背景抓取必须在创建线程执行");}
    public void Dispose()
    {
        VerifyThread();if(disposed)return;disposed=true;
        if(child!=IntPtr.Zero){MagSetImageScalingCallback(child,null);DestroyWindow(child);child=IntPtr.Zero;}
        if(host!=IntPtr.Zero){DestroyWindow(host);host=IntPtr.Zero;}
        if(initialized){MagUninitialize();initialized=false;}pixels=null;
    }
}

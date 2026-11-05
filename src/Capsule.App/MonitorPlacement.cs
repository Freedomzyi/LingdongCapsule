using System.Runtime.InteropServices;
using Forms=System.Windows.Forms;
using Capsule.Core;

namespace Capsule.App;

internal static class MonitorPlacement
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] private struct Rect{public int Left,Top,Right,Bottom;}
    public static IReadOnlyDictionary<string,string> Options()
    {
        Dictionary<string,string> options=new(){[""]="主显示器"};
        foreach(var screen in Forms.Screen.AllScreens)options[screen.DeviceName]=screen.DeviceName+(screen.Primary ? " · 主" : "");
        return options;
    }
    public static void Center(IntPtr window,string selected,double topDip)
    {
        if(window==IntPtr.Zero || !GetWindowRect(window,out var rect))return;
        var screen=Target(selected);if(screen==null)return;
        var dpi=GetDpiForWindow(window);if(dpi==0)dpi=96;
        var work=screen.WorkingArea;var point=MonitorGeometry.Center(new(work.Left,work.Top,work.Width,work.Height),rect.Right-rect.Left,rect.Bottom-rect.Top,dpi,topDip);
        if(rect.Left!=point.X || rect.Top!=point.Y)SetWindowPos(window,IntPtr.Zero,point.X,point.Y,0,0,0x0001|0x0004|0x0010);
    }
    internal static bool ResizeAndCenter(IntPtr window,string selected,double topDip,double width,double height)
    {
        if(window==IntPtr.Zero)return false;var screen=Target(selected);if(screen==null)return false;
        var dpi=GetDpiForWindow(window);if(dpi==0)dpi=96;var work=screen.WorkingArea;
        var w=Math.Clamp((int)Math.Round(width*dpi/96),1,work.Width);var h=Math.Clamp((int)Math.Round(height*dpi/96),1,work.Height);
        var point=MonitorGeometry.Center(new(work.Left,work.Top,work.Width,work.Height),w,h,dpi,topDip);
        if(GetWindowRect(window,out var current) && current.Left==point.X && current.Top==point.Y && current.Right-current.Left==w && current.Bottom-current.Top==h)return true;
        // Deliver width, height and anchor in one native frame, avoiding a tall/narrow
        // intermediate HWND between separate WPF Width/Height property updates.
        return SetWindowPos(window,IntPtr.Zero,point.X,point.Y,w,h,0x0004|0x0010);
    }
    private static Forms.Screen? Target(string selected)
    {
        var screens=Forms.Screen.AllScreens;return screens.FirstOrDefault(item=>item.DeviceName==selected) ?? screens.FirstOrDefault(item=>item.Primary) ?? screens.FirstOrDefault();
    }
    internal static double WidthLimit(IntPtr window,string selected)
    {
        var screen=Target(selected);return screen==null ? double.PositiveInfinity : MonitorGeometry.WidthLimit(screen.WorkingArea.Width,window==IntPtr.Zero ? 96 : GetDpiForWindow(window));
    }
    internal static double VisualScale(IntPtr window,string selected,double preferred)
    {
        var screen=Target(selected);
        return screen==null ? 1 : MonitorGeometry.VisualScale(preferred,screen.WorkingArea.Width,screen.WorkingArea.Height,window==IntPtr.Zero ? 96 : GetDpiForWindow(window));
    }
}

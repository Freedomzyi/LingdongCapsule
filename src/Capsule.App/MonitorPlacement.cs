using System.Runtime.InteropServices;
using Forms=System.Windows.Forms;

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
        var screen=Forms.Screen.AllScreens.FirstOrDefault(item=>item.DeviceName==selected) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var dpi=GetDpiForWindow(window);if(dpi==0)dpi=96;
        var left=screen.WorkingArea.Left+(screen.WorkingArea.Width-(rect.Right-rect.Left))/2;
        var top=screen.WorkingArea.Top+(int)Math.Round(topDip*dpi/96);
        SetWindowPos(window,IntPtr.Zero,left,top,0,0,0x0001|0x0004|0x0010);
    }
}

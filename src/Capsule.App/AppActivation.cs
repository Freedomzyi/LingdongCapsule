using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Capsule.App;

internal static class AppActivation
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr data);
    private delegate bool EnumProc(IntPtr window,IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window,int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] private struct Rect{public int Left,Top,Right,Bottom;}

    public static bool Open(string sourceId,bool reopenHiddenPlayer=false)
    {
        if(string.IsNullOrWhiteSpace(sourceId))return false;
        try
        {
            if(sourceId.Contains('!'))
            {
                var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};start.ArgumentList.Add("shell:AppsFolder\\"+sourceId);
                Process.Start(start);return true;
            }
            var name=Path.GetFileNameWithoutExtension(sourceId);
            HashSet<uint> ids=[];
            foreach(var process in Process.GetProcesses())using(process){if(process.ProcessName.Equals(name,StringComparison.OrdinalIgnoreCase) || name.StartsWith(process.ProcessName+'.',StringComparison.OrdinalIgnoreCase))ids.Add((uint)process.Id);}
            var window=FindDesktopWindow(ids,visibleOnly:reopenHiddenPlayer);
            if(window==IntPtr.Zero)return reopenHiddenPlayer && ReopenRunningPlayer(name);
            // A minimized taskbar window stays visible in Win32, but needs a restore request.
            var needsRestore=IsIconic(window) || !IsWindowVisible(window);
            var restoreRequested=needsRestore && ShowWindowAsync(window,9);
            return SetForegroundWindow(window) || restoreRequested;
        }
        catch{return false;}
    }

    private static bool ReopenRunningPlayer(string processName)
    {
        foreach(var process in Process.GetProcessesByName(processName))
        {
            using(process)
            {
                var path=process.MainModule?.FileName;
                if(path==null || !Path.IsPathFullyQualified(path) || !Path.GetFileName(path).Equals(processName+".exe",StringComparison.OrdinalIgnoreCase) || !File.Exists(path))continue;
                // Use the running player's verified executable, never a path from media metadata.
                using var launched=Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute=true,
                    WorkingDirectory=Path.GetDirectoryName(path)!
                });
                return true;
            }
        }
        return false;
    }

    internal static IntPtr FindDesktopWindow(IReadOnlySet<uint> processIds,bool visibleOnly=false)
    {
        var best=IntPtr.Zero;
        var bestVisible=false;
        long bestArea=0;
        EnumWindows((window,_)=>
        {
            GetWindowThreadProcessId(window,out var id);
            if(!processIds.Contains(id) || !GetWindowRect(window,out var rect))return true;

            var title=new StringBuilder(256);
            GetWindowText(window,title,title.Capacity);
            if(title.Length==0)return true;

            var visible=IsWindowVisible(window);
            if(visibleOnly && !visible)return true;
            var area=(long)Math.Max(0,rect.Right-rect.Left)*Math.Max(0,rect.Bottom-rect.Top);
            if(area>0 && (best==IntPtr.Zero || visible && !bestVisible || visible==bestVisible && area>bestArea))
            {
                best=window;
                bestVisible=visible;
                bestArea=area;
            }
            return true;
        },IntPtr.Zero);
        return best;
    }
}

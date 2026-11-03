using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Capsule.Core;

namespace Capsule.App;

internal static class MediaWindowEvidence
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate bool WindowCallback(IntPtr window,IntPtr data);
    [DllImport("user32.dll")]private static extern bool EnumWindows(WindowCallback callback,IntPtr data);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(IntPtr window,StringBuilder text,int maximum);
    private static HashSet<uint> processes=[];private static long sampledAt;
    internal static bool? Matches(string source,string mediaTitle)
    {
        if(!StaleMediaSessions.Tracked(source))return null;
        var wanted=Normalize(mediaTitle);if(wanted.Length<2 || wanted.Length>2048)return null;
        try
        {
            if(Environment.TickCount64-sampledAt>3000 || sampledAt==0)
            {
                var next=new HashSet<uint>();foreach(var name in new[]{"哔哩哔哩","bilibili"})foreach(var process in Process.GetProcessesByName(name)){using(process)next.Add((uint)process.Id);}
                processes=next;sampledAt=Environment.TickCount64;
            }
            if(processes.Count==0)return false;
            var matches=false;
            var enumerated=EnumWindows((window,_)=>
            {
                if(matches || !IsWindowVisible(window))return true;
                GetWindowThreadProcessId(window,out var owner);if(!processes.Contains(owner))return true;
                var title=new StringBuilder(2048);GetWindowText(window,title,title.Capacity);if(Normalize(title.ToString()).Contains(wanted,StringComparison.Ordinal))matches=true;return true;
            },IntPtr.Zero);
            return enumerated ? matches : null;
        }
        catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){return null;}
    }
    private static string Normalize(string value)=>Regex.Replace(value,@"[^\p{L}\p{N}]","").ToLowerInvariant();
}

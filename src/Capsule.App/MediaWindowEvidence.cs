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
    internal static MediaWindowSnapshot Inspect(string source,string mediaTitle)
    {
        if(!StaleMediaSessions.Tracked(source))return new(null,null);
        var wanted=Normalize(mediaTitle);if(wanted.Length<2 || wanted.Length>2048)return new(null,null);
        try
        {
            if(Environment.TickCount64-sampledAt>3000 || sampledAt==0)
            {
                var next=new HashSet<uint>();foreach(var name in new[]{"哔哩哔哩","bilibili"})foreach(var process in Process.GetProcessesByName(name)){using(process)next.Add((uint)process.Id);}
                processes=next;sampledAt=Environment.TickCount64;
            }
            if(processes.Count==0)return new(false,null);
            var matches=false;var uncertain=false;var contentCount=0;string? content=null;
            var enumerated=EnumWindows((window,_)=>
            {
                if(!IsWindowVisible(window))return true;
                GetWindowThreadProcessId(window,out var owner);if(!processes.Contains(owner))return true;
                try{using var current=Process.GetProcessById((int)owner);if(!current.ProcessName.Equals("哔哩哔哩",StringComparison.OrdinalIgnoreCase) && !current.ProcessName.Equals("bilibili",StringComparison.OrdinalIgnoreCase))return true;}catch(ArgumentException){return true;}catch(InvalidOperationException){return true;}
                var title=new StringBuilder(2048);var length=GetWindowText(window,title,title.Capacity);var text=title.ToString();if(Normalize(text).Contains(wanted,StringComparison.Ordinal))matches=true;
                if(length>=title.Capacity-1){uncertain=true;return true;}
                if(Normalize(text).Length>=2 && !text.Contains("bilibili",StringComparison.OrdinalIgnoreCase) && !text.Contains("哔哩哔哩",StringComparison.Ordinal)){contentCount++;content=text;}
                return true;
            },IntPtr.Zero);
            if(!enumerated || uncertain || contentCount>1 && !matches)return new(matches ? true : null,null);
            return new(matches,contentCount==1 ? content : null);
        }
        catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){return new(null,null);}
    }
    private static string Normalize(string value)=>Regex.Replace(value,@"[^\p{L}\p{N}]","").ToLowerInvariant();
}

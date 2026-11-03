using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Capsule.App;

internal sealed record ProcessIdentity(int Id, DateTime Started, string Name, int Session);
internal sealed record MemoryGroup(string Name, long Bytes, IReadOnlyList<ProcessIdentity> Processes, string? IconPath);
internal sealed record OverviewSample(double? Cpu, uint? Memory, string Battery, bool LowBattery, double? Download, double? Upload, string Network);

// Independent, on-demand native sampling. Working sets include shared pages.
internal sealed class OverviewService
{
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length,Load;
        public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessEntry
    {
        public uint Size,Usage,Id;public UIntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private (long Idle,long Total)? cpu;
    private long sampledAt;
    private Dictionary<string,(long Rx,long Tx)> network=[];
    public OverviewSample Sample()
    {
        double? utilization=null,down=null,up=null;uint? memory=null;
        if(GetSystemTimes(out var idle,out var kernel,out var user))
        {
            var total=kernel+user;
            if(cpu is {} previous && total>previous.Total)utilization=Math.Clamp(100d*(total-previous.Total-(idle-previous.Idle))/(total-previous.Total),0,100);
            cpu=(idle,total);
        }
        var state=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(GlobalMemoryStatusEx(ref state))memory=state.Load;
        var now=Environment.TickCount64;var next=new Dictionary<string,(long Rx,long Tx)>();double received=0,sent=0;
        try
        {
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if(adapter.OperationalStatus!=OperationalStatus.Up || adapter.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211) || !adapter.GetIPProperties().GatewayAddresses.Any())continue;
                var counts=adapter.GetIPStatistics();next[adapter.Id]=(counts.BytesReceived,counts.BytesSent);
                if(network.TryGetValue(adapter.Id,out var old)){received+=Math.Max(0,counts.BytesReceived-old.Rx);sent+=Math.Max(0,counts.BytesSent-old.Tx);}
            }
            if(sampledAt!=0 && now-sampledAt is >=500 and <=3500){down=received*1000/(now-sampledAt);up=sent*1000/(now-sampledAt);}
        }
        catch(NetworkInformationException){ }
        network=next;sampledAt=now;
        var connectivity="未知";
        try
        {
            var profile=Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            var bars=profile?.GetSignalBars();
            connectivity=profile?.GetNetworkConnectivityLevel()==Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess && !(bars.HasValue && bars.Value<=2) ? "健康" : "欠佳";
        }
        catch(Exception){ }
        var power=System.Windows.Forms.SystemInformation.PowerStatus;
        var valid=power.BatteryLifePercent is >=0 and <=1 && power.BatteryChargeStatus is not (System.Windows.Forms.BatteryChargeStatus.NoSystemBattery or System.Windows.Forms.BatteryChargeStatus.Unknown);
        var charging=valid && power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging);
        return new(utilization,memory,valid ? $"{(charging ? "⚡ " : "")}{power.BatteryLifePercent*100:0}%" : "—",valid && !charging && power.BatteryLifePercent<=.2,down,up,connectivity);
    }
    public IReadOnlyList<MemoryGroup> ReadProcesses()
    {
        using var self=Process.GetCurrentProcess();
        var entries=new Dictionary<int,(ProcessIdentity Identity,long Bytes,string? Path)>();
        foreach(var process in Process.GetProcesses())using(process)
        {
            try
            {
                if(process.Id==0 || process.SessionId==0 || process.SessionId!=self.SessionId)continue;
                string? path=null;try{path=process.MainModule?.FileName;}catch(System.ComponentModel.Win32Exception){ }
                entries[process.Id]=(new(process.Id,process.StartTime,process.ProcessName,process.SessionId),process.WorkingSet64,path);
            }
            catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){ }
        }
        var parents=ReadParents();
        string Group(int id)
        {
            var start=entries[id].Identity;
            if(!start.Name.Equals("WeChatAppEx",StringComparison.OrdinalIgnoreCase))return start.Name;
            var visited=new HashSet<int>();var child=start;
            for(var depth=0;depth<20 && visited.Add(child.Id);depth++)
            {
                if(!parents.TryGetValue(child.Id,out var parentId) || !entries.TryGetValue(parentId,out var parent) || parent.Identity.Started>child.Started)break;
                if(parent.Identity.Name.Equals("Weixin",StringComparison.OrdinalIgnoreCase))return "Weixin";
                if(!parent.Identity.Name.Equals("WeChatAppEx",StringComparison.OrdinalIgnoreCase))break;
                child=parent.Identity;
            }
            return start.Name;
        }
        return entries.GroupBy(row=>Group(row.Key),StringComparer.OrdinalIgnoreCase).Select(group=>new MemoryGroup(group.Key,group.Sum(row=>row.Value.Bytes),group.Select(row=>row.Value.Identity).ToArray(),group.Select(row=>row.Value.Path).FirstOrDefault(path=>path!=null))).OrderByDescending(group=>group.Bytes).ThenBy(group=>group.Name).Take(3).ToArray();
    }
    private static Dictionary<int,int> ReadParents()
    {
        var parents=new Dictionary<int,int>();var snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot==new IntPtr(-1))return parents;
        try
        {
            var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>(),Name=""};
            if(Process32FirstW(snapshot,ref entry))do{parents[(int)entry.Id]=(int)entry.Parent;}while(Process32NextW(snapshot,ref entry));
        }
        finally{CloseHandle(snapshot);}
        return parents;
    }
    // Caller must present these exact identities in a confirmation dialog first.
    public static int EndConfirmed(MemoryGroup group)
    {
        using var self=Process.GetCurrentProcess();var failed=0;
        var roots=group.Processes.Where(item=>!group.Name.Equals("Weixin",StringComparison.OrdinalIgnoreCase) || item.Name.Equals("Weixin",StringComparison.OrdinalIgnoreCase));
        foreach(var identity in roots)
        {
            if(identity.Id==self.Id || identity.Session==0 || identity.Session!=self.SessionId)continue;
            try
            {
                using var process=Process.GetProcessById(identity.Id);
                if(process.SessionId!=identity.Session || process.StartTime!=identity.Started || !process.ProcessName.Equals(identity.Name,StringComparison.OrdinalIgnoreCase)){failed++;continue;}
                process.Kill(entireProcessTree:group.Name.Equals("Weixin",StringComparison.OrdinalIgnoreCase));
            }
            catch(ArgumentException){ }
            catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){failed++;}
        }
        return failed;
    }
}

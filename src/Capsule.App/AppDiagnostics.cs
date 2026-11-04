using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Capsule.Core;

namespace Capsule.App;

// Explicit local engineering diagnostics. No content, identities, settings or network data.
internal sealed class AppDiagnostics : IDisposable
{
    private readonly object gate=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMinutes(1)};
    private readonly Process process=Process.GetCurrentProcess();
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly Dictionary<string,double> scenes=new();
    private readonly DateTimeOffset started=DateTimeOffset.UtcNow;
    private long lastObservation;
    private double lastSample;
    private TimeSpan lastCpu;
    private string? scene;
    private bool closed;
    private int suspendCount,resumeCount,unhandledCount,writeErrors;
    private double unobservedSeconds;
    private int externalPlugins;
    internal string DirectoryPath{get;}
    internal AppDiagnostics(string root)
    {
        if(!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\",StringComparison.Ordinal))throw new ArgumentException("诊断目录须为本地绝对路径。");
        DirectoryPath=Path.Combine(Path.GetFullPath(root),"session-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(DirectoryPath);
        lastCpu=process.TotalProcessorTime;lastObservation=Stopwatch.GetTimestamp();
        File.WriteAllText(Path.Combine(DirectoryPath,"session.json"),JsonSerializer.Serialize(new{schemaVersion=1,pid=Environment.ProcessId,startedUtc=started,os=Environment.OSVersion.ToString(),framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,logicalProcessors=Environment.ProcessorCount,intervalSeconds=60,scope="Local aggregate diagnostics only; no message/media content, source identities, images, settings or network data"}));
        timer.Tick+=OnTick;timer.Start();Sample();
    }
    internal static AppDiagnostics? FromArguments(string[] args)
    {
        var indices=args.Select((value,index)=>(value,index)).Where(item=>item.value=="--diagnostics-dir").Select(item=>item.index).ToArray();
        if(indices.Length==0)return null;
        if(indices.Length!=1 || indices[0]+1>=args.Length)throw new ArgumentException("--diagnostics-dir 需要一个本地绝对目录。");
        return new(args[indices[0]+1]);
    }
    internal void Observe(IslandMode mode,bool hidden,bool playing,int activeExternalPlugins,bool fixtureMedia=false)
    {
        lock(gate)
        {
            if(closed)return;
            var now=Stopwatch.GetTimestamp();var elapsed=Stopwatch.GetElapsedTime(lastObservation,now).TotalSeconds;lastObservation=now;
            if(scene!=null){scenes[scene]=scenes.GetValueOrDefault(scene)+Math.Min(5,elapsed);unobservedSeconds+=Math.Max(0,elapsed-5);}
            scene=hidden ? "hidden" : mode==IslandMode.Notification ? "notification" : mode==IslandMode.Media && playing ? fixtureMedia ? "fixture-media" : "media-playing" : "idle";
            externalPlugins=Math.Max(0,activeExternalPlugins);
        }
    }
    internal void Power(string mode)
    {
        lock(gate)
        {
            if(closed)return;
            if(mode=="Suspend")suspendCount++;else if(mode=="Resume")resumeCount++;else return;
            Write("power.jsonl",new{utc=DateTimeOffset.UtcNow,mode});
        }
    }
    internal void Unhandled(Exception error)
    {
        lock(gate){if(closed)return;unhandledCount++;Write("errors.jsonl",new{utc=DateTimeOffset.UtcNow,type=error.GetType().FullName});}
    }
    private void OnTick(object? sender,EventArgs e)=>Sample();
    internal void Sample()
    {
        lock(gate)
        {
            if(closed)return;
            try
            {
                process.Refresh();var seconds=clock.Elapsed.TotalSeconds;var cpu=process.TotalProcessorTime;
                Write("samples.jsonl",new{utc=DateTimeOffset.UtcNow,elapsedSeconds=seconds,cpuPercent=seconds>lastSample ? (cpu-lastCpu).TotalSeconds/(seconds-lastSample)/Environment.ProcessorCount*100 : 0,privateMiB=process.PrivateMemorySize64/1048576d,workingMiB=process.WorkingSet64/1048576d,handles=process.HandleCount,threads=process.Threads.Count,externalPlugins,scene,sceneSeconds=scenes.ToDictionary(),unobservedSeconds,suspendCount,resumeCount,unhandledCount,writeErrors});
                lastCpu=cpu;lastSample=seconds;
            }
            catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception){writeErrors++;}
        }
    }
    private void Write(string name,object value)
    {
        try{File.AppendAllText(Path.Combine(DirectoryPath,name),JsonSerializer.Serialize(value)+Environment.NewLine);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){writeErrors++;}
    }
    internal void Finish(int exitCode)
    {
        lock(gate){if(closed)return;Sample();Write("exit.jsonl",new{utc=DateTimeOffset.UtcNow,exitCode,elapsedSeconds=clock.Elapsed.TotalSeconds,unhandledCount,writeErrors});closed=true;timer.Stop();timer.Tick-=OnTick;process.Dispose();}
    }
    public void Dispose()=>Finish(0);
}

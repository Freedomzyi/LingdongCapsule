using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Capsule.App;
using Capsule.Core;

internal static class ResourceProbe
{
    private sealed record Sample(double Seconds,double CpuPercent,double PrivateMiB,double WorkingMiB,int Threads,int Handles);
    internal static async Task RunAsync(string[] args)
    {
        var index=Array.IndexOf(args,"--resource-test");if(index+1>=args.Length)throw new ArgumentException("Provide an evidence directory");
        var directory=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(directory);
        var seconds=30;var option=Array.IndexOf(args,"--sample-seconds");if(option>=0 && (option+1>=args.Length || !int.TryParse(args[option+1],out seconds) || seconds is <10 or >300))throw new ArgumentException("Sample seconds must be 10..300");
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();
        var settings=new Settings{MediaEnabled=false,NotificationsEnabled=false,NotificationSoundEnabled=false,ThemeMode=0,MaterialKind=0};
        var window=new MainWindow(suppressOnboarding:true,initialSettings:settings,localPluginDirectory:Path.Combine(directory,"plugins"),pluginSettingsWriter:()=>{}){ShowActivated=false};
        using var process=Process.GetCurrentProcess();var summaries=new List<object>();OwnedMediaPublisher? publisher=null;
        string revision="unknown";
        try
        {
            using var git=Process.Start(new ProcessStartInfo("git","rev-parse HEAD"){UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true});if(git!=null){revision=(await git.StandardOutput.ReadToEndAsync()).Trim();await git.WaitForExitAsync();}
            window.Show();
            foreach(var scene in new[]{"idle-solid","hidden-solid","notification-burst","combined-plugin","owned-media-spectrum"})
            {
                settings.MediaEnabled=false;settings.NotificationsEnabled=false;settings.HardwareEnabled=false;settings.CompositeEnabled=false;window.LiveState.Dismiss();window.SetIslandHidden(scene=="hidden-solid");
                if(scene=="notification-burst")settings.NotificationsEnabled=true;
                if(scene=="combined-plugin")
                {
                    settings.HardwareEnabled=true;settings.CompositeEnabled=true;
                    var dll=Path.Combine(Environment.CurrentDirectory,"plugins","Capsule.CenterClock","bin","Release","net10.0-windows10.0.19041.0","Capsule.CenterClock.dll");
                    if(!File.Exists(dll))throw new FileNotFoundException("Build the example plugin first",dll);window.LoadPlugin(dll,save:false);
                }
                if(scene=="owned-media-spectrum")
                {
                    publisher=await OwnedMediaPublisher.StartAsync();settings.MediaEnabled=true;settings.LyricsEnabled=false;settings.ManualMediaSource="LingdongCapsule.Owned.bilibili";
                }
                await Task.Delay(5000);
                if(scene=="owned-media-spectrum" && (!window.LiveState.Media.Available || window.LiveState.Media.Title!=publisher!.Title))throw new Exception("Owned media fixture did not reach the measured island");
                var samples=new List<Sample>();process.Refresh();var previousCpu=process.TotalProcessorTime;var clock=Stopwatch.StartNew();var previous=clock.Elapsed.TotalSeconds;
                for(var sample=0;sample<seconds;sample++)
                {
                    if(scene=="notification-burst")window.AcceptNotification(new("owned.resource.fixture","Resource fixture","自建测试通知","仅用于资源测量，不含个人内容",(uint)(sample+1)),DateTimeOffset.Now);
                    await Task.Delay(1000);process.Refresh();var at=clock.Elapsed.TotalSeconds;var cpu=process.TotalProcessorTime;
                    samples.Add(new(at,(cpu-previousCpu).TotalSeconds/(at-previous)/Environment.ProcessorCount*100,process.PrivateMemorySize64/1048576d,process.WorkingSet64/1048576d,process.Threads.Count,process.HandleCount));previousCpu=cpu;previous=at;
                }
                var cpuValues=samples.Select(sample=>sample.CpuPercent).Order().ToArray();
                var summary=new{scene,sampleSeconds=clock.Elapsed.TotalSeconds,samples=samples.Count,cpuMeanPercent=cpuValues.Average(),cpuP95Percent=cpuValues[(int)Math.Ceiling(cpuValues.Length*.95)-1],privateMinMiB=samples.Min(sample=>sample.PrivateMiB),privateMaxMiB=samples.Max(sample=>sample.PrivateMiB),privateChangeMiB=samples[^1].PrivateMiB-samples[0].PrivateMiB,workingMaxMiB=samples.Max(sample=>sample.WorkingMiB),threadsMax=samples.Max(sample=>sample.Threads),handlesMin=samples.Min(sample=>sample.Handles),handlesMax=samples.Max(sample=>sample.Handles)};
                summaries.Add(summary);await File.WriteAllTextAsync(Path.Combine(directory,scene+".json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(summary));
            }
        }
        finally
        {
            publisher?.Dispose();window.Close();
            await File.WriteAllTextAsync(Path.Combine(directory,"summary.json"),JsonSerializer.Serialize(new{revision,completed=summaries.Count==5,utc=DateTimeOffset.UtcNow,os=Environment.OSVersion.ToString(),logicalProcessors=Environment.ProcessorCount,framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,method="Actual WPF island in test host; CPU normalized by logical processor count; 5s warmup per scene; sequential scenes, no forced GC. Idle/hidden/combined have native media and notifications disabled. Notifications scene uses existing permission without requesting; no notification text captured in evidence, permission coverage not inferred. Owned media includes silent in-process publisher overhead and previous plugin remains enabled. Lyrics off in that scene. Solid material only. Not 24h/clean-system/Owner acceptance.",summaries},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}

using Capsule.App;
using Capsule.PluginApi;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Windows.Media.Control;

internal static class TimelineTests
{
    internal static async Task RunAsync()
    {
        var context=new PluginMediaSession("cloudmusic.exe","Owned track","Owned artist",true);
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        Check(PluginTimelineClock.Eligible("KuGou.exe",TimeSpan.Zero) && PluginTimelineClock.Eligible(context.SourceId,TimeSpan.Zero) && !PluginTimelineClock.Eligible(context.SourceId,TimeSpan.FromSeconds(1)) && !PluginTimelineClock.Eligible("qqmusic.exe",TimeSpan.Zero),"Timeline eligibility stole a native or unrelated timeline");
        Check(!PluginTimelineClock.Valid(new("",TimeSpan.Zero,TimeSpan.FromSeconds(10),true)) && !PluginTimelineClock.Valid(new("x",TimeSpan.FromSeconds(-1),TimeSpan.FromSeconds(10),true)),"Malformed timeline was accepted");
        var clock=new PluginTimelineClock();var reading=new PluginTimelineReading("fixture",new("track-1",TimeSpan.FromSeconds(10),TimeSpan.FromMinutes(1),true));
        Check(clock.Read(context,reading,10000)!.Position.TotalSeconds==10 && clock.Read(context,reading,11000)!.Position.TotalSeconds==11 && clock.Read(context,reading,12000)!.Position.TotalSeconds==12,"Stable cached samples reset smooth monotonic advance");
        reading=reading with{Timeline=reading.Timeline with{Position=TimeSpan.FromSeconds(12),Playing=false}};
        clock.Read(context,reading,12000);Check(clock.Read(context,reading,14000)!.Position.TotalSeconds==12,"Paused plugin timeline advanced");
        reading=reading with{Timeline=reading.Timeline with{Playing=true}};clock.Read(context,reading,14000);
        Check(clock.Read(context,reading,16000)!.Position.TotalSeconds==14,"Resume did not reanchor");
        reading=reading with{Timeline=reading.Timeline with{Position=TimeSpan.FromSeconds(5)}};
        Check(clock.Read(context,reading,17000)!.Position.TotalSeconds==5,"Plugin seek did not reanchor");
        reading=reading with{Timeline=reading.Timeline with{TrackKey="track-2"}};
        Check(clock.Read(context,reading,18000)!.Position.TotalSeconds==5 && clock.Read(context with{Title="New native title"},reading,19000)!.Position.TotalSeconds==5,"Track/native identity changes reused a stale anchor");
        Check(clock.Read(context,null,20000)==null && clock.Read(context,reading,22000)!.Position.TotalSeconds==5,"Source loss did not reset the anchor");
        Check(clock.Read(context,reading with{Timeline=reading.Timeline with{Position=TimeSpan.MaxValue}},23000)!.Position.TotalSeconds==60,"Position was not clamped to duration");
        Console.WriteLine("PASS timeline eligibility, cached interpolation, pause/resume, seek, track identity, loss and bounds");

        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var settings=new Settings();
        settings.PluginPreferences["fixture.clock"]=new(){["timeline-mode"]="valid",["timeline-source"]=context.SourceId};
        using(var runtime=new PluginRuntime(settings,()=>new("","",false,false),(_,_,_,_)=>Task.FromResult(true),()=>{}))
        {
            var fixture=typeof(FixturePlugin).Assembly.Location;runtime.Enable(fixture);
            Check(runtime.ReadTimeline(context)?.Timeline.Position.TotalSeconds==45 && runtime.ReadTimeline(context with{SourceId="kugou.exe"})==null,"Independently loaded provider did not match its native source");
            var folder=Path.Combine(Environment.CurrentDirectory,"artifacts","timeline-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var second=Path.Combine(folder,"fixture.second.dll");File.Copy(fixture,second);
            settings.PluginPreferences["fixture.second"]=new(){["timeline-mode"]="valid",["timeline-source"]=context.SourceId,["timeline-priority"]="10"};runtime.Enable(second);
            Check(runtime.ReadTimeline(context)?.PluginId=="fixture.second","Provider priority was ignored");
            settings.PluginPreferences["fixture.second"]["timeline-mode"]="invalid";
            Check(runtime.ReadTimeline(context)?.PluginId=="fixture.clock","Invalid high-priority data hid a valid provider");
            settings.PluginPreferences["fixture.second"]["timeline-mode"]="throw";
            Check(runtime.ReadTimeline(context)?.PluginId=="fixture.clock" && !runtime.ActiveIds.Contains("fixture.second") && runtime.Errors.ContainsKey("fixture.second"),"Provider exception was not isolated");
            runtime.Disable("fixture.clock");Check(runtime.ReadTimeline(context)==null,"Stopped provider retained a timeline");
        }
        Console.WriteLine("PASS independent plugin API, target matching, priority, invalid data, fault isolation and disable");

        using var publisher=await OwnedMediaPublisher.StartAsync("LingdongCapsule.Owned.cloudmusic");
        var manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();GlobalSystemMediaTransportControlsSession? own=null;
        for(var attempt=0;attempt<40 && own==null;attempt++)
        {foreach(var candidate in manager.GetSessions())if((await candidate.TryGetMediaPropertiesAsync()).Title==publisher.Title){own=candidate;break;}if(own==null)await Task.Delay(100);}
        if(own==null)throw new Exception("Owned cloudmusic-like SMTC fixture did not register");
        publisher.ClearTimeline();await Task.Delay(150);
        using(var media=new MediaService())
        {
            var calls=0;PluginTimelineReading? value=new("fixture",new("owned",TimeSpan.FromSeconds(20),TimeSpan.FromMinutes(3),true));
            media.TimelineReader=_=>{calls++;return value;};media.Configure(true,"other",own.SourceAppUserModelId);
            var native=await media.ReadAsync();
            Check(native.Title==publisher.Title && native.Duration==TimeSpan.Zero && media.Duration.TotalSeconds==180 && media.Position.TotalSeconds is >=20 and <22 && !media.CanSeek,"External timeline did not reach native adapter or replaced metadata identity");
            value=value with{Timeline=value.Timeline with{Position=TimeSpan.FromSeconds(40),Playing=false}};
            var unchanged=await media.ReadAsync();var paused=media.Position;await Task.Delay(120);
            Check(native==unchanged && media.Position==paused && paused.TotalSeconds==40 && calls>=2,"Plugin timeline was throttled, advanced while paused or changed metadata");
            value=null;await media.ReadAsync();Check(media.Duration==TimeSpan.Zero,"Lost provider retained external duration");
            media.TimelineReader=_=>throw new IOException("Owned injected provider failure");
            Check((await media.ReadAsync()).Available && media.Duration==TimeSpan.Zero,"Provider failure disconnected native metadata");
            publisher.SetPosition(TimeSpan.FromSeconds(4));await Task.Delay(150);media.Invalidate();calls=0;
            media.TimelineReader=_=>{calls++;return new("fixture",new("wrong",TimeSpan.Zero,TimeSpan.FromMinutes(5),true));};
            await media.ReadAsync();Check(media.Duration.TotalSeconds==30 && media.CanSeek && calls==0,"Plugin overrode a valid native timeline");
            media.Configure(false,"other",own.SourceAppUserModelId);Check(media.Duration==TimeSpan.Zero && media.Position==TimeSpan.Zero && !media.CanSeek,"Disable retained external state");
        }
        publisher.ClearTimeline();await Task.Delay(150);
        var islandSettings=new Settings{ManualMediaSource=own.SourceAppUserModelId,NotificationsEnabled=false,LyricsEnabled=false,OnlineCoversEnabled=false,ShowSpectrum=false,ClockEnabled=false,HardwareEnabled=false,OverviewEnabled=false};
        islandSettings.PluginPreferences["fixture.clock"]=new(){["timeline-mode"]="valid",["timeline-source"]=own.SourceAppUserModelId};
        var island=new MainWindow(suppressOnboarding:true,initialSettings:islandSettings,pluginSettingsWriter:()=>{}){ShowActivated=false};
        try
        {
            island.LoadPlugin(typeof(FixturePlugin).Assembly.Location,save:false);island.Show();
            var refresh=typeof(MainWindow).GetMethod("RefreshAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            for(var attempt=0;attempt<30 && !island.LiveState.Media.Available;attempt++)await Task.Delay(100);
            await (Task)refresh.Invoke(island,null)!;
            typeof(MainWindow).GetField("controlsOpen",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(island,true);
            await (Task)refresh.Invoke(island,null)!;await Task.Delay(150);
            Check(((TextBlock)island.FindName("Total")).Text=="3:00" && !((Slider)island.FindName("SeekBar")).IsEnabled && island.LiveState.Media.Duration==TimeSpan.Zero,"Actual expanded island did not show plugin duration without false native seek support");
            island.DisablePlugin("fixture.clock");await (Task)refresh.Invoke(island,null)!;
            Check(((FrameworkElement)island.FindName("TimelineSection")).Visibility==Visibility.Collapsed && island.LiveState.Media.Title==publisher.Title,"Stopping provider retained UI timeline or removed native metadata");
        }
        finally{island.Close();}
        Console.WriteLine("PASS owned native missing/valid timeline precedence, metadata stability, fault fallback and actual expanded island provider removal");
    }
}

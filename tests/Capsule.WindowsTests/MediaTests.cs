using Capsule.App;
using Windows.Media.Control;
using System.IO;

internal static class MediaTests
{
    internal static async Task RunAsync()
    {
        using var publisher=await OwnedMediaPublisher.StartAsync();var manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();GlobalSystemMediaTransportControlsSession? own=null;
        for(var attempt=0;attempt<40 && own==null;attempt++)
        {
            foreach(var candidate in manager.GetSessions())if((await candidate.TryGetMediaPropertiesAsync()).Title==publisher.Title){own=candidate;break;}
            if(own==null)await Task.Delay(100);
        }
        if(own==null)throw new Exception("Owned SMTC publisher did not register a system session");
        Console.WriteLine("Owned session is tracked by whitelist: "+Capsule.Core.StaleMediaSessions.Tracked(own.SourceAppUserModelId));
        using var media=new MediaService();media.Configure(true,"other",own.SourceAppUserModelId);
        var snapshot=await media.ReadAsync();
        if(!snapshot.Available || snapshot.Title!=publisher.Title || !snapshot.Playing || media.Duration!=TimeSpan.FromSeconds(30))throw new Exception("Owned native media session did not reach adapter");
        if(!await media.ControlAsync("pause"))throw new Exception("Owned native pause failed");
        if((await media.ReadAsync()).Playing)throw new Exception("Owned pause state did not reach adapter");
        if(!await media.ControlAsync("play"))throw new Exception("Owned native play failed");
        if(!await media.SeekAsync(.5,publisher.Title))throw new Exception("Owned native seek failed");
        for(var attempt=0;attempt<30;attempt++){await Task.Delay(100);await media.ReadAsync();if(Math.Abs(media.Position.TotalSeconds-15)<2)break;}
        if(Math.Abs(media.Position.TotalSeconds-15)>2)throw new Exception("Owned native seek timeline mismatch: "+media.Position.TotalSeconds);
        Console.WriteLine("PASS Owned silent SMTC publisher, real metadata/timeline, explicit pause/play and seek; no user playlist or master volume changed");
        publisher.SetMetadata("Capsule owned video",true);media.Invalidate();
        for(var attempt=0;attempt<30;attempt++){await Task.Delay(100);snapshot=await media.ReadAsync();if(snapshot.Title==publisher.Title && snapshot.IsVideo)break;}
        if(!snapshot.IsVideo || snapshot.Artist.Length!=0)throw new Exception("Native video metadata was classified as lyric-capable music");
        publisher.SetMetadata("Capsule owned music restored",false);media.Invalidate();
        for(var attempt=0;attempt<30;attempt++){await Task.Delay(100);snapshot=await media.ReadAsync();if(snapshot.Title==publisher.Title && !snapshot.IsVideo)break;}
        if(snapshot.IsVideo || snapshot.Artist.Length==0)throw new Exception("Native music classification did not recover");
        media.Configure(false,"other",own.SourceAppUserModelId);
        if(media.Duration!=TimeSpan.Zero || media.Position!=TimeSpan.Zero || media.SourceId.Length!=0 || media.Cover!=null || await media.ControlAsync("play"))throw new Exception("Disabling media retained stale metadata, timeline or control access");
        media.Configure(true,"other",own.SourceAppUserModelId);if(!(await media.ReadAsync()).Available)throw new Exception("Media did not reconnect after disabling");media.Dispose();
        if((await media.ReadAsync()).Available || media.Duration!=TimeSpan.Zero || media.Position!=TimeSpan.Zero)throw new Exception("Disposed media reconnected or retained position");
        var fail=false;
        using(var fault=new MediaService(()=>Task.FromResult(manager),target=>fail ? Task.FromException<GlobalSystemMediaTransportControlsSessionMediaProperties>(new IOException("Owned injected read fault")) : target.TryGetMediaPropertiesAsync().AsTask(),(_,_)=>true))
        {
            fault.Configure(true,"other",own.SourceAppUserModelId);if(!(await fault.ReadAsync()).Available)throw new Exception("Fault fixture did not read owned session");
            fail=true;fault.Invalidate();if((await fault.ReadAsync()).Available || fault.Duration!=TimeSpan.Zero || fault.Position!=TimeSpan.Zero || fault.SourceId.Length!=0)throw new Exception("Read failure retained a playing snapshot or timeline");
            fail=false;if(!(await fault.ReadAsync()).Available)throw new Exception("Media read did not recover after source fault");
        }
        var actualProperties=await own.TryGetMediaPropertiesAsync();var started=new TaskCompletionSource();var late=new TaskCompletionSource<GlobalSystemMediaTransportControlsSessionMediaProperties>();
        using(var race=new MediaService(()=>Task.FromResult(manager),_=>{started.TrySetResult();return late.Task;},(_,_)=>true))
        {
            race.Configure(true,"other",own.SourceAppUserModelId);var pending=race.ReadAsync();await started.Task.WaitAsync(TimeSpan.FromSeconds(2));race.Configure(false,"other",own.SourceAppUserModelId);
            if((await pending.WaitAsync(TimeSpan.FromSeconds(2))).Available)throw new Exception("Disabled read committed an in-flight source");
            late.TrySetResult(actualProperties);await Task.Delay(100);
            if(race.Duration!=TimeSpan.Zero || race.Position!=TimeSpan.Zero || (await race.ReadAsync()).Available)throw new Exception("Late native completion restored disabled media");
            race.Configure(true,"other",own.SourceAppUserModelId);if(!(await race.ReadAsync()).Available)throw new Exception("Generation cancellation prevented reconnect");
        }
        if(!Capsule.Core.StaleMediaSessions.Tracked(own.SourceAppUserModelId))throw new Exception("Owned native fixture did not advertise the tracked AUMID");
        using(var stale=new MediaService(()=>Task.FromResult(manager),null,(_,_)=>false))
        {
            stale.Configure(true,"other",own.SourceAppUserModelId);await stale.ReadAsync();publisher.SetPlaying(false);Capsule.Core.MediaSnapshot first=new("","",true,true);
            for(var attempt=0;attempt<30 && first.Playing;attempt++){await Task.Delay(50);first=await stale.ReadAsync();}
            if(!first.Available || first.Playing || stale.PollMilliseconds!=250)throw new Exception("Tracked paused session did not enter confirmation polling");
            await Task.Delay(300);if((await stale.ReadAsync()).Available || stale.Duration!=TimeSpan.Zero || stale.PollMilliseconds!=1000 || await stale.ControlAsync("play"))throw new Exception("Confirmed stale session retained UI data or control access");
            publisher.SetPlaying(true);var restored=false;
            for(var attempt=0;attempt<30;attempt++){await Task.Delay(100);var current=await stale.ReadAsync();if(current.Available && current.Playing){restored=true;break;}}
            if(!restored)throw new Exception("Playing evidence did not recover ignored owned session");
        }
        Console.WriteLine("PASS Native music/video, disable/dispose reset, injected read fault recovery, late-result rejection and tracked stale/recovery with owned SMTC plus injected window evidence");
    }
}

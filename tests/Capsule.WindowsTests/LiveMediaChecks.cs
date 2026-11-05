using Capsule.App;
using Capsule.Core;
using System.IO;
using System.Reflection;
using System.Text.Json;

internal static class LiveMediaChecks
{
    internal static async Task RunAsync(string[] args)
    {
        var index=Array.IndexOf(args,"--media-readonly-check");
        if(index+1>=args.Length || args[index+1].StartsWith("--"))throw new ArgumentException("Provide an evidence directory");
        var root=Path.GetFullPath(args[index+1]);Directory.CreateDirectory(root);
        using var media=new MediaService();var initial=await media.ReadAsync();
        for(var attempt=0;attempt<12 && !initial.Available;attempt++){await Task.Delay(250);initial=await media.ReadAsync();}
        var kind=initial.SourceId.Contains("qqmusic",StringComparison.OrdinalIgnoreCase) ? "QQMusic" : initial.SourceId.Contains("cloudmusic",StringComparison.OrdinalIgnoreCase) ? "NetEase" : initial.SourceId.Contains("kugou",StringComparison.OrdinalIgnoreCase) ? "Kugou" : initial.Available ? "Other" : "None";
        var lyrics=LyricsDocument.Empty;var lookup="not-applicable";
        if(initial.Available && !initial.IsVideo && initial.Artist.Length>0)
        {
            lookup="attempted";using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(30));using var client=new LyricsClient();
            try{lyrics=await client.FetchAsync(initial,cancel.Token,enrich:true);lookup=lyrics.Lines.Count>0 ? "matched" : "no-match";}
            catch(OperationCanceledException){lookup="timeout";}
        }
        await Task.Delay(500);var after=await media.ReadAsync();
        var result=new
        {
            utc=DateTimeOffset.UtcNow,
            applicationBuild=typeof(MediaService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            scope="Read-only actual current system session and actual-track lyric lookup; no play/pause/seek/volume commands or profile writes. No title, artist, lyric text or image bytes stored. Not playback synchronization, Owner interaction or 24h acceptance.",
            available=initial.Available,sourceKind=kind,playing=initial.Playing,video=initial.IsVideo,
            titlePresent=initial.Title.Length>0,artistPresent=initial.Artist.Length>0,nativeDurationPresent=initial.Duration>TimeSpan.Zero,
            nativeCoverPresent=media.Cover!=null,nativeCoverFrozen=media.Cover?.IsFrozen,
            lyricLookup=lookup,lyricSource=lyrics.Source,lyricLines=lyrics.Lines.Count,wordTiming=lyrics.HasWordTiming,translationPresent=lyrics.Lines.Any(line=>line.Translation.Length>0),
            lyricTimingWithinKnownDuration=initial.Duration>TimeSpan.Zero && lyrics.Lines.Count>0 ? (bool?)lyrics.Lines.All(line=>line.StartMs<=initial.Duration.TotalMilliseconds+10000) : null,
            sameTrackAtEnd=initial.SameTrackAs(after),playbackStateAtEnd=after.Playing
        };
        var json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});
        await File.WriteAllTextAsync(Path.Combine(root,"result.json"),json);Console.WriteLine(json);
    }
}
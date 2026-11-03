using System.Text.RegularExpressions;

namespace Capsule.Core;

public static class MediaSessionPolicy
{
    public static bool Matches(string source,string kind,string selected="")
    {
        if(selected.Length>0)return source.Equals(selected,StringComparison.OrdinalIgnoreCase);
        var id=Regex.Replace(source.ToLowerInvariant(),"[^a-z0-9]","");
        return kind switch{"other"=>true,"netease"=>id.Contains("cloudmusic") || id.Contains("netease"),"browser"=>new[]{"chrome","msedge","microsoftedge","firefox","brave","opera","vivaldi","qqbrowser"}.Any(id.Contains),_=>id.Contains(Regex.Replace(kind.ToLowerInvariant(),"[^a-z0-9]",""))};
    }
}

public sealed class StaleMediaSessions
{
    private sealed record Sample(string Title,int Misses,long At);
    private readonly Dictionary<string,Sample> samples=new(StringComparer.OrdinalIgnoreCase);
    public static bool Tracked(string id)=>id.Contains("bilibili",StringComparison.OrdinalIgnoreCase);
    public bool IsStale(string id,string title,bool playing,bool? matchingWindow,long now)
    {
        if(!Tracked(id) || playing || matchingWindow!=false || Regex.Replace(title,@"[^\p{L}\p{N}]","").Length<2){samples.Remove(id);return false;}
        if(!samples.TryGetValue(id,out var sample) || sample.Title!=title){samples[id]=new(title,1,now);return false;}
        if(now-sample.At<250)return sample.Misses>=2;
        samples[id]=sample with{Misses=Math.Min(2,sample.Misses+1),At=now};return samples[id].Misses>=2;
    }
    public void Clear()=>samples.Clear();
}

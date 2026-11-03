using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Capsule.Core;

namespace Capsule.App;

public sealed partial class LyricsClient
{
    private static bool ValidMid(string value)=>Regex.IsMatch(value,@"^[A-Za-z0-9]{1,64}$");
    internal async Task<LyricsDocument> QqOfficialAsync(MediaSnapshot track,string mid,CancellationToken cancellation)
    {
        if(!ValidMid(mid))
        {
            var query=Uri.EscapeDataString(track.Title+" "+track.Artist);
            using var search=await GetAsync("https://c.y.qq.com/splcloud/fcgi-bin/smartbox_new.fcg?key="+query+"&format=json&utf8=1",cancellation);
            if(!search.RootElement.TryGetProperty("data",out var data) || !data.TryGetProperty("song",out var songs) || !songs.TryGetProperty("itemlist",out var items) || items.ValueKind!=JsonValueKind.Array)return LyricsDocument.Empty;
            var wanted=Normalize(track.Title);var artists=Regex.Split(track.Artist,@"[/、&,，;；|]").Select(Normalize).Where(item=>item.Length>0).ToArray();
            if(wanted.Length==0 || artists.Length==0)return LyricsDocument.Empty;
            mid=items.EnumerateArray().Where(item=>Normalize(Text(item,"name"))==wanted && artists.Any(Normalize(Text(item,"singer")).Contains)).Select(item=>Text(item,"mid")).FirstOrDefault(ValidMid) ?? "";
            if(!ValidMid(mid))return LyricsDocument.Empty;
            using var detail=await GetAsync("https://c.y.qq.com/v8/fcg-bin/fcg_play_single_song.fcg?songmid="+Uri.EscapeDataString(mid)+"&platform=yqq&format=json",cancellation);
            if(!detail.RootElement.TryGetProperty("data",out var details) || details.ValueKind!=JsonValueKind.Array || details.GetArrayLength()==0)return LyricsDocument.Empty;
            var song=details[0];
            if(Normalize(Text(song,"name"))!=wanted && Normalize(Text(song,"title"))!=wanted)return LyricsDocument.Empty;
            if(song.TryGetProperty("singer",out var singers) && singers.ValueKind==JsonValueKind.Array && singers.GetArrayLength()>0 && !artists.Any(string.Join("/",singers.EnumerateArray().Select(item=>Normalize(Text(item,"name")))).Contains))return LyricsDocument.Empty;
            if(song.TryGetProperty("interval",out var interval) && interval.ValueKind==JsonValueKind.Number && interval.TryGetDouble(out var seconds) && track.Duration>TimeSpan.Zero && Math.Abs(seconds-track.Duration.TotalSeconds)>4)return LyricsDocument.Empty;
        }
        using var lyric=await GetAsync("https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid="+Uri.EscapeDataString(mid)+"&format=json&nobase64=1",cancellation);
        if(lyric.RootElement.TryGetProperty("code",out var code) && code.ValueKind==JsonValueKind.Number && code.GetInt32()!=0)return LyricsDocument.Empty;
        return LyricsDocument.Parse(DecodeQq(Text(lyric.RootElement,"lyric")),DecodeQq(Text(lyric.RootElement,"trans")),source:"QQ 音乐");
    }
    private static string DecodeQq(string value)
    {
        if(value.Length==0)return "";
        if(!value.Contains('['))try{value=Encoding.UTF8.GetString(Convert.FromBase64String(value));}catch(FormatException){ }
        return System.Net.WebUtility.HtmlDecode(value).Replace("\\r","\r").Replace("\\n","\n");
    }
}

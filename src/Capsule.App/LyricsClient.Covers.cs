using System.Text.Json;
using System.Text.RegularExpressions;
using Capsule.Core;

namespace Capsule.App;

public sealed partial class LyricsClient
{
    internal async Task<string> CoverUrlAsync(string provider,MediaSnapshot track,CancellationToken token)
    {
        var query=Uri.EscapeDataString(track.Title+" "+track.Artist);
        if(provider is "tencent" or "netease")
        {
            using var search=await GetAsync(provider=="tencent" ? "https://api.vkeys.cn/v2/music/tencent/search/song?word="+query : "https://api.vkeys.cn/v2/music/netease?word="+query,token);
            if(search.RootElement.TryGetProperty("data",out var list) && Select(list,track,false) is {} selected)return Text(selected,"cover");
            return "";
        }
        if(provider=="netease-official")
        {
            using var request=new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,"https://music.163.com/api/search/get/web"){Content=new System.Net.Http.FormUrlEncodedContent(new Dictionary<string,string>{["s"]=track.Title+" "+track.Artist,["type"]="1",["limit"]="20"})};request.Headers.Referrer=new Uri("https://music.163.com/");
            using var search=await SendJsonAsync(request,token);
            if(!search.RootElement.TryGetProperty("result",out var result) || !result.TryGetProperty("songs",out var songs) || Select(songs,track,true) is not {} song)return "";
            using var detail=await GetAsync("https://music.163.com/api/song/detail?ids="+Uri.EscapeDataString("["+song.GetProperty("id")+"]"),token);
            if(detail.RootElement.TryGetProperty("songs",out var details) && details.ValueKind==JsonValueKind.Array && details.GetArrayLength()>0 && details[0].TryGetProperty("id",out var identity) && identity.ToString()==song.GetProperty("id").ToString() && details[0].TryGetProperty("album",out var album))return Text(album,"picUrl");
            return "";
        }
        using var qq=await GetAsync("https://c.y.qq.com/splcloud/fcgi-bin/smartbox_new.fcg?key="+query+"&format=json&utf8=1",token);
        if(!qq.RootElement.TryGetProperty("data",out var data) || !data.TryGetProperty("song",out var box) || !box.TryGetProperty("itemlist",out var items) || items.ValueKind!=JsonValueKind.Array)return "";
        var artists=Regex.Split(track.Artist,@"[/、&,，;；|]").Select(Normalize).Where(value=>value.Length>0).ToArray();
        var mid=items.EnumerateArray().Where(item=>Normalize(Text(item,"name"))==Normalize(track.Title) && artists.Any(Normalize(Text(item,"singer")).Contains)).Select(item=>Text(item,"mid")).FirstOrDefault(ValidMid);
        if(mid==null)return "";
        using var qqDetail=await GetAsync("https://c.y.qq.com/v8/fcg-bin/fcg_play_single_song.fcg?songmid="+Uri.EscapeDataString(mid)+"&platform=yqq&format=json",token);
        if(!qqDetail.RootElement.TryGetProperty("data",out var detailList) || detailList.ValueKind!=JsonValueKind.Array || detailList.GetArrayLength()==0)return "";
        var match=detailList[0];if(Normalize(Text(match,"name"))!=Normalize(track.Title) && Normalize(Text(match,"title"))!=Normalize(track.Title))return "";
        if(match.TryGetProperty("singer",out var singers) && singers.ValueKind==JsonValueKind.Array && !artists.Any(string.Join("/",singers.EnumerateArray().Select(item=>Normalize(Text(item,"name")))).Contains))return "";
        if(match.TryGetProperty("interval",out var length) && length.TryGetDouble(out var seconds) && track.Duration>TimeSpan.Zero && Math.Abs(seconds-track.Duration.TotalSeconds)>4)return "";
        if(!match.TryGetProperty("album",out var qqAlbum))return "";
        var albumMid=Text(qqAlbum,"mid");return ValidMid(albumMid) ? "https://y.gtimg.cn/music/photo_new/T002R300x300M000"+albumMid+".jpg" : "";
    }
}

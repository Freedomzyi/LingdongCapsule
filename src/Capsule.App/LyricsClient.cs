using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Capsule.Core;

namespace Capsule.App;

public sealed partial class LyricsClient : IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    public LyricsClient(HttpClient? client=null){ownsHttp=client==null;http=client ?? new HttpClient{Timeout=TimeSpan.FromSeconds(5)};}
    public void Dispose(){if(ownsHttp)http.Dispose();}
    public string Status { get; private set; } = "等待音乐";

    public async Task<LyricsDocument> FetchAsync(MediaSnapshot track, CancellationToken cancellation,bool enrich=false,Action<LyricsDocument>? progress=null)
    {
        if (!track.Available || track.IsVideo || string.IsNullOrWhiteSpace(track.Artist) || track.Title == "未知曲目" || Normalize(track.Title).Length==0 || Normalize(track.Artist).Length==0){Status="等待音乐";return LyricsDocument.Empty;}
        Status = "正在获取歌词";
        var netease=track.SourceId.Contains("netease",StringComparison.OrdinalIgnoreCase) || track.SourceId.Contains("cloudmusic",StringComparison.OrdinalIgnoreCase);
        var order=netease ? new[]{"netease","netease-official","tencent","qq-official","lrclib"} : new[]{"tencent","qq-official","netease","netease-official","lrclib"};
        var qqMid="";
        var result=LyricsDocument.Empty;
        using var enrichment=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        foreach (var provider in order)
        {
            cancellation.ThrowIfCancellationRequested();
            if(result.Lines.Count>0 && (provider=="lrclib" || enrichment.IsCancellationRequested))break;
            var token=result.Lines.Count>0 ? enrichment.Token : cancellation;
            try
            {
                var document = provider switch
                {
                    "tencent" or "netease" => await RelayAsync(provider,track,token,mid=>qqMid=mid),
                    "qq-official"=>await QqOfficialAsync(track,qqMid,token),
                    "netease-official" => await NeteaseAsync(track, token),
                    _ => await LrclibAsync(track, token)
                };
                if (document.Lines.Any(line=>!string.IsNullOrWhiteSpace(line.Text)))
                {
                    if(result.Lines.Count==0){result=document;enrichment.CancelAfter(TimeSpan.FromSeconds(6));progress?.Invoke(result);}
                    else{var merged=result.Supplement(document);if(!ReferenceEquals(merged,result)){result=merged;progress?.Invoke(result);}}
                    if(!enrich || result.HasWordTiming && result.Lines.Any(line=>line.Translation.Length>0))break;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (enrichment.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or FormatException or System.IO.IOException or System.IO.InvalidDataException or KeyNotFoundException)
            { Status = "歌词源暂不可用，正在尝试其他来源"; }
        }
        cancellation.ThrowIfCancellationRequested();
        Status = result.Lines.Count>0 ? "歌词已连接 · "+result.Source : "未找到匹配歌词";
        return result;
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        if(request.RequestUri!.Host=="c.y.qq.com")request.Headers.Referrer=new Uri("https://y.qq.com/");
        if(request.RequestUri.Host=="music.163.com")request.Headers.Referrer=new Uri("https://music.163.com/");
        return await SendJsonAsync(request,cancellation);
    }
    private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request,CancellationToken cancellation)
    {
        request.Headers.UserAgent.ParseAdd("LingdongCapsule/0.1 (+https://github.com/Freedomzyi/LingdongCapsule)");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>2*1024*1024)throw new System.IO.InvalidDataException("歌词响应体积超限。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer=new System.IO.MemoryStream();var block=new byte[8192];int count;
        while((count=await stream.ReadAsync(block,cancellation))>0){if(buffer.Length+count>2*1024*1024)throw new System.IO.InvalidDataException("歌词响应实际体积超限。");buffer.Write(block,0,count);}
        return JsonDocument.Parse(buffer.ToArray());
    }

    private async Task<LyricsDocument> RelayAsync(string provider, MediaSnapshot track, CancellationToken cancellation,Action<string> rememberMid)
    {
        var query = Uri.EscapeDataString(track.Title + " " + track.Artist);
        var search = provider == "tencent" ? $"https://api.vkeys.cn/v2/music/tencent/search/song?word={query}" : $"https://api.vkeys.cn/v2/music/netease?word={query}";
        using var results = await GetAsync(search, cancellation);
        if (!results.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return LyricsDocument.Empty;
        var selected = Select(data, track, false);
        if (selected is null) return LyricsDocument.Empty;
        var id = selected.Value.GetProperty("id").ToString();
        if(provider=="tencent" && ValidMid(Text(selected.Value,"mid")))rememberMid(Text(selected.Value,"mid"));
        using var lyric = await GetAsync($"https://api.vkeys.cn/v2/music/{provider}/lyric?id={Uri.EscapeDataString(id)}", cancellation);
        if (!lyric.RootElement.TryGetProperty("data", out var payload) || payload.ValueKind != JsonValueKind.Object) return LyricsDocument.Empty;
        return LyricsDocument.Parse(Text(payload, "lrc") is { Length: > 0 } lrc ? lrc : Text(payload, "rc"), Text(payload, "trans"), Text(payload, "yrc"), provider == "tencent" ? "落月 QQ" : "落月网易云");
    }

    private async Task<LyricsDocument> NeteaseAsync(MediaSnapshot track, CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://music.163.com/api/search/get/web"){Content=new FormUrlEncodedContent(new Dictionary<string,string>{["s"]=track.Title+" "+track.Artist,["type"]="1",["limit"]="20"})};request.Headers.Referrer=new Uri("https://music.163.com/");
        using var json=await SendJsonAsync(request,cancellation);
        if (!json.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var list)) return LyricsDocument.Empty;
        var selected = Select(list, track, true);
        if (selected is null) return LyricsDocument.Empty;
        using var lyric = await GetAsync($"https://music.163.com/api/song/lyric?id={selected.Value.GetProperty("id")}&lv=-1&kv=-1&tv=-1", cancellation);
        var root = lyric.RootElement;
        return LyricsDocument.Parse(root.TryGetProperty("lrc", out var original) ? Text(original, "lyric") : "", root.TryGetProperty("tlyric", out var translated) ? Text(translated, "lyric") : "", source: "网易云");
    }

    private async Task<LyricsDocument> LrclibAsync(MediaSnapshot track, CancellationToken cancellation)
    {
        if (track.Duration <= TimeSpan.Zero) return LyricsDocument.Empty;
        var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(track.Title) + "&artist_name=" + Uri.EscapeDataString(track.Artist) + "&album_name=" + Uri.EscapeDataString(track.Album) + "&duration=" + Math.Round(track.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        using var result = await GetAsync(url, cancellation);
        return LyricsDocument.Parse(Text(result.RootElement, "syncedLyrics"), source: "LRCLIB");
    }

    private static JsonElement? Select(JsonElement list, MediaSnapshot track, bool official)
    {
        if(list.ValueKind!=JsonValueKind.Array)return null;
        var target = Normalize(track.Title);
        var artists = Regex.Split(track.Artist, @"[/、&,，;；|]").Select(Normalize).Where(name => name.Length > 0).ToArray();
        if(artists.Length==0 || target.Length==0)return null;
        JsonElement? best = null;
        var bestScore = 0;
        foreach (var song in list.EnumerateArray())
        {
            if(song.ValueKind!=JsonValueKind.Object)continue;
            if(!song.TryGetProperty("id",out var identifier) || identifier.ValueKind is not(JsonValueKind.String or JsonValueKind.Number) || identifier.ToString().Length is <1 or >80)continue;
            var name = Text(song, official ? "name" : "song");
            var full = name + Text(song, "subtitle");
            var score = Normalize(name) == target || Normalize(full) == target ? 100 : 0;
            if (score == 0) continue;
            var singer = Text(song, "singer");
            if (official && song.TryGetProperty("artists", out var singers) && singers.ValueKind==JsonValueKind.Array) singer = string.Join("/", singers.EnumerateArray().Select(item => Text(item, "name")));
            var normalizedSinger = Normalize(singer);
            if (artists.Length > 0 && !artists.Any(normalizedSinger.Contains)) continue;
            var duration = official && song.TryGetProperty("duration", out var milliseconds) && milliseconds.ValueKind==JsonValueKind.Number && milliseconds.TryGetDouble(out var ms) ? ms/1000 : Duration(Text(song, "interval"));
            if (duration > 0 && track.Duration > TimeSpan.Zero && Math.Abs(duration - track.Duration.TotalSeconds) > 4) continue;
            score += artists.Count(normalizedSinger.Contains) * 10;
            if (score > bestScore) { bestScore = score; best = song; }
        }
        return best;
    }

    private static double Duration(string value)
    {
        var parts = value.Split(':');
        if (parts.Length == 2 && double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var minutes) && double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds)) return minutes * 60 + seconds;
        var chinese=Regex.Match(value,@"^(?:(\d+)分)?(?:(\d+)秒)?$");
        if(chinese.Success && value.Length>0){double.TryParse(chinese.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var minutePart);double.TryParse(chinese.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var secondPart);var total=minutePart*60+secondPart;return double.IsFinite(total) ? total : 0;}
        return double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number) && double.IsFinite(number) ? number : 0;
    }
    private static string Normalize(string value) => Regex.Replace(WebUtility.HtmlDecode(value), @"[^\p{L}\p{N}]", "").ToLowerInvariant();
    private static string Text(JsonElement item, string key) => item.ValueKind==JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(value.GetString() ?? "") : "";
}

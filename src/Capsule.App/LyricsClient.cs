using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Capsule.Core;

namespace Capsule.App;

public sealed class LyricsClient : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
    public LyricsClient() => http.DefaultRequestHeaders.UserAgent.ParseAdd("LingdongCapsule/0.1 (+https://github.com/Freedomzyi/LingdongCapsule)");
    public void Dispose() => http.Dispose();
    public string Status { get; private set; } = "等待音乐";

    public async Task<LyricsDocument> FetchAsync(MediaSnapshot track, CancellationToken cancellation)
    {
        if (!track.Available || string.IsNullOrWhiteSpace(track.Artist) || track.Title == "未知曲目") return LyricsDocument.Empty;
        Status = "正在获取歌词";
        var first = track.SourceId.Contains("netease", StringComparison.OrdinalIgnoreCase) || track.SourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ? "netease" : "tencent";
        foreach (var provider in new[] { first, first == "netease" ? "tencent" : "netease", "netease-official", "lrclib" })
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var document = provider switch
                {
                    "tencent" or "netease" => await RelayAsync(provider, track, cancellation),
                    "netease-official" => await NeteaseAsync(track, cancellation),
                    _ => await LrclibAsync(track, cancellation)
                };
                if (document.Lines.Count > 0) { Status = "歌词已连接 · " + document.Source; return document; }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or FormatException)
            { Status = "歌词源暂不可用，正在尝试其他来源"; }
        }
        Status = "未找到匹配歌词";
        return LyricsDocument.Empty;
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
    }

    private async Task<LyricsDocument> RelayAsync(string provider, MediaSnapshot track, CancellationToken cancellation)
    {
        var query = Uri.EscapeDataString(track.Title + " " + track.Artist);
        var search = provider == "tencent" ? $"https://api.vkeys.cn/v2/music/tencent/search/song?word={query}" : $"https://api.vkeys.cn/v2/music/netease?word={query}";
        using var results = await GetAsync(search, cancellation);
        if (!results.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return LyricsDocument.Empty;
        var selected = Select(data, track, false);
        if (selected is null) return LyricsDocument.Empty;
        var id = selected.Value.GetProperty("id").ToString();
        using var lyric = await GetAsync($"https://api.vkeys.cn/v2/music/{provider}/lyric?id={Uri.EscapeDataString(id)}", cancellation);
        if (!lyric.RootElement.TryGetProperty("data", out var payload) || payload.ValueKind != JsonValueKind.Object) return LyricsDocument.Empty;
        return LyricsDocument.Parse(Text(payload, "lrc") is { Length: > 0 } lrc ? lrc : Text(payload, "rc"), Text(payload, "trans"), Text(payload, "yrc"), provider == "tencent" ? "落月 QQ" : "落月网易云");
    }

    private async Task<LyricsDocument> NeteaseAsync(MediaSnapshot track, CancellationToken cancellation)
    {
        using var response = await http.PostAsync("https://music.163.com/api/search/get/web", new FormUrlEncodedContent(new Dictionary<string, string> { ["s"] = track.Title + " " + track.Artist, ["type"] = "1", ["limit"] = "20" }), cancellation);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
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
        var target = Normalize(track.Title);
        var artists = Regex.Split(track.Artist, @"[/、&,，;；|]").Select(Normalize).Where(name => name.Length > 0).ToArray();
        JsonElement? best = null;
        var bestScore = 0;
        foreach (var song in list.EnumerateArray())
        {
            if(song.ValueKind!=JsonValueKind.Object)continue;
            var name = Text(song, official ? "name" : "song");
            var full = name + Text(song, "subtitle");
            var score = Normalize(name) == target || Normalize(full) == target ? 100 : 0;
            if (score == 0) continue;
            var singer = Text(song, "singer");
            if (official && song.TryGetProperty("artists", out var singers)) singer = string.Join("/", singers.EnumerateArray().Select(item => Text(item, "name")));
            var normalizedSinger = Normalize(singer);
            if (artists.Length > 0 && !artists.Any(normalizedSinger.Contains)) continue;
            var duration = official && song.TryGetProperty("duration", out var milliseconds) ? milliseconds.GetDouble() / 1000 : Duration(Text(song, "interval"));
            if (duration > 0 && track.Duration > TimeSpan.Zero && Math.Abs(duration - track.Duration.TotalSeconds) > 4) continue;
            score += artists.Count(normalizedSinger.Contains) * 10;
            if (score > bestScore) { bestScore = score; best = song; }
        }
        return best;
    }

    private static double Duration(string value)
    {
        var parts = value.Split(':');
        if (parts.Length == 2 && double.TryParse(parts[0], out var minutes) && double.TryParse(parts[1], out var seconds)) return minutes * 60 + seconds;
        return double.TryParse(value, out var number) ? number : 0;
    }
    private static string Normalize(string value) => Regex.Replace(WebUtility.HtmlDecode(value), @"[^\p{L}\p{N}]", "").ToLowerInvariant();
    private static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(value.GetString() ?? "") : "";
}

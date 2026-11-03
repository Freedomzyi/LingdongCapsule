using System.Globalization;
using System.Text.RegularExpressions;

namespace Capsule.Core;

public sealed record LyricWord(double StartMs, double DurationMs, string Text);
public sealed record LyricLine(double StartMs, string Text, string Translation, IReadOnlyList<LyricWord> Words);
public readonly record struct LyricFrame(LyricLine? Line, double PositionMs, double Progress);

public sealed class LyricsDocument
{
    private static readonly Regex TimeTag = new(@"\[(\d+):(\d{1,2}(?:\.\d+)?)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordHeader = new(@"^\[(\d+),(\d+)\](.*)$", RegexOptions.Compiled);
    private static readonly Regex PrefixWord = new(@"\((\d+),(\d+)(?:,\d+)?\)([^()]*)", RegexOptions.Compiled);
    private static readonly Regex SuffixWord = new(@"([^()]+)\((\d+),(\d+)\)", RegexOptions.Compiled);
    public static LyricsDocument Empty { get; } = new([], "");
    public IReadOnlyList<LyricLine> Lines { get; }
    public string Source { get; }
    public bool HasWordTiming => Lines.Any(line => line.Words.Count > 0);

    private LyricsDocument(IReadOnlyList<LyricLine> lines, string source) { Lines = lines; Source = source; }
    public static LyricsDocument FromTimedLines(IEnumerable<LyricLine> lines,string source)=>new(lines.Where(line=>double.IsFinite(line.StartMs) && line.StartMs>=0).OrderBy(line=>line.StartMs).ToArray(),source);

    public static LyricsDocument Parse(string lrc, string translation = "", string wordTiming = "", string source = "")
    {
        var main = ReadLrc(lrc);
        var translated = ReadLrc(translation);
        var wordLines = ReadWords(wordTiming);
        if (main.Count == 0) main = wordLines.Select(line => (line.StartMs, line.Text)).ToList();
        List<LyricLine> result = [];
        foreach (var (start, text) in main.OrderBy(line => line.Start))
        {
            var trans = translated.FirstOrDefault(line => Math.Abs(line.Start - start) < 120).Text ?? "";
            var words = wordLines.FirstOrDefault(line => Math.Abs(line.StartMs - start) < 1000 && Normalize(line.Text) == Normalize(text))?.Words ?? [];
            result.Add(new(start, text, trans == text ? "" : trans, words));
        }
        return new(result, source);
    }

    private static List<(double Start, string Text)> ReadLrc(string text)
    {
        List<(double, string)> result = [];
        var offsetMatch = Regex.Match(text, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
        var offset=0d;if(offsetMatch.Success && !FiniteNumber(offsetMatch.Groups[1].Value,out offset))return result;
        foreach (var raw in text.Split('\n'))
        {
            var tags = TimeTag.Matches(raw);
            if (tags.Count == 0) continue;
            var content = raw[(tags[^1].Index + tags[^1].Length)..].Trim();
            foreach (Match tag in tags)
            {
                if(!FiniteNumber(tag.Groups[1].Value,out var minutes) || !FiniteNumber(tag.Groups[2].Value,out var seconds))continue;
                var ms=minutes*60000+seconds*1000-offset;
                if(double.IsFinite(ms))result.Add((ms, content));
            }
        }
        return result;
    }

    private static List<LyricLine> ReadWords(string text)
    {
        List<LyricLine> result = [];
        foreach (var raw in text.Split('\n'))
        {
            var header = WordHeader.Match(raw.Trim());
            if (!header.Success) continue;
            if(!FiniteNumber(header.Groups[1].Value,out var start) || !FiniteNumber(header.Groups[2].Value,out var duration))continue;
            var body = header.Groups[3].Value;
            var prefix = body.StartsWith('(');
            List<LyricWord> words = [];
            foreach (Match token in (prefix ? PrefixWord : SuffixWord).Matches(body))
            {
                var word = token.Groups[prefix ? 3 : 1].Value;
                if(!FiniteNumber(token.Groups[prefix ? 1 : 2].Value,out var at) || !FiniteNumber(token.Groups[prefix ? 2 : 3].Value,out var length)){words.Clear();break;}
                if (at < start && at < duration) at += start;
                if(!double.IsFinite(at) || !double.IsFinite(at+length)){words.Clear();break;}
                words.Add(new(at, Math.Max(1, length), word));
            }
            if (words.Count > 0) result.Add(new(start, string.Concat(words.Select(word => word.Text)), "", words));
        }
        return result;
    }
    private static bool FiniteNumber(string text,out double number)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out number) && double.IsFinite(number);

    public LyricFrame At(TimeSpan position, double offsetSeconds = 0)
    {
        var ms = position.TotalMilliseconds + offsetSeconds * 1000;
        int lower = 0, upper = Lines.Count - 1, found = -1;
        while (lower <= upper)
        {
            var mid = (lower + upper) / 2;
            if (Lines[mid].StartMs <= ms) { found = mid; lower = mid + 1; } else upper = mid - 1;
        }
        if (found < 0) return new(null, ms, 0);
        var line = Lines[found];
        var end = found + 1 < Lines.Count ? Lines[found + 1].StartMs : line.Words.Count > 0 ? line.Words[^1].StartMs + line.Words[^1].DurationMs : line.StartMs + 4000;
        return new(line, ms, Math.Clamp((ms - line.StartMs) / Math.Max(1, end - line.StartMs), 0, 1));
    }

    private static string Normalize(string text) => Regex.Replace(text, @"[^\p{L}\p{N}]", "").ToLowerInvariant();
}

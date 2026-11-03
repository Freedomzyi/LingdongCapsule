using System.Text.Json;

namespace Capsule.Core;

// Protocol state is independent of WebSocket transport and desktop rendering.
public sealed class SoloState
{
    public LyricsDocument Lyrics{get;private set;}=LyricsDocument.Empty;
    public bool Playing{get;private set;}
    public double? Volume{get;private set;}
    private double position;
    private long positionAt,spectrumAt;
    private double[] bands=[];
    public double Position(long now)=>Math.Clamp(position+(Playing ? Math.Max(0,now-positionAt) : 0),0,int.MaxValue);
    public IReadOnlyList<double> Spectrum(long now)=>Playing && now-spectrumAt<=500 ? bands : Array.Empty<double>();
    public void Reset(){Lyrics=LyricsDocument.Empty;Playing=false;Volume=null;position=0;positionAt=0;bands=[];spectrumAt=0;}
    public bool Apply(string message,long now)
    {
        if(message.Length>1024*1024)return false;
        try
        {
            using var document=JsonDocument.Parse(message);var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("type",out var type) || type.ValueKind!=JsonValueKind.String)return false;
            switch(type.GetString())
            {
                case "init":
                    if(!root.TryGetProperty("lyrics",out var lyrics) || lyrics.ValueKind!=JsonValueKind.Array){Lyrics=LyricsDocument.Empty;return false;}
                    List<LyricLine> rows=[];
                    foreach(var row in lyrics.EnumerateArray().Take(10000))
                    {
                        if(row.ValueKind!=JsonValueKind.Object || !row.TryGetProperty("time",out var time) || time.ValueKind!=JsonValueKind.Number || !time.TryGetDouble(out var stamp) || !double.IsFinite(stamp) || stamp<0 || stamp>int.MaxValue)continue;
                        var text=row.TryGetProperty("text",out var textValue) && textValue.ValueKind==JsonValueKind.String ? textValue.GetString()! : "";
                        var translated=row.TryGetProperty("translation",out var transValue) && transValue.ValueKind==JsonValueKind.String ? transValue.GetString()! : "";
                        var parts=text.Split(['\r','\n','\u2028','\u2029','\u0085','\v','\f'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                        if(parts.Length>1){text=parts[0];if(translated.Length==0)translated=string.Join(" ",parts.Skip(1));}
                        rows.Add(new(stamp,text.Length==0 ? translated : text,text.Length==0 || text==translated ? "" : translated,[]));
                    }
                    Lyrics=LyricsDocument.FromTimedLines(rows,"Just Solo");position=0;positionAt=now;return true;
                case "progress":
                    if(!root.TryGetProperty("position",out var progress) || progress.ValueKind!=JsonValueKind.Number || !progress.TryGetDouble(out var value) || !double.IsFinite(value) || value<0 || value>int.MaxValue)return false;
                    position=value;positionAt=now;return true;
                case "playback":
                    if(!root.TryGetProperty("status",out var status) || status.ValueKind!=JsonValueKind.String)return false;
                    position=Position(now);positionAt=now;Playing=status.GetString()=="playing";if(!Playing)bands=[];return true;
                case "volume":
                    if(!root.TryGetProperty("value",out var volume) || !volume.TryGetDouble(out var level) || !double.IsFinite(level))return false;
                    Volume=Math.Clamp(level,0,1);return true;
                case "spectrum":
                    if(!root.TryGetProperty("bands",out var spectrum) || spectrum.ValueKind!=JsonValueKind.Array)return false;
                    var next=spectrum.EnumerateArray().Take(512).Select(item=>item.ValueKind==JsonValueKind.Number && item.TryGetDouble(out var amplitude) && double.IsFinite(amplitude) ? Math.Clamp(amplitude,0,1) : 0).ToArray();bands=next;spectrumAt=now;return true;
                default:return false;
            }
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or FormatException){return false;}
    }
}

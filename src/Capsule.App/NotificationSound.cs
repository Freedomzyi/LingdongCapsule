using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;

namespace Capsule.App;

// Original synthesized chimes. No borrowed iOS, messenger or reference-project audio.
internal sealed class NotificationSound : IDisposable
{
    private MediaPlayer? player;
    private readonly DispatcherTimer deadline=new(){Interval=TimeSpan.FromSeconds(5)};
    private int generation;
    private bool disposed;
    public string Error{get;private set;}="";
    internal int PlayCount{get;private set;}
    public NotificationSound(){deadline.Tick+=(_,_)=>Stop();}
    public static IReadOnlyDictionary<string,string> Options()
    {
        var options=new Dictionary<string,string>{{"none","无"}};
        var labels=new[]{"微光","晨露","水滴","轻铃","星点","和弦","竹音","回声","清风","浮光","晨星","涟漪","月色","轻跃"};
        for(var index=0;index<labels.Length;index++)options[$"chime-{index+1:00}"]=labels[index];
        options["custom"]="自选音频";return options;
    }
    internal static byte[] Chime(int index)
    {
        if(index is <1 or >14)throw new ArgumentOutOfRangeException(nameof(index));
        const int rate=44100;const double duration=.9;var samples=(int)(rate*duration);
        using var memory=new MemoryStream();using var writer=new BinaryWriter(memory,Encoding.ASCII,true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);
        var root=440*Math.Pow(2,(index-5)/12d);var interval=(index%3) switch{0=>1.25,1=>1.5,_=>2d};
        for(var sample=0;sample<samples;sample++)
        {
            var t=(double)sample/rate;var envelope=Math.Min(1,t/.014)*Math.Exp(-t*7);var second=Math.Max(0,t-.16);
            var signal=Math.Sin(2*Math.PI*root*t)*envelope+.55*Math.Sin(2*Math.PI*root*interval*second)*Math.Min(1,second/.014)*Math.Exp(-second*9);
            writer.Write((short)Math.Clamp(signal*12000,short.MinValue,short.MaxValue));
        }
        writer.Flush();return memory.ToArray();
    }
    public static bool ValidFile(string path,out string error)
    {
        error="";
        try
        {
            var file=new FileInfo(path);var supported=new[]{".wav",".mp3",".m4a",".aac",".wma",".flac",".aiff",".aif"};
            if(!file.Exists || file.Length==0 || file.Length>2*1024*1024 || !supported.Contains(file.Extension,StringComparer.OrdinalIgnoreCase)){error="请选择不超过 2 MB 的受支持音频。";return false;}
            return true;
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){error="无法读取所选音频。";return false;}
    }
    internal static async Task<bool> ValidateDurationAsync(string path)
    {
        var probe=new MediaPlayer();var ready=new TaskCompletionSource<bool>();
        probe.MediaOpened+=(_,_)=>ready.TrySetResult(probe.NaturalDuration.HasTimeSpan && probe.NaturalDuration.TimeSpan>TimeSpan.Zero && probe.NaturalDuration.TimeSpan<=TimeSpan.FromSeconds(5));
        probe.MediaFailed+=(_,_)=>ready.TrySetResult(false);
        try{probe.Open(new Uri(Path.GetFullPath(path)));return await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));}
        catch(Exception exception) when(exception is IOException or TimeoutException or ArgumentException or InvalidOperationException){return false;}
        finally{probe.Close();}
    }
    public async Task<bool> PlayAsync(Settings settings,bool preview=false)
    {
        if(disposed || (!preview && (!settings.NotificationSoundEnabled || !settings.NotificationsEnabled || settings.QuietUntil>DateTimeOffset.UtcNow)))return false;
        if(settings.NotificationSoundKey=="none")return true;
        Stop();var current=++generation;Error="";
        try
        {
            string path;
            if(settings.NotificationSoundKey=="custom")
            {
                path=settings.NotificationSoundPath;if(!ValidFile(path,out var error)){Error=error;return false;}
            }
            else
            {
                if(!settings.NotificationSoundKey.StartsWith("chime-") || !int.TryParse(settings.NotificationSoundKey[6..],out var index) || index is <1 or >14){Error="提示音选择无效。";return false;}
                var folder=Path.Combine(Settings.Folder,"Sounds");Directory.CreateDirectory(folder);path=Path.Combine(folder,$"original-{index:00}.wav");
                if(!File.Exists(path))File.WriteAllBytes(path,Chime(index));
            }
            var active=new MediaPlayer{Volume=Math.Clamp(settings.NotificationSoundVolume/100,0,1)};player=active;var ready=new TaskCompletionSource<bool>();
            active.MediaOpened+=(_,_)=>
            {
                if(disposed || current!=generation){ready.TrySetResult(false);return;}
                if(!active.NaturalDuration.HasTimeSpan || active.NaturalDuration.TimeSpan<=TimeSpan.Zero || active.NaturalDuration.TimeSpan>TimeSpan.FromSeconds(5)){Error="请选择时长不超过 5 秒的有效音频。";Stop();ready.TrySetResult(false);return;}
                deadline.Start();active.Play();PlayCount++;ready.TrySetResult(true);
            };
            active.MediaFailed+=(_,_)=>{if(current==generation){Error="无法解码或播放所选音频。";Stop();}ready.TrySetResult(false);};
            active.MediaEnded+=(_,_)=>{if(current==generation)Stop();};
            active.Open(new Uri(Path.GetFullPath(path)));return await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TimeoutException){if(current==generation){Error="无法读取或播放提示音。";Stop();}return false;}
    }
    private void Stop(){generation++;deadline.Stop();player?.Close();player=null;}
    public void StopPlayback()=>Stop();
    public void Dispose(){disposed=true;Stop();}
}

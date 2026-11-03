using Capsule.Core;
using System.Windows;

namespace Capsule.App;

public partial class MainWindow
{
    private readonly LyricsClient lyricsClient=new();
    private readonly Dictionary<string,LyricsDocument> lyricsCache=[];
    private LyricsDocument lyrics=LyricsDocument.Empty;
    private CancellationTokenSource? lyricRequest;
    private string lyricKey="";
    internal LyricsDocument CurrentLyrics=>lyrics;

    private LyricFrame CurrentLyricFrame()=>settings.LyricsEnabled ? lyrics.At(media.Position,settings.LyricOffsetSeconds+0.13) : default;
    private void UpdateLyrics(MediaSnapshot track)
    {
        var key=settings.MediaEnabled && settings.LyricsEnabled && track.Available && track.Artist.Length>0 ? $"{track.SourceId}|{track.Title}|{track.Artist}|{track.Duration.TotalSeconds:0}" : "";
        if(key==lyricKey)return;
        lyricKey=key;lyricRequest?.Cancel();lyricRequest?.Dispose();lyricRequest=null;lyrics=LyricsDocument.Empty;
        if(key.Length==0)return;
        if(lyricsCache.TryGetValue(key,out var cached)){lyrics=cached;return;}
        lyricRequest=new CancellationTokenSource();
        _=LoadLyricsAsync(track,key,lyricRequest.Token);
    }
    private async Task LoadLyricsAsync(MediaSnapshot track,string key,CancellationToken cancellation)
    {
        try
        {
            for(var attempt=0;attempt<3;attempt++)
            {
                var result=await lyricsClient.FetchAsync(track,cancellation);
                if(closing || key!=lyricKey || cancellation.IsCancellationRequested)return;
                if(result.Lines.Count>0)
                {
                    lyrics=result;
                    if(lyricsCache.Count>=24)lyricsCache.Remove(lyricsCache.Keys.First());
                    lyricsCache[key]=result;
                    Render();return;
                }
                if(attempt<2)await Task.Delay(2000,cancellation);
            }
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
        catch(Exception){if(!closing && key==lyricKey)lyrics=LyricsDocument.Empty;}
    }
    private void RenderLyrics()
    {
        var frame=CurrentLyricFrame();
        var show=!IsMediaExpanded && frame.Line is {Text.Length:>0};
        LiveLyrics.Visibility=show ? Visibility.Visible : Visibility.Collapsed;
        Track.Visibility=show ? Visibility.Collapsed : Visibility.Visible;
        LiveLyrics.TranslationEnabled=settings.TranslationEnabled;
        LiveLyrics.KaraokeEnabled=settings.KaraokeEnabled;
        LiveLyrics.ScrollEnabled=settings.ScrollLyrics;
        LiveLyrics.Playing=state.Media.Playing;
        LiveLyrics.Refresh();
    }
}

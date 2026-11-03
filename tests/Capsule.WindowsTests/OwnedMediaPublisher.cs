using System.IO;
using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

internal sealed class OwnedMediaPublisher : IDisposable
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    private bool disposed;
    private readonly MediaPlayer player=new(){Volume=0,IsLoopingEnabled=true,AutoPlay=false};
    internal SystemMediaTransportControls Controls=>player.SystemMediaTransportControls;
    internal string Title{get;private set;}="Capsule owned test audio "+Guid.NewGuid().ToString("N")[..8];
    internal static async Task<OwnedMediaPublisher> StartAsync(string appId="LingdongCapsule.Owned.bilibili")
    {
        if(SetCurrentProcessExplicitAppUserModelID(appId)!=0)throw new Exception("Owned process AUMID could not be set");
        var owned=new OwnedMediaPublisher();
        try
        {
            var folder=Path.Combine(Environment.CurrentDirectory,"artifacts","media-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var path=Path.Combine(folder,"owned-silence.wav");
            using(var file=File.Create(path))using(var writer=new BinaryWriter(file))
            {
                const int bytes=8000*2*30;writer.Write("RIFF"u8);writer.Write(36+bytes);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(bytes);writer.Write(new byte[bytes]);
            }
            var ready=new TaskCompletionSource();owned.player.MediaOpened+=(_,_)=>ready.TrySetResult();owned.player.MediaFailed+=(_,args)=>ready.TrySetException(new Exception("Owned media failed: "+args.ErrorMessage));
            owned.player.CommandManager.IsEnabled=false;owned.Controls.IsEnabled=true;owned.Controls.IsPlayEnabled=true;owned.Controls.IsPauseEnabled=true;
            owned.Controls.ButtonPressed+=owned.OnButton;owned.Controls.PlaybackPositionChangeRequested+=owned.OnPosition;
            owned.player.Source=MediaSource.CreateFromUri(new Uri(path));await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
            owned.SetMetadata(owned.Title,false);owned.player.Play();owned.Controls.PlaybackStatus=MediaPlaybackStatus.Playing;owned.SetPosition(TimeSpan.FromSeconds(2));return owned;
        }
        catch{owned.Dispose();throw;}
    }
    internal void SetMetadata(string title,bool video)
    {
        Title=title;var display=Controls.DisplayUpdater;display.ClearAll();display.Type=video ? MediaPlaybackType.Video : MediaPlaybackType.Music;
        if(video)display.VideoProperties.Title=title;else{display.MusicProperties.Title=title;display.MusicProperties.Artist="Capsule test artist";display.MusicProperties.AlbumTitle="Owned test album";}display.Update();
    }
    internal async Task SetThumbnailAsync(string path)
    {
        var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path);Controls.DisplayUpdater.Thumbnail=Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file);Controls.DisplayUpdater.Update();
    }
    internal void SetPosition(TimeSpan position)
    {
        player.PlaybackSession.Position=position;Controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties{StartTime=TimeSpan.Zero,EndTime=TimeSpan.FromSeconds(30),MinSeekTime=TimeSpan.Zero,MaxSeekTime=TimeSpan.FromSeconds(30),Position=position});
    }
    private void OnButton(SystemMediaTransportControls sender,SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        if(args.Button==SystemMediaTransportControlsButton.Play)SetPlaying(true);
        if(args.Button==SystemMediaTransportControlsButton.Pause)SetPlaying(false);
    }
    internal void SetPlaying(bool active){if(active)player.Play();else player.Pause();Controls.PlaybackStatus=active ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;SetPosition(player.PlaybackSession.Position);}
    private void OnPosition(SystemMediaTransportControls sender,PlaybackPositionChangeRequestedEventArgs args)=>SetPosition(args.RequestedPlaybackPosition);
    public void Dispose(){if(disposed)return;disposed=true;Controls.IsEnabled=false;Controls.ButtonPressed-=OnButton;Controls.PlaybackPositionChangeRequested-=OnPosition;player.Dispose();}
}

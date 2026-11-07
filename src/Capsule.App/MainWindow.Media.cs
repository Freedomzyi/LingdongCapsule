using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Capsule.App;

public partial class MainWindow
{
    private readonly AudioService audio=new();
    private readonly DispatcherTimer mediaTimer=new(){Interval=TimeSpan.FromMilliseconds(80)};
    private bool timelineUpdating,seeking,volumeUpdating;
    private Capsule.Core.MediaSnapshot? seekTrack;
    private bool IsMediaExpanded=>controlsOpen && settings.ExpandedMediaControls;

    private void InitializeMediaControls()
    {
        CoverRing.ReadProgress=()=>media.Duration>TimeSpan.Zero ? media.Position.TotalSeconds/media.Duration.TotalSeconds : 0;
        Spectrum.ReadBands=()=>
        {
            var local=SoloSelected ? solo.Snapshot() : null;
            if(local is not {Connected:true,Bands.Length:>0})return audio.Bands;
            var source=local.Bands;var output=new double[5];
            for(var band=0;band<5;band++){var start=band*source.Length/5;var end=(band+1)*source.Length/5;for(var index=start;index<end;index++)output[band]=Math.Max(output[band],source[index]);}
            return output;
        };
        mediaTimer.Tick+=(_,_)=>UpdateTimeline();
        SeekBar.PreviewMouseLeftButtonDown+=(_,_)=>{seeking=true;seekTrack=state.Media;};
        SeekBar.PreviewMouseLeftButtonUp+=async(_,_)=>await CommitSeekAsync();
        SeekBar.LostMouseCapture+=(_,_)=>{if(seeking){seeking=false;seekTrack=null;UpdateTimeline();}};
        SeekBar.PreviewKeyUp+=async(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageDown or Key.PageUp){seekTrack=state.Media;seeking=true;await CommitSeekAsync();}};
        SeekBar.ValueChanged+=(_,_)=>{if(seeking && !timelineUpdating)Elapsed.Text=FormatTime(TimeSpan.FromSeconds(media.Duration.TotalSeconds*SeekBar.Value));};
        VolumeBar.ValueChanged+=async(_,_)=>
        {
            if(volumeUpdating || closing)return;
            var changed=await SetRequestedVolumeAsync(VolumeBar.Value);
            if(!closing && !changed)Design.PresentMessage("音量暂不可用","未能连接播放器或当前系统输出设备，请稍后重试。");
        };
        Shell.MouseWheel+=async(_,e)=>
        {
            if(state.Notification is not null || overviewOpen || !state.Media.Available)return;
            var local=SoloSelected ? solo.Snapshot() : null;var volume=local is {Connected:true,Volume:not null} ? local.Volume.Value : audio.Volume;
            e.Handled=true;await SetRequestedVolumeAsync(volume+e.Delta/120d*0.05);if(!closing)UpdateTimeline();
        };
    }
    private Task<bool> SetRequestedVolumeAsync(double level)=>SoloSelected && solo.Snapshot() is {Connected:true,Volume:not null} ? solo.SetVolumeAsync(level) : Task.FromResult(audio.SetVolume(level));
    private async Task CommitSeekAsync()
    {
        if(!seeking || closing)return;
        seeking=false;var expected=seekTrack;seekTrack=null;
        if(state.Media.SameTrackAs(expected))
        {
            var changed=await media.SeekAsync(SeekBar.Value,expectedTrack:expected);if(closing)return;
            if(!changed)Design.PresentMessage("无法定位播放进度","播放器暂不支持此次定位，或当前曲目已经变化。");
        }
        await RefreshAsync();if(!closing)UpdateTimeline();
    }
    private void PrepareMediaLayout()
    {
        var expanded=IsMediaExpanded;
        Controls.Visibility=expanded ? Visibility.Visible : Visibility.Collapsed;
        InlineControls.Visibility=!settings.ExpandedMediaControls ? Visibility.Visible : Visibility.Collapsed;
        TimelineSection.Visibility=media.Duration>TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        Spectrum.Visibility=settings.ShowSpectrum && state.Media.Playing ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RenderMediaControls()
    {
        PrepareMediaLayout();
        var expanded=IsMediaExpanded;
        PlayGlyph.Kind=state.Media.Playing ? TransportKind.Pause : TransportKind.Play;
        InlinePlayGlyph.Kind=PlayGlyph.Kind;
        var visibleMedia=MediaPanel.Visibility==Visibility.Visible && !visibilityResult.PassThrough;
        Spectrum.Active=settings.ShowSpectrum && state.Media.Playing && visibleMedia;
        var soloSpectrum=SoloSelected && solo.Snapshot() is {Connected:true,Bands.Length:>0};
        audio.Tick(!soloSpectrum && settings.ShowSpectrum && state.Media.Playing && settings.MediaEnabled && visibleMedia);
        CoverRing.Radius=Shell.CornerRadius.TopLeft;
        CoverRing.Active=settings.CoverProgress && visibleMedia && state.Mode==Capsule.Core.IslandMode.Media && state.Media.Playing && media.Duration>TimeSpan.Zero;
        mediaTimer.IsEnabled=expanded && visibleMedia;
        if(expanded)UpdateTimeline();
    }
    private void UpdateTimeline()
    {
        Total.Text=FormatTime(media.Duration);
        if(!seeking)
        {
            Elapsed.Text=FormatTime(media.Position);
            timelineUpdating=true;
            SeekBar.Value=media.Duration>TimeSpan.Zero ? Math.Clamp(media.Position.TotalSeconds/media.Duration.TotalSeconds,0,1) : 0;
            SeekBar.IsEnabled=media.CanSeek;
            timelineUpdating=false;
        }
        if(VolumeSection.Visibility==Visibility.Visible)
        {
            var local=SoloSelected ? solo.Snapshot() : null;volumeUpdating=true;
            if(local is {Connected:true,Volume:not null}){VolumeBar.Value=local.Volume.Value;VolumeBar.IsEnabled=true;}
            else{VolumeBar.Value=audio.Volume;VolumeBar.IsEnabled=audio.HasDevice;}
            volumeUpdating=false;
        }
    }
    private static string FormatTime(TimeSpan time)=>$"{(int)Math.Max(0,time.TotalMinutes)}:{Math.Max(0,time.Seconds):00}";
    private void OnCoverClick(object sender,MouseButtonEventArgs e)
    {
        if(!settings.ReturnToPlayer)return;
        e.Handled=true;
        if(!AppActivation.Open(state.Media.SourceId,reopenHiddenPlayer:true))Design.PresentMessage("无法唤回播放器","请从任务栏或开始菜单打开播放器。");
    }
}

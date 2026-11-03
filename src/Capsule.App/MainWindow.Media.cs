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
    private string seekTitle="";
    private bool IsMediaExpanded=>controlsOpen && settings.ExpandedMediaControls;

    private void InitializeMediaControls()
    {
        CoverRing.ReadProgress=()=>media.Duration>TimeSpan.Zero ? media.Position.TotalSeconds/media.Duration.TotalSeconds : 0;
        Spectrum.ReadBands=()=>audio.Bands;
        mediaTimer.Tick+=(_,_)=>UpdateTimeline();
        SeekBar.PreviewMouseLeftButtonDown+=(_,_)=>{seeking=true;seekTitle=state.Media.Title;};
        SeekBar.PreviewMouseLeftButtonUp+=async(_,_)=>await CommitSeekAsync();
        SeekBar.LostMouseCapture+=(_,_)=>{if(seeking){seeking=false;UpdateTimeline();}};
        SeekBar.PreviewKeyUp+=async(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageDown or Key.PageUp){seekTitle=state.Media.Title;seeking=true;await CommitSeekAsync();}};
        SeekBar.ValueChanged+=(_,_)=>{if(seeking && !timelineUpdating)Elapsed.Text=FormatTime(TimeSpan.FromSeconds(media.Duration.TotalSeconds*SeekBar.Value));};
        VolumeBar.ValueChanged+=(_,_)=>{if(!volumeUpdating && !audio.SetVolume(VolumeBar.Value))Design.PresentMessage("音量暂不可用","未能连接当前系统输出设备，请稍后重试。");};
        Shell.MouseWheel+=(_,e)=>
        {
            if(state.Notification is not null || overviewOpen || !state.Media.Available)return;
            audio.SetVolume(audio.Volume+e.Delta/120d*0.05);UpdateTimeline();e.Handled=true;
        };
    }
    private async Task CommitSeekAsync()
    {
        if(!seeking)return;
        seeking=false;
        if(state.Media.Title==seekTitle && !await media.SeekAsync(SeekBar.Value,seekTitle))Design.PresentMessage("无法定位播放进度","播放器暂不支持此次定位，或当前曲目已经变化。");
        await RefreshAsync();UpdateTimeline();
    }
    private void RenderMediaControls()
    {
        var expanded=IsMediaExpanded;
        Controls.Visibility=expanded ? Visibility.Visible : Visibility.Collapsed;
        InlineControls.Visibility=!settings.ExpandedMediaControls ? Visibility.Visible : Visibility.Collapsed;
        TimelineSection.Visibility=media.Duration>TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        PlayGlyph.Kind=state.Media.Playing ? TransportKind.Pause : TransportKind.Play;
        InlinePlayGlyph.Kind=PlayGlyph.Kind;
        Spectrum.Visibility=settings.ShowSpectrum && state.Media.Playing ? Visibility.Visible : Visibility.Collapsed;
        var visibleMedia=MediaPanel.Visibility==Visibility.Visible;
        Spectrum.Active=settings.ShowSpectrum && state.Media.Playing && visibleMedia;
        audio.Tick(settings.ShowSpectrum && state.Media.Playing && settings.MediaEnabled && visibleMedia);
        CoverRing.Radius=Shell.CornerRadius.TopLeft;
        CoverRing.Active=settings.CoverProgress && visibleMedia && state.Mode==Capsule.Core.IslandMode.Media && state.Media.Playing && media.Duration>TimeSpan.Zero;
        mediaTimer.IsEnabled=expanded;
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
            SeekBar.IsEnabled=media.Duration>TimeSpan.Zero;
            timelineUpdating=false;
        }
        if(VolumeSection.Visibility==Visibility.Visible){volumeUpdating=true;VolumeBar.Value=audio.Volume;VolumeBar.IsEnabled=audio.HasDevice;volumeUpdating=false;}
    }
    private static string FormatTime(TimeSpan time)=>$"{(int)Math.Max(0,time.TotalMinutes)}:{Math.Max(0,time.Seconds):00}";
    private void OnCoverClick(object sender,MouseButtonEventArgs e)
    {
        if(!settings.ReturnToPlayer)return;
        e.Handled=true;
        if(!AppActivation.Open(media.SourceId))Design.PresentMessage("无法唤回播放器","未找到对应的软件窗口，请从任务栏或开始菜单打开播放器。");
    }
}

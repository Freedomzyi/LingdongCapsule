using Capsule.Core;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Capsule.App;

public partial class MainWindow
{
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("shell32.dll")]private static extern int SHQueryUserNotificationState(out int state);
    [StructLayout(LayoutKind.Sequential)]private struct CursorPoint{public int X,Y;}
    private readonly DispatcherTimer visibilityProbe=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly GlobalShortcuts shortcuts;
    private IslandVisibilityResult visibilityResult;
    private bool manuallyHidden,awake,fullscreen,shortcutBusy;
    private long nextFullscreenProbe;
    internal IslandVisibilityResult LiveVisibility=>visibilityResult;
    internal GlobalShortcuts LiveShortcuts=>shortcuts;
    internal bool ManuallyHidden=>manuallyHidden;
    private void InitializeVisibility()
    {
        visibilityProbe.Tick+=(_,_)=>UpdateIslandVisibility();
        WakeButton.Click+=(_,args)=>{args.Handled=true;awake=true;UpdateIslandVisibility();};
        shortcuts.Pressed+=async action=>await RunShortcutAsync(action);
    }
    private bool PointerOverIsland()
    {
        if(!GetCursorPos(out var cursor) || !IsLoaded)return false;
        try{var point=PointFromScreen(new Point(cursor.X,cursor.Y));return point.X>=0 && point.Y>=0 && point.X<ActualWidth && point.Y<ActualHeight;}
        catch(InvalidOperationException){return false;}
    }
    private void UpdateIslandVisibility()
    {
        if(closing || !IsLoaded)return;
        visibilityProbe.IsEnabled=!manuallyHidden && (settings.AutoHide || settings.Passthrough);
        var over=PointerOverIsland();
        if(!over && (GetAsyncKeyState(1)&0x8000)!=0)awake=false;
        if(settings.AutoHide && settings.FullscreenAutoHide && Environment.TickCount64>=nextFullscreenProbe)
        {
            nextFullscreenProbe=Environment.TickCount64+800;
            fullscreen=SHQueryUserNotificationState(out var status)==0 && status is 2 or 3 or 4;
        }
        var result=IslandVisibility.Resolve(new(manuallyHidden,settings.AutoHide,settings.FocusAutoHide,settings.PauseAutoHide,settings.FullscreenAutoHide,fullscreen,state.Media.Available,state.Media.Playing,state.Notification!=null,overviewOpen || controlsOpen || seeking,awake,settings.Passthrough,over));
        WakeButton.Height=result.Hidden && settings.IslandShape==0 ? 4 : 36;
        WakeButton.Width=result.Hidden && settings.IslandShape==0 ? 42 : 36;
        WakeButton.VerticalAlignment=result.Hidden && settings.IslandShape==0 ? VerticalAlignment.Top : VerticalAlignment.Center;
        WakeButton.Content=result.Hidden && settings.IslandShape==0 ? "" : "⌄";
        if(result==visibilityResult)return;
        visibilityResult=result;
        var hidden=result.PassThrough;
        var duration=TimeSpan.FromMilliseconds(settings.MotionMs);
        var easing=new CubicEase{EasingMode=EasingMode.EaseOut};
        var fade=new DoubleAnimation(hidden ? 0 : 1,duration){EasingFunction=easing};Timeline.SetDesiredFrameRate(fade,settings.Glass.FramesPerSecond);
        IslandSurface.BeginAnimation(OpacityProperty,fade);
        IslandSurface.IsHitTestVisible=!hidden;
        WakeButton.Visibility=result.ShowWake ? Visibility.Visible : Visibility.Collapsed;
        RenderMediaControls();RenderLyrics();UpdateMaterial();
    }
    internal void SetIslandHidden(bool hidden)
    {
        manuallyHidden=hidden;awake=!hidden;
        if(hidden){controlsOpen=false;overviewOpen=false;state.Dismiss();notificationSound.StopPlayback();}
        Render();UpdateIslandVisibility();
    }
    internal void WakeIsland(){manuallyHidden=false;awake=true;UpdateIslandVisibility();}
    private bool PassThroughHit(IntPtr coordinates)
    {
        if(closing || !IsLoaded || !visibilityResult.PassThrough)return false;
        var packed=coordinates.ToInt64();var local=PointFromScreen(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));
        if(WakeButton.Visibility==Visibility.Visible)
        {
            var hit=WakeButton.TransformToAncestor(this).TransformBounds(new Rect(new Point(),new Size(WakeButton.ActualWidth,WakeButton.ActualHeight)));
            if(hit.Contains(local))return false;
        }
        return true;
    }
    private async Task RunShortcutAsync(ShortcutAction action)
    {
        if(action==ShortcutAction.ToggleIsland){SetIslandHidden(!manuallyHidden);return;}
        if(shortcutBusy || closing)return;
        shortcutBusy=true;
        try
        {
            var track=await media.ReadAsync();if(!track.Available)return;
            var success=action switch
            {
                ShortcutAction.PlayPause=>await media.ControlAsync("toggle"),
                ShortcutAction.Previous=>await media.ControlAsync("previous"),
                ShortcutAction.Next=>await media.ControlAsync("next"),
                _=>media.Duration>TimeSpan.Zero && await media.SeekAsync(Math.Clamp((media.Position.TotalSeconds+(action==ShortcutAction.SeekForward ? 3 : -3))/media.Duration.TotalSeconds,0,1),track.Title)
            };
            if(!success)Design.PresentMessage("媒体操作未完成","播放器暂不支持此次操作，或曲目已经变化。");
            await RefreshAsync();
        }
        catch(Exception){if(!closing)Design.PresentMessage("媒体操作未完成","媒体会话已变化，请稍后重试。");}
        finally{shortcutBusy=false;}
    }
}

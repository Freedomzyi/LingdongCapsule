using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Capsule.App;

public partial class MainWindow
{
    private void SetVisualScale(double scale)
    {
        if(ActiveVisualScale==scale)return;
        var previous=islandScale.ScaleX;ActiveVisualScale=scale;
        islandScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);islandScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        islandScale.ScaleX=islandScale.ScaleY=scale;
        if(!IsLoaded)return;
        var transition=new DoubleAnimation(previous,scale,TimeSpan.FromMilliseconds(settings.MotionMs)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
        Timeline.SetDesiredFrameRate(transition,settings.Glass.FramesPerSecond);
        islandScale.BeginAnimation(ScaleTransform.ScaleXProperty,transition);islandScale.BeginAnimation(ScaleTransform.ScaleYProperty,transition);
    }
}

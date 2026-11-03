using System.Windows.Media;

namespace Capsule.App;

internal sealed class AnimationCadence
{
    internal int FramesPerSecond{get;set;}=90;
    private TimeSpan next;
    internal bool Advance(EventArgs args)=>args is not RenderingEventArgs frame || Advance(frame.RenderingTime);
    internal bool Advance(TimeSpan time)
    {
        if(time<next)return false;
        next=time+TimeSpan.FromSeconds(1d/Math.Clamp(FramesPerSecond,30,144));return true;
    }
}

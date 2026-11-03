using System.Windows;
using System.Windows.Media;

namespace Capsule.App;

public sealed class CapsuleProgressView : FrameworkElement
{
    private bool active,listening;
    public Func<double>? ReadProgress {get;set;}
    public double Radius {get;set;}=20;
    public bool Active{get=>active;set{active=value;Subscribe();}}
    private static readonly Brush Green=new SolidColorBrush(Color.FromRgb(48,209,88));
    public CapsuleProgressView(){IsHitTestVisible=false;Loaded+=(_,_)=>Subscribe();Unloaded+=(_,_)=>{CompositionTarget.Rendering-=OnFrame;listening=false;};}
    private void Subscribe(){var next=active&&IsLoaded;if(next==listening)return;if(next)CompositionTarget.Rendering+=OnFrame;else CompositionTarget.Rendering-=OnFrame;listening=next;InvalidateVisual();}
    private readonly AnimationCadence cadence=new();
    public int FrameRate{get=>cadence.FramesPerSecond;set=>cadence.FramesPerSecond=value;}
    private void OnFrame(object? sender,EventArgs args){if(cadence.Advance(args))InvalidateVisual();}
    protected override void OnRender(DrawingContext context)
    {
        if(!active || ActualWidth<3 || ActualHeight<3)return;
        const double stroke=1.4;
        var rect=new Rect(stroke/2,stroke/2,ActualWidth-stroke,ActualHeight-stroke);
        var radius=Math.Min(Radius,Math.Min(rect.Width,rect.Height)/2);
        var perimeter=2*(rect.Width+rect.Height-4*radius)+2*Math.PI*radius;
        var fraction=Math.Clamp(ReadProgress?.Invoke()??0,0,1);
        if(fraction<=0)return;
        var pen=new Pen(Green,stroke){DashStyle=new DashStyle([fraction*perimeter/stroke,Math.Max(0.001,(1-fraction)*perimeter/stroke)],0),DashCap=PenLineCap.Round};
        context.DrawGeometry(null,pen,new RectangleGeometry(rect,radius,radius));
    }
}

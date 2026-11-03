using System.Windows;
using System.Windows.Media;

namespace Capsule.App;

public sealed class AudioBarsView : FrameworkElement
{
    private readonly double[] levels=new double[5];
    private bool active,listening;
    private static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(225,225,232));
    public Func<double[]>? ReadBands {get;set;}
    public bool Active {get=>active;set{active=value;Subscribe();}}
    public AudioBarsView(){Loaded+=(_,_)=>Subscribe();IsVisibleChanged+=(_,_)=>Subscribe();Unloaded+=(_,_)=>{CompositionTarget.Rendering-=OnFrame;listening=false;};}
    private void Subscribe(){var on=active&&IsVisible&&IsLoaded;if(on==listening)return;if(on)CompositionTarget.Rendering+=OnFrame;else CompositionTarget.Rendering-=OnFrame;listening=on;InvalidateVisual();}
    private void OnFrame(object? sender,EventArgs args)
    {
        var target=ReadBands?.Invoke();
        for(var i=0;i<5;i++){var next=target is {Length:>=5} ? target[i] : 0;levels[i]+=(next-levels[i])*(next>levels[i] ? 0.42 : 0.12);}
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        var gap=2d;var width=Math.Max(1,(ActualWidth-gap*4)/5);
        for(var i=0;i<5;i++){var height=Math.Max(2,ActualHeight*levels[i]);context.DrawRoundedRectangle(Design.IsDark ? Ink : Brushes.Black,null,new Rect(i*(width+gap),(ActualHeight-height)/2,width,height),width/2,width/2);}
    }
}

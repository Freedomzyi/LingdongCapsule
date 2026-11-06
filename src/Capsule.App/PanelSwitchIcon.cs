using System.Windows;
using System.Windows.Media;

namespace Capsule.App;

// Optical-size vector glyphs avoid font-dependent grid and music characters.
public sealed class PanelSwitchIcon : FrameworkElement
{
    public static readonly DependencyProperty MediaProperty=DependencyProperty.Register(nameof(Media),typeof(bool),typeof(PanelSwitchIcon),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Media{get=>(bool)GetValue(MediaProperty);set=>SetValue(MediaProperty,value);}
    protected override void OnRender(DrawingContext drawing)
    {
        var ink=TryFindResource("IslandInk") as Brush ?? Brushes.White;
        var pen=new Pen(ink,1.35){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        drawing.PushTransform(new ScaleTransform(ActualWidth/18,ActualHeight/18));
        if(Media)
        {
            drawing.DrawLine(pen,new Point(7,12.5),new Point(7,4));
            drawing.DrawLine(pen,new Point(7,4),new Point(14,2.5));
            drawing.DrawLine(pen,new Point(14,2.5),new Point(14,11));
            drawing.DrawEllipse(ink,null,new Point(4.8,13),2.3,1.8);
            drawing.DrawEllipse(ink,null,new Point(11.8,11.5),2.3,1.8);
        }
        else
        {
            drawing.DrawRoundedRectangle(null,pen,new Rect(2,3,14,11),2,2);
            drawing.DrawLine(pen,new Point(6,17),new Point(12,17));
            drawing.DrawLine(pen,new Point(9,14),new Point(9,17));
            drawing.DrawLine(pen,new Point(5,10),new Point(5,8));
            drawing.DrawLine(pen,new Point(9,10),new Point(9,6));
            drawing.DrawLine(pen,new Point(13,10),new Point(13,7));
        }
        drawing.Pop();
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Capsule.Widgets;

internal sealed class RingWidget : FrameworkElement
{
    internal double Progress;
    internal string Caption="",Value="";
    internal bool Dark=true;
    internal Color Accent=Color.FromRgb(95,210,165);
    internal RingWidget(){Width=74;Height=22;Focusable=true;}
    protected override void OnRender(DrawingContext drawing)
    {
        var accent=new SolidColorBrush(Accent);
        drawing.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromArgb(50,Accent.R,Accent.G,Accent.B)),2.3),new Point(11,11),8.5,8.5);
        var progress=Math.Clamp(Progress,0,1);
        if(progress>=.9999)drawing.DrawEllipse(null,new Pen(accent,2.3),new Point(11,11),8.5,8.5);
        else if(progress>0)
        {
            var angle=progress*Math.PI*2;
            var arc=new StreamGeometry();using(var path=arc.Open()){path.BeginFigure(new Point(11,2.5),false,false);path.ArcTo(new Point(11+8.5*Math.Sin(angle),11-8.5*Math.Cos(angle)),new Size(8.5,8.5),0,progress>.5,SweepDirection.Clockwise,true,false);}arc.Freeze();
            drawing.DrawGeometry(null,new Pen(accent,2.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},arc);
        }
        var font=new Typeface(System.Windows.Documents.TextElement.GetFontFamily(this),FontStyles.Normal,FontWeights.Medium,FontStretches.Normal);
        var ink=Dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(29,29,31));
        void Text(string text,double size,Brush brush,double x,double y)
        {
            var formatted=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,font,size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=45,Trimming=TextTrimming.CharacterEllipsis};drawing.DrawText(formatted,new Point(x,y));
        }
        Text(Caption,8,new SolidColorBrush(Dark ? Color.FromRgb(173,173,181) : Color.FromRgb(100,100,110)),25,0);
        Text(Value,11,ink,25,8.5);
    }
}

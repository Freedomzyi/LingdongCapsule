using System.Windows;
using System.Windows.Media;

namespace Capsule.App;

public enum TransportKind {Previous,Play,Pause,Next}
public sealed class TransportIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(TransportKind),typeof(TransportIcon),new FrameworkPropertyMetadata(TransportKind.Play,FrameworkPropertyMetadataOptions.AffectsRender));
    public TransportKind Kind{get=>(TransportKind)GetValue(KindProperty);set=>SetValue(KindProperty,value);}
    private static readonly Geometry[] Shapes=
    [
        Geometry.Parse("M14,3 L5,9 L14,15 Z M3,3 L5,3 L5,15 L3,15 Z"),
        Geometry.Parse("M5,2 L16,9 L5,16 Z"),
        Geometry.Parse("M4,3 L7,3 L7,15 L4,15 Z M11,3 L14,3 L14,15 L11,15 Z"),
        Geometry.Parse("M4,3 L13,9 L4,15 Z M13,3 L15,3 L15,15 L13,15 Z")
    ];
    protected override void OnRender(DrawingContext context){context.PushTransform(new ScaleTransform(ActualWidth/18,ActualHeight/18));context.DrawGeometry(Design.IsDark ? Brushes.White : Brushes.Black,null,Shapes[(int)Kind]);context.Pop();}
}

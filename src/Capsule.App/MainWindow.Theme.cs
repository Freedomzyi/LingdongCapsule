using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capsule.App;

public partial class MainWindow
{
    private bool? appliedIslandDark;
    private byte? appliedBackgroundAlpha;
    private void ApplyIslandTheme()
    {
        var dark=Design.IsDark;
        var alpha=(byte)Math.Round(Math.Clamp(settings.BackgroundOpacity,0,100)*255/100);
        if(appliedIslandDark==dark && appliedBackgroundAlpha==alpha)return;appliedIslandDark=dark;appliedBackgroundAlpha=alpha;
        Shell.Background=new SolidColorBrush(dark ? Color.FromArgb(alpha,16,16,18) : Color.FromArgb(alpha,245,245,247));
        Shell.BorderBrush=new SolidColorBrush(dark ? Color.FromRgb(51,51,56) : Color.FromRgb(210,210,216));
        var ink=dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(29,29,31));
        var muted=new SolidColorBrush(dark ? Color.FromRgb(173,173,181) : Color.FromRgb(100,100,110));
        void Visit(DependencyObject parent)
        {
            if(ReferenceEquals(parent,OverviewHost))return;
            if(parent is Border {Tag:string tag} && tag.StartsWith("plugin:",StringComparison.Ordinal))return;
            if(parent is TextBlock text)text.Foreground=ink;
            if(parent is TransportIcon icon)icon.InvalidateVisual();
            for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)Visit(VisualTreeHelper.GetChild(parent,index));
        }
        Visit(IslandSurface);
        foreach(var text in new[]{ClockDate,Artist,Elapsed,Total,NoticeApp,NoticeBody,NoticeRightApp,NoticeNow})text.Foreground=muted;
        LiveLyrics.SetDark(dark);Spectrum.InvalidateVisual();
    }
}

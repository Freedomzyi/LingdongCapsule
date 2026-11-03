using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capsule.App;

public partial class MainWindow
{
    private bool? appliedIslandDark;
    private void ApplyIslandTheme()
    {
        var dark=Design.IsDark;
        if(appliedIslandDark==dark)return;appliedIslandDark=dark;
        Shell.Background=new SolidColorBrush(dark ? Color.FromArgb(245,16,16,18) : Color.FromArgb(245,245,245,247));
        Shell.BorderBrush=new SolidColorBrush(dark ? Color.FromRgb(51,51,56) : Color.FromRgb(210,210,216));
        var ink=dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(29,29,31));
        var muted=new SolidColorBrush(dark ? Color.FromRgb(173,173,181) : Color.FromRgb(100,100,110));
        void Visit(DependencyObject parent)
        {
            if(ReferenceEquals(parent,OverviewHost))return;
            if(parent is TextBlock text)text.Foreground=ink;
            if(parent is TransportIcon icon)icon.InvalidateVisual();
            for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)Visit(VisualTreeHelper.GetChild(parent,index));
        }
        Visit(IslandSurface);
        foreach(var text in new[]{ClockDate,Artist,Elapsed,Total,NoticeApp,NoticeBody,NoticeRightApp,NoticeNow})text.Foreground=muted;
        LiveLyrics.SetDark(dark);Spectrum.InvalidateVisual();
    }
}

using System.Windows;
using System.Windows.Controls;

namespace Capsule.App;

public partial class MainWindow
{
    private readonly Grid compactLayout=new(){ClipToBounds=true};
    private void InitializeComposite()
    {
        var root=(Grid)Shell.Child;root.Children.Remove(Clock);root.Children.Remove(MediaPanel);
        compactLayout.ColumnDefinitions.Add(new());compactLayout.ColumnDefinitions.Add(new());
        compactLayout.Children.Add(Clock);compactLayout.Children.Add(MediaPanel);root.Children.Insert(0,compactLayout);
        Clock.ClipToBounds=true;
    }
    private void RenderComposite(bool showMedia,bool combined)
    {
        Clock.Margin=new Thickness(0);Grid.SetColumn(Clock,settings.MediaFirst ? 1 : 0);Grid.SetColumn(MediaPanel,settings.MediaFirst ? 0 : 1);
        var clockColumn=compactLayout.ColumnDefinitions[settings.MediaFirst ? 1 : 0];var mediaColumn=compactLayout.ColumnDefinitions[settings.MediaFirst ? 0 : 1];
        if(combined)
        {
            Clock.MaxWidth=Math.Max(0,settings.Width-24-110-8);
            Clock.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            clockColumn.Width=new GridLength(Math.Min(Clock.DesiredSize.Width+8,Math.Max(0,settings.Width-24-110)));mediaColumn.Width=new GridLength(1,GridUnitType.Star);
            Clock.Margin=settings.MediaFirst ? new Thickness(8,0,0,0) : new Thickness(0,0,8,0);
        }
        else{Clock.MaxWidth=double.PositiveInfinity;clockColumn.Width=showMedia ? new GridLength(0) : new GridLength(1,GridUnitType.Star);mediaColumn.Width=showMedia ? new GridLength(1,GridUnitType.Star) : new GridLength(0);}
    }
}

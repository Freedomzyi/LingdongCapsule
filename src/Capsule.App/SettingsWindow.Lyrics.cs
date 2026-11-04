using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capsule.App;

public sealed partial class SettingsWindow
{
    private void AddLyricOffset(StackPanel panel)
    {
        var row=new Grid{Margin=new(0,8,0,8)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        row.Children.Add(Label("歌词偏移","秒；正值提前，负值延后，每次 0.1 秒，没有额外的 ±3 秒限制；还原为 0。"));
        var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);row.Children.Add(actions);
        var minus=new Button{Content="−",Width=30,Height=30};var plus=new Button{Content="＋",Width=30,Height=30};var reset=new Button{Content="还原",Margin=new(7,0,0,0),Padding=new(9,6,9,6)};
        var number=new TextBlock{MinWidth=70,MaxWidth=100,TextAlignment=TextAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(0,122,255))};
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Visibility=Visibility.Collapsed};Design.Bind(feedback,TextBlock.ForegroundProperty,"TextSecondary");
        void Paint()=>number.Text=(Math.Abs(settings.LyricOffsetSeconds)<1000000 ? settings.LyricOffsetSeconds.ToString("+0.0;-0.0;0.0") : settings.LyricOffsetSeconds.ToString("G4"))+" s";
        void Change(double next)
        {
            var old=settings.LyricOffsetSeconds;if(!double.IsFinite(next) || next==old)return;
            settings.LyricOffsetSeconds=next;
            try{save();feedback.Visibility=Visibility.Collapsed;}
            catch(Exception error){settings.LyricOffsetSeconds=old;feedback.Text="歌词偏移未保存："+error.Message;feedback.Visibility=Visibility.Visible;}
            Paint();
        }
        System.Windows.Automation.AutomationProperties.SetName(minus,"减小歌词偏移");System.Windows.Automation.AutomationProperties.SetName(plus,"增大歌词偏移");System.Windows.Automation.AutomationProperties.SetName(reset,"还原歌词偏移");
        minus.Click+=(_,_)=>Change(Math.Round(settings.LyricOffsetSeconds-.1,1));plus.Click+=(_,_)=>Change(Math.Round(settings.LyricOffsetSeconds+.1,1));reset.Click+=(_,_)=>Change(0);
        actions.Children.Add(minus);actions.Children.Add(number);actions.Children.Add(plus);actions.Children.Add(reset);panel.Children.Add(row);panel.Children.Add(feedback);refreshPreferences.Add(Paint);Paint();
    }
}

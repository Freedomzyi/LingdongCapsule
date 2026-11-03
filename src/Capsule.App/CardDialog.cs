using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capsule.App;

internal static class CardDialog
{
    internal static string? Show(string title,string description,string accept,string? initial=null,bool destructive=false)
    {
        var window=new Window{Title=title,Width=330,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen,FontFamily=Design.UserFont};
        var panel=new StackPanel{Margin=new Thickness(6)};
        var heading=new TextBlock{Text=title,FontSize=19,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,8,0,8)};Design.Bind(heading,TextBlock.ForegroundProperty,"TextPrimary");panel.Children.Add(heading);
        var detail=new TextBlock{Text=description,FontSize=13,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,0,0,16)};Design.Bind(detail,TextBlock.ForegroundProperty,"TextSecondary");panel.Children.Add(new ScrollViewer{Content=detail,MaxHeight=Math.Max(80,Math.Min(300,SystemParameters.WorkArea.Height-220)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        TextBox? input=null;
        if(initial!=null)
        {
            input=new TextBox{Text=initial,MaxLength=80,BorderThickness=new Thickness(0),Background=Brushes.Transparent,FontSize=15,Padding=new Thickness(10,8,10,8)};Design.Bind(input,Control.ForegroundProperty,"TextPrimary");
            var field=new Border{Child=input,CornerRadius=new CornerRadius(9)};Design.Bind(field,Border.BackgroundProperty,"ButtonFill");panel.Children.Add(field);
        }
        var actions=new Grid{Margin=new Thickness(0,18,0,2)};actions.ColumnDefinitions.Add(new());actions.ColumnDefinitions.Add(new());
        var cancel=new Button{Content="取消",Height=38,Margin=new Thickness(0,0,4,0),IsCancel=true};cancel.Click+=(_,_)=>window.DialogResult=false;actions.Children.Add(cancel);
        var confirm=new Button{Content=accept,Height=38,Margin=new Thickness(4,0,0,0),IsDefault=!destructive,Foreground=Brushes.White,Background=new SolidColorBrush(destructive ? Color.FromRgb(190,48,60) : Color.FromRgb(0,122,255))};
        Grid.SetColumn(confirm,1);actions.Children.Add(confirm);panel.Children.Add(actions);
        confirm.Click+=(_,_)=>window.DialogResult=true;
        if(input!=null){confirm.IsEnabled=input.Text.Trim().Length>0;input.TextChanged+=(_,_)=>confirm.IsEnabled=input.Text.Trim().Length>0;}
        window.Content=Design.Card(panel);
        window.ContentRendered+=(_,_)=>{if(input!=null){input.Focus();input.SelectAll();}else cancel.Focus();};
        return window.ShowDialog()==true ? input?.Text.Trim() ?? "ok" : null;
    }
}

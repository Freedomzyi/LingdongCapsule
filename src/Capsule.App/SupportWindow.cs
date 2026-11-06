using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Capsule.App;

internal sealed class SupportWindow : Window
{
    internal const string ImageResource="pack://application:,,,/LingdongCapsule;component/Assets/support-wechat.png";
    internal BitmapImage PaymentImage{get;}
    internal SupportWindow()
    {
        Title="赞赏支持";Width=340;Height=Math.Min(390,SystemParameters.WorkArea.Height-32);MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-24);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=Design.UserFont;
        var layout=new Grid{Margin=new Thickness(6)};layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        PaymentImage=new BitmapImage();PaymentImage.BeginInit();PaymentImage.CacheOption=BitmapCacheOption.OnLoad;PaymentImage.UriSource=new Uri(ImageResource);PaymentImage.EndInit();PaymentImage.Freeze();
        var image=new Image{Source=PaymentImage,Stretch=Stretch.Uniform,Margin=new Thickness(0)};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);System.Windows.Automation.AutomationProperties.SetName(image,"作者提供的微信赞赏收款码");layout.Children.Add(new Border{Background=Brushes.White,Padding=new Thickness(12),Child=image,Margin=new Thickness(0,0,0,12)});
        var close=new Button{Content="关闭",Height=38,IsCancel=true,IsDefault=true};close.Click+=(_,_)=>Close();Grid.SetRow(close,1);layout.Children.Add(close);Content=Design.Card(layout);
        ContentRendered+=(_,_)=>close.Focus();
    }
}

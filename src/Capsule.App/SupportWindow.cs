using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Capsule.App;

internal sealed class SupportWindow : Window
{
    internal const string ImageResource="pack://application:,,,/LingdongCapsule;component/Assets/support-wechat.jpg";
    internal BitmapImage PaymentImage{get;}
    internal SupportWindow()
    {
        Title="赞赏支持";Width=340;Height=Math.Min(600,SystemParameters.WorkArea.Height-32);MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-24);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=Design.UserFont;
        var layout=new Grid{Margin=new Thickness(6)};layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new TextBlock{Text="感谢你的支持",FontSize=20,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,6,0,8)};Design.Bind(heading,TextBlock.ForegroundProperty,"TextPrimary");layout.Children.Add(heading);
        var caption=new TextBlock{Text="微信扫码赞赏，金额随意。\n你的鼓励，会让我更有动力继续优化。",FontSize=12,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,LineHeight=19,Margin=new Thickness(0,0,0,12)};Design.Bind(caption,TextBlock.ForegroundProperty,"TextSecondary");Grid.SetRow(caption,1);layout.Children.Add(caption);
        PaymentImage=new BitmapImage();PaymentImage.BeginInit();PaymentImage.CacheOption=BitmapCacheOption.OnLoad;PaymentImage.UriSource=new Uri(ImageResource);PaymentImage.EndInit();PaymentImage.Freeze();
        var image=new Image{Source=PaymentImage,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,0,12)};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);System.Windows.Automation.AutomationProperties.SetName(image,"作者提供的微信赞赏收款码");Grid.SetRow(image,2);layout.Children.Add(image);
        var close=new Button{Content="关闭",Height=38,IsCancel=true,IsDefault=true};close.Click+=(_,_)=>Close();Grid.SetRow(close,3);layout.Children.Add(close);Content=Design.Card(layout);
        ContentRendered+=(_,_)=>close.Focus();
    }
}

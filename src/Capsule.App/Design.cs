using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Capsule.App;

internal static class Design
{
    public static bool IsDark{get;private set;}=true;
    public static FontFamily UserFont{get;private set;}=new("Microsoft YaHei UI");
    public static string FontStatus{get;private set;}="";
    public static void ApplyFont(string family,string file="")
    {
        FontStatus="";
        try{UserFont=string.IsNullOrWhiteSpace(file) ? new(string.IsNullOrWhiteSpace(family) ? "Microsoft YaHei UI" : family) : LocalFont.Load(file);}
        catch(Exception error) when(LocalFont.IsLoadError(error)){UserFont=new("Microsoft YaHei UI");FontStatus="字体暂不可用，已回退系统默认；保留原选择。"+error.Message;}
        System.Windows.Application.Current.Resources["UserFont"]=UserFont;
    }
    public static void ApplySystemTheme()=>ApplyTheme(2);
    public static void ApplyTheme(int mode)
    {
        if(!System.Windows.Application.Current.Resources.Contains("UserFont"))ApplyFont("");
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark=mode==0 || mode==2 && key?.GetValue("AppsUseLightTheme") is int value && value==0;
        IsDark=dark;Set("IslandInk",dark ? "#FFFFFF" : "#1D1D1F");Set("IslandTrack",dark ? "#38FFFFFF" : "#33000000");
        Set("OverviewTile",dark ? "#17FFFFFF" : "#10000000");
        Set("PageFill",dark ? "#1C1C1E" : "#F5F5F7");Set("CardFill",dark ? "#2C2C2E" : "#FFFFFF");Set("TextPrimary",dark ? "#F5F5F7" : "#1D1D1F");Set("TextSecondary",dark ? "#A1A1AA" : "#7A7A82");Set("ButtonFill",dark ? "#3A3A3C" : "#E9E9EF");
    }
    private static void Set(string name,string color)
    {
        var value=(Color)ColorConverter.ConvertFromString(color);
        if(System.Windows.Application.Current.Resources[name] is SolidColorBrush brush && !brush.IsFrozen)brush.Color=value;
        else System.Windows.Application.Current.Resources[name]=new SolidColorBrush(value);
    }
    public static void Bind(FrameworkElement element, DependencyProperty property, string resource) => element.SetResourceReference(property, resource);
    public static Border Card(UIElement content)
    {
        var card=new Border{CornerRadius=new(16),Padding=new(16,10,16,10),Margin=new(0,0,0,12),Child=content};Bind(card,Border.BackgroundProperty,"CardFill");Bind(card,System.Windows.Documents.TextElement.FontFamilyProperty,"UserFont");return card;
    }
    public static void PresentMessage(string title, string message)
    {
        var window = new Window { Title = title, Width = 360, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, FontFamily = new("Microsoft YaHei UI") };
        var panel = new StackPanel { Margin = new(8) };
        var heading = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new(0, 8, 0, 12) }; Bind(heading, TextBlock.ForegroundProperty, "TextPrimary"); panel.Children.Add(heading);
        var body = new TextBlock { Text = message, FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new(0, 0, 0, 20) }; Bind(body, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(body);
        var button = new Button { Content = "知道了", Padding = new(14, 10, 14, 10), Background = new SolidColorBrush(Color.FromRgb(0, 122, 255)), Foreground = Brushes.White, IsDefault = true, IsCancel = true }; button.Click += (_, _) => window.Close(); panel.Children.Add(button);
        window.Content = Card(panel); window.ShowDialog();
    }
}

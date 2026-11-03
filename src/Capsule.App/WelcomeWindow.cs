using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
namespace Capsule.App;

internal sealed class WelcomeWindow : Window
{
    public WelcomeWindow(bool startupEnabled, Action<bool> configureStartup, Action settings, Action complete)
    {
        Title = "欢迎使用灵动胶囊"; Width = 410; Height = Math.Min(610, SystemParameters.WorkArea.Height - 48); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = new("Microsoft YaHei UI"); FontSize = 13;
        Design.Bind(this, ForegroundProperty, "TextPrimary");
        var outer = new Border { CornerRadius = new(24), Padding = new(26) }; Design.Bind(outer, Border.BackgroundProperty, "PageFill");
        var panel = new StackPanel(); outer.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Content = outer;
        panel.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/LingdongCapsule;component/Assets/icon.png")), Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 2, 0, 14) });
        panel.Children.Add(new TextBlock { Text = "欢迎使用灵动胶囊", FontSize = 23, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
        var subtitle = new TextBlock { Text = "时间、音乐与消息，收在屏幕上沿。", TextAlignment = TextAlignment.Center, FontSize = 12, Margin = new(0, 8, 0, 22) }; Design.Bind(subtitle, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(subtitle);
        Add(panel, "01", "看看时间，控制音乐", "点击胶囊展开媒体控制，暂停后回到时钟。");
        Add(panel, "02", "让消息轻轻出现", "系统通知可在设置中授权；点击展开正文，再次点击收起。仅有提示声的微信消息不在接入范围。");
        Add(panel, "03", "需要设置时，去托盘", "日常不占任务栏。右键胶囊或双击托盘图标打开设置，托盘右键可以退出。");
        var startup = new CheckBox { Content = "登录 Windows 后自动启动", IsChecked = startupEnabled, Margin = new(4, 7, 4, 14) }; panel.Children.Add(startup);
        var start = new Button { Content = "开始使用", Padding = new(16, 12, 16, 12), Background = new SolidColorBrush(Color.FromRgb(0, 122, 255)), Foreground = Brushes.White };
        start.Click += (_, _) => { try { configureStartup(startup.IsChecked == true); complete(); Close(); } catch (Exception ex) { Design.PresentMessage("启动设置未保存", ex.Message); } }; panel.Children.Add(start);
        var openSettings = new Button { Content = "先看看设置", Padding = new(12, 10, 12, 10), Margin = new(0, 8, 0, 0) }; openSettings.Click += (_, _) => { complete(); Close(); settings(); }; panel.Children.Add(openSettings);
        Closed += (_, _) => complete();
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }
    private static void Add(StackPanel panel, string number, string title, string description)
    {
        var row = new Grid { Margin = new(0, 0, 0, 16) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(34) }); row.ColumnDefinitions.Add(new());
        row.Children.Add(new TextBlock { Text = number, Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255)), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new(0, 2, 0, 0) });
        var text = new StackPanel(); Grid.SetColumn(text, 1); text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Medium });
        var body = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19, Margin = new(0, 5, 0, 0) }; Design.Bind(body, TextBlock.ForegroundProperty, "TextSecondary"); text.Children.Add(body); row.Children.Add(text); panel.Children.Add(row);
    }
}

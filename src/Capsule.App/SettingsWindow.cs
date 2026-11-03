using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Capsule.App;

public sealed class SettingsWindow : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19 };
    private readonly StackPanel sources = new();
    private readonly Settings settings;
    private readonly Action save;
    private string sourceKey = "__initial__";
    private CheckBox? notificationsCheck;
    private Button? authorizeButton;

    public SettingsWindow(Settings settings, Func<Task<string>> request, Action save, Action test, Action<bool> startup, Action welcome)
    {
        this.settings = settings; this.save = save;
        Title = "灵动胶囊 · 设置"; Width = 420; Height = Math.Min(720, SystemParameters.WorkArea.Height - 48);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 13; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Design.Bind(this, ForegroundProperty, "TextPrimary");
        var outer = new Border { CornerRadius = new(22), BorderBrush = new SolidColorBrush(Color.FromArgb(35, 128, 128, 128)), BorderThickness = new(1) };
        Design.Bind(outer, Border.BackgroundProperty, "PageFill"); Content = outer;
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new()); outer.Child = layout;
        var header = new Grid { Margin = new(24, 20, 16, 12) }; header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel(); brand.Children.Add(new TextBlock { Text = "灵动胶囊", FontSize = 25, FontWeight = FontWeights.SemiBold });
        var subtitle = new TextBlock { Text = "让重要的信息，轻轻出现。", FontSize = 12, Margin = new(0, 5, 0, 0) }; Design.Bind(subtitle, TextBlock.ForegroundProperty, "TextSecondary"); brand.Children.Add(subtitle); header.Children.Add(brand);
        var close = new Button { Content = "×", Width = 30, Height = 30, FontSize = 20, VerticalAlignment = VerticalAlignment.Top, ToolTip = "关闭设置" }; close.Click += (_, _) => Close(); Grid.SetColumn(close, 1); header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button && e.LeftButton == MouseButtonState.Pressed) DragMove(); }; layout.Children.Add(header);
        var panel = new StackPanel { Margin = new(24, 0, 24, 20) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); layout.Children.Add(scroll);

        Heading(panel, "外观与动效");
        var appearance = new StackPanel();
        AddSlider(appearance, "播放与通知宽度", 220, 480, settings.Width, v => settings.Width = v, "逻辑像素；播放固定宽度，通知不会超过此值。");
        AddSlider(appearance, "折叠高度", 32, 60, settings.CompactHeight, v => settings.CompactHeight = v, "逻辑像素；默认 35，控制时钟与紧凑媒体高度，不压缩展开内容。", 1);
        AddSlider(appearance, "收放时长", 150, 900, settings.MotionMs, v => settings.MotionMs = (int)v, "毫秒；统一控制展开和收起，数值越大越舒缓。");
        panel.Children.Add(Design.Card(appearance));
        var switches = new StackPanel();
        AddCheck(switches, "截图中显示胶囊", settings.ShowInCapture, v => settings.ShowInCapture = v, "关闭后请求 Windows 从兼容截图和录屏中排除主岛体；设置窗口不在此范围。");
        AddCheck(switches, "通知隐私预览", settings.PrivatePreview, v => settings.PrivatePreview = v, "展开前仅显示应用名与新消息提示，通知正文不写入磁盘。");
        notificationsCheck = AddCheck(switches, "系统通知", settings.NotificationsEnabled, v => settings.NotificationsEnabled = v, "只接收 Windows 通知中心可提供的通知，不修改系统横幅策略。");
        AddCheck(switches, "开机启动", StartupService.IsEnabled, startup, "登录当前 Windows 用户后自动启动，只驻留系统托盘；不需要管理员权限。");
        panel.Children.Add(Design.Card(switches));

        Heading(panel, "连接与体验");
        var connection = new StackPanel();
        Design.Bind(status, TextBlock.ForegroundProperty, "TextSecondary"); connection.Children.Add(status);
        var authorize = new Button { Content = "授权系统通知", Padding = new(12, 10, 12, 10), Margin = new(0, 12, 0, 8), Background = new SolidColorBrush(Color.FromRgb(0, 122, 255)), Foreground = Brushes.White };
        authorizeButton = authorize;
        authorize.Click += async (_, _) => { authorize.IsEnabled = false; try { status.Text = await request(); } finally { authorize.IsEnabled = true; } };
        connection.Children.Add(authorize);
        var testButton = new Button { Content = "试一条通知", Padding = new(12, 10, 12, 10) }; testButton.Click += (_, _) => test(); connection.Children.Add(testButton);
        var guide = new Button { Content = "使用指南", Padding = new(12, 10, 12, 10), Margin = new(0, 8, 0, 0) }; guide.Click += (_, _) => welcome(); connection.Children.Add(guide);
        panel.Children.Add(Design.Card(connection));

        Heading(panel, "通知来源");
        var help = new TextBlock { Text = "已发现的应用显示在这里。关闭开关即可静音该来源。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 0, 0, 10) }; Design.Bind(help, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(help);
        panel.Children.Add(Design.Card(sources));
        var footer = new TextBlock { Text = "0.1.0 预览版  ·  Windows 桌面交互胶囊", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 6, 0, 0) }; Design.Bind(footer, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(footer);
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    private static void Heading(StackPanel panel, string text)
    {
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, Margin = new(4, 12, 0, 8) }; Design.Bind(label, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(label);
    }
    private static StackPanel Label(string text, string help)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var info = new TextBlock { Text = "ⓘ", FontSize = 12, Margin = new(6, 0, 0, 0), ToolTip = help, VerticalAlignment = VerticalAlignment.Center }; Design.Bind(info, TextBlock.ForegroundProperty, "TextSecondary"); row.Children.Add(info); return row;
    }
    private void AddSlider(StackPanel panel, string title, double min, double max, double value, Action<double> changed, string help, double step = 10)
    {
        var row = new Grid { Margin = new(0, 9, 0, 0) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(Label(title, help));
        var number = new TextBlock { Text = value.ToString("0"), FontSize = 13, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255)) }; Grid.SetColumn(number, 1); row.Children.Add(number); panel.Children.Add(row);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new(0, 4, 0, 7), ToolTip = help };
        slider.ValueChanged += (_, _) => { changed(slider.Value); number.Text = slider.Value.ToString("0"); save(); }; panel.Children.Add(slider);
    }
    private CheckBox AddCheck(StackPanel panel, string text, bool value, Action<bool> changed, string help)
    {
        var check = new CheckBox { Content = Label(text, help), IsChecked = value, Margin = new(0, 4, 0, 4), ToolTip = help }; System.Windows.Automation.AutomationProperties.SetName(check, text);
        check.Click += (_, _) => { changed(check.IsChecked == true); save(); }; panel.Children.Add(check); return check;
    }
    public void UpdateStatus(string media, string notification, Dictionary<string, string> apps)
    {
        status.Text = media + "\n" + notification; if (notificationsCheck is not null) notificationsCheck.IsChecked = settings.NotificationsEnabled;
        if (authorizeButton is not null) authorizeButton.Visibility = notification.Contains("正常") || notification.Contains("已授权") ? Visibility.Collapsed : Visibility.Visible;
        var key = string.Join("|", apps.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value)); if (key == sourceKey) return; sourceKey = key; sources.Children.Clear();
        foreach (var app in apps.OrderBy(x => x.Value))
        {
            var id = app.Key; var label = new TextBlock { Text = app.Value, MaxWidth = 238, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var check = new CheckBox { Content = label, IsChecked = !settings.MutedSources.Contains(id), Margin = new(0, 4, 0, 4), ToolTip = app.Value }; System.Windows.Automation.AutomationProperties.SetName(check, app.Value);
            check.Click += (_, _) => { if (check.IsChecked == true) settings.MutedSources.Remove(id); else settings.MutedSources.Add(id); save(); }; sources.Children.Add(check);
        }
        if (apps.Count == 0) { var empty = new TextBlock { Text = "收到系统通知后，来源会自动出现在这里。", TextWrapping = TextWrapping.Wrap, FontSize = 12 }; Design.Bind(empty, TextBlock.ForegroundProperty, "TextSecondary"); sources.Children.Add(empty); }
    }
}

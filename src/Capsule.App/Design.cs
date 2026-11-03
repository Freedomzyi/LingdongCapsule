using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Capsule.App;

internal static class Design
{
    public static void ApplySystemTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        if (key?.GetValue("AppsUseLightTheme") is not int value || value != 0) return;
        Set("PageFill", "#1C1C1E"); Set("CardFill", "#2C2C2E"); Set("TextPrimary", "#F5F5F7"); Set("TextSecondary", "#A1A1AA"); Set("ButtonFill", "#3A3A3C");
    }
    private static void Set(string name, string color) => System.Windows.Application.Current.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    public static void Bind(FrameworkElement element, DependencyProperty property, string resource) => element.SetResourceReference(property, resource);
    public static Border Card(UIElement content) => new() { Background = (Brush)System.Windows.Application.Current.FindResource("CardFill"), CornerRadius = new(16), Padding = new(16, 10, 16, 10), Margin = new(0, 0, 0, 12), Child = content };
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

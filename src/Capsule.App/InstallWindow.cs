using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Capsule.App;

internal sealed class InstallWindow : Window
{
    public InstallWindow()
    {
        Title = "安装灵动胶囊";
        Width = Math.Min(420, Math.Max(160, SystemParameters.WorkArea.Width - 24));
        MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height - 24);
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        Design.Bind(this, ForegroundProperty, "TextPrimary");
        var layout = new Grid { Margin = new Thickness(14) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var panel = new StackPanel();
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(scroll);
        panel.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/LingdongCapsule;component/Assets/icon.png")), Width = 64, Height = 64, Margin = new Thickness(0, 8, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "安装灵动胶囊", FontSize = 23, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
        var detail = new TextBlock { Text = "安装到当前用户，无需管理员权限。\n完成后展示使用引导，日常仅驻留系统托盘。", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 21, Margin = new Thickness(0, 12, 0, 20) };
        Design.Bind(detail, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(detail);
        var install = new Button { Content = "安装并打开", Padding = new Thickness(14, 12, 14, 12), Background = new SolidColorBrush(Color.FromRgb(0, 122, 255)), Foreground = Brushes.White };
        var actions = new StackPanel(); Grid.SetRow(actions, 1); layout.Children.Add(actions);
        actions.Children.Add(install);
        var cancel = new Button { Content = "暂不安装", Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 6) };
        cancel.Click += (_, _) => Close(); actions.Children.Add(cancel);
        install.Click += async (_, _) =>
        {
            install.IsEnabled = cancel.IsEnabled = false; install.Content = "正在安装…";
            try
            {
                var script = Path.Combine(AppContext.BaseDirectory, "Install.ps1");
                if (!File.Exists(script)) throw new IOException("请完整解压发布包后安装。");
                var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("无法启动安装程序。");
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(); await output;
                if (process.ExitCode != 0) throw new IOException(await error);
                Close();
            }
            catch (Exception ex) { Design.PresentMessage("安装未完成", ex.Message); install.IsEnabled = cancel.IsEnabled = true; install.Content = "重试安装"; }
        };
        Content = Design.Card(layout);
    }
}

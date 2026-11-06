using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capsule.App;

internal static class InstallWindowTests
{
    internal static async Task RunAsync()
    {
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;Design.ApplyTheme(0);
        foreach(var size in new[]{(420d,600d),(320d,240d),(240d,200d)})
        {
            var window=new MaintenanceWindow{Width=size.Item1,Height=size.Item2,MaxHeight=size.Item2};window.Show();await Task.Delay(100);window.UpdateLayout();
            try
            {
                var buttons=Children<Button>(window).ToArray();
                Console.WriteLine("Installer viewport "+size+" buttons: "+string.Join(" | ",buttons.Select(b=>b.Content?.ToString())));
                foreach(var text in new[]{"开始安装","取消"})
                {
                    var button=buttons.Single(b=>Equals(b.Content,text));
                    var bounds=button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                    if(bounds.Top<0 || bounds.Bottom>window.ActualHeight+1 || bounds.Left<0 || bounds.Right>window.ActualWidth+1 || button.ActualHeight<25)throw new Exception("Installer action clipped at "+size+": "+text);
                }
                if(size.Item2<300)
                {
                    var scroll=Children<ScrollViewer>(window).First();
                    if(scroll.ScrollableHeight<=0 || scroll.ViewportHeight<=0)throw new Exception("Short installer cannot scroll description");
                    scroll.ScrollToEnd();window.UpdateLayout();
                    if(scroll.VerticalOffset<=0)throw new Exception("Installer description did not scroll");
                }
                var image=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                var folder=System.IO.Path.GetFullPath("artifacts/install-viewport-tests");System.IO.Directory.CreateDirectory(folder);
                using(var output=System.IO.File.Create(System.IO.Path.Combine(folder,$"{size.Item1}-{size.Item2}.png")))encoder.Save(output);
                buttons.Single(b=>Equals(b.Content,"取消")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(window.IsVisible)throw new Exception("Cancel failed to close own installer");
            }
            finally{window.Close();}
        }
        Console.WriteLine("PASS installer default/short/narrow viewports keep actions visible, description scrollable and cancel usable; installation was not invoked");
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var descendant in Children<T>(child))yield return descendant;}
    }
}

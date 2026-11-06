using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capsule.App;

internal static class MaintenanceTests
{
    internal static async Task RunAsync()
    {
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var root=Path.GetFullPath(Path.Combine("artifacts","maintenance-ui-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        if(MaintenanceProgress.Parse("CAPSULE_PROGRESS|copying|5|10") is not {Completed:5,Total:10} || MaintenanceProgress.Parse("CAPSULE_PROGRESS|copying|11|10")!=null || MaintenanceProgress.Parse("private log")!=null)throw new Exception("Progress protocol validation failed");
        foreach(var theme in new[]{0,1})
        {
            Design.ApplyTheme(theme);var calls=0;var wait=new TaskCompletionSource();string[]? arguments=null;
            Task Run(string script,string[] args,IProgress<MaintenanceProgress> progress){calls++;arguments=args;progress.Report(new("copying",3,10));return calls==1 ? Task.FromException(new IOException("Owned permission refusal")) : wait.Task;}
            var window=new MaintenanceWindow(directory:root,runner:Run){ShowActivated=false};
            Button Find(string text)=>Descendants<Button>(window).Single(button=>Equals(button.Content,text));
            void Click(string text)=>Find(text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                window.Show();await Task.Delay(60);Capture(window,Path.Combine(root,$"welcome-{theme}.png"));Click("开始安装");
                if(window.State!=MaintenanceState.Location)throw new Exception("Welcome did not navigate to location");
                Capture(window,Path.Combine(root,$"location-{theme}.png"));Click("安装");await Task.Delay(40);
                if(window.State!=MaintenanceState.Failed || calls!=1)throw new Exception("Deployment failure did not retain recoverable UI");
                Capture(window,Path.Combine(root,$"failed-{theme}.png"));Click("重试");await Task.Delay(40);
                if(window.State!=MaintenanceState.Working || arguments==null || !arguments.Contains("-NoLaunch") || !arguments.Contains("-ReportProgress"))throw new Exception("Retry did not use controlled deployment arguments");
                window.Close();if(!window.IsVisible)throw new Exception("In-progress close escaped transaction guard");
                Capture(window,Path.Combine(root,$"working-{theme}.png"));wait.SetResult();await Task.Delay(60);
                if(window.State!=MaintenanceState.Completed || calls!=2)throw new Exception("Deployment did not reach completion exactly once");
                Capture(window,Path.Combine(root,$"completed-{theme}.png"));window.Height=270;window.UpdateLayout();var finish=Find("打开灵动胶囊");var bounds=finish.TransformToAncestor(window).TransformBounds(new Rect(finish.RenderSize));if(bounds.Top<0 || bounds.Bottom>window.ActualHeight)throw new Exception("Short maintenance window hides completion action");
            }
            finally{wait.TrySetResult();window.Close();}
            var removal=new MaintenanceWindow(true,root,(_,_,_)=>Task.CompletedTask){ShowActivated=false};
            try{removal.Show();await Task.Delay(40);var accept=Descendants<Button>(removal).Single(button=>Equals(button.Content,"确认卸载"));accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(40);if(removal.State!=MaintenanceState.Completed)throw new Exception("Uninstall result page missing");Capture(removal,Path.Combine(root,$"uninstalled-{theme}.png"));}finally{removal.Close();}
        }
        Console.WriteLine("PASS maintenance welcome/location, failure/retry, progress, close guard, completion, uninstall result, short-window actions and both themes; mocked file worker only; evidence: "+root);
    }
    private static void Capture(Window view,string path){view.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(view);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(view.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);png.Save(output);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

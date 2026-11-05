using Capsule.App;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class AuthorizationTests
{
    internal static async Task RunAsync(App app)
    {
        var settings=new Settings{NotificationsEnabled=false};using var shortcuts=new GlobalShortcuts(settings);
        var fail=true;var unhandled=false;var errorShown=false;TaskCompletionSource<string>? pending=null;
        var page=new SettingsWindow(settings,()=>pending?.Task ?? (fail ? Task.FromException<string>(new IOException("Owned notification request refusal")) : Task.FromResult("Owned retry succeeded")),()=>{},()=>{},_=>{},()=>{},()=>new Dictionary<string,string>(),shortcuts,_=>{},()=>[],_=>{},_=>{},()=>[],_=>{},(_,_)=>false,()=>[],(_,_)=>{},()=>false){ShowActivated=false};
        void OnUnhandled(object sender,DispatcherUnhandledExceptionEventArgs error){if(error.Exception is IOException && error.Exception.Message=="Owned notification request refusal"){unhandled=true;error.Handled=true;}}
        app.DispatcherUnhandledException+=OnUnhandled;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>{foreach(var dialog in app.Windows.OfType<Window>().Where(item=>item.Title=="通知授权未完成").ToArray()){errorShown=true;dialog.Close();}};
        try
        {
            page.Show();page.SelectPage("通用设置");await Task.Delay(80);
            var button=Descendants<Button>(page).Single(item=>Equals(item.Content,"授权系统通知"));timer.Start();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(250);
            if(unhandled || !errorShown || !button.IsEnabled || !page.IsVisible)throw new Exception("Notification authorization refusal escaped UI boundary or prevented retry");
            fail=false;button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
            if(!button.IsEnabled || !Descendants<TextBlock>(page).Any(item=>item.Text=="Owned retry succeeded"))throw new Exception("Notification authorization did not recover on retry");
            pending=new();errorShown=false;button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));page.Close();
            pending.SetException(new IOException("Owned notification request refusal"));await Task.Delay(100);
            if(unhandled || errorShown)throw new Exception("Closed settings page surfaced late permission error or unhandled exception");
            Console.WriteLine("PASS actual notification authorization refusal is contained, provides feedback and allows retry; no OS permission request made");
        }
        finally{timer.Stop();app.DispatcherUnhandledException-=OnUnhandled;if(page.IsVisible)page.Close();}
        MessageLayout(app);
        await DisabledFeedbackAsync();
        await PersistenceAsync();
    }
    private static async Task DisabledFeedbackAsync()
    {
        var button=new Button{Content="Owned action",Width=150,Height=36};
        var host=new Window{Content=button,Width=200,Height=100,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            host.Show();await Task.Delay(50);button.ApplyTemplate();
            var surface=(FrameworkElement)button.Template.FindName("Surface",button);
            surface.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(1,1,TimeSpan.FromMilliseconds(100)));
            await Task.Delay(50);button.IsEnabled=false;await Task.Delay(100);
            var effective=surface.Opacity*button.Opacity;
            if(effective>.41)throw new Exception("Held hover/press opacity animation overrides disabled action feedback");
            button.IsEnabled=true;await Task.Delay(30);
            if(surface.Opacity*button.Opacity<.8)throw new Exception("Re-enabled action remained visually disabled");
            Console.WriteLine("PASS disabled action remains visibly dimmed despite retained hover animation and recovers on re-enable");
        }
        finally{host.Close();}
    }    private static void MessageLayout(App app)
    {
        foreach(var theme in new[]{0,1})
        {
            Design.ApplyTheme(theme);var observed=false;var fits=false;
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
            timer.Tick+=(_,_)=>
            {
                var dialog=app.Windows.OfType<Window>().SingleOrDefault(item=>item.Title=="Owned bounded error");
                if(dialog==null || !dialog.IsLoaded || dialog.ActualHeight<=0)return;
                dialog.UpdateLayout();var button=Descendants<Button>(dialog).Single(item=>Equals(item.Content,"知道了"));
                var bounds=button.TransformToAncestor(dialog).TransformBounds(new Rect(button.RenderSize));
                observed=true;fits=dialog.ActualHeight<=SystemParameters.WorkArea.Height && bounds.Top>=0 && bounds.Bottom<=dialog.ActualHeight && !dialog.ShowInTaskbar && Descendants<ScrollViewer>(dialog).Any(item=>item.ScrollableHeight>0);
                dialog.Close();
            };
            timer.Start();try{Design.PresentMessage("Owned bounded error",string.Join('\n',Enumerable.Repeat("Owned generated error detail for layout validation.",120)));}finally{timer.Stop();}
            if(!observed || !fits)throw new Exception("Long error dialog hides acknowledgement, overflows work area or lacks scroll boundary");
        }
        Design.ApplyTheme(0);
        Console.WriteLine("PASS long error feedback retains visible acknowledgement and scrolling in dark/light theme without extra taskbar window");
    }    private static async Task PersistenceAsync()
    {
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","authorization-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var settings=new Settings{NotificationsEnabled=false,MediaEnabled=false,ThemeMode=0};var file=Path.Combine(root,"settings.json");settings.SaveTo(file);
        var fail=true;var writes=0;
        var main=new MainWindow(suppressOnboarding:true,initialSettings:settings,localMspDirectory:Path.Combine(root,"nodes"),localPluginDirectory:Path.Combine(root,"plugins"),pluginSettingsWriter:()=>{writes++;if(fail)throw new IOException("Owned notification save refusal");settings.SaveTo(file);}){ShowActivated=false};
        try
        {
            main.Show();await Task.Delay(80);var refused=false;
            try{await main.AuthorizeNotificationsAsync(()=>Task.FromResult(true));}catch(IOException){refused=true;}
            if(!refused || settings.NotificationsEnabled || Settings.LoadFrom(file).NotificationsEnabled || writes!=1)throw new Exception("Notification save refusal did not roll back preference and preserve disk");
            fail=false;await main.AuthorizeNotificationsAsync(()=>Task.FromResult(false));
            if(settings.NotificationsEnabled || Settings.LoadFrom(file).NotificationsEnabled || writes!=2)throw new Exception("Notification save retry did not persist denied choice");
            var denied=false;try{await main.AuthorizeNotificationsAsync(()=>Task.FromException<bool>(new UnauthorizedAccessException("Owned permission refusal")));}catch(UnauthorizedAccessException){denied=true;}
            if(!denied || settings.NotificationsEnabled || writes!=2)throw new Exception("Permission error wrote notification preferences");
            Console.WriteLine("PASS actual main notification permission/save failures preserve preference and disk; retry succeeds with injected permission service, no OS permission changes; evidence: "+root);
        }
        finally{main.Close();}
    }    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
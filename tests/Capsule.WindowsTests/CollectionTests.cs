using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capsule.App;
using Capsule.PluginApi;

internal static class CollectionTests
{
    private sealed class Host : ICapsulePluginHost
    {
        internal Dictionary<string,string> Values=[];
        internal int Notifications;
        internal bool Fail;
        public PluginMedia CurrentMedia=>new("","",false,false);
        public string ReadSetting(string key,string fallback="")=>Values.GetValueOrDefault(key,fallback);
        public void WriteSetting(string key,string value){if(Fail)throw new IOException("Owned preference refusal");Values[key]=value;}
        public Task<bool> NotifyAsync(string title,string body){Notifications++;return Task.FromResult(true);}
    }
    internal static async Task RunAsync()
    {
        var root=Path.GetFullPath(Path.Combine("artifacts","collection-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        using var focus=new Capsule.FocusTimer.FocusTimer();using var day=new Capsule.DayProgress.DayProgress();var host=new Host();
        var focusView=focus.CreateWidget(host);var dayView=day.CreateWidget(host);
        var frame=new PluginFrame(DateTimeOffset.UtcNow,true,host.CurrentMedia);focus.Update(frame);
        void Click()=>focusView.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
        string FocusText()=>AutomationProperties.GetName(focusView);
        if(!FocusText().Contains("25:00"))throw new Exception("Focus default missing");
        Click();if(!FocusText().Contains("专注中"))throw new Exception("Focus did not start");
        Click();var paused=FocusText();focus.Update(frame with{Now=frame.Now.AddHours(1)});if(FocusText()!=paused || host.Notifications!=0)throw new Exception("Paused focus advanced or notified");
        Click();focus.Update(frame with{Now=DateTimeOffset.UtcNow.AddMinutes(26)});focus.Update(frame with{Now=DateTimeOffset.UtcNow.AddMinutes(27)});
        if(host.Notifications!=1 || !FocusText().Contains("已完成"))throw new Exception("Focus completion was lost or repeated");
        var duration=focusView.ContextMenu!.Items.OfType<MenuItem>().Single(item=>item.Header.ToString()!.Contains("45"));
        host.Fail=true;duration.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));focus.Update(frame);
        if(host.Values.Count!=0 || !focusView.ToolTip.ToString()!.Contains("未能保存"))throw new Exception("Failed focus preference was committed or hidden");
        host.Fail=false;duration.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));if(host.Values["minutes"]!="45" || !FocusText().Contains("45:00"))throw new Exception("Focus preference retry failed");
        day.Update(frame with{Now=new DateTimeOffset(2026,11,6,12,0,0,TimeSpan.FromHours(8))});if(!AutomationProperties.GetName(dayView).Contains("50%"))throw new Exception("Day progress not based on local day");
        day.Update(frame with{Now=new DateTimeOffset(2026,11,7,0,0,0,TimeSpan.FromHours(8))});if(!AutomationProperties.GetName(dayView).Contains("0%"))throw new Exception("Day progress did not roll over");
        var widgets=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(18)};widgets.Children.Add(focusView);widgets.Children.Add(dayView);
        var preview=new Window{Content=widgets,Width=270,Height=90,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        try
        {
            preview.Show();await Task.Delay(70);
            foreach(var dark in new[]{true,false}){focus.Update(frame with{DarkTheme=dark});day.Update(frame with{DarkTheme=dark});preview.Background=dark ? new SolidColorBrush(Color.FromRgb(16,16,18)) : Brushes.White;Capture(preview,Path.Combine(root,dark ? "widgets-dark.png" : "widgets-light.png"));}
        }
        finally{preview.Close();}
        focus.Dispose();day.Dispose();focus.Update(frame);day.Update(frame);if(focusView.ContextMenu!=null || host.Notifications!=1)throw new Exception("Stopped widget retained its menu or emitted a late notification");
        Console.WriteLine("PASS real widget drawing; focus start/pause/one-shot completion, preference rollback/retry, day rollover, dark/light, accessibility and dispose; simulated frame times only");
        await SettingsAndPackages(root);
        Console.WriteLine("Collection evidence: "+root);
    }
    private static async Task SettingsAndPackages(string root)
    {
        var settings=new Settings{MediaEnabled=false,NotificationsEnabled=false,PrivatePreview=true,Width=250,FontName="Microsoft YaHei UI"};var fail=false;var writes=0;
        void Persist(){if(fail)throw new IOException("Owned preset save refusal");writes++;settings.SaveTo(Path.Combine(root,"settings.json"));}
        var island=new MainWindow(true,settings,localPluginDirectory:Path.Combine(root,"plugins"),pluginSettingsWriter:Persist);
        using var shortcuts=new GlobalShortcuts(settings);
        var page=new SettingsWindow(settings,()=>Task.FromResult(""),Persist,()=>{},_=>{},()=>{},()=>new Dictionary<string,string>(),shortcuts,file=>island.LoadPlugin(file),Active,island.DisablePlugin,island.UninstallPlugin,island.InstalledPlugins,island.RemoveInstalledPlugin,island.InstallMarketPackage,island.DisplayComponents,island.MoveDisplayComponent,startupState:()=>false,bundledComponentDirectory:Path.GetFullPath("artifacts/plugins")){ShowActivated=false};
        // Read canonical IDs rather than deriving identity from rendered text.
        string[] Active()=>island.DisplayComponents().Where(component=>component.Id.StartsWith("plugin:")).Select(component=>component.Id[7..]).ToArray();
        try
        {
            island.Show();page.Show();await Task.Delay(200);
            foreach(var preset in AppearancePreset.All)
            {
                var button=Descendants<Button>(page).Single(item=>Equals(item.Tag,"theme:"+preset.Id));button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!preset.Matches(settings) || settings.Width!=250 || settings.FontName!="Microsoft YaHei UI" || !settings.PrivatePreview)throw new Exception("Preset did not apply or changed unrelated preferences");
                if(!preset.Matches(Settings.LoadFrom(Path.Combine(root,"settings.json"))))throw new Exception("Preset was not persisted");
                Capture(page,Path.Combine(root,"theme-"+preset.Id+".png"));
            }
            var previous=(settings.ThemeMode,settings.MaterialKind,settings.BackgroundOpacity);var before=writes;var feedback=false;
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
            timer.Tick+=(_,_)=>{foreach(var dialog in Application.Current.Windows.OfType<Window>().Where(item=>item.Title=="未能更新设置").ToArray()){feedback=true;dialog.Close();}};
            fail=true;timer.Start();try{Descendants<Button>(page).Single(item=>Equals(item.Tag,"theme:moonlight")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}finally{timer.Stop();fail=false;}
            if(!feedback || writes!=before || previous!=(settings.ThemeMode,settings.MaterialKind,settings.BackgroundOpacity))throw new Exception("Preset save refusal failed to roll back");
            page.SelectPage("我的插件");await Task.Delay(80);
            foreach(var id in new[]{"capsule.focustimer","capsule.dayprogress"})
            {
                var add=Descendants<Button>(page).Single(item=>Equals(item.Tag,"bundled:"+id));if(!add.IsEnabled)throw new Exception("Bundled component not available offline");add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!Active().Contains(id) || !island.InstalledPlugins().Any(item=>item.Id==id && item.Enabled))throw new Exception("Offline component did not install and activate");
            }
            await Task.Delay(1100);Capture(page,Path.Combine(root,"component-collection.png"));Capture(island,Path.Combine(root,"compact-components.png"));
            foreach(var widget in island.LivePluginWidgets){var rect=widget.TransformToAncestor(island).TransformBounds(new Rect(widget.RenderSize));if(rect.Top<0 || rect.Bottom>island.ActualHeight+.5 || rect.Right>island.ActualWidth+.5)throw new Exception("Bundled widgets clipped in the standard-height 250px island");}
            var focusWidget=island.LivePluginWidgets.Single(widget=>AutomationProperties.GetName(widget).Contains("专注"));var windowCount=Application.Current.Windows.Count;
            var menuClick=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Right){RoutedEvent=UIElement.MouseRightButtonUpEvent};focusWidget.RaiseEvent(menuClick);
            if(!menuClick.Handled || focusWidget.ContextMenu?.IsOpen!=true || Application.Current.Windows.Count!=windowCount)throw new Exception("Focus context menu leaked into island settings");focusWidget.ContextMenu.IsOpen=false;
            focusWidget.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});if(!AutomationProperties.GetName(focusWidget).Contains("专注中"))throw new Exception("Actual island widget did not start focus");
            foreach(var id in Active())
            {
                var button=Descendants<Button>(page).Single(item=>Equals(item.Tag,"bundled:"+id));var count=island.InstalledPlugins().Length;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(Active().Contains(id))throw new Exception("Bundled component did not stop from its canonical card");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!Active().Contains(id) || island.InstalledPlugins().Length!=count)throw new Exception("Component re-enable reinstalled or failed");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Console.WriteLine("PASS actual settings theme cards and save rollback, preserved layout/privacy/font; offline two-component package installation, live activation and stop");
        }
        finally{page.Close();island.Close();}
    }
    private static void Capture(Window window,string path){window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

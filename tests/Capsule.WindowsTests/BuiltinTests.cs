using Capsule.App;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class BuiltinTests
{
    internal static async Task RunAsync()
    {
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","builtin-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var path=Path.Combine(root,"settings.json");
        var settings=new Settings{MediaEnabled=false,NotificationsEnabled=false,ClockLayout=1,ShowTomorrow=false,ComponentOrder=["hardware","clock","media"]};settings.SaveTo(path);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var window=new MainWindow(suppressOnboarding:true,initialSettings:settings){ShowActivated=false};
        var render=typeof(MainWindow).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
        void Save(){settings.SaveTo(path);render.Invoke(window,null);}
        var page=new SettingsWindow(settings,()=>Task.FromResult("Owned test"),Save,()=>{},_=>{},()=>{},()=>new Dictionary<string,string>(),window.LiveShortcuts,file=>window.LoadPlugin(file),()=>[],window.DisablePlugin,window.UninstallPlugin,window.InstalledPlugins,window.RemoveInstalledPlugin,window.InstallMarketPackage,window.DisplayComponents,window.MoveDisplayComponent){ShowActivated=false};
        try
        {
            window.Show();page.Show();await Task.Delay(500);
            Button Tab(string name)=>Descendants<Button>(page).Single(button=>button.Content as string==name);
            Tab("我的插件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);page.UpdateLayout();
            var checks=Descendants<CheckBox>(page).Where(check=>check.Tag is string tag && tag.StartsWith("builtin:")).ToArray();
            if(checks.Length!=2)throw new Exception("Built-in components do not have exactly two canonical manager controls");
            var clock=checks.Single(check=>Equals(check.Tag,"builtin:centerclock"));var overview=checks.Single(check=>Equals(check.Tag,"builtin:idleoverview"));
            void Toggle(CheckBox check,bool active){check.IsChecked=active;check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
            var image=new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth),(int)Math.Ceiling(page.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(page);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(root,"builtin-manager.png")))png.Save(output);
            Toggle(clock,false);
            if(settings.ClockEnabled || window.LiveModuleOrder.Contains("clock") || Settings.LoadFrom(path).ClockEnabled)throw new Exception("Stopping built-in clock did not update island and saved state");
            Toggle(clock,true);
            if(!window.LiveModuleOrder.Contains("clock") || settings.ClockLayout!=1 || !Settings.LoadFrom(path).ComponentOrder.SequenceEqual(new[]{"hardware","clock","media"}))throw new Exception("Clock re-enable lost style or saved slot");
            window.OpenOverview();if(((FrameworkElement)window.FindName("OverviewHost")).Visibility!=Visibility.Visible)throw new Exception("Enabled built-in overview cannot open");
            settings.WeatherCity="保留城市";Toggle(overview,false);window.OpenOverview();
            var stopped=Settings.LoadFrom(path);
            if(((FrameworkElement)window.FindName("OverviewHost")).Visibility!=Visibility.Collapsed || stopped.OverviewEnabled || stopped.WeatherCity!="保留城市" || stopped.ShowTomorrow)throw new Exception("Stopping overview did not close panel or preserve its choices");
            Toggle(overview,true);var restored=Settings.LoadFrom(path);
            if(!restored.OverviewEnabled || restored.WeatherCity!="保留城市" || restored.ShowTomorrow)throw new Exception("Overview re-enable reset retained choices");
            Tab("显示设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
            if(Descendants<CheckBox>(page).Any(check=>check.Tag is string tag && tag.StartsWith("builtin:")))throw new Exception("Duplicate built-in controls remain on display page");
            var manager=Descendants<Button>(page).Single(button=>button.Content as string=="管理时钟与空闲面板");manager.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
            if(Descendants<CheckBox>(page).Count(check=>check.Tag is string tag && tag.StartsWith("builtin:"))!=2 || window.DisplayComponents().Single(item=>item.Id=="clock").Name!="*居中时钟")throw new Exception("Manager navigation or custom component naming inconsistent");
            Design.ApplyTheme(1);await Task.Delay(150);page.UpdateLayout();
            if(((SolidColorBrush)Tab("通用设置").Foreground).Color!=Color.FromRgb(29,29,31) || ((SolidColorBrush)Tab("我的插件").Background).Color!=Color.FromRgb(233,233,239))throw new Exception("Navigation retained dark brushes after live theme switch");
            var lightImage=new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth),(int)Math.Ceiling(page.ActualHeight),96,96,PixelFormats.Pbgra32);lightImage.Render(page);var lightPng=new PngBitmapEncoder();lightPng.Frames.Add(BitmapFrame.Create(lightImage));using(var output=File.Create(Path.Combine(root,"builtin-manager-light.png")))lightPng.Save(output);
        }
        finally{page.Close();window.Close();}
        var reloaded=Settings.LoadFrom(path);var restarted=new MainWindow(suppressOnboarding:true,initialSettings:reloaded){ShowActivated=false};
        try{restarted.Show();await Task.Delay(500);if(!restarted.LiveModuleOrder.Contains("clock") || !reloaded.OverviewEnabled || reloaded.ClockLayout!=1 || reloaded.WeatherCity!="保留城市")throw new Exception("Restart failed to retain built-in state");}finally{restarted.Close();}
        Console.WriteLine("PASS Built-in manager canonical controls, real clock/overview stop-resume, retained choices, navigation and restarted island; evidence: "+root);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++){var child=VisualTreeHelper.GetChild(parent,index);if(child is T found)yield return found;foreach(var nested in Descendants<T>(child))yield return nested;}
    }
}

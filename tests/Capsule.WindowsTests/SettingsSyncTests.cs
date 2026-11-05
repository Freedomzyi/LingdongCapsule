using Capsule.App;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class SettingsSyncTests
{
    internal static async Task RunAsync(bool auditOnly)
    {
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","settings-sync-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;Design.ApplyTheme(0);
        if(!auditOnly)await AuthorizationTests.RunAsync(app);
        var settings=new Settings{ThemeMode=0,MaterialKind=0,IslandShape=1};var path=Path.Combine(root,"settings.json");var startup=false;var readFails=false;var writes=0;using var shortcuts=new GlobalShortcuts(settings);
        void Save(){writes++;settings.SaveTo(path);}
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        SettingsWindow CreatePage()=>new(settings,()=>Task.FromResult("Owned test"),Save,()=>{},value=>startup=value,()=>{},()=>new Dictionary<string,string>(),shortcuts,_=>{},()=>[],_=>{},_=>{},()=>[],_=>{},(_,_)=>false,()=>[],(_,_)=>{},()=>readFails ? throw new IOException("Owned read failure") : startup){ShowActivated=false,Height=300};
        var page=CreatePage();
        try
        {
            page.Show();await Task.Delay(200);page.UpdateLayout();
            var about=Descendants<Button>(page).Single(item=>Equals(item.Content,"关于软件"));var bounds=about.TransformToAncestor(page).TransformBounds(new Rect(new Point(),about.RenderSize));
            var choices=new WelcomeOptions(0,1,2,"owned.missing.monitor",true);
            WelcomeOptions.Commit(settings,choices,true,Save,(value,persist)=>{startup=value;persist();});
            Button Control(string name)=>Descendants<Button>(page).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)==name);
            var theme=(TextBlock)Control("界面主题").Content;
            var observation=new{staleTheme=theme.Text!="浅色 ▾",theme=theme.Text,modelTheme=settings.ThemeMode,navigationOutsideWindow=bounds.Bottom>page.ActualHeight,aboutBottom=bounds.Bottom,windowHeight=page.ActualHeight};
            await File.WriteAllTextAsync(Path.Combine(root,"observation.json"),JsonSerializer.Serialize(observation));
            if(auditOnly){Console.WriteLine("AUDIT "+JsonSerializer.Serialize(observation)+"; evidence: "+root);return;}
            Design.ApplyTheme(settings.ThemeMode);page.RefreshPreferences();page.UpdateLayout();
            Check(theme.Text=="浅色 ▾" && ((TextBlock)Control("背景材质").Content).Text=="液态玻璃 ▾" && writes==1,"Wizard preference refresh is stale or wrote settings again");
            page.SelectPage("显示设置");page.UpdateLayout();
            Check(((TextBlock)Control("岛体形态").Content).Text=="刘海 ▾" && ((TextBlock)Control("目标显示器").Content).Text=="owned.missing.monitor ▾" && Descendants<CheckBox>(page).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)=="待机模式").IsChecked==true,"Display choices did not refresh from shared settings");
            var contentScroll=Descendants<ScrollViewer>(page).First(item=>!Equals(item.Tag,"settings-navigation") && item.Content is StackPanel);contentScroll.ScrollToVerticalOffset(60);page.UpdateLayout();var scrollOffset=contentScroll.VerticalOffset;
            page.RefreshPreferences();page.UpdateLayout();Check(Math.Abs(contentScroll.VerticalOffset-scrollOffset)<1 && Control("岛体形态")!=null,"Preference refresh replaced the page or reset its scroll");
            page.SelectPage("通用设置");page.UpdateLayout();
            var startupToggle=Descendants<CheckBox>(page).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)=="开机启动");
            Check(startupToggle.IsChecked==true,"Startup switch did not refresh from its backend");
            readFails=true;page.RefreshPreferences();Check(writes==1 && startupToggle.IsChecked==true && !startupToggle.IsEnabled && Descendants<TextBlock>(page).Any(item=>item.Text.Contains("自启状态暂时无法读取")),"Backend refresh failure escaped, changed settings or lacked feedback");readFails=false;
            var navigation=Descendants<ScrollViewer>(page).Single(item=>Equals(item.Tag,"settings-navigation"));
            Check(navigation.ScrollableHeight>0,"Short settings navigation has no scroll path");about.BringIntoView();await Task.Delay(150);page.UpdateLayout();bounds=about.TransformToAncestor(page).TransformBounds(new Rect(new Point(),about.RenderSize));
            Check(bounds.Bottom<=page.ActualHeight+1 && bounds.Top>=navigation.TransformToAncestor(page).Transform(new Point()).Y-1,"Last navigation action remains outside the short window");
            about.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));page.UpdateLayout();
            Check(Descendants<Button>(page).Any(item=>Equals(item.Content,"检查更新")),"Scrolled About page could not open");
            await Task.Delay(180);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth),(int)Math.Ceiling(page.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(page);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(root,"short-settings-light.png")))encoder.Save(output);
            Check(Settings.LoadFrom(path).ThemeMode==1 && Settings.LoadFrom(path).OnboardingCompleted,"Owned persisted guide state did not survive reload");
            page.SelectPage("个性化中心");page.UpdateLayout();
            var glassCard=Descendants<Border>(page).Single(item=>Equals(item.Tag,"material-glass"));
            var opacity=Descendants<StackPanel>(page).Single(item=>Equals(item.Tag,"material-opacity"));
            var retainedGlass=settings.Glass;var retainedOpacity=settings.BackgroundOpacity;var beforeRefresh=writes;
            foreach(var material in new[]{0,1,2,0,2})
            {
                settings.MaterialKind=material;page.RefreshPreferences();page.UpdateLayout();
                Check(glassCard.Visibility==(material==2 ? Visibility.Visible : Visibility.Collapsed) && opacity.Visibility==(material==2 ? Visibility.Collapsed : Visibility.Visible),"Material controls do not follow selection/guide refresh");
                Check(settings.Glass==retainedGlass && settings.BackgroundOpacity==retainedOpacity && writes==beforeRefresh,"Material visibility reset preferences or saved during refresh");
            }
            Check(Descendants<Slider>(glassCard).Count()==12 && Control("玻璃质量").IsVisible,"Glass detail controls are missing after mode changes");
            Console.WriteLine("PASS material-dependent opacity/glass controls, twelve parameters and preserved preferences without refresh writes");
            Console.WriteLine("PASS shared guide preferences, startup backend, no extra save/page reset, read-fault feedback and short-window navigation; evidence: "+root);
        }
        finally{page.Close();}
        if(auditOnly)return;
        readFails=true;var retry=CreatePage();
        try
        {
            retry.Show();retry.SelectPage("通用设置");retry.UpdateLayout();
            var check=Descendants<CheckBox>(retry).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)=="开机启动");
            Check(!check.IsEnabled && Descendants<TextBlock>(retry).Any(item=>item.Text.Contains("自启状态暂时无法读取")),"Initial startup read fault prevented settings from opening or falsely enabled control");
            readFails=false;retry.RefreshPreferences();Check(check.IsEnabled && check.IsChecked==true && writes==1 && !Descendants<TextBlock>(retry).Any(item=>item.Text.Contains("自启状态暂时无法读取")),"Startup read recovery did not restore control and clear stale feedback");
        }
        finally{retry.Close();}
        Console.WriteLine("PASS startup read refusal on first open and refresh, disabled unknown state, recovery and no persistence side effect");
        var feedback=CreatePage();
        try
        {
            feedback.Show();feedback.SelectPage("通用设置");feedback.UpdateLayout();
            var msp=Descendants<CheckBox>(feedback).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)=="MSP 实验性接入");
            var errors=0;var guard=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
            guard.Tick+=(_,_)=>{foreach(var dialog in app.Windows.OfType<Window>().Where(item=>item.Title=="未能更新设置").ToArray()){errors++;dialog.Close();}};
            guard.Start();
            try{using var held=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);msp.IsChecked=true;msp.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
            finally{guard.Stop();}
            Check(errors==1 && msp.IsChecked==false && !settings.MspEnabled && !Settings.LoadFrom(path).MspEnabled,"MSP save refusal retained an enabled switch or unsaved in-memory preference");
            feedback.Height=780;feedback.SelectPage("显示设置");feedback.UpdateLayout();
            var composite=Descendants<CheckBox>(feedback).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)=="组合显示");
            var info=((StackPanel)composite.Content).Children.OfType<TextBlock>().Single(item=>item.Text=="ⓘ");
            foreach(var theme in new[]{0,1})
            {
                Design.ApplyTheme(theme);var tip=new ToolTip{Content=info.ToolTip,PlacementTarget=info,IsOpen=true};await Task.Delay(100);tip.ApplyTemplate();tip.UpdateLayout();
                Check(tip.ActualWidth<=280.5 && Descendants<TextBlock>(tip).Any(item=>item.TextWrapping==TextWrapping.Wrap) && Descendants<Border>(tip).Any(item=>item.CornerRadius.TopLeft==10),"Tooltip lacks bounded wrapped text or rounded theme template");
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(tip.ActualWidth*2),(int)Math.Ceiling(tip.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(tip);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(root,"tooltip-"+theme+".png")))png.Save(output);tip.IsOpen=false;
            }
            Console.WriteLine("PASS actual MSP switch save-refusal rollback and bounded wrapping rounded dark/light tooltips; evidence: "+root);
        }
        finally{feedback.Close();}
        var nodes=Path.Combine(root,"refused-msp");var failedSettings=new Settings{MediaEnabled=false,NotificationsEnabled=false,MspEnabled=false};
        var main=new MainWindow(suppressOnboarding:true,initialSettings:failedSettings,localMspDirectory:nodes,localPluginDirectory:Path.Combine(root,"no-plugins"),pluginSettingsWriter:()=>throw new IOException("Owned preference write refusal")){ShowActivated=false};
        try
        {
            main.Show();await Task.Delay(150);failedSettings.MspEnabled=true;var refused=false;
            try{typeof(MainWindow).GetMethod("SaveAndApplySettings",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,null);}
            catch(System.Reflection.TargetInvocationException error) when(error.InnerException is IOException){refused=true;}
            var bridge=(MspBridge)typeof(MainWindow).GetField("msp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
            Check(refused && !bridge.Enabled && !Directory.Exists(nodes),"Preference save failure started or registered MSP before persistence");
            Console.WriteLine("PASS actual main preference writer propagates refusal before any MSP service/registration side effect");
        }
        finally{main.Close();}
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

using Capsule.App;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class WelcomeTests
{
    internal static async Task RunAsync()
    {
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","onboarding-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        var name="LingdongCapsule.Test."+Guid.NewGuid().ToString("N");const string run=@"Software\Microsoft\Windows\CurrentVersion\Run";
        using(var key=Registry.CurrentUser.CreateSubKey(run))
        {
            try
            {
                var threw=false;
                try{StartupService.SetEnabledTransactional(true,()=>throw new IOException("Owned save failure"),name,@"C:\Owned\Capsule.exe");}catch(IOException){threw=true;}
                Check(threw && key.GetValue(name)==null,"Failed initial startup save left a registry entry");
                key.SetValue(name,@"%TEMP%\owned --extra",RegistryValueKind.ExpandString);
                foreach(var enable in new[]{true,false})
                {
                    threw=false;try{StartupService.SetEnabledTransactional(enable,()=>throw new IOException("Owned save failure"),name);}catch(IOException){threw=true;}
                    Check(threw && key.GetValueKind(name)==RegistryValueKind.ExpandString && (string?)key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames)==@"%TEMP%\owned --extra","Failed startup change lost exact old command or type");
                }
                var transactionSettings=new Settings{MonitorId="keep",IslandShape=1,ThemeMode=2,MaterialKind=0,StandbyActive=false,CompactHeight=42};
                var before=WelcomeOptions.Read(transactionSettings);var requested=new WelcomeOptions(0,1,2,"",true);var path=Path.Combine(root,"transaction.json");transactionSettings.SaveTo(path);
                using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))
                {
                    threw=false;try{WelcomeOptions.Commit(transactionSettings,requested,true,()=>transactionSettings.SaveTo(path),(value,persist)=>StartupService.SetEnabledTransactional(value,persist,name,@"C:\Owned\Capsule.exe"));}catch(IOException){threw=true;}
                    Check(threw && WelcomeOptions.Read(transactionSettings)==before && !transactionSettings.OnboardingCompleted && !transactionSettings.StartWithWindows && key.GetValueKind(name)==RegistryValueKind.ExpandString,"Failed wizard commit left memory or startup partially changed");
                }
                WelcomeOptions.Commit(transactionSettings,requested,true,()=>transactionSettings.SaveTo(path),(value,persist)=>StartupService.SetEnabledTransactional(value,persist,name,@"C:\Owned\Capsule.exe"));
                var reloaded=Settings.LoadFrom(path);
                Check(WelcomeOptions.Read(reloaded)==requested && reloaded.OnboardingCompleted && reloaded.StartWithWindows && reloaded.CompactHeight==42 && (string?)key.GetValue(name)=="\"C:\\Owned\\Capsule.exe\" --startup","Successful wizard did not commit existing fields/startup or changed unrelated height");
            }
            finally{key.DeleteValue(name,false);}
        }
        Console.WriteLine("PASS owned startup registry rollback, locked settings recovery and persisted wizard choices without changing unrelated settings");
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;Design.ApplyTheme(0);
        var original=new WelcomeOptions(1,2,0,"OwnedMissingMonitor",false);var completed=0;var opened=0;var fail=true;WelcomeOptions? saved=null;bool? startup=null;
        var window=new WelcomeWindow(original,false,(choices,value)=>{completed++;if(fail)throw new IOException("Owned retry");saved=choices;startup=value;},()=>opened++){ShowActivated=false};
        Button FindButton(string text)=>Descendants<Button>(window).Single(item=>Equals(item.Content,text));
        void Click(string text){FindButton(text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();}
        void Choose(string tag)=>Descendants<Button>(window).Single(item=>Equals(item.Tag,tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Capture(string name)
        {
            await Task.Delay(230);window.UpdateLayout();
            var scroll=Descendants<ScrollViewer>(window).First(item=>item.Content is StackPanel);
            Check(scroll.ScrollableWidth==0 && FindButton("稍后设置").ActualHeight>0,"Wizard content overflowed horizontally or lost footer controls");
            var image=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(root,name+".png"));encoder.Save(output);
        }
        try
        {
            window.Show();await Capture("welcome-dark");Click("下一步");Choose("welcome:岛体形态:0");Choose("welcome:界面主题:1");Choose("welcome:背景材质:2");await Capture("appearance-dark");
            Click("下一步");var monitor=Descendants<Button>(window).Single(item=>Equals(item.Tag,"welcome-monitor"));
            Check(monitor.Content.ToString()!.Contains("已断开的显示器"),"Disconnected monitor choice was discarded on entry");monitor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            monitor.ContextMenu.Items.OfType<MenuItem>().Single(item=>Equals(item.Tag,"")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Choose("welcome:显示模式:1");
            Click("上一步");Check(Descendants<Button>(window).Single(item=>Equals(item.Tag,"welcome:背景材质:2")).Foreground==Brushes.White,"Back navigation lost selected appearance");
            Click("下一步");Design.ApplyTheme(1);await Capture("display-light");Click("下一步");
            var toggle=Descendants<CheckBox>(window).Single(item=>Equals(item.Tag,"welcome-startup"));toggle.IsChecked=true;toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(opened==0 && completed==0,"Wizard choices were committed before confirmation");await Capture("ready-light");
            Click("开始使用");Check(window.IsVisible && Descendants<TextBlock>(window).Any(item=>item.Text.Contains("未能保存")),"Failed save closed the wizard or hid retry feedback");
            fail=false;Click("开始使用");Check(!window.IsVisible && completed==2 && saved==new WelcomeOptions(0,1,2,"",true) && startup==true,"Wizard retry lost choices or duplicated successful completion");
        }
        finally{window.Close();}
        var closedCalls=0;var closed=new WelcomeWindow(original,true,(_,_)=>closedCalls++,()=>{}){ShowActivated=false};closed.Show();closed.Close();Check(closedCalls==0,"Closing an unfinished wizard falsely marked onboarding complete");
        WelcomeOptions? skipped=original;bool? skippedStartup=true;
        var skip=new WelcomeWindow(original,true,(choices,value)=>{skipped=choices;skippedStartup=value;},()=>{}){ShowActivated=false};skip.Show();Descendants<Button>(skip).Single(item=>Equals(item.Content,"稍后设置")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(skipped==null && skippedStartup==null && !skip.IsVisible,"Skip overwrote existing choices or startup");
        var settingsCompletions=0;var settingsGuide=new WelcomeWindow(original,false,(_,_)=>settingsCompletions++,()=>opened++){ShowActivated=false};settingsGuide.Show();
        for(var index=0;index<3;index++){Descendants<Button>(settingsGuide).Single(item=>Equals(item.Content,"下一步")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));settingsGuide.UpdateLayout();}
        Descendants<Button>(settingsGuide).Single(item=>Equals(item.Content,"完成并打开设置")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(settingsCompletions==1 && opened==1 && !settingsGuide.IsVisible,"Settings navigation did not commit once and close the completed wizard");
        var unknownState=new Settings{StartWithWindows=true};var startupWrites=0;
        var unknownGuide=new WelcomeWindow(original,null,(choices,value)=>WelcomeOptions.Commit(unknownState,choices,value,()=>{},(_,persist)=>{startupWrites++;persist();}),()=>{}){ShowActivated=false};unknownGuide.Show();
        for(var index=0;index<3;index++){Descendants<Button>(unknownGuide).Single(item=>Equals(item.Content,"下一步")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));unknownGuide.UpdateLayout();}
        var unknownToggle=Descendants<CheckBox>(unknownGuide).Single(item=>Equals(item.Tag,"welcome-startup"));
        Check(!unknownToggle.IsEnabled && unknownToggle.IsChecked==null,"Unknown startup state falsely appeared actionable");
        Descendants<Button>(unknownGuide).Single(item=>Equals(item.Content,"开始使用")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(unknownState.OnboardingCompleted && unknownState.StartWithWindows && startupWrites==0 && !unknownGuide.IsVisible,"Guide completion changed unreadable startup state or prevented normal use");
        var pageSettings=new Settings();using var shortcuts=new GlobalShortcuts(pageSettings);
        var settingsPage=new SettingsWindow(pageSettings,()=>Task.FromResult("Owned test"),()=>{},()=>{},_=>throw new IOException("Owned startup persistence failure"),()=>{},()=>new Dictionary<string,string>(),shortcuts,_=>{},()=>[],_=>{},_=>{},()=>[],_=>{},(_,_)=>false,()=>[],(_,_)=>{}){ShowActivated=false};
        var dismissed=false;var modalCloser=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
        modalCloser.Tick+=(_,_)=>{foreach(var dialog in app.Windows.OfType<Window>().Where(item=>item.Title=="未能更新设置").ToArray()){dismissed=true;dialog.Close();}};
        try
        {
            settingsPage.Show();settingsPage.UpdateLayout();Descendants<Button>(settingsPage).Single(item=>Equals(item.Content,"通用设置")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));settingsPage.UpdateLayout();
            var check=Descendants<CheckBox>(settingsPage).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)=="开机启动");var prior=check.IsChecked;
            check.IsChecked=prior!=true;modalCloser.Start();check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(dismissed && settingsPage.IsVisible && check.IsChecked==prior,"Startup callback failure escaped settings or left a false enabled state");
        }
        finally{modalCloser.Stop();settingsPage.Close();}
        Console.WriteLine("PASS actual settings startup failure feedback and switch restore using an owned callback, without modifying daily startup");
        Console.WriteLine("PASS actual four-page wizard, selected states, disconnected monitor retention, themes, retry, skip and incomplete close; evidence: "+root);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

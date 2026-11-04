using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Capsule.App;

internal static class LyricOffsetTests
{
    internal static async Task RunAsync()
    {
        var root=Path.GetFullPath(Path.Combine("artifacts","lyric-offset-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;Design.ApplyTheme(0);
        var settings=new Settings{LyricOffsetSeconds=10.4};var file=Path.Combine(root,"settings.json");settings.SaveTo(file);settings=Settings.LoadFrom(file);
        if(settings.LyricOffsetSeconds!=10.4)throw new Exception("Existing offset was clamped to three seconds");
        var writes=0;var fail=false;using var shortcuts=new GlobalShortcuts(settings);
        void Save(){if(fail)throw new IOException("Owned save refusal");settings.SaveTo(file);writes++;}
        var page=new SettingsWindow(settings,()=>Task.FromResult("Owned test"),Save,()=>{},_=>{},()=>{},()=>new Dictionary<string,string>(),shortcuts,_=>{},()=>[],_=>{},_=>{},()=>[],_=>{},(_,_)=>false,()=>[],(_,_)=>{},()=>false){ShowActivated=false};
        try
        {
            page.Show();page.SelectPage("媒体设置");await Task.Delay(150);page.UpdateLayout();
            Button Find(string name)=>Descendants<Button>(page).Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)==name);
            void Click(string name)=>Find(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click("增大歌词偏移");if(settings.LyricOffsetSeconds!=10.5 || writes!=1 || Settings.LoadFrom(file).LyricOffsetSeconds!=10.5)throw new Exception("Positive unbounded lyric step/persistence failed");
            Click("减小歌词偏移");if(settings.LyricOffsetSeconds!=10.4 || writes!=2)throw new Exception("Lyric step is not one tenth");
            settings.LyricOffsetSeconds=-8.4;page.RefreshPreferences();Click("减小歌词偏移");if(settings.LyricOffsetSeconds!=-8.5 || Settings.LoadFrom(file).LyricOffsetSeconds!=-8.5)throw new Exception("Negative unbounded offset was clamped");
            Click("还原歌词偏移");if(settings.LyricOffsetSeconds!=0)throw new Exception("Offset reset did not restore zero");
            var before=writes;fail=true;Click("增大歌词偏移");fail=false;
            if(settings.LyricOffsetSeconds!=0 || writes!=before || !Descendants<TextBlock>(page).Any(text=>text.Text.StartsWith("歌词偏移未保存")))throw new Exception("Save refusal lost offset or lacked feedback");
            settings.LyricOffsetSeconds=double.MaxValue;settings.SaveTo(file);page.RefreshPreferences();Click("还原歌词偏移");
            if(settings.LyricOffsetSeconds!=0 || Settings.LoadFrom(file).LyricOffsetSeconds!=0)throw new Exception("Large retained offset could not reset");
        }
        finally{page.Close();}
        var invalid=new Settings{LyricOffsetSeconds=double.NaN};invalid.Normalize();if(invalid.LyricOffsetSeconds!=0)throw new Exception("Non-finite offset was retained");
        Console.WriteLine("PASS actual lyric offset 0.1 steps beyond +/-3, preserved reload, negative values, reset, save refusal and finite guard; evidence: "+root);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

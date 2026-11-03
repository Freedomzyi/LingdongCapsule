using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capsule.App;

internal static class FontTests
{
    internal static async Task RunAsync(string[] args)
    {
        var root=Path.GetFullPath(Path.Combine("artifacts","font-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var fontDirectory=Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var original=File.Exists(Path.Combine(fontDirectory,"segoeui.ttf")) ? Path.Combine(fontDirectory,"segoeui.ttf") : Directory.EnumerateFiles(fontDirectory,"*.ttf").OrderBy(path=>new FileInfo(path).Length).First();
        // Local test fixture only: never included in source control or distributable bundles.
        var owned=Path.Combine(root,"本地 font # 100%.ttf");File.Copy(original,owned);
        bool Uses(FontFamily family,string path)=>family.GetTypefaces().Any(type=>type.TryGetGlyphTypeface(out var glyph) && glyph.FontUri.IsFile && string.Equals(glyph.FontUri.LocalPath,path,StringComparison.OrdinalIgnoreCase));
        if(!Uses(LocalFont.Load(owned),owned))throw new Exception("Local TTF silently fell back to an installed font");
        var ttc=Directory.EnumerateFiles(fontDirectory,"*.ttc").OrderBy(path=>new FileInfo(path).Length).First();
        if(!Uses(LocalFont.Load(ttc),ttc))throw new Exception("TTC did not resolve selected collection");
        var otfIndex=Array.IndexOf(args,"--otf-file");
        if(otfIndex>=0)
        {
            if(otfIndex+1>=args.Length)throw new ArgumentException("Provide --otf-file path");
            var otf=Path.GetFullPath(args[otfIndex+1]);
            if(!string.Equals(Path.GetExtension(otf),".otf",StringComparison.OrdinalIgnoreCase) || !Uses(LocalFont.Load(otf),otf))throw new Exception("Provided OTF did not resolve selected file");
            Console.WriteLine("PASS provided real OTF file resolves selected face without installing font");
        }
        var malformed=Path.Combine(root,"invalid.ttf");File.WriteAllText(malformed,"This is not a font; own invalid fixture.");
        foreach(var bad in new[]{malformed,Path.Combine(root,"missing.ttf"),"https://example.invalid/font.ttf",@"\\invalid\share\font.ttf"})
        {try{LocalFont.Load(bad);throw new Exception("Invalid font accepted");}catch(Exception error) when(LocalFont.IsLoadError(error)){}}
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;Design.ApplyTheme(0);
        var settings=new Settings{MediaEnabled=false,NotificationsEnabled=false};var saved=Path.Combine(root,"settings.json");string? picked=owned;var fail=false;var writes=0;
        using var shortcuts=new GlobalShortcuts(settings);
        void Save(){if(fail)throw new IOException("Owned save refusal");settings.SaveTo(saved);writes++;}
        var page=new SettingsWindow(settings,()=>Task.FromResult("Owned test"),Save,()=>{},_=>{},()=>{},()=>new Dictionary<string,string>(),shortcuts,_=>{},()=>[],_=>{},_=>{},()=>[],_=>{},(_,_)=>false,()=>[],(_,_)=>{},()=>false,fontPreferenceWriter:Save,fontPicker:()=>picked){ShowActivated=false};
        try
        {
            page.Show();page.SelectPage("个性化中心");await Task.Delay(150);page.UpdateLayout();
            var choose=Descendants<Button>(page).Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="选择字体文件");
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(settings.FontFilePath!=owned || settings.FontName!="" || writes!=1 || !Uses(page.FontFamily,owned))throw new Exception("File selection did not save/apply exactly one font preference");
            page.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth),(int)Math.Ceiling(page.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(page);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(root,"local-font-settings.png")))encoder.Save(output);
            var reloaded=Settings.LoadFrom(saved);Design.ApplyFont(reloaded.FontName,reloaded.FontFilePath);
            if(!Uses(Design.UserFont,owned))throw new Exception("Saved local font did not restore");
            picked=malformed;choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(settings.FontFilePath!=owned || writes!=1 || !Descendants<TextBlock>(page).Any(text=>text.Text.StartsWith("字体未能更新")))throw new Exception("Bad font changed choice or lacked feedback");
            picked=ttc;fail=true;choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));fail=false;
            if(settings.FontFilePath!=owned || writes!=1 || !Uses(Design.UserFont,owned) || Settings.LoadFrom(saved).FontFilePath!=owned)throw new Exception("Save failure did not retain original font and disk choice");
            picked=null;choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(writes!=1)throw new Exception("Cancelled picker saved settings");
            var current=Descendants<Button>(page).Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="字体");
            current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var menu=current.ContextMenu!;
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;
            if(settings.FontFilePath!="" || settings.FontName!="" || writes!=2 || Design.UserFont.Source!="Microsoft YaHei UI")throw new Exception("Restore default did not clear both font selections");
        }
        finally{page.Close();}
        settings.FontFilePath=owned;settings.SaveTo(saved);
        var loadedWindow=new MainWindow(suppressOnboarding:true,initialSettings:Settings.LoadFrom(saved),localPluginDirectory:Path.Combine(root,"plugins"),pluginSettingsWriter:()=>{}){ShowActivated=false};
        try
        {
            loadedWindow.Show();await Task.Delay(150);loadedWindow.UpdateLayout();
            var text=(TextBlock)loadedWindow.FindName("ClockTime");
            if(!Uses(loadedWindow.FontFamily,owned) || !Uses(text.FontFamily,owned))throw new Exception("Restarted actual island did not inherit selected local font");
        }
        finally{loadedWindow.Close();}
        settings.FontFilePath=Path.Combine(root,"moved.ttf");settings.SaveTo(saved);Design.ApplyFont(settings.FontName,settings.FontFilePath);
        if(Design.FontStatus.Length==0 || settings.FontFilePath.Length==0 || Design.UserFont.Source!="Microsoft YaHei UI")throw new Exception("Missing file did not retain preference and provide fallback feedback");
        var window=new MainWindow(suppressOnboarding:true,initialSettings:Settings.LoadFrom(saved),localPluginDirectory:Path.Combine(root,"plugins"),pluginSettingsWriter:()=>{}){ShowActivated=false};
        try{window.Show();await Task.Delay(150);if(window.FontFamily.Source!="Microsoft YaHei UI")throw new Exception("Missing font prevented actual island fallback");}finally{window.Close();}
        Console.WriteLine("PASS real local TTF/TTC, escaped Unicode path, invalid/missing/remote refusal, UI selection, persistence, save rollback, cancel and default restore; evidence: "+root);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}

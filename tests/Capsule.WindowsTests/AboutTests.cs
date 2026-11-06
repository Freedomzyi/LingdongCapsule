using Capsule.App;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class AboutTests
{
    internal static async Task RunAsync()
    {
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","about-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var settings=new Settings();using var shortcuts=new GlobalShortcuts(settings);
        var page=new SettingsWindow(settings,()=>Task.FromResult("Owned fixture"),()=>{},()=>{},_=>{},()=>{},()=>new Dictionary<string,string>(),shortcuts,_=>{},()=>[],_=>{},_=>{},()=>[],_=>{},(_,_)=>false,()=>[],(_,_)=>{},()=>false){ShowActivated=false,Height=820};
        try
        {
            page.Show();page.SelectPage("关于软件");await Task.Delay(200);page.UpdateLayout();
            Button Find(string label)=>Descendants<Button>(page).Single(item=>System.Windows.Automation.AutomationProperties.GetName(item)==label);
            if(!Equals(Find("复制联系邮箱").CommandParameter,"freedomzy@agent.qq.com") || !Equals(Find("复制抖音号").CommandParameter,"Freedomzyi"))throw new Exception("About contact values do not match Owner's exact identifiers");
            var uninstall=Find("卸载灵动胶囊");
            if(!uninstall.IsEnabled)throw new Exception("Settings uninstall entry is unavailable");
            var portable=Path.Combine(root,"portable");Directory.CreateDirectory(portable);
            if(SettingsWindow.CreateUninstallCommand(portable)!=null)throw new Exception("Portable entry targets an unrelated installed app");
            var installed=Path.Combine(root,"installed 中文 空格");Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(installed,"installed-files.json"),"[]");
            try{SettingsWindow.CreateUninstallCommand(installed);throw new Exception("Missing executable accepted");}catch(IOException){}
            File.WriteAllText(Path.Combine(installed,"LingdongCapsule.exe"),"Fixture: never execute");
            var command=SettingsWindow.CreateUninstallCommand(installed)!;
            if(command.FileName!=Path.Combine(installed,"LingdongCapsule.exe") || command.ArgumentList.Count!=1 || command.ArgumentList[0]!="--uninstall" || command.UseShellExecute || !command.CreateNoWindow || command.WorkingDirectory!=installed)throw new Exception("Settings uninstall command does not target its own graphical maintenance entry");
            foreach(var theme in new[]{0,1})
            {
                Design.ApplyTheme(theme);uninstall.BringIntoView();await Task.Delay(50);page.UpdateLayout();
                var bounds=uninstall.TransformToAncestor(page).TransformBounds(new Rect(new Point(),uninstall.RenderSize));
                if(bounds.Top<0 || bounds.Bottom>page.ActualHeight || uninstall.ActualHeight<30)throw new Exception("Uninstall button cannot be reached in settings");
                Capture(page,Path.Combine(root,"uninstall-entry-"+theme+".png"));
            }
            var original=Application.GetResourceStream(new Uri(SupportWindow.ImageResource));using(original.Stream)if(Convert.ToHexString(SHA256.HashData(original.Stream))!="FBF15088AA6FE3830BF1009F164F4219A9ED708A957B78526C8424D719B7BDEC")throw new Exception("Embedded support image differs from Owner's original bytes");
            var opens=0;var guard=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
            guard.Tick+=(_,_)=>{var dialog=app.Windows.OfType<SupportWindow>().SingleOrDefault();if(dialog==null)return;if(dialog.PaymentImage.PixelWidth!=365 || dialog.PaymentImage.PixelHeight!=370)throw new Exception("Support image was cropped or resampled before display");opens++;dialog.Close();};
            guard.Start();try{Find("查看微信赞赏码").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}finally{guard.Stop();}
            if(opens!=1)throw new Exception("About appreciation action did not open its owned dialog exactly once");
            foreach(var theme in new[]{0,1})
            {
                Design.ApplyTheme(theme);var dialog=new SupportWindow{Owner=page,ShowActivated=false};
                try
                {
                    dialog.Show();await Task.Delay(100);dialog.UpdateLayout();
                    var image=Descendants<Image>(dialog).Single();var close=Descendants<Button>(dialog).Single(item=>Equals(item.Content,"关闭"));
                    if(image.ActualWidth<200 || image.ActualHeight<250 || close.ActualHeight<30 || image.Stretch!=Stretch.Uniform)throw new Exception("Support dialog clips the image or hides its close action");
                    Capture(dialog,Path.Combine(root,"support-"+theme+".png"));
                    dialog.Height=260;dialog.UpdateLayout();var bounds=close.TransformToAncestor(dialog).TransformBounds(new Rect(new Point(),close.RenderSize));if(bounds.Bottom>dialog.ActualHeight || bounds.Top<0)throw new Exception("Short support dialog loses its close action");
                }
                finally{dialog.Close();}
            }
            Find("查看微信赞赏码").BringIntoView();await Task.Delay(100);Capture(page,Path.Combine(root,"about.png"));
            Console.WriteLine("PASS exact author contacts, original QR, appreciation routing, dark/light layout, reachable uninstall entry and own-directory graphical uninstall command; portable/missing executable boundaries; no uninstall or payment apps invoked. Evidence: "+root);
        }
        finally{page.Close();}
    }
    private static void Capture(FrameworkElement view,string path){view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth*2),(int)Math.Ceiling(view.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);png.Save(output);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T value)yield return value;foreach(var item in Descendants<T>(child))yield return item;}}
}

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
            Console.WriteLine("PASS exact email/Douyin values, Owner QR-only embedded image SHA256, real appreciation dialog routing, dark/light layout and short-window close; clipboard and payment apps were not invoked. Evidence: "+root);
        }
        finally{page.Close();}
    }
    private static void Capture(FrameworkElement view,string path){view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth*2),(int)Math.Ceiling(view.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);png.Save(output);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T value)yield return value;foreach(var item in Descendants<T>(child))yield return item;}}
}

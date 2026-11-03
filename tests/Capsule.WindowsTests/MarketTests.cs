using Capsule.App;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

internal static class MarketTests
{
    internal static async Task RunLiveAsync()
    {
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(25)};
        var packages=await new PluginMarket(http).ReadAsync(default);
        Console.WriteLine($"PASS Live official catalog read: {packages.Length} published packages; this is not live package download acceptance");
    }
    private sealed class Handler(Func<Uri,HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(reply(request.RequestUri!));}
    }
    private static HttpResponseMessage Json(string text)=>new(HttpStatusCode.OK){Content=new StringContent(text)};
    internal static async Task RunAsync()
    {
        var zip=File.ReadAllBytes(Path.Combine("artifacts","plugins","capsule.centerclock-0.1.0.zip"));
        var package=new MarketPlugin("capsule.centerclock","居中时钟","0.1.0","独立时钟示例","https://raw.githubusercontent.com/Freedomzyi/LingdongCapsule/main/plugins/packages/clock.zip",Convert.ToHexString(SHA256.HashData(zip)),"0.1.0","灵动胶囊","MIT","时间");
        var catalog=JsonSerializer.Serialize(new MarketCatalog(1,[package]));
        using var http=new HttpClient(new Handler(uri=>uri==PluginMarket.CatalogUri ? Json(catalog) : new(HttpStatusCode.OK){Content=new ByteArrayContent(zip)}));var market=new PluginMarket(http);
        var list=await market.ReadAsync(default);if(list.Single()!=package)throw new Exception("Market catalog decode failed");
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","market-tests",Guid.NewGuid().ToString("N"));var downloaded=await market.DownloadAsync(package,root,new(0,1,0,0),default);
        if(!File.ReadAllBytes(downloaded).SequenceEqual(zip))throw new Exception("Verified market download changed bytes");File.Delete(downloaded);
        async Task Reject(Func<Task> action){try{await action();}catch(Exception error) when(error is InvalidDataException or InvalidOperationException or OperationCanceledException){return;}throw new Exception("Unsafe market input was accepted");}
        await Reject(()=>market.DownloadAsync(package with{Sha256=new string('0',64)},root,new(0,1,0,0),default));
        await Reject(()=>market.DownloadAsync(package with{Id="different.clock"},root,new(0,1,0,0),default));
        await Reject(()=>market.DownloadAsync(package with{MinimumHostVersion="9.0.0"},root,new(0,1,0,0),default));
        await Reject(()=>market.DownloadAsync(package with{DownloadUrl="http://127.0.0.1/private"},root,new(0,1,0,0),default));
        await Reject(()=>market.DownloadAsync(package with{DownloadUrl="https://github.com/other/repo/releases/download/v1/file.zip"},root,new(0,1,0,0),default));
        using(var invalid=new HttpClient(new Handler(_=>Json(JsonSerializer.Serialize(new MarketCatalog(1,[package,package]))))))await Reject(()=>new PluginMarket(invalid).ReadAsync(default));
        using(var oversized=new HttpClient(new Handler(_=>Json(new string('x',256*1024+1)))))await Reject(()=>new PluginMarket(oversized).ReadAsync(default));
        using(var redirect=new HttpClient(new Handler(_=>{var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("https://untrusted.example/package.zip");return response;})))await Reject(()=>new PluginMarket(redirect).DownloadAsync(package,root,new(0,1,0,0),default));
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();await Reject(()=>market.DownloadAsync(package,root,new(0,1,0,0),canceled.Token));}
        if(Directory.EnumerateFiles(root).Any())throw new Exception("Rejected market input left a downloaded file");
        Console.WriteLine("PASS Market catalog, verified real ZIP, identity/version/host/SHA validation, source/redirect bounds, size, cancel and cleanup");
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();Design.ApplyTheme(0);
        PluginCatalogEntry[] local=[];
        var window=new PluginMarketWindow(_=>throw new Exception("Test must not install without Owner confirmation"),()=>local,()=>{},http);
        try
        {
            window.Show();await Task.Delay(200);await window.RefreshAsync();window.UpdateLayout();
            var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(window);
            var folder=Path.Combine("artifacts","verification","market");Directory.CreateDirectory(folder);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(folder,"market-window.png")))encoder.Save(file);
            Console.WriteLine("PASS Actual market window renders an injected catalog without executing a plugin");
            var search=Descendants<System.Windows.Controls.TextBox>(window).Single();search.Text="no matching package";
            if(Descendants<System.Windows.Controls.Button>(window).Any(button=>button.Content as string=="安装"))throw new Exception("Market search did not filter package cards");
            search.Text="";local=[new("owned-test.dll",package.Name,package.Id,"0.0.1",true,false,"")];var toggle=Descendants<System.Windows.Controls.CheckBox>(window).Single();toggle.IsChecked=true;
            if(!Descendants<System.Windows.Controls.Button>(window).Any(button=>button.Content as string=="更新"))throw new Exception("Market did not identify an installed update");
            local=[local[0] with{Version="0.1.0"}];toggle.IsChecked=false;
            if(!Descendants<System.Windows.Controls.Button>(window).Any(button=>button.Content as string=="已启用" && !button.IsEnabled))throw new Exception("Market offered an already enabled version");
            var island=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:new Settings{MediaEnabled=false,NotificationsEnabled=false,ClockEnabled=false},localPluginDirectory:Path.Combine(root,"installed"));
            string? installFile=null;
            try
            {
                island.Show();installFile=await market.DownloadAsync(package,root,new(0,1,0),default);var before=DateTime.Now;
                island.LoadPlugin(installFile,save:false);
                for(var attempt=0;attempt<30 && ((System.Windows.Controls.TextBlock)island.LivePluginView.Children[0]).Text.Length==0;attempt++)await Task.Delay(100);
                var displayed=((System.Windows.Controls.TextBlock)island.LivePluginView.Children[0]).Text;
                if(displayed!=before.ToString("HH:mm") && displayed!=DateTime.Now.ToString("HH:mm"))throw new Exception("Verified market ZIP did not run in actual island");
            }
            finally{island.Close();if(installFile!=null)File.Delete(installFile);}
            Console.WriteLine("PASS Market search/update/enabled UI states and verified ZIP installation into actual island");
        }
        finally{window.Close();}
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);index++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,index);if(child is T found)yield return found;foreach(var nested in Descendants<T>(child))yield return nested;}
    }
}

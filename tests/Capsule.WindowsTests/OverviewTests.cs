using Capsule.App;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class OverviewTests
{
    private sealed class WeatherHandler : HttpMessageHandler
    {
        public bool Fail;
        public int Calls;
        public TaskCompletionSource? Delay;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;if(Delay!=null)await Delay.Task; // Deliberately ignore cancellation to exercise stale-response guard.
            if(Fail)throw new HttpRequestException("Intentional offline test");
            return new(HttpStatusCode.OK){Content=new StringContent(request.RequestUri!.Host.Contains("geocoding") ? "{\"results\":[{\"latitude\":31.2,\"longitude\":121.5}]}" : "{\"current\":{\"temperature_2m\":22.5,\"weather_code\":2},\"daily\":{\"time\":[\"2026-11-03\",\"2026-11-04\"],\"weather_code\":[2,3],\"temperature_2m_min\":[17,18],\"temperature_2m_max\":[24,25]}}")};
        }
    }
    public static async Task RunAsync(string[] args)
    {
        var index=Array.IndexOf(args,"--overview-test");
        var output=index+1<args.Length && !args[index+1].StartsWith("--") ? Path.GetFullPath(args[index+1]) : throw new Exception("Provide an output directory");
        Directory.CreateDirectory(output);var run=Guid.NewGuid().ToString("N");var cache=Path.Combine(output,"test-weather-"+run+".json");
        var handler=new WeatherHandler();
        using(var weather=new WeatherService(new HttpClient(handler),cache))
        {
            weather.SelectCity("上海");await weather.RefreshIfDueAsync();
            if(weather.Data?.City!="上海" || weather.Data.Temperature!=22.5 || weather.Cached)throw new Exception("Weather parsing failed");
            var calls=handler.Calls;await weather.RefreshIfDueAsync();if(handler.Calls!=calls)throw new Exception("Weather cache interval ignored");
        }
        using(var offline=new WeatherService(new HttpClient(new WeatherHandler{Fail=true}),cache))
        {
            offline.SelectCity("上海");await offline.RefreshIfDueAsync();
            if(offline.Data?.City!="上海" || !offline.Cached || !offline.Status.Contains("缓存"))throw new Exception("Same-city offline cache lost");
            offline.SelectCity("北京");await offline.RefreshIfDueAsync();
            if(offline.Data!=null || offline.Cached)throw new Exception("Different-city cache leaked");
        }
        var delayed=new WeatherHandler{Delay=new TaskCompletionSource()};
        using(var weather=new WeatherService(new HttpClient(delayed),Path.Combine(output,"stale-weather-"+run+".json")))
        {
            weather.SelectCity("上海");var pending=weather.RefreshIfDueAsync();weather.SelectCity("北京");delayed.Delay.SetResult();await pending;
            if(weather.Data!=null)throw new Exception("Stale city response replaced current selection");
            await weather.RefreshIfDueAsync();if(weather.Data?.City!="北京")throw new Exception("New city did not refresh after cancellation");
        }
        Console.WriteLine("PASS Weather decoding, request interval, same-city offline cache and stale-city cancellation");
        var sampler=new OverviewService();sampler.Sample();await Task.Delay(1100);var sample=sampler.Sample();
        if(sample.Cpu is not (>=0 and <=100) || sample.Memory is not (>=0 and <=100))throw new Exception("Native hardware readings invalid");
        var groups=await Task.Run(sampler.ReadProcesses);
        using var self=Process.GetCurrentProcess();
        if(groups.Count is <1 or >3 || groups.Any(group=>group.Bytes<=0 || group.Processes.Any(process=>process.Session!=self.SessionId || process.Session==0)))throw new Exception("Process ranking session boundary failed");
        var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-Command");start.ArgumentList.Add("Start-Sleep -Seconds 60");
        using(var child=Process.Start(start)!)
        {
            try
            {
                var identity=new ProcessIdentity(child.Id,child.StartTime,child.ProcessName,child.SessionId);
                var wrong=new MemoryGroup(identity.Name,0,new[]{identity with{Started=identity.Started.AddSeconds(-1)}},null);
                if(OverviewService.EndConfirmed(wrong)!=1 || child.HasExited)throw new Exception("Mismatched process identity was allowed");
                var correct=new MemoryGroup(identity.Name,0,new[]{identity},null);
                if(OverviewService.EndConfirmed(correct)!=0 || !child.WaitForExit(5000))throw new Exception("Owned test process did not end");
            }
            finally{if(!child.HasExited){child.Kill();child.WaitForExit();}}
        }
        Console.WriteLine("PASS Real CPU/memory, current-session Top 3 and identity-safe termination using only an owned disposable child");
        if(args.Contains("--weather-live"))
        {
            using var weather=new WeatherService(cacheFile:Path.Combine(output,"live-weather.json"));weather.SelectCity("上海");await weather.RefreshIfDueAsync();
            if(weather.Data==null || weather.Cached)throw new Exception("Live weather request failed: "+weather.Status);
            Console.WriteLine("PASS Public weather service returned current and two daily forecasts");
        }
        var app=new Capsule.App.App();app.InitializeComponent();Design.ApplySystemTheme();
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:new Settings{NotificationsEnabled=false,MediaEnabled=false});
        try
        {
            window.Show();await Task.Delay(500);window.OpenOverview();await Task.Delay(600);
            window.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0){RoutedEvent=System.Windows.UIElement.MouseEnterEvent});
            await Task.Delay(1100);
            if(window.LiveOverview.CurrentSample?.Cpu==null || window.LiveOverview.CurrentProcesses.Count==0 || window.ActualHeight<250)throw new Exception("Actual island overview did not update");
            window.UpdateLayout();if(!window.LiveOverview.ContentFits)throw new Exception("Overview content exceeds available island height: "+window.LiveOverview.LayoutSizes);var dpi=VisualTreeHelper.GetDpi(window);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"overview.png")))encoder.Save(file);
            Console.WriteLine("PASS Actual WPF overview displays live hardware, network and process ranking");
        }
        finally{window.Close();}
    }
}

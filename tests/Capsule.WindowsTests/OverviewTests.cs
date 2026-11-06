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
        SharedRuntimeGrouping();
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
        var stoppedCache=Path.Combine(output,"stopped-weather-"+run+".json");var stoppedHandler=new WeatherHandler{Delay=new TaskCompletionSource()};
        using(var weather=new WeatherService(new HttpClient(stoppedHandler),stoppedCache))
        {
            weather.SelectCity("上海");var pending=weather.RefreshIfDueAsync();weather.Suspend();stoppedHandler.Delay.SetResult();await pending;
            if(weather.Data!=null || File.Exists(stoppedCache))throw new Exception("Stopped component accepted or cached a late weather result");
            await weather.RefreshIfDueAsync();if(weather.Data?.City!="上海")throw new Exception("Stopped weather request prevented later re-enable");
        }
        Console.WriteLine("PASS Stopping overview rejects late weather/cache writes and allows re-enable without clearing city");
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
                if(!OverviewService.ReadParents().TryGetValue(child.Id,out var nativeParent) || nativeParent!=Environment.ProcessId)throw new Exception("Native Unicode process snapshot did not identify the owned child parent");
                var identity=new ProcessIdentity(child.Id,child.StartTime,child.ProcessName,child.SessionId);
                var wrong=new MemoryGroup(identity.Name,0,new[]{identity with{Started=identity.Started.AddSeconds(-1)}},null);
                if(await OverviewService.EndConfirmedAsync(wrong)!=1 || child.HasExited)throw new Exception("Mismatched process identity was allowed");
                var correct=new MemoryGroup(identity.Name,0,new[]{identity},null);
                if(await OverviewService.EndConfirmedAsync(correct)!=0 || !child.HasExited)throw new Exception("Owned test process did not end before completion was reported");
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
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;Design.ApplySystemTheme();
        await EndDialogAsync(app,output);
        await CitySaveFailureAsync(app,output,run);
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:new Settings{NotificationsEnabled=false,MediaEnabled=false,ThemeMode=1});
        try
        {
            window.Show();await Task.Delay(500);window.OpenOverview();await Task.Delay(600);
            if(Design.IsDark || ((SolidColorBrush)((System.Windows.Controls.Border)window.FindName("Shell")).Background).Color.R<200)throw new Exception("Light overview theme was not applied");
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
    private static void SharedRuntimeGrouping()
    {
        var time=DateTime.Now.AddMinutes(-2);
        var ids=new Dictionary<int,ProcessIdentity>
        {
            [1]=new(1,time,"Weixin",1),[2]=new(2,time,"QQMusic",1),
            [3]=new(3,time.AddSeconds(1),"WeChatAppEx",1),[4]=new(4,time.AddSeconds(2),"WeChatAppEx",1),
            [5]=new(5,time.AddSeconds(1),"WeChatAppEx",1),[6]=new(6,time.AddSeconds(2),"WeChatAppEx",1),
            [7]=new(7,time,"explorer",1)
        };
        var parents=new Dictionary<int,int>{{3,1},{4,3},{5,2},{6,5}};
        if(OverviewService.GroupProcess(4,ids,parents)!="Weixin" || OverviewService.GroupProcess(6,ids,parents)!="QQMusic")throw new Exception("Shared WeChatAppEx runtime was not assigned to its actual WeChat/QQ Music owner");
        parents[5]=7;if(OverviewService.GroupProcess(6,ids,parents)!="WeChatAppEx")throw new Exception("Unknown helper owner was promoted to an unrelated application");
        parents[5]=2;ids[2]=ids[2] with{Started=time.AddSeconds(10)};
        if(OverviewService.GroupProcess(6,ids,parents)!="WeChatAppEx")throw new Exception("Reused parent PID was accepted");
        ids[2]=ids[2] with{Started=time,Session=2};if(OverviewService.GroupProcess(6,ids,parents)!="WeChatAppEx")throw new Exception("Cross-session helper owner was accepted");
        Console.WriteLine("PASS shared runtime ownership separates WeChat and QQ Music, rejects reused parent PID/foreign session and unknown host");
    }
    private static async Task EndDialogAsync(Capsule.App.App app,string output)
    {
        var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-Command");start.ArgumentList.Add("Start-Sleep -Seconds 60");
        using var child=Process.Start(start)!;
        var weather=new WeatherService(new HttpClient(new WeatherHandler()),Path.Combine(output,"end-dialog-weather.json"));
        using var view=new OverviewView(new Settings(),()=>{},weather);
        var identity=new ProcessIdentity(child.Id,child.StartTime,child.ProcessName,child.SessionId);
        var group=new MemoryGroup(identity.Name,0,new[]{identity},null);
        var cancel=true;var sawDialog=false;var timeout=false;var clock=Stopwatch.StartNew();
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>
        {
            foreach(var dialog in app.Windows.OfType<System.Windows.Window>().ToArray())
            {
                if(clock.Elapsed.TotalSeconds>5){timeout=true;dialog.Close();continue;}
                if(dialog.Title!="关闭应用程序？")continue;
                sawDialog=true;
                Descendants<System.Windows.Controls.Button>(dialog).Single(button=>Equals(button.Content,cancel ? "取消" : "结束进程")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            }
        };
        try
        {
            timer.Start();await view.ConfirmEndAsync(group);
            if(!sawDialog || timeout || child.HasExited || view.DialogOpen)throw new Exception("Canceled real end dialog killed the owned fixture or retained modal state");
            cancel=false;sawDialog=false;clock.Restart();await view.ConfirmEndAsync(group);
            if(!sawDialog || timeout || !child.HasExited || view.DialogOpen)throw new Exception("Confirmed real end dialog did not await owned process exit");
            Console.WriteLine("PASS actual confirmation cancel preserves owned process; accept waits for verified exit and releases modal state; no daily app terminated");
        }
        finally{timer.Stop();if(!child.HasExited){child.Kill();child.WaitForExit();}}
    }
    private static async Task CitySaveFailureAsync(Capsule.App.App app,string output,string run)
    {
        var settings=new Settings{WeatherCity="上海"};var file=Path.Combine(output,"city-settings-"+run+".json");settings.SaveTo(file);
        var handler=new WeatherHandler();var weather=new WeatherService(new HttpClient(handler),Path.Combine(output,"city-cache-"+run+".json"));
        weather.SelectCity(settings.WeatherCity);await weather.RefreshIfDueAsync();
        var fail=true;var writes=0;var errorShown=false;var errorModalHeld=false;var timedOut=false;var cancel=false;
        using var view=new OverviewView(settings,()=>{writes++;if(fail)throw new IOException("Owned weather save refusal");settings.SaveTo(file);},weather);
        var host=new System.Windows.Window{Title="Owned city dialog fixture",Content=view,Width=300,Height=340,ShowActivated=false,ShowInTaskbar=false};
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};var deadline=Stopwatch.StartNew();
        timer.Tick+=(_,_)=>
        {
            foreach(var dialog in app.Windows.OfType<System.Windows.Window>().Where(item=>item!=host).ToArray())
            {
                if(deadline.Elapsed.TotalSeconds>4){timedOut=true;dialog.Close();continue;}
                if(dialog.Title=="天气城市")
                {
                    if(cancel){dialog.Close();continue;}
                    Descendants<System.Windows.Controls.TextBox>(dialog).Single().Text="北京";
                    Descendants<System.Windows.Controls.Button>(dialog).Single(button=>Equals(button.Content,"保存")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
                else if(dialog.Title=="天气城市未保存"){errorShown=true;errorModalHeld=view.DialogOpen;dialog.Close();}
            }
        };
        try
        {
            host.Show();await Task.Delay(80);var before=handler.Calls;timer.Start();var escaped=false;
            try{view.EditCity();}catch(IOException){escaped=true;}finally{timer.Stop();}
            if(escaped || timedOut || !errorShown || !errorModalHeld || settings.WeatherCity!="上海" || Settings.LoadFrom(file).WeatherCity!="上海" || view.CurrentWeather?.City!="上海" || handler.Calls!=before || writes!=1 || view.DialogOpen)throw new Exception("Weather city save refusal escaped, lost original selection or started new weather request");
            fail=false;deadline.Restart();timer.Start();try{view.EditCity();}finally{timer.Stop();}
            for(var attempt=0;attempt<30 && view.CurrentWeather?.City!="北京";attempt++)await Task.Delay(30);
            if(settings.WeatherCity!="北京" || Settings.LoadFrom(file).WeatherCity!="北京" || view.CurrentWeather?.City!="北京" || writes!=2 || view.DialogOpen)throw new Exception("Weather city save retry did not persist and refresh");
            cancel=true;deadline.Restart();timer.Start();try{view.EditCity();}finally{timer.Stop();}
            if(writes!=2 || view.DialogOpen)throw new Exception("Canceled city dialog saved or retained modal state");
            Console.WriteLine("PASS real city dialog save refusal preserves preference/disk/weather, shows feedback without escaping; retry/cancel and modal state recover");
        }
        finally{timer.Stop();host.Close();}
    }
    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T:System.Windows.DependencyObject
    {for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}}

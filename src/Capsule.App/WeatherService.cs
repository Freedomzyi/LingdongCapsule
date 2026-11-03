using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Capsule.App;

internal sealed record WeatherDay(string Date,int Code,double Minimum,double Maximum)
{
    public string Display(bool tomorrow)
    {
        var expected=DateTime.Today.AddDays(tomorrow ? 1 : 0).ToString("yyyy-MM-dd");
        var label=Date==expected ? tomorrow ? "明日" : "今日" : Date.Length>=10 ? Date[5..] : "预报";
        return $"{label}    {WeatherService.Sky(Code)}    {Minimum:0.#}～{Maximum:0.#}°";
    }
}
internal sealed record WeatherData(string City,double Latitude,double Longitude,double Temperature,int Code,WeatherDay Today,WeatherDay Tomorrow,DateTimeOffset SavedAt);

internal sealed class WeatherService : IDisposable
{
    private readonly HttpClient http;
    private readonly string cachePath;
    private CancellationTokenSource? request;
    private string city="";
    private long nextAt;
    private bool busy,disposed;
    public WeatherData? Data{get;private set;}
    public string Status{get;private set;}="点此设置天气城市";
    public bool Cached{get;private set;}
    public event Action? Changed;
    internal WeatherService(HttpClient? client=null,string? cacheFile=null)
    {
        http=client ?? new HttpClient{Timeout=TimeSpan.FromSeconds(12)};
        cachePath=cacheFile ?? Path.Combine(Settings.Folder,"weather-cache.json");
    }
    public void SelectCity(string selected)
    {
        selected=selected.Trim();if(city==selected)return;
        city=selected;request?.Cancel();request?.Dispose();request=null;busy=false;nextAt=0;Data=null;Cached=false;
        Status=city.Length==0 ? "点此设置天气城市" : "正在查询天气…";
        try
        {
            var cached=JsonSerializer.Deserialize<WeatherData>(File.ReadAllText(cachePath));
            if(cached?.City==city && cached.Today!=null && cached.Tomorrow!=null && double.IsFinite(cached.Latitude) && double.IsFinite(cached.Longitude)){Data=cached;Cached=true;Status="缓存 · "+cached.SavedAt.ToLocalTime().ToString("MM-dd HH:mm");}
        }
        catch(Exception error) when(error is IOException or JsonException or UnauthorizedAccessException){ }
        Changed?.Invoke();
    }
    public Task RefreshIfDueAsync()
    {
        if(disposed || busy || city.Length==0 || Environment.TickCount64<nextAt)return Task.CompletedTask;
        request?.Dispose();request=new CancellationTokenSource();busy=true;
        return FetchAsync(city,request.Token);
    }
    private async Task FetchAsync(string selected,CancellationToken token)
    {
        try
        {
            double latitude,longitude;
            if(Data is {} cached && cached.City==selected){latitude=cached.Latitude;longitude=cached.Longitude;}
            else
            {
                var term=selected.EndsWith('市') && selected.Length>2 ? selected[..^1] : selected;
                using var geo=JsonDocument.Parse(await http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?language=zh&count=1&name="+Uri.EscapeDataString(term),token));
                if(!geo.RootElement.TryGetProperty("results",out var places) || places.GetArrayLength()==0)
                {
                    if(city==selected && !token.IsCancellationRequested){Status="未找到城市 · 点此修改";nextAt=Environment.TickCount64+600000;}
                    return;
                }
                latitude=places[0].GetProperty("latitude").GetDouble();longitude=places[0].GetProperty("longitude").GetDouble();
            }
            var url=FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=2&timezone=auto");
            using var document=JsonDocument.Parse(await http.GetStringAsync(url,token));
            var current=document.RootElement.GetProperty("current");var daily=document.RootElement.GetProperty("daily");
            WeatherDay Day(int index)=>new(daily.GetProperty("time")[index].GetString()!,daily.GetProperty("weather_code")[index].GetInt32(),daily.GetProperty("temperature_2m_min")[index].GetDouble(),daily.GetProperty("temperature_2m_max")[index].GetDouble());
            var result=new WeatherData(selected,latitude,longitude,current.GetProperty("temperature_2m").GetDouble(),current.GetProperty("weather_code").GetInt32(),Day(0),Day(1),DateTimeOffset.UtcNow);
            if(disposed || city!=selected || token.IsCancellationRequested)return;
            Data=result;Cached=false;Status="";nextAt=Environment.TickCount64+600000;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllText(cachePath+".tmp",JsonSerializer.Serialize(result));File.Move(cachePath+".tmp",cachePath,true);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){Status="天气已更新 · 缓存未保存";}
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){ }
        catch(Exception error) when(error is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException or FormatException)
        {
            if(!disposed && city==selected && !token.IsCancellationRequested)
            {
                Cached=Data!=null;Status=Data is {} cached ? "缓存 · "+cached.SavedAt.ToLocalTime().ToString("MM-dd HH:mm") : "网络请求失败，稍后重试";nextAt=Environment.TickCount64+60000;
            }
        }
        finally{if(city==selected && !token.IsCancellationRequested){busy=false;Changed?.Invoke();}}
    }
    internal static string Sky(int code)=>code switch
    {
        0=>"晴",1=>"大部晴朗",2=>"多云",3=>"阴",45 or 48=>"雾",51 or 53 or 55 or 56 or 57=>"毛毛雨",61 or 63 or 65 or 66 or 67 or 80 or 81 or 82=>"雨",71 or 73 or 75 or 77 or 85 or 86=>"雪",95 or 96 or 99=>"雷雨",_=>"未知"
    };
    public void Dispose(){disposed=true;request?.Cancel();request?.Dispose();http.Dispose();}
}

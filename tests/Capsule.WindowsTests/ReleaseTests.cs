using Capsule.App;
using System.Net;
using System.Net.Http;

internal static class ReleaseTests
{
    private sealed class Handler(HttpStatusCode code,string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(code){Content=new StringContent(body)});
    }
    internal static async Task RunAsync()
    {
        var narrow=new Settings{Width=20};narrow.Normalize();if(narrow.Width!=80)throw new Exception("Global width lower boundary is incorrect");
        var wide=new Settings{Width=500};wide.Normalize();if(wide.Width!=300)throw new Exception("Global width upper boundary is incorrect");
        var cache=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"capsule-source-test-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            using(var sources=new NotificationService(cacheFile:cache)){sources.Sources["test.source"]="测试来源";sources.PersistSourceNames();}
            using var restored=new NotificationService(cacheFile:cache);if(restored.Sources.GetValueOrDefault("test.source")!="测试来源")throw new Exception("Notification source names did not persist");
            var data=System.IO.File.ReadAllText(cache);if(data.Contains("Body") || data.Contains("Title"))throw new Exception("Source metadata contains message fields");
        }
        finally{if(System.IO.File.Exists(cache))System.IO.File.Delete(cache);}
        Console.WriteLine("PASS Notification source names survive restart without message fields");
        using var missing=new HttpClient(new Handler(HttpStatusCode.NotFound,""));
        if(!(await ReleaseChecker.CheckAsync(missing,new(0,1,0))).Message.Contains("暂无"))throw new Exception("Missing release misreported as current");
        using var newer=new HttpClient(new Handler(HttpStatusCode.OK,"{\"tag_name\":\"v0.2.0\",\"html_url\":\"https://invalid.example/\"}"));
        var result=await ReleaseChecker.CheckAsync(newer,new(0,1,0));
        if(result.Link?.Host!="github.com" || !result.Message.Contains("新版本"))throw new Exception("New release link validation failed");
        using var current=new HttpClient(new Handler(HttpStatusCode.OK,"{\"tag_name\":\"v0.1.0\"}"));
        if((await ReleaseChecker.CheckAsync(current,new(0,1,0))).Link!=null)throw new Exception("Current release was offered as upgrade");
        Console.WriteLine("PASS Release absence, version comparison and repository-bound download link");
    }
}

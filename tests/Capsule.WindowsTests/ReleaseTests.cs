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

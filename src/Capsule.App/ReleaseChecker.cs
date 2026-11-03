using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Capsule.App;

internal sealed record ReleaseCheck(string Message,Uri? Link=null);
internal static class ReleaseChecker
{
    internal static async Task<ReleaseCheck> CheckAsync(HttpClient http,Version current,CancellationToken token=default)
    {
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/Freedomzyi/LingdongCapsule/releases/latest");
            request.Headers.UserAgent.ParseAdd("LingdongCapsule/0.1.0");request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response=await http.SendAsync(request,token);
            if(response.StatusCode==HttpStatusCode.NotFound)return new("暂无正式发布版本；当前仍处于开发阶段。");
            if(response.StatusCode==HttpStatusCode.Forbidden || (int)response.StatusCode==429)return new("检查过于频繁或服务暂不可用，请稍后再试。");
            response.EnsureSuccessStatusCode();
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));var root=json.RootElement;
            var tag=root.GetProperty("tag_name").GetString() ?? "";
            if(!Version.TryParse(tag.TrimStart('v','V'),out var latest))return new("发现发布记录，但版本号无法比较，请到仓库查看。");
            if(latest<=current)return new("当前已是最新正式版本。");
            var link=new Uri("https://github.com/Freedomzyi/LingdongCapsule/releases/tag/"+Uri.EscapeDataString(tag));
            return new($"发现新版本 {tag}，可前往 GitHub 查看说明并下载。",link);
        }
        catch(OperationCanceledException){return new("检查已取消或网络请求超时，请稍后重试。");}
        catch(Exception error) when(error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException){return new("未能检查更新，请检查网络连接后重试。");}
    }
}

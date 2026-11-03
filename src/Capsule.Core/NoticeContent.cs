using System.Text.Json;

namespace Capsule.Core;

public sealed record NoticeContent(string Title,string Body,string? Avatar,string? Icon)
{
    public const int MaxImageSpec=6*1024*1024;
    public static NoticeContent? Parse(JsonElement payload)
    {
        if(payload.ValueKind!=JsonValueKind.Object)return null;
        static string Read(JsonElement root,params string[] names)
        {
            foreach(var name in names)if(root.TryGetProperty(name,out var item) && item.ValueKind==JsonValueKind.String)return item.GetString() ?? "";
            return "";
        }
        var title=Read(payload,"title","Title","name");var body=Read(payload,"body","Body","text","message");
        string? avatar=null,icon=null,unmarked=null;var fallback="";
        if(payload.TryGetProperty("content",out var blocks) && blocks.ValueKind==JsonValueKind.Array)
        {
            if(blocks.GetArrayLength()>64)return null;
            foreach(var block in blocks.EnumerateArray())
            {
                if(block.ValueKind!=JsonValueKind.Object)continue;
                var type=Read(block,"type");
                if(type=="text"){if(fallback.Length==0)fallback=Read(block,"text");continue;}
                string? spec=null;
                if(type=="resource_link")spec=Read(block,"uri");
                else if(type=="image")
                {
                    var data=Read(block,"data");var mime=Read(block,"mimeType");
                    if(mime.Length==0)mime="image/png";
                    if(data.Length>0 && data.Length<=MaxImageSpec && mime.Length<=64 && mime.StartsWith("image/",StringComparison.OrdinalIgnoreCase))spec="data:"+mime+";base64,"+data;
                }
                if(string.IsNullOrWhiteSpace(spec) || spec.Length>MaxImageSpec)continue;
                var role=block.TryGetProperty("annotations",out var annotations) && annotations.ValueKind==JsonValueKind.Object ? Read(annotations,"role").ToLowerInvariant() : "";
                if(role is "avatar" or "head" or "headimg" or "headimage" or "portrait" or "profile" or "photo")avatar??=spec;
                else if(role=="icon")icon??=spec;
                else unmarked??=spec;
            }
        }
        var direct=Read(payload,"avatar","avatarUrl","avatar_url","head","headimg");if(avatar==null && direct.Length is >0 and <=MaxImageSpec)avatar=direct;
        icon??=unmarked;
        if(string.IsNullOrWhiteSpace(body))body=fallback;
        if(title.Length>4096 || body.Length>16384 || string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body) && avatar==null && icon==null)return null;
        if(string.IsNullOrWhiteSpace(title))title=body.Length>0 ? body : "新通知";
        return new(title,body,avatar,icon);
    }
}

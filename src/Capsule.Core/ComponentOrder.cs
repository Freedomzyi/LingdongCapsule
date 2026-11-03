using System.Text.RegularExpressions;
namespace Capsule.Core;

public static class ComponentOrder
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? configured,bool hardwareFirst=false,bool mediaFirst=false)
    {
        var order=(configured ?? []).Where(id=>id!=null && (id is "clock" or "hardware" or "media" || Regex.IsMatch(id,@"^plugin:[a-z][a-z0-9.-]{2,79}$"))).Distinct(StringComparer.Ordinal).ToList();
        if(order.Count==0){if(mediaFirst)order.Add("media");order.AddRange(hardwareFirst ? ["hardware","clock"] : new[]{"clock","hardware"});}
        foreach(var id in new[]{"clock","hardware","media"})if(!order.Contains(id))order.Add(id);
        return order;
    }
    public static IReadOnlyList<string> Effective(IEnumerable<string>? configured,IEnumerable<string> plugins,bool hardwareFirst=false,bool mediaFirst=false)=>Normalize(configured,hardwareFirst,mediaFirst).Concat(plugins.Select(id=>"plugin:"+id)).Distinct(StringComparer.Ordinal).ToArray();
}

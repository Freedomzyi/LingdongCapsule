using System.IO;
using System.Windows.Media;

namespace Capsule.App;

internal static class LocalFont
{
    private static string? cachedPath;
    private static long cachedLength,cachedWrite;
    private static FontFamily? cachedFamily;
    internal static FontFamily Load(string path)
    {
        if(path.Length>4096 || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("请选择本地字体文件。");
        path=Path.GetFullPath(path);
        if(!new[]{".ttf",".otf",".ttc"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))throw new IOException("支持 TTF、OTF 和 TTC 字体文件。");
        var info=new FileInfo(path);
        if(!info.Exists)throw new FileNotFoundException("字体文件已移动或不存在。");
        if(info.Length is <12 or >64*1024*1024)throw new IOException("字体文件大小须在 12 字节到 64 MiB 之间。");
        if(cachedFamily!=null && string.Equals(cachedPath,path,StringComparison.OrdinalIgnoreCase) && cachedLength==info.Length && cachedWrite==info.LastWriteTimeUtc.Ticks)return cachedFamily;
        var uri=new Uri(path);var face=new GlyphTypeface(uri);
        var name=face.FamilyNames.OrderBy(pair=>pair.Key.Name=="en-US" ? 0 : 1).Select(pair=>pair.Value).FirstOrDefault();
        if(string.IsNullOrWhiteSpace(name))throw new IOException("字体没有可用的字体家族名称。");
        var family=new FontFamily(uri.AbsoluteUri+"#"+name);
        // WPF can silently substitute a system font for an invalid family URI.
        if(!family.GetTypefaces().Any(type=>type.TryGetGlyphTypeface(out var glyph) && glyph.FontUri.IsFile && string.Equals(Path.GetFullPath(glyph.FontUri.LocalPath),path,StringComparison.OrdinalIgnoreCase)))throw new IOException("无法从所选文件加载字体家族。");
        cachedPath=path;cachedLength=info.Length;cachedWrite=info.LastWriteTimeUtc.Ticks;cachedFamily=family;return family;
    }
    internal static bool IsLoadError(Exception error)=>error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or FormatException;
}

using System.IO;
using System.Text.Json;
namespace Capsule.App;
public sealed class Settings
{
    public double Width { get; set; } = 280;
    public int MotionMs { get; set; } = 350;
    public bool ShowInCapture { get; set; } = true;
    public bool PrivatePreview { get; set; } = true;
    public bool NotificationsEnabled { get; set; }
    public HashSet<string> MutedSources { get; set; } = [];
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LingdongCapsule");
    public static Settings Load()
    {
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder, "settings.json"))) ?? new(); s.Width = Math.Clamp(s.Width, 220, 480); s.MotionMs = Math.Clamp(s.MotionMs, 150, 900); s.MutedSources ??= []; return s; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}

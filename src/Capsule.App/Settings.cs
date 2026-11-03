using System.IO;
using System.Text.Json;
namespace Capsule.App;

public sealed class Settings
{
    public double Width { get; set; } = 280;
    public double CompactHeight { get; set; } = 35;
    public int MotionMs { get; set; } = 450;
    public bool ShowInCapture { get; set; } = true;
    public bool PrivatePreview { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool OnboardingCompleted { get; set; }
    public bool LyricsEnabled { get; set; }=true;
    public bool TranslationEnabled { get; set; }=true;
    public bool KaraokeEnabled { get; set; }=true;
    public bool ScrollLyrics { get; set; }=true;
    public double LyricOffsetSeconds { get; set; }
    public bool MediaEnabled {get;set;}=true;
    public bool ExpandedMediaControls {get;set;}=true;
    public bool ReturnToPlayer {get;set;}=true;
    public bool CoverProgress {get;set;}=true;
    public bool ShowSpectrum {get;set;}=true;
    public bool MediaClockSwitch {get;set;}=true;
    public string MediaSourceKind {get;set;}="other";
    public string ManualMediaSource {get;set;}="";
    public bool Topmost {get;set;}=true;
    public string MonitorId {get;set;}="";
    public int IslandShape {get;set;}=1;
    public double BottomRadius {get;set;}=12;
    public HashSet<string> MutedSources { get; set; } = [];
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LingdongCapsule");
    public static Settings Load()
    {
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder, "settings.json"))) ?? new(); s.Width = double.IsFinite(s.Width) ? Math.Clamp(s.Width, 220, 480) : 280; s.CompactHeight = double.IsFinite(s.CompactHeight) ? Math.Clamp(s.CompactHeight, 32, 60) : 35; s.MotionMs = Math.Clamp(s.MotionMs, 150, 900); s.MutedSources ??= []; return s; }
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

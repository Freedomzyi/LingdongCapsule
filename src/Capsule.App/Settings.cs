using System.IO;
using System.Text.Json;
using Capsule.Core;
namespace Capsule.App;

public sealed partial class Settings
{
    public Dictionary<string,Dictionary<string,string>> PluginPreferences{get;set;}=[];
    public List<string> PluginFiles{get;set;}=[];
    public List<string> PendingPluginRemovals{get;set;}=[];
    public Dictionary<string,string> PluginFailures{get;set;}=[];
    public int ThemeMode{get;set;}=2;
    public double BackgroundOpacity{get;set;}=96;
    public int MaterialKind{get;set;}
    public int GlassQuality{get;set;}=1;
    public GlassOptions Glass{get;set;}=new();
    public bool MspEnabled{get;set;}
    public bool MspPublishEvents{get;set;}
    public string FontName{get;set;}="";
    public bool ClockEnabled{get;set;}=true;
    public int ClockLayout{get;set;}
    public bool HardwareEnabled{get;set;}
    public bool HardwareFirst{get;set;}
    public bool CompositeEnabled{get;set;}
    public bool MediaFirst{get;set;}
    public List<string> ComponentOrder{get;set;}=[];
    public bool StandbyActive{get;set;}
    public int StandbyScene{get;set;}=1;
    public bool StandbyDoubleClick{get;set;}
    public bool MediaShortcutsEnabled{get;set;}
    public bool IslandShortcutEnabled{get;set;}
    public Dictionary<ShortcutAction,ShortcutChord> Shortcuts{get;set;}=Enum.GetValues<ShortcutAction>().ToDictionary(action=>action,ShortcutChord.Default);
    public bool AutoHide{get;set;}
    public bool FocusAutoHide{get;set;}
    public bool PauseAutoHide{get;set;}
    public bool FullscreenAutoHide{get;set;}
    public bool Passthrough{get;set;}
    public double Width { get; set; } = 280;
    public double CompactHeight { get; set; } = 35;
    public int MotionMs { get; set; } = 450;
    public bool ShowInCapture { get; set; } = true;
    public bool PrivatePreview { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public int NotificationMode{get;set;}
    public double NotificationHeight{get;set;}=55;
    public double NotificationIconSize{get;set;}=38;
    public double NotificationIconGap{get;set;}=6;
    public bool GroupNotifications{get;set;}=true;
    public bool ExpandNotifications{get;set;}=true;
    public bool SwipeNotifications{get;set;}=true;
    public DateTimeOffset QuietUntil{get;set;}
    public bool NotificationSoundEnabled{get;set;}
    public string NotificationSoundKey{get;set;}="none";
    public string NotificationSoundPath{get;set;}="";
    public double NotificationSoundVolume{get;set;}=10;
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
    public bool OnlineCoversEnabled {get;set;}=true;
    public bool ShowSpectrum {get;set;}=true;
    public bool MediaClockSwitch {get;set;}=true;
    public string MediaSourceKind {get;set;}="other";
    public string ManualMediaSource {get;set;}="";
    public bool Topmost {get;set;}=true;
    public string MonitorId {get;set;}="";
    public int IslandShape {get;set;}=1;
    public double BottomRadius {get;set;}=12;
    public bool OverviewEnabled {get;set;}=true;
    public bool ShowTomorrow {get;set;}=true;
    public string WeatherCity {get;set;}="";
    public HashSet<string> MutedSources { get; set; } = [];
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LingdongCapsule");
    public static Settings Load()
    {
        return LoadFrom(Path.Combine(Folder,"settings.json"));
    }
    internal void Normalize()
    {
        PluginPreferences??=[];
        PluginFiles??=[];
        PendingPluginRemovals??=[];
        PluginFailures??=[];
        ComponentOrder=Capsule.Core.ComponentOrder.Normalize(ComponentOrder,HardwareFirst,MediaFirst).ToList();
        PluginFiles=PluginFiles.Where(path=>!string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        PendingPluginRemovals=PendingPluginRemovals.Where(path=>!string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach(var key in PluginPreferences.Keys.ToArray())PluginPreferences[key]??=[];
        ThemeMode=Math.Clamp(ThemeMode,0,2);
        FontName??="";
        ClockLayout=Math.Clamp(ClockLayout,0,1);
        StandbyScene=Math.Clamp(StandbyScene,1,3);
        static double Limit(double value,double min,double max,double fallback)=>double.IsFinite(value) ? Math.Clamp(value,min,max) : fallback;
        Width=Limit(Width,80,300,280);CompactHeight=Limit(CompactHeight,32,60,35);MotionMs=Math.Clamp(MotionMs,150,900);
        BackgroundOpacity=Limit(BackgroundOpacity,0,100,96);
        MaterialKind=Math.Clamp(MaterialKind,0,2);GlassQuality=Math.Clamp(GlassQuality,0,2);Glass=(Glass ?? new()).Normalized();
        NotificationMode=Math.Clamp(NotificationMode,0,2);NotificationHeight=Limit(NotificationHeight,40,120,55);
        NotificationIconSize=Limit(NotificationIconSize,18,Width,38);NotificationIconGap=Limit(NotificationIconGap,0,20,6);
        NotificationSoundVolume=Limit(NotificationSoundVolume,0,100,10);NotificationSoundKey??="none";NotificationSoundPath??="";
        if(!NotificationSound.Options().ContainsKey(NotificationSoundKey))NotificationSoundKey="none";
        BottomRadius=Limit(BottomRadius,0,28,12);IslandShape=Math.Clamp(IslandShape,0,1);MonitorId??="";
        MediaSourceKind??="other";ManualMediaSource??="";WeatherCity=(WeatherCity ?? "").Trim();if(WeatherCity.Length>80)WeatherCity=WeatherCity[..80];
        LyricOffsetSeconds=Limit(LyricOffsetSeconds,-3,3,0);MutedSources=new(MutedSources ?? [],StringComparer.OrdinalIgnoreCase);
        Shortcuts??=[];foreach(var action in Enum.GetValues<ShortcutAction>())if(!Shortcuts.TryGetValue(action,out var chord) || chord==null || !chord.IsValid)Shortcuts[action]=ShortcutChord.Default(action);
    }
    public void Save()
    {
        SaveTo(Path.Combine(Folder,"settings.json"));
    }
}

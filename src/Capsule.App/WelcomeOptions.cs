namespace Capsule.App;

internal sealed record WelcomeOptions(int Shape,int Theme,int Material,string Monitor,bool Standby)
{
    internal static WelcomeOptions Read(Settings settings)=>new(settings.IslandShape,settings.ThemeMode,settings.MaterialKind,settings.MonitorId,settings.StandbyActive);
    internal void Apply(Settings settings)
    {settings.IslandShape=Shape;settings.ThemeMode=Theme;settings.MaterialKind=Material;settings.MonitorId=Monitor;settings.StandbyActive=Standby;}
    internal static void Commit(Settings settings,WelcomeOptions? choices,bool? startup,Action save,Action<bool,Action> writeStartup)
    {
        var previous=Read(settings);var completed=settings.OnboardingCompleted;var previousStartup=settings.StartWithWindows;
        choices?.Apply(settings);settings.OnboardingCompleted=true;
        if(startup is {} enabled)settings.StartWithWindows=enabled;
        try{if(startup is {} value)writeStartup(value,save);else save();}
        catch{previous.Apply(settings);settings.OnboardingCompleted=completed;settings.StartWithWindows=previousStartup;throw;}
    }
}

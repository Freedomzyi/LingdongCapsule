using System.Windows;

namespace Capsule.PluginApi;

public sealed record PluginIdentity(string Id,string DisplayName,string Version);
public sealed record PluginMedia(string Title,string Artist,bool Available,bool Playing);
public sealed record PluginFrame(DateTimeOffset Now,bool DarkTheme,PluginMedia Media);

// Implementations are invoked on the desktop UI dispatcher. Plugins own their views
// and must release timers and event subscriptions when disposed.
public interface ICapsulePlugin : IDisposable
{
    PluginIdentity Identity{get;}
    FrameworkElement CreateWidget(ICapsulePluginHost host);
    void Update(PluginFrame frame);
}

// Optional deadline work for enabled widgets whose presentation is not visible.
// Called on the UI dispatcher at the host cadence; must remain lightweight.
public interface ICapsuleBackgroundPlugin
{
    void UpdateBackground(PluginFrame frame);
}

public interface ICapsulePluginHost
{
    PluginMedia CurrentMedia{get;}
    string ReadSetting(string key,string fallback="");
    void WriteSetting(string key,string value);
    Task<bool> NotifyAsync(string title,string body);
}

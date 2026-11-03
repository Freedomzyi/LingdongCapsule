namespace Capsule.Core;

public record MediaSnapshot(string Title, string Artist, bool Playing, bool Available, string Album = "", TimeSpan Duration = default, string SourceId = "");
public record Notice(string SourceId, string App, string Title, string Body);
public enum IslandMode { Clock, Media, Notification }

// Media is live state, never a saved UI frame. Dismissing an overlay resolves it again.
public sealed class IslandState
{
    public MediaSnapshot Media { get; private set; } = new("", "", false, false);
    public Notice? Notification { get; private set; }
    public bool Expanded { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool MediaClockSwitch {get;set;}=true;
    public IslandMode Mode => Notification is not null ? IslandMode.Notification : Media.Available && (Media.Playing || !MediaClockSwitch) ? IslandMode.Media : IslandMode.Clock;
    public void UpdateMedia(MediaSnapshot media) => Media = media;
    public void Show(Notice notice, DateTimeOffset now) { Notification = notice; Expanded = false; ExpiresAt = now.AddSeconds(6); }
    public void Expand(DateTimeOffset now) { Expanded = true; ExpiresAt = now.AddSeconds(20); }
    public void Dismiss() { Notification = null; Expanded = false; }
    public void Tick(DateTimeOffset now) { if (Notification is not null && now >= ExpiresAt) Dismiss(); }
}

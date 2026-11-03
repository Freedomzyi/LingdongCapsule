namespace Capsule.Core;

public record MediaSnapshot(string Title, string Artist, bool Playing, bool Available, string Album = "", TimeSpan Duration = default, string SourceId = "",bool IsVideo=false);
public record Notice(string SourceId, string App, string Title, string Body, uint Id=0,DateTimeOffset CreatedAt=default,string? Avatar=null,string? Icon=null);
public enum IslandMode { Clock, Media, Notification }

// Media is live state, never a saved UI frame. Dismissing an overlay resolves it again.
public sealed class IslandState
{
    public MediaSnapshot Media { get; private set; } = new("", "", false, false);
    public Notice? Notification { get; private set; }
    public bool Expanded { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool MediaClockSwitch {get;set;}=true;
    public bool GroupNotifications{get;set;}=true;
    public int GroupCount{get;private set;}
    public bool LastNoticeGrouped{get;private set;}
    public DateTimeOffset QuietUntil{get;private set;}
    public bool IsQuiet(DateTimeOffset now)=>now<QuietUntil;
    public IslandMode Mode => Notification is not null ? IslandMode.Notification : Media.Available && (Media.Playing || !MediaClockSwitch) ? IslandMode.Media : IslandMode.Clock;
    public void UpdateMedia(MediaSnapshot media) => Media = media;
    public bool Show(Notice notice, DateTimeOffset now)
    {
        if(IsQuiet(now))return false;
        LastNoticeGrouped=GroupNotifications && Notification is {} previous && now<ExpiresAt && notice.SourceId.Length>0 && string.Equals(previous.SourceId,notice.SourceId,StringComparison.OrdinalIgnoreCase) && notice.Id!=previous.Id;
        GroupCount=LastNoticeGrouped ? GroupCount+1 : 1;
        Notification=notice;Expanded=false;ExpiresAt=now.AddSeconds(6);return true;
    }
    public void SetQuietUntil(DateTimeOffset until,DateTimeOffset now){QuietUntil=until;if(IsQuiet(now))Dismiss();}
    public void Expand(DateTimeOffset now) { Expanded = true; ExpiresAt = now.AddSeconds(20); }
    public void Dismiss() { Notification = null; Expanded = false;GroupCount=0;LastNoticeGrouped=false; }
    public void Tick(DateTimeOffset now) { if (Notification is not null && now >= ExpiresAt) Dismiss(); }
}

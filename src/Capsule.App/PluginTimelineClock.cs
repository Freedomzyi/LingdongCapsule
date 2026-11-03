using Capsule.PluginApi;

namespace Capsule.App;

internal sealed record PluginTimelineReading(string PluginId, PluginTimeline Timeline);
internal sealed record ResolvedPluginTimeline(TimeSpan Position, TimeSpan Duration, bool Playing);

internal sealed class PluginTimelineClock
{
    private (string Plugin, string Source, string Title, string Artist, string Track)? identity;
    private TimeSpan snapshotPosition, anchorPosition;
    private bool advancing;
    private long anchorAt;

    internal static bool Eligible(string source, TimeSpan nativeDuration) => nativeDuration <= TimeSpan.Zero &&
        (source.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) || source.Contains("netease", StringComparison.OrdinalIgnoreCase) || source.Contains("kugou", StringComparison.OrdinalIgnoreCase));

    internal static bool Valid(PluginTimeline timeline) => !string.IsNullOrWhiteSpace(timeline.TrackKey) &&
        timeline.TrackKey.Length <= 512 && timeline.Duration > TimeSpan.Zero && timeline.Position >= TimeSpan.Zero;

    internal void Reset() => identity = null;

    internal ResolvedPluginTimeline? Read(PluginMediaSession session, PluginTimelineReading? reading, long now)
    {
        if (reading == null || !Valid(reading.Timeline)) { Reset(); return null; }
        var value = reading.Timeline;
        var key = (reading.PluginId, session.SourceId, session.Title, session.Artist, value.TrackKey);
        var position = value.Position > value.Duration ? value.Duration : value.Position;
        if (identity != key || snapshotPosition != position || advancing != value.Playing)
        {
            identity = key; snapshotPosition = anchorPosition = position; anchorAt = now; advancing = value.Playing;
        }
        var elapsed = advancing ? Math.Max(0, now - anchorAt) : 0;
        var remaining = value.Duration.Ticks - Math.Min(anchorPosition.Ticks, value.Duration.Ticks);
        var extra = Math.Min(elapsed, remaining / TimeSpan.TicksPerMillisecond) * TimeSpan.TicksPerMillisecond;
        return new(TimeSpan.FromTicks(Math.Min(anchorPosition.Ticks, value.Duration.Ticks) + extra), value.Duration, advancing);
    }
}

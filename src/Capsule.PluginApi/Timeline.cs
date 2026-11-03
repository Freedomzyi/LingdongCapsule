namespace Capsule.PluginApi;

public sealed record PluginMediaSession(string SourceId, string Title, string Artist, bool Playing);

// TrackKey identifies the provider's cached track without replacing native metadata.
public sealed record PluginTimeline(string TrackKey, TimeSpan Position, TimeSpan Duration, bool Playing, int Priority = 0);

// Optional alongside ICapsulePlugin. Read cached state only, on the UI dispatcher.
// Return null when the requested native session does not match the provider's data.
public interface ICapsuleTimelineSource
{
    PluginTimeline? ReadTimeline(PluginMediaSession session);
}

using System.Text.Json;

namespace WindowsMediaControl.Messaging;

// Message-bus topics for the Windows Media Control integration (Macro Deck 3
// beta.12 channel). Track/playback/volume/mute mirror the integration events;
// topics are a stable public contract, so keep them stable and additive.
public static class MediaMessageTopics
{
	public const string TrackChanged = "windows-media.track-changed";
	public const string PlaybackChanged = "windows-media.playback-changed";
	public const string VolumeChanged = "windows-media.volume-changed";
	public const string MuteChanged = "windows-media.mute-changed";
	public const string StateGet = "windows-media.state.get";
}

internal static class MediaMessageJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record TrackChangedMessage(string Title, string Artist, string Album, string App);

public sealed record PlaybackChangedMessage(string Status, bool IsPlaying);

public sealed record VolumeChangedMessage(double Volume, bool Muted);

public sealed record MuteChangedMessage(bool Muted);

public sealed record MediaStateMessage(
	bool HasSession,
	string? Title,
	string? Artist,
	string? Album,
	string? App,
	string Status,
	bool IsPlaying,
	double? VolumePercent,
	bool IsMuted,
	double PositionSeconds,
	double DurationSeconds,
	double ProgressPercent);

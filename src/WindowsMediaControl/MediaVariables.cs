using MacroDeck.Sdk.Variables;
using WindowsMediaControl.Media;

namespace WindowsMediaControl;

internal static class MediaVariables
{
	public static IReadOnlyList<VariableDefinition> CreateDefinitions() =>
	[
		Eager("title", VariableType.Text, Strings.Variables.Title.DisplayName(), Strings.Variables.Title.Description()),
		Eager("artist", VariableType.Text, Strings.Variables.Artist.DisplayName(), Strings.Variables.Artist.Description()),
		Eager("album", VariableType.Text, Strings.Variables.Album.DisplayName(), Strings.Variables.Album.Description()),
		Eager("source-app", VariableType.Text, Strings.Variables.SourceApp.DisplayName(), Strings.Variables.SourceApp.Description()),
		Eager("playback-status", VariableType.Text, Strings.Variables.PlaybackStatus.DisplayName(), Strings.Variables.PlaybackStatus.Description()),
		Eager("is-playing", VariableType.Boolean, Strings.Variables.IsPlaying.DisplayName(), Strings.Variables.IsPlaying.Description(), name: "media_is_playing"),
		Eager("has-media", VariableType.Boolean, Strings.Variables.HasMedia.DisplayName(), Strings.Variables.HasMedia.Description()),
		Eager("position-seconds", VariableType.Numeric, Strings.Variables.PositionSeconds.DisplayName(), Strings.Variables.PositionSeconds.Description(), unit: "s", semanticKind: VariableSemanticKinds.Duration, refresh: TimeSpan.FromSeconds(2), write: new VariableWriteCapability { CommitOnRelease = true }),
		Eager("duration-seconds", VariableType.Numeric, Strings.Variables.DurationSeconds.DisplayName(), Strings.Variables.DurationSeconds.Description(), unit: "s", semanticKind: VariableSemanticKinds.Duration),
		Eager("position-text", VariableType.Text, Strings.Variables.PositionText.DisplayName(), Strings.Variables.PositionText.Description(), refresh: TimeSpan.FromSeconds(2)),
		Eager("duration-text", VariableType.Text, Strings.Variables.DurationText.DisplayName(), Strings.Variables.DurationText.Description()),
		Eager("progress-percent", VariableType.Numeric, Strings.Variables.ProgressPercent.DisplayName(), Strings.Variables.ProgressPercent.Description(), unit: "%", semanticKind: VariableSemanticKinds.Percentage, refresh: TimeSpan.FromSeconds(2), write: new VariableWriteCapability { CommitOnRelease = true }),
		VariableDefinition.Eager("volume-percent", VariableType.Numeric) with
		{
			Name = "volume_percent",
			DisplayName = Strings.Variables.VolumePercent.DisplayName(),
			Description = Strings.Variables.VolumePercent.Description(),
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			RefreshInterval = TimeSpan.FromSeconds(5),
			Write = new VariableWriteCapability(),
		},
		VariableDefinition.Eager("is-muted", VariableType.Boolean) with
		{
			Name = "is_muted",
			DisplayName = Strings.Variables.IsMuted.DisplayName(),
			Description = Strings.Variables.IsMuted.Description(),
			Write = new VariableWriteCapability(),
		},
		Eager("shuffle-enabled", VariableType.Boolean, Strings.Variables.ShuffleEnabled.DisplayName(), Strings.Variables.ShuffleEnabled.Description()),
		Eager("repeat-mode", VariableType.Text, Strings.Variables.RepeatMode.DisplayName(), Strings.Variables.RepeatMode.Description()),
		Eager("album-artist", VariableType.Text, Strings.Variables.AlbumArtist.DisplayName(), Strings.Variables.AlbumArtist.Description()),
		Eager("genres", VariableType.Text, Strings.Variables.Genres.DisplayName(), Strings.Variables.Genres.Description()),
		Eager("track-number", VariableType.Numeric, Strings.Variables.TrackNumber.DisplayName(), Strings.Variables.TrackNumber.Description()),
		Eager("track-count", VariableType.Numeric, Strings.Variables.TrackCount.DisplayName(), Strings.Variables.TrackCount.Description()),
		Eager("subtitle", VariableType.Text, Strings.Variables.Subtitle.DisplayName(), Strings.Variables.Subtitle.Description()),
		Eager("playback-type", VariableType.Text, Strings.Variables.PlaybackType.DisplayName(), Strings.Variables.PlaybackType.Description()),
		Eager("playback-rate", VariableType.Numeric, Strings.Variables.PlaybackRate.DisplayName(), Strings.Variables.PlaybackRate.Description()),
		Eager("is-live", VariableType.Boolean, Strings.Variables.IsLive.DisplayName(), Strings.Variables.IsLive.Description()),
		Eager("can-play", VariableType.Boolean, Strings.Variables.CanPlay.DisplayName(), Strings.Variables.CanPlay.Description()),
		Eager("can-pause", VariableType.Boolean, Strings.Variables.CanPause.DisplayName(), Strings.Variables.CanPause.Description()),
		Eager("can-stop", VariableType.Boolean, Strings.Variables.CanStop.DisplayName(), Strings.Variables.CanStop.Description()),
		Eager("can-next", VariableType.Boolean, Strings.Variables.CanNext.DisplayName(), Strings.Variables.CanNext.Description()),
		Eager("can-previous", VariableType.Boolean, Strings.Variables.CanPrevious.DisplayName(), Strings.Variables.CanPrevious.Description()),
		Eager("can-seek", VariableType.Boolean, Strings.Variables.CanSeek.DisplayName(), Strings.Variables.CanSeek.Description()),
		Eager("can-shuffle", VariableType.Boolean, Strings.Variables.CanShuffle.DisplayName(), Strings.Variables.CanShuffle.Description()),
		Eager("can-repeat", VariableType.Boolean, Strings.Variables.CanRepeat.DisplayName(), Strings.Variables.CanRepeat.Description()),
		Eager("default-device", VariableType.Text, Strings.Variables.DefaultDevice.DisplayName(), Strings.Variables.DefaultDevice.Description()),
		Eager("cover-accent", VariableType.Text, Strings.Variables.CoverAccent.DisplayName(), Strings.Variables.CoverAccent.Description()),
	];

	public static VariableDefinition AppVolume(string processName) =>
		VariableDefinition.OnDemand(processName, VariableType.Numeric) with
		{
			Name = SanitizeName(processName),
			DisplayName = $"{processName} volume",
			Description = Strings.Variables.AppVolume.Description(),
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			IsBindable = true,
			IsContainer = false,
			Write = new VariableWriteCapability(),
		};

	private static string SanitizeName(string processName)
	{
		var builder = new System.Text.StringBuilder("app_");
		foreach (var ch in processName)
		{
			builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
		}

		return builder.ToString();
	}

	private static VariableDefinition Eager(
		string id,
		VariableType type,
		MacroDeck.Localization.LocalizedText displayName,
		MacroDeck.Localization.LocalizedText description,
		string? unit = null,
		string? semanticKind = null,
		TimeSpan? refresh = null,
		VariableWriteCapability? write = null,
		string? name = null) =>
		VariableDefinition.Eager(id, type) with
		{
			Name = name ?? id.Replace("-", "_"),
			DisplayName = displayName,
			Description = description,
			Unit = unit ?? string.Empty,
			SemanticKind = semanticKind ?? VariableSemanticKinds.None,
			RefreshInterval = refresh,
			Write = write,
		};
}

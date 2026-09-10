using MacroDeck.Sdk.Variables;
using WindowsMediaControl.Media;

namespace WindowsMediaControl;

internal static class MediaVariables
{
	private static readonly TimeSpan FastRefresh = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan SlowRefresh = TimeSpan.FromSeconds(5);

	public static IReadOnlyList<VariableDefinition> CreateDefinitions() =>
	[
		Eager("title", VariableType.Text, Strings.Variables.Title.DisplayName(), Strings.Variables.Title.Description(), refresh: SlowRefresh),
		Eager("artist", VariableType.Text, Strings.Variables.Artist.DisplayName(), Strings.Variables.Artist.Description(), refresh: SlowRefresh),
		Eager("album", VariableType.Text, Strings.Variables.Album.DisplayName(), Strings.Variables.Album.Description(), refresh: SlowRefresh),
		Eager("source-app", VariableType.Text, Strings.Variables.SourceApp.DisplayName(), Strings.Variables.SourceApp.Description(), refresh: SlowRefresh),
		Eager("playback-status", VariableType.Text, Strings.Variables.PlaybackStatus.DisplayName(), Strings.Variables.PlaybackStatus.Description(), refresh: FastRefresh),
		Eager("is-playing", VariableType.Boolean, Strings.Variables.IsPlaying.DisplayName(), Strings.Variables.IsPlaying.Description(), refresh: FastRefresh, name: "media_is_playing"),
		Eager("has-media", VariableType.Boolean, Strings.Variables.HasMedia.DisplayName(), Strings.Variables.HasMedia.Description(), refresh: FastRefresh),
		Eager("position-seconds", VariableType.Numeric, Strings.Variables.PositionSeconds.DisplayName(), Strings.Variables.PositionSeconds.Description(), unit: "s", semanticKind: VariableSemanticKinds.Duration, refresh: TimeSpan.FromSeconds(2), write: new VariableWriteCapability { CommitOnRelease = true }),
		Eager("duration-seconds", VariableType.Numeric, Strings.Variables.DurationSeconds.DisplayName(), Strings.Variables.DurationSeconds.Description(), unit: "s", semanticKind: VariableSemanticKinds.Duration, refresh: SlowRefresh),
		Eager("position-text", VariableType.Text, Strings.Variables.PositionText.DisplayName(), Strings.Variables.PositionText.Description(), refresh: TimeSpan.FromSeconds(2)),
		Eager("duration-text", VariableType.Text, Strings.Variables.DurationText.DisplayName(), Strings.Variables.DurationText.Description(), refresh: SlowRefresh),
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
			RefreshInterval = FastRefresh,
			Write = new VariableWriteCapability(),
		},
		VariableDefinition.Eager("mic-volume-percent", VariableType.Numeric) with
		{
			Name = "mic_volume_percent",
			DisplayName = Strings.Variables.MicVolume.DisplayName(),
			Description = Strings.Variables.MicVolume.Description(),
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			RefreshInterval = TimeSpan.FromSeconds(5),
			Write = new VariableWriteCapability(),
		},
		VariableDefinition.Eager("is-mic-muted", VariableType.Boolean) with
		{
			Name = "is_mic_muted",
			DisplayName = Strings.Variables.MicMuted.DisplayName(),
			Description = Strings.Variables.MicMuted.Description(),
			RefreshInterval = FastRefresh,
			Write = new VariableWriteCapability(),
		},
		Eager("mic-level-percent", VariableType.Numeric, Strings.Variables.MicLevel.DisplayName(), Strings.Variables.MicLevel.Description(), unit: "%", semanticKind: VariableSemanticKinds.Percentage, refresh: TimeSpan.FromMilliseconds(250)),
		Eager("system-level-percent", VariableType.Numeric, Strings.Variables.SystemLevel.DisplayName(), Strings.Variables.SystemLevel.Description(), unit: "%", semanticKind: VariableSemanticKinds.Percentage, refresh: TimeSpan.FromMilliseconds(250)),
		Eager("active-apps", VariableType.Text, Strings.Variables.ActiveApps.DisplayName(), Strings.Variables.ActiveApps.Description(), refresh: SlowRefresh),
		Eager("shuffle-enabled", VariableType.Boolean, Strings.Variables.ShuffleEnabled.DisplayName(), Strings.Variables.ShuffleEnabled.Description(), refresh: FastRefresh),
		Eager("repeat-mode", VariableType.Text, Strings.Variables.RepeatMode.DisplayName(), Strings.Variables.RepeatMode.Description(), refresh: FastRefresh),
		Eager("album-artist", VariableType.Text, Strings.Variables.AlbumArtist.DisplayName(), Strings.Variables.AlbumArtist.Description(), refresh: SlowRefresh),
		Eager("genres", VariableType.Text, Strings.Variables.Genres.DisplayName(), Strings.Variables.Genres.Description(), refresh: SlowRefresh),
		Eager("track-number", VariableType.Numeric, Strings.Variables.TrackNumber.DisplayName(), Strings.Variables.TrackNumber.Description(), refresh: SlowRefresh),
		Eager("track-count", VariableType.Numeric, Strings.Variables.TrackCount.DisplayName(), Strings.Variables.TrackCount.Description(), refresh: SlowRefresh),
		Eager("subtitle", VariableType.Text, Strings.Variables.Subtitle.DisplayName(), Strings.Variables.Subtitle.Description(), refresh: SlowRefresh),
		Eager("playback-type", VariableType.Text, Strings.Variables.PlaybackType.DisplayName(), Strings.Variables.PlaybackType.Description(), refresh: SlowRefresh),
		Eager("playback-rate", VariableType.Numeric, Strings.Variables.PlaybackRate.DisplayName(), Strings.Variables.PlaybackRate.Description(), refresh: SlowRefresh),
		Eager("is-live", VariableType.Boolean, Strings.Variables.IsLive.DisplayName(), Strings.Variables.IsLive.Description(), refresh: FastRefresh),
		Eager("can-play", VariableType.Boolean, Strings.Variables.CanPlay.DisplayName(), Strings.Variables.CanPlay.Description(), refresh: FastRefresh),
		Eager("can-pause", VariableType.Boolean, Strings.Variables.CanPause.DisplayName(), Strings.Variables.CanPause.Description(), refresh: FastRefresh),
		Eager("can-stop", VariableType.Boolean, Strings.Variables.CanStop.DisplayName(), Strings.Variables.CanStop.Description(), refresh: FastRefresh),
		Eager("can-next", VariableType.Boolean, Strings.Variables.CanNext.DisplayName(), Strings.Variables.CanNext.Description(), refresh: FastRefresh),
		Eager("can-previous", VariableType.Boolean, Strings.Variables.CanPrevious.DisplayName(), Strings.Variables.CanPrevious.Description(), refresh: FastRefresh),
		Eager("can-seek", VariableType.Boolean, Strings.Variables.CanSeek.DisplayName(), Strings.Variables.CanSeek.Description(), refresh: FastRefresh),
		Eager("can-shuffle", VariableType.Boolean, Strings.Variables.CanShuffle.DisplayName(), Strings.Variables.CanShuffle.Description(), refresh: FastRefresh),
		Eager("can-repeat", VariableType.Boolean, Strings.Variables.CanRepeat.DisplayName(), Strings.Variables.CanRepeat.Description(), refresh: FastRefresh),
		Eager("default-device", VariableType.Text, Strings.Variables.DefaultDevice.DisplayName(), Strings.Variables.DefaultDevice.Description(), refresh: SlowRefresh),
		Eager("default-input-device", VariableType.Text, Strings.Variables.DefaultInputDevice.DisplayName(), Strings.Variables.DefaultInputDevice.Description(), refresh: SlowRefresh),
		Eager("cover-accent", VariableType.Text, Strings.Variables.CoverAccent.DisplayName(), Strings.Variables.CoverAccent.Description(), refresh: SlowRefresh),
	];

	public static VariableDefinition AppVolume(string processName) =>
		VariableDefinition.OnDemand(processName, VariableType.Numeric) with
		{
			Name = SanitizeName(processName),
			DisplayName = Strings.Variables.AppVolume.DisplayName(processName),
			Description = Strings.Variables.AppVolume.Description(),
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			RefreshInterval = TimeSpan.FromSeconds(2),
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

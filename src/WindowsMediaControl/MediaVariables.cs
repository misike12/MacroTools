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
	];

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

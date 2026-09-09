using System.Globalization;
using MacroDeck.Sdk.ConfigFlow;

namespace WindowsMediaControl.Config;

public static class MediaSettingsReader
{
	public static async Task<MediaSettings> ReadAsync(IIntegrationConfig config)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		ConfigEntrySnapshot? entry;
		try
		{
			var entries = await config.GetEntriesAsync(timeout.Token);
			entry = entries.Count > 0 ? entries[0] : null;
		}
		catch (Exception)
		{
			return MediaSettings.Default;
		}

		if (entry is null)
		{
			return MediaSettings.Default;
		}

		var fallback = MediaSettings.Default;
		return new MediaSettings(
			PreferredApp: await ReadTextAsync(config, entry.Id, MediaSettings.Keys.PreferredApp, fallback.PreferredApp, timeout.Token),
			DefaultSeekSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.SeekSeconds, fallback.DefaultSeekSeconds, timeout.Token),
			FastForwardSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.FastForwardSeconds, fallback.FastForwardSeconds, timeout.Token),
			DefaultVolumeStep: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.VolumeStep, fallback.DefaultVolumeStep, timeout.Token),
			MaxVolumeLimit: (int)Math.Round(await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.MaxVolume, fallback.MaxVolumeLimit, timeout.Token)),
			UnmuteOnVolumeChange: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.UnmuteOnVolume, fallback.UnmuteOnVolumeChange, timeout.Token),
			PollIntervalSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.PollInterval, fallback.PollIntervalSeconds, timeout.Token),
			StatePollSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.StatePoll, fallback.StatePollSeconds, timeout.Token),
			IconPollSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.IconPoll, fallback.IconPollSeconds, timeout.Token),
			TrackEvents: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.EventsTrack, fallback.TrackEvents, timeout.Token),
			PlaybackEvents: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.EventsPlayback, fallback.PlaybackEvents, timeout.Token),
			VolumeEvents: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.EventsVolume, fallback.VolumeEvents, timeout.Token),
			MuteEvents: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.EventsMute, fallback.MuteEvents, timeout.Token),
			ExtrapolatePosition: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.Extrapolate, fallback.ExtrapolatePosition, timeout.Token),
			ButtonArtwork: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.ButtonArtwork, fallback.ButtonArtwork, timeout.Token),
			SnapshotTimeoutSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.SnapshotTimeout, fallback.SnapshotTimeoutSeconds, timeout.Token),
			ControlTimeoutSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.ControlTimeout, fallback.ControlTimeoutSeconds, timeout.Token),
			ArtworkCacheSize: Math.Max(1, (int)Math.Round(await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.ArtworkCache, fallback.ArtworkCacheSize, timeout.Token))));
	}

	private static async Task<string> ReadTextAsync(IIntegrationConfig config, Guid entryId, string key, string fallback, CancellationToken cancellationToken)
	{
		try
		{
			return await config.GetStringAsync(entryId, key, cancellationToken) ?? fallback;
		}
		catch (Exception)
		{
			return fallback;
		}
	}

	private static async Task<double> ReadNumberAsync(IIntegrationConfig config, Guid entryId, string key, double fallback, CancellationToken cancellationToken)
	{
		var raw = await ReadTextAsync(config, entryId, key, string.Empty, cancellationToken);
		return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : fallback;
	}

	private static async Task<bool> ReadBoolAsync(IIntegrationConfig config, Guid entryId, string key, bool fallback, CancellationToken cancellationToken)
	{
		var raw = await ReadTextAsync(config, entryId, key, string.Empty, cancellationToken);
		return bool.TryParse(raw, out var value) ? value : fallback;
	}
}

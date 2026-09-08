using System.Globalization;
using MacroDeck.Sdk.ConfigFlow;

namespace WindowsMediaControl.Config;

public static class MediaSettingsReader
{
	public static async Task<MediaSettings> ReadAsync(IIntegrationConfig config)
	{
		ConfigEntrySnapshot? entry;
		try
		{
			var entries = await config.GetEntriesAsync(CancellationToken.None);
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
			PreferredApp: await ReadTextAsync(config, entry.Id, MediaSettings.Keys.PreferredApp, fallback.PreferredApp),
			DefaultSeekSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.SeekSeconds, fallback.DefaultSeekSeconds),
			FastForwardSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.FastForwardSeconds, fallback.FastForwardSeconds),
			DefaultVolumeStep: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.VolumeStep, fallback.DefaultVolumeStep),
			MaxVolumeLimit: (int)Math.Round(await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.MaxVolume, fallback.MaxVolumeLimit)),
			UnmuteOnVolumeChange: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.UnmuteOnVolume, fallback.UnmuteOnVolumeChange),
			PollIntervalSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.PollInterval, fallback.PollIntervalSeconds),
			StatePollSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.StatePoll, fallback.StatePollSeconds),
			IconPollSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.IconPoll, fallback.IconPollSeconds),
			TrackEvents: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.EventsTrack, fallback.TrackEvents),
			PlaybackEvents: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.EventsPlayback, fallback.PlaybackEvents),
			VolumeEvents: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.EventsVolume, fallback.VolumeEvents),
			MuteEvents: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.EventsMute, fallback.MuteEvents),
			ExtrapolatePosition: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.Extrapolate, fallback.ExtrapolatePosition),
			ButtonArtwork: await ReadBoolAsync(config, entry.Id, MediaSettings.Keys.ButtonArtwork, fallback.ButtonArtwork),
			SnapshotTimeoutSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.SnapshotTimeout, fallback.SnapshotTimeoutSeconds),
			ControlTimeoutSeconds: await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.ControlTimeout, fallback.ControlTimeoutSeconds),
			ArtworkCacheSize: Math.Max(1, (int)Math.Round(await ReadNumberAsync(config, entry.Id, MediaSettings.Keys.ArtworkCache, fallback.ArtworkCacheSize))));
	}

	private static async Task<string> ReadTextAsync(IIntegrationConfig config, Guid entryId, string key, string fallback)
	{
		try
		{
			return await config.GetStringAsync(entryId, key, CancellationToken.None) ?? fallback;
		}
		catch (Exception)
		{
			return fallback;
		}
	}

	private static async Task<double> ReadNumberAsync(IIntegrationConfig config, Guid entryId, string key, double fallback)
	{
		var raw = await ReadTextAsync(config, entryId, key, string.Empty);
		return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : fallback;
	}

	private static async Task<bool> ReadBoolAsync(IIntegrationConfig config, Guid entryId, string key, bool fallback)
	{
		var raw = await ReadTextAsync(config, entryId, key, string.Empty);
		return bool.TryParse(raw, out var value) ? value : fallback;
	}
}

namespace WindowsMediaControl.Media;

public interface IMediaControlService
{
	event EventHandler? MediaChanged;

	Task<MediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
	Task<bool> PlayAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> PauseAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> StopAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> NextAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> PreviousAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> FastForwardAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> RewindAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> SeekByAsync(TimeSpan offset, CancellationToken cancellationToken, string? appId = null);
	Task<bool> SeekToAsync(TimeSpan position, CancellationToken cancellationToken, string? appId = null);
	Task VolumeUpAsync(int stepPercent, CancellationToken cancellationToken);
	Task VolumeDownAsync(int stepPercent, CancellationToken cancellationToken);
	Task SetVolumeAsync(int percent, CancellationToken cancellationToken);
	Task MuteAsync(CancellationToken cancellationToken);
	Task UnmuteAsync(CancellationToken cancellationToken);
	Task ToggleMuteAsync(CancellationToken cancellationToken);
	Task<bool> ToggleShuffleAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> SetShuffleAsync(bool enabled, CancellationToken cancellationToken, string? appId = null);
	Task<bool> CycleRepeatAsync(CancellationToken cancellationToken, string? appId = null);
	Task<bool> SetRepeatAsync(MediaRepeatMode mode, CancellationToken cancellationToken, string? appId = null);
	Task<ArtworkData?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken);
	ArtworkData? TryGetCachedArtwork(string artworkId);
	Task<IReadOnlyList<AudioAppSession>> GetAudioAppsAsync(CancellationToken cancellationToken);
	Task<bool> SetAppVolumeAsync(string app, int percent, CancellationToken cancellationToken);
	Task<bool> AdjustAppVolumeAsync(string app, int delta, CancellationToken cancellationToken);
	Task<bool> SetAppMuteAsync(string app, bool muted, CancellationToken cancellationToken);
	Task<bool> ToggleAppMuteAsync(string app, CancellationToken cancellationToken);
	Task<IReadOnlyList<AudioDevice>> GetAudioDevicesAsync(CancellationToken cancellationToken);
	Task<bool> SetDefaultDeviceAsync(string device, CancellationToken cancellationToken);
	Task<bool> CycleDefaultDeviceAsync(CancellationToken cancellationToken);
	Task<IReadOnlyList<AudioDevice>> GetAudioInputDevicesAsync(CancellationToken cancellationToken);
	Task<bool> SetDefaultInputDeviceAsync(string device, CancellationToken cancellationToken);
	Task<bool> CycleDefaultInputDeviceAsync(CancellationToken cancellationToken);
}

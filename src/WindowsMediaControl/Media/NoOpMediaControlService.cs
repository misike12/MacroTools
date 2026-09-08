namespace WindowsMediaControl.Media;

public sealed class NoOpMediaControlService : IMediaControlService
{
#pragma warning disable CS0067 // Required by IMediaControlService; the no-op service never raises it.
	public event EventHandler? MediaChanged;
#pragma warning restore CS0067

	public Task<MediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
		Task.FromResult(MediaSnapshot.Empty);

	public Task<bool> PlayAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> PauseAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> StopAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> NextAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> PreviousAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> FastForwardAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> RewindAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> SeekByAsync(TimeSpan offset, CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> SeekToAsync(TimeSpan position, CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task VolumeUpAsync(int stepPercent, CancellationToken cancellationToken) => Task.CompletedTask;
	public Task VolumeDownAsync(int stepPercent, CancellationToken cancellationToken) => Task.CompletedTask;
	public Task SetVolumeAsync(int percent, CancellationToken cancellationToken) => Task.CompletedTask;
	public Task MuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task UnmuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task ToggleMuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task<ArtworkData?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken) =>
		Task.FromResult<ArtworkData?>(null);
	public ArtworkData? TryGetCachedArtwork(string artworkId) => null;
	public Task<bool> ToggleShuffleAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> SetShuffleAsync(bool enabled, CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> CycleRepeatAsync(CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<bool> SetRepeatAsync(MediaRepeatMode mode, CancellationToken cancellationToken, string? appId = null) => Task.FromResult(false);
	public Task<IReadOnlyList<AudioAppSession>> GetAudioAppsAsync(CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<AudioAppSession>>([]);
	public Task<bool> SetAppVolumeAsync(string app, int percent, CancellationToken cancellationToken) => Task.FromResult(false);
	public Task<bool> AdjustAppVolumeAsync(string app, int delta, CancellationToken cancellationToken) => Task.FromResult(false);
	public Task<bool> SetAppMuteAsync(string app, bool muted, CancellationToken cancellationToken) => Task.FromResult(false);
	public Task<bool> ToggleAppMuteAsync(string app, CancellationToken cancellationToken) => Task.FromResult(false);
	public Task<IReadOnlyList<AudioOutputDevice>> GetAudioDevicesAsync(CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
	public Task<bool> SetDefaultDeviceAsync(string device, CancellationToken cancellationToken) => Task.FromResult(false);
	public Task<bool> CycleDefaultDeviceAsync(CancellationToken cancellationToken) => Task.FromResult(false);
}

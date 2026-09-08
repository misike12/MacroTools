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
	Task<ArtworkData?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken);
}

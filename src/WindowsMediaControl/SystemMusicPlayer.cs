using MacroDeck.Sdk.MusicPlayer;
using WindowsMediaControl.Media;

namespace WindowsMediaControl;

public sealed class SystemMusicPlayer(IMediaControlService media) : IMusicPlayer
{
	public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken)
	{
		MediaSnapshot snapshot;
		try
		{
			snapshot = await media.GetSnapshotAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception)
		{
			return MusicPlayerState.Unavailable("media");
		}

		if (!snapshot.HasSession)
		{
			return MusicPlayerState.Unavailable("media");
		}

		return new MusicPlayerState
		{
			IsConnected = true,
			IsUnavailable = false,
			PlaybackState = snapshot.Status switch
			{
				PlaybackStatus.Playing => PlaybackState.Playing,
				PlaybackStatus.Paused => PlaybackState.Paused,
				_ => PlaybackState.Stopped,
			},
			TrackName = snapshot.Title,
			Artists = string.IsNullOrWhiteSpace(snapshot.Artist)
				? []
				: [snapshot.Artist],
			AlbumName = snapshot.Album,
			ArtworkId = snapshot.ArtworkId,
			Position = snapshot.Duration > TimeSpan.Zero ? snapshot.Position : null,
			Duration = snapshot.Duration > TimeSpan.Zero ? snapshot.Duration : null,
			VolumePercent = snapshot.VolumePercent,
			ShuffleEnabled = snapshot.ShuffleActive ?? false,
			RepeatMode = snapshot.RepeatMode switch
			{
				MediaRepeatMode.One => RepeatMode.Track,
				MediaRepeatMode.All => RepeatMode.Context,
				_ => RepeatMode.Off,
			},
			DeviceName = snapshot.AppId,
		};
	}

	public async Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken)
	{
		var art = await media.GetArtworkAsync(artworkId, cancellationToken);
		return art is null ? null : new MusicPlayerArtwork(art.Data, art.MimeType);
	}

	public Task PlayAsync(CancellationToken cancellationToken) =>
		media.PlayAsync(cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken) =>
		media.PauseAsync(cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken) =>
		media.TogglePlayPauseAsync(cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken) =>
		media.NextAsync(cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken) =>
		media.PreviousAsync(cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken) =>
		media.SeekToAsync(position, cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken) =>
		media.SetVolumeAsync(volumePercent, cancellationToken);

	public async Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken)
	{
		var snapshot = await media.GetSnapshotAsync(cancellationToken);
		if ((snapshot.ShuffleActive ?? enabled) == enabled)
		{
			return;
		}

		throw new InvalidOperationException("The current media session does not support changing shuffle.");
	}

	public async Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken)
	{
		var snapshot = await media.GetSnapshotAsync(cancellationToken);
		var current = snapshot.RepeatMode switch
		{
			MediaRepeatMode.One => RepeatMode.Track,
			MediaRepeatMode.All => RepeatMode.Context,
			_ => RepeatMode.Off,
		};
		if (current == mode)
		{
			return;
		}

		throw new InvalidOperationException("The current media session does not support changing the repeat mode.");
	}
}

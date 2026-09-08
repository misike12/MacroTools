using WindowsMediaControl.Media;

namespace WindowsMediaControl.Tests;

internal sealed class FakeMediaControlService : IMediaControlService
{
	public MediaSnapshot Snapshot { get; set; } = new MediaSnapshot
	{
		HasSession = true,
		Title = "Nightcall",
		Artist = "Kavinsky",
		Album = "OutRun",
		AppId = "Spotify.exe",
		Status = PlaybackStatus.Playing,
		Position = TimeSpan.FromSeconds(42),
		Duration = TimeSpan.FromSeconds(215),
		VolumePercent = 50,
		IsMuted = false,
		ShuffleActive = false,
		RepeatMode = MediaRepeatMode.Off,
	};

	public bool TransportResult { get; set; } = true;

	public byte[]? ArtworkBytes { get; set; }

	public List<string> Calls { get; } = [];

	public Task<MediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
		Task.FromResult(Snapshot with { UpdatedAt = DateTimeOffset.UtcNow });

	public Task<bool> PlayAsync(CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(PlayAsync), appId, () => Snapshot = Snapshot with { Status = PlaybackStatus.Playing });

	public Task<bool> PauseAsync(CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(PauseAsync), appId, () => Snapshot = Snapshot with { Status = PlaybackStatus.Paused });

	public Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(TogglePlayPauseAsync), appId, () => Snapshot = Snapshot with
		{
			Status = Snapshot.Status == PlaybackStatus.Playing ? PlaybackStatus.Paused : PlaybackStatus.Playing,
		});

	public Task<bool> StopAsync(CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(StopAsync), appId, () => Snapshot = Snapshot with { Status = PlaybackStatus.Stopped });

	public Task<bool> NextAsync(CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(NextAsync), appId, null);

	public Task<bool> PreviousAsync(CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(PreviousAsync), appId, null);

	public Task<bool> FastForwardAsync(CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(FastForwardAsync), appId, () => Snapshot = Snapshot with { Position = Snapshot.Position + TimeSpan.FromSeconds(10) });

	public Task<bool> RewindAsync(CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(RewindAsync), appId, () => Snapshot = Snapshot with { Position = Snapshot.Position - TimeSpan.FromSeconds(10) });

	public Task<bool> SeekByAsync(TimeSpan offset, CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(SeekByAsync), appId, () => Snapshot = Snapshot with { Position = Snapshot.Position + offset });

	public Task<bool> SeekToAsync(TimeSpan position, CancellationToken cancellationToken, string? appId = null) =>
		RecordBool(nameof(SeekToAsync), appId, () => Snapshot = Snapshot with { Position = position });

	public Task VolumeUpAsync(int stepPercent, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(VolumeUpAsync));
		Snapshot = Snapshot with { VolumePercent = Math.Clamp(Snapshot.VolumePercent + stepPercent, 0, 100) };
		return Task.CompletedTask;
	}

	public Task VolumeDownAsync(int stepPercent, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(VolumeDownAsync));
		Snapshot = Snapshot with { VolumePercent = Math.Clamp(Snapshot.VolumePercent - stepPercent, 0, 100) };
		return Task.CompletedTask;
	}

	public Task SetVolumeAsync(int percent, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(SetVolumeAsync));
		Snapshot = Snapshot with { VolumePercent = Math.Clamp(percent, 0, 100) };
		return Task.CompletedTask;
	}

	public Task MuteAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(MuteAsync));
		Snapshot = Snapshot with { IsMuted = true };
		return Task.CompletedTask;
	}

	public Task UnmuteAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(UnmuteAsync));
		Snapshot = Snapshot with { IsMuted = false };
		return Task.CompletedTask;
	}

	public Task ToggleMuteAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(ToggleMuteAsync));
		Snapshot = Snapshot with { IsMuted = !Snapshot.IsMuted };
		return Task.CompletedTask;
	}

	public Task<ArtworkData?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken) =>
		Task.FromResult(ArtworkBytes is not null && artworkId == Snapshot.ArtworkId
			? new ArtworkData(ArtworkBytes, "image/png")
			: null);

	private Task<bool> RecordBool(string call, string? appId, Action? apply)
	{
		Calls.Add(call);
		if (!AppMatches(appId))
		{
			return Task.FromResult(false);
		}

		if (TransportResult)
		{
			apply?.Invoke();
		}

		return Task.FromResult(TransportResult);
	}

	private bool AppMatches(string? appId) =>
		string.IsNullOrWhiteSpace(appId)
			|| (Snapshot.AppId?.Contains(appId, StringComparison.OrdinalIgnoreCase) ?? true);
}

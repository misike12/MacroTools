using WindowsMediaControl.Media;

namespace WindowsMediaControl.Tests;

internal sealed class FakeMediaControlService : IMediaControlService
{
	public event EventHandler? MediaChanged;

	public void RaiseMediaChanged() => MediaChanged?.Invoke(this, EventArgs.Empty);

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

	public Dictionary<string, (int Volume, bool Muted)> AppVolumes { get; } = new(StringComparer.OrdinalIgnoreCase)
	{
		["Spotify"] = (50, false),
	};

	public List<AudioDevice> Devices { get; } = [new AudioDevice("id-speakers", "Speakers", true), new AudioDevice("id-headphones", "Headphones", false)];

	public List<AudioDevice> InputDevices { get; } = [new AudioDevice("id-mic-array", "Microphone Array", true), new AudioDevice("id-headset-mic", "Headset Microphone", false)];

	public int? MicVolumePercent { get; set; } = 80;

	public bool IsMicMuted { get; set; }

	public double? MicPeak { get; set; }

	public double? SystemPeak { get; set; }

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
		Snapshot = Snapshot with { VolumePercent = Math.Clamp(Snapshot.VolumePercent.GetValueOrDefault() + stepPercent, 0, 100) };
		return Task.CompletedTask;
	}

	public Task VolumeDownAsync(int stepPercent, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(VolumeDownAsync));
		Snapshot = Snapshot with { VolumePercent = Math.Clamp(Snapshot.VolumePercent.GetValueOrDefault() - stepPercent, 0, 100) };
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
		Task.FromResult(TryGetCachedArtwork(artworkId));

	public ArtworkData? TryGetCachedArtwork(string artworkId) =>
		ArtworkBytes is not null && artworkId == Snapshot.ArtworkId
			? new ArtworkData(ArtworkBytes, "image/png")
			: null;

	public Task<bool> ToggleShuffleAsync(CancellationToken cancellationToken, string? appId = null) =>
	RecordBool(nameof(ToggleShuffleAsync), appId, () => Snapshot = Snapshot with { ShuffleActive = !(Snapshot.ShuffleActive ?? false) });

	public Task<bool> SetShuffleAsync(bool enabled, CancellationToken cancellationToken, string? appId = null) =>
	RecordBool(nameof(SetShuffleAsync), appId, () => Snapshot = Snapshot with { ShuffleActive = enabled });

	public Task<bool> CycleRepeatAsync(CancellationToken cancellationToken, string? appId = null) =>
	RecordBool(nameof(CycleRepeatAsync), appId, () => Snapshot = Snapshot with
		{
			RepeatMode = Snapshot.RepeatMode switch
			{
				MediaRepeatMode.Off => MediaRepeatMode.All,
				MediaRepeatMode.All => MediaRepeatMode.One,
				_ => MediaRepeatMode.Off,
			},
		});

	public Task<bool> SetRepeatAsync(MediaRepeatMode mode, CancellationToken cancellationToken, string? appId = null) =>
	RecordBool(nameof(SetRepeatAsync), appId, () => Snapshot = Snapshot with { RepeatMode = mode });

	public Task<IReadOnlyList<AudioAppSession>> GetAudioAppsAsync(CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<AudioAppSession>>(AppVolumes
			.Select(pair => new AudioAppSession(pair.Key, 1000 + pair.Key.Length, pair.Key, pair.Value.Volume, pair.Value.Muted))
			.ToList());

	public Task<bool> SetAppVolumeAsync(string app, int percent, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(SetAppVolumeAsync));
		var key = AppVolumes.Keys.FirstOrDefault(k => k.Contains(app, StringComparison.OrdinalIgnoreCase));
		if (key is null)
		{
			return Task.FromResult(false);
		}

		AppVolumes[key] = (Math.Clamp(percent, 0, 100), AppVolumes[key].Muted);
		return Task.FromResult(TransportResult);
	}

	public Task<bool> AdjustAppVolumeAsync(string app, int delta, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(AdjustAppVolumeAsync));
		var key = AppVolumes.Keys.FirstOrDefault(k => k.Contains(app, StringComparison.OrdinalIgnoreCase));
		if (key is null)
		{
			return Task.FromResult(false);
		}

		AppVolumes[key] = (Math.Clamp(AppVolumes[key].Volume + delta, 0, 100), AppVolumes[key].Muted);
		return Task.FromResult(TransportResult);
	}

	public Task<bool> SetAppMuteAsync(string app, bool muted, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(SetAppMuteAsync));
		var key = AppVolumes.Keys.FirstOrDefault(k => k.Contains(app, StringComparison.OrdinalIgnoreCase));
		if (key is null)
		{
			return Task.FromResult(false);
		}

		AppVolumes[key] = (AppVolumes[key].Volume, muted);
		return Task.FromResult(TransportResult);
	}

	public Task<bool> ToggleAppMuteAsync(string app, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(ToggleAppMuteAsync));
		var key = AppVolumes.Keys.FirstOrDefault(k => k.Contains(app, StringComparison.OrdinalIgnoreCase));
		if (key is null)
		{
			return Task.FromResult(false);
		}

		AppVolumes[key] = (AppVolumes[key].Volume, !AppVolumes[key].Muted);
		return Task.FromResult(TransportResult);
	}

	public Task<IReadOnlyList<AudioDevice>> GetAudioDevicesAsync(CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<AudioDevice>>(Devices.ToList());

	public Task<bool> SetDefaultDeviceAsync(string device, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(SetDefaultDeviceAsync));
		var match = Devices.FindIndex(d =>
			string.Equals(d.Id, device, StringComparison.OrdinalIgnoreCase) ||
			d.Name.Contains(device, StringComparison.OrdinalIgnoreCase));
		if (match < 0)
		{
			return Task.FromResult(false);
		}

		for (var i = 0; i < Devices.Count; i++)
		{
			Devices[i] = Devices[i] with { IsDefault = i == match };
		}

		return Task.FromResult(TransportResult);
	}

	public Task<bool> CycleDefaultDeviceAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(CycleDefaultDeviceAsync));
		if (Devices.Count == 0)
		{
			return Task.FromResult(false);
		}

		var current = Devices.FindIndex(d => d.IsDefault);
		var next = Devices[(current + 1) % Devices.Count];
		for (var i = 0; i < Devices.Count; i++)
		{
			Devices[i] = Devices[i] with { IsDefault = Devices[i].Id == next.Id };
		}

		return Task.FromResult(TransportResult);
	}

	public Task<IReadOnlyList<AudioDevice>> GetAudioInputDevicesAsync(CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<AudioDevice>>(InputDevices.ToList());

	public Task<bool> SetDefaultInputDeviceAsync(string device, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(SetDefaultInputDeviceAsync));
		var match = InputDevices.FindIndex(d =>
			string.Equals(d.Id, device, StringComparison.OrdinalIgnoreCase) ||
			d.Name.Contains(device, StringComparison.OrdinalIgnoreCase));
		if (match < 0)
		{
			return Task.FromResult(false);
		}

		for (var i = 0; i < InputDevices.Count; i++)
		{
			InputDevices[i] = InputDevices[i] with { IsDefault = i == match };
		}

		return Task.FromResult(TransportResult);
	}

	public Task<bool> CycleDefaultInputDeviceAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(CycleDefaultInputDeviceAsync));
		if (InputDevices.Count == 0)
		{
			return Task.FromResult(false);
		}

		var current = InputDevices.FindIndex(d => d.IsDefault);
		var next = InputDevices[(current + 1) % InputDevices.Count];
		for (var i = 0; i < InputDevices.Count; i++)
		{
			InputDevices[i] = InputDevices[i] with { IsDefault = InputDevices[i].Id == next.Id };
		}

		return Task.FromResult(TransportResult);
	}

	public Task SetMicVolumeAsync(int percent, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(SetMicVolumeAsync));
		MicVolumePercent = Math.Clamp(percent, 0, 100);
		return Task.CompletedTask;
	}

	public Task MuteMicAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(MuteMicAsync));
		IsMicMuted = true;
		return Task.CompletedTask;
	}

	public Task UnmuteMicAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(UnmuteMicAsync));
		IsMicMuted = false;
		return Task.CompletedTask;
	}

	public Task ToggleMicMuteAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(ToggleMicMuteAsync));
		IsMicMuted = !IsMicMuted;
		return Task.CompletedTask;
	}

	public Task<bool> SoloAppAsync(string app, CancellationToken cancellationToken)
	{
		Calls.Add(nameof(SoloAppAsync));
		var match = AppVolumes.Keys.FirstOrDefault(k => k.Contains(app, StringComparison.OrdinalIgnoreCase));
		if (match is null)
		{
			return Task.FromResult(false);
		}

		foreach (var key in AppVolumes.Keys.ToList())
		{
			AppVolumes[key] = (AppVolumes[key].Volume, !string.Equals(key, match, StringComparison.OrdinalIgnoreCase));
		}

		return Task.FromResult(TransportResult);
	}

	public Task<double?> GetMicPeakAsync(CancellationToken cancellationToken) =>
		Task.FromResult(MicPeak);

	public Task<double?> GetSystemPeakAsync(CancellationToken cancellationToken) =>
		Task.FromResult(SystemPeak);

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

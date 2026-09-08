using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WindowsMediaControl.Config;

namespace WindowsMediaControl.Media;

public sealed class WindowsMediaControlService : IMediaControlService
{
	private static readonly ulong MaxArtworkBytes = 4 * 1024 * 1024;

	private readonly ConcurrentDictionary<string, ArtworkData> _artworkCache = new(StringComparer.Ordinal);
	private readonly MediaSettingsProvider _settings;
	private readonly object _subscriptionGate = new();
	private GlobalSystemMediaTransportControlsSessionManager? _manager;
	private GlobalSystemMediaTransportControlsSession? _watchedSession;
	private bool _managerSubscribed;

	public event EventHandler? MediaChanged;

	public WindowsMediaControlService(MediaSettingsProvider? settings = null)
	{
		_settings = settings ?? new MediaSettingsProvider();
	}

	private TimeSpan SnapshotTimeout =>
		TimeSpan.FromSeconds(Math.Clamp(_settings.Current.SnapshotTimeoutSeconds, 1, 30));

	private TimeSpan ControlTimeout =>
		TimeSpan.FromSeconds(Math.Clamp(_settings.Current.ControlTimeoutSeconds, 1, 60));

	public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

	public Task<MediaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
		WithTimeoutAsync(ct => GetSnapshotCoreAsync(ct), SnapshotTimeout, MediaSnapshot.Empty, cancellationToken, offload: true);

	private async Task<MediaSnapshot> GetSnapshotCoreAsync(CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return MediaSnapshot.Empty;
		}

		try
		{
			var manager = await GetManagerAsync(cancellationToken);
			if (manager is null)
			{
				return MediaSnapshot.Empty;
			}

			EnsureWatchedSession(manager);
			var session = manager.GetCurrentSession();
			if (session is null)
			{
				return MediaSnapshot.Empty;
			}

			var props = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
			var info = session.GetPlaybackInfo();
			var timeline = session.GetTimelineProperties();
			var audio = SystemAudio.TryRead();

			var status = MapStatus(info?.PlaybackStatus);
			var hasSession = status != PlaybackStatus.NoMedia || !string.IsNullOrWhiteSpace(props?.Title);

			var position = timeline?.Position ?? TimeSpan.Zero;
			var duration = timeline?.EndTime ?? TimeSpan.Zero;
			if (_settings.Current.ExtrapolatePosition && status == PlaybackStatus.Playing && timeline is not null)
			{
				var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
				if (elapsed is { TotalSeconds: >= 0 and < 3600 })
				{
					position += elapsed;
				}
			}

			if (duration > TimeSpan.Zero && position > duration)
			{
				position = duration;
			}

			if (position < TimeSpan.Zero)
			{
				position = TimeSpan.Zero;
			}

			var artworkId = hasSession
				? ArtworkIdFor(session.SourceAppUserModelId, props?.Title, props?.Artist, props?.AlbumTitle)
				: string.Empty;
			var (accent, accentDark) = ArtworkAccentFor(artworkId);

			return new MediaSnapshot
			{
				HasSession = hasSession,
				Title = props?.Title ?? string.Empty,
				Artist = props?.Artist ?? string.Empty,
				Album = props?.AlbumTitle ?? string.Empty,
				AlbumArtist = props?.AlbumArtist ?? string.Empty,
				Genres = props?.Genres?.ToList() ?? [],
				TrackNumber = props is null ? 0 : props.TrackNumber,
				AlbumTrackCount = props is null ? 0 : props.AlbumTrackCount,
				Subtitle = props?.Subtitle ?? string.Empty,
				PlaybackType = MapContentType(props?.PlaybackType ?? info?.PlaybackType),
				PlaybackRate = info?.PlaybackRate,
				AppId = session.SourceAppUserModelId ?? string.Empty,
				ArtworkId = artworkId,
				ArtworkAccent = accent,
				ArtworkAccentDark = accentDark,
				Status = hasSession ? status : PlaybackStatus.NoMedia,
				Duration = duration,
				VolumePercent = audio?.VolumePercent ?? 50,
				IsMuted = audio?.IsMuted ?? false,
				ShuffleActive = info?.IsShuffleActive,
				RepeatMode = MapRepeat(info?.AutoRepeatMode),
				CanPlay = info?.Controls.IsPlayEnabled ?? false,
				CanPause = info?.Controls.IsPauseEnabled ?? false,
				CanStop = info?.Controls.IsStopEnabled ?? false,
				CanNext = info?.Controls.IsNextEnabled ?? false,
				CanPrevious = info?.Controls.IsPreviousEnabled ?? false,
				CanSeek = info?.Controls.IsPlaybackPositionEnabled ?? false,
				CanShuffle = info?.Controls.IsShuffleEnabled ?? false,
				CanRepeat = info?.Controls.IsRepeatEnabled ?? false,
				UpdatedAt = DateTimeOffset.UtcNow,
			};
		}
		catch (COMException)
		{
			return MediaSnapshot.Empty;
		}
		catch (UnauthorizedAccessException)
		{
			return MediaSnapshot.Empty;
		}
	}

	public Task<bool> PlayAsync(CancellationToken cancellationToken, string? appId = null) =>
		TryControlAsync(s => s.TryPlayAsync(), appId, cancellationToken);

	public Task<bool> PauseAsync(CancellationToken cancellationToken, string? appId = null) =>
		TryControlAsync(s => s.TryPauseAsync(), appId, cancellationToken);

	public Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken, string? appId = null) =>
		TryControlAsync(s => s.TryTogglePlayPauseAsync(), appId, cancellationToken);

	public Task<bool> StopAsync(CancellationToken cancellationToken, string? appId = null) =>
		TryControlAsync(s => s.TryStopAsync(), appId, cancellationToken);

	public Task<bool> NextAsync(CancellationToken cancellationToken, string? appId = null) =>
		TryControlAsync(s => s.TrySkipNextAsync(), appId, cancellationToken);

	public Task<bool> PreviousAsync(CancellationToken cancellationToken, string? appId = null) =>
		TryControlAsync(s => s.TrySkipPreviousAsync(), appId, cancellationToken);

	public async Task<bool> FastForwardAsync(CancellationToken cancellationToken, string? appId = null)
	{
		var ok = await TryControlAsync(s => s.TryFastForwardAsync(), appId, cancellationToken);
		return ok || await SeekByAsync(FallbackSpan(), cancellationToken, appId);
	}

	public async Task<bool> RewindAsync(CancellationToken cancellationToken, string? appId = null)
	{
		var ok = await TryControlAsync(s => s.TryRewindAsync(), appId, cancellationToken);
		return ok || await SeekByAsync(-FallbackSpan(), cancellationToken, appId);
	}

	private TimeSpan FallbackSpan() =>
		TimeSpan.FromSeconds(Math.Clamp(_settings.Current.FastForwardSeconds, 1, 300));

	public async Task<bool> SeekByAsync(TimeSpan offset, CancellationToken cancellationToken, string? appId = null) =>
		await WithTimeoutAsync(ct => SeekByCoreAsync(offset, appId, ct), ControlTimeout, false, cancellationToken);

	private async Task<bool> SeekByCoreAsync(TimeSpan offset, string? appId, CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return false;
		}

		try
		{
			var session = await GetTargetSessionAsync(appId, cancellationToken);
			if (session is null)
			{
				return false;
			}

			var timeline = session.GetTimelineProperties();
			var target = timeline.Position + offset;
			if (target < TimeSpan.Zero)
			{
				target = TimeSpan.Zero;
			}

			if (timeline.EndTime > TimeSpan.Zero && target > timeline.EndTime)
			{
				target = timeline.EndTime;
			}

			return await session.TryChangePlaybackPositionAsync(target.Ticks).AsTask(cancellationToken);
		}
		catch (COMException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	public async Task<bool> SeekToAsync(TimeSpan position, CancellationToken cancellationToken, string? appId = null) =>
		await WithTimeoutAsync(ct => SeekToCoreAsync(position, appId, ct), ControlTimeout, false, cancellationToken);

	private async Task<bool> SeekToCoreAsync(TimeSpan position, string? appId, CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return false;
		}

		try
		{
			var session = await GetTargetSessionAsync(appId, cancellationToken);
			if (session is null)
			{
				return false;
			}

			if (position < TimeSpan.Zero)
			{
				position = TimeSpan.Zero;
			}

			return await session.TryChangePlaybackPositionAsync(position.Ticks).AsTask(cancellationToken);
		}
		catch (COMException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	public Task VolumeUpAsync(int stepPercent, CancellationToken cancellationToken)
	{
		AdjustVolume(stepPercent);
		return Task.CompletedTask;
	}

	public Task VolumeDownAsync(int stepPercent, CancellationToken cancellationToken)
	{
		AdjustVolume(-stepPercent);
		return Task.CompletedTask;
	}

	public Task SetVolumeAsync(int percent, CancellationToken cancellationToken)
	{
		var settings = _settings.Current;
		if (settings.UnmuteOnVolumeChange)
		{
			SystemAudio.SetMute(false);
		}

		SystemAudio.SetVolume(settings.ClampVolume(percent));
		return Task.CompletedTask;
	}

	private void AdjustVolume(int delta)
	{
		var settings = _settings.Current;
		if (settings.UnmuteOnVolumeChange)
		{
			SystemAudio.SetMute(false);
		}

		var current = SystemAudio.TryRead();
		SystemAudio.SetVolume(settings.ClampVolume((current?.VolumePercent ?? 50) + delta));
	}

	public Task MuteAsync(CancellationToken cancellationToken)
	{
		SystemAudio.SetMute(true);
		return Task.CompletedTask;
	}

	public Task UnmuteAsync(CancellationToken cancellationToken)
	{
		SystemAudio.SetMute(false);
		return Task.CompletedTask;
	}

	public Task ToggleMuteAsync(CancellationToken cancellationToken)
	{
		SystemAudio.ToggleMute();
		return Task.CompletedTask;
	}

	public Task<bool> ToggleShuffleAsync(CancellationToken cancellationToken, string? appId = null) =>
		WithTimeoutAsync(
			ct => ToggleShuffleCoreAsync(appId, ct),
			ControlTimeout,
			false,
			cancellationToken);

	public Task<bool> SetShuffleAsync(bool enabled, CancellationToken cancellationToken, string? appId = null) =>
		WithTimeoutAsync(
			ct => SetShuffleCoreAsync(enabled, appId, ct),
			ControlTimeout,
			false,
			cancellationToken);

	public Task<bool> CycleRepeatAsync(CancellationToken cancellationToken, string? appId = null) =>
		WithTimeoutAsync(
			ct => CycleRepeatCoreAsync(appId, ct),
			ControlTimeout,
			false,
			cancellationToken);

	public Task<bool> SetRepeatAsync(MediaRepeatMode mode, CancellationToken cancellationToken, string? appId = null) =>
		WithTimeoutAsync(
			ct => SetRepeatCoreAsync(mode, appId, ct),
			ControlTimeout,
			false,
			cancellationToken);

	private async Task<bool> ToggleShuffleCoreAsync(string? appId, CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return false;
		}

		try
		{
			var session = await GetTargetSessionAsync(appId, cancellationToken);
			if (session is null)
			{
				return false;
			}

			var shuffle = ReadShuffle(session);
			if (shuffle is null)
			{
				return false;
			}

			return await session.TryChangeShuffleActiveAsync(!shuffle.Value).AsTask(cancellationToken);
		}
		catch (COMException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private async Task<bool> SetShuffleCoreAsync(bool enabled, string? appId, CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return false;
		}

		try
		{
			var session = await GetTargetSessionAsync(appId, cancellationToken);
			if (session is null)
			{
				return false;
			}

			return await session.TryChangeShuffleActiveAsync(enabled).AsTask(cancellationToken);
		}
		catch (COMException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private async Task<bool> CycleRepeatCoreAsync(string? appId, CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return false;
		}

		try
		{
			var session = await GetTargetSessionAsync(appId, cancellationToken);
			if (session is null)
			{
				return false;
			}

			var next = MapRepeat(ReadRepeatMode(session)) switch
			{
				MediaRepeatMode.Off => MediaRepeatMode.All,
				MediaRepeatMode.All => MediaRepeatMode.One,
				_ => MediaRepeatMode.Off,
			};
			return await SetRepeatCoreAsync(next, appId, cancellationToken);
		}
		catch (COMException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private async Task<bool> SetRepeatCoreAsync(MediaRepeatMode mode, string? appId, CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return false;
		}

		try
		{
			var session = await GetTargetSessionAsync(appId, cancellationToken);
			if (session is null)
			{
				return false;
			}

			return await session.TryChangeAutoRepeatModeAsync(mode switch
			{
				MediaRepeatMode.One => MediaPlaybackAutoRepeatMode.Track,
				MediaRepeatMode.All => MediaPlaybackAutoRepeatMode.List,
				_ => MediaPlaybackAutoRepeatMode.None,
			}).AsTask(cancellationToken);
		}
		catch (COMException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool? ReadShuffle(GlobalSystemMediaTransportControlsSession session)
	{
		try
		{
			return session.GetPlaybackInfo()?.IsShuffleActive;
		}
		catch (COMException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static MediaPlaybackAutoRepeatMode? ReadRepeatMode(GlobalSystemMediaTransportControlsSession session)
	{
		try
		{
			return session.GetPlaybackInfo()?.AutoRepeatMode;
		}
		catch (COMException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	public Task<IReadOnlyList<AudioAppSession>> GetAudioAppsAsync(CancellationToken cancellationToken) =>
		Task.FromResult(AppAudio.GetAppSessions());

	public Task<bool> SetAppVolumeAsync(string app, int percent, CancellationToken cancellationToken) =>
		Task.FromResult(AppAudio.TryAdjustAppVolume(app, (_, muted) => (percent, muted)));

	public Task<bool> AdjustAppVolumeAsync(string app, int delta, CancellationToken cancellationToken) =>
		Task.FromResult(AppAudio.TryAdjustAppVolume(app, (volume, muted) => (volume + delta, muted)));

	public Task<bool> SetAppMuteAsync(string app, bool muted, CancellationToken cancellationToken) =>
		Task.FromResult(AppAudio.TryAdjustAppVolume(app, (volume, _) => (volume, muted)));

	public Task<bool> ToggleAppMuteAsync(string app, CancellationToken cancellationToken) =>
		Task.FromResult(AppAudio.TryAdjustAppVolume(app, (volume, muted) => (volume, !muted)));

	public async Task<IReadOnlyList<AudioOutputDevice>> GetAudioDevicesAsync(CancellationToken cancellationToken) =>
		await AppAudio.GetOutputDevicesAsync();

	public async Task<bool> SetDefaultDeviceAsync(string device, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(device) || !IsSupported)
		{
			return false;
		}

		var id = await ResolveDeviceIdAsync(device);
		return id is not null && AppAudio.TrySetDefaultDevice(id);
	}

	public async Task<bool> CycleDefaultDeviceAsync(CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return false;
		}

		var devices = await AppAudio.GetOutputDevicesAsync();
		if (devices.Count == 0)
		{
			return false;
		}

		var current = devices.ToList().FindIndex(d => d.IsDefault);
		var next = devices[(current + 1) % devices.Count];
		return AppAudio.TrySetDefaultDevice(next.Id);
	}

	private static async Task<string?> ResolveDeviceIdAsync(string device)
	{
		var text = device.Trim();
		var devices = await AppAudio.GetOutputDevicesAsync();
		return devices.FirstOrDefault(d =>
			string.Equals(d.Id, text, StringComparison.OrdinalIgnoreCase) ||
			d.Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
			d.Name.Contains(text, StringComparison.OrdinalIgnoreCase))?.Id;
	}

	public async Task<ArtworkData?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken) =>
		await WithTimeoutAsync(ct => GetArtworkCoreAsync(artworkId, ct), ControlTimeout, (ArtworkData?)null, cancellationToken);

	private async Task<ArtworkData?> GetArtworkCoreAsync(string artworkId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(artworkId) || !IsSupported)
		{
			return null;
		}

		if (_artworkCache.TryGetValue(artworkId, out var cached))
		{
			return cached;
		}

		try
		{
			var manager = await GetManagerAsync(cancellationToken);
			if (manager is null)
			{
				return null;
			}

			var session = manager.GetCurrentSession();
			if (session is null)
			{
				return null;
			}

			var props = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
			if (ArtworkIdFor(session.SourceAppUserModelId, props?.Title, props?.Artist, props?.AlbumTitle) != artworkId)
			{
				return null;
			}

			var art = await ReadThumbnailAsync(props, cancellationToken);
			if (art is null)
			{
				return null;
			}

			if (_artworkCache.Count >= Math.Max(1, _settings.Current.ArtworkCacheSize))
			{
				_artworkCache.Clear();
			}

			return _artworkCache.GetOrAdd(artworkId, art);
		}
		catch (COMException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static async Task<ArtworkData?> ReadThumbnailAsync(
		GlobalSystemMediaTransportControlsSessionMediaProperties? props,
		CancellationToken cancellationToken)
	{
		var reference = props?.Thumbnail;
		if (reference is null)
		{
			return null;
		}

		try
		{
			using var stream = await reference.OpenReadAsync().AsTask(cancellationToken);
			if (stream.Size == 0 || stream.Size > MaxArtworkBytes)
			{
				return null;
			}

			using var reader = new DataReader(stream.GetInputStreamAt(0));
			await reader.LoadAsync((uint)stream.Size).AsTask(cancellationToken);
			var bytes = new byte[stream.Size];
			reader.ReadBytes(bytes);
			var (accent, dark) = ArtworkColors.FromImage(bytes);
			return new ArtworkData(bytes, SniffMimeType(bytes, stream.ContentType), accent, dark);
		}
		catch (COMException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static string SniffMimeType(byte[] bytes, string contentType)
	{
		if (bytes is [0xFF, 0xD8, ..])
		{
			return "image/jpeg";
		}

		if (bytes is [0x89, 0x50, 0x4E, 0x47, ..])
		{
			return "image/png";
		}

		return string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
	}

	public ArtworkData? TryGetCachedArtwork(string artworkId) =>
		string.IsNullOrEmpty(artworkId) || !_artworkCache.TryGetValue(artworkId, out var art) ? null : art;

	private (string Accent, string Dark) ArtworkAccentFor(string artworkId)
	{
		if (string.IsNullOrEmpty(artworkId) || !_artworkCache.TryGetValue(artworkId, out var art))
		{
			return (string.Empty, string.Empty);
		}

		return (art.Accent, art.AccentDark);
	}

	public static string ArtworkIdFor(string? appId, string? title, string? artist, string? album)
	{
		var input = $"{appId ?? string.Empty}\0{title ?? string.Empty}\0{artist ?? string.Empty}\0{album ?? string.Empty}";
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32];
	}

	private Task<bool> TryControlAsync(
		Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> invoke,
		string? appId,
		CancellationToken cancellationToken) =>
		WithTimeoutAsync(ct => TryControlCoreAsync(invoke, appId, ct), ControlTimeout, false, cancellationToken);

	private async Task<bool> TryControlCoreAsync(
		Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> invoke,
		string? appId,
		CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return false;
		}

		try
		{
			var session = await GetTargetSessionAsync(appId, cancellationToken);
			if (session is null)
			{
				return false;
			}

			return await invoke(session).AsTask(cancellationToken);
		}
		catch (COMException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private async Task<GlobalSystemMediaTransportControlsSession?> GetTargetSessionAsync(
		string? appId,
		CancellationToken cancellationToken)
	{
		var manager = await GetManagerAsync(cancellationToken);
		if (manager is null)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(appId))
		{
			return manager.GetCurrentSession();
		}

		foreach (var session in manager.GetSessions())
		{
			var id = session.SourceAppUserModelId;
			if (!string.IsNullOrEmpty(id) && id.Contains(appId, StringComparison.OrdinalIgnoreCase))
			{
				return session;
			}
		}

		return null;
	}

	private async Task<GlobalSystemMediaTransportControlsSessionManager?> GetManagerAsync(
		CancellationToken cancellationToken)
	{
		if (!IsSupported)
		{
			return null;
		}

		lock (_subscriptionGate)
		{
			if (_manager is not null)
			{
				return _manager;
			}
		}

		try
		{
			var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
			lock (_subscriptionGate)
			{
				_manager ??= manager;
				SubscribeManagerLocked(_manager);
				EnsureWatchedSessionLocked(_manager);
				return _manager;
			}
		}
		catch (COMException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private void EnsureWatchedSession(GlobalSystemMediaTransportControlsSessionManager manager)
	{
		lock (_subscriptionGate)
		{
			SubscribeManagerLocked(manager);
			EnsureWatchedSessionLocked(manager);
		}
	}

	private void SubscribeManagerLocked(GlobalSystemMediaTransportControlsSessionManager manager)
	{
		if (_managerSubscribed)
		{
			return;
		}

		Subscribe(() => manager.SessionsChanged += OnSessionsChanged);
		Subscribe(() => manager.CurrentSessionChanged += OnCurrentSessionChanged);
		_managerSubscribed = true;
	}

	private void EnsureWatchedSessionLocked(GlobalSystemMediaTransportControlsSessionManager manager)
	{
		GlobalSystemMediaTransportControlsSession? current = null;
		try
		{
			current = manager.GetCurrentSession();
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}

		if (ReferenceEquals(current, _watchedSession))
		{
			return;
		}

		var previous = _watchedSession;
		_watchedSession = current;
		if (previous is not null)
		{
			Unsubscribe(() => previous.MediaPropertiesChanged -= OnSessionInvalidated);
			Unsubscribe(() => previous.PlaybackInfoChanged -= OnSessionInvalidated);
			Unsubscribe(() => previous.TimelinePropertiesChanged -= OnSessionInvalidated);
		}

		if (current is not null)
		{
			Subscribe(() => current.MediaPropertiesChanged += OnSessionInvalidated);
			Subscribe(() => current.PlaybackInfoChanged += OnSessionInvalidated);
			Subscribe(() => current.TimelinePropertiesChanged += OnSessionInvalidated);
		}
	}

	private void OnSessionsChanged(
		GlobalSystemMediaTransportControlsSessionManager sender,
		SessionsChangedEventArgs args)
	{
		lock (_subscriptionGate)
		{
			EnsureWatchedSessionLocked(sender);
		}

		NotifyChanged();
	}

	private void OnCurrentSessionChanged(
		GlobalSystemMediaTransportControlsSessionManager sender,
		CurrentSessionChangedEventArgs args) =>
		NotifyChanged();

	private void OnSessionInvalidated(object? sender, object? args) => NotifyChanged();

	private void NotifyChanged()
	{
		try
		{
			MediaChanged?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception)
		{
		}
	}

	private static void Subscribe(Action subscribe)
	{
		try
		{
			subscribe();
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static void Unsubscribe(Action unsubscribe)
	{
		try
		{
			unsubscribe();
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		catch (ArgumentException)
		{
		}
	}

	private static async Task<T> WithTimeoutAsync<T>(
		Func<CancellationToken, Task<T>> invoke,
		TimeSpan timeout,
		T fallback,
		CancellationToken cancellationToken,
		bool offload = false)
	{
		using var timeoutSource = new CancellationTokenSource();
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
		timeoutSource.CancelAfter(timeout);
		try
		{
			// Offload exists for paths containing synchronous calls SMTC can block in
			// uncancellably. The timeout still bounds the caller; abandoned pool work is
			// safe here because snapshot reads have no side effects.
			var work = offload
				? Task.Run(() => invoke(linked.Token), CancellationToken.None)
				: invoke(linked.Token);
			return await work;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return fallback;
		}
	}

	private static MediaPlaybackType MapContentType(Windows.Media.MediaPlaybackType? type) =>
		type switch
		{
			Windows.Media.MediaPlaybackType.Music => MediaPlaybackType.Music,
			Windows.Media.MediaPlaybackType.Video => MediaPlaybackType.Video,
			Windows.Media.MediaPlaybackType.Image => MediaPlaybackType.Image,
			_ => MediaPlaybackType.Unknown,
		};

	private static PlaybackStatus MapStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status) =>
		status switch
		{
			GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
			GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
			GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackStatus.Stopped,
			GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => PlaybackStatus.Stopped,
			GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackStatus.Paused,
			_ => PlaybackStatus.NoMedia,
		};

	private static MediaRepeatMode MapRepeat(MediaPlaybackAutoRepeatMode? mode) =>
		mode switch
		{
			MediaPlaybackAutoRepeatMode.Track => MediaRepeatMode.One,
			MediaPlaybackAutoRepeatMode.List => MediaRepeatMode.All,
			_ => MediaRepeatMode.Off,
		};
}

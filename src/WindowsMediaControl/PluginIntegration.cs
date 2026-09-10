using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Serilog;
using WindowsMediaControl.Actions;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;
using WindowsMediaControl.Widgets;

namespace WindowsMediaControl;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IEventProvider, IMusicPlayerProvider, IWidgetTypeProvider, IUiProvider, IConfigFlowProvider, IDisposable
{
	private readonly IMediaControlService _media;
	private readonly MediaSettingsProvider _settings;
	private readonly ILogger _logger;
	private readonly NowPlayingWidget _widget;
	private readonly object _loopGate = new();
	private readonly object _snapshotGate = new();
	private IIntegrationContext? _context;
	private CancellationTokenSource? _loopCts;
	private Task? _loopTask;
	private MediaSnapshot _last = MediaSnapshot.Empty;
	private string _defaultDeviceName = string.Empty;
	private string _defaultInputDeviceName = string.Empty;
	private long _lastEventRefreshTicks;
	private bool _disposed;

	public PluginIntegration(IMediaControlService media, MediaSettingsProvider settings, ILogger logger)
	{
		_media = media;
		_settings = settings;
		_logger = logger.ForContext<PluginIntegration>();
		_widget = new NowPlayingWidget(media, logger);
		Actions =
		[
			new PlayAction(media, settings),
			new PauseAction(media, settings),
			new TogglePlayPauseAction(media, settings),
			new StopAction(media, settings),
			new NextAction(media, settings),
			new PreviousAction(media, settings),
			new FastForwardAction(media, settings),
			new RewindAction(media, settings),
			new SeekForwardAction(media, settings),
			new SeekBackwardAction(media, settings),
			new SeekToAction(media, settings),
			new VolumeUpAction(media, settings),
			new VolumeDownAction(media, settings),
			new SetVolumeAction(media),
			new MuteAction(media),
			new UnmuteAction(media),
			new ToggleMuteAction(media),
			new ToggleShuffleAction(media, settings),
			new SetShuffleAction(media, settings),
			new CycleRepeatAction(media, settings),
			new SetRepeatAction(media, settings),
			new SetAppVolumeAction(media),
			new AdjustAppVolumeAction(media),
			new MuteAppAction(media),
			new UnmuteAppAction(media),
			new ToggleAppMuteAction(media),
			new SetOutputDeviceAction(media),
			new CycleOutputDeviceAction(media),
			new SetInputDeviceAction(media),
			new CycleInputDeviceAction(media),
			new SetMicVolumeAction(media),
			new MuteMicAction(media),
			new UnmuteMicAction(media),
			new ToggleMicMuteAction(media),
			new FocusAppAction(media),
			new MuteSystemSoundsAction(media),
			new UnmuteSystemSoundsAction(media),
			new ToggleSystemSoundsAction(media),
			new SleepTimerAction(media, settings),
			new FadeOutPauseAction(media, settings),
			new FadeInPlayAction(media, settings),
		];
		Variables = MediaVariables.CreateDefinitions();
		DeclaredVariables = Variables;
		EventDefinitions =
		[
			new EventDefinition
			{
				Id = "track-changed",
				Name = Strings.Events.TrackChanged.Name(),
				Description = Strings.Events.TrackChanged.Description(),
				PayloadParameters =
				[
					new ActionParameter { Name = "title", Type = ActionParameterType.String, Label = Strings.Variables.Title.DisplayName() },
					new ActionParameter { Name = "artist", Type = ActionParameterType.String, Label = Strings.Variables.Artist.DisplayName() },
					new ActionParameter { Name = "album", Type = ActionParameterType.String, Label = Strings.Variables.Album.DisplayName() },
					new ActionParameter { Name = "app", Type = ActionParameterType.String, Label = Strings.Variables.SourceApp.DisplayName() },
				],
			},
			new EventDefinition
			{
				Id = "playback-changed",
				Name = Strings.Events.PlaybackChanged.Name(),
				Description = Strings.Events.PlaybackChanged.Description(),
				PayloadParameters =
				[
					new ActionParameter { Name = "status", Type = ActionParameterType.String, Label = Strings.Variables.PlaybackStatus.DisplayName() },
					new ActionParameter { Name = "isPlaying", Type = ActionParameterType.Boolean, Label = Strings.Variables.IsPlaying.DisplayName() },
				],
			},
			new EventDefinition
			{
				Id = "volume-changed",
				Name = Strings.Events.VolumeChanged.Name(),
				Description = Strings.Events.VolumeChanged.Description(),
				PayloadParameters =
				[
					new ActionParameter { Name = "volume", Type = ActionParameterType.Number, Label = Strings.Variables.VolumePercent.DisplayName() },
					new ActionParameter { Name = "muted", Type = ActionParameterType.Boolean, Label = Strings.Variables.IsMuted.DisplayName() },
				],
			},
			new EventDefinition
			{
				Id = "mute-changed",
				Name = Strings.Events.MuteChanged.Name(),
				Description = Strings.Events.MuteChanged.Description(),
				PayloadParameters =
				[
					new ActionParameter { Name = "muted", Type = ActionParameterType.Boolean, Label = Strings.Variables.IsMuted.DisplayName() },
				],
			},
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<VariableDefinition> Variables { get; }

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; }

	public bool VariablesDependOnConfiguration => false;

	public bool SupportsCatalog => true;

	public bool SupportsPush => false;

	public bool SupportsSearch => true;

	public string CatalogName => "App volumes";

	public int? CatalogEntryCount => null;

	public string ProviderName => "Windows media";

	public IReadOnlyList<EventDefinition> EventDefinitions { get; }

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_media.MediaChanged -= OnMediaChanged;
		_media.MediaChanged += OnMediaChanged;
		try
		{
			_settings.Update(await MediaSettingsReader.ReadAsync(context.Config));
			_last = await _media.GetSnapshotAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Initial media snapshot failed.");
		}

		lock (_loopGate)
		{
			if (_disposed)
			{
				return;
			}

			_loopCts?.Cancel();
			_loopCts?.Dispose();
			_loopCts = new CancellationTokenSource();
			_loopTask = RunPollLoopAsync(_loopCts.Token);
		}
	}

	public Task ShutdownAsync()
	{
		_media.MediaChanged -= OnMediaChanged;
		foreach (var action in Actions.OfType<SleepTimerAction>())
		{
			action.CancelPendingTimer();
		}

		lock (_loopGate)
		{
			_loopCts?.Cancel();
		}

		return Task.CompletedTask;
	}

	private void OnMediaChanged(object? sender, EventArgs e)
	{
		var now = DateTimeOffset.UtcNow.Ticks;
		var last = Interlocked.Read(ref _lastEventRefreshTicks);
		if (now - last < TimeSpan.FromMilliseconds(Math.Clamp(_settings.Current.EventDebounceMs, 100, 5000)).Ticks)
		{
			return;
		}

		Interlocked.Exchange(ref _lastEventRefreshTicks, now);
		_ = RefreshFromEventAsync();
	}

	private async Task RefreshFromEventAsync()
	{
		MediaSnapshot snapshot;
		try
		{
			snapshot = await _media.GetSnapshotAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Event-driven media refresh failed.");
			return;
		}

		lock (_snapshotGate)
		{
			var previous = _last;
			_last = snapshot;
			PublishChanges(previous, snapshot);
		}
	}

	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (localId is "mic-level-percent" or "system-level-percent")
		{
			return await ReadPeakAsync(localId == "mic-level-percent", cancellationToken);
		}

		if (localId == "active-apps")
		{
			return await ReadActiveAppsAsync(cancellationToken);
		}

		var snapshot = _last;
		if (TryReadEager(snapshot, localId, out var reading))
		{
			return reading;
		}

		return await ReadAppVolumeAsync(localId, cancellationToken);
	}

	private async ValueTask<VariableReading> ReadPeakAsync(bool microphone, CancellationToken cancellationToken)
	{
		try
		{
			var peak = microphone
				? await _media.GetMicPeakAsync(cancellationToken)
				: await _media.GetSystemPeakAsync(cancellationToken);
			return peak is null ? VariableReading.Unavailable : VariableReading.Of(peak.Value, 0, 100, 1);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Peak level read failed.");
			return VariableReading.Unavailable;
		}
	}

	private async ValueTask<VariableReading> ReadActiveAppsAsync(CancellationToken cancellationToken)
	{
		try
		{
			var apps = await _media.GetAudioAppsAsync(cancellationToken);
			var names = apps
				.Select(app => app.ProcessName)
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
				.ToList();
			return names.Count == 0 ? VariableReading.Unavailable : VariableReading.Of(string.Join(", ", names));
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Active apps read failed.");
			return VariableReading.Unavailable;
		}
	}

	private bool TryReadEager(MediaSnapshot snapshot, string localId, out VariableReading reading)
	{
		switch (localId)
		{
			case "title":
				reading = TextOrUnavailable(snapshot.HasSession, snapshot.Title);
				return true;
			case "artist":
				reading = TextOrUnavailable(snapshot.HasSession, snapshot.Artist);
				return true;
			case "album":
				reading = TextOrUnavailable(snapshot.HasSession, snapshot.Album);
				return true;
			case "source-app":
				reading = TextOrUnavailable(snapshot.HasSession, snapshot.AppId);
				return true;
			case "playback-status":
				reading = VariableReading.Of(StatusToken(snapshot));
				return true;
			case "is-playing":
				reading = VariableReading.Of(snapshot is { HasSession: true, Status: PlaybackStatus.Playing });
				return true;
			case "has-media":
				reading = VariableReading.Of(snapshot.HasSession);
				return true;
			case "position-seconds":
				reading = NumberOrUnavailable(snapshot.HasSession, snapshot.Position.TotalSeconds);
				return true;
			case "duration-seconds":
				reading = NumberOrUnavailable(snapshot.HasSession, snapshot.Duration.TotalSeconds);
				return true;
			case "position-text":
				reading = TextOrUnavailable(snapshot.HasSession, FormatTime(snapshot.Position));
				return true;
			case "duration-text":
				reading = TextOrUnavailable(snapshot.HasSession, FormatTime(snapshot.Duration));
				return true;
			case "progress-percent":
				reading = NumberOrUnavailable(snapshot.HasSession, Math.Round(snapshot.ProgressPercent, 1));
				return true;
			case "volume-percent":
				reading = snapshot.VolumePercent is int percent
					? VariableReading.Of((double)percent, 0, 100, 1)
					: VariableReading.Unavailable;
				return true;
			case "is-muted":
				reading = VariableReading.Of(snapshot.IsMuted);
				return true;
			case "mic-volume-percent":
				reading = snapshot.MicVolumePercent is int micPercent
					? VariableReading.Of((double)micPercent, 0, 100, 1)
					: VariableReading.Unavailable;
				return true;
			case "is-mic-muted":
				reading = VariableReading.Of(snapshot.IsMicMuted);
				return true;
			case "shuffle-enabled":
				reading = snapshot.ShuffleActive is null
					? VariableReading.Unavailable
					: VariableReading.Of(snapshot.ShuffleActive.Value);
				return true;
			case "repeat-mode":
				reading = TextOrUnavailable(snapshot.HasSession, RepeatToken(snapshot.RepeatMode));
				return true;
			case "album-artist":
				reading = TextOrUnavailable(snapshot.HasSession, snapshot.AlbumArtist);
				return true;
			case "genres":
				reading = TextOrUnavailable(snapshot.HasSession, string.Join(", ", snapshot.Genres));
				return true;
			case "track-number":
				reading = snapshot is { HasSession: true } && snapshot.TrackNumber > 0
					? VariableReading.Of((double)snapshot.TrackNumber)
					: VariableReading.Unavailable;
				return true;
			case "track-count":
				reading = snapshot is { HasSession: true } && snapshot.AlbumTrackCount > 0
					? VariableReading.Of((double)snapshot.AlbumTrackCount)
					: VariableReading.Unavailable;
				return true;
			case "subtitle":
				reading = TextOrUnavailable(snapshot.HasSession, snapshot.Subtitle);
				return true;
			case "playback-type":
				reading = TextOrUnavailable(snapshot.HasSession, PlaybackTypeToken(snapshot.PlaybackType));
				return true;
			case "playback-rate":
				reading = snapshot is { HasSession: true } && snapshot.PlaybackRate is double rate
					? VariableReading.Of(rate)
					: VariableReading.Unavailable;
				return true;
			case "is-live":
				reading = VariableReading.Of(snapshot is { HasSession: true, Status: PlaybackStatus.Playing } && snapshot.Duration <= TimeSpan.Zero);
				return true;
			case "can-play":
				reading = VariableReading.Of(snapshot is { HasSession: true } && snapshot.CanPlay);
				return true;
			case "can-pause":
				reading = VariableReading.Of(snapshot is { HasSession: true } && snapshot.CanPause);
				return true;
			case "can-stop":
				reading = VariableReading.Of(snapshot is { HasSession: true } && snapshot.CanStop);
				return true;
			case "can-next":
				reading = VariableReading.Of(snapshot is { HasSession: true } && snapshot.CanNext);
				return true;
			case "can-previous":
				reading = VariableReading.Of(snapshot is { HasSession: true } && snapshot.CanPrevious);
				return true;
			case "can-seek":
				reading = VariableReading.Of(snapshot is { HasSession: true } && snapshot.CanSeek);
				return true;
			case "can-shuffle":
				reading = VariableReading.Of(snapshot is { HasSession: true } && snapshot.CanShuffle);
				return true;
			case "can-repeat":
				reading = VariableReading.Of(snapshot is { HasSession: true } && snapshot.CanRepeat);
				return true;
			case "default-device":
				reading = TextOrUnavailable(!string.IsNullOrEmpty(_defaultDeviceName), _defaultDeviceName);
				return true;
			case "default-input-device":
				reading = TextOrUnavailable(!string.IsNullOrEmpty(_defaultInputDeviceName), _defaultInputDeviceName);
				return true;
			case "cover-accent":
				reading = TextOrUnavailable(snapshot.HasSession, snapshot.ArtworkAccent);
				return true;
			default:
				reading = VariableReading.Unavailable;
				return false;
		}
	}

	private async ValueTask<VariableReading> ReadAppVolumeAsync(string localId, CancellationToken cancellationToken)
	{
		var appId = NormalizeAppId(localId);
		if (!MacroDeckId.IsValidLocalId(appId, LocalIdKind.Resource))
		{
			return VariableReading.Unavailable;
		}

		try
		{
			var app = await FindAppAsync(appId, cancellationToken);
			return app is null
				? VariableReading.Unavailable
				: VariableReading.Of((double)app.VolumePercent, 0, 100, 1);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "App volume read failed.");
			return VariableReading.Unavailable;
		}
	}

	private static string NormalizeAppId(string localId)
	{
		var text = localId.Trim();
		return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
			? text[..^4].Trim()
			: text;
	}

	private async ValueTask<AudioAppSession?> FindAppAsync(string localId, CancellationToken cancellationToken)
	{
		var apps = await _media.GetAudioAppsAsync(cancellationToken);
		foreach (var app in apps)
		{
			if (string.Equals(app.ProcessName, localId, StringComparison.OrdinalIgnoreCase))
			{
				return app;
			}
		}

		return null;
	}

	public async ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken = default)
	{
		try
		{
			if (localId == "volume-percent")
			{
				var percent = MediaParameters.ReadNumberValue(value);
				if (percent is null || percent < 0 || percent > 100)
				{
					return VariableWriteResult.InvalidValue(Strings.Variables.VolumePercent.DisplayName());
				}

				await _media.SetVolumeAsync((int)Math.Round(percent.Value), cancellationToken);
				_last = await _media.GetSnapshotAsync(cancellationToken);
				return VariableWriteResult.Applied();
			}

			if (localId == "is-muted")
			{
				var muted = MediaParameters.ReadBooleanValue(value);
				if (muted is null)
				{
					return VariableWriteResult.InvalidValue(Strings.Variables.IsMuted.DisplayName());
				}

				if (muted.Value)
				{
					await _media.MuteAsync(cancellationToken);
				}
				else
				{
					await _media.UnmuteAsync(cancellationToken);
				}

				_last = await _media.GetSnapshotAsync(cancellationToken);
				return VariableWriteResult.Applied();
			}

			if (localId == "mic-volume-percent")
			{
				var percent = MediaParameters.ReadNumberValue(value);
				if (percent is null || percent < 0 || percent > 100)
				{
					return VariableWriteResult.InvalidValue(Strings.Variables.MicVolume.DisplayName());
				}

				await _media.SetMicVolumeAsync((int)Math.Round(percent.Value), cancellationToken);
				_last = await _media.GetSnapshotAsync(cancellationToken);
				return VariableWriteResult.Applied();
			}

			if (localId == "is-mic-muted")
			{
				var muted = MediaParameters.ReadBooleanValue(value);
				if (muted is null)
				{
					return VariableWriteResult.InvalidValue(Strings.Variables.MicMuted.DisplayName());
				}

				if (muted.Value)
				{
					await _media.MuteMicAsync(cancellationToken);
				}
				else
				{
					await _media.UnmuteMicAsync(cancellationToken);
				}

				_last = await _media.GetSnapshotAsync(cancellationToken);
				return VariableWriteResult.Applied();
			}

			if (localId == "position-seconds")
			{
				var position = MediaParameters.ReadNumberValue(value);
				if (position is null || position < 0)
				{
					return VariableWriteResult.InvalidValue(Strings.Variables.PositionSeconds.DisplayName());
				}

				if (!await _media.SeekToAsync(TimeSpan.FromSeconds(position.Value), cancellationToken))
				{
					return VariableWriteResult.Unavailable(Strings.Errors.MediaCommandFailed());
				}

				_last = await _media.GetSnapshotAsync(cancellationToken);
				return VariableWriteResult.Applied();
			}

			if (localId == "progress-percent")
			{
				var progress = MediaParameters.ReadNumberValue(value);
				if (progress is null || progress < 0 || progress > 100)
				{
					return VariableWriteResult.InvalidValue(Strings.Variables.ProgressPercent.DisplayName());
				}

				var duration = _last.Duration;
				if (!_last.HasSession || duration <= TimeSpan.Zero)
				{
					return VariableWriteResult.Unavailable(Strings.Errors.MediaCommandFailed());
				}

				if (!await _media.SeekToAsync(TimeSpan.FromTicks((long)(duration.Ticks * progress.Value / 100)), cancellationToken))
				{
					return VariableWriteResult.Unavailable(Strings.Errors.MediaCommandFailed());
				}

				_last = await _media.GetSnapshotAsync(cancellationToken);
				return VariableWriteResult.Applied();
			}

			if (MacroDeckId.IsValidLocalId(localId, LocalIdKind.Resource))
			{
				var percent = MediaParameters.ReadNumberValue(value);
				if (percent is null || percent < 0 || percent > 100)
				{
					return VariableWriteResult.InvalidValue(Strings.Variables.AppVolume.Description());
				}

				var appId = NormalizeAppId(localId);
				try
				{
					return await _media.SetAppVolumeAsync(appId, (int)Math.Round(percent.Value), cancellationToken)
						? VariableWriteResult.Applied()
						: VariableWriteResult.Unavailable(Strings.Variables.AppVolume.Description());
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					_logger.Debug(ex, "App volume write failed.");
					return VariableWriteResult.Unavailable(Strings.Variables.AppVolume.Description());
				}
			}

			return VariableWriteResult.NotWritable(Strings.Errors.ReadOnlyVariable());
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Variable write failed.");
			return VariableWriteResult.Unavailable(Strings.Errors.MediaCommandFailed());
		}
	}

	// No CatalogChanged calls here on purpose. The host applies a refreshed variables snapshot by
	// unregistering and re-registering the integration, which also drops this plugin's localization
	// catalog without fetching it again, so every label renders as [[plugin:...:Key]] afterwards.
	// The eager catalog below is static and the app catalog is browsed live, so a refresh would not
	// update anything anyway. Revisit if the host starts preserving catalogs across refreshes.
	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query, CancellationToken cancellationToken = default) =>
		DiscoverAppVolumesAsync(query, cancellationToken);

	private async ValueTask<VariableCatalogPage> DiscoverAppVolumesAsync(VariableCatalogQuery query, CancellationToken cancellationToken)
	{
		var items = new List<VariableDefinition>();
		try
		{
			var apps = await _media.GetAudioAppsAsync(cancellationToken);
			foreach (var app in apps)
			{
				if (!string.IsNullOrWhiteSpace(query.Search)
					&& !app.ProcessName.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (!MacroDeckId.IsValidLocalId(app.ProcessName, LocalIdKind.Resource))
				{
					continue;
				}

				items.Add(MediaVariables.AppVolume(app.ProcessName));
				if (query.PageSize > 0 && items.Count >= query.PageSize)
				{
					break;
				}
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "App volume discovery failed.");
		}

		return new VariableCatalogPage { Items = items };
	}

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default)
	{
		var appId = NormalizeAppId(localId);
		return ValueTask.FromResult<VariableDefinition?>(
			MacroDeckId.IsValidLocalId(appId, LocalIdKind.Resource) ? MediaVariables.AppVolume(appId) : null);
	}

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(IReadOnlyCollection<string> localIds, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<VariableValue>>([]);

	public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	public IReadOnlyList<MusicPlayerInstance> GetInstances() =>
		[new("system", "System media")];

	public IMusicPlayer GetPlayer(string instanceId) =>
		instanceId == "system"
			? new SystemMusicPlayer(_media)
			: throw new KeyNotFoundException($"Unknown music player instance '{instanceId}'.");

	public bool AllowsMultipleConfigurations => false;

	public IConfigFlow CreateConfigFlow() => new MediaConfigFlow();

	public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken) =>
		_widget.InitializeAsync(context, cancellationToken);

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => _widget.GetWidgetTypes();

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _widget.Surfaces;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken) =>
		_widget.CreateSessionAsync(request, cancellationToken);

	public void Dispose()
	{
		lock (_loopGate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_media.MediaChanged -= OnMediaChanged;
			foreach (var action in Actions.OfType<SleepTimerAction>())
			{
				action.CancelPendingTimer();
			}

			_loopCts?.Cancel();
			_loopCts?.Dispose();
		}
	}

	private async Task RunPollLoopAsync(CancellationToken cancellationToken)
	{
		var interval = TimeSpan.FromSeconds(Math.Clamp(_settings.Current.PollIntervalSeconds, 1, 30));
		using var timer = new PeriodicTimer(interval);
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await timer.WaitForNextTickAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}

			MediaSnapshot snapshot;
			try
			{
				snapshot = await _media.GetSnapshotAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Media poll failed.");
				continue;
			}

			lock (_snapshotGate)
			{
				var previous = _last;
				_last = snapshot;
				PublishChanges(previous, snapshot);
			}

			await RefreshDefaultDeviceAsync(cancellationToken);
		}
	}

	private async Task RefreshDefaultDeviceAsync(CancellationToken cancellationToken)
	{
		try
		{
			var devices = await _media.GetAudioDevicesAsync(cancellationToken);
			foreach (var device in devices)
			{
				if (device.IsDefault)
				{
					_defaultDeviceName = device.Name;
					break;
				}
			}

			var inputs = await _media.GetAudioInputDevicesAsync(cancellationToken);
			foreach (var device in inputs)
			{
				if (device.IsDefault)
				{
					_defaultInputDeviceName = device.Name;
					return;
				}
			}
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Default device refresh failed.");
		}
	}

	private void PublishChanges(MediaSnapshot previous, MediaSnapshot current)
	{
		var context = _context;
		if (context is null)
		{
			return;
		}

		if (TrackKey(previous) != TrackKey(current))
		{
			if (_settings.Current.TrackEvents)
			{
				context.Events.Publish("track-changed", new Dictionary<string, object?>
				{
					["title"] = current.Title,
					["artist"] = current.Artist,
					["album"] = current.Album,
					["app"] = current.AppId,
				});
			}

			if (_settings.Current.TrackToast && !string.IsNullOrWhiteSpace(current.Title))
			{
				context.Notifications.Notify(new UserNotificationRequest
				{
					Title = current.Title,
					Message = string.IsNullOrWhiteSpace(current.Artist) ? null : current.Artist,
					Key = "now-playing",
				});
			}

			// No InvalidateIconAsync here on purpose. The toggle action also provides the
			// widget icon, but the invalidate-icon host operation is not understood by the
			// current host (it fails deserialization), so calling it only produces an Error
			// log on every track change. The icon poll interval refreshes artwork instead.
		}

		if (previous.Status != current.Status || previous.HasSession != current.HasSession)
		{
			if (_settings.Current.PlaybackEvents)
			{
				context.Events.Publish("playback-changed", new Dictionary<string, object?>
				{
					["status"] = StatusToken(current),
					["isPlaying"] = current is { HasSession: true, Status: PlaybackStatus.Playing },
				});
			}
		}

		if (previous.VolumePercent != current.VolumePercent && current.VolumePercent is int volume)
		{
			if (_settings.Current.VolumeEvents)
			{
				context.Events.Publish("volume-changed", new Dictionary<string, object?>
				{
					["volume"] = (double)volume,
					["muted"] = current.IsMuted,
				});
			}
		}

		if (previous.IsMuted != current.IsMuted)
		{
			if (_settings.Current.MuteEvents)
			{
				context.Events.Publish("mute-changed", new Dictionary<string, object?>
				{
					["muted"] = current.IsMuted,
				});
			}
		}
	}

	private static string TrackKey(MediaSnapshot snapshot) =>
		snapshot.HasSession
			? $"{snapshot.AppId}\n{snapshot.Title}\n{snapshot.Artist}\n{snapshot.Album}"
			: string.Empty;

	private static string StatusToken(MediaSnapshot snapshot)
	{
		if (!snapshot.HasSession)
		{
			return "no-media";
		}

		return snapshot.Status switch
		{
			PlaybackStatus.Playing => "playing",
			PlaybackStatus.Paused => "paused",
			PlaybackStatus.Stopped => "stopped",
			_ => "no-media",
		};
	}

	private static string RepeatToken(MediaRepeatMode mode) =>
		mode switch
		{
			MediaRepeatMode.All => "all",
			MediaRepeatMode.One => "one",
			_ => "off",
		};

	private static string PlaybackTypeToken(MediaPlaybackType type) =>
		type switch
		{
			MediaPlaybackType.Music => "music",
			MediaPlaybackType.Video => "video",
			MediaPlaybackType.Image => "image",
			_ => "unknown",
		};

	private static VariableReading TextOrUnavailable(bool available, string value) =>
		available ? VariableReading.Of(value) : VariableReading.Unavailable;

	private static VariableReading NumberOrUnavailable(bool available, double value) =>
		available ? VariableReading.Of(value) : VariableReading.Unavailable;

	private static string FormatTime(TimeSpan value) => MediaText.FormatDuration(value);
}

using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Messaging;
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
using WindowsMediaControl.Messaging;
using WindowsMediaControl.Widgets;

namespace WindowsMediaControl;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IEventProvider, IMusicPlayerProvider, IWidgetTypeProvider, IUiProvider, IConfigFlowProvider, IDisposable
{
	private readonly IMediaControlService _media;
	private readonly MediaSettingsProvider _settings;
	private readonly ILogger _logger;
	private readonly IPluginCatalogNotifier? _catalogs;
	private readonly NowPlayingWidget _widget;
	private readonly object _loopGate = new();
	private readonly object _snapshotGate = new();
	// A poll tick wedged in driver calls must not hold shutdown past the
	// supervisor's grace period, or a SupervisorShutdown close (MDC0604) sees
	// a live process. The cancelled loop ends on its own; shutdown moves on.
	private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);
	private IIntegrationContext? _context;
	private IMessageChannel? _messages;
	private CancellationTokenSource? _loopCts;
	private Task? _loopTask;
	private MediaSnapshot _last = MediaSnapshot.Empty;
	private string _defaultDeviceName = string.Empty;
	private string _defaultInputDeviceName = string.Empty;
	private string? _lastAudioApps;
	private long _lastEventRefreshTicks;
	private bool _disposed;

	public PluginIntegration(IMediaControlService media, MediaSettingsProvider settings, ILogger logger, IPluginCatalogNotifier? catalogs = null)
	{
		_media = media;
		_settings = settings;
		_logger = logger.ForContext<PluginIntegration>();
		_catalogs = catalogs;
		_widget = new NowPlayingWidget(media, logger, () => _last, () => _context?.UiResources);
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
			new ToggleMuteAction(media, settings),
			new ToggleShuffleAction(media, settings),
			new SetShuffleAction(media, settings),
			new CycleRepeatAction(media, settings),
			new SetRepeatAction(media, settings),
			new SetAppVolumeAction(media),
			new AdjustAppVolumeAction(media),
			new MuteAppAction(media),
			new UnmuteAppAction(media),
			new ToggleAppMuteAction(media, settings),
			new SetOutputDeviceAction(media),
			new CycleOutputDeviceAction(media),
			new SetInputDeviceAction(media),
			new CycleInputDeviceAction(media),
			new SetMicVolumeAction(media),
			new MuteMicAction(media),
			new UnmuteMicAction(media),
			new ToggleMicMuteAction(media, settings),
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
		_messages = context.Messages;
		_media.MediaChanged -= OnMediaChanged;
		_media.MediaChanged += OnMediaChanged;
		await RegisterMessagingAsync(context.Messages).ConfigureAwait(false);
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

	private async Task RegisterMessagingAsync(IMessageChannel messages)
	{
		try
		{
			await messages.HandleRequestsAsync(
				MediaMessageTopics.StateGet,
				(_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(BuildStateSnapshot(), MediaMessageJson.Options)),
				default).ConfigureAwait(false);
		}
		catch (MessageChannelException ex)
		{
			_logger.Debug(ex, "Message channel unavailable, skipping messaging registration.");
		}
	}

	private Task PublishMessageAsync(string topic, object payload)
	{
		var channel = _messages;
		if (channel is null)
		{
			return Task.CompletedTask;
		}

		return PublishMessageCoreAsync(channel, topic, payload);
	}

	private async Task PublishMessageCoreAsync(IMessageChannel channel, string topic, object payload)
	{
		try
		{
			await channel.PublishAsync(topic, JsonSerializer.SerializeToElement(payload, MediaMessageJson.Options), CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Message publish on {Topic} failed.", topic);
		}
	}

	private MediaStateMessage BuildStateSnapshot()
	{
		MediaSnapshot snapshot;
		lock (_snapshotGate)
		{
			snapshot = _last;
		}

		return new MediaStateMessage(
			snapshot.HasSession,
			OrNull(snapshot.Title),
			OrNull(snapshot.Artist),
			OrNull(snapshot.Album),
			OrNull(snapshot.AppId),
			StatusToken(snapshot),
			snapshot is { HasSession: true, Status: PlaybackStatus.Playing },
			snapshot.VolumePercent is int volume ? volume : null,
			snapshot.IsMuted,
			snapshot.Position.TotalSeconds,
			snapshot.Duration.TotalSeconds,
			snapshot.ProgressPercent);
	}

	private static string? OrNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

	public async Task ShutdownAsync()
	{
		_media.MediaChanged -= OnMediaChanged;
		foreach (var action in Actions.OfType<SleepTimerAction>())
		{
			action.CancelPendingTimer();
		}

		Task? loop;
		lock (_loopGate)
		{
			_loopCts?.Cancel();
			loop = _loopTask;
			_loopTask = null;
		}

		if (loop != null)
		{
			await Task.WhenAny(loop, Task.Delay(ShutdownDrainTimeout, CancellationToken.None)).ConfigureAwait(false);
		}
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

		List<Action> pending;
		lock (_snapshotGate)
		{
			var previous = _last;
			_last = snapshot;
			pending = CollectChanges(previous, snapshot);
		}

		foreach (var publish in pending)
		{
			publish();
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
			// The host addresses this one by the id derived from its compatibility name
			// (media_is_playing -> media-is-playing), not by the historical "is-playing".
			case "media-is-playing":
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
				await RefreshAfterWriteAsync(cancellationToken);
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

				await RefreshAfterWriteAsync(cancellationToken);
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
				await RefreshAfterWriteAsync(cancellationToken);
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

				await RefreshAfterWriteAsync(cancellationToken);
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

				await RefreshAfterWriteAsync(cancellationToken);
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

				await RefreshAfterWriteAsync(cancellationToken);
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

	// The audio-app set is watched by the poll loop, which calls CatalogChanged when apps
	// come or go (safe since beta.4 keeps the localization catalog across re-describes).
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
		Task? loop;
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
			loop = _loopTask;
			_loopTask = null;
			_loopCts?.Dispose();
		}

		if (loop != null)
		{
			try
			{
				if (!loop.Wait(ShutdownDrainTimeout))
				{
					_logger.Debug("Poll loop drain timed out; the cancelled loop ends on its own.");
				}
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Poll loop drain failed.");
			}
		}
	}

	private async Task RunPollLoopAsync(CancellationToken cancellationToken)
	{
		var interval = TimeSpan.FromSeconds(Math.Clamp(_settings.Current.PollIntervalSeconds, 1, 30));
		using var timer = new PeriodicTimer(interval);
		var ticks = 0;
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

		List<Action> pending;
		lock (_snapshotGate)
		{
			var previous = _last;
			_last = snapshot;
			pending = CollectChanges(previous, snapshot);
		}

		// Host callbacks are network hops: run them outside the snapshot lock
		// so variable reads never queue behind a slow host round trip.
		foreach (var publish in pending)
		{
			publish();
		}

		// Device names barely change and the WinRT enumeration behind them is the
		// most expensive call in this loop, so it runs every fifth tick. The app
		// set is cheaper to read and drives the visible catalog, so it runs
		// every second tick instead of waiting on the device cadence.
		var tick = ticks++;
		if (tick % 5 == 0)
		{
			await RefreshDefaultDeviceAsync(cancellationToken);
		}

		if (tick % 2 == 0)
		{
			await RefreshAudioAppsAsync(cancellationToken);
		}
	}
}

private async Task RefreshAudioAppsAsync(CancellationToken cancellationToken)
{
	IReadOnlyList<AudioAppSession> apps;
	try
	{
		apps = await _media.GetAudioAppsAsync(cancellationToken);
	}
	catch (OperationCanceledException)
	{
		return;
	}
	catch (Exception ex)
	{
		_logger.Debug(ex, "Audio app refresh failed.");
		return;
	}

	var signature = string.Join("\n", apps
		.Select(app => app.ProcessName)
		.Where(name => !string.IsNullOrWhiteSpace(name))
		.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
	bool changed;
	lock (_snapshotGate)
	{
		changed = _lastAudioApps is not null && !string.Equals(_lastAudioApps, signature, StringComparison.Ordinal);
		_lastAudioApps = signature;
	}

	if (changed)
	{
		_catalogs?.CatalogChanged("variables", reason: "audio-apps-changed");
	}
}

	private async Task RefreshDefaultDeviceAsync(CancellationToken cancellationToken)
	{
		try
		{
			// The two enumerations are independent: overlap them instead of
			// paying both WinRT round trips back to back.
			var devicesTask = _media.GetAudioDevicesAsync(cancellationToken);
			var inputsTask = _media.GetAudioInputDevicesAsync(cancellationToken);
			await Task.WhenAll(devicesTask, inputsTask);
			foreach (var device in await devicesTask)
			{
				if (device.IsDefault)
				{
					_defaultDeviceName = device.Name;
					break;
				}
			}

			foreach (var device in await inputsTask)
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

	// A write already changed the world: re-read under the same swap + change
	// detection as the poll loop so dependents learn at once, and never report
	// a successful control call as failed just because the confirm snapshot hit
	// a wedged driver.
	private async Task RefreshAfterWriteAsync(CancellationToken cancellationToken)
	{
		MediaSnapshot snapshot;
		try
		{
			snapshot = await _media.GetSnapshotAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Post-write snapshot failed.");
			return;
		}
		List<Action> pending;
		lock (_snapshotGate)
		{
			var previous = _last;
			_last = snapshot;
			pending = CollectChanges(previous, snapshot);
		}

		foreach (var publish in pending)
		{
			publish();
		}
	}

	private List<Action> CollectChanges(MediaSnapshot previous, MediaSnapshot current)
	{
		var context = _context;
		var pending = new List<Action>();
		if (context is null)
		{
			return pending;
		}

		if (!SameTrack(previous, current))
		{
			if (_settings.Current.TrackEvents)
			{
				var title = current.Title;
				var artist = current.Artist;
				var album = current.Album;
				var app = current.AppId;
				pending.Add(() =>
				{
					context.Events.Publish("track-changed", new Dictionary<string, object?>
					{
						["title"] = title,
						["artist"] = artist,
						["album"] = album,
						["app"] = app,
					});
					_ = PublishMessageAsync(
						MediaMessageTopics.TrackChanged,
						new TrackChangedMessage(title, artist, album, app));
				});
			}

			if (_settings.Current.TrackToast && !string.IsNullOrWhiteSpace(current.Title))
			{
				var title = current.Title;
				var artist = current.Artist;
				pending.Add(() => context.Notifications.Notify(new UserNotificationRequest
				{
					Title = title,
					Message = string.IsNullOrWhiteSpace(artist) ? null : artist,
					Key = "now-playing",
				}));
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
				var status = StatusToken(current);
				var isPlaying = current is { HasSession: true, Status: PlaybackStatus.Playing };
				pending.Add(() =>
				{
					context.Events.Publish("playback-changed", new Dictionary<string, object?>
					{
						["status"] = status,
						["isPlaying"] = isPlaying,
					});
					_ = PublishMessageAsync(
						MediaMessageTopics.PlaybackChanged,
						new PlaybackChangedMessage(status, isPlaying));
				});
			}
		}

		if (previous.VolumePercent != current.VolumePercent && current.VolumePercent is int volume)
		{
			if (_settings.Current.VolumeEvents)
			{
				var muted = current.IsMuted;
				pending.Add(() =>
				{
					context.Events.Publish("volume-changed", new Dictionary<string, object?>
					{
						["volume"] = (double)volume,
						["muted"] = muted,
					});
					_ = PublishMessageAsync(
						MediaMessageTopics.VolumeChanged,
						new VolumeChangedMessage(volume, muted));
				});
			}
		}

		if (previous.IsMuted != current.IsMuted)
		{
			if (_settings.Current.MuteEvents)
			{
				var muted = current.IsMuted;
				pending.Add(() =>
				{
					context.Events.Publish("mute-changed", new Dictionary<string, object?>
					{
						["muted"] = muted,
					});
					_ = PublishMessageAsync(MediaMessageTopics.MuteChanged, new MuteChangedMessage(muted));
				});
			}
		}

		return pending;
	}

	// Field comparison instead of an interpolated key: two fewer ~200-char
	// strings per tick, off the snapshot lock.
	private static bool SameTrack(MediaSnapshot previous, MediaSnapshot current) =>
		previous.HasSession == current.HasSession
		&& string.Equals(previous.AppId, current.AppId, StringComparison.Ordinal)
		&& string.Equals(previous.Title, current.Title, StringComparison.Ordinal)
		&& string.Equals(previous.Artist, current.Artist, StringComparison.Ordinal)
		&& string.Equals(previous.Album, current.Album, StringComparison.Ordinal);

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

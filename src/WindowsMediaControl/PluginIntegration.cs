using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.ConfigFlow;
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
	private IIntegrationContext? _context;
	private CancellationTokenSource? _loopCts;
	private Task? _loopTask;
	private MediaSnapshot _last = MediaSnapshot.Empty;
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

	public bool SupportsCatalog => false;

	public bool SupportsPush => false;

	public bool SupportsSearch => false;

	public string CatalogName => string.Empty;

	public int? CatalogEntryCount => null;

	public string ProviderName => "Windows media";

	public IReadOnlyList<EventDefinition> EventDefinitions { get; }

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
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
		lock (_loopGate)
		{
			_loopCts?.Cancel();
		}

		return Task.CompletedTask;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var snapshot = _last;
		VariableReading reading = localId switch
		{
			"title" => TextOrUnavailable(snapshot.HasSession, snapshot.Title),
			"artist" => TextOrUnavailable(snapshot.HasSession, snapshot.Artist),
			"album" => TextOrUnavailable(snapshot.HasSession, snapshot.Album),
			"source-app" => TextOrUnavailable(snapshot.HasSession, snapshot.AppId),
			"playback-status" => VariableReading.Of(StatusToken(snapshot)),
			"is-playing" => VariableReading.Of(snapshot is { HasSession: true, Status: PlaybackStatus.Playing }),
			"has-media" => VariableReading.Of(snapshot.HasSession),
			"position-seconds" => NumberOrUnavailable(snapshot.HasSession, snapshot.Position.TotalSeconds),
			"duration-seconds" => NumberOrUnavailable(snapshot.HasSession, snapshot.Duration.TotalSeconds),
			"position-text" => TextOrUnavailable(snapshot.HasSession, FormatTime(snapshot.Position)),
			"duration-text" => TextOrUnavailable(snapshot.HasSession, FormatTime(snapshot.Duration)),
			"progress-percent" => NumberOrUnavailable(snapshot.HasSession, Math.Round(snapshot.ProgressPercent, 1)),
			"volume-percent" => VariableReading.Of((double)snapshot.VolumePercent, 0, 100, 1),
			"is-muted" => VariableReading.Of(snapshot.IsMuted),
			"shuffle-enabled" => snapshot.ShuffleActive is null
				? VariableReading.Unavailable
				: VariableReading.Of(snapshot.ShuffleActive.Value),
			"repeat-mode" => TextOrUnavailable(snapshot.HasSession, RepeatToken(snapshot.RepeatMode)),
			_ => VariableReading.Unavailable,
		};
		return ValueTask.FromResult(reading);
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

	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(VariableCatalogPage.Empty);

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<VariableDefinition?>(null);

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

			var previous = _last;
			_last = snapshot;
			PublishChanges(previous, snapshot);
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

		if (previous.VolumePercent != current.VolumePercent)
		{
			if (_settings.Current.VolumeEvents)
			{
				context.Events.Publish("volume-changed", new Dictionary<string, object?>
				{
					["volume"] = (double)current.VolumePercent,
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

	private static VariableReading TextOrUnavailable(bool available, string value) =>
		available ? VariableReading.Of(value) : VariableReading.Unavailable;

	private static VariableReading NumberOrUnavailable(bool available, double value) =>
		available ? VariableReading.Of(value) : VariableReading.Unavailable;

	private static string FormatTime(TimeSpan value) => MediaText.FormatDuration(value);
}

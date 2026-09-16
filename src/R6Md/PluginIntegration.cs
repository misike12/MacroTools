using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Serilog;
using R6Md.Actions;
using R6Md.Config;
using R6Md.Replays;
using R6Md.Widgets;

namespace R6Md;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IEventProvider, IConfigFlowProvider, IWidgetTypeProvider, IUiProvider, IDisposable
{
	private readonly ReplayService _replays;
	private readonly R6SettingsProvider _settings;
	private readonly OverwolfBridge _bridge;
	private readonly MatchHudWidget _widget;
	private readonly ILogger _logger;
	private IIntegrationContext? _context;
	private bool _disposed;

	public PluginIntegration(ReplayService replays, R6SettingsProvider settings, OverwolfBridge bridge, ILogger logger)
	{
		_replays = replays;
		_settings = settings;
		_bridge = bridge;
		_logger = logger.ForContext<PluginIntegration>();
		_widget = new MatchHudWidget(replays, logger);
		Actions =
		[
			new SimulateMatchAction(replays),
			new ResetSessionStatsAction(replays),
			new RescanReplaysAction(replays),
			new OpenReplayFolderAction(replays, logger),
		];
		Variables = R6Variables.CreateDefinitions();
		DeclaredVariables = Variables;
		EventDefinitions =
		[
			Event(R6EventIds.Kill,
				Strings.Events.Kill.Name(), Strings.Events.Kill.Description(),
				Param("player", ActionParameterType.String, Strings.Events.Kill.Player.Label()),
				Param("target", ActionParameterType.String, Strings.Events.Kill.Target.Label())),
			Event(R6EventIds.Headshot,
				Strings.Events.Headshot.Name(), Strings.Events.Headshot.Description(),
				Param("player", ActionParameterType.String, Strings.Events.Headshot.Player.Label()),
				Param("target", ActionParameterType.String, Strings.Events.Headshot.Target.Label())),
			Event(R6EventIds.YourKill,
				Strings.Events.YourKill.Name(), Strings.Events.YourKill.Description(),
				Param("target", ActionParameterType.String, Strings.Events.YourKill.Target.Label())),
			Event(R6EventIds.YourDeath,
				Strings.Events.YourDeath.Name(), Strings.Events.YourDeath.Description(),
				Param("player", ActionParameterType.String, Strings.Events.YourDeath.Player.Label())),
			Event(R6EventIds.RoundWon,
				Strings.Events.RoundWon.Name(), Strings.Events.RoundWon.Description(),
				Param("round", ActionParameterType.Number, Strings.Events.RoundWon.Round.Label()),
				Param("condition", ActionParameterType.String, Strings.Events.RoundWon.Condition.Label())),
			Event(R6EventIds.RoundLost,
				Strings.Events.RoundLost.Name(), Strings.Events.RoundLost.Description(),
				Param("round", ActionParameterType.Number, Strings.Events.RoundLost.Round.Label()),
				Param("condition", ActionParameterType.String, Strings.Events.RoundLost.Condition.Label())),
			Event(R6EventIds.MatchWon,
				Strings.Events.MatchWon.Name(), Strings.Events.MatchWon.Description(),
				Param("your-score", ActionParameterType.Number, Strings.Events.MatchWon.YourScore.Label()),
				Param("opp-score", ActionParameterType.Number, Strings.Events.MatchWon.OppScore.Label())),
			Event(R6EventIds.MatchLost,
				Strings.Events.MatchLost.Name(), Strings.Events.MatchLost.Description(),
				Param("your-score", ActionParameterType.Number, Strings.Events.MatchLost.YourScore.Label()),
				Param("opp-score", ActionParameterType.Number, Strings.Events.MatchLost.OppScore.Label())),
			Event(R6EventIds.Ace,
				Strings.Events.Ace.Name(), Strings.Events.Ace.Description(),
				Param("player", ActionParameterType.String, Strings.Events.Ace.Player.Label())),
			Event(R6EventIds.Clutch,
				Strings.Events.Clutch.Name(), Strings.Events.Clutch.Description(),
				Param("player", ActionParameterType.String, Strings.Events.Clutch.Player.Label())),
			Event(R6EventIds.StreakMilestone,
				Strings.Events.StreakMilestone.Name(), Strings.Events.StreakMilestone.Description(),
				Param("streak", ActionParameterType.Number, Strings.Events.StreakMilestone.Streak.Label())),
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<VariableDefinition> Variables { get; }

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; }

	public bool VariablesDependOnConfiguration => false;

	public bool SupportsCatalog => false;

	public bool SupportsPush => false;

	public bool SupportsSearch => false;

	public string CatalogName => "Matches";

	public int? CatalogEntryCount => null;

	public IReadOnlyList<EventDefinition> EventDefinitions { get; }

	public bool AllowsMultipleConfigurations => false;

	public IConfigFlow CreateConfigFlow() => new R6ConfigFlow();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_replays.MatchEvent -= OnMatchEvent;
		_replays.MatchEvent += OnMatchEvent;
		await ApplySettingsAsync();
	}

	public Task ShutdownAsync()
	{
		_replays.MatchEvent -= OnMatchEvent;
		_bridge.Stop();
		_replays.Stop();
		return Task.CompletedTask;
	}

	public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken) =>
		_widget.InitializeAsync(context, cancellationToken);

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => _widget.GetWidgetTypes();

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _widget.Surfaces;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken) =>
		_widget.CreateSessionAsync(request, cancellationToken);

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_replays.MatchEvent -= OnMatchEvent;
	}

	private async Task ApplySettingsAsync()
	{
		R6Settings settings;
		try
		{
			settings = _context is not null
				? await R6SettingsReader.ReadAsync(_context.Config)
				: R6Settings.Default;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "R6 settings read failed, using defaults.");
			settings = R6Settings.Default;
		}

		_settings.Update(settings);
		if (settings.WatchEnabled)
		{
			_replays.Start(string.IsNullOrWhiteSpace(settings.ReplayRoot) ? null : settings.ReplayRoot);
			if (_replays.ReplayRoot is null)
			{
				_logger.Warning("No MatchReplay folder was found; match data stays unavailable until one appears.");
			}
		}
		else
		{
			_replays.Stop();
		}

		if (settings.OverwolfEnabled)
		{
			if (!_bridge.Start(settings.OverwolfPort, settings.OverwolfToken))
			{
				_logger.Warning("Overwolf bridge could not bind port {Port}; live data stays unavailable.", settings.OverwolfPort);
			}
		}
		else
		{
			_bridge.Stop();
		}
	}

	private void OnMatchEvent(object? sender, R6MatchEvent matchEvent)
	{
		try
		{
			if (!EventEnabled(matchEvent.EventId))
			{
				return;
			}

			_context?.Events.Publish(matchEvent.EventId, matchEvent.Payload);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Match event publish failed.");
		}
	}

	private bool EventEnabled(string eventId)
	{
		var settings = _settings.Current;
		return eventId switch
		{
			R6EventIds.Kill or R6EventIds.Headshot or R6EventIds.YourKill or R6EventIds.YourDeath => settings.KillEvents,
			R6EventIds.RoundWon or R6EventIds.RoundLost => settings.RoundEvents,
			R6EventIds.MatchWon or R6EventIds.MatchLost => settings.MatchEvents,
			R6EventIds.Ace or R6EventIds.Clutch or R6EventIds.StreakMilestone => settings.StreakEvents,
			_ => true,
		};
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		R6Snapshot snapshot;
		try
		{
			snapshot = _replays.Snapshot();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Match state read failed.");
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		return ValueTask.FromResult(localId switch
		{
			"replay-connected" => VariableReading.Of(snapshot.Connected),
			"replay-root" => TextOrUnavailable(_replays.ReplayRoot),
			"match-active" => VariableReading.Of(snapshot.HasMatch),
			"game-version" => TextOrUnavailable(snapshot.GameVersion),
			"map" => TextOrUnavailable(snapshot.MapName),
			"mode" => TextOrUnavailable(snapshot.MapMode),
			"match-type" => TextOrUnavailable(snapshot.MatchType),
			"site" => TextOrUnavailable(snapshot.Site),
			"round" => NumberOrUnavailable(snapshot.RoundNumber, snapshot.HasMatch),
			"rounds-per-match" => NumberOrUnavailable(snapshot.RoundsPerMatch, snapshot.HasMatch),
			"overtime" => VariableReading.Of(snapshot.Overtime),
			"your-score" => NumberOrUnavailable(snapshot.YourScore, snapshot.HasMatch),
			"opp-score" => NumberOrUnavailable(snapshot.OppScore, snapshot.HasMatch),
			"your-role" => TextOrUnavailable(snapshot.YourRole),
			"opp-role" => TextOrUnavailable(snapshot.OppRole),
			"round-history" => TextOrUnavailable(snapshot.RoundHistory),
			"players-count" => NumberOrUnavailable(snapshot.Players.Count, snapshot.HasMatch),
			"top-fragger" => TextOrUnavailable(snapshot.TopFragger),
			"top-frags" => NumberOrUnavailable(snapshot.TopFrags, snapshot.HasMatch),
			"last-killer" => TextOrUnavailable(snapshot.LastKiller),
			"last-victim" => TextOrUnavailable(snapshot.LastVictim),
			"last-headshot" => snapshot.HasLastKill ? VariableReading.Of(snapshot.LastHeadshot) : VariableReading.Unavailable,
			"your-name" => TextOrUnavailable(snapshot.YourName),
			"your-operator" => TextOrUnavailable(snapshot.YourOperator),
			"your-kills" => NumberOrUnavailable(snapshot.YourKills, snapshot.HasMatch),
			"your-deaths" => NumberOrUnavailable(snapshot.YourDeaths, snapshot.HasMatch),
			"your-assists" => NumberOrUnavailable(snapshot.YourAssists, snapshot.HasMatch),
			"your-headshots" => NumberOrUnavailable(snapshot.YourHeadshots, snapshot.HasMatch),
			"session-kills" => VariableReading.Of((double)snapshot.SessionKills),
			"session-deaths" => VariableReading.Of((double)snapshot.SessionDeaths),
			"session-assists" => VariableReading.Of((double)snapshot.SessionAssists),
			"session-hs" => VariableReading.Of((double)snapshot.SessionHs),
			"streak" => VariableReading.Of((double)snapshot.Streak),
			"best-streak" => VariableReading.Of((double)snapshot.BestStreak),
			"match-outcome" => TextOrUnavailable(snapshot.MatchOutcome),
			"rounds-tracked" => VariableReading.Of((double)snapshot.RoundsTracked),
			"ow-connected" => VariableReading.Of(snapshot.OwConnected),
			"ow-phase" => TextOrUnavailable(snapshot.OwPhase),
			"your-hp" => snapshot.YourHp >= 0 ? VariableReading.Of((double)snapshot.YourHp) : VariableReading.Unavailable,
			"site-history" => TextOrUnavailable(snapshot.SiteHistory),
			"opener" => TextOrUnavailable(snapshot.Opener),
			"your-kost" => VariableReading.Of(snapshot.YourKost),
			"dcs" => NumberOrUnavailable(snapshot.Dcs, snapshot.HasMatch),
			"match-duration-minutes" => VariableReading.Of(snapshot.MatchDurationMinutes),
			_ => VariableReading.Unavailable,
		});
	}

	public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(VariableWriteResult.NotWritable(Strings.Errors.ReadOnlyVariable()));

	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(new VariableCatalogPage { Items = [] });

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<VariableDefinition?>(null);

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(IReadOnlyCollection<string> localIds, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<VariableValue>>([]);

	public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	private static EventDefinition Event(
		string id,
		LocalizedText name,
		LocalizedText description,
		params ActionParameter[] payload) => new()
		{
			Id = id,
			Name = name,
			Description = description,
			PayloadParameters = payload,
		};

	private static ActionParameter Param(string name, ActionParameterType type, LocalizedText label) => new()
	{
		Name = name,
		Type = type,
		Label = label,
	};

	private static VariableReading TextOrUnavailable(string? value) =>
		string.IsNullOrWhiteSpace(value) ? VariableReading.Unavailable : VariableReading.Of(value);

	private static VariableReading NumberOrUnavailable(double value, bool available) =>
		available ? VariableReading.Of(value) : VariableReading.Unavailable;
}

using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Messaging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Serilog;
using CsMd.Actions;
using CsMd.Config;
using CsMd.Gsi;
using CsMd.Messaging;
using CsMd.Widgets;

namespace CsMd;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IEventProvider, IConfigFlowProvider, IWidgetTypeProvider, IUiProvider, IIntegrationIssueProvider, IDisposable
{
	private readonly GsiService _gsi;
	private readonly CsSettingsProvider _settings;
	private readonly MatchHudWidget _widget;
	private readonly ILogger _logger;
	private IIntegrationContext? _context;
	private IMessageChannel? _messages;
	private bool _gsiBindFailed;
	private bool _disposed;

	public PluginIntegration(GsiService gsi, CsSettingsProvider settings, ILogger logger)
	{
		_gsi = gsi;
		_settings = settings;
		_logger = logger.ForContext<PluginIntegration>();
		_widget = new MatchHudWidget(gsi, settings, logger);
		Actions =
		[
			new InstallGsiConfigAction(settings, gsi),
			new ResetSessionStatsAction(gsi),
			new SimulateMatchAction(gsi),
			new SimulateEventAction(PublishTestEvent),
		];
		Variables = CsVariables.CreateDefinitions();
		DeclaredVariables = Variables;
		EventDefinitions =
		[
			Event("round-started",
				Strings.Events.RoundStarted.Name(), Strings.Events.RoundStarted.Description(),
				Param("round", ActionParameterType.Number, Strings.Events.RoundStarted.RoundParameter.Label())),
			Event("round-ended",
				Strings.Events.RoundEnded.Name(), Strings.Events.RoundEnded.Description(),
				Param("round", ActionParameterType.Number, Strings.Events.RoundEnded.RoundParameter.Label()),
				Param("winner", ActionParameterType.String, Strings.Events.RoundEnded.WinnerParameter.Label())),
			Event("round-won",
				Strings.Events.RoundWon.Name(), Strings.Events.RoundWon.Description(),
				Param("round", ActionParameterType.Number, Strings.Events.RoundWon.RoundParameter.Label()),
				Param("winner", ActionParameterType.String, Strings.Events.RoundWon.WinnerParameter.Label())),
			Event("round-lost",
				Strings.Events.RoundLost.Name(), Strings.Events.RoundLost.Description(),
				Param("round", ActionParameterType.Number, Strings.Events.RoundLost.RoundParameter.Label()),
				Param("winner", ActionParameterType.String, Strings.Events.RoundLost.WinnerParameter.Label())),
			Event("bomb-planted",
				Strings.Events.BombPlanted.Name(), Strings.Events.BombPlanted.Description(),
				Param("site", ActionParameterType.String, Strings.Events.BombPlanted.SiteParameter.Label())),
			Event("bomb-defused",
				Strings.Events.BombDefused.Name(), Strings.Events.BombDefused.Description()),
			Event("bomb-exploded",
				Strings.Events.BombExploded.Name(), Strings.Events.BombExploded.Description()),
			Event("player-died",
				Strings.Events.PlayerDied.Name(), Strings.Events.PlayerDied.Description(),
				Param("player", ActionParameterType.String, Strings.Events.PlayerDied.PlayerParameter.Label()),
				Param("pos-x", ActionParameterType.Number, Strings.Events.PlayerDied.PosXParameter.Label()),
				Param("pos-y", ActionParameterType.Number, Strings.Events.PlayerDied.PosYParameter.Label()),
				Param("pos-z", ActionParameterType.Number, Strings.Events.PlayerDied.PosZParameter.Label()),
				Param("place", ActionParameterType.String, Strings.Events.PlayerDied.PlaceParameter.Label())),
			Event("player-kill",
				Strings.Events.PlayerKill.Name(), Strings.Events.PlayerKill.Description(),
				Param("player", ActionParameterType.String, Strings.Events.PlayerKill.PlayerParameter.Label()),
				Param("weapon", ActionParameterType.String, Strings.Events.PlayerKill.WeaponParameter.Label()),
				Param("pos-x", ActionParameterType.Number, Strings.Events.PlayerKill.PosXParameter.Label()),
				Param("pos-y", ActionParameterType.Number, Strings.Events.PlayerKill.PosYParameter.Label()),
				Param("pos-z", ActionParameterType.Number, Strings.Events.PlayerKill.PosZParameter.Label()),
				Param("place", ActionParameterType.String, Strings.Events.PlayerKill.PlaceParameter.Label())),
			Event("match-started",
				Strings.Events.MatchStarted.Name(), Strings.Events.MatchStarted.Description(),
				Param("map", ActionParameterType.String, Strings.Events.MatchStarted.MapParameter.Label()),
				Param("mode", ActionParameterType.String, Strings.Events.MatchStarted.ModeParameter.Label())),
			Event("match-ended",
				Strings.Events.MatchEnded.Name(), Strings.Events.MatchEnded.Description(),
				Param("map", ActionParameterType.String, Strings.Events.MatchEnded.MapParameter.Label()),
				Param("winner", ActionParameterType.String, Strings.Events.MatchEnded.WinnerParameter.Label()),
				Param("ct-score", ActionParameterType.Number, Strings.Events.MatchEnded.CtScoreParameter.Label()),
				Param("t-score", ActionParameterType.Number, Strings.Events.MatchEnded.TScoreParameter.Label())),
			Event("streak-milestone",
				Strings.Events.StreakMilestone.Name(), Strings.Events.StreakMilestone.Description(),
				Param("streak", ActionParameterType.Number, Strings.Events.StreakMilestone.StreakParameter.Label())),
			Event("place-changed",
				Strings.Events.PlaceChanged.Name(), Strings.Events.PlaceChanged.Description(),
				Param("place", ActionParameterType.String, Strings.Events.PlaceChanged.PlaceParameter.Label())),
			Event("chat-message",
				Strings.Events.ChatMessage.Name(), Strings.Events.ChatMessage.Description(),
				Param("player", ActionParameterType.String, Strings.Events.ChatMessage.PlayerParameter.Label()),
				Param("scope", ActionParameterType.String, Strings.Events.ChatMessage.ScopeParameter.Label()),
				Param("text", ActionParameterType.String, Strings.Events.ChatMessage.TextParameter.Label())),
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

	public IConfigFlow CreateConfigFlow() => new CsConfigFlow();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_messages = context.Messages;
		_gsi.MatchEvent -= OnMatchEvent;
		_gsi.MatchEvent += OnMatchEvent;
		await ApplySettingsAsync();
		await RegisterMessagingAsync(context.Messages).ConfigureAwait(false);
	}

	private async Task RegisterMessagingAsync(IMessageChannel messages)
	{
		try
		{
			await messages.HandleRequestsAsync(
				CsMessageTopics.ScoreGet,
				(_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(BuildScoreSnapshot(), CsMessageJson.Options)),
				default).ConfigureAwait(false);
		}
		catch (MessageChannelException ex)
		{
			_logger.Debug(ex, "Message channel unavailable, skipping messaging registration.");
		}
	}

	public Task ShutdownAsync()
	{
		_gsi.MatchEvent -= OnMatchEvent;
		_gsi.Stop();
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
		_gsi.MatchEvent -= OnMatchEvent;
	}

	private async Task ApplySettingsAsync()
	{
		CsSettings settings;
		try
		{
			settings = _context is not null
				? await CsSettingsReader.ReadAsync(_context.Config)
				: CsSettings.Default;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "CS settings read failed, using defaults.");
			settings = CsSettings.Default;
		}

		_settings.Update(settings);
		_gsi.SetSteamIdFilter(settings.PlayerSteamId);
		_gsi.UpdatePositionOptions(settings.PositionTracking, settings.PositionIntervalSeconds, settings.PositionKeyCode);
		_gsiBindFailed = !_gsi.Start(settings.Port, settings.AuthToken);
		if (_gsiBindFailed)
		{
			_logger.Warning("GSI listener could not bind port {Port}; match data stays unavailable.", settings.Port);
		}
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<IntegrationIssue> issues = !_gsiBindFailed
			? []
			:
			[
				new IntegrationIssue
				{
					Id = "gsi-port-unavailable",
					Title = Strings.Issues.GsiPort.Title(),
					Description = Strings.Issues.GsiPort.Description(_settings.Current.Port),
					Severity = IntegrationIssueSeverity.Error,
					ActionLabel = Strings.Issues.GsiPort.Action(),
				},
			];
		return Task.FromResult(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!string.Equals(issueId, "gsi-port-unavailable", StringComparison.Ordinal))
		{
			return Task.FromResult(IssueResolution.Failed(Strings.Issues.Unknown.Text()));
		}

		try
		{
			var settings = _settings.Current;
			if (_gsi.Start(settings.Port, settings.AuthToken))
			{
				_gsiBindFailed = false;
				return Task.FromResult(IssueResolution.Ok(
					Strings.Issues.GsiPort.RetryOk(settings.Port),
					IssueResolutionFollowUp.None));
			}

			return Task.FromResult(IssueResolution.Failed(Strings.Issues.GsiPort.RetryFailed(settings.Port)));
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "GSI rebind failed.");
			return Task.FromResult(IssueResolution.Failed(Strings.Issues.GsiPort.RetryFailed(_settings.Current.Port)));
		}
	}

	private void OnMatchEvent(object? sender, GsiMatchEvent matchEvent)
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

		_ = PublishMessageAsync(CsMessageTopics.ForEvent(matchEvent.EventId), matchEvent.Payload);
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
			await channel.PublishAsync(topic, JsonSerializer.SerializeToElement(payload, CsMessageJson.Options), CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Message publish on {Topic} failed.", topic);
		}
	}

	private CsScoreMessage BuildScoreSnapshot()
	{
		GsiSnapshot snapshot;
		try
		{
			snapshot = _gsi.Snapshot();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Match state read failed.");
			return new CsScoreMessage(false, null, null, null, 0, 0, 0, null, null, null, null);
		}

		return new CsScoreMessage(
			snapshot.Connected,
			OrNull(snapshot.MapName),
			OrNull(snapshot.MapMode),
			OrNull(snapshot.MapPhase),
			snapshot.MapRound,
			snapshot.CtScore,
			snapshot.TScore,
			OrNull(snapshot.CtName),
			OrNull(snapshot.TName),
			OrNull(snapshot.RoundPhase),
			OrNull(snapshot.BombState));
	}

	private static string? OrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

	private Task PublishTestEvent(string eventId)
	{
		GsiSnapshot snapshot;
		try
		{
			snapshot = _gsi.Snapshot();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Test event snapshot failed.");
			return Task.CompletedTask;
		}

		var player = snapshot.PlayerName ?? string.Empty;
		var place = snapshot.PlaceName ?? string.Empty;
		Dictionary<string, object?> payload = eventId switch
		{
			GsiEventIds.PlayerKill => new()
			{
				["player"] = player,
				["weapon"] = snapshot.Weapon ?? string.Empty,
				["pos-x"] = snapshot.PosX,
				["pos-y"] = snapshot.PosY,
				["pos-z"] = snapshot.PosZ,
				["place"] = place,
			},
			GsiEventIds.PlayerDied => new()
			{
				["player"] = player,
				["pos-x"] = snapshot.PosX,
				["pos-y"] = snapshot.PosY,
				["pos-z"] = snapshot.PosZ,
				["place"] = place,
			},
			GsiEventIds.BombPlanted => new() { ["site"] = string.Empty },
			GsiEventIds.RoundStarted or GsiEventIds.RoundEnded or GsiEventIds.RoundWon or GsiEventIds.RoundLost => new()
			{
				["round"] = (double)snapshot.MapRound,
				["winner"] = snapshot.PlayerTeam ?? string.Empty,
			},
			GsiEventIds.MatchStarted => new()
			{
				["map"] = snapshot.MapName ?? string.Empty,
				["mode"] = snapshot.MapMode ?? string.Empty,
			},
			GsiEventIds.MatchEnded => new()
			{
				["map"] = snapshot.MapName ?? string.Empty,
				["winner"] = string.Empty,
				["ct-score"] = (double)snapshot.CtScore,
				["t-score"] = (double)snapshot.TScore,
			},
			GsiEventIds.StreakMilestone => new()
			{
				["streak"] = (double)(snapshot.KillStreak > 0 ? snapshot.KillStreak : 3),
			},
			GsiEventIds.PlaceChanged => new() { ["place"] = place },
			_ => [],
		};

		OnMatchEvent(this, new GsiMatchEvent(eventId, payload));
		return Task.CompletedTask;
	}

	private bool EventEnabled(string eventId)
	{
		var settings = _settings.Current;
		return eventId switch
		{
			GsiEventIds.PlayerKill => settings.KillEvents,
			GsiEventIds.PlayerDied => settings.DeathEvents,
			GsiEventIds.RoundStarted or GsiEventIds.RoundEnded or GsiEventIds.RoundWon or GsiEventIds.RoundLost => settings.RoundEvents,
			GsiEventIds.BombPlanted or GsiEventIds.BombDefused or GsiEventIds.BombExploded => settings.BombEvents,
			GsiEventIds.MatchStarted or GsiEventIds.MatchEnded => settings.MatchEvents,
			GsiEventIds.StreakMilestone => settings.StreakEvents,
			GsiEventIds.PlaceChanged => settings.PlaceEvents,
			GsiEventIds.ChatMessage => settings.ChatEvents,
			_ => true,
		};
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		GsiSnapshot snapshot;
		try
		{
			snapshot = _gsi.Snapshot();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Match state read failed.");
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		return ValueTask.FromResult(localId switch
		{
			"gsi-connected" => VariableReading.Of(snapshot.Connected),
			"map-name" => TextOrUnavailable(snapshot.MapName),
			"map-mode" => TextOrUnavailable(snapshot.MapMode),
			"map-phase" => TextOrUnavailable(snapshot.MapPhase),
			"map-round" => NumberOrUnavailable(snapshot.MapRound, snapshot.Connected),
			"ct-score" => NumberOrUnavailable(snapshot.CtScore, snapshot.Connected),
			"t-score" => NumberOrUnavailable(snapshot.TScore, snapshot.Connected),
			"ct-name" => TextOrUnavailable(snapshot.CtName),
			"t-name" => TextOrUnavailable(snapshot.TName),
			"round-phase" => TextOrUnavailable(snapshot.RoundPhase),
			"bomb-state" => TextOrUnavailable(snapshot.BombState),
			"phase-ends-in" => snapshot.PhaseEndsIn is { } ends ? VariableReading.Of(ends) : VariableReading.Unavailable,
			"my-team" => TextOrUnavailable(snapshot.PlayerTeam),
			"player-name" => TextOrUnavailable(snapshot.PlayerName),
			"alive" => snapshot.HasPlayer ? VariableReading.Of(snapshot.Alive) : VariableReading.Unavailable,
			"health" => NumberOrUnavailable(snapshot.Health, snapshot.HasPlayer),
			"armor" => NumberOrUnavailable(snapshot.Armor, snapshot.HasPlayer),
			"helmet" => snapshot.HasPlayer ? VariableReading.Of(snapshot.Helmet) : VariableReading.Unavailable,
			"flashed" => snapshot.HasPlayer ? VariableReading.Of(snapshot.Flashed) : VariableReading.Unavailable,
			"money" => NumberOrUnavailable(snapshot.Money, snapshot.HasPlayer),
			"weapon" => TextOrUnavailable(snapshot.Weapon),
			"ammo-clip" => snapshot.AmmoClip >= 0 ? VariableReading.Of((double)snapshot.AmmoClip) : VariableReading.Unavailable,
			"ammo-reserve" => snapshot.AmmoReserve >= 0 ? VariableReading.Of((double)snapshot.AmmoReserve) : VariableReading.Unavailable,
			"kills" => NumberOrUnavailable(snapshot.Kills, snapshot.HasPlayer),
			"deaths" => NumberOrUnavailable(snapshot.Deaths, snapshot.HasPlayer),
			"assists" => NumberOrUnavailable(snapshot.Assists, snapshot.HasPlayer),
			"mvps" => NumberOrUnavailable(snapshot.Mvps, snapshot.HasPlayer),
			"score" => NumberOrUnavailable(snapshot.Score, snapshot.HasPlayer),
			"smokes-active" => NumberOrUnavailable(snapshot.SmokesActive, snapshot.Connected),
			"fire-active" => NumberOrUnavailable(snapshot.FireActive, snapshot.Connected),
			"pos-x" => snapshot.HasPosition ? VariableReading.Of(snapshot.PosX) : VariableReading.Unavailable,
			"pos-y" => snapshot.HasPosition ? VariableReading.Of(snapshot.PosY) : VariableReading.Unavailable,
			"pos-z" => snapshot.HasPosition ? VariableReading.Of(snapshot.PosZ) : VariableReading.Unavailable,
			"position-source" => snapshot.Connected ? TextOrUnavailable(snapshot.PositionSource) : VariableReading.Unavailable,
			"bomb-countdown" => snapshot.BombCountdown is { } bombIn ? VariableReading.Of(bombIn) : VariableReading.Unavailable,
			"bomb-carrier" => TextOrUnavailable(snapshot.BombCarrier),
			"place-name" => TextOrUnavailable(snapshot.PlaceName),
			"session-kills" => VariableReading.Of((double)snapshot.SessionKills),
			"session-deaths" => VariableReading.Of((double)snapshot.SessionDeaths),
			"session-kd" => VariableReading.Of(snapshot.SessionKd),
			"round-kills" => NumberOrUnavailable(snapshot.RoundKills, snapshot.HasPlayer),
			"round-headshots" => NumberOrUnavailable(snapshot.RoundHeadshots, snapshot.HasPlayer),
			"round-damage" => NumberOrUnavailable(snapshot.RoundDamage, snapshot.HasPlayer),
			"smoked" => snapshot.HasPlayer ? VariableReading.Of(snapshot.Smoked) : VariableReading.Unavailable,
			"burning" => snapshot.HasPlayer ? VariableReading.Of(snapshot.Burning) : VariableReading.Unavailable,
			"defusekit" => snapshot.HasPlayer ? VariableReading.Of(snapshot.DefuseKit) : VariableReading.Unavailable,
			"equip-value" => NumberOrUnavailable(snapshot.EquipValue, snapshot.HasPlayer),
			"player-activity" => TextOrUnavailable(snapshot.Activity),
			"player-clan" => TextOrUnavailable(snapshot.Clan),
			"weapon-type" => TextOrUnavailable(snapshot.WeaponType),
			"round-history" => TextOrUnavailable(snapshot.RoundHistory),
			"facing-yaw" => snapshot.FacingYaw is { } yaw ? VariableReading.Of(yaw) : VariableReading.Unavailable,
			"grenades-active" => NumberOrUnavailable(snapshot.GrenadesActive, snapshot.Connected),
			"ct-timeouts" => NumberOrUnavailable(snapshot.TimeoutsCt, snapshot.Connected),
			"t-timeouts" => NumberOrUnavailable(snapshot.TimeoutsT, snapshot.Connected),
			"countdown-phase" => TextOrUnavailable(snapshot.CountdownPhase),
			"kill-streak" => VariableReading.Of((double)snapshot.KillStreak),
			"best-streak" => VariableReading.Of((double)snapshot.BestStreak),
			"top-weapon" => TextOrUnavailable(snapshot.TopWeapon),
			"top-weapon-kills" => NumberOrUnavailable(snapshot.TopWeaponKills, snapshot.TopWeapon is not null),
			"rounds-played" => VariableReading.Of((double)snapshot.RoundsPlayed),
			"session-damage" => VariableReading.Of((double)snapshot.SessionDamage),
			"loss-bonus" => GsiService.LossBonusOf(snapshot.RoundHistory, snapshot.PlayerTeam) is { } bonus and > 0
				? VariableReading.Of((double)bonus)
				: VariableReading.Unavailable,
			"session-adr" => snapshot.RoundsPlayed > 0
				? VariableReading.Of((double)snapshot.SessionDamage / snapshot.RoundsPlayed)
				: VariableReading.Of(0.0),
			"session-hs" => VariableReading.Of((double)snapshot.SessionHs),
			"hs-rate" => snapshot.SessionKills > 0
				? VariableReading.Of((double)snapshot.SessionHs / snapshot.SessionKills)
				: VariableReading.Of(0.0),
			"match-elapsed" => snapshot.Connected
				? VariableReading.Of(snapshot.MatchElapsed)
				: VariableReading.Unavailable,
			"session-match-time" => snapshot.Connected
				? VariableReading.Of(snapshot.SessionMatchTime)
				: VariableReading.Unavailable,
			"last-chat" => TextOrUnavailable(snapshot.LastChat),
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

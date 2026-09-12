using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;
using Serilog;
using CsMd.Actions;
using CsMd.Config;
using CsMd.Gsi;

namespace CsMd;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IEventProvider, IConfigFlowProvider, IDisposable
{
	private readonly GsiService _gsi;
	private readonly CsSettingsProvider _settings;
	private readonly ILogger _logger;
	private IIntegrationContext? _context;
	private bool _disposed;

	public PluginIntegration(GsiService gsi, CsSettingsProvider settings, ILogger logger)
	{
		_gsi = gsi;
		_settings = settings;
		_logger = logger.ForContext<PluginIntegration>();
		Actions =
		[
			new InstallGsiConfigAction(settings, gsi),
			new ResetSessionStatsAction(gsi),
			new SimulateMatchAction(gsi),
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
		_gsi.MatchEvent -= OnMatchEvent;
		_gsi.MatchEvent += OnMatchEvent;
		await ApplySettingsAsync();
	}

	public Task ShutdownAsync()
	{
		_gsi.MatchEvent -= OnMatchEvent;
		_gsi.Stop();
		return Task.CompletedTask;
	}

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
		if (!_gsi.Start(settings.Port, settings.AuthToken))
		{
			_logger.Warning("GSI listener could not bind port {Port}; match data stays unavailable.", settings.Port);
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
			"bomb-countdown" => snapshot.BombCountdown is { } bombIn ? VariableReading.Of(bombIn) : VariableReading.Unavailable,
			"bomb-carrier" => TextOrUnavailable(snapshot.BombCarrier),
			"place-name" => TextOrUnavailable(snapshot.PlaceName),
			"session-kills" => VariableReading.Of((double)snapshot.SessionKills),
			"session-deaths" => VariableReading.Of((double)snapshot.SessionDeaths),
			"session-kd" => VariableReading.Of(snapshot.SessionKd),
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

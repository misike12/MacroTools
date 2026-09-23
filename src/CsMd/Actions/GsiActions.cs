using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using CsMd.Config;
using CsMd.Gsi;

namespace CsMd.Actions;

internal static class CsActionResults
{
	public static ActionResult ListenerDown() =>
		ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Errors.ListenerNotRunning());

	public static ActionResult ProviderError() =>
		ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed());
}

public sealed class InstallGsiConfigAction(CsSettingsProvider settings, GsiService gsi) : IActionDefinition, IStateProviderActionDefinition
{
	public string Id => "install-gsi-config";
	public LocalizedText Name => Strings.Actions.InstallGsiConfig.Name();
	public LocalizedText Description => Strings.Actions.InstallGsiConfig.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(settings, gsi);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken) => Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(
		[
			new ActionStateDefinition("ready", Strings.Actions.InstallGsiConfig.States.Ready()),
			new ActionStateDefinition("missing", Strings.Actions.InstallGsiConfig.States.Missing()),
		],
		GsiConfig.FindCsDirectory() is null ? "missing" : "ready"));

	private sealed class Executor(CsSettingsProvider settings, GsiService gsi) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var current = settings.Current;
				var (ok, detail) = GsiConfig.Install(current.Port, current.AuthToken);
				if (!ok)
				{
					return Task.FromResult(detail == "not-found"
						? ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.CsNotFound())
						: ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.ConfigWriteFailed()));
				}

				if (!gsi.Start(current.Port, current.AuthToken))
				{
					return Task.FromResult(CsActionResults.ListenerDown());
				}

				return ActionResult.SucceededTask;
			}
			catch (Exception)
			{
				return Task.FromResult(CsActionResults.ProviderError());
			}
		}
	}
}

public sealed class ResetSessionStatsAction(GsiService gsi) : IActionDefinition
{
	public string Id => "reset-session-stats";
	public LocalizedText Name => Strings.Actions.ResetSessionStats.Name();
	public LocalizedText Description => Strings.Actions.ResetSessionStats.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(gsi);

	private sealed class Executor(GsiService gsi) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				gsi.ResetSessionStats();
				return ActionResult.SucceededTask;
			}
			catch (Exception)
			{
				return Task.FromResult(CsActionResults.ProviderError());
			}
		}
	}
}

public sealed class SimulateMatchAction(GsiService gsi) : IActionDefinition, IStateProviderActionDefinition
{
	private static readonly IReadOnlyList<ActionStateDefinition> s_states =
	[
		new("live", Strings.States.Live())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" },
		},
		new("idle", Strings.States.Idle())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568", LabelColor = "#cbd5e0" },
		},
	];

	public string Id => "simulate-match";
	public LocalizedText Name => Strings.Actions.SimulateMatch.Name();
	public LocalizedText Description => Strings.Actions.SimulateMatch.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(gsi);

	public TimeSpan StatePollInterval => TimeSpan.FromSeconds(2);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(
				s_states,
				gsi.Snapshot().Connected ? "live" : "idle"));
		}
		catch (Exception)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}
	}

	private sealed class Executor(GsiService gsi) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				gsi.InjectTestState();
				return Task.FromResult(ActionResult.Success("live"));
			}
			catch (Exception)
			{
				return Task.FromResult(CsActionResults.ProviderError());
			}
		}
	}
}

public sealed class SimulateEventAction(Func<string, Task> publish) : IActionDefinition
{
	public static readonly IReadOnlyList<string> KnownEvents =
	[
		"round-started", "round-ended", "round-won", "round-lost",
		"bomb-planted", "bomb-defused", "bomb-exploded",
		"player-died", "player-kill",
		"match-started", "match-ended",
		"streak-milestone", "place-changed",
	];

	public string Id => "simulate-event";
	public LocalizedText Name => Strings.Actions.SimulateEvent.Name();
	public LocalizedText Description => Strings.Actions.SimulateEvent.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(
			"event",
			[
				new ActionParameterOption { Value = "round-started", Label = Strings.Events.RoundStarted.Name() },
				new ActionParameterOption { Value = "round-ended", Label = Strings.Events.RoundEnded.Name() },
				new ActionParameterOption { Value = "round-won", Label = Strings.Events.RoundWon.Name() },
				new ActionParameterOption { Value = "round-lost", Label = Strings.Events.RoundLost.Name() },
				new ActionParameterOption { Value = "bomb-planted", Label = Strings.Events.BombPlanted.Name() },
				new ActionParameterOption { Value = "bomb-defused", Label = Strings.Events.BombDefused.Name() },
				new ActionParameterOption { Value = "bomb-exploded", Label = Strings.Events.BombExploded.Name() },
				new ActionParameterOption { Value = "player-died", Label = Strings.Events.PlayerDied.Name() },
				new ActionParameterOption { Value = "player-kill", Label = Strings.Events.PlayerKill.Name() },
				new ActionParameterOption { Value = "match-started", Label = Strings.Events.MatchStarted.Name() },
				new ActionParameterOption { Value = "match-ended", Label = Strings.Events.MatchEnded.Name() },
				new ActionParameterOption { Value = "streak-milestone", Label = Strings.Events.StreakMilestone.Name() },
				new ActionParameterOption { Value = "place-changed", Label = Strings.Events.PlaceChanged.Name() },
			],
			Strings.Actions.SimulateEvent.Event.Label(),
			Strings.Actions.SimulateEvent.Event.Description(),
			"player-kill"),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(publish);

	private sealed class Executor(Func<string, Task> publish) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			string? eventId = null;
			if (context.Parameters.TryGetValue("event", out var raw))
			{
				var text = raw?.ToString();
				eventId = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
			}

			if (eventId is null || !KnownEvents.Contains(eventId, StringComparer.Ordinal))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.FieldInvalid());
			}

			try
			{
				await publish(eventId);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return CsActionResults.ProviderError();
			}
		}
	}
}

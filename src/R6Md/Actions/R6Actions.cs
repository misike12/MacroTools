using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using R6Md.Replays;

namespace R6Md.Actions;

internal static class R6ActionResults
{
	public static ActionResult ProviderError() =>
		ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed());
}

public sealed class SimulateMatchAction(ReplayService replays) : IActionDefinition, IStateProviderActionDefinition
{
	private static readonly IReadOnlyList<ActionStateDefinition> s_states =
	[
		new("tracking", MacroDeckStrings.States.On())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" },
		},
		new("idle", MacroDeckStrings.States.Off())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568", LabelColor = "#cbd5e0" },
		},
	];

	public string Id => "simulate-match";
	public LocalizedText Name => Strings.Actions.SimulateMatch.Name();
	public LocalizedText Description => Strings.Actions.SimulateMatch.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(replays);

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
				replays.Snapshot().HasMatch ? "tracking" : "idle"));
		}
		catch (Exception)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}
	}

	private sealed class Executor(ReplayService replays) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				replays.InjectSample();
				return Task.FromResult(ActionResult.Success("tracking"));
			}
			catch (Exception)
			{
				return Task.FromResult(R6ActionResults.ProviderError());
			}
		}
	}
}

public sealed class ResetSessionStatsAction(ReplayService replays) : IActionDefinition
{
	public string Id => "reset-session-stats";
	public LocalizedText Name => Strings.Actions.ResetSessionStats.Name();
	public LocalizedText Description => Strings.Actions.ResetSessionStats.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(replays);

	private sealed class Executor(ReplayService replays) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				replays.ResetSessionStats();
				return ActionResult.SucceededTask;
			}
			catch (Exception)
			{
				return Task.FromResult(R6ActionResults.ProviderError());
			}
		}
	}
}

public sealed class RescanReplaysAction(ReplayService replays) : IActionDefinition
{
	public string Id => "rescan-replays";
	public LocalizedText Name => Strings.Actions.RescanReplays.Name();
	public LocalizedText Description => Strings.Actions.RescanReplays.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(replays);

	private sealed class Executor(ReplayService replays) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				await replays.RescanNowAsync(context.CancellationToken);
				return ActionResult.Success();
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception)
			{
				return R6ActionResults.ProviderError();
			}
		}
	}
}

public sealed class OpenReplayFolderAction(ReplayService replays, Serilog.ILogger logger) : IActionDefinition
{
	public string Id => "open-replay-folder";
	public LocalizedText Name => Strings.Actions.OpenReplayFolder.Name();
	public LocalizedText Description => Strings.Actions.OpenReplayFolder.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(replays, logger);

	private sealed class Executor(ReplayService replays, Serilog.ILogger logger) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return Task.FromResult(Widgets.MatchHudWidget.OpenReplayFolder(replays, logger)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.ReplayRootMissing()));
			}
			catch (Exception)
			{
				return Task.FromResult(R6ActionResults.ProviderError());
			}
		}
	}
}

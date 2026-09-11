using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Timers.Timing;

namespace Timers.Actions;

public sealed class StartStopwatchAction(TimerService timers) : IActionDefinition
{
	public string Id => "start-stopwatch";
	public LocalizedText Name => Strings.Actions.StartStopwatch.Name();
	public LocalizedText Description => Strings.Actions.StartStopwatch.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				timers.StartStopwatch();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class StopStopwatchAction(TimerService timers) : IActionDefinition
{
	public string Id => "stop-stopwatch";
	public LocalizedText Name => Strings.Actions.StopStopwatch.Name();
	public LocalizedText Description => Strings.Actions.StopStopwatch.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				timers.StopStopwatch();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class ResetStopwatchAction(TimerService timers) : IActionDefinition
{
	public string Id => "reset-stopwatch";
	public LocalizedText Name => Strings.Actions.ResetStopwatch.Name();
	public LocalizedText Description => Strings.Actions.ResetStopwatch.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				timers.ResetStopwatch();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class ToggleStopwatchAction(TimerService timers) : IActionDefinition
{
	public string Id => "toggle-stopwatch";
	public LocalizedText Name => Strings.Actions.ToggleStopwatch.Name();
	public LocalizedText Description => Strings.Actions.ToggleStopwatch.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				timers.ToggleStopwatch();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Timers.Timing;

namespace Timers.Actions;

public sealed class StartPomodoroAction(PomodoroService pomodoro) : IActionDefinition
{
	public string Id => "start-pomodoro";
	public LocalizedText Name => Strings.Actions.StartPomodoro.Name();
	public LocalizedText Description => Strings.Actions.StartPomodoro.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = "work-minutes",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.StartPomodoro.WorkMinutes.Label(),
			Description = Strings.Actions.StartPomodoro.WorkMinutes.Description(),
			Min = 1,
			Max = 180,
			Step = 1,
			DefaultValue = 25.0,
		},
		new ActionParameter
		{
			Name = "short-break-minutes",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.StartPomodoro.ShortBreak.Label(),
			Description = Strings.Actions.StartPomodoro.ShortBreak.Description(),
			Min = 1,
			Max = 60,
			Step = 1,
			DefaultValue = 5.0,
		},
		new ActionParameter
		{
			Name = "long-break-minutes",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.StartPomodoro.LongBreak.Label(),
			Description = Strings.Actions.StartPomodoro.LongBreak.Description(),
			Min = 1,
			Max = 90,
			Step = 1,
			DefaultValue = 15.0,
		},
		new ActionParameter
		{
			Name = "rounds",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.StartPomodoro.Rounds.Label(),
			Description = Strings.Actions.StartPomodoro.Rounds.Description(),
			Min = 1,
			Max = 12,
			Step = 1,
			DefaultValue = 4.0,
		},
		new ActionParameter
		{
			Name = "auto-advance",
			Type = ActionParameterType.Boolean,
			Label = Strings.Actions.StartPomodoro.AutoAdvance.Label(),
			Description = Strings.Actions.StartPomodoro.AutoAdvance.Description(),
			DefaultValue = true,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(pomodoro);

	private sealed class Executor(PomodoroService pomodoro) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var work = TimerParameters.ReadSeconds(context.Parameters, "work-minutes", 25.0);
			var shortBreak = TimerParameters.ReadSeconds(context.Parameters, "short-break-minutes", 5.0);
			var longBreak = TimerParameters.ReadSeconds(context.Parameters, "long-break-minutes", 15.0);
			var rounds = TimerParameters.ReadSeconds(context.Parameters, "rounds", 4.0);
			var autoAdvance = TimerParameters.ReadBool(context.Parameters, "auto-advance", true);
			if (work is null || work < 1 || work > 180
				|| shortBreak is null || shortBreak < 1 || shortBreak > 60
				|| longBreak is null || longBreak < 1 || longBreak > 90
				|| rounds is null || rounds < 1 || rounds > 12
				|| autoAdvance is null)
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.StartPomodoro.WorkMinutes.Label()));
			}

			try
			{
				pomodoro.Start(new PomodoroSettings(work.Value, shortBreak.Value, longBreak.Value, (int)rounds.Value, autoAdvance.Value));
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class StopPomodoroAction(PomodoroService pomodoro) : IActionDefinition
{
	public string Id => "stop-pomodoro";
	public LocalizedText Name => Strings.Actions.StopPomodoro.Name();
	public LocalizedText Description => Strings.Actions.StopPomodoro.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(pomodoro);

	private sealed class Executor(PomodoroService pomodoro) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				pomodoro.Stop();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class SkipPomodoroPhaseAction(PomodoroService pomodoro) : IActionDefinition
{
	public string Id => "skip-pomodoro-phase";
	public LocalizedText Name => Strings.Actions.SkipPomodoroPhase.Name();
	public LocalizedText Description => Strings.Actions.SkipPomodoroPhase.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(pomodoro);

	private sealed class Executor(PomodoroService pomodoro) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				pomodoro.Skip();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class TogglePomodoroAction(PomodoroService pomodoro) : IActionDefinition
{
	public string Id => "toggle-pomodoro";
	public LocalizedText Name => Strings.Actions.TogglePomodoro.Name();
	public LocalizedText Description => Strings.Actions.TogglePomodoro.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(pomodoro);

	private sealed class Executor(PomodoroService pomodoro) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				pomodoro.Toggle();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

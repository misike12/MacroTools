using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Timers.Timing;

namespace Timers.Actions;

internal static class TimerParameters
{
	public static double? ReadSeconds(IReadOnlyDictionary<string, object> parameters, string name, double fallback)
	{
		if (!parameters.TryGetValue(name, out var raw) || raw is null)
		{
			return fallback;
		}

		if (raw is string text && string.IsNullOrWhiteSpace(text))
		{
			return fallback;
		}

		double? value = raw switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => null,
		};
		return value is { } finite && double.IsFinite(finite) ? finite : null;
	}

	public static string ReadLabel(IReadOnlyDictionary<string, object> parameters)
	{
		if (!parameters.TryGetValue("label", out var raw))
		{
			return string.Empty;
		}

		var text = raw?.ToString();
		return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
	}
}

public sealed class StartCountdownAction(TimerService timers) : IActionDefinition
{
	public string Id => "start-countdown";
	public LocalizedText Name => Strings.Actions.StartCountdown.Name();
	public LocalizedText Description => Strings.Actions.StartCountdown.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = "hours",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.StartCountdown.Hours.Label(),
			Description = Strings.Actions.StartCountdown.Hours.Description(),
			Min = 0,
			Max = 24,
			Step = 1,
			DefaultValue = 0.0,
		},
		new ActionParameter
		{
			Name = "minutes",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.StartCountdown.Minutes.Label(),
			Description = Strings.Actions.StartCountdown.Minutes.Description(),
			Min = 0,
			Max = 59,
			Step = 1,
			DefaultValue = 5.0,
		},
		new ActionParameter
		{
			Name = "seconds",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.StartCountdown.Seconds.Label(),
			Description = Strings.Actions.StartCountdown.Seconds.Description(),
			Min = 0,
			Max = 59,
			Step = 1,
			DefaultValue = 0.0,
		},
		new ActionParameter
		{
			Name = "label",
			Type = ActionParameterType.String,
			Label = Strings.Actions.StartCountdown.TimerLabel.Label(),
			Description = Strings.Actions.StartCountdown.TimerLabel.Description(),
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var hours = TimerParameters.ReadSeconds(context.Parameters, "hours", 0.0);
			var minutes = TimerParameters.ReadSeconds(context.Parameters, "minutes", 5.0);
			var seconds = TimerParameters.ReadSeconds(context.Parameters, "seconds", 0.0);
			if (hours is null || minutes is null || seconds is null
				|| hours < 0 || hours > 24 || minutes < 0 || minutes > 59 || seconds < 0 || seconds > 59)
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.StartCountdown.Seconds.Label()));
			}

			var total = TimeSpan.FromHours(hours.Value) + TimeSpan.FromMinutes(minutes.Value) + TimeSpan.FromSeconds(seconds.Value);
			if (total > TimeSpan.FromHours(24))
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.StartCountdown.Seconds.Label()));
			}

			try
			{
				timers.StartCountdown(total, TimerParameters.ReadLabel(context.Parameters));
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class PauseCountdownAction(TimerService timers) : IActionDefinition
{
	public string Id => "pause-countdown";
	public LocalizedText Name => Strings.Actions.PauseCountdown.Name();
	public LocalizedText Description => Strings.Actions.PauseCountdown.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				timers.PauseCountdown();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class ResumeCountdownAction(TimerService timers) : IActionDefinition
{
	public string Id => "resume-countdown";
	public LocalizedText Name => Strings.Actions.ResumeCountdown.Name();
	public LocalizedText Description => Strings.Actions.ResumeCountdown.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				timers.ResumeCountdown();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class CancelCountdownAction(TimerService timers) : IActionDefinition
{
	public string Id => "cancel-countdown";
	public LocalizedText Name => Strings.Actions.CancelCountdown.Name();
	public LocalizedText Description => Strings.Actions.CancelCountdown.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				timers.CancelCountdown();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class ToggleCountdownAction(TimerService timers) : IActionDefinition
{
	public string Id => "toggle-countdown";
	public LocalizedText Name => Strings.Actions.ToggleCountdown.Name();
	public LocalizedText Description => Strings.Actions.ToggleCountdown.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				timers.ToggleCountdown();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

public sealed class AdjustCountdownAction(TimerService timers) : IActionDefinition
{
	public string Id => "adjust-countdown";
	public LocalizedText Name => Strings.Actions.AdjustCountdown.Name();
	public LocalizedText Description => Strings.Actions.AdjustCountdown.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = "delta",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.AdjustCountdown.Delta.Label(),
			Description = Strings.Actions.AdjustCountdown.Delta.Description(),
			Min = -86400,
			Max = 86400,
			Step = 1,
			DefaultValue = 60.0,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(timers);

	private sealed class Executor(TimerService timers) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var delta = TimerParameters.ReadSeconds(context.Parameters, "delta", 60.0);
			if (delta is null || delta < -86400 || delta > 86400)
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.AdjustCountdown.Delta.Label()));
			}

			if (delta == 0)
			{
				return Task.FromResult(ActionResult.Success());
			}

			try
			{
				timers.AdjustCountdown(TimeSpan.FromSeconds(delta.Value));
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed()));
			}
		}
	}
}

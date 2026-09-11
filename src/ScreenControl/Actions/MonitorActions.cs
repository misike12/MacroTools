using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScreenControl.Monitors;

namespace ScreenControl.Actions;

internal static class DisplayActionResults
{
	public static ActionResult NoMonitor() =>
		ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Errors.MonitorNotAvailable());

	public static ActionResult ProviderError() =>
		ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed());
}

public sealed class SetMonitorBrightnessAction(IMonitorService monitors) : IActionDefinition
{
	public string Id => "set-monitor-brightness";
	public LocalizedText Name => Strings.Actions.SetMonitorBrightness.Name();
	public LocalizedText Description => Strings.Actions.SetMonitorBrightness.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DisplayParameters.MonitorOption(),
		new ActionParameter
		{
			Name = DisplayParameters.BrightnessParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.SetMonitorBrightness.Brightness.Label(),
			Description = Strings.Actions.SetMonitorBrightness.Brightness.Description(),
			Min = 0,
			Max = 100,
			Step = 1,
			DefaultValue = 80.0,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(monitors);

	private sealed class Executor(IMonitorService monitors) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var brightness = DisplayParameters.ReadNumber(context.Parameters, DisplayParameters.BrightnessParameter) ?? 80.0;
			if (brightness < 0 || brightness > 100)
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetMonitorBrightness.Brightness.Label()));
			}

			try
			{
				return Task.FromResult(monitors.TrySetBrightness(DisplayParameters.ReadMonitor(context.Parameters), (int)brightness)
					? ActionResult.Success()
					: DisplayActionResults.NoMonitor());
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class AdjustMonitorBrightnessAction(IMonitorService monitors) : IActionDefinition
{
	public string Id => "adjust-monitor-brightness";
	public LocalizedText Name => Strings.Actions.AdjustMonitorBrightness.Name();
	public LocalizedText Description => Strings.Actions.AdjustMonitorBrightness.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DisplayParameters.MonitorOption(),
		new ActionParameter
		{
			Name = "delta",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.AdjustMonitorBrightness.Delta.Label(),
			Description = Strings.Actions.AdjustMonitorBrightness.Delta.Description(),
			Min = -50,
			Max = 50,
			Step = 1,
			DefaultValue = 5.0,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(monitors);

	private sealed class Executor(IMonitorService monitors) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var delta = DisplayParameters.ReadNumber(context.Parameters, "delta") ?? 5.0;
			if (delta == 0)
			{
				return Task.FromResult(ActionResult.Success());
			}

			try
			{
				var monitor = monitors.GetMonitors()
					.FirstOrDefault(m => m.Index == DisplayParameters.ReadMonitor(context.Parameters));
				if (monitor is null || !monitor.SupportsBrightness)
				{
					return Task.FromResult(DisplayActionResults.NoMonitor());
				}

				return Task.FromResult(monitors.TrySetBrightness(monitor.Index, monitor.BrightnessPercent + (int)delta)
					? ActionResult.Success()
					: DisplayActionResults.NoMonitor());
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class SetMonitorInputAction(IMonitorService monitors) : IActionDefinition
{
	public string Id => "set-monitor-input";
	public LocalizedText Name => Strings.Actions.SetMonitorInput.Name();
	public LocalizedText Description => Strings.Actions.SetMonitorInput.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DisplayParameters.MonitorOption(),
		new ActionParameter
		{
			Name = DisplayParameters.InputParameter,
			Type = ActionParameterType.Choice,
			Label = Strings.Actions.SetMonitorInput.Input.Label(),
			Description = Strings.Actions.SetMonitorInput.Input.Description(),
			Options =
			[
				new ActionParameterOption { Value = "hdmi1", Label = Strings.Inputs.Hdmi1() },
				new ActionParameterOption { Value = "hdmi2", Label = Strings.Inputs.Hdmi2() },
				new ActionParameterOption { Value = "dp1", Label = Strings.Inputs.DisplayPort1() },
				new ActionParameterOption { Value = "dp2", Label = Strings.Inputs.DisplayPort2() },
				new ActionParameterOption { Value = "dvi", Label = Strings.Inputs.Dvi() },
			],
			DefaultValue = "hdmi1",
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(monitors);

	private sealed class Executor(IMonitorService monitors) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var input = context.Parameters.TryGetValue(DisplayParameters.InputParameter, out var raw)
				? raw?.ToString()?.ToLowerInvariant()
				: "hdmi1";
			var vcp = input switch
			{
				"hdmi1" => 0x11,
				"hdmi2" => 0x12,
				"dp1" => 0x0F,
				"dp2" => 0x10,
				"dvi" => 0x03,
				_ => (int?)null,
			};
			if (vcp is null)
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetMonitorInput.Input.Label()));
			}

			try
			{
				return Task.FromResult(monitors.TrySetInput(DisplayParameters.ReadMonitor(context.Parameters), vcp.Value)
					? ActionResult.Success()
					: DisplayActionResults.NoMonitor());
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class CycleMonitorInputAction(IMonitorService monitors) : IActionDefinition
{
	private static readonly int[] DefaultInputOrder = [0x11, 0x12, 0x0F, 0x10, 0x03];

	public string Id => "cycle-monitor-input";
	public LocalizedText Name => Strings.Actions.CycleMonitorInput.Name();
	public LocalizedText Description => Strings.Actions.CycleMonitorInput.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [DisplayParameters.MonitorOption()];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(monitors);

	private sealed class Executor(IMonitorService monitors) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var index = DisplayParameters.ReadMonitor(context.Parameters);
				var supported = monitors.GetSupportedInputs(index);
				var order = supported.Count > 0 ? supported : DefaultInputOrder;
				var current = monitors.TryGetInput(index);
				var next = order[0];
				if (current is int known)
				{
					for (var i = 0; i < order.Count; i++)
					{
						if (order[i] == known)
						{
							next = order[(i + 1) % order.Count];
							break;
						}
					}
				}
				return Task.FromResult(monitors.TrySetInput(index, next)
					? ActionResult.Success()
					: DisplayActionResults.NoMonitor());
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class SetMonitorPowerAction(IMonitorService monitors) : IActionDefinition
{
	public string Id => "set-monitor-power";
	public LocalizedText Name => Strings.Actions.SetMonitorPower.Name();
	public LocalizedText Description => Strings.Actions.SetMonitorPower.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DisplayParameters.MonitorOption(),
		new ActionParameter
		{
			Name = DisplayParameters.PowerParameter,
			Type = ActionParameterType.Choice,
			Label = Strings.Actions.SetMonitorPower.Mode.Label(),
			Description = Strings.Actions.SetMonitorPower.Mode.Description(),
			Options =
			[
				new ActionParameterOption { Value = "on", Label = Strings.Power.On() },
				new ActionParameterOption { Value = "standby", Label = Strings.Power.Standby() },
				new ActionParameterOption { Value = "off", Label = Strings.Power.Off() },
			],
			DefaultValue = "on",
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(monitors);

	private sealed class Executor(IMonitorService monitors) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var mode = context.Parameters.TryGetValue(DisplayParameters.PowerParameter, out var raw)
				? raw?.ToString()?.ToLowerInvariant()
				: "on";
			var dpm = mode switch
			{
				"on" => MonitorPowerModes.On,
				"standby" => MonitorPowerModes.Standby,
				"off" => MonitorPowerModes.Off,
				_ => (int?)null,
			};
			if (dpm is null)
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetMonitorPower.Mode.Label()));
			}

			try
			{
				return Task.FromResult(monitors.TrySetPower(DisplayParameters.ReadMonitor(context.Parameters), dpm.Value)
					? ActionResult.Success()
					: DisplayActionResults.NoMonitor());
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

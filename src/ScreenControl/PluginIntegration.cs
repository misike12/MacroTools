using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using ScreenControl.Actions;
using ScreenControl.Monitors;
using ScreenControl.Windows;
using Serilog;

namespace ScreenControl;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider
{
	private readonly IMonitorService _monitors;
	private readonly IWindowService _windows;
	private readonly ILogger _logger;

	public PluginIntegration(IMonitorService monitors, IWindowService windows, ILogger logger)
	{
		_monitors = monitors;
		_windows = windows;
		_logger = logger.ForContext<PluginIntegration>();
		Actions =
		[
			new SetMonitorBrightnessAction(monitors),
			new AdjustMonitorBrightnessAction(monitors),
			new SetMonitorInputAction(monitors),
			new CycleMonitorInputAction(monitors),
			new SetMonitorPowerAction(monitors),
			new FocusWindowAction(windows),
			new MinimizeWindowAction(windows),
			new MaximizeWindowAction(windows),
			new RestoreWindowAction(windows),
			new CloseWindowAction(windows),
			new ToggleAlwaysOnTopAction(windows),
			new SnapWindowAction(windows),
			new NextDesktopAction(windows),
			new PreviousDesktopAction(windows),
		];
		Variables = DisplayVariables.CreateDefinitions();
		DeclaredVariables = Variables;
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<VariableDefinition> Variables { get; }

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; }

	public bool VariablesDependOnConfiguration => false;

	public bool SupportsCatalog => false;

	public bool SupportsPush => false;

	public bool SupportsSearch => false;

	public string CatalogName => "Screens";

	public int? CatalogEntryCount => null;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		try
		{
			return localId switch
			{
				"monitor-count" => ValueTask.FromResult(VariableReading.Of((double)_monitors.GetMonitors().Count)),
				"primary-brightness" => ValueTask.FromResult(ReadPrimaryBrightness()),
				"primary-input" => ValueTask.FromResult(ReadPrimaryInput()),
				"focused-window-title" => ValueTask.FromResult(
					TextOrUnavailable(_windows.GetForeground()?.Title)),
				"focused-window-process" => ValueTask.FromResult(
					TextOrUnavailable(_windows.GetForeground()?.ProcessName)),
				"focused-window-topmost" => ValueTask.FromResult(ReadFocusedTopmost()),
				_ => ValueTask.FromResult(VariableReading.Unavailable),
			};
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Variable read failed.");
			return ValueTask.FromResult(VariableReading.Unavailable);
		}
	}

	public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken = default)
	{
		try
		{
			if (localId == "primary-brightness")
			{
				var brightness = DisplayParameters.ReadNumberValue(value);
				if (brightness is null || brightness < 0 || brightness > 100)
				{
					return ValueTask.FromResult(VariableWriteResult.InvalidValue(Strings.Variables.PrimaryBrightness.DisplayName()));
				}

				var primary = PrimaryMonitor(_monitors.GetMonitors());
				if (primary is null || !_monitors.TrySetBrightness(primary.Index, (int)brightness))
				{
					return ValueTask.FromResult(VariableWriteResult.Unavailable(Strings.Errors.MonitorNotAvailable()));
				}

				return ValueTask.FromResult(VariableWriteResult.Applied());
			}

			if (localId == "focused-window-topmost")
			{
				var topmost = ReadBoolValue(value);
				if (topmost is null)
				{
					return ValueTask.FromResult(VariableWriteResult.InvalidValue(Strings.Variables.FocusedTopmost.DisplayName()));
				}

				var focused = _windows.GetForeground();
				if (focused is null || !_windows.SetTopmost(focused.Handle, topmost.Value))
				{
					return ValueTask.FromResult(VariableWriteResult.Unavailable(Strings.Errors.WindowNotFound()));
				}

				return ValueTask.FromResult(VariableWriteResult.Applied());
			}

			return ValueTask.FromResult(VariableWriteResult.NotWritable(Strings.Errors.ReadOnlyVariable()));
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Variable write failed.");
			return ValueTask.FromResult(VariableWriteResult.Unavailable(Strings.Errors.CommandFailed()));
		}
	}

	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(new VariableCatalogPage { Items = [] });

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<VariableDefinition?>(null);

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(IReadOnlyCollection<string> localIds, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<VariableValue>>([]);

	public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	private VariableReading ReadPrimaryBrightness()
	{
		var primary = PrimaryMonitor(_monitors.GetMonitors());
		return primary is not null && primary.SupportsBrightness
			? VariableReading.Of((double)primary.BrightnessPercent, 0, 100, 1)
			: VariableReading.Unavailable;
	}

	private VariableReading ReadPrimaryInput()
	{
		var primary = PrimaryMonitor(_monitors.GetMonitors());
		if (primary is null)
		{
			return VariableReading.Unavailable;
		}

		var input = _monitors.TryGetInput(primary.Index);
		if (input is null)
		{
			return VariableReading.Unavailable;
		}

		return VariableReading.Of(input.Value switch
		{
			0x11 => "hdmi1",
			0x12 => "hdmi2",
			0x0F => "dp1",
			0x10 => "dp2",
			0x03 => "dvi",
			_ => "unknown",
		});
	}

	private VariableReading ReadFocusedTopmost()
	{
		var focused = _windows.GetForeground();
		return focused is null
			? VariableReading.Unavailable
			: VariableReading.Of(_windows.IsTopmost(focused.Handle));
	}

	private static Monitors.MonitorInfo? PrimaryMonitor(IReadOnlyList<Monitors.MonitorInfo> monitors)
	{
		foreach (var monitor in monitors)
		{
			if (monitor.IsPrimary)
			{
				return monitor;
			}
		}

		return monitors.Count > 0 ? monitors[0] : null;
	}

	private static VariableReading TextOrUnavailable(string? value) =>
		string.IsNullOrWhiteSpace(value) ? VariableReading.Unavailable : VariableReading.Of(value);

	private static bool? ReadBoolValue(object? value) => value switch
	{
		bool b => b,
		double d when d == 0 => false,
		double d when d == 1 => true,
		float f when f == 0 => false,
		float f when f == 1 => true,
		int i when i == 0 => false,
		int i when i == 1 => true,
		long l when l == 0 => false,
		long l when l == 1 => true,
		string s when bool.TryParse(s, out var parsed) => parsed,
		string s when s.Trim() == "0" => false,
		string s when s.Trim() == "1" => true,
		_ => null,
	};
}

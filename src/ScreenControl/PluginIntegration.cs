using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Messaging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using ScreenControl.Actions;
using ScreenControl.Messaging;
using ScreenControl.Monitors;
using ScreenControl.Widgets;
using ScreenControl.Windows;
using Serilog;

namespace ScreenControl;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IWidgetTypeProvider, IUiProvider, IIntegrationIssueProvider, IDisposable
{
	private static readonly TimeSpan CatalogWatchInterval = TimeSpan.FromSeconds(30);
	// A watch tick wedged in driver calls must not hold shutdown past the
	// supervisor's grace period, or a SupervisorShutdown close (MDC0604) sees
	// a live process. The cancelled loop ends on its own; shutdown moves on.
	private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

	private readonly IMonitorService _monitors;
	private readonly IWindowService _windows;
	private readonly BrightnessWidget _brightness;
	private readonly ILogger _logger;
	private readonly IPluginCatalogNotifier? _catalogs;
	private readonly MonitorCatalogWatcher _watcher = new();
	private readonly object _loopGate = new();
	private CancellationTokenSource? _loopCts;
	private Task? _loopTask;
	private IMessageChannel? _messages;
	private bool _disposed;

	public PluginIntegration(IMonitorService monitors, IWindowService windows, ILogger logger, IPluginCatalogNotifier? catalogs = null)
	{
		_monitors = monitors;
		_windows = windows;
		_brightness = new BrightnessWidget(monitors, logger);
		_logger = logger.ForContext<PluginIntegration>();
		_catalogs = catalogs;
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

	public bool SupportsCatalog => true;

	public bool SupportsPush => false;

	public bool SupportsSearch => true;

	public string CatalogName => "Monitors";

	public int? CatalogEntryCount => null;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_messages = context.Messages;
		lock (_loopGate)
		{
			if (_disposed)
			{
				return;
			}

			_watcher.Reset();
			_loopCts?.Cancel();
			_loopCts?.Dispose();
			_loopCts = new CancellationTokenSource();
			_loopTask = RunCatalogWatchAsync(_loopCts.Token);
		}

		await RegisterMessagingAsync(context.Messages).ConfigureAwait(false);
	}

	private async Task RegisterMessagingAsync(IMessageChannel messages)
	{
		try
		{
			await messages.HandleRequestsAsync(
				ScreenMessageTopics.StateGet,
				(_, _) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(BuildStateSnapshot(), ScreenMessageJson.Options)),
				default).ConfigureAwait(false);
		}
		catch (MessageChannelException ex)
		{
			_logger.Debug(ex, "Message channel unavailable, skipping messaging registration.");
		}
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			if (_monitors.GetMonitors().Count > 0)
			{
				return Task.FromResult<IReadOnlyList<IntegrationIssue>>([]);
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Monitor list read failed.");
			return Task.FromResult<IReadOnlyList<IntegrationIssue>>([]);
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(
		[
			new IntegrationIssue
			{
				Id = "no-monitors",
				Title = Strings.Issues.NoMonitors.Title(),
				Description = Strings.Issues.NoMonitors.Description(),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = Strings.Issues.NoMonitors.Action(),
			},
		]);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!string.Equals(issueId, "no-monitors", StringComparison.Ordinal))
		{
			return Task.FromResult(IssueResolution.Failed(Strings.Issues.Unknown.Text()));
		}

		try
		{
			if (_monitors.GetMonitors().Count > 0)
			{
				return Task.FromResult(IssueResolution.Ok(
					Strings.Issues.NoMonitors.RetryOk(),
					IssueResolutionFollowUp.None));
			}

			return Task.FromResult(IssueResolution.Failed(Strings.Issues.NoMonitors.RetryFailed()));
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Monitor rescan failed.");
			return Task.FromResult(IssueResolution.Failed(Strings.Issues.NoMonitors.RetryFailed()));
		}
	}

	public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken) =>
		_brightness.InitializeAsync(context, cancellationToken);

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => _brightness.GetWidgetTypes();

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _brightness.Surfaces;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken) =>
		_brightness.CreateSessionAsync(request, cancellationToken);

	public async Task ShutdownAsync()
	{
		try
		{
			_monitors.HideOverlays();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Overlay hide failed.");
		}

		Task? loop;
		lock (_loopGate)
		{
			_loopCts?.Cancel();
			loop = _loopTask;
			_loopTask = null;
		}

		if (loop != null)
		{
			await Task.WhenAny(loop, Task.Delay(ShutdownDrainTimeout, CancellationToken.None)).ConfigureAwait(false);
		}
	}

	public void Dispose()
	{
		Task? loop;
		lock (_loopGate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_loopCts?.Cancel();
			loop = _loopTask;
			_loopTask = null;
			_loopCts?.Dispose();
		}

		if (loop != null)
		{
			try
			{
				if (!loop.Wait(ShutdownDrainTimeout))
				{
					_logger.Debug("Watch loop drain timed out; the cancelled loop ends on its own.");
				}
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Watch loop drain failed.");
			}
		}
	}

	private async Task RunCatalogWatchAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(CatalogWatchInterval);
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await timer.WaitForNextTickAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}

			try
			{
				if (_watcher.CheckForChanges(_monitors.GetMonitors()))
				{
					_catalogs?.CatalogChanged("variables", reason: "monitors-changed");
					_ = PublishMessageAsync(ScreenMessageTopics.MonitorsChanged, BuildMonitorsChangedMessage());
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Monitor catalog watch failed.");
			}
		}
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		try
		{
			if (DisplayVariables.TryParseMonitorBrightnessId(localId, out var catalogIndex))
			{
				return ValueTask.FromResult(ReadMonitorBrightness(catalogIndex));
			}

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
			if (DisplayVariables.TryParseMonitorBrightnessId(localId, out var catalogIndex))
			{
				var brightness = DisplayParameters.ReadNumberValue(value);
				if (brightness is null || brightness < 0 || brightness > 100)
				{
					return ValueTask.FromResult(VariableWriteResult.InvalidValue(Strings.Variables.MonitorBrightness.DisplayName(catalogIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))));
				}

				return ValueTask.FromResult(_monitors.TrySetBrightness(catalogIndex, (int)brightness)
					? VariableWriteResult.Applied()
					: VariableWriteResult.Unavailable(Strings.Errors.MonitorNotAvailable()));
			}

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

	// The monitor set is watched by the catalog loop, which calls CatalogChanged when monitors
	// come or go (safe since beta.4 keeps the localization catalog across re-describes).
	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query, CancellationToken cancellationToken = default)
	{
		var items = new List<VariableDefinition>();
		try
		{
			foreach (var monitor in _monitors.GetMonitors())
			{
				if (!string.IsNullOrWhiteSpace(query.Search)
					&& !$"monitor {monitor.Index}".Contains(query.Search, StringComparison.OrdinalIgnoreCase)
					&& !monitor.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				items.Add(DisplayVariables.MonitorBrightness(monitor.Index));
				if (query.PageSize > 0 && items.Count >= query.PageSize)
				{
					break;
				}
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Monitor discovery failed.");
		}

		return ValueTask.FromResult(new VariableCatalogPage { Items = items });
	}

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default)
	{
		try
		{
			if (DisplayVariables.TryParseMonitorBrightnessId(localId, out var index))
			{
				foreach (var monitor in _monitors.GetMonitors())
				{
					if (monitor.Index == index)
					{
						return ValueTask.FromResult<VariableDefinition?>(DisplayVariables.MonitorBrightness(index));
					}
				}
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Monitor resolve failed.");
		}

		return ValueTask.FromResult<VariableDefinition?>(null);
	}

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

	private VariableReading ReadMonitorBrightness(int index)
	{
		try
		{
			foreach (var monitor in _monitors.GetMonitors())
			{
				if (monitor.Index == index)
				{
					return monitor.SupportsBrightness
						? VariableReading.Of((double)monitor.BrightnessPercent, 0, 100, 1)
						: VariableReading.Unavailable;
				}
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Monitor brightness read failed.");
		}

		return VariableReading.Unavailable;
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

		return VariableReading.Of(InputToken(input.Value));
	}

	private static string InputToken(int vcpValue) => vcpValue switch
	{
		0x11 => "hdmi1",
		0x12 => "hdmi2",
		0x0F => "dp1",
		0x10 => "dp2",
		0x03 => "dvi",
		_ => "unknown",
	};

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
			await channel.PublishAsync(topic, JsonSerializer.SerializeToElement(payload, ScreenMessageJson.Options), CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Message publish on {Topic} failed.", topic);
		}
	}

	private MonitorsChangedMessage BuildMonitorsChangedMessage()
	{
		try
		{
			var monitors = _monitors.GetMonitors();
			return new MonitorsChangedMessage(monitors.Count, monitors.Select(m => m.Name).ToArray());
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Monitor list read failed.");
			return new MonitorsChangedMessage(0, []);
		}
	}

	private ScreenStateMessage BuildStateSnapshot()
	{
		try
		{
			var monitors = _monitors.GetMonitors();
			var primary = PrimaryMonitor(monitors);
			var focused = _windows.GetForeground();
			var input = primary is null ? null : _monitors.TryGetInput(primary.Index);
			return new ScreenStateMessage(
				monitors.Count,
				primary is not null && primary.SupportsBrightness ? primary.BrightnessPercent : null,
				input is null ? null : InputToken(input.Value),
				string.IsNullOrWhiteSpace(focused?.Title) ? null : focused.Title,
				string.IsNullOrWhiteSpace(focused?.ProcessName) ? null : focused.ProcessName);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Screen state read failed.");
			return new ScreenStateMessage(0, null, null, null, null);
		}
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

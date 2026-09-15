using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using ScreenControl.Monitors;

namespace ScreenControl.Widgets;

public sealed record BrightnessContent(double Level, string MonitorName, bool HasMonitor)
{
	public static BrightnessContent Empty { get; } = new(
		0, string.Empty, false);
}

public sealed record BrightnessActions(
	Func<UiEventData, UiEventOutcome> Adjust,
	Func<UiEventData, UiEventOutcome> Change);

internal static class BrightnessView
{
	// Bump when the layout changes. Node ids compose from the root key, so a new
	// generation makes old patches unmatchable and forces the host to resync a
	// clean tree instead of patching new values into a stale structure.
	internal const string TreeGeneration = "1";

	public static UiElement Build(UiState<BrightnessContent> content, BrightnessActions? actions = null)
	{
		var slider = new UiSlider
		{
			Key = "level",
			Level = UiValue.From(() => content.Value.Level),
			Step = 0.05,
			Interaction = UiComponentSliderInteractions.Relative,
			Thickness = 0.06,
			MainSize = UiSize.Capped(0.12, 34),
		};
		return new UiStack
		{
			Key = "brightness-g" + TreeGeneration,
			Padding = 0.06,
			Gap = 0.03,
		Children =
		[
			new UiWhen
			{
				Key = "caption-when",
				Condition = () => content.Value.HasMonitor,
				Content = () => new UiTextRun
				{
					Key = "caption",
					Text = UiText.From(() => $"{content.Value.MonitorName} · {(int)Math.Round(content.Value.Level * 100, MidpointRounding.AwayFromZero)}"),
					Size = UiSize.Capped(0.075, 11),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
			new UiWhen
			{
				Key = "empty-when",
				Condition = () => !content.Value.HasMonitor,
				Content = () => new UiTextRun
				{
					Key = "empty",
					Text = UiText.FromLocalized(() => Strings.Widget.Brightness.NoMonitor()),
					Size = UiSize.Capped(0.075, 11),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
				actions is null ? slider : slider with
				{
					Events =
					[
						UiEventHandler.On(UiComponentEvents.Adjust, data => actions.Adjust(data)),
						UiEventHandler.On(UiComponentEvents.Change, data => actions.Change(data)),
					],
					Fallback = new UiRangeBar
					{
						Key = "level-fallback",
						Start = UiValue.Of(0.0),
						End = UiValue.From(() => content.Value.Level),
						StartColor = UiValue.Of("#38BDF8"),
						EndColor = UiValue.Of("#38BDF8"),
						Thickness = 0.05,
					},
				},
			],
		};
	}
}

public static class BrightnessPreviews
{
	[UiPreview("Brightness", View = "Brightness", Profile = UiPreviewProfiles.Widget)]
	public static UiElement WithLevel() => BrightnessView.Build(
		new UiState<BrightnessContent>(new BrightnessContent(0.72, "Display 1", true)));

	[UiPreview("No monitor", View = "Brightness", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NoMonitor() => BrightnessView.Build(
		new UiState<BrightnessContent>(BrightnessContent.Empty));

	// Test and tooling support: builds the live tree against caller-owned
	// state so patches can be observed without a running session.
	public static UiElement FromState(UiState<BrightnessContent> state) => BrightnessView.Build(state);
}

public sealed class BrightnessWidget : IWidgetTypeProvider, IUiProvider
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

	private readonly IMonitorService _monitors;
	private readonly Serilog.ILogger _logger;
	private static readonly object s_registrationGate = new();
	private static readonly WidgetTypeDescriptor s_descriptor = new(
		"monitor-brightness",
		Strings.Widget.Brightness.Name(),
		Strings.Widget.Brightness.Description(),
		"""{}""",
		"""{"type":"object","properties":{}}""",
		true,
		new Dictionary<string, string>());
	private static string? s_widgetTypeId;
	private static bool s_registered;

	public BrightnessWidget(IMonitorService monitors, Serilog.ILogger logger)
	{
		_monitors = monitors;
		_logger = logger.ForContext<BrightnessWidget>();
	}

	public string ProviderName => "Screen Control";

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
	];

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken)
	{
		lock (s_registrationGate)
		{
			if (s_registered)
			{
				return;
			}
		}

		const int maxAttempts = 5;
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				WidgetTypeRegistration registration = await context.RegisterWidgetTypeAsync(s_descriptor, cancellationToken);
				lock (s_registrationGate)
				{
					s_widgetTypeId = registration.WidgetTypeId;
					s_registered = true;
				}

				return;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (attempt < maxAttempts)
			{
				_logger.Debug(ex, "Widget registration attempt {Attempt} failed, retrying.", attempt);
				await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
			}
		}
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [s_descriptor];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var surface = request.Surface;
		if (surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview)
		{
			if (s_widgetTypeId is not null
				&& ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) is string widgetType
				&& widgetType != s_widgetTypeId)
			{
				return Task.FromResult<IUiSession?>(null);
			}

			if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample) == true)
			{
				return Task.FromResult<IUiSession?>(new BrightnessSession(
					surface,
					new UiState<BrightnessContent>(new BrightnessContent(0.72, "Display 1", true))));
			}

			return Task.FromResult<IUiSession?>(new BrightnessSession(surface, new UiState<BrightnessContent>(ReadContent()), this, _monitors, _logger));
		}

		return Task.FromResult<IUiSession?>(null);
	}

	public BrightnessContent ReadContent()
	{
		try
		{
			var monitor = PrimaryMonitor(_monitors);
			if (monitor is null)
			{
				return new BrightnessContent(0, string.Empty, false);
			}

			return new BrightnessContent(
				Math.Clamp(monitor.BrightnessPercent / 100.0, 0, 1),
				monitor.Name,
				true);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Brightness read failed.");
			return new BrightnessContent(0, string.Empty, false);
		}
	}

	private static MonitorInfo? PrimaryMonitor(IMonitorService monitors)
	{
		try
		{
			var all = monitors.GetMonitors();
			return all.FirstOrDefault(m => m.IsPrimary && m.SupportsBrightness)
				?? all.FirstOrDefault(m => m.SupportsBrightness);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string? ReadString(UiSurface surface, string key)
	{
		var element = ReadElement(surface, key);
		return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
	}

	private static JsonElement ReadElement(UiSurface surface, string key) =>
		surface.Attributes.TryGetValue(key, out var element) ? element : default;

	private static bool? ReadBool(UiSurface surface, string key)
	{
		var element = ReadElement(surface, key);
		return element.ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetBoolean() : null;
	}

	private sealed class BrightnessSession : IUiSession, IDisposable, IAsyncDisposable
	{
		private readonly UiView _view;
		private readonly UiState<BrightnessContent>? _content;
		private readonly BrightnessWidget? _owner;
		private readonly IMonitorService? _monitors;
		private readonly Serilog.ILogger? _logger;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task? _loop;
		private bool _disposed;

		public BrightnessSession(UiSurface surface, UiState<BrightnessContent> content)
		{
			_content = content;
			_view = new UiView(surface, BrightnessView.Build(content));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
		}

		public BrightnessSession(
			UiSurface surface,
			UiState<BrightnessContent> content,
			BrightnessWidget owner,
			IMonitorService monitors,
			Serilog.ILogger logger)
		{
			_content = content;
			_owner = owner;
			_monitors = monitors;
			_logger = logger.ForContext<BrightnessSession>();
			_view = new UiView(surface, BrightnessView.Build(content, new BrightnessActions(Adjust, Change)));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
			_loop = RefreshLoopAsync(_cts.Token);
		}

		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

		public UiTree BuildTree() => _view.Tree;

		public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

		public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_cts.Cancel();
			_cts.Dispose();
			_view.Changed -= OnChanged;
			_view.HandlerFaulted -= OnHandlerFaulted;
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}

		private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);

		private void OnHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("handler-fault", e.Exception));

		// Rejection reasons travel as plain strings with no localization reference,
		// so these stay English literals rather than keys that would render raw.
		private UiEventOutcome Adjust(UiEventData data)
		{
			if (_content is null || !data.TryGetDouble(out var level))
			{
				return UiEventOutcome.Rejected("Unknown level.");
			}

			_content.Set(_content.Peek() with { Level = Math.Clamp(level, 0, 1) });
			return UiEventOutcome.Accepted;
		}

		private UiEventOutcome Change(UiEventData data)
		{
			if (_content is null || _monitors is null || !data.TryGetDouble(out var level))
			{
				return UiEventOutcome.Rejected("Unknown level.");
			}

			var clamped = Math.Clamp(level, 0, 1);
			try
			{
				var monitor = PrimaryMonitor(_monitors);
				if (monitor is null || !_monitors.TrySetBrightness(monitor.Index, (int)Math.Round(clamped * 100)))
				{
					return UiEventOutcome.Rejected("No brightness-capable monitor found.");
				}
			}
			catch (Exception ex)
			{
				_logger?.Debug(ex, "Brightness apply failed.");
				return UiEventOutcome.Rejected("Brightness apply failed.");
			}

			_content.Set(_content.Peek() with { Level = clamped });
			return UiEventOutcome.Accepted;
		}

		private async Task RefreshLoopAsync(CancellationToken cancellationToken)
		{
			using var timer = new PeriodicTimer(PollInterval);
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
					if (_owner is not null && _content is not null)
					{
						var next = _owner.ReadContent();
						if (!next.Equals(_content.Value))
						{
							_content.Set(next);
						}
					}
				}
				catch (Exception ex)
				{
					_logger?.Debug(ex, "Brightness refresh failed.");
				}
			}
		}
	}
}

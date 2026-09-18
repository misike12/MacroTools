using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using ScreenControl.Monitors;

namespace ScreenControl.Widgets;

public sealed record BrightnessOptions(int Monitor, bool ShowPresets)
{
	public static BrightnessOptions Default { get; } = new(1, true);

	public static BrightnessOptions FromData(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return Default;
		}

		return new BrightnessOptions(ReadMonitor(data), ReadFlag(data, "showPresets", true));
	}

	private static int ReadMonitor(JsonElement data)
	{
		if (data.TryGetProperty("monitor", out var element))
		{
			// Choice inputs carry the selection as a string; the descriptor
			// default and older data may carry a number. Both spell a monitor.
			if (element.ValueKind == JsonValueKind.String
				&& int.TryParse(element.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
			{
				return Math.Clamp(parsed, 1, 9);
			}

			if (element.ValueKind == JsonValueKind.Number)
			{
				try
				{
					return Math.Clamp((int)element.GetDouble(), 1, 9);
				}
				catch (Exception)
				{
				}
			}
		}

		return Default.Monitor;
	}

	private static bool ReadFlag(JsonElement data, string name, bool fallback) =>
		data.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
			? element.GetBoolean()
			: fallback;
}

public sealed record BrightnessContent(double Level, MacroDeck.Localization.LocalizedText MonitorName, bool HasMonitor)
{
	public static BrightnessContent Empty { get; } = new(
		0, Strings.Widget.Brightness.NoMonitor(), false);
}

public sealed record BrightnessActions(
	Func<UiEventData, UiEventOutcome> Adjust,
	Func<UiEventData, UiEventOutcome> Change,
	Func<int, UiEventOutcome> Preset);

internal static class BrightnessView
{
	// Bump when the layout changes. Node ids compose from the root key, so a new
	// generation makes old patches unmatchable and forces the host to resync a
	// clean tree instead of patching new values into a stale structure.
	internal const string TreeGeneration = "2";

	public static UiElement Build(UiState<BrightnessContent> content, BrightnessOptions options, BrightnessActions? actions = null)
	{
		var body = new List<UiElement>
		{
			new UiWhen
			{
				Key = "caption-when",
				Condition = () => content.Value.HasMonitor,
				Content = () => new UiStack
				{
					Key = "caption",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Baseline,
					Gap = 0.015,
					Children =
					[
						new UiTextRun
						{
							Key = "caption-name",
							Text = UiText.Optional(() => content.Value.MonitorName),
							Size = UiSize.Capped(0.075, 11),
							Role = UiComponentTextRoles.Muted,
							Align = UiComponentAlignments.Center,
						},
						new UiTextRun
						{
							Key = "caption-level",
							Text = UiText.From(() => ((int)Math.Round(content.Value.Level * 100, MidpointRounding.AwayFromZero)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
							Size = UiSize.Capped(0.075, 11),
							Weight = UiComponentTextWeights.SemiBold,
							Align = UiComponentAlignments.Center,
						},
					],
				},
			},
			new UiWhen
			{
				Key = "empty-when",
				Condition = () => !content.Value.HasMonitor,
				Content = () => new UiTextRun
				{
					Key = "empty",
					Text = Strings.Widget.Brightness.NoMonitor(),
					Size = UiSize.Capped(0.075, 11),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
		};

		body.Add(new UiWhen
		{
			Key = "slider-when",
			Condition = () => content.Value.HasMonitor,
			Content = () => Slider(content, actions),
		});

		if (options.ShowPresets)
		{
			body.Add(new UiWhen
			{
				Key = "presets-when",
				Condition = () => content.Value.HasMonitor,
				Content = () => Presets(actions),
			});
		}

		return new UiStack
		{
			Key = "brightness-g" + TreeGeneration,
			Padding = 0.06,
			Gap = 0.03,
			Children = body,
		};
	}

	private static UiSlider Slider(UiState<BrightnessContent> content, BrightnessActions? actions)
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
		return actions is null ? slider : slider with
		{
			Events =
			[
				UiEventHandler.On(UiComponentEvents.Adjust, data => actions.Adjust(data)),
				UiEventHandler.On(UiComponentEvents.Change, data => actions.Change(data)),
				// Double tap jumps back to full brightness, the slider's home level.
				// The slider is relative, so taps move nothing and double-press arrives
				// on its own with no tap latency held back.
				UiEventHandler.On(UiComponentEvents.DoublePress, _ => actions.Preset(100)),
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
		};
	}

	private static UiStack Presets(BrightnessActions? actions)
	{
		return new UiStack
		{
			Key = "presets",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.Center,
			Gap = 0.02,
			Children =
			[
				PresetButton("25", 25, actions),
				PresetButton("50", 50, actions),
				PresetButton("75", 75, actions),
				PresetButton("100", 100, actions),
			],
		};
	}

	private static UiButton PresetButton(string key, int percent, BrightnessActions? actions)
	{
		var button = new UiButton
		{
			Key = "preset-" + key,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Padding = 0.015,
			Background = UiValue.Of("#22252C"),
			Children =
			[
				new UiTextRun
				{
					Key = "preset-" + key + "-label",
					Text = UiText.Of(percent.ToString(System.Globalization.CultureInfo.InvariantCulture)),
					Size = UiSize.Capped(0.05, 11),
					Weight = UiComponentTextWeights.SemiBold,
					Align = UiComponentAlignments.Center,
				},
			],
		};
		return actions is null
			? button
			: button with { Events = [UiEventHandler.On(UiComponentEvents.Press, _ => actions.Preset(percent))] };
	}
}

public static class BrightnessPreviews
{
	[UiPreview("Brightness", View = "Brightness", Profile = UiPreviewProfiles.Widget)]
	public static UiElement WithLevel() => BrightnessView.Build(
		new UiState<BrightnessContent>(new BrightnessContent(0.72, Strings.Widget.Brightness.Display(1), true)),
		BrightnessOptions.Default);

	[UiPreview("No monitor", View = "Brightness", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NoMonitor() => BrightnessView.Build(
		new UiState<BrightnessContent>(BrightnessContent.Empty),
		BrightnessOptions.Default);

	// Test and tooling support: builds the live tree against caller-owned
	// state so patches can be observed without a running session.
	public static UiElement FromState(UiState<BrightnessContent> state, BrightnessOptions options) =>
		BrightnessView.Build(state, options);
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
		"""{"monitor":"1","showPresets":true}""",
		"""{"type":"object","properties":{"monitor":{"type":"string"},"showPresets":{"type":"boolean"}}}""",
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
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
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

			var options = BrightnessOptions.FromData(ReadElement(surface, UiWidgetSurfaceAttributes.Data));
			if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample) == true)
			{
				return Task.FromResult<IUiSession?>(new BrightnessSession(
					surface,
					new UiState<BrightnessContent>(new BrightnessContent(0.72, Strings.Widget.Brightness.Display(1), true)),
					options));
			}

			return Task.FromResult<IUiSession?>(new BrightnessSession(surface, new UiState<BrightnessContent>(ReadContent(options.Monitor)), options, this, _monitors, _logger));
		}

		if (surface.Kind == UiSurfaceKinds.Config
			&& ReadString(surface, UiConfigSurfaceAttributes.EntryPoint) == UiConfigEntryPoints.WidgetConfig)
		{
			return Task.FromResult<IUiSession?>(BuildConfigSession(surface, BrightnessOptions.FromData(ReadElement(surface, UiConfigSurfaceAttributes.WidgetData))));
		}

		return Task.FromResult<IUiSession?>(null);
	}

	public BrightnessContent ReadContent(int index)
	{
		try
		{
			var monitor = TargetMonitor(_monitors, index);
			if (monitor is null)
			{
				return new BrightnessContent(0, string.Empty, false);
			}

			return new BrightnessContent(
				Math.Clamp(monitor.BrightnessPercent / 100.0, 0, 1),
				Strings.Widget.Brightness.Display(monitor.Index),
				true);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Brightness read failed.");
			return new BrightnessContent(0, string.Empty, false);
		}
	}

	private static MonitorInfo? TargetMonitor(IMonitorService monitors, int index)
	{
		try
		{
			return monitors.GetMonitors().FirstOrDefault(m => m.Index == index && m.SupportsBrightness);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private BrightnessSession BuildConfigSession(UiSurface surface, BrightnessOptions options)
	{
		var monitor = new UiState<string>(options.Monitor.ToString(System.Globalization.CultureInfo.InvariantCulture));
		var showPresets = new UiState<bool>(options.ShowPresets);
		var choices = MonitorChoices();
		var view = new UiView(surface, new UiWidgetConfiguration
		{
			Key = "config",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "monitor-heading", Text = Strings.Widget.Config.MonitorHeading() },
					new UiChoiceInput
					{
						Key = "monitor",
						Label = Strings.Widget.Config.Monitor(),
						Description = Strings.Widget.Config.MonitorDescription(),
						Segmented = UiValue.Of(choices.Count <= 4),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(choices),
						Binding = Bind.To(monitor),
					},
					new UiBooleanInput
					{
						Key = "showPresets",
						Label = Strings.Widget.Config.ShowPresets(),
						Description = Strings.Widget.Config.ShowPresetsDescription(),
						Binding = Bind.To(showPresets),
					},
				],
			},
		});
		return new BrightnessSession(view);
	}

	private List<UiOption> MonitorChoices()
	{
		try
		{
			var choices = _monitors.GetMonitors()
				.Where(m => m.SupportsBrightness)
				.Select(m => UiOption.Of(
					m.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
					Strings.Widget.Brightness.Display(m.Index)))
				.ToList();
			if (choices.Count > 0)
			{
				return choices;
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Monitor choices failed.");
		}

		return [UiOption.Of("1", Strings.Widget.Brightness.Display(1))];
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
		private readonly int _monitor;
		private readonly IMonitorService? _monitors;
		private readonly Serilog.ILogger? _logger;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task? _loop;
		private bool _disposed;

		public BrightnessSession(UiSurface surface, UiState<BrightnessContent> content, BrightnessOptions options)
		{
			_content = content;
			_monitor = options.Monitor;
			_view = new UiView(surface, BrightnessView.Build(content, options));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
		}

		public BrightnessSession(
			UiSurface surface,
			UiState<BrightnessContent> content,
			BrightnessOptions options,
			BrightnessWidget owner,
			IMonitorService monitors,
			Serilog.ILogger logger)
		{
			_content = content;
			_monitor = options.Monitor;
			_owner = owner;
			_monitors = monitors;
			_logger = logger.ForContext<BrightnessSession>();
			_view = new UiView(surface, BrightnessView.Build(content, options, new BrightnessActions(Adjust, Change, Preset)));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
			_loop = RefreshLoopAsync(_cts.Token);
		}

		public BrightnessSession(UiView view)
		{
			_view = view;
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
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
			// UiView is disposable since SDK beta.11: disposing detaches it from the
			// state it reads, so a closed session no longer leaks on every write.
			_view.Dispose();
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

			return ApplyPercent((int)Math.Round(Math.Clamp(level, 0, 1) * 100, MidpointRounding.AwayFromZero));
		}

		private UiEventOutcome Preset(int percent) => ApplyPercent(percent);

		private UiEventOutcome ApplyPercent(int percent)
		{
			if (_content is null || _monitors is null)
			{
				return UiEventOutcome.Rejected("Unknown level.");
			}

			try
			{
				var monitor = TargetMonitor(_monitors, _monitor);
				if (monitor is null || !_monitors.TrySetBrightness(monitor.Index, Math.Clamp(percent, 0, 100)))
				{
					return UiEventOutcome.Rejected("No brightness-capable monitor found.");
				}
			}
			catch (Exception ex)
			{
				_logger?.Debug(ex, "Brightness apply failed.");
				return UiEventOutcome.Rejected("Brightness apply failed.");
			}

			_content.Set(_content.Peek() with { Level = Math.Clamp(percent, 0, 100) / 100.0 });
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
						var next = _owner.ReadContent(_monitor);
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

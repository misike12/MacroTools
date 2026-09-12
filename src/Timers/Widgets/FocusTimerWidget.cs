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
using MacroDeck.Ui.Model.References;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using Timers.Timing;

namespace Timers.Widgets;

public sealed record FocusTimerOptions(
	string Mode,
	double CountdownMinutes,
	double WorkMinutes,
	double ShortBreakMinutes,
	double LongBreakMinutes,
	double Rounds,
	bool AutoAdvance,
	bool ShowLabel,
	bool ShowProgress,
	bool ShowControls,
	bool Compact,
	string AccentColor)
{
	public static FocusTimerOptions Default { get; } = new(
		"pomodoro", 5, 25, 5, 15, 4, true, true, true, true, false, string.Empty);

	public static FocusTimerOptions FromData(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return Default;
		}

		return new FocusTimerOptions(
			Mode: ReadMode(data),
			CountdownMinutes: ReadNumber(data, "countdownMinutes", 5, 1, 180),
			WorkMinutes: ReadNumber(data, "workMinutes", 25, 1, 180),
			ShortBreakMinutes: ReadNumber(data, "shortBreakMinutes", 5, 1, 60),
			LongBreakMinutes: ReadNumber(data, "longBreakMinutes", 15, 1, 90),
			Rounds: ReadNumber(data, "rounds", 4, 1, 12),
			AutoAdvance: ReadFlag(data, "autoAdvance", true),
			ShowLabel: ReadFlag(data, "showLabel", true),
			ShowProgress: ReadFlag(data, "showProgress", true),
			ShowControls: ReadFlag(data, "showControls", true),
			Compact: ReadFlag(data, "compactMode", false),
			AccentColor: ReadColor(data));
	}

	public PomodoroSettings ToPomodoroSettings() => new(
		FocusMinutes: WorkMinutes,
		ShortBreakMinutes: ShortBreakMinutes,
		LongBreakMinutes: LongBreakMinutes,
		Rounds: (int)Rounds,
		AutoAdvance: AutoAdvance);

	private static string ReadMode(JsonElement data)
	{
		if (data.TryGetProperty("mode", out var element) && element.ValueKind == JsonValueKind.String)
		{
			var mode = element.GetString()?.ToLowerInvariant();
			if (mode is "countdown" or "stopwatch" or "pomodoro")
			{
				return mode;
			}
		}

		return Default.Mode;
	}

	private static double ReadNumber(JsonElement data, string name, double fallback, double min, double max)
	{
		if (data.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number)
		{
			try
			{
				return Math.Clamp(element.GetDouble(), min, max);
			}
			catch (Exception)
			{
			}
		}

		return fallback;
	}

	private static bool ReadFlag(JsonElement data, string name, bool fallback) =>
		data.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
			? element.GetBoolean()
			: fallback;

	private static string ReadColor(JsonElement data)
	{
		if (data.TryGetProperty("accentColor", out var element) && element.ValueKind == JsonValueKind.String)
		{
			var color = element.GetString()?.Trim();
			if (!string.IsNullOrEmpty(color))
			{
				return color;
			}
		}

		return string.Empty;
	}
}

public sealed record FocusTimerContent(
	string Mode,
	string Caption,
	string Hero,
	string Subtitle,
	string Dots,
	int Round,
	UiProgressReference Progress,
	bool HasTotal,
	bool Running,
	bool HasSession,
	FocusTimerOptions Options,
	string Accent)
{
	public static FocusTimerContent Empty { get; } = new(
		"pomodoro", string.Empty, "25:00", string.Empty, string.Empty,
		0, new UiProgressReference { PositionMs = 0, Anchor = DateTimeOffset.UtcNow },
		false, false, false, FocusTimerOptions.Default, string.Empty);

	public static string FormatRemaining(TimeSpan remaining)
	{
		if (remaining < TimeSpan.Zero)
		{
			remaining = TimeSpan.Zero;
		}

		return remaining.TotalHours >= 1
			? $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
			: $"{remaining.Minutes}:{remaining.Seconds:D2}";
	}
}

internal static class FocusTimerView
{
	public static UiElement Build(
		UiState<FocusTimerContent> content,
		Func<string, CancellationToken, Task>? command)
	{
		var options = content.Peek().Options;
		var body = new List<UiElement>();

		if (options.ShowLabel)
		{
			body.Add(new UiTextRun
			{
				Key = "caption",
				Text = UiText.From(() => content.Value.Caption),
				Size = options.Compact ? UiSize.Capped(0.09, 10) : UiSize.Capped(0.1, 12),
				Weight = UiComponentTextWeights.Medium,
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			});
		}

		body.Add(new UiTextRun
		{
			Key = "hero",
			Text = UiText.From(() => content.Value.Hero),
			Size = options.Compact ? UiSize.Capped(0.16, 22) : UiSize.Capped(0.2, 30),
			Weight = UiComponentTextWeights.SemiBold,
			Align = UiComponentAlignments.Center,
		});

		if (!options.Compact && !string.IsNullOrEmpty(content.Peek().Dots))
		{
			body.Add(new UiTextRun
			{
				Key = "rounds",
				Text = UiText.From(() => content.Value.Dots),
				Size = UiSize.Capped(0.09, 11),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			});
		}

		if (options.ShowProgress && content.Peek().HasTotal)
		{
			var anchor = content.Peek().Progress;
			var span = anchor.DurationMs is { } total && total > 0
				? Math.Clamp((double)anchor.PositionMs / total, 0, 1)
				: 0;
			body.Add(new UiProgressBar
			{
				Key = "progress",
				Value = UiValue.From(() => content.Value.Progress),
				StartColor = UiValue.Optional(() => string.IsNullOrEmpty(content.Value.Accent)
					? UiValue.None<string>()
					: UiValue.Of(content.Value.Accent)),
				EndColor = UiValue.Optional(() => string.IsNullOrEmpty(content.Value.Accent)
					? UiValue.None<string>()
					: UiValue.Of(content.Value.Accent)),
				Thickness = 0.04,
				Fallback = new UiRangeBar
				{
					Key = "progress-fallback",
					Start = UiValue.Of(0.0),
					End = UiValue.Of(span),
					Thickness = 0.04,
				},
			});
		}

		if (!options.Compact && !string.IsNullOrEmpty(content.Peek().Subtitle))
		{
			body.Add(new UiTextRun
			{
				Key = "subtitle",
				Text = UiText.From(() => content.Value.Subtitle),
				Size = UiSize.Capped(0.08, 10),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			});
		}

		if (options.ShowControls)
		{
			body.Add(ControlRow(content, command));
		}

		return new UiStack
		{
			Key = "focus-timer",
			Padding = 0.07,
			Gap = 0.045,
			Children = body,
		};
	}

	private static UiStack ControlRow(
		UiState<FocusTimerContent> content,
		Func<string, CancellationToken, Task>? command)
	{
		var snapshot = content.Peek();
		var children = new List<UiElement>
		{
			ControlButton("reset", Strings.Widget.Symbols.Reset(), "reset", side: true, command),
		};

		if (snapshot is { Mode: "pomodoro", HasSession: true })
		{
			children.Add(ControlButton("skip", Strings.Widget.Symbols.Skip(), "skip", side: true, command));
		}

		children.Add(ControlButton("primary", PrimaryGlyph(snapshot), "primary", side: false, command));

		return new UiStack
		{
			Key = "controls",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.Center,
			Gap = 0.05,
			Children = children,
		};
	}

	private static MacroDeck.Localization.LocalizedString PrimaryGlyph(FocusTimerContent snapshot) =>
		snapshot.Running ? Strings.Widget.Symbols.Pause() : Strings.Widget.Symbols.Play();

	private static UiButton ControlButton(
		string key,
		MacroDeck.Localization.LocalizedString label,
		string command,
		bool side,
		Func<string, CancellationToken, Task>? handler)
	{
		var button = new UiButton
		{
			Key = key,
			Justify = UiComponentJustify.Center,
			Fill = !side,
			MainSize = side ? UiSize.Capped(0.2, 48) : default,
			Corner = UiComponentButtonCorners.Tile,
			Children =
			[
				new UiTextRun
				{
					Key = key + "-label",
					Text = UiText.FromLocalized(() => label),
					Size = 0.14,
					Align = UiComponentAlignments.Center,
				},
			],
		};

		if (handler is not null)
		{
			var press = handler;
			var action = command;
			button = button with { Events = [UiEventHandler.OnAsync(UiComponentEvents.Press, ct => press(action, ct))] };
		}

		return button;
	}
}

public static class FocusTimerPreviews
{
	[UiPreview("Pomodoro focus", View = "FocusTimer", Profile = UiPreviewProfiles.Widget)]
	public static UiElement PomodoroFocus() => FocusTimerView.Build(
		new UiState<FocusTimerContent>(new FocusTimerContent(
			"pomodoro", "Focus", "24:59", "Round 1 of 4", "◉ ○ ○ ○", 1,
			new UiProgressReference { PositionMs = 1000, Anchor = DateTimeOffset.UtcNow, DurationMs = 1500000, Rate = 1 },
			true, true, true, FocusTimerOptions.Default, "#F59E0B")),
		null);

	[UiPreview("Short break", View = "FocusTimer", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ShortBreak() => FocusTimerView.Build(
		new UiState<FocusTimerContent>(new FocusTimerContent(
			"pomodoro", "Short break", "4:12", "Round 1 of 4", "◉ ○ ○ ○", 1,
			new UiProgressReference { PositionMs = 48000, Anchor = DateTimeOffset.UtcNow, DurationMs = 300000, Rate = 0 },
			true, false, true, FocusTimerOptions.Default with { AccentColor = "#22C55E" }, "#22C55E")),
		null);

	[UiPreview("Idle", View = "FocusTimer", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Idle() => FocusTimerView.Build(
		new UiState<FocusTimerContent>(FocusTimerContent.Empty),
		null);
}

public sealed class FocusTimerWidget : IWidgetTypeProvider, IUiProvider
{
	private readonly TimerService _timers;
	private readonly PomodoroService _pomodoro;
	private readonly Serilog.ILogger _logger;
	private static readonly object s_registrationGate = new();
	private static readonly WidgetTypeDescriptor s_descriptor = new(
		"focus-timer",
		Strings.Widget.FocusTimer.Name(),
		Strings.Widget.FocusTimer.Description(),
		"""{"mode":"pomodoro","countdownMinutes":5,"workMinutes":25,"shortBreakMinutes":5,"longBreakMinutes":15,"rounds":4,"autoAdvance":true,"showLabel":true,"showProgress":true,"showControls":true,"compactMode":false,"accentColor":""}""",
		"""{"type":"object","properties":{"mode":{"type":"string"},"countdownMinutes":{"type":"number"},"workMinutes":{"type":"number"},"shortBreakMinutes":{"type":"number"},"longBreakMinutes":{"type":"number"},"rounds":{"type":"number"},"autoAdvance":{"type":"boolean"},"showLabel":{"type":"boolean"},"showProgress":{"type":"boolean"},"showControls":{"type":"boolean"},"compactMode":{"type":"boolean"},"accentColor":{"type":"string"}}}""",
		true,
		new Dictionary<string, string>());
	private static string? s_widgetTypeId;
	private static bool s_registered;

	public FocusTimerWidget(TimerService timers, PomodoroService pomodoro, Serilog.ILogger logger)
	{
		_timers = timers;
		_pomodoro = pomodoro;
		_logger = logger.ForContext<FocusTimerWidget>();
	}

	public string ProviderName => "Timers";

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

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var surface = request.Surface;
		if (surface.Kind is UiSurfaceKinds.Widget or UiSurfaceKinds.Preview)
		{
			if (s_widgetTypeId is not null
				&& ReadString(surface, UiWidgetSurfaceAttributes.WidgetType) is string widgetType
				&& widgetType != s_widgetTypeId)
			{
				return null;
			}

			var options = FocusTimerOptions.FromData(ReadElement(surface, UiWidgetSurfaceAttributes.Data));
			if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample) == true)
			{
				return new FocusTimerSession(
					surface,
					new UiState<FocusTimerContent>(new FocusTimerContent(
						"pomodoro", "Focus", "24:59", "Round 1 of 4", "◉ ○ ○ ○", 1,
						new UiProgressReference { PositionMs = 1000, Anchor = DateTimeOffset.UtcNow, DurationMs = 1500000, Rate = 1 },
						true, true, true, options, "#F59E0B")),
					this,
					_timers,
					_pomodoro,
					_logger,
					live: false);
			}

			return new FocusTimerSession(
				surface,
				new UiState<FocusTimerContent>(BuildContent(options)),
				this,
				_timers,
				_pomodoro,
				_logger,
				live: true);
		}

		if (surface.Kind == UiSurfaceKinds.Config
			&& ReadString(surface, UiConfigSurfaceAttributes.EntryPoint) == UiConfigEntryPoints.WidgetConfig)
		{
			return BuildConfigSession(surface, FocusTimerOptions.FromData(ReadElement(surface, UiConfigSurfaceAttributes.WidgetData)));
		}

		return null;
	}

	internal FocusTimerContent BuildContent(FocusTimerOptions options)
	{
		var accent = string.IsNullOrWhiteSpace(options.AccentColor) ? DefaultAccent(options.Mode) : options.AccentColor.Trim();
		return options.Mode switch
		{
			"countdown" => CountdownContent(options, accent),
			"stopwatch" => StopwatchContent(options, accent),
			_ => PomodoroContent(options, accent),
		};
	}

	private FocusTimerContent CountdownContent(FocusTimerOptions options, string accent)
	{
		var remaining = _timers.CountdownRemaining;
		var total = _timers.CountdownTotalSeconds > 0
			? TimeSpan.FromSeconds(_timers.CountdownTotalSeconds)
			: TimeSpan.Zero;
		var running = _timers.CountdownRunning;
		var hasSession = total > TimeSpan.Zero;
		var elapsed = total - remaining;
		if (elapsed < TimeSpan.Zero)
		{
			elapsed = TimeSpan.Zero;
		}

		var label = _timers.CountdownLabel;
		return new FocusTimerContent(
			"countdown",
			string.IsNullOrWhiteSpace(label) ? Strings.Widget.CountdownFallback().ToString() : label,
			hasSession ? FocusTimerContent.FormatRemaining(remaining) : FocusTimerContent.FormatRemaining(TimeSpan.FromMinutes(options.CountdownMinutes)),
			string.Empty, string.Empty, 0,
			new UiProgressReference
			{
				PositionMs = (long)Math.Clamp(elapsed.TotalMilliseconds, 0, double.MaxValue),
				Anchor = DateTimeOffset.UtcNow,
				DurationMs = total > TimeSpan.Zero ? (long?)total.TotalMilliseconds : null,
				Rate = running ? 1 : 0,
			},
			hasSession, running, hasSession, options, accent);
	}

	private FocusTimerContent StopwatchContent(FocusTimerOptions options, string accent)
	{
		var elapsed = _timers.StopwatchElapsed;
		var running = _timers.StopwatchRunning;
		var hasSession = running || elapsed > TimeSpan.Zero;
		return new FocusTimerContent(
			"stopwatch",
			Strings.Widget.StopwatchCaption().ToString(),
			FocusTimerContent.FormatRemaining(elapsed),
			string.Empty, string.Empty, 0,
			new UiProgressReference
			{
				PositionMs = (long)Math.Clamp(elapsed.TotalMilliseconds, 0, double.MaxValue),
				Anchor = DateTimeOffset.UtcNow,
				DurationMs = null,
				Rate = running ? 1 : 0,
			},
			false, running, hasSession, options, accent);
	}

	private FocusTimerContent PomodoroContent(FocusTimerOptions options, string accent)
	{
		var snapshot = _pomodoro.Snapshot();
		var hasSession = snapshot.Phase != PomodoroPhase.Idle;
		var elapsed = snapshot.Total - snapshot.Remaining;
		if (elapsed < TimeSpan.Zero)
		{
			elapsed = TimeSpan.Zero;
		}

		var totalRounds = Math.Max(1, snapshot.TotalRounds > 0 ? snapshot.TotalRounds : (int)options.Rounds);
		var dots = options.Compact || !hasSession
			? string.Empty
			: string.Join(" ", Enumerable.Range(1, totalRounds).Select(round => round < snapshot.Round ? "●" : round == snapshot.Round ? "◉" : "○"));
		return new FocusTimerContent(
			"pomodoro",
			PhaseCaption(snapshot.Phase),
			hasSession
				? FocusTimerContent.FormatRemaining(snapshot.Remaining)
				: FocusTimerContent.FormatRemaining(TimeSpan.FromMinutes(options.WorkMinutes)),
			hasSession ? RoundSubtitle(snapshot) : string.Empty,
			dots,
			snapshot.Round,
			new UiProgressReference
			{
				PositionMs = (long)Math.Clamp(elapsed.TotalMilliseconds, 0, double.MaxValue),
				Anchor = DateTimeOffset.UtcNow,
				DurationMs = snapshot.Total > TimeSpan.Zero ? (long?)snapshot.Total.TotalMilliseconds : null,
				Rate = snapshot.Running ? 1 : 0,
			},
			hasSession, snapshot.Running, hasSession, options, accent);
	}

	internal static string PhaseCaption(PomodoroPhase phase) => phase switch
	{
		PomodoroPhase.Focus => Strings.Widget.Phases.Focus().ToString(),
		PomodoroPhase.ShortBreak => Strings.Widget.Phases.ShortBreak().ToString(),
		PomodoroPhase.LongBreak => Strings.Widget.Phases.LongBreak().ToString(),
		_ => Strings.Widget.Phases.Idle().ToString(),
	};

	internal static string RoundSubtitle(PomodoroSnapshot snapshot) =>
		Strings.Widget.RoundOf(
			snapshot.Round.ToString(System.Globalization.CultureInfo.InvariantCulture),
			snapshot.TotalRounds.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString();

	private static string DefaultAccent(string mode) => mode switch
	{
		"stopwatch" => "#38BDF8",
		"countdown" => "#A78BFA",
		_ => "#F59E0B",
	};

	private static FocusTimerSession BuildConfigSession(
		MacroDeck.Ui.Model.Surfaces.UiSurface surface,
		FocusTimerOptions options)
	{
		var mode = new UiState<string>(options.Mode);
		var countdownMinutes = new UiState<double>(options.CountdownMinutes);
		var workMinutes = new UiState<double>(options.WorkMinutes);
		var shortBreakMinutes = new UiState<double>(options.ShortBreakMinutes);
		var longBreakMinutes = new UiState<double>(options.LongBreakMinutes);
		var rounds = new UiState<double>(options.Rounds);
		var autoAdvance = new UiState<bool>(options.AutoAdvance);
		var showLabel = new UiState<bool>(options.ShowLabel);
		var showProgress = new UiState<bool>(options.ShowProgress);
		var showControls = new UiState<bool>(options.ShowControls);
		var compactMode = new UiState<bool>(options.Compact);
		var accentColor = new UiState<string>(options.AccentColor);
		var view = new UiView(surface, new UiWidgetConfiguration
		{
			Key = "config",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "timer-heading", Text = Strings.Widget.Config.TimerHeading() },
					new UiChoiceInput
					{
						Key = "mode",
						Label = Strings.Widget.Config.Mode(),
						Description = Strings.Widget.Config.ModeDescription(),
						Segmented = UiValue.Of(true),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of("countdown", Strings.Widget.Config.ModeCountdown()),
							UiOption.Of("stopwatch", Strings.Widget.Config.ModeStopwatch()),
							UiOption.Of("pomodoro", Strings.Widget.Config.ModePomodoro()),
						]),
						Binding = Bind.To(mode),
					},
					new UiNumberInput
					{
						Key = "countdownMinutes",
						Label = Strings.Widget.Config.CountdownMinutes(),
						Description = Strings.Widget.Config.CountdownMinutesDescription(),
						Min = UiValue.Of(1.0),
						Max = UiValue.Of(180.0),
						Step = UiValue.Of(1.0),
						ShowSlider = UiValue.Of(true),
						Binding = Bind.To(countdownMinutes),
					},
					new UiNumberInput
					{
						Key = "workMinutes",
						Label = Strings.Widget.Config.WorkMinutes(),
						Description = Strings.Widget.Config.WorkMinutesDescription(),
						Min = UiValue.Of(1.0),
						Max = UiValue.Of(180.0),
						Step = UiValue.Of(1.0),
						ShowSlider = UiValue.Of(true),
						Binding = Bind.To(workMinutes),
					},
					new UiNumberInput
					{
						Key = "shortBreakMinutes",
						Label = Strings.Widget.Config.ShortBreakMinutes(),
						Description = Strings.Widget.Config.ShortBreakMinutesDescription(),
						Min = UiValue.Of(1.0),
						Max = UiValue.Of(60.0),
						Step = UiValue.Of(1.0),
						ShowSlider = UiValue.Of(true),
						Binding = Bind.To(shortBreakMinutes),
					},
					new UiNumberInput
					{
						Key = "longBreakMinutes",
						Label = Strings.Widget.Config.LongBreakMinutes(),
						Description = Strings.Widget.Config.LongBreakMinutesDescription(),
						Min = UiValue.Of(1.0),
						Max = UiValue.Of(90.0),
						Step = UiValue.Of(1.0),
						ShowSlider = UiValue.Of(true),
						Binding = Bind.To(longBreakMinutes),
					},
					new UiNumberInput
					{
						Key = "rounds",
						Label = Strings.Widget.Config.Rounds(),
						Description = Strings.Widget.Config.RoundsDescription(),
						Min = UiValue.Of(1.0),
						Max = UiValue.Of(12.0),
						Step = UiValue.Of(1.0),
						ShowSlider = UiValue.Of(true),
						Binding = Bind.To(rounds),
					},
					new UiBooleanInput
					{
						Key = "autoAdvance",
						Label = Strings.Widget.Config.AutoAdvance(),
						Description = Strings.Widget.Config.AutoAdvanceDescription(),
						Binding = Bind.To(autoAdvance),
					},
					new UiDivider { Key = "look-divider" },
					new UiHeading { Key = "look-heading", Text = Strings.Widget.Config.LookHeading() },
					new UiBooleanInput
					{
						Key = "showLabel",
						Label = Strings.Widget.Config.ShowLabel(),
						Binding = Bind.To(showLabel),
					},
					new UiBooleanInput
					{
						Key = "showProgress",
						Label = Strings.Widget.Config.ShowProgress(),
						Binding = Bind.To(showProgress),
					},
					new UiBooleanInput
					{
						Key = "showControls",
						Label = Strings.Widget.Config.ShowControls(),
						Binding = Bind.To(showControls),
					},
					new UiBooleanInput
					{
						Key = "compactMode",
						Label = Strings.Widget.Config.CompactMode(),
						Binding = Bind.To(compactMode),
					},
					new UiColorInput
					{
						Key = "accentColor",
						Label = Strings.Widget.Config.AccentColor(),
						Description = Strings.Widget.Config.AccentColorDescription(),
						Binding = Bind.To(accentColor),
					},
				],
			},
		});
		return new FocusTimerSession(view);
	}

	private static JsonElement ReadElement(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key) =>
		surface.Attributes.TryGetValue(key, out var element) ? element : default;

	private static string? ReadString(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key)
	{
		var element = ReadElement(surface, key);
		return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
	}

	private static bool? ReadBool(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key)
	{
		var element = ReadElement(surface, key);
		return element.ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetBoolean() : null;
	}

	private sealed class FocusTimerSession : IUiSession, IDisposable, IAsyncDisposable
	{
		private readonly UiView _view;
		private readonly UiState<FocusTimerContent>? _content;
		private readonly TimerService? _timers;
		private readonly PomodoroService? _pomodoro;
		private readonly FocusTimerWidget? _owner;
		private readonly Serilog.ILogger? _logger;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task? _loop;
		private bool _disposed;

		public FocusTimerSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<FocusTimerContent> content,
			FocusTimerWidget owner,
			TimerService timers,
			PomodoroService pomodoro,
			Serilog.ILogger logger,
			bool live)
		{
			_content = content;
			_owner = owner;
			_timers = timers;
			_pomodoro = pomodoro;
			_logger = logger;
			Func<string, CancellationToken, Task>? command = live ? HandleCommandAsync : null;
			_view = new UiView(surface, FocusTimerView.Build(content, command));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
			if (live)
			{
				_loop = RefreshLoopAsync(_cts.Token);
			}
		}

		public FocusTimerSession(UiView view)
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
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}

		private async Task HandleCommandAsync(string command, CancellationToken cancellationToken)
		{
			if (_timers is null || _pomodoro is null || _content is null)
			{
				return;
			}

			try
			{
				var options = _content.Value.Options;
				switch (_content.Value.Mode)
				{
					case "countdown":
						await HandleCountdownCommandAsync(command, options, cancellationToken);
						break;
					case "stopwatch":
						HandleStopwatchCommand(command);
						break;
					default:
						HandlePomodoroCommand(command, options);
						break;
				}

				await RefreshAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				_logger?.Debug(ex, "Widget command failed.");
			}
		}

		private Task HandleCountdownCommandAsync(string command, FocusTimerOptions options, CancellationToken cancellationToken)
		{
			if (_timers is null)
			{
				return Task.CompletedTask;
			}

			switch (command)
			{
				case "reset":
					_timers.CancelCountdown();
					break;
				default:
				{
					if (_timers.CountdownRunning)
					{
						_timers.PauseCountdown();
					}
					else if (_timers.CountdownRemaining > TimeSpan.Zero)
					{
						_timers.ResumeCountdown();
					}
					else
					{
						_timers.StartCountdown(TimeSpan.FromMinutes(options.CountdownMinutes), string.Empty);
					}

					break;
				}
			}

			return Task.CompletedTask;
		}

		private void HandleStopwatchCommand(string command)
		{
			if (_timers is null)
			{
				return;
			}

			if (command == "reset")
			{
				_timers.ResetStopwatch();
				return;
			}

			if (_timers.StopwatchRunning)
			{
				_timers.StopStopwatch();
			}
			else
			{
				_timers.StartStopwatch();
			}
		}

		private void HandlePomodoroCommand(string command, FocusTimerOptions options)
		{
			if (_pomodoro is null)
			{
				return;
			}

			switch (command)
			{
				case "reset":
					_pomodoro.Stop();
					break;
				case "skip":
					_pomodoro.Skip();
					break;
				default:
				{
					var snapshot = _pomodoro.Snapshot();
					if (snapshot.Phase == PomodoroPhase.Idle)
					{
						_pomodoro.Start(options.ToPomodoroSettings());
					}
					else if (!snapshot.Running && snapshot.Remaining <= TimeSpan.Zero)
					{
						_pomodoro.Skip();
					}
					else
					{
						_pomodoro.Toggle();
					}

					break;
				}
			}
		}

		private async Task RefreshLoopAsync(CancellationToken cancellationToken)
		{
			using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
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
					await RefreshAsync(cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					_logger?.Debug(ex, "Widget refresh failed.");
				}
			}
		}

		private Task RefreshAsync(CancellationToken cancellationToken)
		{
			if (_content is null || _owner is null)
			{
				return Task.CompletedTask;
			}

			var next = _owner.BuildContent(_content.Value.Options);
			if (NeedsRefresh(_content.Value, next))
			{
				_content.Set(next);
			}

			return Task.CompletedTask;
		}

		private static bool NeedsRefresh(FocusTimerContent current, FocusTimerContent next)
		{
			if (current.Mode != next.Mode
				|| current.Caption != next.Caption
				|| current.Hero != next.Hero
				|| current.Subtitle != next.Subtitle
				|| current.Round != next.Round
				|| current.Running != next.Running
				|| current.HasSession != next.HasSession
				|| current.HasTotal != next.HasTotal
				|| current.Accent != next.Accent
				|| current.Progress.DurationMs != next.Progress.DurationMs
				|| current.Progress.Rate != next.Progress.Rate
				|| current.Dots != next.Dots)
			{
				return true;
			}

			var predicted = current.Progress.PositionMs;
			if (current.Running)
			{
				predicted += (long)(DateTimeOffset.UtcNow - current.Progress.Anchor).TotalMilliseconds;
			}

			return Math.Abs(predicted - next.Progress.PositionMs) > 1500;
		}

		private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);

		private void OnHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("handler-fault", e.Exception));
	}
}


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
using CsMd.Gsi;

namespace CsMd.Widgets;

public sealed record MatchHudOptions(
	bool ShowScore,
	bool ShowPlayer,
	bool ShowStatus,
	bool Compact)
{
	public static MatchHudOptions Default { get; } = new(true, true, true, false);

	public static MatchHudOptions FromData(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return Default;
		}

		return new MatchHudOptions(
			ShowScore: ReadFlag(data, "showScore", true),
			ShowPlayer: ReadFlag(data, "showPlayer", true),
			ShowStatus: ReadFlag(data, "showStatus", true),
			Compact: ReadFlag(data, "compactMode", false));
	}

	private static bool ReadFlag(JsonElement data, string name, bool fallback) =>
		data.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
			? element.GetBoolean()
			: fallback;
}

public sealed record MatchHudContent(
	bool Connected,
	string MapLine,
	string CtName,
	int CtScore,
	string TName,
	int TScore,
	string RoundText,
	string MapPhase,
	string PhaseTime,
	bool HasTimer,
	string PlayerName,
	string PlayerTeam,
	bool Alive,
	bool HasPlayer,
	double HpFrac,
	string HpText,
	string LoadoutLine,
	string MoneyLine,
	string PlaceText,
	bool HasPlace,
	string BombText,
	bool HasBomb,
	bool Smoked,
	bool Burning,
	bool DefuseKit,
	string SessionLine,
	MatchHudOptions Options)
{
	public static MatchHudContent Empty { get; } = new(
		false, string.Empty, "CT", 0, "T", 0, string.Empty, string.Empty, string.Empty, false,
		string.Empty, string.Empty, false, false, 0, string.Empty, string.Empty, string.Empty,
		string.Empty, false, string.Empty, false, false, false, false, string.Empty,
		MatchHudOptions.Default);

	public static MatchHudContent SampleLive { get; } = new(
		true, "DE_MIRAGE · COMPETITIVE", "NAVI", 9, "FAZE", 7, "R17", "LIVE", "1:23", true,
		"s1mple", "CT", true, true, 0.87, "87", "AWP · Rifle · 5 / 30", "$4,700 · 18 / 9 / 4",
		"Middle", true, "CARRIED", true, false, false, true, "18 / 9 · 2.00",
		MatchHudOptions.Default);

	public static MatchHudContent SampleBomb { get; } = new(
		true, "DE_DUST2 · COMPETITIVE", "CTs", 11, "Ts", 9, "R21", "LIVE", "0:32", true,
		"misuuu", "T", false, true, 0, "0", "AK-47 · Rifle · 0 / 90", "$800 · 14 / 12 / 3",
		"Bombsite A", true, "PLANTED · 32", true, false, false, false, "14 / 12 · 1.17",
		MatchHudOptions.Default);

	public static string FormatClock(double seconds)
	{
		if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
		{
			seconds = 0;
		}

		var total = (int)Math.Floor(seconds);
		return $"{total / 60}:{total % 60:D2}";
	}

	public static string FormatMoney(int money) =>
		"$" + money.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}

internal static class MatchHudView
{
	public static UiElement Build(UiState<MatchHudContent> content)
	{
		var options = content.Peek().Options;
		var body = new List<UiElement>
		{
			new UiWhen
			{
				Key = "live",
				Condition = () => content.Value.Connected,
				Content = () => LiveBody(content, options),
			},
			new UiWhen
			{
				Key = "idle",
				Condition = () => !content.Value.Connected,
				Content = () => new UiTextRun
				{
					Key = "idle-caption",
					Text = UiText.FromLocalized(() => Strings.Widget.NoData.Caption()),
					Size = UiSize.Capped(0.09, 11),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
		};

		return new UiStack
		{
			Key = "match-hud",
			Padding = 0.06,
			Gap = 0.04,
			Children = body,
		};
	}

	private static UiStack LiveBody(UiState<MatchHudContent> content, MatchHudOptions options)
	{
		var body = new List<UiElement>();

		if (options.ShowScore)
		{
			body.Add(Scorebug(content, options));
		}

		if (options.ShowPlayer)
		{
			body.Add(new UiWhen
			{
				Key = "player-when",
				Condition = () => content.Value.HasPlayer,
				Content = () => PlayerPlate(content, options),
			});
		}

		if (options.ShowStatus)
		{
			body.Add(StatusRow(content, options));
		}

		return new UiStack
		{
			Key = "live-body",
			Gap = options.Compact ? 0.02 : 0.04,
			Children = body,
		};
	}

	private static UiStack Scorebug(UiState<MatchHudContent> content, MatchHudOptions options)
	{
		var big = options.Compact ? UiSize.Capped(0.16, 24) : UiSize.Capped(0.2, 32);
		var micro = UiSize.Capped(0.07, 9);
		return new UiStack
		{
			Key = "scorebug",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.SpaceBetween,
			Children =
			[
				new UiStack
				{
					Key = "ct",
					Children =
					[
						SideScore(content, big, micro, ct: true),
					],
				},
				new UiStack
				{
					Key = "mid",
					Children =
					[
						new UiTextRun
						{
							Key = "round",
							Text = UiText.From(() => content.Value.RoundText),
							Size = UiSize.Capped(0.09, 12),
							Weight = UiComponentTextWeights.SemiBold,
							Align = UiComponentAlignments.Center,
						},
						new UiTextRun
						{
							Key = "phase",
							Text = UiText.From(() => content.Value.MapPhase),
							Size = micro,
							Role = UiComponentTextRoles.Muted,
							Align = UiComponentAlignments.Center,
						},
					],
				},
				new UiStack
				{
					Key = "t",
					Children =
					[
						SideScore(content, big, micro, ct: false),
					],
				},
			],
		};
	}

	private static UiStack SideScore(UiState<MatchHudContent> content, UiSize big, UiSize micro, bool ct) => new UiStack
	{
		Key = ct ? "ct-side" : "t-side",
		Children =
		[
			new UiTextRun
			{
				Key = ct ? "ct-score" : "t-score",
				Text = UiText.From(() => ct
					? content.Value.CtScore.ToString(System.Globalization.CultureInfo.InvariantCulture)
					: content.Value.TScore.ToString(System.Globalization.CultureInfo.InvariantCulture)),
				Size = big,
				Weight = UiComponentTextWeights.SemiBold,
				Align = UiComponentAlignments.Center,
			},
			new UiTextRun
			{
				Key = ct ? "ct-name" : "t-name",
				Text = UiText.From(() => ct ? content.Value.CtName : content.Value.TName),
				Size = micro,
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			},
		],
	};

	private static UiStack PlayerPlate(UiState<MatchHudContent> content, MatchHudOptions options)
	{
		var body = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "player-name",
				Text = UiText.From(() => content.Value.PlayerName),
				Size = options.Compact ? UiSize.Capped(0.11, 14) : UiSize.Capped(0.13, 17),
				Weight = UiComponentTextWeights.SemiBold,
				Align = UiComponentAlignments.Center,
			},
			new UiTextRun
			{
				Key = "hp-line",
				Text = UiText.From(() => content.Value.HpText),
				Size = options.Compact ? UiSize.Capped(0.16, 22) : UiSize.Capped(0.2, 30),
				Weight = UiComponentTextWeights.SemiBold,
				Align = UiComponentAlignments.Center,
			},
			new UiRangeBar
			{
				Key = "hp-bar",
				Start = UiValue.Of(0.0),
				End = UiValue.From(() => content.Value.HpFrac),
				Thickness = 0.035,
			},
			MicroLine(content, "loadout", () => content.Value.LoadoutLine),
			MicroLine(content, "money", () => content.Value.MoneyLine),
		};

		return new UiStack
		{
			Key = "player",
			Gap = 0.02,
			Children = body,
		};
	}

	private static UiStack StatusRow(UiState<MatchHudContent> content, MatchHudOptions options)
	{
		var pills = new List<UiElement>
		{
			LocalizedPill("state", () => true, () => content.Value.Alive
				? Strings.Widget.State.Alive() : Strings.Widget.State.Dead()),
			TextPill(content, "place", () => content.Value.HasPlace, () => content.Value.PlaceText),
			TextPill(content, "bomb", () => content.Value.HasBomb, () => content.Value.BombText),
			LocalizedPill("smoked", () => content.Value.Smoked, Strings.Widget.Effects.Smoked),
			LocalizedPill("burning", () => content.Value.Burning, Strings.Widget.Effects.Burning),
			LocalizedPill("defuse", () => content.Value.DefuseKit, Strings.Widget.Effects.DefuseKit),
		};

		var children = new List<UiElement>
		{
			new UiStack
			{
				Key = "pills",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Gap = 0.03,
				Children = pills,
			},
		};

		if (!options.Compact)
		{
			children.Add(new UiTextRun
			{
				Key = "session",
				Text = UiText.From(() => content.Value.SessionLine),
				Size = UiSize.Capped(0.07, 9),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			});
		}

		return new UiStack
		{
			Key = "status",
			Gap = 0.02,
			Children = children,
		};
	}

	private static UiWhen LocalizedPill(
		string key,
		Func<bool> condition,
		Func<MacroDeck.Localization.LocalizedString> text)
	{
		return new UiWhen
		{
			Key = key + "-when",
			Condition = () => condition(),
			Content = () => new UiTextRun
			{
				Key = key,
				Text = UiText.FromLocalized(() => text()),
				Size = UiSize.Capped(0.075, 10),
				Weight = UiComponentTextWeights.Medium,
				Align = UiComponentAlignments.Center,
			},
		};
	}

	private static UiWhen TextPill(
		UiState<MatchHudContent> content,
		string key,
		Func<bool> condition,
		Func<string> text)
	{
		return new UiWhen
		{
			Key = key + "-when",
			Condition = () => condition(),
			Content = () => new UiTextRun
			{
				Key = key,
				Text = UiText.From(() => text()),
				Size = UiSize.Capped(0.075, 10),
				Weight = UiComponentTextWeights.Medium,
				Align = UiComponentAlignments.Center,
			},
		};
	}

	private static UiWhen MicroLine(UiState<MatchHudContent> content, string key, Func<string> text) => new UiWhen
	{
		Key = key + "-when",
		Condition = () => !string.IsNullOrWhiteSpace(text()),
		Content = () => new UiTextRun
		{
			Key = key,
			Text = UiText.From(() => text()),
			Size = UiSize.Capped(0.08, 10),
			Role = UiComponentTextRoles.Muted,
			Align = UiComponentAlignments.Center,
		},
	};
}

public static class MatchHudPreviews
{
	[UiPreview("Live match", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement LiveMatch() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.SampleLive));

	[UiPreview("Bomb planted", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement BombPlanted() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.SampleBomb));

	[UiPreview("No data", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NoData() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.Empty));
}

public sealed class MatchHudWidget : IWidgetTypeProvider, IUiProvider
{
	private readonly GsiService _gsi;
	private readonly Serilog.ILogger _logger;
	private static readonly object s_registrationGate = new();
	private static readonly WidgetTypeDescriptor s_descriptor = new(
		"match-hud",
		Strings.Widget.MatchHud.Name(),
		Strings.Widget.MatchHud.Description(),
		"""{"showScore":true,"showPlayer":true,"showStatus":true,"compactMode":false}""",
		"""{"type":"object","properties":{"showScore":{"type":"boolean"},"showPlayer":{"type":"boolean"},"showStatus":{"type":"boolean"},"compactMode":{"type":"boolean"}}}""",
		true,
		new Dictionary<string, string>());
	private static string? s_widgetTypeId;
	private static bool s_registered;

	public MatchHudWidget(GsiService gsi, Serilog.ILogger logger)
	{
		_gsi = gsi;
		_logger = logger.ForContext<MatchHudWidget>();
	}

	public string ProviderName => "CS:MD";

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

			var options = MatchHudOptions.FromData(ReadElement(surface, UiWidgetSurfaceAttributes.Data));
			if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample) == true)
			{
				return Task.FromResult<IUiSession?>(new MatchHudSession(
					surface,
					new UiState<MatchHudContent>(MatchHudContent.SampleLive with { Options = options })));
			}

			return Task.FromResult<IUiSession?>(new MatchHudSession(surface, new UiState<MatchHudContent>(BuildContent(options)), this, _gsi, _logger));
		}

		if (surface.Kind == UiSurfaceKinds.Config
			&& ReadString(surface, UiConfigSurfaceAttributes.EntryPoint) == UiConfigEntryPoints.WidgetConfig)
		{
			return Task.FromResult<IUiSession?>(BuildConfigSession(surface, MatchHudOptions.FromData(ReadElement(surface, UiConfigSurfaceAttributes.WidgetData))));
		}

		return Task.FromResult<IUiSession?>(null);
	}

	public MatchHudContent BuildContent(MatchHudOptions options)
	{
		GsiSnapshot snapshot;
		try
		{
			snapshot = _gsi.Snapshot();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Match HUD snapshot failed.");
			return MatchHudContent.Empty with { Options = options };
		}

		if (!snapshot.Connected)
		{
			return MatchHudContent.Empty with { Options = options };
		}

		var hp = Math.Clamp(snapshot.Health, 0, 100);
		var loadout = JoinParts(
			snapshot.Weapon,
			snapshot.WeaponType,
			snapshot.AmmoClip >= 0 && snapshot.AmmoReserve >= 0
				? $"{snapshot.AmmoClip} / {snapshot.AmmoReserve}"
				: string.Empty);
		var money = JoinParts(
			MatchHudContent.FormatMoney(snapshot.Money),
			$"{snapshot.Kills} / {snapshot.Deaths} / {snapshot.Assists}");
		var bomb = snapshot.BombState;
		if (!string.IsNullOrEmpty(bomb) && snapshot.BombCountdown is { } countdown)
		{
			bomb += " · " + MatchHudContent.FormatClock(countdown);
		}

		return new MatchHudContent(
			true,
			JoinParts((snapshot.MapName ?? string.Empty).ToUpperInvariant(), (snapshot.MapMode ?? string.Empty).ToUpperInvariant()),
			OrDash(snapshot.CtName), snapshot.CtScore,
			OrDash(snapshot.TName), snapshot.TScore,
			"R" + snapshot.MapRound.ToString(System.Globalization.CultureInfo.InvariantCulture),
			(snapshot.MapPhase ?? string.Empty).ToUpperInvariant(),
			snapshot.PhaseEndsIn is { } ends ? MatchHudContent.FormatClock(ends) : string.Empty,
			snapshot.PhaseEndsIn is not null,
			snapshot.PlayerName ?? string.Empty,
			NormalizeTeam(snapshot.PlayerTeam),
			snapshot.Alive,
			snapshot.HasPlayer,
			hp / 100.0,
			hp.ToString(System.Globalization.CultureInfo.InvariantCulture),
			loadout,
			money,
			snapshot.PlaceName ?? string.Empty,
			!string.IsNullOrEmpty(snapshot.PlaceName),
			(bomb ?? string.Empty).ToUpperInvariant(),
			!string.IsNullOrEmpty(bomb),
			snapshot.Smoked,
			snapshot.Burning,
			snapshot.DefuseKit,
			$"{snapshot.SessionKills} / {snapshot.SessionDeaths} · {snapshot.SessionKd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}",
			options);
	}

	public static string JoinParts(params string?[] parts) =>
		string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

	public static string OrDash(string? value) =>
		string.IsNullOrWhiteSpace(value) ? "-" : value;

	public static string NormalizeTeam(string? team) => team?.ToUpperInvariant() switch
	{
		"CT" => "CT",
		"T" => "T",
		_ => string.Empty,
	};

	private static MatchHudSession BuildConfigSession(
		MacroDeck.Ui.Model.Surfaces.UiSurface surface,
		MatchHudOptions options)
	{
		var showScore = new UiState<bool>(options.ShowScore);
		var showPlayer = new UiState<bool>(options.ShowPlayer);
		var showStatus = new UiState<bool>(options.ShowStatus);
		var compactMode = new UiState<bool>(options.Compact);
		var view = new UiView(surface, new UiWidgetConfiguration
		{
			Key = "config",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "look-heading", Text = Strings.Widget.Config.LookHeading() },
					new UiBooleanInput
					{
						Key = "showScore",
						Label = Strings.Widget.Config.ShowScore(),
						Description = Strings.Widget.Config.ShowScoreDescription(),
						Binding = Bind.To(showScore),
					},
					new UiBooleanInput
					{
						Key = "showPlayer",
						Label = Strings.Widget.Config.ShowPlayer(),
						Description = Strings.Widget.Config.ShowPlayerDescription(),
						Binding = Bind.To(showPlayer),
					},
					new UiBooleanInput
					{
						Key = "showStatus",
						Label = Strings.Widget.Config.ShowStatus(),
						Description = Strings.Widget.Config.ShowStatusDescription(),
						Binding = Bind.To(showStatus),
					},
					new UiBooleanInput
					{
						Key = "compactMode",
						Label = Strings.Widget.Config.CompactMode(),
						Binding = Bind.To(compactMode),
					},
				],
			},
		});
		return new MatchHudSession(view);
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

	private sealed class MatchHudSession : IUiSession, IDisposable, IAsyncDisposable
	{
		private readonly UiView _view;
		private readonly UiState<MatchHudContent>? _content;
		private readonly MatchHudWidget? _owner;
		private readonly GsiService? _gsi;
		private readonly Serilog.ILogger? _logger;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task? _loop;
		private bool _disposed;

		public MatchHudSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<MatchHudContent> content,
			MatchHudWidget owner,
			GsiService gsi,
			Serilog.ILogger logger)
		{
			_content = content;
			_owner = owner;
			_gsi = gsi;
			_logger = logger;
			_view = new UiView(surface, MatchHudView.Build(content));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
			_loop = RefreshLoopAsync(_cts.Token);
		}

		public MatchHudSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<MatchHudContent> content)
		{
			_content = content;
			_view = new UiView(surface, MatchHudView.Build(content));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
		}

		public MatchHudSession(UiView view)
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
					Refresh();
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

		private void Refresh()
		{
			if (_content is null || _owner is null)
			{
				return;
			}

			var next = _owner.BuildContent(_content.Value.Options);
			if (_content.Value != next)
			{
				_content.Set(next);
			}
		}

		private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);

		private void OnHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("handler-fault", e.Exception));
	}
}

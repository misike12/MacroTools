using System.Diagnostics;
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
using R6Md.Replays;

namespace R6Md.Widgets;

public sealed record R6HudOptions(
	bool ShowScore,
	bool ShowRoster,
	bool ShowFeed,
	int FeedCount,
	bool ShowSession)
{
	public static R6HudOptions Default { get; } = new(true, true, true, 4, true);

	public static R6HudOptions FromData(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return Default;
		}

		return new R6HudOptions(
			ShowScore: ReadFlag(data, "showScore", true),
			ShowRoster: ReadFlag(data, "showRoster", true),
			ShowFeed: ReadFlag(data, "showFeed", true),
			FeedCount: ReadCount(data),
			ShowSession: ReadFlag(data, "showSession", true));
	}

	private static bool ReadFlag(JsonElement data, string name, bool fallback) =>
		data.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
			? element.GetBoolean()
			: fallback;

	private static int ReadCount(JsonElement data)
	{
		if (data.TryGetProperty("feedCount", out var element) && element.ValueKind == JsonValueKind.Number)
		{
			try
			{
				return Math.Clamp((int)element.GetDouble(), 1, 8);
			}
			catch (Exception)
			{
			}
		}

		return Default.FeedCount;
	}
}

public sealed record R6FeedItem(string Key, MacroDeck.Localization.LocalizedString Text);

public sealed record R6HudContent(
	bool Connected,
	bool HasMatch,
	string MapLine,
	string YourScore,
	string OppScore,
	MacroDeck.Localization.LocalizedString YourRole,
	MacroDeck.Localization.LocalizedString OppRole,
	string YourRoleRaw,
	string OppRoleRaw,
	string RoundText,
	string SiteLine,
	IReadOnlyList<R6Dot> HistoryDots,
	IReadOnlyList<R6RosterRow> YourTeam,
	IReadOnlyList<R6RosterRow> OppTeam,
	string OppLine,
	string YourLine,
	MacroDeck.Localization.LocalizedString SessionLine,
	IReadOnlyList<R6FeedItem> FeedItems,
	bool HasFeed,
	R6HudOptions Options)
{
	public static R6HudContent Empty { get; } = new(
		false, false, string.Empty, "0", "0",
		Strings.Widget.Roles.Attack(), Strings.Widget.Roles.Defense(), string.Empty, string.Empty,
		string.Empty, string.Empty,
		[], [], [], string.Empty, string.Empty,
		Strings.Widget.Session.Line(0, 0, 0, 0), [], false, R6HudOptions.Default);

	public static R6HudContent Sample { get; } = new(
		true, true, "CHALET · BOMB · RANKED", "2", "1",
		Strings.Widget.Roles.Defense(), Strings.Widget.Roles.Attack(), "Defense", "Attack",
		"R4 OF 9",
		"2F Master Bedroom, 2F Office",
		[new R6Dot("0", "W"), new R6Dot("1", "L"), new R6Dot("2", "W")],
		[
			new R6RosterRow("You.Siege", "Jäger", "1-0-0", true),
			new R6RosterRow("Mate.One", "Mute", "0-1-0", false),
		],
		[
			new R6RosterRow("Rival.One", "Ash", "0-1-0", false),
			new R6RosterRow("Rival.Two", "Thermite", "1-0-0", false),
		],
		"Top: Rival.Two (1-0)",
		"You.Siege · Jäger · 1-0-0 · HS 100%",
		Strings.Widget.Session.Line(4, 2, 1, 3),
		[
			new R6FeedItem("f2", Strings.Widget.Feed.Kill("You.Siege", "Rival.One")),
			new R6FeedItem("f1", Strings.Widget.Feed.RoundWon(2, "Elimination")),
		],
		true, R6HudOptions.Default);
}

public sealed record R6RosterRow(string Name, string Operator, string Kda, bool IsYou);

public sealed record R6Dot(string Key, string Kind);

internal static class R6HudView
{
	internal const string TreeGeneration = "1";

	private const string AttackColor = "#FB923C";
	private const string DefenseColor = "#38BDF8";
	private const string GoodColor = "#4ADE80";
	private const string BadColor = "#F87171";

	public static UiElement Build(UiState<R6HudContent> content, R6HudActions? actions = null)
	{
		var options = content.Peek().Options;
		var body = new List<UiElement>
		{
			new UiWhen
			{
				Key = "live",
				Condition = () => content.Value.HasMatch,
				Content = () => LiveBody(content, options),
			},
			new UiWhen
			{
				Key = "idle",
				Condition = () => !content.Value.HasMatch,
				Content = () => IdleCard(actions),
			},
		};

		return new UiStack
		{
			Key = "r6-hud-g" + TreeGeneration,
			Padding = 0.04,
			Gap = 0.015,
			Children = body,
		};
	}

	private static UiStack LiveBody(UiState<R6HudContent> content, R6HudOptions options)
	{
		var body = new List<UiElement>();

		if (options.ShowScore)
		{
			body.Add(Scorebug(content));
		}

		if (options.ShowRoster)
		{
			body.Add(new UiWhen
			{
				Key = "roster-when",
				Condition = () => content.Value.YourTeam.Count > 0,
				Content = () => Roster(content),
			});
		}

		if (options.ShowFeed)
		{
			body.Add(new UiWhen
			{
				Key = "feed-when",
				Condition = () => content.Value.HasFeed,
				Content = () => FeedList(content),
			});
		}

		if (options.ShowSession)
		{
			body.Add(new UiTextRun
			{
				Key = "session",
				Text = UiText.FromLocalized(() => content.Value.SessionLine),
				Size = UiSize.Capped(0.075, 9),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			});
		}

		return new UiStack
		{
			Key = "live-body",
			Gap = 0.012,
			Children = body,
		};
	}

	private static UiStack Scorebug(UiState<R6HudContent> content) => new()
	{
		Key = "scorebug",
		Gap = 0.015,
		Children =
		[
			new UiStack
			{
				Key = "scores",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.SpaceBetween,
				Align = UiComponentAlignments.Center,
				Children =
				[
					SideScore(content, yours: true),
					new UiStack
					{
						Key = "mid",
						Gap = 0.008,
						Children =
						[
							new UiTextRun
							{
								Key = "round",
								Text = UiText.From(() => content.Value.RoundText),
								Size = UiSize.Capped(0.08, 10),
								Weight = UiComponentTextWeights.SemiBold,
								Align = UiComponentAlignments.Center,
							},
							new UiTextRun
							{
								Key = "map",
								Text = UiText.From(() => content.Value.MapLine),
								Size = UiSize.Capped(0.075, 9),
								Role = UiComponentTextRoles.Muted,
								Align = UiComponentAlignments.Center,
							},
						],
					},
					SideScore(content, yours: false),
				],
			},
			new UiWhen
			{
				Key = "history-when",
				Condition = () => content.Value.HistoryDots.Count > 0,
				Content = () => new UiStack
				{
					Key = "history",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.Center,
					Gap = 0.015,
					Children =
					[
						new UiRepeat<R6Dot>
						{
							Key = "dots",
							Items = UiValue.From(() => content.Value.HistoryDots),
							KeySelector = static dot => dot.Key,
							Template = static (dot, key) => new UiTextRun
							{
								Key = key,
								Text = UiText.From(() => dot.Kind == "W" ? "●" : dot.Kind == "L" ? "●" : "○"),
								Size = UiSize.Capped(0.07, 10),
								Color = dot.Kind == "W"
									? UiValue.Of(GoodColor)
									: dot.Kind == "L" ? UiValue.Of(BadColor) : UiValue.None<string>(),
								Align = UiComponentAlignments.Center,
							},
						},
					],
				},
			},
			new UiWhen
			{
				Key = "site-when",
				Condition = () => !string.IsNullOrWhiteSpace(content.Value.SiteLine),
				Content = () => new UiTextRun
				{
					Key = "site",
					Text = UiText.From(() => content.Value.SiteLine),
					Size = UiSize.Capped(0.075, 9),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
		],
	};

	private static UiStack SideScore(UiState<R6HudContent> content, bool yours) => new()
	{
		Key = yours ? "your-side" : "opp-side",
		Gap = 0.004,
		Children =
		[
			new UiTextRun
			{
				Key = yours ? "your-score" : "opp-score",
				Text = UiText.From(() => yours ? content.Value.YourScore : content.Value.OppScore),
				Size = UiSize.Capped(0.17, 30),
				Weight = UiComponentTextWeights.SemiBold,
				Color = UiValue.From(() => RoleColor(yours ? content.Value.YourRoleRaw : content.Value.OppRoleRaw)),
				Digits = UiValue.Of(2.0),
				Align = UiComponentAlignments.Center,
			},
			new UiTextRun
			{
				Key = yours ? "your-role" : "opp-role",
				Text = UiText.FromLocalized(() => yours ? content.Value.YourRole : content.Value.OppRole),
				Size = UiSize.Capped(0.07, 9),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			},
		],
	};

	private static string RoleColor(string role) =>
		string.Equals(role, "Attack", StringComparison.OrdinalIgnoreCase) ? AttackColor : DefenseColor;

	private static UiStack Roster(UiState<R6HudContent> content) => new()
	{
		Key = "roster",
		Gap = 0.008,
		Children =
		[
			new UiRepeat<R6RosterRow>
			{
				Key = "your-team",
				Items = UiValue.From(() => content.Value.YourTeam),
				KeySelector = static row => row.Name,
				Template = static (row, key) => new UiTextRun
				{
					Key = key,
					Text = UiText.From(() => $"{row.Name} · {row.Operator} · {row.Kda}"),
					Size = UiSize.Capped(0.075, 10),
					Weight = row.IsYou ? UiComponentTextWeights.SemiBold : UiComponentTextWeights.Regular,
					Align = UiComponentAlignments.Center,
				},
			},
			new UiWhen
			{
				Key = "opp-when",
				Condition = () => !string.IsNullOrWhiteSpace(content.Value.OppLine),
				Content = () => new UiTextRun
				{
					Key = "opp-line",
					Text = UiText.From(() => content.Value.OppLine),
					Size = UiSize.Capped(0.075, 9),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
			new UiWhen
			{
				Key = "you-when",
				Condition = () => !string.IsNullOrWhiteSpace(content.Value.YourLine),
				Content = () => new UiTextRun
				{
					Key = "you-line",
					Text = UiText.From(() => content.Value.YourLine),
					Size = UiSize.Capped(0.08, 10),
					Weight = UiComponentTextWeights.SemiBold,
					Align = UiComponentAlignments.Center,
				},
			},
		],
	};

	private static UiStack FeedList(UiState<R6HudContent> content) => new()
	{
		Key = "feed",
		Gap = 0.015,
		Children =
		[
			new UiRepeat<R6FeedItem>
			{
				Key = "feed-items",
				Items = UiValue.From(() => content.Value.FeedItems),
				KeySelector = static item => item.Key,
				Template = static (item, key) => new UiTextRun
				{
					Key = key,
					Text = UiText.FromLocalized(() => item.Text),
					Size = UiSize.Capped(0.085, 10),
					Role = UiComponentTextRoles.Secondary,
					Align = UiComponentAlignments.Center,
				},
			},
		],
	};

	private static UiStack IdleCard(R6HudActions? actions)
	{
		var simulate = new UiButton
		{
			Key = "idle-simulate",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Padding = 0.02,
			Children =
			[
				new UiTextRun
				{
					Key = "idle-simulate-label",
					Text = UiText.FromLocalized(() => Strings.Widget.Idle.Simulate()),
					Size = UiSize.Capped(0.05, 12),
					Weight = UiComponentTextWeights.SemiBold,
					Align = UiComponentAlignments.Center,
				},
			],
		};
		var folder = new UiButton
		{
			Key = "idle-folder",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Padding = 0.02,
			Background = UiValue.Of("#22252C"),
			Children =
			[
				new UiTextRun
				{
					Key = "idle-folder-label",
					Text = UiText.FromLocalized(() => Strings.Widget.Idle.OpenFolder()),
					Size = UiSize.Capped(0.05, 12),
					Weight = UiComponentTextWeights.SemiBold,
					Align = UiComponentAlignments.Center,
				},
			],
		};
		return new UiStack
		{
			Key = "idle",
			Align = UiComponentAlignments.Center,
			Gap = 0.02,
			Children =
			[
				new UiTextRun
				{
					Key = "idle-title",
					Text = UiText.FromLocalized(() => Strings.Widget.Idle.Title()),
					Size = UiSize.Capped(0.06, 14),
					Weight = UiComponentTextWeights.SemiBold,
					Align = UiComponentAlignments.Center,
				},
				new UiTextRun
				{
					Key = "idle-caption",
					Text = UiText.FromLocalized(() => Strings.Widget.NoData.Caption()),
					Size = UiSize.Capped(0.04, 10),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
				new UiStack
				{
					Key = "idle-buttons",
					Direction = UiComponentDirections.Horizontal,
					Gap = 0.02,
					Children =
					[
						actions is null ? simulate : simulate with { Events = [UiEventHandler.On(UiComponentEvents.Press, _ => actions.Simulate())] },
						actions is null ? folder : folder with { Events = [UiEventHandler.On(UiComponentEvents.Press, _ => actions.OpenFolder())] },
					],
				},
				new UiTextRun
				{
					Key = "idle-hint",
					Text = UiText.FromLocalized(() => Strings.Widget.Idle.Hint()),
					Size = UiSize.Capped(0.035, 9),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			],
		};
	}
}

public sealed record R6HudActions(
	Func<UiEventOutcome> Simulate,
	Func<UiEventOutcome> OpenFolder);

public static class R6HudPreviews
{
	[UiPreview("Match", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Match() => R6HudView.Build(
		new UiState<R6HudContent>(R6HudContent.Sample));

	[UiPreview("No data", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NoData() => R6HudView.Build(
		new UiState<R6HudContent>(R6HudContent.Empty));

	// Test and tooling support: builds the live tree against caller-owned
	// state so patches can be observed without a running session.
	public static UiElement FromState(UiState<R6HudContent> state) =>
		R6HudView.Build(state);
}

public sealed class MatchHudWidget : IWidgetTypeProvider, IUiProvider
{
	private readonly ReplayService _replays;
	private readonly Serilog.ILogger _logger;
	private static readonly object s_registrationGate = new();
	private static readonly WidgetTypeDescriptor s_descriptor = new(
		"match-hud",
		Strings.Widget.MatchHud.Name(),
		Strings.Widget.MatchHud.Description(),
		"""{"showScore":true,"showRoster":true,"showFeed":true,"feedCount":4,"showSession":true}""",
		"""{"type":"object","properties":{"showScore":{"type":"boolean"},"showRoster":{"type":"boolean"},"showFeed":{"type":"boolean"},"feedCount":{"type":"number"},"showSession":{"type":"boolean"}}}""",
		true,
		new Dictionary<string, string>());
	private static string? s_widgetTypeId;
	private static bool s_registered;

	public MatchHudWidget(ReplayService replays, Serilog.ILogger logger)
	{
		_replays = replays;
		_logger = logger.ForContext<MatchHudWidget>();
	}

	public string ProviderName => "R6MD";

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

			var options = R6HudOptions.FromData(ReadElement(surface, UiWidgetSurfaceAttributes.Data));
			if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample) == true)
			{
				return Task.FromResult<IUiSession?>(new MatchHudSession(
					surface,
					new UiState<R6HudContent>(R6HudContent.Sample with { Options = options })));
			}

			return Task.FromResult<IUiSession?>(new MatchHudSession(surface, new UiState<R6HudContent>(BuildContent(options)), this, _replays, _logger));
		}

		if (surface.Kind == UiSurfaceKinds.Config
			&& ReadString(surface, UiConfigSurfaceAttributes.EntryPoint) == UiConfigEntryPoints.WidgetConfig)
		{
			return Task.FromResult<IUiSession?>(BuildConfigSession(surface, R6HudOptions.FromData(ReadElement(surface, UiConfigSurfaceAttributes.WidgetData))));
		}

		return Task.FromResult<IUiSession?>(null);
	}

	public R6HudContent BuildContent(R6HudOptions options)
	{
		R6Snapshot snapshot;
		try
		{
			snapshot = _replays.Snapshot();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Match HUD snapshot failed.");
			return R6HudContent.Empty with { Options = options };
		}

		if (!snapshot.HasMatch)
		{
			return R6HudContent.Empty with { Options = options };
		}

		var feed = snapshot.FeedItems
			.Take(options.FeedCount)
			.Select(f => new R6FeedItem(f.Key, f.Text))
			.ToList();
		return new R6HudContent(
			snapshot.Connected, true,
			$"{snapshot.MapName} · {snapshot.MapMode} · {snapshot.MatchType}",
			snapshot.YourScore.ToString(System.Globalization.CultureInfo.InvariantCulture),
			snapshot.OppScore.ToString(System.Globalization.CultureInfo.InvariantCulture),
			RoleDisplay(snapshot.YourRole),
			RoleDisplay(snapshot.OppRole),
			snapshot.YourRole,
			snapshot.OppRole,
			$"R{snapshot.RoundNumber} of {Math.Max(snapshot.RoundsPerMatch, snapshot.RoundNumber)}",
			snapshot.Site,
			snapshot.RoundHistory.Select((c, i) => new R6Dot(
				i.ToString(System.Globalization.CultureInfo.InvariantCulture), c.ToString())).ToList(),
			snapshot.Players
				.Where(p => p.Team == snapshot.YourTeamIndex)
				.OrderByDescending(p => p.Kills)
				.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
				.Select(p => new R6RosterRow(p.Name, p.Operator, $"{p.Kills}-{p.Deaths}-{p.Assists}", p.IsYou))
				.ToList(),
			snapshot.Players
				.Where(p => p.Team != snapshot.YourTeamIndex)
				.OrderByDescending(p => p.Kills)
				.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
				.Select(p => new R6RosterRow(p.Name, p.Operator, $"{p.Kills}-{p.Deaths}-{p.Assists}", false))
				.ToList(),
			FormatOppLine(snapshot),
			FormatYourLine(snapshot),
			Strings.Widget.Session.Line(snapshot.SessionKills, snapshot.SessionDeaths, snapshot.SessionAssists, snapshot.SessionHs),
			feed, feed.Count > 0, options);
	}

	private static MacroDeck.Localization.LocalizedString RoleDisplay(string role) =>
		string.Equals(role, "Attack", StringComparison.OrdinalIgnoreCase)
			? Strings.Widget.Roles.Attack()
			: string.Equals(role, "Defense", StringComparison.OrdinalIgnoreCase)
				? Strings.Widget.Roles.Defense()
				: Strings.Widget.Roles.Unknown();

	private static string FormatOppLine(R6Snapshot snapshot)
	{
		var best = snapshot.Players
			.Where(p => p.Team != snapshot.YourTeamIndex)
			.OrderByDescending(p => p.Kills)
			.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		return best is null
			? string.Empty
			: $"Top: {best.Name} ({best.Kills}-{best.Deaths})";
	}

	private static string FormatYourLine(R6Snapshot snapshot)
	{
		if (string.IsNullOrEmpty(snapshot.YourName))
		{
			return string.Empty;
		}

		var hs = $" · HS {snapshot.YourHeadshots}";
		var hp = snapshot.YourHp >= 0 ? $" · {snapshot.YourHp} HP" : string.Empty;
		return $"{snapshot.YourName} · {snapshot.YourOperator} · {snapshot.YourKills}-{snapshot.YourDeaths}-{snapshot.YourAssists}{hs}{hp}";
	}

	private static MatchHudSession BuildConfigSession(
		MacroDeck.Ui.Model.Surfaces.UiSurface surface,
		R6HudOptions options)
	{
		var showScore = new UiState<bool>(options.ShowScore);
		var showRoster = new UiState<bool>(options.ShowRoster);
		var showFeed = new UiState<bool>(options.ShowFeed);
		var feedCount = new UiState<double>(options.FeedCount);
		var showSession = new UiState<bool>(options.ShowSession);
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
						Binding = Bind.To(showScore),
					},
					new UiBooleanInput
					{
						Key = "showRoster",
						Label = Strings.Widget.Config.ShowRoster(),
						Binding = Bind.To(showRoster),
					},
					new UiBooleanInput
					{
						Key = "showFeed",
						Label = Strings.Widget.Config.ShowFeed(),
						Binding = Bind.To(showFeed),
					},
					new UiNumberInput
					{
						Key = "feedCount",
						Label = Strings.Widget.Config.FeedCount(),
						Min = UiValue.Of(1.0),
						Max = UiValue.Of(8.0),
						Step = UiValue.Of(1.0),
						ShowSlider = UiValue.Of(true),
						Binding = Bind.To(feedCount),
					},
					new UiBooleanInput
					{
						Key = "showSession",
						Label = Strings.Widget.Config.ShowSession(),
						Binding = Bind.To(showSession),
					},
				],
			},
		});
		return new MatchHudSession(view);
	}

	private sealed class MatchHudSession : IUiSession, IDisposable, IAsyncDisposable
	{
		private readonly UiView _view;
		private readonly UiState<R6HudContent>? _content;
		private readonly MatchHudWidget? _owner;
		private readonly ReplayService? _replays;
		private readonly Serilog.ILogger? _logger;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task? _loop;
		private bool _disposed;

		public MatchHudSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<R6HudContent> content,
			MatchHudWidget owner,
			ReplayService replays,
			Serilog.ILogger logger)
		{
			_content = content;
			_owner = owner;
			_replays = replays;
			_logger = logger.ForContext<MatchHudSession>();
			_replays.MatchEvent += OnMatchEvent;
			_view = new UiView(surface, R6HudView.Build(content, SessionActions(content, owner, replays, logger)));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
			_loop = RefreshLoopAsync(_cts.Token);
		}

		public MatchHudSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<R6HudContent> content)
		{
			_content = content;
			_view = new UiView(surface, R6HudView.Build(content));
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
			if (_replays is not null)
			{
				_replays.MatchEvent -= OnMatchEvent;
			}

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

		private static R6HudActions SessionActions(
			UiState<R6HudContent> content,
			MatchHudWidget owner,
			ReplayService replays,
			Serilog.ILogger logger) => new(
			Simulate: () =>
			{
				try
				{
					replays.InjectSample();
					return UiEventOutcome.Accepted;
				}
				catch (Exception ex)
				{
					logger.Debug(ex, "Widget simulate failed.");
					return UiEventOutcome.Rejected("Simulate failed.");
				}
			},
			OpenFolder: () =>
			{
				try
				{
					return MatchHudWidget.OpenReplayFolder(replays, logger)
						? UiEventOutcome.Accepted
						: UiEventOutcome.Rejected("No replay folder was found.");
				}
				catch (Exception ex)
				{
					logger.Debug(ex, "Widget open-folder failed.");
					return UiEventOutcome.Rejected("Open folder failed.");
				}
			});
		// Rejection reasons travel as plain strings with no localization
		// reference, so these stay English literals rather than keys that
		// would render raw.

		private void OnMatchEvent(object? sender, R6MatchEvent matchEvent)
		{
			// Feed rows arrive through the refresh loop's content rebuild; the
			// event itself only needs to wake the loop immediately.
			RefreshNow();
		}

		private void RefreshNow()
		{
			if (_owner is null || _content is null)
			{
				return;
			}

			try
			{
				var next = _owner.BuildContent(_content.Value.Options);
				if (!next.Equals(_content.Value))
				{
					_content.Set(next);
				}
			}
			catch (Exception ex)
			{
				_logger?.Debug(ex, "Match HUD refresh failed.");
			}
		}

		private async Task RefreshLoopAsync(CancellationToken cancellationToken)
		{
			using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
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

				RefreshNow();
			}
		}
	}

	public static bool OpenReplayFolder(ReplayService replays, Serilog.ILogger logger)
	{
		try
		{
			var root = replays.ReplayRoot;
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
			{
				return false;
			}

			using var process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = $"\"{root}\"",
					UseShellExecute = false,
					CreateNoWindow = true,
				},
			};
			return process.Start();
		}
		catch (Exception ex)
		{
			logger.Debug(ex, "Open replay folder failed.");
			return false;
		}
	}

	public static string JoinParts(params string?[] parts) =>
		string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

	private static string? ReadString(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key)
	{
		var element = ReadElement(surface, key);
		return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
	}

	private static JsonElement ReadElement(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key) =>
		surface.Attributes.TryGetValue(key, out var element) ? element : default;

	private static bool? ReadBool(MacroDeck.Ui.Model.Surfaces.UiSurface surface, string key)
	{
		var element = ReadElement(surface, key);
		return element.ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetBoolean() : null;
	}
}

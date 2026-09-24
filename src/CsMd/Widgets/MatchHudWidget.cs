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
using CsMd.Places;
using CsMd.Config;

namespace CsMd.Widgets;

public sealed record MatchHudOptions(
	bool ShowScore,
	bool ShowHistory,
	bool ShowPlayer,
	bool ShowCharts,
	bool ShowStatus,
	bool ShowSession,
	bool ShowFeed,
	int FeedCount,
	bool Compact)
{
	public static MatchHudOptions Default { get; } = new(true, true, true, true, true, true, true, 3, false);

	public static MatchHudOptions FromData(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return Default;
		}

		return new MatchHudOptions(
			ShowScore: ReadFlag(data, "showScore", true),
			ShowHistory: ReadFlag(data, "showHistory", true),
			ShowPlayer: ReadFlag(data, "showPlayer", true),
			ShowCharts: ReadFlag(data, "showCharts", true),
			ShowStatus: ReadFlag(data, "showStatus", true),
			ShowSession: ReadFlag(data, "showSession", true),
			ShowFeed: ReadFlag(data, "showFeed", true),
			FeedCount: ReadCount(data),
			Compact: ReadFlag(data, "compactMode", false));
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
				return Math.Clamp((int)element.GetDouble(), 1, 5);
			}
			catch (Exception)
			{
			}
		}

		return Default.FeedCount;
	}
}

public sealed record RoundDot(string Key, string Glyph, string? Color, bool Latest);

public sealed record FeedItem(string Key, MacroDeck.Localization.LocalizedString Text, string? Accent);

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
	IReadOnlyList<RoundDot> HistoryDots,
	bool HasHistory,
	string NameLine,
	string PlayerTeam,
	bool Alive,
	bool HasPlayer,
	double HpFrac,
	string HpText,
	double ArmorFrac,
	string GearLine,
	MacroDeck.Localization.LocalizedString RoundLine,
	MacroDeck.Localization.LocalizedString? TopWeaponLine,
	string PlaceText,
	bool HasPlace,
	string CoordsLine,
	bool HasCoords,
	string TrackingLine,
	string BombText,
	bool HasBomb,
	UiProgressReference BombProgress,
	bool HasBombBar,
	bool Smoked,
	bool Burning,
	bool Flashed,
	bool Helmet,
	bool DefuseKit,
	int Streak,
	bool HasStreak,
	MacroDeck.Localization.LocalizedString SessionLine,
	IReadOnlyList<double> HpHistory,
	bool HasHpHistory,
	IReadOnlyList<double> DmgHistory,
	string DmgCaption,
	bool HasDmgHistory,
	IReadOnlyList<double> MoneyHistory,
	bool HasMoneyHistory,
	IReadOnlyList<FeedItem> FeedItems,
	bool HasFeed,
	int Page,
	int Kills,
	int Deaths,
	int Assists,
	int SessionHs,
	int SessionDamage,
	int Mvps,
	int Score,
	int EquipValue,
	int RoundKills,
	int RoundHs,
	int RoundDmg,
	int BestStreak,
	int TimeoutsCt,
	int TimeoutsT,
	string ElapsedLine,
	string SessionMatchTimeLine,
	string BombDetail,
	string BombSite,
	double? FacingYaw,
	int Smokes,
	int Fires,
	int Nades,
	MatchHudOptions Options)
{
	public const int PageMatch = 0;
	public const int PagePlayer = 1;
	public const int PageIntel = 2;
	public static MatchHudContent Empty { get; } = new(
		false, string.Empty, "CT", 0, "T", 0, string.Empty, string.Empty, string.Empty, false,
		[], false,
		string.Empty, string.Empty, false, false, 0, string.Empty, 0, string.Empty, Strings.Widget.Round.Line(0, 0, 0), null,
		string.Empty, false, string.Empty, false, string.Empty, string.Empty, false, new UiProgressReference { PositionMs = 0, Anchor = DateTimeOffset.UtcNow }, false,
		false, false, false, false, false, 0, false, Strings.Widget.Session.Line(0, 0, "0.00"),
		[], false, [], string.Empty, false, [], false, [], false,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty, null, 0, 0, 0,
		MatchHudOptions.Default);

	public static MatchHudContent SampleLive { get; } = new(
		true, "DE_MIRAGE · COMPETITIVE", "NAVI", 9, "FAZE", 7, "R17", "LIVE", "1:23", true,
		[new RoundDot("1", "●", "#4ADE80", false), new RoundDot("2", "●", "#4ADE80", false), new RoundDot("3", "●", "#F87171", true)], true,
		"s1mple [NAVI]", "CT", true, true, 0.87, "87", 1.0, "100 · AWP · Rifle · 5 / 30 · $4,700 · 18 / 9 / 4", Strings.Widget.Round.Line(17, 2, 250), Strings.Widget.TopWeapon.Line("AWP", 14),
		"Middle", true, "512 · -735 · -148", true, "CONSOLE", "CARRIED", false, new UiProgressReference { PositionMs = 0, Anchor = DateTimeOffset.UtcNow }, false,
		false, false, false, true, true, 4, true, Strings.Widget.Session.Line(18, 9, "2.00"),
		[0.9, 0.85, 0.87, 0.6, 0.62, 0.87], true, [0.2, 0.5, 0.3], "250 / 400", true, [0.1, 0.2, 0.29], true,
		[new FeedItem("f2", Strings.Widget.Feed.Kill("s1mple", "AWP", "Middle"), MatchHudColors.White), new FeedItem("f1", Strings.Widget.Feed.RoundWon(), MatchHudColors.Good)], true,
		0, 18, 9, 4, 11, 2450, 2, 42, 5200, 2, 1, 250, 6, 1, 0, "38:12", "25:30", "s1mple", string.Empty, 135, 1, 0, 2,
		MatchHudOptions.Default);

	public static MatchHudContent SampleBomb { get; } = new(
		true, "DE_DUST2 · COMPETITIVE", "CTs", 11, "Ts", 9, "R21", "LIVE", "0:32", true,
		[], false,
		"misuuu", "T", false, true, 0, "0", 0, "AK-47 · Rifle · 0 / 90 · $800 · 14 / 12 / 3", Strings.Widget.Round.Line(21, 0, 0), null,
		"Bombsite A", true, string.Empty, false, string.Empty, "PLANTED", true,
		new UiProgressReference { PositionMs = 8000, Anchor = DateTimeOffset.UtcNow, DurationMs = 40000, Rate = 1 }, true,
		false, false, false, false, false, 0, false, Strings.Widget.Session.Line(14, 12, "1.17"),
		[], false, [], string.Empty, false, [], false, [new FeedItem("f1", Strings.Widget.Feed.BombPlanted("B"), MatchHudColors.Bad)], true,
		0, 14, 12, 3, 9, 1980, 1, 35, 4700, 0, 0, 0, 3, 1, 1, "41:05", "12:45", "misuuu", "B", null, 2, 1, 0,
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

internal static class MatchHudColors
{
	public const string Ct = "#7DD3FC";
	public const string T = "#FCD34D";
	public const string Good = "#4ADE80";
	public const string Bad = "#F87171";
	public const string Warn = "#FBBF24";
	public const string Hot = "#FB923C";
	public const string White = "#FFFFFF";
	public const string Slate = "#94A3B8";
}

public sealed record MatchHudActions(
	Func<int, UiEventOutcome> SelectPage,
	Func<UiEventData, UiEventOutcome> SelectPageData,
	Func<UiEventData, UiEventOutcome> SwipePage,
	Func<UiEventOutcome> CyclePage,
	Func<UiEventOutcome> Simulate,
	Func<UiEventOutcome> Install);

internal static class MatchHudView
{
	// Bump when the layout changes. Node ids compose from the root key, so a new
	// generation makes old patches unmatchable and forces the host to resync a
	// clean tree instead of patching new values into a stale structure.
	internal const string TreeGeneration = "19";

	private const string CardBackground = "#262C38";
	private const string BombCardBackground = "#33222B";
	private const string AccentBackground = "#38BDF8";
	private const string AccentInk = "#0B1220";
	private const string PillNeutral = "#323A48";
	private const string PillGreen = "#123A24";
	private const string PillRed = "#3D1F1F";
	private const string PillBlue = "#1C3A4D";
	private const string PillYellow = "#3D3417";
	private const string PillHot = "#3D2317";
	private const string ChromeEdge = "#39435A";

	// Broadcast-dark depth system. These are modifier-only members (background,
	// radius, hairline border), so they merge onto the wrapped card node: older
	// readers simply draw it flatter, no fallback is owed, and no geometry
	// changes, so the tile fit budget is untouched. Never add Padding, Opacity,
	// Clip, Mask or Frame here: those promote the modifier to its own node and
	// would need a fallback per card. Never set Background on the wrapped card
	// itself: the same member twice on one node is rejected at view build.
	private static UiModifier CardChrome(string key, UiButton card) => new()
	{
		Key = key + "-deco",
		Background = UiGradient.Linear(135,
		[
			new UiGradientStop { Offset = 0, Color = "#2C3547" },
			new UiGradientStop { Offset = 0.55, Color = "#202839" },
			new UiGradientStop { Offset = 1, Color = "#161B25" },
		]),
		Radius = 0.02,
		BorderColor = UiValue.Of(ChromeEdge),
		BorderWidth = 0.003,
		Child = card,
	};

	private static UiModifier PillChrome(string key, UiButton pill) => new()
	{
		Key = key + "-deco",
		Radius = 0.03,
		Child = pill,
	};

	public static UiElement Build(UiState<MatchHudContent> content, MatchHudActions? actions = null, UiWidgetAppearanceValues? appearance = null)
	{
		var options = content.Peek().Options;
		var body = new List<UiElement>
		{
			new UiWhen
			{
				Key = "live",
				Condition = () => content.Value.Connected,
				Content = () => LiveBody(content, options, actions),
			},
			new UiWhen
			{
				Key = "idle",
				Condition = () => !content.Value.Connected,
				Content = () => IdleCard(actions),
			},
		};

		var appearanceLabel = appearance?.Label;
		if (!string.IsNullOrWhiteSpace(appearanceLabel))
		{
			var appearanceLabelColor = appearance?.LabelColor;
			body.Insert(0, new UiTextRun
			{
				Key = "appearance-label",
				Text = UiText.From(() => appearanceLabel),
				Size = UiSize.Capped(0.09, 11),
				Weight = UiComponentTextWeights.Medium,
				Color = string.IsNullOrWhiteSpace(appearanceLabelColor) ? UiValue.None<string>() : UiValue.Of(appearanceLabelColor),
				Align = UiComponentAlignments.Center,
			});
		}

		var root = new UiStack
		{
			Key = "match-hud-g" + TreeGeneration,
			Padding = 0.035,
			Gap = 0.015,
			Children = body,
		};

		var background = appearance?.BackgroundColor;
		if (string.IsNullOrWhiteSpace(background))
		{
			return root;
		}

		return new UiModifier
		{
			Key = "appearance",
			Background = UiBackground.Solid(background),
			Child = root,
		};
	}

	private static UiStack LiveBody(UiState<MatchHudContent> content, MatchHudOptions options, MatchHudActions? actions)
	{
		return new UiStack
		{
			Key = "live-body",
			Gap = 0.012,
			Children =
			[
				TabBar(content, actions),
				new UiWhen
				{
					Key = "page-match",
					Condition = () => content.Value.Page == MatchHudContent.PageMatch,
					Content = () => MatchPage(content, options, actions),
				},
				new UiWhen
				{
					Key = "page-player",
					Condition = () => content.Value.Page == MatchHudContent.PagePlayer,
					Content = () => PlayerPage(content, options),
				},
				new UiWhen
				{
					Key = "page-intel",
					Condition = () => content.Value.Page == MatchHudContent.PageIntel,
					Content = () => IntelPage(content, options),
				},
			],
		};
	}

	private static UiSegmented TabBar(UiState<MatchHudContent> content, MatchHudActions? actions)
	{
		var segments = new UiElement[]
		{
			TabSegment("match", UiIcons.Chart, Strings.Widget.Tabs.Match()),
			TabSegment("player", UiIcons.User, Strings.Widget.Tabs.Player()),
			TabSegment("intel", UiIcons.Globe, Strings.Widget.Tabs.Intel()),
		};
		return new UiSegmented
		{
			Key = "tabs",
			Selected = UiValue.From(() => content.Value.Page),
			LevelColor = UiValue.From(() => content.Value.Page switch
			{
				MatchHudContent.PagePlayer => MatchHudColors.Good,
				MatchHudContent.PageIntel => MatchHudColors.Warn,
				_ => "#38BDF8",
			}),
			MainSize = UiSize.Capped(0.075, 20),
			Events = actions is null
				? []
				: [
					UiEventHandler.On(UiComponentEvents.Change, data => actions.SelectPageData(data)),
					// A swipe on the always-visible tab bar flips pages the same way
					// a swipe on the scorebug does. Taps still select: the inner
					// press wins until the pointer travels past slop.
					UiEventHandler.On(UiComponentEvents.Swipe, data => actions.SwipePage(data)),
				],
			Children = segments,
			Fallback = new UiStack
			{
				Key = "tabs-fallback",
				Direction = UiComponentDirections.Horizontal,
				Gap = 0.015,
				Children =
				[
					TabButton("match", Strings.Widget.Tabs.Match(), MatchHudContent.PageMatch, actions),
					TabButton("player", Strings.Widget.Tabs.Player(), MatchHudContent.PagePlayer, actions),
					TabButton("intel", Strings.Widget.Tabs.Intel(), MatchHudContent.PageIntel, actions),
				],
			},
		};
	}

	private static UiStack TabSegment(string key, string icon, MacroDeck.Localization.LocalizedString label) => new()
	{
		Key = "tab-" + key,
		Direction = UiComponentDirections.Horizontal,
		Justify = UiComponentJustify.Center,
		Align = UiComponentAlignments.Center,
		Gap = 0.015,
		Children =
		[
			new UiIcon
			{
				Key = "tab-" + key + "-icon",
				Icon = icon,
				Size = UiSize.Capped(0.05, 12),
				MainSize = UiSize.Capped(0.05, 12),
				Role = UiComponentTextRoles.Primary,
			},
			new UiTextRun
			{
				Key = "tab-" + key + "-label",
				Text = UiText.FromLocalized(() => label),
				Size = UiSize.Capped(0.045, 11),
				Weight = UiComponentTextWeights.SemiBold,
				Align = UiComponentAlignments.Center,
			},
		],
	};

	private static UiButton TabButton(string key, MacroDeck.Localization.LocalizedString label, int page, MatchHudActions? actions)
	{
		var button = new UiButton
		{
			Key = "tabbtn-" + key,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Padding = 0.015,
			Children =
			[
				new UiTextRun
				{
					Key = "tabbtn-" + key + "-label",
					Text = UiText.FromLocalized(() => label),
					Size = UiSize.Capped(0.045, 11),
					Weight = UiComponentTextWeights.SemiBold,
					Align = UiComponentAlignments.Center,
				},
			],
		};
		return actions is null
			? button
			: button with { Events = [UiEventHandler.On(UiComponentEvents.Press, _ => actions.SelectPage(page))] };
	}

	private static UiModifier IdleCard(MatchHudActions? actions)
	{
		var simulate = new UiButton
		{
			Key = "idle-simulate",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Padding = 0.025,
			Background = UiValue.Of(AccentBackground),
			Children =
			[
				new UiTextRun
				{
					Key = "idle-simulate-label",
					Text = UiText.FromLocalized(() => Strings.Widget.Idle.Simulate()),
					Size = UiSize.Capped(0.06, 14),
					Weight = UiComponentTextWeights.Bold,
					Color = UiValue.Of(AccentInk),
					Align = UiComponentAlignments.Center,
				},
			],
		};
		var install = new UiButton
		{
			Key = "idle-install",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Padding = 0.025,
			Background = UiValue.Of(CardBackground),
			Children =
			[
				new UiTextRun
				{
					Key = "idle-install-label",
					Text = UiText.FromLocalized(() => Strings.Widget.Idle.Install()),
					Size = UiSize.Capped(0.06, 14),
					Weight = UiComponentTextWeights.Bold,
					Color = UiValue.Of(MatchHudColors.Ct),
					Align = UiComponentAlignments.Center,
				},
			],
		};
		return new UiModifier
		{
			Key = "idle-deco",
			Background = UiGradient.Linear(135,
			[
				new UiGradientStop { Offset = 0, Color = "#1C2942" },
				new UiGradientStop { Offset = 1, Color = "#131A29" },
			]),
			Radius = 0.03,
			BorderColor = UiValue.Of("#33507A"),
			BorderWidth = 0.004,
			Child = new UiButton
			{
				Key = "idle",
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Padding = 0.04,
				Gap = 0.025,
			Children =
			[
			new UiIcon
			{
				Key = "idle-icon",
				Icon = UiIcons.Crosshair,
				Size = UiSize.Capped(0.14, 36),
				MainSize = UiSize.Capped(0.14, 36),
				Color = UiValue.Of(MatchHudColors.Ct),
			},
		new UiTextRun
		{
			Key = "idle-title",
			Text = UiText.FromLocalized(() => Strings.Widget.Idle.Title()),
			Size = UiSize.Capped(0.08, 20),
			Weight = UiComponentTextWeights.Bold,
			Align = UiComponentAlignments.Center,
		},
		PillChrome("idle-status", new UiButton
		{
			Key = "idle-status",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Background = UiValue.Of(PillNeutral),
			BorderStyle = UiValue.Of(UiComponentBorderStyles.Breathing),
			BorderColor = UiValue.Of(MatchHudColors.Slate),
			Padding = 0.008,
			Children =
			[
				new UiTextRun
				{
					Key = "idle-status-label",
					Text = UiText.FromLocalized(() => Strings.Widget.Idle.Status()),
					Size = UiSize.Capped(0.06, 10),
					Weight = UiComponentTextWeights.Medium,
					Color = UiValue.Of(MatchHudColors.Slate),
					Align = UiComponentAlignments.Center,
				},
			],
		}),
			new UiShape
			{
				Key = "idle-rule",
				Shape = UiComponentShapes.Capsule,
				Color = UiValue.Of(MatchHudColors.Slate),
				MainSize = 0.006,
			},
				new UiTextRun
				{
					Key = "idle-caption",
					Text = UiText.FromLocalized(() => Strings.Widget.NoData.Caption()),
					Size = UiSize.Capped(0.045, 11),
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
						actions is null ? install : install with { Events = [UiEventHandler.On(UiComponentEvents.Press, _ => actions.Install())] },
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
			},
		};
	}

	private static UiStack MatchPage(UiState<MatchHudContent> content, MatchHudOptions options, MatchHudActions? actions)
	{
		var body = new List<UiElement>();

		if (options.ShowScore)
		{
			body.Add(Scorebug(content, options, actions));
		}

		if (options.ShowStatus)
		{
			body.Add(new UiWhen
			{
				Key = "bomb-when",
				Condition = () => content.Value.HasBomb || content.Value.HasBombBar,
				Content = () => BombCard(content),
			});
			body.Add(MetaRows(content));
			body.Add(MatchPills(content));
		}

		return new UiStack
		{
			Key = "match-page",
			Gap = 0.015,
			Children = body,
		};
	}

	private static UiModifier Scorebug(UiState<MatchHudContent> content, MatchHudOptions options, MatchHudActions? actions)
	{
		var big = options.Compact ? UiSize.Capped(0.15, 24) : UiSize.Capped(0.19, 34);
		var body = new List<UiElement>
		{
			new UiStack
			{
				Key = "scores",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.SpaceBetween,
				Children =
				[
					SideScore(content, big, ct: true),
					new UiStack
					{
						Key = "mid",
						Fill = true,
						Gap = 0.008,
					Children =
					[
						// Round and phase share one centered row ("R17 · FREEZETIME")
						// instead of stacking: the mid column no longer breathes
						// when the phase appears, and phase states gain a row.
						new UiStack
						{
							Key = "roundline",
							Direction = UiComponentDirections.Horizontal,
							Justify = UiComponentJustify.Center,
							Align = UiComponentAlignments.Center,
							Gap = 0.012,
							Children =
							[
								new UiTextRun
								{
									Key = "round",
									Text = UiText.From(() => content.Value.RoundText),
									Size = UiSize.Capped(0.08, 10),
									Weight = UiComponentTextWeights.Bold,
									Align = UiComponentAlignments.Center,
								},
								new UiWhen
								{
									Key = "phase-when",
									Condition = () => !string.IsNullOrWhiteSpace(content.Value.MapPhase) && content.Value.MapPhase != "LIVE",
									Content = () => new UiTextRun
									{
										Key = "phase",
										Text = UiText.From(() => content.Value.MapPhase.ToUpperInvariant()),
										Size = UiSize.Capped(0.08, 10),
										MinSize = UiSize.Capped(0.05, 8),
										Role = UiComponentTextRoles.Muted,
										Align = UiComponentAlignments.Center,
									},
								},
							],
						},
							new UiWhen
							{
								Key = "clock-when",
								Condition = () => content.Value.HasTimer,
								Content = () => new UiTextRun
								{
									Key = "clock",
									Text = UiText.From(() => content.Value.PhaseTime),
									Size = UiSize.Capped(0.105, 15),
									Weight = UiComponentTextWeights.Bold,
									Digits = UiValue.Of(4.0),
									Align = UiComponentAlignments.Center,
								},
							},
							new UiWhen
							{
								Key = "live-when",
								Condition = () => content.Value.MapPhase == "LIVE",
								Content = () => PillChrome("live-pill", new UiButton
								{
									Key = "live-pill",
									Justify = UiComponentJustify.Center,
									Align = UiComponentAlignments.Center,
									Background = UiValue.Of(PillRed),
									BorderStyle = UiValue.Of(UiComponentBorderStyles.Heartbeat),
									BorderColor = UiValue.Of(MatchHudColors.Bad),
									Padding = 0.008,
									Children =
									[
									new UiTextRun
									{
										Key = "live-pill-label",
										Text = UiText.FromLocalized(() => Strings.Widget.State.Live()),
										Size = UiSize.Capped(0.07, 10),
										MinSize = UiSize.Capped(0.05, 8),
										Weight = UiComponentTextWeights.Bold,
										Color = UiValue.Of(MatchHudColors.Bad),
										Align = UiComponentAlignments.Center,
									},
									],
								}),
							},
						new UiWhen
						{
							Key = "final-when",
							Condition = () => string.Equals(content.Value.MapPhase, "GAMEOVER", StringComparison.OrdinalIgnoreCase),
							Content = () => PillChrome("final-pill", new UiButton
							{
								Key = "final-pill",
								Justify = UiComponentJustify.Center,
								Align = UiComponentAlignments.Center,
								Background = UiValue.Of(PillYellow),
								BorderStyle = UiValue.Of(UiComponentBorderStyles.Breathing),
								BorderColor = UiValue.Of(MatchHudColors.Warn),
								Padding = 0.008,
								Children =
								[
									new UiTextRun
									{
										Key = "final-label",
										Text = UiText.FromLocalized(() => Strings.Widget.Match.Winner(MatchHudWidget.MatchWinner(content.Value.CtScore, content.Value.TScore, content.Value.CtName, content.Value.TName))),
										Size = UiSize.Capped(0.075, 10),
										MinSize = UiSize.Capped(0.05, 8),
										Weight = UiComponentTextWeights.Bold,
										Color = UiValue.Of(MatchHudColors.Warn),
										Align = UiComponentAlignments.Center,
									},
								],
							}),
						},
						new UiWhen
						{
							Key = "mp-when",
							Condition = () => MatchHudWidget.MatchPoint(content.Value.CtScore, content.Value.TScore, content.Value.CtName, content.Value.TName) is not null,
								Content = () => PillChrome("mp-pill", new UiButton
								{
									Key = "mp-pill",
									Justify = UiComponentJustify.Center,
									Align = UiComponentAlignments.Center,
									Background = UiValue.Of(PillYellow),
									BorderStyle = UiValue.Of(UiComponentBorderStyles.Blink),
									BorderColor = UiValue.Of(MatchHudColors.Warn),
									Padding = 0.008,
									Children =
									[
									new UiTextRun
									{
										Key = "mp-label",
										Text = UiText.FromLocalized(() => MatchHudWidget.MatchPointText(content.Value.CtScore, content.Value.TScore, content.Value.CtName, content.Value.TName)),
										Size = UiSize.Capped(0.075, 10),
										MinSize = UiSize.Capped(0.05, 8),
										Weight = UiComponentTextWeights.Bold,
										Color = UiValue.Of(MatchHudColors.Warn),
										Align = UiComponentAlignments.Center,
									},
									],
								}),
							},
						],
					},
					SideScore(content, big, ct: false),
				],
			},
		};

		body.Add(new UiWhen
		{
			Key = "history-when",
			Condition = () => options.ShowHistory && content.Value.HasHistory,
			// Dots ride a hairline rail: the track draws the timeline the
			// dots belong to, instead of floating on void. The rail is
			// thinner than the dots, so the layer measures like the row.
			Content = () => new UiLayer
			{
				Key = "history",
				MainSize = UiSize.Capped(0.09, 13),
				Children =
				[
					new UiRangeBar
					{
						Key = "history-rail",
						Start = UiValue.Of(0.0),
						End = UiValue.Of(1.0),
						StartColor = UiValue.Of(ChromeEdge),
						EndColor = UiValue.Of(ChromeEdge),
						Thickness = 0.008,
					},
					new UiStack
					{
						Key = "history-dots",
						Direction = UiComponentDirections.Horizontal,
						Justify = UiComponentJustify.Center,
						Align = UiComponentAlignments.Center,
						Gap = 0.015,
						Children =
						[
							new UiRepeat<RoundDot>
							{
								Key = "dots",
								Items = UiValue.From(() => content.Value.HistoryDots),
								KeySelector = static dot => dot.Key,
								Template = static (dot, key) => new UiTextRun
								{
									Key = key,
									Text = UiText.From(() => dot.Glyph),
									Size = dot.Latest ? UiSize.Capped(0.09, 13) : UiSize.Capped(0.075, 11),
									Weight = UiComponentTextWeights.Bold,
									Color = dot.Color is null ? UiValue.None<string>() : UiValue.Of(dot.Color),
									Align = UiComponentAlignments.Center,
								},
							},
						],
					},
				],
			},
		});
		body.Add(new UiWhen
		{
			Key = "map-when",
			Condition = () => !string.IsNullOrWhiteSpace(content.Value.MapLine),
			Content = () => new UiStack
			{
				Key = "map",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = 0.012,
				Children =
				[
					new UiIcon
					{
						Key = "map-pin",
						Icon = UiIcons.Pin,
						Size = UiSize.Capped(0.04, 8),
						MainSize = UiSize.Capped(0.04, 8),
						Role = UiComponentTextRoles.Muted,
					},
					new UiTextRun
					{
						Key = "map-line",
						Text = UiText.From(() => content.Value.MapLine),
						Size = UiSize.Capped(0.075, 9),
						MinSize = UiSize.Capped(0.055, 7),
						Role = UiComponentTextRoles.Muted,
						Align = UiComponentAlignments.Center,
					},
				],
			},
		});

		return new UiModifier
		{
			Key = "scorebug-deco",
			Background = UiGradient.Linear(135,
			[
				new UiGradientStop { Offset = 0, Color = "#202839" },
				new UiGradientStop { Offset = 1, Color = "#141A26" },
			]),
			Radius = 0.025,
			BorderColor = UiValue.From(() => content.Value.HasBombBar
				? MatchHudColors.Bad
				: content.Value.Flashed
					? MatchHudColors.White
					: content.Value.Alive && content.Value.HpFrac <= 0.25
						? MatchHudColors.Bad
						: ChromeEdge),
			BorderWidth = 0.004,
			AccessibilityLabel = UiText.FromLocalized(() => Strings.Widget.Scorebug.Accessibility(
				content.Value.CtName,
				content.Value.CtScore,
				content.Value.TName,
				content.Value.TScore,
				content.Value.RoundText)),
			Child = new UiButton
			{
				Key = "scorebug",
				Justify = UiComponentJustify.Start,
				Padding = 0.015,
				Gap = 0.015,
				Events = actions is null
					? []
					: [
						UiEventHandler.On(UiComponentEvents.Press, _ => actions.CyclePage()),
						UiEventHandler.On(UiComponentEvents.Swipe, data => actions.SwipePage(data)),
					],
				Children = body,
			},
		};
	}

	private static UiModifier StatCard(string key, MacroDeck.Localization.LocalizedString caption, Func<string> value, Func<string>? color = null)
	{
		var valueRun = new UiTextRun
		{
			Key = key + "-value",
			Text = UiText.From(value),
			Size = UiSize.Capped(0.06, 14),
			Weight = UiComponentTextWeights.Bold,
			Color = color is null ? UiValue.None<string>() : UiValue.From(color),
			Digits = UiValue.Of(4.0),
			Align = UiComponentAlignments.Center,
		};
		return CardChrome(key, new UiButton
		{
			Key = key,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Padding = 0.012,
			Gap = 0.004,
			Children =
			[
				new UiTextRun
				{
					Key = key + "-caption",
					Text = UiText.FromLocalized(() => caption),
					Size = UiSize.Capped(0.032, 8),
					Weight = UiComponentTextWeights.Medium,
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
				valueRun,
			],
		});
	}

	private static UiStack MetaRows(UiState<MatchHudContent> content) => new()
	{
		Key = "meta-rows",
		Gap = 0.01,
		Children =
		[
			new UiStack
			{
				Key = "meta-row1",
				Direction = UiComponentDirections.Horizontal,
				Gap = 0.015,
				Children =
				[
					MetaStrip("meta-round", Strings.Widget.Cards.Round(), new UiTextRun
					{
						Key = "meta-round-value",
						Text = UiText.FromLocalized(() => content.Value.RoundLine),
						Size = UiSize.Capped(0.055, 13),
						Weight = UiComponentTextWeights.SemiBold,
						Align = UiComponentAlignments.Start,
					},
					UiIcons.Chart),
					MetaStrip("meta-streak", Strings.Widget.Cards.Streak(), new UiTextRun
					{
						Key = "meta-streak-value",
						Text = UiText.FromLocalized(() => Strings.Widget.Streak.Best(content.Value.Streak, content.Value.BestStreak)),
						Size = UiSize.Capped(0.055, 13),
						Weight = UiComponentTextWeights.SemiBold,
						Align = UiComponentAlignments.Start,
					},
					UiIcons.Zap),
				],
			},
			new UiStack
			{
				Key = "meta-row2",
				Direction = UiComponentDirections.Horizontal,
				Gap = 0.015,
				Children =
				[
					MetaStrip("meta-timeouts", Strings.Widget.Cards.Timeouts(), new UiTextRun
					{
						Key = "meta-timeouts-value",
						Text = UiText.FromLocalized(() => Strings.Widget.Timeouts.Line(content.Value.TimeoutsCt, content.Value.TimeoutsT)),
						Size = UiSize.Capped(0.055, 13),
						Weight = UiComponentTextWeights.SemiBold,
						Align = UiComponentAlignments.Start,
					},
					UiIcons.Pause),
					MetaStrip("meta-time", Strings.Widget.Cards.MatchTime(), new UiTextRun
					{
						Key = "meta-time-value",
						Text = UiText.From(() => content.Value.ElapsedLine),
						Size = UiSize.Capped(0.055, 13),
						Weight = UiComponentTextWeights.SemiBold,
						Align = UiComponentAlignments.Start,
					},
					UiIcons.ClockType),
				],
			},
		],
	};

	private static UiModifier MetaStrip(string key, MacroDeck.Localization.LocalizedString caption, UiTextRun value, string? icon = null) => CardChrome(key, new UiButton
	{
		Key = key,
		Direction = UiComponentDirections.Horizontal,
		Align = UiComponentAlignments.Center,
		Gap = 0.015,
		Fill = true,
		Padding = 0.012,
		Children =
		[
			..(icon is null
				? []
				: new UiElement[]
				{
					new UiIcon
					{
						Key = key + "-icon",
						Icon = icon,
						Size = UiSize.Capped(0.045, 11),
						MainSize = UiSize.Capped(0.045, 11),
						Role = UiComponentTextRoles.Muted,
					},
				}),
			new UiTextRun
			{
				Key = key + "-caption",
				Text = UiText.FromLocalized(() => caption),
				Size = UiSize.Capped(0.032, 8),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Start,
			},
			value,
		],
	});

	private static UiButton BombCard(UiState<MatchHudContent> content)
	{
		var body = new List<UiElement>
		{
			// Icon pinned left, text centered on the true tile axis: centering
			// an icon+text row as one group pushes the text off-center, which
			// reads as a mistake at broadcast sizes. Layer children each get
			// the whole box, so the text stays centered and the icon docks.
			new UiLayer
			{
				Key = "bomb-head",
				MainSize = UiSize.Capped(0.075, 17),
				Children =
				[
					new UiTextRun
					{
						Key = "bomb-state",
						Text = UiText.From(() => content.Value.BombText),
						Size = UiSize.Capped(0.075, 17),
						Weight = UiComponentTextWeights.Bold,
						Color = UiValue.Of(MatchHudColors.Bad),
						Align = UiComponentAlignments.Center,
					},
					new UiStack
					{
						Key = "bomb-head-iconbox",
						Direction = UiComponentDirections.Horizontal,
						Align = UiComponentAlignments.Center,
						Children =
						[
							new UiIcon
							{
								Key = "bomb-icon",
								Icon = UiIcons.AlertTriangle,
								Size = UiSize.Capped(0.075, 17),
								MainSize = UiSize.Capped(0.075, 17),
								Color = UiValue.Of(MatchHudColors.Bad),
							},
						],
					},
				],
				Fallback = new UiTextRun
				{
					Key = "bomb-state-fb",
					Text = UiText.From(() => content.Value.BombText),
					Size = UiSize.Capped(0.075, 17),
					Weight = UiComponentTextWeights.Bold,
					Color = UiValue.Of(MatchHudColors.Bad),
					Align = UiComponentAlignments.Center,
				},
			},
			new UiWhen
			{
				Key = "bomb-detail-when",
				Condition = () => !string.IsNullOrWhiteSpace(content.Value.BombDetail),
				Content = () => new UiTextRun
				{
					Key = "bomb-detail",
					Text = UiText.From(() => content.Value.BombDetail),
					Size = UiSize.Capped(0.05, 12),
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
		};
		body.Add(new UiWhen
		{
			Key = "bombbar-when",
			Condition = () => content.Value.HasBombBar,
			Content = () => BombPanel(content),
		});

		return new UiButton
		{
			Key = "bomb-card",
			Background = UiValue.From(() => content.Value.HasBombBar ? BombCardBackground : CardBackground),
			BorderStyle = UiValue.Optional(() => content.Value.HasBombBar
				? UiValue.Of(BombSecondsRemaining(content.Value.BombProgress) < 10
					? UiComponentBorderStyles.Blink
					: UiComponentBorderStyles.Breathing)
				: UiValue.None<string>()),
			BorderColor = UiValue.Of(MatchHudColors.Bad),
			Padding = 0.015,
			Gap = 0.01,
			Children = body,
		};
	}

	private static double BombSecondsRemaining(UiProgressReference progress)
	{
		if (progress.DurationMs is not { } total || total <= 0)
		{
			return double.MaxValue;
		}

		var elapsed = progress.PositionMs + (DateTimeOffset.UtcNow - progress.Anchor).TotalMilliseconds * (progress.Rate ?? 0);
		return Math.Max(0, (total - elapsed) / 1000);
	}

	private static UiStack MatchPills(UiState<MatchHudContent> content) => new()
	{
		Key = "match-pills",
		Direction = UiComponentDirections.Horizontal,
		Justify = UiComponentJustify.Center,
		Gap = 0.015,
		Children =
		[
			AlivePill(content, "state"),
			TextPill(content, "place", () => content.Value.HasPlace, () => content.Value.PlaceText),
			TextPill(content, "bomb", () => content.Value.HasBomb, () => content.Value.BombText,
				() => PillRed, () => MatchHudColors.Bad),
			LocalizedPill("streak", () => content.Value.HasStreak,
				() => Strings.Widget.Streak.Label(content.Value.Streak),
				() => content.Value.Streak >= 5 ? PillHot : PillYellow,
				() => content.Value.Streak >= 5 ? MatchHudColors.Hot : MatchHudColors.Warn),
		],
	};

	private static UiModifier SideScore(UiState<MatchHudContent> content, UiSize big, bool ct) => new UiModifier
	{
		Key = ct ? "ct-side-deco" : "t-side-deco",
		// A faint team wash behind each column: broadcast scorebugs sit each
		// side in its own color zone. Modifier-only, so the equal-thirds
		// measure below is untouched and the middle column stays centered.
		Background = UiGradient.Linear(ct ? 200 : 160,
		[
			new UiGradientStop { Offset = 0, Color = ct ? "#1B2C3E" : "#2E2A18" },
			new UiGradientStop { Offset = 1, Color = "#1A2030" },
		]),
		Radius = 0.02,
		Child = new UiButton
		{
			Key = ct ? "ct-side" : "t-side",
			// Fixed equal thirds: both sides always measure the same, so the middle
			// column lands on the true tile center and both team bars span the same
			// width no matter how long the team names are. Real scoreboard tags fit
			// easily; longer names shrink into the slot instead of stretching it.
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			MainSize = 0.30,
			Gap = 0.008,
			Children =
			[
				new UiTextRun
				{
					Key = ct ? "ct-score" : "t-score",
					Text = UiText.From(() => ct
						? content.Value.CtScore.ToString(System.Globalization.CultureInfo.InvariantCulture)
						: content.Value.TScore.ToString(System.Globalization.CultureInfo.InvariantCulture)),
					Size = big,
					Weight = UiComponentTextWeights.Bold,
					Color = UiValue.Of(ct ? MatchHudColors.Ct : MatchHudColors.T),
					Digits = UiValue.Of(2.0),
					Align = UiComponentAlignments.Center,
				},
				new UiWhen
				{
					Key = ct ? "ct-name-when" : "t-name-when",
					Condition = () => !string.IsNullOrWhiteSpace(ct ? content.Value.CtName : content.Value.TName),
					Content = () => new UiTextRun
					{
							Key = ct ? "ct-name" : "t-name",
							Text = UiText.From(() => ct ? content.Value.CtName : content.Value.TName),
							Size = UiSize.Capped(0.07, 11),
							MinSize = UiSize.Capped(0.05, 8),
							Weight = UiComponentTextWeights.SemiBold,
						Color = UiValue.Of(MatchHudColors.White),
						Align = UiComponentAlignments.Center,
					},
				},
				new UiShape
				{
					Key = ct ? "ct-bar" : "t-bar",
					Shape = UiComponentShapes.Capsule,
					Color = UiValue.Of(ct ? MatchHudColors.Ct : MatchHudColors.T),
					MainSize = 0.01,
				},
			],
		},
	};

	private static UiStack PlayerPage(UiState<MatchHudContent> content, MatchHudOptions options)
	{
		var body = new List<UiElement>();

		if (options.ShowPlayer)
		{
			body.Add(new UiWhen
			{
				Key = "player-when",
				Condition = () => content.Value.HasPlayer,
				Content = () => PlayerHero(content, options),
			});
		}

		body.Add(StatGrid(content));

		if (options.ShowCharts && !options.Compact)
		{
			body.Add(new UiWhen
			{
				Key = "charts-when",
				Condition = () => content.Value.HasHpHistory || content.Value.HasDmgHistory || content.Value.HasMoneyHistory,
				Content = () => Charts(content),
			});
		}

		return new UiStack
		{
			Key = "player-page",
			Gap = 0.015,
			Children = body,
		};
	}

	private static UiStack PlayerHero(UiState<MatchHudContent> content, MatchHudOptions options)
	{
		var body = new List<UiElement>
		{
			new UiModifier
			{
				Key = "hp-deco",
				AccessibilityLabel = UiText.FromLocalized(() => Strings.Widget.Hero.HealthAccessibility(content.Value.HpText)),
				Child = new UiLayer
				{
					Key = "hp",
					MainSize = UiSize.Capped(0.27, 64),
				Children =
				[
					new UiStack
					{
						Key = "hp-ring",
						Fill = true,
						Align = UiComponentAlignments.Center,
						Justify = UiComponentJustify.Center,
						Children =
						[
							new UiGauge
							{
								Key = "hp-gauge",
								Level = UiValue.From(() => content.Value.HpFrac),
								LevelColor = UiValue.From(() => HpColor(content.Value.HpFrac)),
								StartAngle = 0,
								EndAngle = 360,
								Thickness = UiSize.Capped(0.05, 12),
								MainSize = UiSize.Capped(0.27, 64),
								Fallback = new UiRangeBar
								{
									Key = "hp-gauge-fallback",
									Start = UiValue.Of(0.0),
									End = UiValue.From(() => content.Value.HpFrac),
									StartColor = UiValue.From(() => HpColor(content.Value.HpFrac)),
									EndColor = UiValue.From(() => HpColor(content.Value.HpFrac)),
									Thickness = 0.035,
								},
							},
						],
					},
					new UiStack
					{
						Key = "hp-num",
						Fill = true,
						Align = UiComponentAlignments.Center,
						Justify = UiComponentJustify.Center,
						Children =
						[
							new UiTextRun
							{
								Key = "hp-line",
								Text = UiText.From(() => content.Value.HpText),
								Size = options.Compact ? UiSize.Capped(0.09, 16) : UiSize.Capped(0.09, 22),
								Weight = UiComponentTextWeights.Bold,
								Color = UiValue.From(() => content.Value.HpFrac <= 0.25 ? MatchHudColors.Bad : MatchHudColors.White),
							Digits = UiValue.Of(3.0),
								Align = UiComponentAlignments.Center,
							},
						],
					},
				],
			},
			},
			new UiWhen
			{
				Key = "player-name-when",
				Condition = () => !string.IsNullOrWhiteSpace(content.Value.NameLine),
				Content = () => new UiTextRun
				{
						Key = "player-name",
						Text = UiText.From(() => content.Value.NameLine),
						Size = options.Compact ? UiSize.Capped(0.09, 12) : UiSize.Capped(0.095, 14),
						MinSize = UiSize.Capped(0.07, 10),
					Weight = UiComponentTextWeights.SemiBold,
					Color = UiValue.From(() => TeamColor(content.Value.PlayerTeam)),
					Align = UiComponentAlignments.Center,
				},
			},
			new UiStack
			{
				Key = "hero-pills",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = 0.02,
				Children =
				[
					TextPill(content, "hero-team",
						() => !string.IsNullOrWhiteSpace(content.Value.PlayerTeam),
						() => content.Value.PlayerTeam,
						() => content.Value.PlayerTeam == "CT" ? PillBlue : content.Value.PlayerTeam == "T" ? PillYellow : PillNeutral,
						() => TeamColor(content.Value.PlayerTeam)),
					AlivePill(content, "hero-state"),
					LocalizedPill("hero-low", () => content.Value.Alive && content.Value.HpFrac is > 0 and <= 0.25, Strings.Widget.State.Low,
						() => PillRed, () => MatchHudColors.Bad),
					LocalizedPill("hero-helm", () => content.Value.Helmet, Strings.Widget.Effects.Helmet,
						() => PillBlue, () => MatchHudColors.Ct),
					LocalizedPill("hero-defuse", () => content.Value.DefuseKit, Strings.Widget.Effects.DefuseKit,
						() => PillYellow, () => MatchHudColors.Warn),
				],
			},
			new UiWhen
			{
				Key = "armor-bar-when",
				Condition = () => content.Value.ArmorFrac > 0,
				Content = () => new UiRangeBar
				{
					Key = "armor-bar",
					Start = UiValue.Of(0.0),
					End = UiValue.From(() => content.Value.ArmorFrac),
					StartColor = UiValue.Of("#93C5FD"),
					EndColor = UiValue.Of("#93C5FD"),
					Thickness = 0.022,
				},
			},
			new UiWhen
			{
				Key = "gear-when",
				Condition = () => !string.IsNullOrWhiteSpace(content.Value.GearLine),
				Content = () => new UiStack
				{
					Key = "gear",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Center,
					Gap = 0.012,
					Children =
					[
						new UiIcon
						{
							Key = "gear-icon",
							Icon = UiIcons.Crosshair,
							Size = UiSize.Capped(0.04, 8),
							MainSize = UiSize.Capped(0.04, 8),
							Role = UiComponentTextRoles.Muted,
						},
						new UiTextRun
						{
							Key = "gear-line",
							Text = UiText.From(() => content.Value.GearLine),
							Size = UiSize.Capped(0.075, 9),
							MinSize = UiSize.Capped(0.055, 7),
							Role = UiComponentTextRoles.Muted,
							Align = UiComponentAlignments.Center,
						},
					],
				},
			},
			new UiTextRun
			{
				Key = "round-line",
				Text = UiText.FromLocalized(() => content.Value.RoundLine),
				Size = UiSize.Capped(0.07, 8),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
			},
			new UiWhen
			{
				Key = "topweapon-when",
				Condition = () => content.Value.TopWeaponLine is not null,
				Content = () => new UiTextRun
				{
					Key = "topweapon",
					Text = UiText.FromLocalized(() => content.Value.TopWeaponLine ?? Strings.Widget.TopWeapon.Line(string.Empty, 0)),
					Size = UiSize.Capped(0.07, 8),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
		};

		return new UiStack
		{
			Key = "player",
			Gap = 0.016,
			Children = body,
		};
	}

	private static UiGrid StatGrid(UiState<MatchHudContent> content) => new()
	{
		Key = "stat-grid",
		Columns = UiValue.Of(4),
		Gap = 0.015,
		MainSize = UiSize.Capped(0.3, 68),
		Children =
		[
			StatCard("stat-k", Strings.Widget.Cards.Kills(), () => content.Value.Kills.ToString(System.Globalization.CultureInfo.InvariantCulture),
				() => content.Value.Kills > content.Value.Deaths ? MatchHudColors.Good : MatchHudColors.White),
			StatCard("stat-d", Strings.Widget.Cards.Deaths(), () => content.Value.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture)),
			StatCard("stat-a", Strings.Widget.Cards.Assists(), () => content.Value.Assists.ToString(System.Globalization.CultureInfo.InvariantCulture)),
			StatCard("stat-hs", Strings.Widget.Cards.Headshots(), () => content.Value.SessionHs.ToString(System.Globalization.CultureInfo.InvariantCulture)),
			StatCard("stat-dmg", Strings.Widget.Cards.Damage(), () => content.Value.SessionDamage.ToString(System.Globalization.CultureInfo.InvariantCulture)),
			StatCard("stat-mvp", Strings.Widget.Cards.Mvps(), () => content.Value.Mvps.ToString(System.Globalization.CultureInfo.InvariantCulture),
				() => content.Value.Mvps > 0 ? MatchHudColors.Warn : MatchHudColors.White),
			StatCard("stat-score", Strings.Widget.Cards.Score(), () => content.Value.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)),
			StatCard("stat-equip", Strings.Widget.Cards.Equip(), () => MatchHudContent.FormatMoney(content.Value.EquipValue),
				() => content.Value.EquipValue < 1000 ? MatchHudColors.Warn : MatchHudColors.White),
		],
	};

	private static string HpColor(double frac) =>
		frac > 0.5 ? MatchHudColors.Good : frac > 0.25 ? MatchHudColors.Warn : MatchHudColors.Bad;

	private static string TeamColor(string team) =>
		team == "CT" ? MatchHudColors.Ct : team == "T" ? MatchHudColors.T : MatchHudColors.White;

	private static UiGrid Charts(UiState<MatchHudContent> content)
	{
		return new UiGrid
		{
			Key = "charts",
			Columns = UiValue.Of(3),
			Gap = 0.02,
			MainSize = UiSize.Capped(0.12, 26),
			Children =
			[
				ChartBox(content, "hp-chart", MatchHudColors.Good, static c => c.HpHistory, null),
				ChartBox(content, "dmg-chart", MatchHudColors.T, static c => c.DmgHistory, static c => c.DmgCaption),
				ChartBox(content, "money-chart", MatchHudColors.Ct, static c => c.MoneyHistory, null),
			],
			Fallback = new UiStack
			{
				Key = "charts-fallback",
				Direction = UiComponentDirections.Horizontal,
				Gap = 0.04,
				Children =
				[
					FilledBox("hp-chart-fallback", ChartBox(content, "hp-chart-fb", MatchHudColors.Good, static c => c.HpHistory, null)),
					FilledBox("dmg-chart-fallback", ChartBox(content, "dmg-chart-fb", MatchHudColors.T, static c => c.DmgHistory, static c => c.DmgCaption)),
					FilledBox("money-chart-fallback", ChartBox(content, "money-chart-fb", MatchHudColors.Ct, static c => c.MoneyHistory, null)),
				],
			},
		};
	}

	private static UiStack FilledBox(string key, UiElement child) => new UiStack
	{
		Key = key,
		Fill = true,
		Children = [child],
	};

	private static UiWhen ChartBox(
		UiState<MatchHudContent> content,
		string prefix,
		string color,
		Func<MatchHudContent, IReadOnlyList<double>> points,
		Func<MatchHudContent, string>? caption)
	{
		var children = new List<UiElement>
		{
			new UiChart
			{
				Key = prefix,
				Points = UiValue.From(() => points(content.Value)),
						Color = UiValue.Of(color),
						Thickness = 0.02,
						MainSize = UiSize.Capped(0.07, 16),
			},
		};

		if (caption is not null)
		{
				children.Add(new UiWhen
				{
					Key = prefix + "-caption-when",
					Condition = () => !string.IsNullOrWhiteSpace(caption(content.Value)),
					Content = () => new UiTextRun
					{
					Key = prefix + "-caption",
					Text = UiText.From(() => caption(content.Value)),
					Size = UiSize.Capped(0.07, 8),
						Role = UiComponentTextRoles.Muted,
						Align = UiComponentAlignments.Center,
					},
				});
		}

		return new UiWhen
		{
			Key = prefix + "-box-when",
			Condition = () => points(content.Value).Count > 0,
			Content = () => new UiStack
			{
				Key = prefix + "-box",
				Children = children,
			},
		};
	}

	private static UiStack IntelPage(UiState<MatchHudContent> content, MatchHudOptions options)
	{
		var body = new List<UiElement>
		{
			new UiWhen
			{
				Key = "compass-when",
				Condition = () => content.Value.HasPlace || content.Value.HasCoords || content.Value.FacingYaw.HasValue,
				Content = () => CompassCard(content),
			},
		};

		if (options.ShowStatus)
		{
			body.Add(BattlefieldCard(content));
		}

		if (options.ShowFeed)
		{
			body.Add(EventsCard(content));
		}

		// No tracking microline: whenever TrackingLine is non-empty the compass
		// card above already shows the same coordinates, so a second row would
		// only repeat them.
		if (options.ShowSession && !options.Compact)
		{
			body.Add(new UiTextRun
			{
				Key = "session",
				Text = UiText.FromLocalized(() => content.Value.SessionLine),
				Size = UiSize.Capped(0.075, 9),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
				Wrap = true,
				MaxLines = 2,
			});
		}

		return new UiStack
		{
			Key = "intel-page",
			Gap = 0.015,
			Children = body,
		};
	}

	private static UiModifier CompassCard(UiState<MatchHudContent> content) => CardChrome("compass-card", new UiButton
	{
		Key = "compass-card",
		Padding = 0.015,
		Children =
		[
			new UiStack
			{
				Key = "compass",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Gap = 0.02,
				Children =
				[
					new UiWhen
					{
						Key = "compass-needle-when",
						Condition = () => content.Value.FacingYaw.HasValue,
						// A smooth needle on new readers, the octant glyph as the
						// fallback: yaw degrees clockwise match the arrow glyph
						// convention exactly (0 is up, 90 is right).
						Content = () => new UiTransform
						{
							Key = "compass-needle",
							Rotation = UiValue.From(() => NeedleRotation(content.Value.FacingYaw)),
							Children =
							[
								new UiTextRun
								{
									Key = "compass-needle-arrow",
									Text = UiText.From(() => "↑"),
									Size = UiSize.Capped(0.09, 24),
									Weight = UiComponentTextWeights.Bold,
									Color = UiValue.Of(MatchHudColors.Good),
									Align = UiComponentAlignments.Center,
								},
							],
							Fallback = new UiTextRun
							{
								Key = "compass-arrow",
								Text = UiText.From(() => MatchHudWidget.YawArrow(content.Value.FacingYaw)),
								Size = UiSize.Capped(0.09, 24),
								Weight = UiComponentTextWeights.Bold,
								Color = UiValue.Of(MatchHudColors.Good),
								Align = UiComponentAlignments.Center,
							},
						},
					},
					new UiStack
					{
						Key = "compass-text",
						Fill = true,
						Gap = 0.004,
						Children =
						[
							new UiTextRun
							{
								Key = "compass-caption",
								Text = UiText.FromLocalized(() => Strings.Widget.Cards.Position()),
								Size = UiSize.Capped(0.032, 8),
								Role = UiComponentTextRoles.Muted,
								Align = UiComponentAlignments.Start,
							},
							new UiWhen
							{
								Key = "compass-place-when",
								Condition = () => content.Value.HasPlace,
								Content = () => new UiTextRun
								{
									Key = "compass-place",
									Text = UiText.From(() => content.Value.PlaceText),
									Size = UiSize.Capped(0.055, 13),
									MinSize = UiSize.Capped(0.04, 10),
									Weight = UiComponentTextWeights.SemiBold,
									Align = UiComponentAlignments.Start,
								},
							},
						new UiWhen
						{
							Key = "compass-coords-when",
							Condition = () => content.Value.HasCoords,
							Content = () => new UiTextRun
							{
								Key = "compass-coords",
								Text = UiText.From(() => content.Value.TrackingLine),
								Size = UiSize.Capped(0.04, 10),
								Role = UiComponentTextRoles.Muted,
								Align = UiComponentAlignments.Start,
							},
					},
					],
				},
			],
			},
		],
	});

	private static double NeedleRotation(double? yaw)
	{
		if (yaw is not { } degrees || !double.IsFinite(degrees))
		{
			return 0;
		}

		return degrees;
	}

	private static UiModifier BattlefieldCard(UiState<MatchHudContent> content) => CardChrome("battlefield-card", new UiButton
	{
		Key = "battlefield-card",
		Padding = 0.015,
		Gap = 0.008,
		Children =
		[
			new UiStack
			{
				Key = "battlefield-head",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = 0.012,
				Children =
				[
					new UiIcon
					{
						Key = "battlefield-icon",
						Icon = UiIcons.Chart,
						Size = UiSize.Capped(0.045, 11),
						MainSize = UiSize.Capped(0.045, 11),
						Role = UiComponentTextRoles.Muted,
					},
					new UiTextRun
					{
						Key = "battlefield-caption",
						Text = UiText.FromLocalized(() => Strings.Widget.Cards.Battlefield()),
						Size = UiSize.Capped(0.032, 8),
						Role = UiComponentTextRoles.Muted,
						Align = UiComponentAlignments.Center,
					},
				],
			},
			new UiGrid
			{
				Key = "battlefield-grid",
				Columns = UiValue.Of(3),
				Gap = 0.015,
				MainSize = UiSize.Capped(0.16, 40),
				Children =
				[
					StatCard("bf-smokes", Strings.Widget.Cards.Smokes(), () => content.Value.Smokes.ToString(System.Globalization.CultureInfo.InvariantCulture)),
					StatCard("bf-fires", Strings.Widget.Cards.Fires(), () => content.Value.Fires.ToString(System.Globalization.CultureInfo.InvariantCulture)),
					StatCard("bf-nades", Strings.Widget.Cards.Nades(), () => content.Value.Nades.ToString(System.Globalization.CultureInfo.InvariantCulture)),
				],
			},
			new UiStack
			{
				Key = "battlefield-pills",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Gap = 0.02,
				Children =
				[
					LocalizedPill("bf-smoked", () => content.Value.Smoked, Strings.Widget.Effects.Smoked,
						() => PillRed, () => MatchHudColors.Bad),
					LocalizedPill("bf-burning", () => content.Value.Burning, Strings.Widget.Effects.Burning,
						() => PillRed, () => MatchHudColors.Bad),
					LocalizedPill("bf-flashed", () => content.Value.Flashed, Strings.Widget.Effects.Flashed,
						() => PillRed, () => MatchHudColors.Bad),
				],
			},
		],
	});

	private static UiModifier EventsCard(UiState<MatchHudContent> content) => CardChrome("events-card", new UiButton
	{
		Key = "events-card",
		Padding = 0.015,
		Gap = 0.008,
		Children =
		[
			new UiStack
			{
				Key = "events-head",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = 0.012,
				Children =
				[
					new UiIcon
					{
						Key = "events-icon",
						Icon = UiIcons.MessageSquare,
						Size = UiSize.Capped(0.045, 11),
						MainSize = UiSize.Capped(0.045, 11),
						Role = UiComponentTextRoles.Muted,
					},
					new UiTextRun
					{
						Key = "events-caption",
						Text = UiText.FromLocalized(() => Strings.Widget.Cards.Events()),
						Size = UiSize.Capped(0.032, 8),
						Role = UiComponentTextRoles.Muted,
						Align = UiComponentAlignments.Center,
					},
				],
			},
			new UiWhen
			{
				Key = "feed-when",
				Condition = () => content.Value.HasFeed,
				Content = () => FeedList(content),
			},
		],
	});

private static UiStack BombPanel(UiState<MatchHudContent> content) => new UiStack
		{
			Key = "bomb-panel",
			Gap = 0.02,
			Children =
			[
				// Progress bar with gradient - red to orange as it counts down
				new UiProgressBar
				{
					Key = "bomb-bar",
					Value = UiValue.From(() => content.Value.BombProgress),
					StartColor = UiValue.Of(MatchHudColors.Bad),
					EndColor = UiValue.Of(MatchHudColors.Hot),
					Thickness = 0.05,
					Fallback = new UiRangeBar
					{
						Key = "bomb-bar-fallback",
						Start = UiValue.Of(0.0),
						End = UiValue.From(() => BombFallbackFrac(content.Value.BombProgress)),
						StartColor = UiValue.Of(MatchHudColors.Bad),
						EndColor = UiValue.Of(MatchHudColors.Hot),
						Thickness = 0.05,
					},
				},
// Countdown ring around the clock: the gauge drains with the remaining
// fraction while the reader-owned clock text keeps ticking inside it. The
// layer measures exactly like the clock row it replaces, so the tile fit
// budget is untouched.
			new UiModifier
			{
				Key = "bomb-dial-deco",
				AccessibilityLabel = UiText.FromLocalized(() => Strings.Widget.Bomb.Accessibility(
					(int)Math.Floor(BombSecondsRemaining(content.Value.BombProgress)))),
				Child = new UiLayer
				{
					Key = "bomb-dial",
					MainSize = UiSize.Capped(0.13, 22),
					Children =
					[
						new UiGauge
						{
							Key = "bomb-ring",
							Level = UiValue.From(() => 1.0 - BombFallbackFrac(content.Value.BombProgress)),
							LevelColor = UiValue.Of(MatchHudColors.Bad),
							StartAngle = 0,
							EndAngle = 360,
							Thickness = UiSize.Capped(0.015, 6),
							MainSize = UiSize.Capped(0.13, 22),
						},
						new UiProgressText
						{
							Key = "bomb-clock",
							Value = UiValue.From(() => content.Value.BombProgress),
							Format = UiValue.Of(UiProgressFormats.Remaining),
							Size = UiSize.Capped(0.11, 18),
							Weight = UiComponentTextWeights.Bold,
							Align = UiComponentAlignments.Center,
						},
					],
				},
			},
				// Plant site from the game feed (A/B); the detail line above
				// already names the carrier, so this row no longer repeats it.
				new UiWhen
				{
					Key = "bomb-site-when",
					Condition = () => !string.IsNullOrWhiteSpace(content.Value.BombSite),
					Content = () => new UiTextRun
					{
						Key = "bomb-site",
						Text = UiText.From(() => content.Value.BombSite),
						Size = UiSize.Capped(0.055, 12),
						MinSize = UiSize.Capped(0.04, 9),
						Weight = UiComponentTextWeights.SemiBold,
						Role = UiComponentTextRoles.Muted,
						Align = UiComponentAlignments.Center,
					},
				},
			],
		};

	private static double BombFallbackFrac(UiProgressReference progress)
	{
		if (progress.DurationMs is not { } total || total <= 0)
		{
			return 0;
		}

		return Math.Clamp((double)progress.PositionMs / total, 0, 1);
	}

	private static UiStack FeedList(UiState<MatchHudContent> content) => new UiStack
	{
		Key = "feed",
		Gap = 0.015,
		Children =
		[
			new UiRepeat<FeedItem>
			{
				Key = "feed-items",
				Items = UiValue.From(() => content.Value.FeedItems),
				KeySelector = static item => item.Key,
				// A status dot leads each row so the feed reads as a timeline
				// instead of plain centered lines; the dot box is smaller than
				// the text, so row height is unchanged.
				Template = static (item, key) => new UiStack
				{
					Key = key,
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Center,
					Gap = 0.015,
					Children =
					[
						new UiShape
						{
							Key = key + "-dot",
							Shape = UiComponentShapes.Circle,
							Fill = true,
							Color = UiValue.Of(item.Accent ?? MatchHudColors.Slate),
							MainSize = UiSize.Capped(0.03, 8),
						},
						new UiTextRun
						{
							Key = key + "-text",
							Text = UiText.FromLocalized(() => item.Text),
							Size = UiSize.Capped(0.075, 10),
							Role = UiComponentTextRoles.Secondary,
							Color = item.Accent is null ? UiValue.None<string>() : UiValue.Of(item.Accent),
							Align = UiComponentAlignments.Start,
							Wrap = true,
							MaxLines = 2,
						},
					],
				},
			},
		],
	};

	private static UiWhen AlivePill(UiState<MatchHudContent> content, string key) =>
		PillButton(key, () => true, new UiTextRun
		{
			Key = key + "-label",
			Text = UiText.FromLocalized(() => content.Value.Alive
				? Strings.Widget.State.Alive() : Strings.Widget.State.Dead()),
			Size = UiSize.Capped(0.085, 10),
			Weight = UiComponentTextWeights.Medium,
			Color = UiValue.From(() => content.Value.Alive ? MatchHudColors.Good : MatchHudColors.Bad),
			Align = UiComponentAlignments.Center,
		}, () => content.Value.Alive ? PillGreen : PillRed);

	private static UiWhen PillButton(
		string key,
		Func<bool> condition,
		UiTextRun run,
		Func<string> background)
	{
		return new UiWhen
		{
			Key = key + "-when",
			Condition = () => condition(),
			Content = () => PillChrome(key, new UiButton
			{
				Key = key,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Background = UiValue.From(background),
				Padding = 0.01,
				Children = [run],
			}),
		};
	}

	private static UiWhen LocalizedPill(
		string key,
		Func<bool> condition,
		Func<MacroDeck.Localization.LocalizedString> text,
		Func<string>? background = null,
		Func<string>? color = null)
	{
		return PillButton(key, condition, new UiTextRun
		{
			Key = key + "-label",
			Text = UiText.FromLocalized(() => text()),
			Size = UiSize.Capped(0.085, 10),
			Weight = UiComponentTextWeights.Medium,
			Color = color is null ? UiValue.None<string>() : UiValue.From(color),
			Align = UiComponentAlignments.Center,
		}, background ?? (() => PillNeutral));
	}

	private static UiWhen TextPill(
		UiState<MatchHudContent> content,
		string key,
		Func<bool> condition,
		Func<string> text,
		Func<string>? background = null,
		Func<string>? color = null)
	{
		return PillButton(key, condition, new UiTextRun
		{
			Key = key + "-label",
			Text = UiText.From(() => text()),
			Size = UiSize.Capped(0.085, 10),
			MinSize = UiSize.Capped(0.05, 8),
			Weight = UiComponentTextWeights.Medium,
			Color = color is null ? UiValue.None<string>() : UiValue.From(color),
			Align = UiComponentAlignments.Center,
		}, background ?? (() => PillNeutral));
	}

}

public static class MatchHudHistory
{
	public const int MaxHpPoints = 90;
	public const int MaxDamageRounds = 24;

	public static IReadOnlyList<double> PushCapped(IReadOnlyList<double> history, double value, int cap)
	{
		var next = history.Append(Math.Clamp(value, 0, 1)).ToList();
		if (next.Count > cap)
		{
			next.RemoveRange(0, next.Count - cap);
		}

		return next;
	}

	public static bool RoundChanged(int previousRound, int round) => round > 0 && round != previousRound;

	public static IReadOnlyList<RoundDot> BuildDots(string history, string team)
	{
		if (string.IsNullOrEmpty(history))
		{
			return [];
		}

		var dots = new List<RoundDot>();
		var rounds = history.Length > 12 ? history.Substring(history.Length - 12) : history;
		var offset = history.Length - rounds.Length;
		for (var i = 0; i < rounds.Length; i++)
		{
			var known = team == "CT" || team == "T";
			var won = rounds[i] switch
			{
				'C' => known ? team == "CT" : (bool?)null,
				'T' => known ? team == "T" : null,
				_ => null,
			};
			var color = won switch
			{
				true => MatchHudColors.Good,
				false => MatchHudColors.Bad,
				_ => rounds[i] switch
				{
					'C' => MatchHudColors.Ct,
					'T' => MatchHudColors.T,
					_ => null,
				},
			};
			dots.Add(new RoundDot((offset + i).ToString(System.Globalization.CultureInfo.InvariantCulture), won is null && color is null ? "○" : "●", color, i == rounds.Length - 1));
		}

		return dots;
	}
}

public static class MatchHudFeed
{
	public const int MaxItems = 3;

	public static MacroDeck.Localization.LocalizedString? Format(string eventId, IReadOnlyDictionary<string, object?> payload) => eventId switch
	{
		GsiEventIds.PlayerKill => KillLine(TextOf(payload, "player"), TextOf(payload, "weapon"), TextOf(payload, "place")),
		GsiEventIds.PlayerDied => DeathLine(TextOf(payload, "player"), TextOf(payload, "place")),
		GsiEventIds.BombPlanted => BombLine(TextOf(payload, "site")),
		GsiEventIds.BombDefused => Strings.Widget.Feed.BombDefused(),
		GsiEventIds.BombExploded => Strings.Widget.Feed.BombExploded(),
		GsiEventIds.RoundWon => Strings.Widget.Feed.RoundWon(),
		GsiEventIds.RoundLost => Strings.Widget.Feed.RoundLost(),
		GsiEventIds.StreakMilestone => Strings.Widget.Feed.Streak(NumberOf(payload, "streak")),
		GsiEventIds.PlaceChanged => Strings.Widget.Feed.Place(TextOf(payload, "place")),
		GsiEventIds.ChatMessage => Strings.Widget.Feed.Chat(TextOf(payload, "player"), TextOf(payload, "text")),
		_ => null,
	};

	public static string? Accent(string eventId) => eventId switch
	{
		GsiEventIds.PlayerKill => MatchHudColors.White,
		GsiEventIds.PlayerDied => MatchHudColors.Slate,
		GsiEventIds.RoundWon => MatchHudColors.Good,
		GsiEventIds.RoundLost => MatchHudColors.Bad,
		GsiEventIds.BombPlanted => MatchHudColors.Bad,
		GsiEventIds.BombDefused => MatchHudColors.Good,
		GsiEventIds.BombExploded => MatchHudColors.Warn,
		GsiEventIds.StreakMilestone => MatchHudColors.Warn,
		GsiEventIds.PlaceChanged => MatchHudColors.Slate,
		_ => null,
	};

	private static MacroDeck.Localization.LocalizedString KillLine(string player, string weapon, string place) =>
		string.IsNullOrEmpty(place)
			? Strings.Widget.Feed.KillBare(player, weapon)
			: Strings.Widget.Feed.Kill(player, weapon, place);
	private static MacroDeck.Localization.LocalizedString DeathLine(string player, string place) =>
		string.IsNullOrEmpty(place)
			? Strings.Widget.Feed.DeathBare(player)
			: Strings.Widget.Feed.Death(player, place);

	private static MacroDeck.Localization.LocalizedString BombLine(string site) =>
		string.IsNullOrEmpty(site)
			? Strings.Widget.Feed.BombPlantedBare()
			: Strings.Widget.Feed.BombPlanted(site);

	private static string TextOf(IReadOnlyDictionary<string, object?> payload, string key) =>
		payload.TryGetValue(key, out var value)
			? MatchHudWidget.SanitizeDisplay(value?.ToString() ?? string.Empty)
			: string.Empty;

	private static int NumberOf(IReadOnlyDictionary<string, object?> payload, string key)
	{
		if (!payload.TryGetValue(key, out var value))
		{
			return 0;
		}

		return value switch
		{
			double d => (int)d,
			float f => (int)f,
			int i => i,
			long l => (int)l,
			string s when int.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => 0,
		};
	}
}

public static class MatchHudPreviews
{
	[UiPreview("Live match", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement LiveMatch() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.SampleLive));

	[UiPreview("Bomb planted", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement BombPlanted() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.SampleBomb));

	[UiPreview("Player page", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement PlayerPage() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.SampleLive with { Page = MatchHudContent.PagePlayer }));

	[UiPreview("Intel page", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement IntelPage() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.SampleLive with { Page = MatchHudContent.PageIntel }));

	[UiPreview("No data", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NoData() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.Empty));

	// Test and tooling support: builds the live tree against caller-owned
	// state so patches can be observed without a running session.
	public static UiElement FromState(UiState<MatchHudContent> state) => MatchHudView.Build(state);
}

public sealed class MatchHudWidget : IWidgetTypeProvider, IUiProvider
{
	private const long BombPlantTotalMs = 40000;
	private readonly GsiService _gsi;
	private readonly CsSettingsProvider _settings;
	private readonly Serilog.ILogger _logger;
	private static readonly object s_registrationGate = new();
	private static readonly WidgetTypeDescriptor s_descriptor = new(
		"match-hud",
		Strings.Widget.MatchHud.Name(),
		Strings.Widget.MatchHud.Description(),
		"""{"showScore":true,"showHistory":true,"showPlayer":true,"showCharts":true,"showStatus":true,"showSession":true,"showFeed":true,"feedCount":3,"compactMode":false,"backgroundColor":"","label":"","labelColor":"","flows":[]}""",
		"""{"type":"object","properties":{"showScore":{"type":"boolean"},"showHistory":{"type":"boolean"},"showPlayer":{"type":"boolean"},"showCharts":{"type":"boolean"},"showStatus":{"type":"boolean"},"showSession":{"type":"boolean"},"showFeed":{"type":"boolean"},"feedCount":{"type":"number"},"compactMode":{"type":"boolean"},"backgroundColor":{"type":"string"},"label":{"type":"string"},"labelColor":{"type":"string"},"flows":{"type":"array"}}}""",
		true,
		new Dictionary<string, string>())
	{
		SupportsFlows = true,
		AppearanceProperties =
		[
			WidgetAppearanceProperty.BackgroundColor,
			WidgetAppearanceProperty.Label,
			WidgetAppearanceProperty.LabelColor,
		],
	};
	private static string? s_widgetTypeId;
	private static bool s_registered;

	public MatchHudWidget(GsiService gsi, CsSettingsProvider settings, Serilog.ILogger logger)
	{
		_gsi = gsi;
		_settings = settings;
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

			var data = ReadElement(surface, UiWidgetSurfaceAttributes.Data);
			var options = MatchHudOptions.FromData(data);
			var appearance = UiWidgetAppearance.Read(data);
			if (ReadBool(surface, UiWidgetSurfaceAttributes.Sample) == true)
			{
				return Task.FromResult<IUiSession?>(new MatchHudSession(
					surface,
					new UiState<MatchHudContent>(MatchHudContent.SampleLive with { Options = options }),
					appearance));
			}

			return Task.FromResult<IUiSession?>(new MatchHudSession(surface, new UiState<MatchHudContent>(BuildContent(options)), this, _gsi, _settings, _logger, appearance));
		}

		if (surface.Kind == UiSurfaceKinds.Config
			&& ReadString(surface, UiConfigSurfaceAttributes.EntryPoint) == UiConfigEntryPoints.WidgetConfig)
		{
			return Task.FromResult<IUiSession?>(BuildConfigSession(surface, MatchHudOptions.FromData(ReadElement(surface, UiConfigSurfaceAttributes.WidgetData))));
		}

		return Task.FromResult<IUiSession?>(null);
	}

	public MatchHudContent BuildContent(MatchHudOptions options) =>
		BuildContent(options, [], [], [], []);

	public MatchHudContent BuildContent(
		MatchHudOptions options,
		IReadOnlyList<double> hpHistory,
		IReadOnlyList<double> dmgHistory,
		IReadOnlyList<double> moneyHistory,
		IReadOnlyList<FeedItem> feed)
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
		var armor = Math.Clamp(snapshot.Armor, 0, 100);
		var loadout = JoinParts(
			snapshot.Weapon,
			snapshot.WeaponType,
			snapshot.AmmoClip >= 0 && snapshot.AmmoReserve >= 0
				? $"{snapshot.AmmoClip} / {snapshot.AmmoReserve}"
				: string.Empty);
		var money = JoinParts(
			MatchHudContent.FormatMoney(snapshot.Money),
			$"{snapshot.Kills} / {snapshot.Deaths} / {snapshot.Assists}");
		var gear = JoinParts(
			armor > 0 ? armor.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
			loadout,
			money);
		var roundLine = Strings.Widget.Round.Line(snapshot.MapRound, snapshot.RoundKills, snapshot.RoundDamage);
		var sessionLine = Strings.Widget.Session.Line(
			snapshot.SessionKills,
			snapshot.SessionDeaths,
			snapshot.SessionKd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
		var bomb = snapshot.BombState;
		if (!string.IsNullOrEmpty(bomb) && snapshot.BombCountdown is { } countdown)
		{
			bomb += " · " + MatchHudContent.FormatClock(countdown);
		}

		var team = NormalizeTeam(snapshot.PlayerTeam);
		var history = snapshot.RoundHistory;
		var dots = MatchHudHistory.BuildDots(history, team);
		var dmgMax = dmgHistory.Count > 0 ? dmgHistory.Max() * 500 : 0;
		var dmgLast = dmgHistory.Count > 0 ? dmgHistory[^1] * 500 : 0;
		var coords = snapshot.HasPosition
			? $"{snapshot.PosX:F0} · {snapshot.PosY:F0} · {snapshot.PosZ:F0}"
			: string.Empty;
		var facing = snapshot.FacingYaw is { } yaw ? $"{yaw:F0}°" : string.Empty;

		return new MatchHudContent(
			true,
			JoinParts(MapNames.DisplayName(snapshot.MapName).ToUpperInvariant(), (snapshot.MapMode ?? string.Empty).ToUpperInvariant()),
			SanitizeDisplay(snapshot.CtName), snapshot.CtScore,
			SanitizeDisplay(snapshot.TName), snapshot.TScore,
			"R" + snapshot.MapRound.ToString(System.Globalization.CultureInfo.InvariantCulture),
			(snapshot.MapPhase ?? string.Empty).ToUpperInvariant(),
			snapshot.PhaseEndsIn is { } ends ? MatchHudContent.FormatClock(ends) : string.Empty,
			snapshot.PhaseEndsIn is not null,
			dots, dots.Count > 0,
			JoinParts(SanitizeDisplay(snapshot.PlayerName), ClanTag(snapshot.Clan)),
			team,
			snapshot.Alive,
			snapshot.HasPlayer,
			hp / 100.0,
			hp.ToString(System.Globalization.CultureInfo.InvariantCulture),
			armor / 100.0,
			gear,
			roundLine,
			!string.IsNullOrEmpty(snapshot.TopWeapon)
				? Strings.Widget.TopWeapon.Line(snapshot.TopWeapon, snapshot.TopWeaponKills)
				: null,
			SanitizeDisplay(snapshot.PlaceName),
			!string.IsNullOrEmpty(snapshot.PlaceName),
			coords,
			!string.IsNullOrEmpty(coords),
			JoinParts(coords, facing),
			(bomb ?? string.Empty).ToUpperInvariant(),
			!string.IsNullOrEmpty(bomb),
			BombProgressOf(snapshot),
			IsBombLive(snapshot),
			snapshot.Smoked,
			snapshot.Burning,
			snapshot.Flashed,
			snapshot.Helmet,
			snapshot.DefuseKit,
			snapshot.KillStreak + snapshot.TopWeaponKills,
			snapshot.KillStreak + snapshot.TopWeaponKills >= 2,
			sessionLine,
			hpHistory,
			hpHistory.Count > 0,
			dmgHistory,
			dmgHistory.Count > 0
				? $"{dmgLast:F0} / {dmgMax:F0}"
				: string.Empty,
			dmgHistory.Count > 0,
			moneyHistory,
			moneyHistory.Count > 0,
			feed,
			feed.Count > 0,
			0,
			snapshot.Kills,
			snapshot.Deaths,
			snapshot.Assists,
			snapshot.SessionHs,
			snapshot.SessionDamage,
			snapshot.Mvps,
			snapshot.Score,
			snapshot.EquipValue,
			snapshot.RoundKills,
			snapshot.RoundHeadshots,
			snapshot.RoundDamage,
			snapshot.BestStreak,
			snapshot.TimeoutsCt,
			snapshot.TimeoutsT,
			MatchHudContent.FormatClock(snapshot.MatchElapsed),
			MatchHudContent.FormatClock(snapshot.SessionMatchTime),
			SanitizeDisplay(snapshot.BombCarrier),
			snapshot.BombSite ?? string.Empty,
			snapshot.FacingYaw,
			snapshot.SmokesActive,
			snapshot.FireActive,
			snapshot.GrenadesActive,
			options);
	}

	private static UiProgressReference BombProgressOf(GsiSnapshot snapshot)
	{
		if (!IsBombLive(snapshot) || snapshot.BombCountdown is not { } countdown)
		{
			return new UiProgressReference { PositionMs = 0, Anchor = DateTimeOffset.UtcNow };
		}

		var elapsed = Math.Clamp(BombPlantTotalMs - countdown * 1000, 0, BombPlantTotalMs);
		return new UiProgressReference
		{
			PositionMs = (long)elapsed,
			Anchor = DateTimeOffset.UtcNow,
			DurationMs = BombPlantTotalMs,
			Rate = 1,
		};
	}

	private static bool IsBombLive(GsiSnapshot snapshot) =>
		string.Equals(snapshot.BombState, "planted", StringComparison.OrdinalIgnoreCase)
		&& snapshot.BombCountdown is not null;

	public static string YawArrow(double? yaw)
	{
		if (yaw is not { } degrees || double.IsNaN(degrees) || double.IsInfinity(degrees))
		{
			return string.Empty;
		}

		var octant = (int)Math.Round(degrees / 45, MidpointRounding.AwayFromZero) & 7;
		return octant switch
		{
			0 => "↑",
			1 => "↗",
			2 => "→",
			3 => "↘",
			4 => "↓",
			5 => "↙",
			6 => "←",
			_ => "↖",
		};
	}

	public static string JoinParts(params string?[] parts) =>
		string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

	public static string? MatchPoint(int ctScore, int tScore, string? ctName, string? tName)
	{
		if (ctScore >= 12 && tScore >= 12)
		{
			return string.Empty;
		}

		if (ctScore == 12 && tScore < 12)
		{
			return string.IsNullOrWhiteSpace(ctName) ? "CT" : ctName;
		}

		if (tScore == 12 && ctScore < 12)
		{
			return string.IsNullOrWhiteSpace(tName) ? "T" : tName;
		}

		return null;
	}

	public static string MatchWinner(int ctScore, int tScore, string? ctName, string? tName)
	{
		if (tScore > ctScore)
		{
			return string.IsNullOrWhiteSpace(tName) ? "T" : tName;
		}

		return string.IsNullOrWhiteSpace(ctName) ? "CT" : ctName;
	}

	public static MacroDeck.Localization.LocalizedString MatchPointText(int ctScore, int tScore, string? ctName, string? tName)
	{
		var leader = MatchPoint(ctScore, tScore, ctName, tName);
		return string.IsNullOrEmpty(leader)
			? Strings.Widget.Match.Overtime()
			: Strings.Widget.Match.Point(SanitizeDisplay(leader));
	}

	public static string ClanTag(string? clan)
	{
		var clean = SanitizeDisplay(clan);
		return clean.Length == 0 ? string.Empty : "[" + clean + "]";
	}

	public static string SanitizeDisplay(string? value) => DisplayText.Sanitize(value);

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
		var showHistory = new UiState<bool>(options.ShowHistory);
		var showPlayer = new UiState<bool>(options.ShowPlayer);
		var showCharts = new UiState<bool>(options.ShowCharts);
		var showStatus = new UiState<bool>(options.ShowStatus);
		var showSession = new UiState<bool>(options.ShowSession);
		var showFeed = new UiState<bool>(options.ShowFeed);
		var feedCount = new UiState<double>(options.FeedCount);
		var compactMode = new UiState<bool>(options.Compact);
		var data = ReadElement(surface, UiConfigSurfaceAttributes.WidgetData);
		var flows = new UiState<JsonElement>(ReadFlows(data));
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
						Key = "showHistory",
						Label = Strings.Widget.Config.ShowHistory(),
						Description = Strings.Widget.Config.ShowHistoryDescription(),
						Binding = Bind.To(showHistory),
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
						Key = "showCharts",
						Label = Strings.Widget.Config.ShowCharts(),
						Description = Strings.Widget.Config.ShowChartsDescription(),
						Binding = Bind.To(showCharts),
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
						Key = "showSession",
						Label = Strings.Widget.Config.ShowSession(),
						Description = Strings.Widget.Config.ShowSessionDescription(),
						Binding = Bind.To(showSession),
					},
					new UiBooleanInput
					{
						Key = "showFeed",
						Label = Strings.Widget.Config.ShowFeed(),
						Description = Strings.Widget.Config.ShowFeedDescription(),
						Binding = Bind.To(showFeed),
					},
					new UiNumberInput
					{
						Key = "feedCount",
						Label = Strings.Widget.Config.FeedCount(),
						Description = Strings.Widget.Config.FeedCountDescription(),
						Min = UiValue.Of(1.0),
						Max = UiValue.Of(5.0),
						Step = UiValue.Of(1.0),
						ShowSlider = UiValue.Of(true),
						Binding = Bind.To(feedCount),
					},
					new UiBooleanInput
					{
						Key = "compactMode",
						Label = Strings.Widget.Config.CompactMode(),
						Binding = Bind.To(compactMode),
					},
					new UiTextRun
					{
						Key = "swipeHint",
						Text = UiText.FromLocalized(() => Strings.Widget.Config.SwipeHint()),
						Size = UiSize.Capped(0.04, 10),
						Role = UiComponentTextRoles.Muted,
						Align = UiComponentAlignments.Center,
					},
					UiWidgetAppearance.Section(
						data,
						UiWidgetAppearanceFields.BackgroundColor | UiWidgetAppearanceFields.Label | UiWidgetAppearanceFields.LabelColor),
				],
			},
			Editor = new UiWidgetEditor
			{
				Key = "editor",
				Children = [new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows), CanRun = true }],
			},
		});
		return new MatchHudSession(view);
	}

	private static JsonElement ReadFlows(JsonElement data)
	{
		if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("flows", out var flows))
		{
			return flows;
		}

		// The editor binding must always serialize, so an absent key becomes an
		// empty array rather than an undefined element.
		using var empty = JsonDocument.Parse("[]");
		return empty.RootElement.Clone();
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
		private readonly object _feedGate = new();
		private readonly List<FeedItem> _feed = [];
		private IReadOnlyList<double> _hp = [];
		private IReadOnlyList<double> _dmg = [];
		private IReadOnlyList<double> _money = [];
		private int _dmgRound;
		private int _dmgDamage;
		private int _feedSeq;
		private bool _disposed;

		public MatchHudSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<MatchHudContent> content,
			MatchHudWidget owner,
			GsiService gsi,
			CsSettingsProvider settings,
			Serilog.ILogger logger,
			UiWidgetAppearanceValues? appearance = null)
		{
			_content = content;
			_owner = owner;
			_gsi = gsi;
			_logger = logger.ForContext<MatchHudSession>();
			_gsi.MatchEvent += OnMatchEvent;
			_view = new UiView(surface, MatchHudView.Build(content, SessionActions(content, owner, gsi, settings, _logger), appearance));
			_view.Changed += OnChanged;
			_view.HandlerFaulted += OnHandlerFaulted;
			_loop = RefreshLoopAsync(_cts.Token);
		}

		private static MatchHudActions SessionActions(
			UiState<MatchHudContent> content,
			MatchHudWidget owner,
			GsiService gsi,
			CsSettingsProvider settings,
			Serilog.ILogger logger) => new(
			SelectPage: index => SelectPage(content, index, logger),
			SelectPageData: data => SelectPageData(content, data, logger),
			SwipePage: data => SwipePage(content, data, logger),
			CyclePage: () => CyclePage(content, logger),
			Simulate: () => SimulateMatch(gsi, logger),
			Install: () => InstallGsiConfig(gsi, settings, logger));

		// Rejection reasons travel as plain strings with no localization reference,
		// so these stay English literals rather than keys that would render raw.
		private static UiEventOutcome SelectPage(UiState<MatchHudContent> content, int index, Serilog.ILogger? logger = null)
		{
			if (index is < MatchHudContent.PageMatch or > MatchHudContent.PageIntel)
			{
				logger?.Debug("Widget tab change rejected: page {Index} out of range", index);
				return UiEventOutcome.Rejected($"Unknown page {index}.");
			}

			logger?.Debug("Widget tab change accepted: {Page}", index);
			content.Set(content.Peek() with { Page = index });
			return UiEventOutcome.Accepted;
		}

		private static UiEventOutcome SelectPageData(UiState<MatchHudContent> content, UiEventData data, Serilog.ILogger? logger)
		{
			// Segmented control sends the segment index; payload shape varies by host version.
			// Try double first (standard), then integer, then string.
			double? index = null;
			if (data.TryGetDouble(out var d))
			{
				index = d;
			}
			else if (data.Raw is { ValueKind: System.Text.Json.JsonValueKind.Number } rawNumber)
			{
				try { index = rawNumber.GetDouble(); } catch { }
			}
			else if (data.Raw is { ValueKind: System.Text.Json.JsonValueKind.String } rawString)
			{
				if (double.TryParse(rawString.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
				{
					index = parsed;
				}
			}

			if (index is null)
			{
				logger?.Debug("Widget tab change carried no parseable segment index: {Raw}", data.Raw);
				return UiEventOutcome.Rejected("Unknown page.");
			}

			var page = (int)Math.Round(index.Value);
			logger?.Debug("Widget tab change requested: {Page}", page);
			return SelectPage(content, page, logger);
		}

		private static UiEventOutcome SwipePage(UiState<MatchHudContent> content, UiEventData data, Serilog.ILogger? logger)
		{
			// A swipe carries the direction as a bare string. Left advances through
			// Match, Player, Intel and wraps; right goes back. Anything else is a
			// no-op rejection, like an unknown tab index.
			if (!data.TryGetString(out var direction))
			{
				return UiEventOutcome.Rejected("Unknown swipe.");
			}

			var page = content.Value.Page;
			var next = direction switch
			{
				"left" => (page + 1) % 3,
				"right" => (page + 2) % 3,
				_ => -1,
			};
			if (next < 0)
			{
				logger?.Debug("Widget swipe rejected: {Direction}.", direction);
				return UiEventOutcome.Rejected("Unknown swipe.");
			}

			logger?.Debug("Widget swipe accepted: {Direction} to page {Page}.", direction, next);
			return SelectPage(content, next, logger);
		}

		// Tap advances one page with wrap: the control readers without touch
		// or swipe support (hardware decks, older hosts) can still flip pages.
		private static UiEventOutcome CyclePage(UiState<MatchHudContent> content, Serilog.ILogger? logger)
		{
			var next = (content.Value.Page + 1) % 3;
			logger?.Debug("Widget scorebug press advances to page {Page}.", next);
			return SelectPage(content, next, logger);
		}

		private static UiEventOutcome SimulateMatch(GsiService gsi, Serilog.ILogger logger)
		{
			try
			{
				gsi.InjectTestState();
				return UiEventOutcome.Accepted;
			}
			catch (Exception ex)
			{
				logger.Debug(ex, "Widget simulate failed.");
				return UiEventOutcome.Rejected("Simulate failed.");
			}
		}

		private static UiEventOutcome InstallGsiConfig(GsiService gsi, CsSettingsProvider settings, Serilog.ILogger logger)
		{
			try
			{
				var current = settings.Current;
				var (ok, detail) = GsiConfig.Install(current.Port, current.AuthToken);
				if (!ok)
				{
					return UiEventOutcome.Rejected(detail == "not-found"
						? "Counter-Strike 2 was not found."
						: "Writing the game config failed.");
				}

				gsi.Start(current.Port, current.AuthToken);
				return UiEventOutcome.Accepted;
			}
			catch (Exception ex)
			{
				logger.Debug(ex, "Widget install failed.");
				return UiEventOutcome.Rejected("Install failed.");
			}
		}

		public MatchHudSession(
			MacroDeck.Ui.Model.Surfaces.UiSurface surface,
			UiState<MatchHudContent> content,
			UiWidgetAppearanceValues? appearance = null)
		{
			_content = content;
			_view = new UiView(surface, MatchHudView.Build(content, null, appearance));
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
			if (_gsi is not null)
			{
				_gsi.MatchEvent -= OnMatchEvent;
			}

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

		private void OnMatchEvent(object? sender, GsiMatchEvent matchEvent)
		{
			try
			{
				var line = MatchHudFeed.Format(matchEvent.EventId, matchEvent.Payload);
				if (line is null)
				{
					return;
				}

				lock (_feedGate)
				{
					_feedSeq++;
					_feed.Insert(0, new FeedItem(
						_feedSeq.ToString(System.Globalization.CultureInfo.InvariantCulture), line.Value,
						MatchHudFeed.Accent(matchEvent.EventId)));
					var cap = _content?.Value.Options.FeedCount ?? MatchHudFeed.MaxItems;
					while (_feed.Count > cap)
					{
						_feed.RemoveAt(_feed.Count - 1);
					}
				}
			}
			catch (Exception ex)
			{
				_logger?.Debug(ex, "Feed event failed.");
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
			if (_content is null || _owner is null || _gsi is null)
			{
				return;
			}

			GsiSnapshot snapshot;
			try
			{
				snapshot = _gsi.Snapshot();
			}
			catch (Exception ex)
			{
				_logger?.Debug(ex, "Widget snapshot failed.");
				return;
			}

			List<FeedItem> feed;
			lock (_feedGate)
			{
				feed = _feed.ToList();
			}

			if (snapshot.Connected && snapshot.HasPlayer)
			{
				_hp = MatchHudHistory.PushCapped(_hp, snapshot.Health / 100.0, MatchHudHistory.MaxHpPoints);
				_money = MatchHudHistory.PushCapped(_money, snapshot.Money / 16000.0, MatchHudHistory.MaxHpPoints);
				if (MatchHudHistory.RoundChanged(_dmgRound, snapshot.MapRound))
				{
					if (_dmgRound > 0)
					{
						_dmg = MatchHudHistory.PushCapped(_dmg, _dmgDamage / 500.0, MatchHudHistory.MaxDamageRounds);
					}

					_dmgRound = snapshot.MapRound;
				}

				_dmgDamage = snapshot.RoundDamage;
			}
			else if (!snapshot.Connected)
			{
				_hp = [];
				_dmg = [];
				_money = [];
				_dmgRound = 0;
				_dmgDamage = 0;
			}

			var cap = _content.Value.Options.FeedCount;
			if (feed.Count > cap)
			{
				feed = feed.Take(cap).ToList();
			}

			var next = _owner.BuildContent(_content.Value.Options, _hp, _dmg, _money, feed) with
			{
				Page = _content.Value.Page,
			};
			if (!ContentsEqual(_content.Value, next))
			{
				_content.Set(next);
			}
		}

		private static bool ContentsEqual(MatchHudContent current, MatchHudContent next)
		{
			if (!current.Equals(next))
			{
				var quick = current with
				{
					HpHistory = next.HpHistory,
					DmgHistory = next.DmgHistory,
					MoneyHistory = next.MoneyHistory,
					FeedItems = next.FeedItems,
					BombProgress = next.BombProgress,
				};
				if (!quick.Equals(next))
				{
					return false;
				}
			}

			if (!current.HpHistory.SequenceEqual(next.HpHistory)
				|| !current.DmgHistory.SequenceEqual(next.DmgHistory)
				|| !current.MoneyHistory.SequenceEqual(next.MoneyHistory)
				|| !FeedEqual(current.FeedItems, next.FeedItems))
			{
				return false;
			}

			return BombQuiet(current.BombProgress, next.BombProgress);
		}

		private static bool FeedEqual(IReadOnlyList<FeedItem> current, IReadOnlyList<FeedItem> next)
		{
			if (current.Count != next.Count)
			{
				return false;
			}

			for (var i = 0; i < current.Count; i++)
			{
				if (current[i].Key != next[i].Key || current[i].Text != next[i].Text)
				{
					return false;
				}
			}

			return true;
		}

		private static bool BombQuiet(UiProgressReference current, UiProgressReference next)
		{
			if (current.DurationMs != next.DurationMs || current.Rate != next.Rate)
			{
				return false;
			}

			var predicted = current.PositionMs;
			if (current.Rate != 0)
			{
				predicted += (long)(DateTimeOffset.UtcNow - current.Anchor).TotalMilliseconds;
			}

			return Math.Abs(predicted - next.PositionMs) < 1500;
		}

		private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);

		private void OnHandlerFaulted(object? sender, UiHandlerFaultEventArgs e) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("handler-fault", e.Exception));
	}
}



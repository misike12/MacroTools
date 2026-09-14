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

public sealed record RoundDot(string Key, string Glyph, string? Color);

public sealed record FeedItem(string Key, MacroDeck.Localization.LocalizedString Text);

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
	string ArmorLine,
	string LoadoutLine,
	string MoneyLine,
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
	MatchHudOptions Options)
{
	public static MatchHudContent Empty { get; } = new(
		false, string.Empty, "CT", 0, "T", 0, string.Empty, string.Empty, string.Empty, false,
		[], false,
		string.Empty, string.Empty, false, false, 0, string.Empty, 0, string.Empty, string.Empty, string.Empty, Strings.Widget.Round.Line(0, 0, 0), null,
		string.Empty, false, string.Empty, false, string.Empty, string.Empty, false, new UiProgressReference { PositionMs = 0, Anchor = DateTimeOffset.UtcNow }, false,
		false, false, false, false, false, 0, false, Strings.Widget.Session.Line(0, 0, "0.00"),
		[], false, [], string.Empty, false, [], false, [], false,
		MatchHudOptions.Default);

	public static MatchHudContent SampleLive { get; } = new(
		true, "DE_MIRAGE · COMPETITIVE", "NAVI", 9, "FAZE", 7, "R17", "LIVE", "1:23", true,
		[new RoundDot("1", "●", "#4ADE80"), new RoundDot("2", "●", "#4ADE80"), new RoundDot("3", "●", "#F87171")], true,
		"s1mple [NAVI]", "CT", true, true, 0.87, "87", 1.0, "100", "AWP · Rifle · 5 / 30", "$4,700 · 18 / 9 / 4", Strings.Widget.Round.Line(17, 2, 250), Strings.Widget.TopWeapon.Line("AWP", 14),
		"Middle", true, "512 · -735 · -148", true, "CONSOLE", "CARRIED", false, new UiProgressReference { PositionMs = 0, Anchor = DateTimeOffset.UtcNow }, false,
		false, false, false, true, true, 4, true, Strings.Widget.Session.Line(18, 9, "2.00"),
		[0.9, 0.85, 0.87, 0.6, 0.62, 0.87], true, [0.2, 0.5, 0.3], "250 / 400", true, [0.1, 0.2, 0.29], true,
		[new FeedItem("f2", Strings.Widget.Feed.Kill("s1mple", "AWP", "Middle")), new FeedItem("f1", Strings.Widget.Feed.RoundWon())], true,
		MatchHudOptions.Default);

	public static MatchHudContent SampleBomb { get; } = new(
		true, "DE_DUST2 · COMPETITIVE", "CTs", 11, "Ts", 9, "R21", "LIVE", "0:32", true,
		[], false,
		"misuuu", "T", false, true, 0, "0", 0, "0", "AK-47 · Rifle · 0 / 90", "$800 · 14 / 12 / 3", Strings.Widget.Round.Line(21, 0, 0), null,
		"Bombsite A", true, string.Empty, false, string.Empty, "PLANTED", true,
		new UiProgressReference { PositionMs = 8000, Anchor = DateTimeOffset.UtcNow, DurationMs = 40000, Rate = 1 }, true,
		false, false, false, false, false, 0, false, Strings.Widget.Session.Line(14, 12, "1.17"),
		[], false, [], string.Empty, false, [], false, [new FeedItem("f1", Strings.Widget.Feed.BombPlanted("B"))], true,
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

		if (options.ShowCharts && !options.Compact)
		{
			body.Add(new UiWhen
			{
				Key = "charts-when",
				Condition = () => content.Value.HasHpHistory || content.Value.HasDmgHistory || content.Value.HasMoneyHistory,
				Content = () => Charts(content),
			});
		}

		if (options.ShowStatus)
		{
			body.Add(StatusRow(content, options));
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
		var body = new List<UiElement>
		{
			new UiStack
			{
				Key = "scores",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.SpaceBetween,
				Children =
				[
					SideScore(content, big, micro, ct: true),
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
							new UiTextRun
							{
								Key = "clock",
								Text = UiText.From(() => content.Value.PhaseTime),
								Size = UiSize.Capped(0.1, 13),
								Weight = UiComponentTextWeights.SemiBold,
								Digits = UiValue.Of(4.0),
								Align = UiComponentAlignments.Center,
							},
						],
					},
					SideScore(content, big, micro, ct: false),
				],
			},
		};

		body.Add(new UiWhen
		{
			Key = "history-when",
			Condition = () => options.ShowHistory && content.Value.HasHistory,
			Content = () => new UiStack
			{
				Key = "history",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
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
							Size = UiSize.Capped(0.07, 10),
							Color = dot.Color is null ? UiValue.None<string>() : UiValue.Of(dot.Color),
							Align = UiComponentAlignments.Center,
						},
					},
				],
			},
		});
		body.Add(MicroLine(content, "map", () => content.Value.MapLine));

		return new UiStack
		{
			Key = "scorebug",
			Gap = 0.02,
			Children = body,
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
					Size = micro,
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
		],
	};

	private static UiStack PlayerPlate(UiState<MatchHudContent> content, MatchHudOptions options)
	{
		var body = new List<UiElement>
		{
			new UiStack
			{
				Key = "name-row",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.SpaceBetween,
				Children =
				[
					new UiTextRun
					{
						Key = "player-name",
						Text = UiText.From(() => content.Value.NameLine),
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
						Color = UiValue.From(() => HpColor(content.Value.HpFrac)),
						Digits = UiValue.Of(3.0),
						Align = UiComponentAlignments.Center,
					},
				],
			},
			new UiRangeBar
			{
				Key = "hp-bar",
				Start = UiValue.Of(0.0),
				End = UiValue.From(() => content.Value.HpFrac),
				StartColor = UiValue.From(() => HpColor(content.Value.HpFrac)),
				EndColor = UiValue.From(() => HpColor(content.Value.HpFrac)),
				Thickness = 0.035,
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
					Thickness = 0.018,
				},
			},
			MicroLine(content, "armor", () => content.Value.ArmorLine),
			MicroLine(content, "loadout", () => content.Value.LoadoutLine),
			MicroLine(content, "money", () => content.Value.MoneyLine),
			new UiTextRun
			{
				Key = "round-line",
				Text = UiText.FromLocalized(() => content.Value.RoundLine),
				Size = UiSize.Capped(0.08, 10),
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
					Size = UiSize.Capped(0.08, 10),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
				},
			},
		};

		return new UiStack
		{
			Key = "player",
			Gap = 0.02,
			Children = body,
		};
	}

	private static string HpColor(double frac) =>
		frac > 0.5 ? MatchHudColors.Good : frac > 0.25 ? MatchHudColors.Warn : MatchHudColors.Bad;

	private static UiStack Charts(UiState<MatchHudContent> content) => new UiStack
	{
		Key = "charts",
		Direction = UiComponentDirections.Horizontal,
		Gap = 0.04,
		Children =
		[
			new UiStack
			{
				Key = "hp-chart-box",
				Fill = true,
				Children =
				[
					new UiChart
					{
						Key = "hp-chart",
						Points = UiValue.From(() => content.Value.HpHistory),
						Color = UiValue.Of(MatchHudColors.Good),
						Thickness = 0.02,
						MainSize = UiSize.Capped(0.1, 36),
					},
				],
			},
			new UiStack
			{
				Key = "dmg-chart-box",
				Fill = true,
				Children =
				[
					new UiChart
					{
						Key = "dmg-chart",
						Points = UiValue.From(() => content.Value.DmgHistory),
						Color = UiValue.Of(MatchHudColors.T),
						Thickness = 0.02,
						MainSize = UiSize.Capped(0.1, 36),
					},
					new UiTextRun
					{
						Key = "dmg-caption",
						Text = UiText.From(() => content.Value.DmgCaption),
						Size = UiSize.Capped(0.07, 9),
						Role = UiComponentTextRoles.Muted,
						Align = UiComponentAlignments.Center,
					},
				],
			},
			new UiStack
			{
				Key = "money-chart-box",
				Fill = true,
				Children =
				[
					new UiChart
					{
						Key = "money-chart",
						Points = UiValue.From(() => content.Value.MoneyHistory),
						Color = UiValue.Of(MatchHudColors.Ct),
						Thickness = 0.02,
						MainSize = UiSize.Capped(0.1, 36),
					},
				],
			},
		],
	};

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
			LocalizedPill("flashed", () => content.Value.Flashed, Strings.Widget.Effects.Flashed),
			LocalizedPill("helm", () => content.Value.Helmet, Strings.Widget.Effects.Helmet),
			LocalizedPill("defuse", () => content.Value.DefuseKit, Strings.Widget.Effects.DefuseKit),
			LocalizedPill("streak", () => content.Value.HasStreak, () => Strings.Widget.Streak.Label(content.Value.Streak)),
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

		children.Add(new UiWhen
		{
			Key = "bombbar-when",
			Condition = () => content.Value.HasBombBar,
			Content = () => BombPanel(content),
		});
		children.Add(MicroLine(content, "tracking", () => content.Value.TrackingLine));

		if (options.ShowSession && !options.Compact)
		{
			children.Add(new UiTextRun
			{
				Key = "session",
				Text = UiText.FromLocalized(() => content.Value.SessionLine),
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

	private static UiStack BombPanel(UiState<MatchHudContent> content) => new UiStack
	{
		Key = "bomb-panel",
		Gap = 0.02,
		Children =
		[
			new UiProgressBar
			{
				Key = "bomb-bar",
				Value = UiValue.From(() => content.Value.BombProgress),
				StartColor = UiValue.Of(MatchHudColors.Bad),
				EndColor = UiValue.Of(MatchHudColors.Bad),
				Thickness = 0.035,
				Fallback = new UiRangeBar
				{
					Key = "bomb-bar-fallback",
					Start = UiValue.Of(0.0),
					End = UiValue.From(() => BombFallbackFrac(content.Value.BombProgress)),
					Thickness = 0.035,
				},
			},
			new UiProgressText
			{
				Key = "bomb-clock",
				Value = UiValue.From(() => content.Value.BombProgress),
				Format = UiValue.Of(UiProgressFormats.Remaining),
				Weight = UiComponentTextWeights.SemiBold,
				Align = UiComponentAlignments.Center,
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
				Template = static (item, key) => new UiTextRun
				{
					Key = key,
					Text = UiText.FromLocalized(() => item.Text),
					Size = UiSize.Capped(0.075, 10),
					Role = UiComponentTextRoles.Secondary,
					Align = UiComponentAlignments.Center,
				},
			},
		],
	};

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
			dots.Add(new RoundDot((offset + i).ToString(System.Globalization.CultureInfo.InvariantCulture), won is null && color is null ? "○" : "●", color));
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

	[UiPreview("No data", View = "MatchHud", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NoData() => MatchHudView.Build(
		new UiState<MatchHudContent>(MatchHudContent.Empty));
}

public sealed class MatchHudWidget : IWidgetTypeProvider, IUiProvider
{
	private const long BombPlantTotalMs = 40000;
	private readonly GsiService _gsi;
	private readonly Serilog.ILogger _logger;
	private static readonly object s_registrationGate = new();
	private static readonly WidgetTypeDescriptor s_descriptor = new(
		"match-hud",
		Strings.Widget.MatchHud.Name(),
		Strings.Widget.MatchHud.Description(),
		"""{"showScore":true,"showHistory":true,"showPlayer":true,"showCharts":true,"showStatus":true,"showSession":true,"showFeed":true,"feedCount":3,"compactMode":false}""",
		"""{"type":"object","properties":{"showScore":{"type":"boolean"},"showHistory":{"type":"boolean"},"showPlayer":{"type":"boolean"},"showCharts":{"type":"boolean"},"showStatus":{"type":"boolean"},"showSession":{"type":"boolean"},"showFeed":{"type":"boolean"},"feedCount":{"type":"number"},"compactMode":{"type":"boolean"}}}""",
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
			armor > 0 ? armor.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
			loadout,
			money,
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
			snapshot.KillStreak,
			snapshot.KillStreak >= 2,
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

	public static string JoinParts(params string?[] parts) =>
		string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

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
			Serilog.ILogger logger)
		{
			_content = content;
			_owner = owner;
			_gsi = gsi;
			_logger = logger.ForContext<MatchHudSession>();
			_gsi.MatchEvent += OnMatchEvent;
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
			if (_gsi is not null)
			{
				_gsi.MatchEvent -= OnMatchEvent;
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
						_feedSeq.ToString(System.Globalization.CultureInfo.InvariantCulture), line.Value));
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

			var next = _owner.BuildContent(_content.Value.Options, _hp, _dmg, _money, feed);
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


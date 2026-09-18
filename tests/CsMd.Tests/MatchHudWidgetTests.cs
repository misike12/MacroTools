using System.Text.Json;
using CsMd.Config;
using CsMd.Gsi;
using CsMd.Places;
using CsMd.Widgets;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using NUnit.Framework;
using Serilog;

namespace CsMd.Tests;

[TestFixture]
public sealed class MatchHudWidgetTests
{
	[Test]
	public void Options_default_all_sections_on()
	{
		var options = MatchHudOptions.FromData(default);

		Assert.That(options, Is.EqualTo(MatchHudOptions.Default));
		Assert.That(options.ShowScore, Is.True);
		Assert.That(options.ShowHistory, Is.True);
		Assert.That(options.ShowPlayer, Is.True);
		Assert.That(options.ShowCharts, Is.True);
		Assert.That(options.ShowStatus, Is.True);
		Assert.That(options.ShowSession, Is.True);
		Assert.That(options.ShowFeed, Is.True);
		Assert.That(options.FeedCount, Is.EqualTo(3));
		Assert.That(options.Compact, Is.False);
	}

	[Test]
	public void Display_text_neutralizes_markup_brackets()
	{
		Assert.That(MatchHudWidget.SanitizeDisplay("Mag1c <3"), Is.EqualTo("Mag1c ‹3"));
		Assert.That(MatchHudWidget.SanitizeDisplay("a>b"), Is.EqualTo("a›b"));
		Assert.That(MatchHudWidget.SanitizeDisplay("Fish & Chips"), Is.EqualTo("Fish ＆ Chips"));
		Assert.That(MatchHudWidget.SanitizeDisplay("say \"hi\""), Is.EqualTo("say ″hi″"));
		Assert.That(MatchHudWidget.SanitizeDisplay("O'Neil"), Is.EqualTo("O′Neil"));
		Assert.That(MatchHudWidget.SanitizeDisplay("a" + (char)0x200B + "b" + (char)0x200F + "c"), Is.EqualTo("abc"));
		Assert.That(MatchHudWidget.SanitizeDisplay("a" + (char)0x202E + "b"), Is.EqualTo("ab"));
		Assert.That(MatchHudWidget.SanitizeDisplay("a" + (char)0xFEFF + "b"), Is.EqualTo("ab"));
		Assert.That(MatchHudWidget.SanitizeDisplay("a\nb\tc"), Is.EqualTo("abc"));
		Assert.That(MatchHudWidget.SanitizeDisplay("  padded  "), Is.EqualTo("padded"));
		Assert.That(MatchHudWidget.SanitizeDisplay("Árvíztűrő 🔥 s1mple"), Is.EqualTo("Árvíztűrő 🔥 s1mple"));
		Assert.That(MatchHudWidget.SanitizeDisplay("plain"), Is.EqualTo("plain"));
		Assert.That(MatchHudWidget.SanitizeDisplay(null), Is.Empty);
		Assert.That(MatchHudWidget.SanitizeDisplay(string.Empty), Is.Empty);
	}

	[Test]
	public void Weapon_display_names_cover_arsenal()
	{
		Assert.That(WeaponNames.DisplayName("weapon_ak47"), Is.EqualTo("AK-47"));
		Assert.That(WeaponNames.DisplayName("weapon_m4a1_silencer"), Is.EqualTo("M4A1-S"));
		Assert.That(WeaponNames.DisplayName("weapon_usp_silencer"), Is.EqualTo("USP-S"));
		Assert.That(WeaponNames.DisplayName("weapon_deagle"), Is.EqualTo("Desert Eagle"));
		Assert.That(WeaponNames.DisplayName("weapon_cz75a"), Is.EqualTo("CZ75-Auto"));
		Assert.That(WeaponNames.DisplayName("weapon_knife_karambit"), Is.EqualTo("Karambit"));
		Assert.That(WeaponNames.DisplayName("weapon_hegrenade"), Is.EqualTo("HE Grenade"));
		Assert.That(WeaponNames.DisplayName("weapon_c4"), Is.EqualTo("C4"));
		Assert.That(WeaponNames.DisplayName("weapon_xm1014"), Is.EqualTo("XM1014"));
		Assert.That(WeaponNames.DisplayName("weapon_sg556"), Is.EqualTo("SG 553"));
		Assert.That(WeaponNames.DisplayName("something_new"), Is.EqualTo("something_new"));
		Assert.That(WeaponNames.DisplayName(null), Is.Empty);
	}

	[Test]
	public void Map_display_names_cover_known_and_fallback()
	{
		Assert.That(MapNames.DisplayName("de_dust2"), Is.EqualTo("Dust II"));
		Assert.That(MapNames.DisplayName("de_mirage"), Is.EqualTo("Mirage"));
		Assert.That(MapNames.DisplayName("de_cbble"), Is.EqualTo("Cobblestone"));
		Assert.That(MapNames.DisplayName("cs_office"), Is.EqualTo("Office"));
		Assert.That(MapNames.DisplayName("dz_frostbite"), Is.EqualTo("Frostbite"));
		Assert.That(MapNames.DisplayName("workshop/123456/de_newbloom"), Is.EqualTo("Newbloom"));
		Assert.That(MapNames.DisplayName("de_stmarc"), Is.EqualTo("St. Marc"));
		Assert.That(MapNames.DisplayName(null), Is.Empty);
		Assert.That(MapNames.DisplayName("  "), Is.Empty);
	}

	[Test]
	public void Content_helpers_format_deterministically()
	{
		Assert.That(MatchHudContent.FormatClock(95.5), Is.EqualTo("1:35"));
		Assert.That(MatchHudContent.FormatClock(-3), Is.EqualTo("0:00"));
		Assert.That(MatchHudContent.FormatMoney(4700), Is.EqualTo("$4,700"));
		Assert.That(MatchHudWidget.JoinParts("a", "", "b"), Is.EqualTo("a · b"));
		Assert.That(MatchHudWidget.JoinParts(null, " "), Is.Empty);
		Assert.That(MatchHudWidget.NormalizeTeam("ct"), Is.EqualTo("CT"));
		Assert.That(MatchHudWidget.NormalizeTeam("nope"), Is.Empty);
	}

	[Test]
	public void Fresh_service_builds_disconnected_content()
	{
		using var gsi = new GsiService(TestLogger());
		var widget = new MatchHudWidget(gsi, new CsSettingsProvider(), TestLogger());

		var content = widget.BuildContent(MatchHudOptions.Default);

		Assert.That(content.Connected, Is.False);
		Assert.That(widget.GetWidgetTypes().Count, Is.EqualTo(1));
		Assert.That(widget.GetWidgetTypes()[0].Id, Is.EqualTo("match-hud"));
	}

	[Test]
	public void Simulated_match_builds_connected_content()
	{
		using var gsi = new GsiService(TestLogger());
		var widget = new MatchHudWidget(gsi, new CsSettingsProvider(), TestLogger());
		gsi.InjectTestState();

		var content = widget.BuildContent(MatchHudOptions.Default);

		Assert.That(content.Connected, Is.True);
		Assert.That(content.MapLine, Is.EqualTo("MIRAGE · COMPETITIVE"));
		Assert.That(content.RoundText, Is.EqualTo("R5"));
		Assert.That(content.NameLine, Is.EqualTo("TestPlayer"));
		Assert.That(content.CtScore, Is.EqualTo(3));
		Assert.That(content.TScore, Is.EqualTo(1));
		Assert.That(content.HpFrac, Is.EqualTo(1.0));
		Assert.That(content.GearLine, Is.EqualTo("100 · AK-47 · Rifle · 30 / 90 · $800 · 4 / 2 / 1"));
		Assert.That(content.BombText, Is.EqualTo("CARRIED"));
		Assert.That(content.HasBomb, Is.True);
		Assert.That(ResolveEn(content.SessionLine), Is.EqualTo("K 0 · D 0 · 0.00"));
		Assert.That(ResolveEn(content.RoundLine), Is.EqualTo("R5 · +0 · 0"));
		Assert.That(content.HasHistory, Is.False);
		Assert.That(content.HasHpHistory, Is.False);
		Assert.That(content.HasMoneyHistory, Is.False);
		Assert.That(content.HasFeed, Is.False);
		Assert.That(content.HasStreak, Is.False);
		Assert.That(content.TopWeaponLine, Is.Null);
		Assert.That(content.Options, Is.EqualTo(MatchHudOptions.Default));
	}

	[Test]
	public void Previews_build_without_throwing()
	{
		Assert.DoesNotThrow(() => MatchHudPreviews.LiveMatch());
		Assert.DoesNotThrow(() => MatchHudPreviews.BombPlanted());
		Assert.DoesNotThrow(() => MatchHudPreviews.PlayerPage());
		Assert.DoesNotThrow(() => MatchHudPreviews.IntelPage());
		Assert.DoesNotThrow(() => MatchHudPreviews.NoData());
	}

	[Test]
	public void History_dots_map_wins_losses_and_teams()
	{
		var ct = MatchHudHistory.BuildDots("CCT", "CT").ToList();
		Assert.That(ct.Select(dot => dot.Color), Is.EqualTo(["#4ADE80", "#4ADE80", "#F87171"]));

		var t = MatchHudHistory.BuildDots("CCT", "T").ToList();
		Assert.That(t.Select(dot => dot.Color), Is.EqualTo(["#F87171", "#F87171", "#4ADE80"]));

		var unknown = MatchHudHistory.BuildDots("CT?", string.Empty).ToList();
		Assert.That(unknown.Select(dot => dot.Color), Is.EqualTo(["#7DD3FC", "#FCD34D", null]));
		Assert.That(unknown[2].Glyph, Is.EqualTo("○"));

		var longHistory = MatchHudHistory.BuildDots(new string('C', 20), "CT").ToList();
		Assert.That(longHistory.Count, Is.EqualTo(12));
		Assert.That(longHistory[0].Key, Is.EqualTo("8"));

		Assert.That(MatchHudHistory.BuildDots(string.Empty, "CT"), Is.Empty);
	}

	[Test]
	public void History_push_caps_and_round_detection()
	{
		var capped = MatchHudHistory.PushCapped([0.1, 0.2], 1.5, 2).ToList();
		Assert.That(capped, Is.EqualTo([0.2, 1.0]));

		Assert.That(MatchHudHistory.RoundChanged(0, 5), Is.True);
		Assert.That(MatchHudHistory.RoundChanged(5, 5), Is.False);
		Assert.That(MatchHudHistory.RoundChanged(5, 0), Is.False);
	}

	[Test]
	public void Feed_formats_known_events_and_ignores_the_rest()
	{
		Dictionary<string, object?> Kill() => new()
		{
			["player"] = "Me",
			["weapon"] = "ak47",
			["pos-x"] = 1.0,
			["pos-y"] = 2.0,
			["pos-z"] = 3.0,
			["place"] = "Middle",
		};

		Assert.That(MatchHudFeed.Format(GsiEventIds.PlayerKill, Kill()), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.PlayerDied, Kill()), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.BombPlanted, new Dictionary<string, object?> { ["site"] = "B" }), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.BombPlanted, new Dictionary<string, object?>()), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.BombDefused, new Dictionary<string, object?>()), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.BombExploded, new Dictionary<string, object?>()), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.RoundWon, new Dictionary<string, object?>()), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.RoundLost, new Dictionary<string, object?>()), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.StreakMilestone, new Dictionary<string, object?> { ["streak"] = 5.0 }), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.PlaceChanged, new Dictionary<string, object?> { ["place"] = "Middle" }), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.ChatMessage, new Dictionary<string, object?> { ["player"] = "Me", ["text"] = "gl" }), Is.Not.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.RoundStarted, new Dictionary<string, object?>()), Is.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.MatchStarted, new Dictionary<string, object?>()), Is.Null);
	}

	[Test]
	public void Sample_trees_fit_a_three_by_three_tile_without_squeezing_text()
	{
		var names = new[] { "LiveMatch", "BombPlanted", "PlayerPage", "IntelPage", "NoData" };
		var previews = new Func<UiElement>[]
		{
			MatchHudPreviews.LiveMatch,
			MatchHudPreviews.BombPlanted,
			MatchHudPreviews.PlayerPage,
			MatchHudPreviews.IntelPage,
			MatchHudPreviews.NoData,
		};
		for (var i = 0; i < previews.Length; i++)
		{
			var surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>(),
			};
			var view = new UiView(surface, previews[i]());
			var json = JsonSerializer.Serialize(view.Tree);
			if (System.Environment.GetEnvironmentVariable("CSMD_DUMP_TREE") == "1")
			{
				File.WriteAllText(
					$"C:\\Users\\Misu\\AppData\\Local\\Temp\\opencode\\matchhud-tree-{names[i]}.json",
					json);
			}

			var height = WidgetFitEstimator.MeasureRootHeight(json);
			Assert.That(
				height,
				Is.LessThanOrEqualTo(WidgetFitEstimator.BudgetUnits),
				$"Tree is {height:F1} ref units tall on a 3x3 tile with a {WidgetFitEstimator.BudgetUnits} budget, so the reader squeezes rows and clips glyph bottoms. Slim sizes, gaps or rows until it fits.");
		}
	}

	[Test]
	public async Task Tab_change_switches_pages_and_rejects_unknown_ones()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.InjectTestState();
		var widget = new MatchHudWidget(gsi, new CsSettingsProvider(), TestLogger());
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse("""{"showScore":true,"showHistory":true,"showPlayer":true,"showCharts":true,"showStatus":true,"showSession":true,"showFeed":true,"feedCount":3,"compactMode":false}""").RootElement.Clone(),
				},
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);
		Assert.That(session, Is.Not.Null);
		try
		{
			static string TabsId(string treeJson)
			{
				using var document = JsonDocument.Parse(treeJson);
				var queue = new Queue<JsonElement>();
				queue.Enqueue(document.RootElement.GetProperty("Root"));
				while (queue.Count > 0)
				{
					var node = queue.Dequeue();
					if (node.GetProperty("Type").GetString() == "ui.segmented")
					{
						return node.GetProperty("Id").GetString()!;
					}

					foreach (var child in node.GetProperty("Children").EnumerateArray())
					{
						queue.Enqueue(child);
					}
				}

				throw new InvalidOperationException("No segmented tab bar in the tree.");
			}

			static MacroDeck.Ui.Model.Events.UiEvent Change(string nodeId, int index) => new()
			{
				NodeId = nodeId,
				Name = "change",
				Data = JsonDocument.Parse(index.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone(),
			};

			var tabs = TabsId(JsonSerializer.Serialize(session!.BuildTree()));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("match-page"));

			session!.Dispatch(Change(tabs, 9));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("match-page"));

			session!.Dispatch(Change(tabs, 1));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("player-page"));

			session!.Dispatch(Change(tabs, 2));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("intel-page"));

			session!.Dispatch(new MacroDeck.Ui.Model.Events.UiEvent
			{
				NodeId = tabs,
				Name = "change",
				Data = JsonDocument.Parse("\"intel\"").RootElement.Clone(),
			});
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("intel-page"));

			session!.Dispatch(new MacroDeck.Ui.Model.Events.UiEvent
			{
				NodeId = tabs,
				Name = "change",
				Data = JsonDocument.Parse("9").RootElement.Clone(),
			});
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("intel-page"));
		}
		finally
		{
			if (session is IAsyncDisposable asyncDisposable)
			{
				await asyncDisposable.DisposeAsync();
			}
		}
	}

	[Test]
	public async Task Swipe_left_and_right_switch_pages_with_wrap()
	{
		using var gsi = new GsiService(TestLogger());
		gsi.InjectTestState();
		var widget = new MatchHudWidget(gsi, new CsSettingsProvider(), TestLogger());
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse("""{"showScore":true,"showHistory":true,"showPlayer":true,"showCharts":true,"showStatus":true,"showSession":true,"showFeed":true,"feedCount":3,"compactMode":false}""").RootElement.Clone(),
				},
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);
		Assert.That(session, Is.Not.Null);
		try
		{
			static string ScorebugId(string treeJson)
			{
				using var document = JsonDocument.Parse(treeJson);
				var queue = new Queue<JsonElement>();
				queue.Enqueue(document.RootElement.GetProperty("Root"));
				while (queue.Count > 0)
				{
					var node = queue.Dequeue();
					if (node.GetProperty("Type").GetString() == "ui.button"
						&& node.GetProperty("Id").GetString()!.EndsWith("scorebug", StringComparison.Ordinal))
					{
						return node.GetProperty("Id").GetString()!;
					}

					foreach (var child in node.GetProperty("Children").EnumerateArray())
					{
						queue.Enqueue(child);
					}
				}

				throw new InvalidOperationException("No scorebug card in the tree.");
			}

			static MacroDeck.Ui.Model.Events.UiEvent Swipe(string nodeId, string direction) => new()
			{
				NodeId = nodeId,
				Name = "swipe",
				Data = JsonDocument.Parse($"\"{direction}\"").RootElement.Clone(),
			};

			static string TabsId(string treeJson)
			{
				using var document = JsonDocument.Parse(treeJson);
				var queue = new Queue<JsonElement>();
				queue.Enqueue(document.RootElement.GetProperty("Root"));
				while (queue.Count > 0)
				{
					var node = queue.Dequeue();
					if (node.GetProperty("Type").GetString() == "ui.segmented")
					{
						return node.GetProperty("Id").GetString()!;
					}

					foreach (var child in node.GetProperty("Children").EnumerateArray())
					{
						queue.Enqueue(child);
					}
				}

				throw new InvalidOperationException("No segmented tab bar in the tree.");
			}

			var scorebug = ScorebugId(JsonSerializer.Serialize(session!.BuildTree()));
			var tabs = TabsId(JsonSerializer.Serialize(session!.BuildTree()));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("match-page"));

			// The scorebug only exists on the match page; the tab bar is the
			// always-visible swipe zone everywhere else.
			session!.Dispatch(Swipe(scorebug, "left"));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("player-page"));

			session!.Dispatch(Swipe(tabs, "left"));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("intel-page"));

			session!.Dispatch(Swipe(tabs, "left"));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("match-page"));

			session!.Dispatch(Swipe(scorebug, "right"));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("intel-page"));

			session!.Dispatch(Swipe(tabs, "right"));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("player-page"));

			session!.Dispatch(Swipe(tabs, "up"));
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("player-page"));
		}
		finally
		{
			if (session is IAsyncDisposable asyncDisposable)
			{
				await asyncDisposable.DisposeAsync();
			}
		}
	}

	[Test]
	public void Compass_needle_rotates_with_yaw_and_keeps_octant_fallback()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(),
		};
		var state = new UiState<MatchHudContent>(
			MatchHudContent.SampleLive with { Page = MatchHudContent.PageIntel, FacingYaw = 90 });
		var tree = JsonSerializer.Serialize(new UiView(surface, MatchHudPreviews.FromState(state)).Tree);

		Assert.That(tree, Does.Contain("compass-needle"));
		Assert.That(tree, Does.Contain("\"rotation\":90"));
		Assert.That(tree, Does.Contain("compass-arrow"));
	}

	[Test]
	public void Cards_render_gradient_chrome()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(),
		};
		var state = new UiState<MatchHudContent>(MatchHudContent.SampleLive);
		var tree = JsonSerializer.Serialize(new UiView(surface, MatchHudPreviews.FromState(state)).Tree);

		Assert.That(tree, Does.Contain("\"linear\""));
		Assert.That(tree, Does.Contain("#28303F"));
		Assert.That(tree, Does.Contain("scorebug"));
	}

	[Test]
	public void Long_names_shrink_instead_of_clipping()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(),
		};
		var state = new UiState<MatchHudContent>(MatchHudContent.SampleLive);
		var tree = JsonSerializer.Serialize(new UiView(surface, MatchHudPreviews.FromState(state)).Tree);

		Assert.That(tree, Does.Contain("minSize"));
	}

	[Test]
	public async Task Widget_config_mentions_swipe_pages()
	{
		using var gsi = new GsiService(TestLogger());
		var widget = new MatchHudWidget(gsi, new CsSettingsProvider(), TestLogger());
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Config,
				SessionMode = UiSessionModes.Exclusive,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiConfigSurfaceAttributes.EntryPoint] = JsonDocument.Parse($"\"{UiConfigEntryPoints.WidgetConfig}\"").RootElement.Clone(),
				},
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);
		Assert.That(session, Is.Not.Null);
		try
		{
			var tree = JsonSerializer.Serialize(session!.BuildTree());

			Assert.That(tree, Does.Contain("swipeHint"));
			Assert.That(tree, Does.Contain("Widget.Config.SwipeHint"));
		}
		finally
		{
			if (session is IAsyncDisposable asyncDisposable)
			{
				await asyncDisposable.DisposeAsync();
			}
		}
	}

	[Test]
	public void Match_point_detects_leader_overtime_and_open_play()
	{
		Assert.That(MatchHudWidget.MatchPoint(12, 9, "NAVI", "FAZE"), Is.EqualTo("NAVI"));
		Assert.That(MatchHudWidget.MatchPoint(9, 12, "NAVI", "FAZE"), Is.EqualTo("FAZE"));
		Assert.That(MatchHudWidget.MatchPoint(12, 12, "NAVI", "FAZE"), Is.Empty);
		Assert.That(MatchHudWidget.MatchPoint(9, 7, "NAVI", "FAZE"), Is.Null);
		Assert.That(MatchHudWidget.MatchPoint(12, 9, "  ", "FAZE"), Is.EqualTo("CT"));
	}

	[Test]
	public void Yaw_arrow_points_by_octant()
	{
		Assert.That(MatchHudWidget.YawArrow(0), Is.EqualTo("↑"));
		Assert.That(MatchHudWidget.YawArrow(90), Is.EqualTo("→"));
		Assert.That(MatchHudWidget.YawArrow(180), Is.EqualTo("↓"));
		Assert.That(MatchHudWidget.YawArrow(270), Is.EqualTo("←"));
		Assert.That(MatchHudWidget.YawArrow(45), Is.EqualTo("↗"));
		Assert.That(MatchHudWidget.YawArrow(-45), Is.EqualTo("↖"));
		Assert.That(MatchHudWidget.YawArrow(360), Is.EqualTo("↑"));
		Assert.That(MatchHudWidget.YawArrow(null), Is.Empty);
		Assert.That(MatchHudWidget.YawArrow(double.NaN), Is.Empty);
	}

	[Test]
	public void Stat_cards_patch_when_content_changes()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(),
		};
		var state = new UiState<MatchHudContent>(
			MatchHudContent.SampleLive with { Page = MatchHudContent.PagePlayer, Kills = 0 });
		var view = new UiView(surface, MatchHudPreviews.FromState(state));
		view.DrainPatches();

		state.Set(state.Peek() with { Kills = 5, SessionHs = 3 });
		var patches = JsonSerializer.Serialize(view.DrainPatches());

		Assert.That(patches, Does.Contain("stat-k-value"));
		Assert.That(patches, Does.Contain("\"5\""));
		Assert.That(patches, Does.Contain("stat-hs-value"));
		Assert.That(patches, Does.Contain("\"3\""));
	}

	[Test]
	public void Live_phase_renders_live_pill_instead_of_phase_text()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(),
		};
		var state = new UiState<MatchHudContent>(MatchHudContent.SampleLive);
		var live = JsonSerializer.Serialize(new UiView(surface, MatchHudPreviews.FromState(state)).Tree);

		Assert.That(live, Does.Contain("live-pill"));
		Assert.That(live, Does.Not.Contain("\"phase\""));

		var freezetime = JsonSerializer.Serialize(new UiView(surface, MatchHudPreviews.FromState(
			new UiState<MatchHudContent>(MatchHudContent.SampleLive with { MapPhase = "FREEZETIME" }))).Tree);

		Assert.That(freezetime, Does.Not.Contain("live-pill"));
		Assert.That(freezetime, Does.Contain("FREEZETIME"));
	}

	[Test]
	public void Low_hp_renders_low_pill_and_red_number()
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(),
		};
		var state = new UiState<MatchHudContent>(
			MatchHudContent.SampleLive with { Page = MatchHudContent.PagePlayer, HpFrac = 0.2, HpText = "20" });
		var tree = JsonSerializer.Serialize(new UiView(surface, MatchHudPreviews.FromState(state)).Tree);

		Assert.That(tree, Does.Contain("hero-low"));
	}

	private static string ResolveEn(MacroDeck.Localization.LocalizedString text)
	{
		var registry = new MacroDeck.Localization.LocalizationCatalogRegistry();
		registry.Register(Strings.LocalizationCatalog);
		return new MacroDeck.Localization.LocalizationResolver(registry).Resolve(text, "en");
	}

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();
}

using CsMd.Gsi;
using CsMd.Places;
using CsMd.Widgets;
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
		var widget = new MatchHudWidget(gsi, TestLogger());

		var content = widget.BuildContent(MatchHudOptions.Default);

		Assert.That(content.Connected, Is.False);
		Assert.That(widget.GetWidgetTypes().Count, Is.EqualTo(1));
		Assert.That(widget.GetWidgetTypes()[0].Id, Is.EqualTo("match-hud"));
	}

	[Test]
	public void Simulated_match_builds_connected_content()
	{
		using var gsi = new GsiService(TestLogger());
		var widget = new MatchHudWidget(gsi, TestLogger());
		gsi.InjectTestState();

		var content = widget.BuildContent(MatchHudOptions.Default);

		Assert.That(content.Connected, Is.True);
		Assert.That(content.MapLine, Is.EqualTo("MIRAGE · COMPETITIVE"));
		Assert.That(content.RoundText, Is.EqualTo("R5"));
		Assert.That(content.NameLine, Is.EqualTo("TestPlayer"));
		Assert.That(content.CtScore, Is.EqualTo(3));
		Assert.That(content.TScore, Is.EqualTo(1));
		Assert.That(content.HpFrac, Is.EqualTo(1.0));
		Assert.That(content.LoadoutLine, Is.EqualTo("AK-47 · Rifle · 30 / 90"));
		Assert.That(content.MoneyLine, Is.EqualTo("$800 · 4 / 2 / 1"));
		Assert.That(content.BombText, Is.EqualTo("CARRIED"));
		Assert.That(content.HasBomb, Is.True);
		Assert.That(content.SessionLine, Is.EqualTo("K 0 · D 0 · 0.00"));
		Assert.That(content.ArmorLine, Is.EqualTo("100"));
		Assert.That(content.RoundLine, Is.EqualTo("R5 · +0 · 0"));
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
		Assert.That(MatchHudFeed.Format(GsiEventIds.RoundStarted, new Dictionary<string, object?>()), Is.Null);
		Assert.That(MatchHudFeed.Format(GsiEventIds.MatchStarted, new Dictionary<string, object?>()), Is.Null);
	}

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();
}

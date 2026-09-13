using CsMd.Gsi;
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
		Assert.That(options.ShowPlayer, Is.True);
		Assert.That(options.ShowCharts, Is.True);
		Assert.That(options.ShowStatus, Is.True);
		Assert.That(options.ShowFeed, Is.True);
		Assert.That(options.Compact, Is.False);
	}

	[Test]
	public void Content_helpers_format_deterministically()
	{
		Assert.That(MatchHudContent.FormatClock(95.5), Is.EqualTo("1:35"));
		Assert.That(MatchHudContent.FormatClock(-3), Is.EqualTo("0:00"));
		Assert.That(MatchHudContent.FormatMoney(4700), Is.EqualTo("$4,700"));
		Assert.That(MatchHudWidget.JoinParts("a", "", "b"), Is.EqualTo("a · b"));
		Assert.That(MatchHudWidget.JoinParts(null, " "), Is.Empty);
		Assert.That(MatchHudWidget.OrDash(null), Is.EqualTo("-"));
		Assert.That(MatchHudWidget.OrDash("CTs"), Is.EqualTo("CTs"));
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
		Assert.That(content.MapLine, Is.EqualTo("DE_MIRAGE · COMPETITIVE"));
		Assert.That(content.RoundText, Is.EqualTo("R5"));
		Assert.That(content.CtScore, Is.EqualTo(3));
		Assert.That(content.TScore, Is.EqualTo(1));
		Assert.That(content.HpFrac, Is.EqualTo(1.0));
		Assert.That(content.LoadoutLine, Is.EqualTo("ak47 · Rifle · 30 / 90"));
		Assert.That(content.MoneyLine, Is.EqualTo("$800 · 4 / 2 / 1"));
		Assert.That(content.BombText, Is.EqualTo("CARRIED"));
		Assert.That(content.HasBomb, Is.True);
		Assert.That(content.SessionLine, Is.EqualTo("0 / 0 · 0.00"));
		Assert.That(content.ArmorLine, Is.EqualTo("100"));
		Assert.That(content.RoundLine, Is.EqualTo("R5 · +0 · 0"));
		Assert.That(content.HasHistory, Is.False);
		Assert.That(content.HasHpHistory, Is.False);
		Assert.That(content.HasFeed, Is.False);
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

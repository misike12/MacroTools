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
		Assert.That(options.ShowStatus, Is.True);
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
		Assert.That(content.Options, Is.EqualTo(MatchHudOptions.Default));
	}

	[Test]
	public void Previews_build_without_throwing()
	{
		Assert.DoesNotThrow(() => MatchHudPreviews.LiveMatch());
		Assert.DoesNotThrow(() => MatchHudPreviews.BombPlanted());
		Assert.DoesNotThrow(() => MatchHudPreviews.NoData());
	}

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();
}

using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using NUnit.Framework;
using R6Md.Widgets;

namespace R6Md.Tests;

[TestFixture]
public sealed class MatchHudWidgetTests
{
	[Test]
	public void Widget_options_default_all_sections_on()
	{
		var options = R6HudOptions.FromData(default);

		Assert.That(options, Is.EqualTo(R6HudOptions.Default));
		Assert.That(options.ShowScore, Is.True);
		Assert.That(options.ShowRoster, Is.True);
		Assert.That(options.ShowFeed, Is.True);
		Assert.That(options.FeedCount, Is.EqualTo(4));
		Assert.That(options.ShowSession, Is.True);
	}

	[Test]
	public void Previews_build_without_throwing()
	{
		Assert.DoesNotThrow(() => R6HudPreviews.Match());
		Assert.DoesNotThrow(() => R6HudPreviews.NoData());
	}

	[Test]
	public void Widget_trees_fit_a_three_by_three_tile_without_squeezing_text()
	{
		foreach (var preview in new Func<UiElement>[]
		{
			R6HudPreviews.Match,
			R6HudPreviews.NoData,
		})
		{
			var surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>(),
			};
			var view = new UiView(surface, preview());
			var height = WidgetFitEstimator.MeasureRootHeight(JsonSerializer.Serialize(view.Tree));
			Assert.That(
				height,
				Is.LessThanOrEqualTo(WidgetFitEstimator.BudgetUnits),
				$"Tree is {height:F1} ref units tall on a 3x3 tile with a {WidgetFitEstimator.BudgetUnits} budget, so the reader squeezes rows and clips glyph bottoms. Slim sizes, gaps or rows until it fits.");
		}
	}
}

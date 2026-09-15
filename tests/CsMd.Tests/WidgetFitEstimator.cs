using System.Text.Json;

namespace CsMd.Tests;

// Mirrors the host reader's vertical layout just closely enough to catch an
// over-subscribed tree: a text is exactly its font size tall (line-height 1),
// a vertical stack sums its children plus gaps and padding, a horizontal row
// (or grid) is as tall as its tallest child. When the total passes the tile
// budget the reader flex-squeezes every row (min-height 0, max-height 100%,
// overflow hidden), which clips the bottom half of every glyph. That is what
// this guards against: keep the default all-on tree under budget.
internal static class WidgetFitEstimator
{
	// A 3x3 deck widget is laid out in reference units where one cell is 120.
	private const double CellUnits = 120;

	private const double BasisUnits = 3 * CellUnits;

	// Tile height minus the root padding the tree itself declares (0.04), with
	// a little margin left for the tile's own safe-area inset.
	public const double BudgetUnits = BasisUnits - 2 * 0.04 * BasisUnits - 12;

	public static double MeasureRootHeight(string treeJson)
	{
		using var document = JsonDocument.Parse(treeJson);
		var root = document.RootElement.GetProperty("Root");

		return MeasureChildrenHeight(root, vertical: true);
	}

	private static double MeasureChildrenHeight(JsonElement node, bool vertical)
	{
		var properties = node.GetProperty("Properties");
		var gap = Resolve(properties, "gap");
		var padding = Resolve(properties, "padding");
		var heights = node.GetProperty("Children").EnumerateArray().Select(MeasureNodeHeight).ToList();

		if (heights.Count == 0)
		{
			return 2 * padding;
		}

		var inner = vertical
			? heights.Sum() + gap * (heights.Count - 1)
			: heights.Max();

		return inner + 2 * padding;
	}

	private static double MeasureNodeHeight(JsonElement node)
	{
		var properties = node.GetProperty("Properties");
		return node.GetProperty("Type").GetString() switch
		{
			"ui.text" => ResolveRequired(properties, "size"),
			"ui.stack" => MeasureChildrenHeight(node, vertical: IsVertical(properties)),
			"ui.grid" => MeasureChildrenHeight(node, vertical: false),
			// A layer draws every child across the same box, so it is as tall
			// as its own main size, never the sum of its children.
			"ui.layer" => ResolveRequired(properties, "mainSize"),
			"ui.gauge" => ResolveRequired(properties, "mainSize"),
			"ui.chart" => ResolveRequired(properties, "mainSize"),
			"ui.range-bar" => Resolve(properties, "thickness"),
			"macrodeck.progress-bar" => Resolve(properties, "thickness"),
			"macrodeck.progress-text" => Resolve(properties, "size"),
			"ui.shape" => ResolveRequired(properties, "mainSize"),
			var unknown => throw new InvalidOperationException($"Fit estimator does not know node type '{unknown}'."),
		};
	}

	private static bool IsVertical(JsonElement properties) =>
		!properties.TryGetProperty("direction", out var direction) || direction.GetString() != "horizontal";

	private static double Resolve(JsonElement properties, string name)
	{
		if (!properties.TryGetProperty(name, out var length))
		{
			return 0;
		}

		var resolved = length.GetProperty("basis").GetDouble() * BasisUnits;
		if (length.TryGetProperty("maxOfCell", out var cap))
		{
			resolved = Math.Min(resolved, cap.GetDouble() * CellUnits);
		}

		return resolved;
	}

	private static double ResolveRequired(JsonElement properties, string name)
	{
		if (!properties.TryGetProperty(name, out _))
		{
			throw new InvalidOperationException($"Fit estimator expected a '{name}' property.");
		}

		return Resolve(properties, name);
	}
}

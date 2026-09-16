using System.Text.Json;

namespace R6Md.Tests;

// Mirrors the host reader's vertical layout just closely enough to catch an
// over-subscribed tree: a text is exactly its font size tall (line-height 1),
// a vertical stack sums its children plus gaps and padding, a horizontal row
// is as tall as its tallest child, and a grid is its declared main size - or,
// without one, square cells sized from its width. When the total passes the
// tile budget the reader flex-squeezes every row (min-height 0, max-height
// 100%, overflow hidden), which clips the bottom half of every glyph and lets
// fixed-size glyphs (icons) spill out over their neighbours. That is what this
// guards against: keep every page under budget.
internal static class WidgetFitEstimator
{
	// A 3x3 deck widget is laid out in reference units where one cell is 120.
	private const double CellUnits = 120;

	private const double BasisUnits = 3 * CellUnits;

	// Tile height minus the root padding the tree itself declares (0.035), with
	// a little margin left for the tile's own safe-area inset.
	public const double BudgetUnits = BasisUnits - 2 * 0.035 * BasisUnits - 12;

	public static double MeasureRootHeight(string treeJson)
	{
		using var document = JsonDocument.Parse(treeJson);
		var root = document.RootElement.GetProperty("Root");
		var width = BasisUnits - 2 * Resolve(root.GetProperty("Properties"), "padding");

		return MeasureChildrenHeight(root, vertical: true, width);
	}

	private static double MeasureChildrenHeight(JsonElement node, bool vertical, double width)
	{
		var properties = node.GetProperty("Properties");
		var gap = Resolve(properties, "gap");
		var padding = Resolve(properties, "padding");
		var heights = node.GetProperty("Children").EnumerateArray().Select(child => MeasureNodeHeight(child, width)).ToList();

		if (heights.Count == 0)
		{
			return 2 * padding;
		}

		var inner = vertical
			? heights.Sum() + gap * (heights.Count - 1)
			: heights.Max();

		return inner + 2 * padding;
	}

	private static double MeasureNodeHeight(JsonElement node, double width)
	{
		var properties = node.GetProperty("Properties");
		return node.GetProperty("Type").GetString() switch
		{
			"ui.text" => ResolveRequired(properties, "size"),
			"ui.stack" => MeasureChildrenHeight(node, vertical: IsVertical(properties), width),
			"ui.grid" => MeasureGridHeight(node, properties, width),
			// A layer draws every child across the same box, so it is as tall
			// as its own main size, never the sum of its children.
			"ui.layer" => ResolveRequired(properties, "mainSize"),
			// A transform is visual only; its box is its main size, or its
			// tallest child when it declares none.
			"ui.transform" => MeasureTransformHeight(node, properties, width),
			// Segmented controls and buttons carry no content extent on the
			// parent axis: segmented takes its main size, a button its children.
			"ui.segmented" => ResolveRequired(properties, "mainSize"),
			"ui.button" => MeasureChildrenHeight(node, vertical: IsVertical(properties), width),
			// An icon draws its glyph into an explicit box: without a main
			// size the box collapses and the glyph spills out over the page.
			"ui.icon" => MeasureIconHeight(properties),
			"ui.gauge" => ResolveRequired(properties, "mainSize"),
			"ui.chart" => ResolveRequired(properties, "mainSize"),
			"ui.range-bar" => Resolve(properties, "thickness"),
			"macrodeck.progress-bar" => Resolve(properties, "thickness"),
			"macrodeck.progress-text" => Resolve(properties, "size"),
			"ui.shape" => ResolveRequired(properties, "mainSize"),
			var unknown => throw new InvalidOperationException($"Fit estimator does not know node type '{unknown}'."),
		};
	}

	private static double MeasureGridHeight(JsonElement node, JsonElement properties, double width)
	{
		if (properties.TryGetProperty("mainSize", out _))
		{
			return ResolveRequired(properties, "mainSize");
		}

		// No declared height: the reader makes square cells from the width.
		var columns = properties.TryGetProperty("columns", out var cols) ? Math.Max(1, cols.GetInt32()) : 1;
		var count = node.GetProperty("Children").EnumerateArray().Count();
		var rows = Math.Max(1, (count + columns - 1) / columns);
		var gap = Resolve(properties, "gap");
		var padding = Resolve(properties, "padding");
		var cell = Math.Max(0, (width - 2 * padding - (columns - 1) * gap) / columns);

		return 2 * padding + rows * cell + (rows - 1) * gap;
	}

	private static bool IsVertical(JsonElement properties) =>
		!properties.TryGetProperty("direction", out var direction) || direction.GetString() != "horizontal";

	private static double MaxChildHeight(JsonElement node, double width)
	{
		var heights = node.GetProperty("Children").EnumerateArray().Select(child => MeasureNodeHeight(child, width)).ToList();
		return heights.Count == 0 ? 0 : heights.Max();
	}

	private static double MeasureTransformHeight(JsonElement node, JsonElement properties, double width)
	{
		var declared = Resolve(properties, "mainSize");
		return declared > 0 ? declared : MaxChildHeight(node, width);
	}

	private static double MeasureIconHeight(JsonElement properties)
	{
		if (properties.TryGetProperty("mainSize", out _))
		{
			return ResolveRequired(properties, "mainSize");
		}

		throw new InvalidOperationException("Fit estimator requires every ui.icon to declare mainSize so its glyph box cannot collapse.");
	}

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

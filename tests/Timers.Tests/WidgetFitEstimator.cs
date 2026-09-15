using System.Text.Json;

namespace Timers.Tests;

// Same guardrail as CS:MD's fit estimator: a text is exactly its font size
// tall (times its line count), a vertical stack sums its children, and when
// the total passes the tile budget the reader flex-squeezes rows and clips
// glyph bottoms. Keep every focus-timer state under budget.
internal static class WidgetFitEstimator
{
	private const double CellUnits = 120;

	private const double BasisUnits = 3 * CellUnits;

	public const double BudgetUnits = BasisUnits - 2 * 0.07 * BasisUnits - 12;

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
			"ui.text" => ResolveRequired(properties, "size") * MaxLines(properties),
			"ui.stack" => MeasureChildrenHeight(node, vertical: IsVertical(properties), width),
			"ui.button" => MeasureChildrenHeight(node, vertical: IsVertical(properties), width),
			"ui.range-bar" => Resolve(properties, "thickness"),
			"macrodeck.progress-bar" => Resolve(properties, "thickness"),
			"macrodeck.progress-text" => Resolve(properties, "size"),
			var unknown => throw new InvalidOperationException($"Fit estimator does not know node type '{unknown}'."),
		};
	}

	private static bool IsVertical(JsonElement properties) =>
		!properties.TryGetProperty("direction", out var direction) || direction.GetString() != "horizontal";

	private static int MaxLines(JsonElement properties) =>
		properties.TryGetProperty("maxLines", out var lines) && lines.TryGetInt32(out var count) && count > 1 ? count : 1;

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

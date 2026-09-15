using System.Text.Json;

namespace ScreenControl.Tests;

// Same guardrail as the other plugins: a text is exactly its font size
// tall, a vertical stack sums its children, and when the total passes the
// tile budget the reader flex-squeezes rows and clips glyph bottoms. Keep
// every brightness state under budget.
internal static class WidgetFitEstimator
{
	private const double CellUnits = 120;

	private const double BasisUnits = 3 * CellUnits;

	public const double BudgetUnits = BasisUnits - 2 * 0.06 * BasisUnits - 12;

	public static double MeasureRootHeight(string treeJson) =>
		MeasureRootHeight(treeJson, BasisUnits, BudgetUnits);

	public static double MeasureRootHeight(string treeJson, double basisUnits, double budgetUnits)
	{
		using var document = JsonDocument.Parse(treeJson);
		var root = document.RootElement.GetProperty("Root");
		var width = basisUnits - 2 * Resolve(root.GetProperty("Properties"), "padding", basisUnits);

		return MeasureChildrenHeight(root, vertical: true, width, basisUnits);
	}

	private static double MeasureChildrenHeight(JsonElement node, bool vertical, double width, double basisUnits)
	{
		var properties = node.GetProperty("Properties");
		var gap = Resolve(properties, "gap", basisUnits);
		var padding = Resolve(properties, "padding", basisUnits);
		var heights = node.GetProperty("Children").EnumerateArray().Select(child => MeasureNodeHeight(child, width, basisUnits)).ToList();

		if (heights.Count == 0)
		{
			return 2 * padding;
		}

		var inner = vertical
			? heights.Sum() + gap * (heights.Count - 1)
			: heights.Max();

		return inner + 2 * padding;
	}

	private static double MeasureNodeHeight(JsonElement node, double width, double basisUnits)
	{
		var properties = node.GetProperty("Properties");
		return node.GetProperty("Type").GetString() switch
		{
			"ui.text" => ResolveRequired(properties, "size", basisUnits),
			"ui.stack" => MeasureChildrenHeight(node, vertical: IsVertical(properties), width, basisUnits),
			"ui.button" => MeasureChildrenHeight(node, vertical: IsVertical(properties), width, basisUnits),
			"ui.slider" => ResolveRequired(properties, "mainSize", basisUnits),
			"ui.range-bar" => Resolve(properties, "thickness", basisUnits),
			var unknown => throw new InvalidOperationException($"Fit estimator does not know node type '{unknown}'."),
		};
	}

	private static bool IsVertical(JsonElement properties) =>
		!properties.TryGetProperty("direction", out var direction) || direction.GetString() != "horizontal";

	private static double Resolve(JsonElement properties, string name, double basisUnits = BasisUnits)
	{
		if (!properties.TryGetProperty(name, out var length))
		{
			return 0;
		}

		var resolved = length.GetProperty("basis").GetDouble() * basisUnits;
		if (length.TryGetProperty("maxOfCell", out var cap))
		{
			resolved = Math.Min(resolved, cap.GetDouble() * CellUnits);
		}

		return resolved;
	}

	private static double ResolveRequired(JsonElement properties, string name, double basisUnits = BasisUnits)
	{
		if (!properties.TryGetProperty(name, out _))
		{
			throw new InvalidOperationException($"Fit estimator expected a '{name}' property.");
		}

		return Resolve(properties, name, basisUnits);
	}
}

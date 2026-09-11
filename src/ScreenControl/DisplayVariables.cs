using MacroDeck.Sdk.Variables;

namespace ScreenControl;

internal static class DisplayVariables
{
	public static IReadOnlyList<VariableDefinition> CreateDefinitions() =>
	[
		Eager("monitor-count", VariableType.Numeric, Strings.Variables.MonitorCount.DisplayName(), Strings.Variables.MonitorCount.Description(), refresh: TimeSpan.FromSeconds(30)),
		VariableDefinition.Eager("primary-brightness", VariableType.Numeric) with
		{
			Name = "primary_brightness",
			DisplayName = Strings.Variables.PrimaryBrightness.DisplayName(),
			Description = Strings.Variables.PrimaryBrightness.Description(),
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			RefreshInterval = TimeSpan.FromSeconds(5),
			Write = new VariableWriteCapability(),
		},
		Eager("focused-window-title", VariableType.Text, Strings.Variables.FocusedWindowTitle.DisplayName(), Strings.Variables.FocusedWindowTitle.Description(), refresh: TimeSpan.FromSeconds(2)),
		Eager("focused-window-process", VariableType.Text, Strings.Variables.FocusedWindowProcess.DisplayName(), Strings.Variables.FocusedWindowProcess.Description(), refresh: TimeSpan.FromSeconds(2)),
		Eager("primary-input", VariableType.Text, Strings.Variables.PrimaryInput.DisplayName(), Strings.Variables.PrimaryInput.Description(), refresh: TimeSpan.FromSeconds(5)),
		VariableDefinition.Eager("focused-window-topmost", VariableType.Boolean) with
		{
			Name = "focused_window_topmost",
			DisplayName = Strings.Variables.FocusedTopmost.DisplayName(),
			Description = Strings.Variables.FocusedTopmost.Description(),
			Unit = string.Empty,
			SemanticKind = VariableSemanticKinds.None,
			RefreshInterval = TimeSpan.FromSeconds(2),
			Write = new VariableWriteCapability(),
		},
	];

	private static VariableDefinition Eager(
		string id,
		VariableType type,
		MacroDeck.Localization.LocalizedText displayName,
		MacroDeck.Localization.LocalizedText description,
		TimeSpan? refresh = null) =>
		VariableDefinition.Eager(id, type) with
		{
			Name = id.Replace("-", "_"),
			DisplayName = displayName,
			Description = description,
			Unit = string.Empty,
			SemanticKind = VariableSemanticKinds.None,
			RefreshInterval = refresh,
		};

	public static string MonitorBrightnessId(int index) => $"monitor-{index}-brightness";

	public static VariableDefinition MonitorBrightness(int index)
	{
		var number = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
		return VariableDefinition.OnDemand(MonitorBrightnessId(index), VariableType.Numeric) with
		{
			Name = $"monitor_{index}_brightness",
			DisplayName = Strings.Variables.MonitorBrightness.DisplayName(number),
			Description = Strings.Variables.MonitorBrightness.Description(number),
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			RefreshInterval = TimeSpan.FromSeconds(5),
			IsBindable = true,
			IsContainer = false,
			Write = new VariableWriteCapability(),
		};
	}

	public static bool TryParseMonitorBrightnessId(string localId, out int index)
	{
		index = 0;
		const string prefix = "monitor-";
		const string suffix = "-brightness";
		if (!localId.StartsWith(prefix, StringComparison.Ordinal)
			|| !localId.EndsWith(suffix, StringComparison.Ordinal))
		{
			return false;
		}

		var middle = localId.Substring(prefix.Length, localId.Length - prefix.Length - suffix.Length);
		return int.TryParse(middle, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index)
			&& index >= 1 && index <= 9;
	}
}

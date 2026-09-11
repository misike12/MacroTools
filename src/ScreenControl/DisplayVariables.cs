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
}

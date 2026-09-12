using MacroDeck.Sdk.Variables;

namespace Timers;

internal static class TimerVariables
{
	private static readonly TimeSpan FastRefresh = TimeSpan.FromSeconds(1);

	public static IReadOnlyList<VariableDefinition> CreateDefinitions() =>
	[
		Eager("countdown-remaining-seconds", VariableType.Numeric, Strings.Variables.CountdownRemaining.DisplayName(), Strings.Variables.CountdownRemaining.Description(), unit: "s", semanticKind: VariableSemanticKinds.Duration, refresh: FastRefresh),
		Eager("countdown-text", VariableType.Text, Strings.Variables.CountdownText.DisplayName(), Strings.Variables.CountdownText.Description(), refresh: FastRefresh),
		Eager("countdown-running", VariableType.Boolean, Strings.Variables.CountdownRunning.DisplayName(), Strings.Variables.CountdownRunning.Description(), refresh: FastRefresh),
		Eager("countdown-label", VariableType.Text, Strings.Variables.CountdownLabel.DisplayName(), Strings.Variables.CountdownLabel.Description(), refresh: FastRefresh),
		Eager("countdown-progress-percent", VariableType.Numeric, Strings.Variables.CountdownProgress.DisplayName(), Strings.Variables.CountdownProgress.Description(), unit: "%", semanticKind: VariableSemanticKinds.Percentage, refresh: FastRefresh),
		Eager("stopwatch-elapsed-seconds", VariableType.Numeric, Strings.Variables.StopwatchElapsed.DisplayName(), Strings.Variables.StopwatchElapsed.Description(), unit: "s", semanticKind: VariableSemanticKinds.Duration, refresh: FastRefresh),
		Eager("stopwatch-text", VariableType.Text, Strings.Variables.StopwatchText.DisplayName(), Strings.Variables.StopwatchText.Description(), refresh: FastRefresh),
		Eager("stopwatch-running", VariableType.Boolean, Strings.Variables.StopwatchRunning.DisplayName(), Strings.Variables.StopwatchRunning.Description(), refresh: FastRefresh),
		Eager("pomodoro-phase", VariableType.Text, Strings.Variables.PomodoroPhase.DisplayName(), Strings.Variables.PomodoroPhase.Description(), refresh: FastRefresh),
		Eager("pomodoro-remaining-seconds", VariableType.Numeric, Strings.Variables.PomodoroRemaining.DisplayName(), Strings.Variables.PomodoroRemaining.Description(), unit: "s", semanticKind: VariableSemanticKinds.Duration, refresh: FastRefresh),
		Eager("pomodoro-phase-text", VariableType.Text, Strings.Variables.PomodoroPhaseText.DisplayName(), Strings.Variables.PomodoroPhaseText.Description(), refresh: FastRefresh),
		Eager("pomodoro-label", VariableType.Text, Strings.Variables.PomodoroLabel.DisplayName(), Strings.Variables.PomodoroLabel.Description(), refresh: FastRefresh),
		Eager("pomodoro-round", VariableType.Numeric, Strings.Variables.PomodoroRound.DisplayName(), Strings.Variables.PomodoroRound.Description(), refresh: FastRefresh),
		Eager("pomodoro-running", VariableType.Boolean, Strings.Variables.PomodoroRunning.DisplayName(), Strings.Variables.PomodoroRunning.Description(), refresh: FastRefresh),
		Eager("pomodoro-progress-percent", VariableType.Numeric, Strings.Variables.PomodoroProgress.DisplayName(), Strings.Variables.PomodoroProgress.Description(), unit: "%", semanticKind: VariableSemanticKinds.Percentage, refresh: FastRefresh),
	];

	private static VariableDefinition Eager(
		string id,
		VariableType type,
		MacroDeck.Localization.LocalizedText displayName,
		MacroDeck.Localization.LocalizedText description,
		string? unit = null,
		string? semanticKind = null,
		TimeSpan? refresh = null) =>
		VariableDefinition.Eager(id, type) with
		{
			Name = id.Replace("-", "_"),
			DisplayName = displayName,
			Description = description,
			Unit = unit ?? string.Empty,
			SemanticKind = semanticKind ?? VariableSemanticKinds.None,
			RefreshInterval = refresh,
		};
}

using System.Text.Json;

namespace Timers.Messaging;

// Message-bus topics for the Timers integration (Macro Deck 3 beta.12 channel).
// Topics are a stable public contract: keep them stable and additive.
public static class TimerMessageTopics
{
	public const string CountdownFinished = "timers.countdown.finished";
	public const string PomodoroPhaseChanged = "timers.pomodoro.phase-changed";
	public const string StateGet = "timers.state.get";
}

internal static class TimerMessageJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record CountdownFinishedMessage(string Label, double Seconds);

public sealed record PomodoroPhaseMessage(string Phase, double Round, string Label);

public sealed record TimerStateMessage(
	double CountdownRemainingSeconds,
	string CountdownText,
	bool CountdownRunning,
	string? CountdownLabel,
	double CountdownProgressPercent,
	double StopwatchElapsedSeconds,
	string StopwatchText,
	bool StopwatchRunning,
	string PomodoroPhase,
	double PomodoroRemainingSeconds,
	string PomodoroPhaseText,
	string? PomodoroLabel,
	double PomodoroRound,
	bool PomodoroRunning,
	double PomodoroProgressPercent);

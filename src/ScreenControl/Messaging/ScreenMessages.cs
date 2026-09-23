using System.Text.Json;

namespace ScreenControl.Messaging;

// Message-bus topics for the Screen Control integration (Macro Deck 3 beta.12
// channel). Topics are a stable public contract: keep them stable and additive.
public static class ScreenMessageTopics
{
	public const string MonitorsChanged = "screen.monitors.changed";
	public const string StateGet = "screen.state.get";
}

internal static class ScreenMessageJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record MonitorsChangedMessage(double MonitorCount, string[] Monitors);

public sealed record ScreenStateMessage(
	double MonitorCount,
	double? PrimaryBrightness,
	string? PrimaryInput,
	string? FocusedTitle,
	string? FocusedProcess);

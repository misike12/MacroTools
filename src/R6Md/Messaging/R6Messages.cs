using System.Text.Json;

namespace R6Md.Messaging;

// Message-bus topics for the R6MD integration (Macro Deck 3 beta.12 channel).
// Match events mirror 1:1 onto r6md.<event-id>; topics are a stable public
// contract, so keep them stable and additive.
public static class R6MessageTopics
{
	public const string Prefix = "r6md.";
	public const string StateGet = "r6md.state.get";

	public static string ForEvent(string eventId) => Prefix + eventId;
}

internal static class R6MessageJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record R6StateMessage(
	bool Connected,
	bool HasMatch,
	string? MapName,
	string? MapMode,
	double RoundNumber,
	double YourScore,
	double OppScore,
	string? RoundHistory,
	string? LastKiller,
	string? LastVictim,
	double SessionKills,
	double SessionDeaths,
	double SessionAssists,
	double Streak,
	double BestStreak,
	string? MatchOutcome,
	string? OwPhase,
	double YourHp);

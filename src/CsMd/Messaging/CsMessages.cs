using System.Text.Json;

namespace CsMd.Messaging;

// Message-bus topics for the CS:MD integration (Macro Deck 3 beta.12 channel).
// Match events mirror 1:1 onto csmd.<event-id>; topics are a stable public
// contract, so keep them stable and additive.
public static class CsMessageTopics
{
	public const string Prefix = "csmd.";
	public const string ScoreGet = "csmd.score.get";

	public static string ForEvent(string eventId) => Prefix + eventId;
}

internal static class CsMessageJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record CsScoreMessage(
	bool Connected,
	string? MapName,
	string? MapMode,
	string? MapPhase,
	double MapRound,
	double CtScore,
	double TScore,
	string? CtName,
	string? TName,
	string? RoundPhase,
	string? BombState);

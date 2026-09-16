using System.Text.Json;
using System.Text.Json.Serialization;

namespace R6Md.Replays;

// Tolerant DTOs for the r6-dissect JSON surface (see Tools/README.md). Every
// member is optional-by-construction: unknown fields are ignored, missing
// ones fall back, and the two feedback-type spellings ({"name":..} on current
// builds, bare strings on old ones) both read. A season that renames
// something degrades one field, never the whole snapshot.
public sealed record ReplayMatch(
	[property: JsonPropertyName("gameVersion")] string? GameVersion,
	[property: JsonPropertyName("timestamp")] string? Timestamp,
	[property: JsonPropertyName("matchType")] ReplayNamed? MatchType,
	[property: JsonPropertyName("map")] ReplayNamed? Map,
	[property: JsonPropertyName("site")] string? Site,
	[property: JsonPropertyName("gamemode")] ReplayNamed? Gamemode,
	[property: JsonPropertyName("roundsPerMatch")] int? RoundsPerMatch,
	[property: JsonPropertyName("roundsPerMatchOvertime")] int? RoundsPerMatchOvertime,
	[property: JsonPropertyName("roundNumber")] int? RoundNumber,
	[property: JsonPropertyName("overtimeRoundNumber")] int? OvertimeRoundNumber,
	[property: JsonPropertyName("teams")] IReadOnlyList<ReplayTeam>? Teams,
	[property: JsonPropertyName("players")] IReadOnlyList<ReplayPlayer>? Players,
	[property: JsonPropertyName("matchID")] string? MatchId,
	[property: JsonPropertyName("recordingPlayerID")] JsonElement? RecordingPlayerId,
	[property: JsonPropertyName("matchFeedback")] IReadOnlyList<ReplayFeedback>? MatchFeedback,
	[property: JsonPropertyName("stats")] IReadOnlyList<ReplayPlayerStat>? Stats);

public sealed record ReplayNamed(
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("id")] JsonElement? Id);

public sealed record ReplayTeam(
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("startingScore")] int? StartingScore,
	[property: JsonPropertyName("score")] int? Score,
	[property: JsonPropertyName("won")] bool? Won,
	[property: JsonPropertyName("winCondition")] string? WinCondition,
	[property: JsonPropertyName("role")] string? Role);

public sealed record ReplayPlayer(
	[property: JsonPropertyName("id")] JsonElement? Id,
	[property: JsonPropertyName("username")] string? Username,
	[property: JsonPropertyName("teamIndex")] int? TeamIndex,
	[property: JsonPropertyName("operator")] ReplayNamed? Operator);

public sealed record ReplayFeedback(
	[property: JsonPropertyName("type")] JsonElement? Type,
	[property: JsonPropertyName("username")] string? Username,
	[property: JsonPropertyName("target")] string? Target,
	[property: JsonPropertyName("headshot")] bool? Headshot,
	[property: JsonPropertyName("timeInSeconds")] int? TimeInSeconds,
	[property: JsonPropertyName("operator")] ReplayNamed? Operator,
	[property: JsonPropertyName("message")] string? Message)
{
	public string TypeName =>
		Type is { ValueKind: JsonValueKind.Object } obj
		&& obj.TryGetProperty("name", out var name)
			? name.GetString() ?? string.Empty
			: Type is { ValueKind: JsonValueKind.String } str
				? str.GetString() ?? string.Empty
				: string.Empty;
}

public sealed record ReplayPlayerStat(
	[property: JsonPropertyName("username")] string? Username,
	[property: JsonPropertyName("score")] int? Score,
	[property: JsonPropertyName("kills")] int? Kills,
	[property: JsonPropertyName("died")] bool? Died,
	[property: JsonPropertyName("assists")] int? Assists,
	[property: JsonPropertyName("headshots")] int? Headshots,
	[property: JsonPropertyName("headshotPercentage")] int? HeadshotPercentage,
	[property: JsonPropertyName("damageTaken")] int? DamageTaken,
	[property: JsonPropertyName("damageDealt")] int? DamageDealt,
	[property: JsonPropertyName("secondsAlive")] int? SecondsAlive);

public static class ReplayJson
{
	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = false,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	public static ReplayMatch? ParseMatch(string json)
	{
		try
		{
			return JsonSerializer.Deserialize<ReplayMatch>(json, Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static string PlayerKey(JsonElement? id) => id switch
	{
		{ ValueKind: JsonValueKind.Number } n => n.GetRawText(),
		{ ValueKind: JsonValueKind.String } s => s.GetString() ?? string.Empty,
		_ => string.Empty,
	};
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CsMd.Gsi;

// Mirror of what CS2 actually POSTs (see the GSI reference the README links). Everything
// is optional: menus send almost nothing, live rounds send everything, and individual
// numbers sometimes arrive as strings, so the shared options below stay permissive and
// unknown members are ignored.
public static class GsiJson
{
	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		AllowTrailingCommas = true,
	};
}

public sealed record GsiPayload(
	[property: JsonPropertyName("auth")] GsiAuth? Auth,
	[property: JsonPropertyName("provider")] GsiProvider? Provider,
	[property: JsonPropertyName("map")] GsiMap? Map,
	[property: JsonPropertyName("round")] GsiRound? Round,
	[property: JsonPropertyName("player")] GsiPlayer? Player,
	[property: JsonPropertyName("allplayers")] Dictionary<string, GsiPlayer>? AllPlayers,
	[property: JsonPropertyName("phase_countdowns")] GsiPhaseCountdowns? PhaseCountdowns,
	[property: JsonPropertyName("grenades")] Dictionary<string, GsiGrenade>? Grenades,
	[property: JsonPropertyName("bomb")] GsiBomb? Bomb);

public sealed record GsiAuth(
	[property: JsonPropertyName("token")] string? Token);

public sealed record GsiProvider(
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("appid")] int AppId,
	[property: JsonPropertyName("version")] int Version,
	[property: JsonPropertyName("steamid")] string? SteamId,
	[property: JsonPropertyName("timestamp")] long Timestamp);

public sealed record GsiTeam(
	[property: JsonPropertyName("score")] int Score,
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("timeouts_remaining")] int TimeoutsRemaining,
	[property: JsonPropertyName("matches_won_this_series")] int MatchesWonThisSeries);

public sealed record GsiMap(
	[property: JsonPropertyName("mode")] string? Mode,
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("phase")] string? Phase,
	[property: JsonPropertyName("round")] int Round,
	[property: JsonPropertyName("team_ct")] GsiTeam? TeamCt,
	[property: JsonPropertyName("team_t")] GsiTeam? TeamT,
	[property: JsonPropertyName("num_matches_to_win_series")] int NumMatchesToWinSeries);

public sealed record GsiRound(
	[property: JsonPropertyName("phase")] string? Phase,
	[property: JsonPropertyName("win_team")] string? WinTeam,
	[property: JsonPropertyName("bomb")] string? Bomb);

public sealed record GsiPlayerState(
	[property: JsonPropertyName("health")] int Health,
	[property: JsonPropertyName("armor")] int Armor,
	[property: JsonPropertyName("helmet")] bool Helmet,
	[property: JsonPropertyName("flashed")] int Flashed,
	[property: JsonPropertyName("smoked")] int Smoked,
	[property: JsonPropertyName("burning")] int Burning,
	[property: JsonPropertyName("money")] int Money,
	[property: JsonPropertyName("round_kills")] int RoundKills,
	[property: JsonPropertyName("round_killhs")] int RoundHeadshots,
	[property: JsonPropertyName("round_totaldmg")] int RoundDamage,
	[property: JsonPropertyName("equip_value")] int EquipmentValue,
	[property: JsonPropertyName("defusekit")] bool DefuseKit);

public sealed record GsiWeapon(
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("paintkit")] string? PaintKit,
	[property: JsonPropertyName("type")] string? Type,
	[property: JsonPropertyName("state")] string? State,
	[property: JsonPropertyName("ammo_clip")] int? AmmoClip,
	[property: JsonPropertyName("ammo_clip_max")] int? AmmoClipMax,
	[property: JsonPropertyName("ammo_reserve")] int? AmmoReserve);

public sealed record GsiMatchStats(
	[property: JsonPropertyName("kills")] int Kills,
	[property: JsonPropertyName("assists")] int Assists,
	[property: JsonPropertyName("deaths")] int Deaths,
	[property: JsonPropertyName("mvps")] int Mvps,
	[property: JsonPropertyName("score")] int Score);

public sealed record GsiPlayer(
	[property: JsonPropertyName("steamid")] string? SteamId,
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("clan")] string? Clan,
	[property: JsonPropertyName("observer_slot")] int ObserverSlot,
	[property: JsonPropertyName("team")] string? Team,
	[property: JsonPropertyName("activity")] string? Activity,
	[property: JsonPropertyName("state")] GsiPlayerState? State,
	[property: JsonPropertyName("weapons")] Dictionary<string, GsiWeapon>? Weapons,
	[property: JsonPropertyName("match_stats")] GsiMatchStats? MatchStats,
	[property: JsonPropertyName("spectarget")] string? SpectateTarget,
	[property: JsonPropertyName("position")] string? Position);

public sealed record GsiPhaseCountdowns(
	[property: JsonPropertyName("phase")] string? Phase,
	[property: JsonPropertyName("phase_ends_in")] double PhaseEndsIn);

public sealed record GsiGrenade(
	[property: JsonPropertyName("owner")] string? Owner,
	[property: JsonPropertyName("type")] string? Type,
	[property: JsonPropertyName("lifetime")] double Lifetime,
	[property: JsonPropertyName("effecttime")] double EffectTime,
	[property: JsonPropertyName("flames")] Dictionary<string, object?>? Flames);

public sealed record GsiBomb(
	[property: JsonPropertyName("state")] string? State,
	[property: JsonPropertyName("player")] string? Player,
	[property: JsonPropertyName("countdown")] double? Countdown);


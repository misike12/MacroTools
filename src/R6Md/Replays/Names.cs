namespace R6Md.Replays;

// Display-name tables for the game's own vocabulary. Anything unknown falls
// back to a cleaned-up raw value, never to empty: operators and maps arrive
// every season, and a new one must read sensibly on day one.
public static class MapNames
{
	public static string Display(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}

		var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
		{
			return raw.Trim();
		}

		return string.Join(' ', parts.Select(part =>
			part.Length <= 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
	}

	public static string Mode(string? raw) => string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();

	public static string WinCondition(string? raw) => raw?.Trim().ToLowerInvariant() switch
	{
		"killedopponents" or "team_has_been_eliminated" => "Elimination",
		"time" or "time_has_expired" => "Time",
		"bombdetonated" or "bomb_detonated" => "Detonation",
		"bombdefused" or "bomb_defused" => "Defuse",
		"objectivesecured" or "objective_secured" => "Objective",
		_ => raw?.Trim() ?? string.Empty,
	};
}

public static class OperatorNames
{
	private static readonly Dictionary<string, string> DisplayByRaw = new(StringComparer.OrdinalIgnoreCase)
	{
		["Sledge"] = "Sledge", ["Thatcher"] = "Thatcher", ["Ash"] = "Ash", ["Thermite"] = "Thermite",
		["Twitch"] = "Twitch", ["Montagne"] = "Montagne", ["Glaz"] = "Glaz", ["Fuze"] = "Fuze",
		["Blitz"] = "Blitz", ["IQ"] = "IQ", ["Buck"] = "Buck", ["Blackbeard"] = "Blackbeard",
		["Capitao"] = "Capitão", ["Hibana"] = "Hibana", ["Jackal"] = "Jackal", ["Ying"] = "Ying",
		["Zofia"] = "Zofia", ["Dokkaebi"] = "Dokkaebi", ["Lion"] = "Lion", ["Finka"] = "Finka",
		["Maverick"] = "Maverick", ["Nomad"] = "Nomad", ["Gridlock"] = "Gridlock", ["Nokk"] = "Nøkk",
		["Amaru"] = "Amaru", ["Kali"] = "Kali", ["Iana"] = "Iana", ["Ace"] = "Ace",
		["Zero"] = "Zero", ["Flores"] = "Flores", ["Osa"] = "Osa", ["Sens"] = "Sens",
		["Grim"] = "Grim", ["Brava"] = "Brava", ["Ram"] = "Ram", ["Deimos"] = "Deimos",
		["Striker"] = "Striker", ["Rail"] = "Rail",
		["Smoke"] = "Smoke", ["Mute"] = "Mute", ["Castle"] = "Castle", ["Pulse"] = "Pulse",
		["Doc"] = "Doc", ["Rook"] = "Rook", ["Kapkan"] = "Kapkan", ["Tachanka"] = "Tachanka",
		["Jager"] = "Jäger", ["Bandit"] = "Bandit", ["Frost"] = "Frost", ["Valkyrie"] = "Valkyrie",
		["Caveira"] = "Caveira", ["Echo"] = "Echo", ["Mira"] = "Mira", ["Lesion"] = "Lesion",
		["Ela"] = "Ela", ["Vigil"] = "Vigil", ["Maestro"] = "Maestro", ["Alibi"] = "Alibi",
		["Clash"] = "Clash", ["Kaid"] = "Kaid", ["Mozzie"] = "Mozzie", ["Warden"] = "Warden",
		["Goyo"] = "Goyo", ["Wamai"] = "Wamai", ["Oryx"] = "Oryx", ["Melusi"] = "Melusi",
		["Aruni"] = "Aruni", ["Thunderbird"] = "Thunderbird", ["Thorn"] = "Thorn", ["Azami"] = "Azami",
		["Solis"] = "Solis", ["Fenrir"] = "Fenrir", ["Tubarao"] = "Tubarão", ["Sentry"] = "Sentry",
		["Skopos"] = "Skopós", ["Denari"] = "Denari",
	};

	public static string Display(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}

		var clean = raw.Trim();
		return DisplayByRaw.TryGetValue(clean, out var display) ? display : clean;
	}
}

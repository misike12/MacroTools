namespace CsMd.Places;

// Display names for maps ("Dust II" instead of "de_dust2"). The game only ever
// sends file names, so known maps are listed and anything else falls back to a
// cleaned-up file name. Display only: variables and events keep the raw name so
// automations matching on it never break.
public static class MapNames
{
	private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
	{
		["de_dust2"] = "Dust II",
		["de_mirage"] = "Mirage",
		["de_inferno"] = "Inferno",
		["de_nuke"] = "Nuke",
		["de_overpass"] = "Overpass",
		["de_ancient"] = "Ancient",
		["de_anubis"] = "Anubis",
		["de_vertigo"] = "Vertigo",
		["de_train"] = "Train",
		["de_cache"] = "Cache",
		["de_cbble"] = "Cobblestone",
		["de_cobblestone"] = "Cobblestone",
		["de_aztec"] = "Aztec",
		["de_thera"] = "Thera",
		["de_mills"] = "Mills",
		["de_guard"] = "Guard",
		["de_grail"] = "Grail",
		["de_lake"] = "Lake",
		["de_safehouse"] = "Safehouse",
		["de_stmarc"] = "St. Marc",
		["cs_office"] = "Office",
		["cs_italy"] = "Italy",
		["cs_agency"] = "Agency",
		["ar_baggage"] = "Baggage",
		["ar_shoots"] = "Shoots",
		["ar_monastery"] = "Monastery",
		["ar_lake"] = "Lake",
		["gd_rialto"] = "Rialto",
		["dz_blacksite"] = "Blacksite",
		["dz_sirocco"] = "Sirocco",
		["dz_frostbite"] = "Frostbite",
	};

	public static string DisplayName(string? map)
	{
		if (string.IsNullOrWhiteSpace(map))
		{
			return string.Empty;
		}

		map = map.Trim();
		if (Known.TryGetValue(map, out var known))
		{
			return known;
		}

		var shortName = map.Contains('/') ? map[(map.LastIndexOf('/') + 1)..] : map;
		foreach (var prefix in new[] { "de_", "cs_", "ar_", "gd_", "dz_", "fy_", "aim_", "awp_" })
		{
			if (shortName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				shortName = shortName[prefix.Length..];
				break;
			}
		}

		var words = shortName.Split(['_', ' '], StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0)
		{
			return map;
		}

		var builder = new System.Text.StringBuilder();
		foreach (var word in words)
		{
			if (builder.Length > 0)
			{
				builder.Append(' ');
			}

			builder.Append(char.ToUpperInvariant(word[0]));
			if (word.Length > 1)
			{
				builder.Append(word.Substring(1).ToLowerInvariant());
			}
		}

		return builder.ToString();
	}
}

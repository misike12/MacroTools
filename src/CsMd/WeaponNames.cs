namespace CsMd;

// Display names for weapons ("AK-47" instead of "ak47"). The game only ever sends
// file names, so known weapons are listed and anything else falls back to the
// stripped file name. Applied at the source, so variables, events and the widget
// all read the same names.
public static class WeaponNames
{
	private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
	{
		["glock"] = "Glock-18",
		["hkp2000"] = "P2000",
		["usp_silencer"] = "USP-S",
		["elite"] = "Dual Berettas",
		["p250"] = "P250",
		["tec9"] = "Tec-9",
		["fiveseven"] = "Five-SeveN",
		["cz75a"] = "CZ75-Auto",
		["deagle"] = "Desert Eagle",
		["revolver"] = "R8 Revolver",
		["mac10"] = "MAC-10",
		["mp9"] = "MP9",
		["mp7"] = "MP7",
		["mp5sd"] = "MP5-SD",
		["ump45"] = "UMP-45",
		["p90"] = "P90",
		["bizon"] = "PP-Bizon",
		["galilar"] = "Galil AR",
		["famas"] = "FAMAS",
		["ak47"] = "AK-47",
		["m4a1"] = "M4A4",
		["m4a1_silencer"] = "M4A1-S",
		["ssg08"] = "SSG 08",
		["sg556"] = "SG 553",
		["aug"] = "AUG",
		["awp"] = "AWP",
		["g3sg1"] = "G3SG1",
		["scar20"] = "SCAR-20",
		["nova"] = "Nova",
		["xm1014"] = "XM1014",
		["mag7"] = "MAG-7",
		["sawedoff"] = "Sawed-Off",
		["m249"] = "M249",
		["negev"] = "Negev",
		["knife"] = "Knife",
		["knife_t"] = "Knife",
		["bayonet"] = "Bayonet",
		["knife_flip"] = "Flip Knife",
		["knife_gut"] = "Gut Knife",
		["knife_karambit"] = "Karambit",
		["knife_m9_bayonet"] = "M9 Bayonet",
		["knife_tactical"] = "Huntsman Knife",
		["knife_falchion"] = "Falchion Knife",
		["knife_survival_bowie"] = "Bowie Knife",
		["knife_butterfly"] = "Butterfly Knife",
		["knife_push"] = "Shadow Daggers",
		["knife_cord"] = "Paracord Knife",
		["knife_canis"] = "Survival Knife",
		["knife_ursus"] = "Ursus Knife",
		["knife_gypsy_jackknife"] = "Navaja Knife",
		["knife_outdoor"] = "Nomad Knife",
		["knife_stiletto"] = "Stiletto Knife",
		["knife_widowmaker"] = "Talon Knife",
		["knife_skeleton"] = "Skeleton Knife",
		["knife_kukri"] = "Kukri Knife",
		["hegrenade"] = "HE Grenade",
		["flashbang"] = "Flashbang",
		["smokegrenade"] = "Smoke Grenade",
		["molotov"] = "Molotov",
		["incgrenade"] = "Incendiary",
		["decoy"] = "Decoy",
		["c4"] = "C4",
		["taser"] = "Zeus x27",
		["healthshot"] = "Medi-Shot",
		["bumpmine"] = "Bump Mine",
		["breachcharge"] = "Breach Charge",
		["tablet"] = "Tablet",
		["fists"] = "Fists",
	};

	public static string DisplayName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return string.Empty;
		}

		name = name.Trim();
		if (name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
		{
			name = name[7..];
		}

		return Known.TryGetValue(name, out var known) ? known : name;
	}
}

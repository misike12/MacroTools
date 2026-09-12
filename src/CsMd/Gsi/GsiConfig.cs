namespace CsMd.Gsi;

// Builds and installs the gamestate_integration cfg CS2 needs to push to us.
// Discovery walks the Steam libraries (libraryfolders.vdf + appmanifest_730.acf),
// because CS2 can live on any drive, not just the Steam default.
public static class GsiConfig
{
	public const string FileName = "gamestate_integration_csmacrodeck.cfg";
	public const int AppId = 730;

	public static string Render(int port, string? authToken)
	{
		var lines = new List<string>
		{
			"\"CS:MD Integration Configuration\"",
			"{",
			$"    \"uri\"          \"http://127.0.0.1:{port}/gsi\"",
			"    \"timeout\"      \"5.0\"",
			"    \"buffer\"       \"0.1\"",
			"    \"throttle\"     \"0.1\"",
			"    \"heartbeat\"    \"10.0\"",
		};

		if (!string.IsNullOrWhiteSpace(authToken))
		{
			lines.Add("    \"auth\"");
			lines.Add("    {");
			lines.Add($"        \"token\" \"{authToken.Trim()}\"");
			lines.Add("    }");
		}

		lines.Add("    \"data\"");
		lines.Add("    {");
		foreach (var section in new[]
		{
			"provider", "map", "round", "player_id", "player_state", "player_weapons",
			"player_match_stats", "allplayers_id", "allplayers_state", "allplayers_match_stats",
			"allplayers_weapons", "allplayers_position", "phase_countdowns", "bomb", "grenades",
		})
		{
			lines.Add($"        \"{section}\" \"1\"");
		}

		lines.Add("    }");
		lines.Add("}");
		return string.Join("\n", lines) + "\n";
	}

	public static string? FindCsDirectory() => FindCsDirectory(SteamRoots());

	public static (bool Ok, string Detail) Install(int port, string? authToken) =>
		Install(port, authToken, SteamRoots());

	public static (bool Ok, string Detail) Install(int port, string? authToken, IEnumerable<string> steamRoots)
	{
		var cfgDirectory = FindCsDirectory(steamRoots);
		if (cfgDirectory is null)
		{
			return (false, "not-found");
		}

		var path = Path.Combine(cfgDirectory, FileName);
		try
		{
			if (File.Exists(path))
			{
				var backup = path + ".bak";
				if (!File.Exists(backup))
				{
					File.Copy(path, backup);
				}
			}

			File.WriteAllText(path, Render(port, authToken));
			return (true, path);
		}
		catch (Exception)
		{
			return (false, "write-failed");
		}
	}

	public static string? FindCsDirectory(IEnumerable<string> steamRoots)
	{
		foreach (var root in steamRoots)
		{
			foreach (var library in LibraryFolders(root))
			{
				var manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
				if (!File.Exists(manifest))
				{
					continue;
				}

				var cfgDirectory = Path.Combine(
					library, "steamapps", "common", "Counter-Strike Global Offensive", "game", "csgo", "cfg");
				if (Directory.Exists(cfgDirectory))
				{
					return cfgDirectory;
				}
			}
		}

		return null;
	}

	internal static IEnumerable<string> SteamRoots()
	{
		var candidates = new List<string?>();
		try
		{
			candidates.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
			candidates.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
		}
		catch (Exception)
		{
		}

		foreach (var programs in candidates)
		{
			if (string.IsNullOrWhiteSpace(programs))
			{
				continue;
			}

			var steam = Path.Combine(programs, "Steam");
			if (Directory.Exists(steam))
			{
				yield return steam;
			}
		}
	}

	internal static IEnumerable<string> LibraryFolders(string steamRoot)
	{
		yield return steamRoot;

		var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
		string text;
		try
		{
			if (!File.Exists(vdf))
			{
				yield break;
			}

			text = File.ReadAllText(vdf);
		}
		catch (Exception)
		{
			yield break;
		}

		foreach (var path in ParseLibraryPaths(text))
		{
			if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
			{
				yield return path;
			}
		}
	}

	public static IEnumerable<string> ParseLibraryPaths(string vdf)
	{
		var tokens = new List<string>();
		var current = new System.Text.StringBuilder();
		var inQuotes = false;
		var escaped = false;
		foreach (var ch in vdf)
		{
			if (escaped)
			{
				current.Append(ch);
				escaped = false;
			}
			else if (ch == '\\')
			{
				escaped = true;
				current.Append(ch);
			}
			else if (ch == '"')
			{
				if (inQuotes)
				{
					tokens.Add(current.ToString());
					current.Clear();
				}

				inQuotes = !inQuotes;
			}
			else if (inQuotes)
			{
				current.Append(ch);
			}
		}

		for (var i = 0; i + 1 < tokens.Count; i += 2)
		{
			if (string.Equals(tokens[i], "path", StringComparison.OrdinalIgnoreCase))
			{
				yield return tokens[i + 1].Replace("\\\\", "\\");
			}
		}
	}
}

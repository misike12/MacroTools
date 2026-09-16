using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace R6Md.Replays;

// Best-effort discovery of the game's MatchReplay folder: Steam libraries
// first (registry plus libraryfolders.vdf), then the stock Ubisoft Connect
// install roots. Anything found must actually contain the MatchReplay
// directory, otherwise it is not a hit. The user can always override with
// the configured folder, which wins over everything here.
public static partial class ReplayDiscovery
{
	private const string MatchReplayFolder = "MatchReplay";
	private const int RainbowSixSteamAppId = 359550;

	public static string? DiscoverReplayRoot()
	{
		foreach (var gameDir in CandidateGameDirs())
		{
			var root = Path.Combine(gameDir, MatchReplayFolder);
			try
			{
				if (Directory.Exists(root))
				{
					return root;
				}
			}
			catch (Exception)
			{
			}
		}

		return null;
	}

	public static bool LooksLikeReplayRoot(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		try
		{
			return Directory.Exists(path)
				&& Directory.EnumerateFileSystemEntries(path, "Match-*").Any();
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static IEnumerable<string> CandidateGameDirs()
	{
		foreach (var library in SteamLibraries())
		{
			yield return Path.Combine(library, "steamapps", "common", "Tom Clancy's Rainbow Six Siege");
		}

		yield return @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Rainbow Six Siege";
		yield return @"C:\Program Files\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Rainbow Six Siege";
	}

	private static IEnumerable<string> SteamLibraries()
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var steamPath in SteamPaths())
		{
			if (seen.Add(steamPath))
			{
				yield return steamPath;
			}

			foreach (var extra in ParseLibraryFolders(Path.Combine(steamPath, "steamapps", "libraryfolders.vdf")))
			{
				if (seen.Add(extra))
				{
					yield return extra;
				}
			}
		}
	}

	private static IEnumerable<string> SteamPaths()
	{
		foreach (var (hive, name) in new[]
		{
			(Registry.CurrentUser, @"SOFTWARE\Valve\Steam"),
			(Registry.LocalMachine, @"SOFTWARE\Valve\Steam"),
			(Registry.LocalMachine, @"SOFTWARE\Wow6432Node\Valve\Steam"),
		})
		{
			string? path = null;
			try
			{
				using var key = hive.OpenSubKey(name);
				path = key?.GetValue("SteamPath") as string;
			}
			catch (Exception)
			{
			}

			if (string.IsNullOrWhiteSpace(path))
			{
				continue;
			}

			string full;
			try
			{
				full = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
			}
			catch (Exception)
			{
				continue;
			}

			if (Directory.Exists(full))
			{
				yield return full;
			}
		}
	}

	private static IEnumerable<string> ParseLibraryFolders(string vdfPath)
	{
		string text;
		try
		{
			if (!File.Exists(vdfPath))
			{
				yield break;
			}

			text = File.ReadAllText(vdfPath);
		}
		catch (Exception)
		{
			yield break;
		}

		foreach (Match match in LibraryPathPattern().Matches(text))
		{
			var path = match.Groups["path"].Value.Replace("\\\\", "\\");
			if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
			{
				yield return path;
			}
		}
	}

	[GeneratedRegex("\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
	private static partial Regex LibraryPathPattern();
}

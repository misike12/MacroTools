using System.Text;
using System.Text.RegularExpressions;
using CsMd;

namespace CsMd.Console;

// Tails the game's console.log (written while CS2 runs with -condebug) and pulls the
// local player's position out of getpos output lines such as:
//   09/13 18:40:02 setpos_exact 8.479980 -2165.968750 -167.968750;setang_exact ...
// Reading a log file is fully external: no memory access, nothing injected, and the
// game only ever prints the local player's own coordinates.
public sealed class ConsolePositionWatcher
{
	private static readonly Regex FixLine = new(
		@"setpos(?:_exact)?\s+(?<x>-?\d+(?:\.\d+)?)\s+(?<y>-?\d+(?:\.\d+)?)\s+(?<z>-?\d+(?:\.\d+)?)",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly Regex FacingLine = new(
		@";\s*setang(?:_exact)?\s+(?<p>-?\d+(?:\.\d+)?)\s+(?<yaw>-?\d+(?:\.\d+)?)\s+(?<r>-?\d+(?:\.\d+)?)",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly Regex LineStamp = new(
		@"^(?<mo>\d{2})/(?<day>\d{2}) (?<h>\d{2}):(?<mi>\d{2}):(?<s>\d{2})\s",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex ChatLine = new(
		@"^\[(?<scope>[^\]]+)\]\s+(?<rest>.+)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	// Match chat scopes per client language. The engine also logs [Server], [Client]
	// and friends in the same shape, so anything outside this list is not chat.
	// Best effort: locales not listed simply never produce chat lines.
	private static readonly HashSet<string> ChatScopes = new(StringComparer.OrdinalIgnoreCase)
	{
		"ALL", "TEAM", "T", "CT", "SPEC", "SPECTATOR", "COACH", "PARTY", "LOBBY",
		"MINDENKI", "CSAPAT",
		"ALLE",
		"TOUS", "EQUIPE",
		"TODOS", "EQUIPO", "EQUIPE",
		"ВСЕ", "КОМАНДА",
		"WSZYSCY", "DRUZYNA",
		"HERKES", "TAKIM",
	};

	private static readonly HashSet<string> SystemSenders = new(StringComparer.OrdinalIgnoreCase)
	{
		"SV", "CL",
	};

	private const long TailBytes = 64 * 1024;

	private readonly object _gate = new();
	private readonly Func<string?> _logPath;
	private string? _cachedPath;
	private bool _hasCachedPath;
	private long _cachedPathTicks;
	private (double X, double Y, double Z, double Yaw, DateTimeOffset At)? _fix;
	private (string Player, string Scope, string Text, DateTimeOffset At)? _chat;
	private bool _logPresent;
	private DateTimeOffset? _logModifiedUtc;

	public ConsolePositionWatcher()
		: this(ConsoleLogPath.Find)
	{
	}

	public ConsolePositionWatcher(Func<string?> logPath)
	{
		_logPath = logPath;
	}

	public (double X, double Y, double Z, double Yaw, DateTimeOffset At)? LatestFix
	{
		get
		{
			lock (_gate)
			{
				return _fix;
			}
		}
	}

	public bool LogPresent
	{
		get
		{
			lock (_gate)
			{
				return _logPresent;
			}
		}
	}

	public DateTimeOffset? LogModifiedUtc
	{
		get
		{
			lock (_gate)
			{
				return _logModifiedUtc;
			}
		}
	}

	public (string Player, string Scope, string Text, DateTimeOffset At)? LatestChat
	{
		get
		{
			lock (_gate)
			{
				return _chat;
			}
		}
	}

	public void Poll()
	{
		var path = ResolveLogPath();
		if (string.IsNullOrWhiteSpace(path))
		{
			MarkMissing();
			return;
		}

		FileInfo info;
		try
		{
			info = new FileInfo(path);
			if (!info.Exists)
			{
				MarkMissing();
				return;
			}
		}
		catch (Exception)
		{
			MarkMissing();
			return;
		}

		string text;
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			var start = Math.Max(0, stream.Length - TailBytes);
			stream.Seek(start, SeekOrigin.Begin);
			using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
			if (start > 0)
			{
				reader.ReadLine();
			}

			text = reader.ReadToEnd();
			lock (_gate)
			{
				_logPresent = true;
				_logModifiedUtc = info.LastWriteTimeUtc;
			}
		}
		catch (Exception)
		{
			MarkMissing();
			return;
		}

		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		var observedAt = DateTimeOffset.UtcNow;
		(DateTimeOffset At, double X, double Y, double Z, double Yaw)? best = null;
		foreach (var line in text.Split('\n'))
		{
			var match = FixLine.Match(line);
			if (!match.Success
				|| !TryParseDouble(match.Groups["x"].Value, out var x)
				|| !TryParseDouble(match.Groups["y"].Value, out var y)
				|| !TryParseDouble(match.Groups["z"].Value, out var z))
			{
				continue;
			}

			var at = ParseLineStamp(line, observedAt);
			if (best is null || at >= best.Value.At)
			{
				best = (at, x, y, z, ParseYaw(line));
			}
		}

		if (best is not null)
		{
			lock (_gate)
			{
				var current = _fix;
				if (current is null || best.Value.At >= current.Value.At)
				{
					_fix = (best.Value.X, best.Value.Y, best.Value.Z, best.Value.Yaw, best.Value.At);
				}
			}
		}

		var chat = ParseChat(text, observedAt);
		if (chat is not null)
		{
			lock (_gate)
			{
				var current = _chat;
				if (current is null || chat.Value.At >= current.Value.At)
				{
					_chat = chat;
				}
			}
		}
	}

	private static (string Player, string Scope, string Text, DateTimeOffset At)? ParseChat(string text, DateTimeOffset observedAt)
	{
		(DateTimeOffset At, string Player, string Scope, string Text)? best = null;
		foreach (var raw in text.Split('\n'))
		{
			var line = raw.Trim();
			var stamp = LineStamp.Match(line);
			var body = stamp.Success ? line.Substring(stamp.Length).TrimStart() : line;
			var chat = ChatLine.Match(body);
			if (!chat.Success)
			{
				continue;
			}

			var rest = chat.Groups["rest"].Value;
			var separator = rest.IndexOf(": ", StringComparison.Ordinal);
			if (separator <= 0)
			{
				continue;
			}

			var scope = DisplayText.Sanitize(chat.Groups["scope"].Value);
			if (!ChatScopes.Contains(scope) || scope.Length == 0)
			{
				continue;
			}

			var player = DisplayText.Sanitize(rest.Substring(0, separator));
			var message = DisplayText.Sanitize(rest.Substring(separator + 2));
			if (player.Length == 0 || message.Length == 0 || SystemSenders.Contains(player))
			{
				continue;
			}

			var at = ParseLineStamp(line, observedAt);
			if (best is null || at >= best.Value.At)
			{
				best = (at, player, scope, message);
			}
		}

		return best is null ? null : (best.Value.Player, best.Value.Scope, best.Value.Text, best.Value.At);
	}

	// Steam discovery (registry plus libraryfolders.vdf parsing) costs file IO
	// on every call, but the install path barely moves. Cache resolutions and
	// re-resolve only when the log goes missing; an absent install re-resolves
	// at most once a minute so a fresh install is still picked up.
	private string? ResolveLogPath()
	{
		var now = DateTimeOffset.UtcNow.Ticks;
		lock (_gate)
		{
			if (_hasCachedPath
				&& (_cachedPath is not null || now - _cachedPathTicks < TimeSpan.FromMinutes(1).Ticks))
			{
				return _cachedPath;
			}
		}

		string? path;
		try
		{
			path = _logPath();
		}
		catch (Exception)
		{
			path = null;
		}

		lock (_gate)
		{
			_cachedPath = path;
			_hasCachedPath = true;
			_cachedPathTicks = now;
		}

		return path;
	}

	private void MarkMissing()
	{
		lock (_gate)
		{
			_logPresent = false;
			_logModifiedUtc = null;
			_hasCachedPath = false;
		}
	}

	private static double ParseYaw(string line)
	{
		var match = FacingLine.Match(line);
		if (!match.Success || !TryParseDouble(match.Groups["yaw"].Value, out var yaw))
		{
			return double.NaN;
		}

		return ((yaw % 360) + 360) % 360;
	}

	private static DateTimeOffset ParseLineStamp(string line, DateTimeOffset observedAt)
	{
		var match = LineStamp.Match(line);
		if (!match.Success
			|| !int.TryParse(match.Groups["mo"].Value, out var month)
			|| !int.TryParse(match.Groups["day"].Value, out var day)
			|| !int.TryParse(match.Groups["h"].Value, out var hour)
			|| !int.TryParse(match.Groups["mi"].Value, out var minute)
			|| !int.TryParse(match.Groups["s"].Value, out var second)
			|| month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 59)
		{
			return observedAt;
		}

		var localNow = observedAt.ToLocalTime();
		var year = localNow.Year;
		DateTime local;
		try
		{
			local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
		}
		catch (ArgumentOutOfRangeException)
		{
			return observedAt;
		}

		var stamp = new DateTimeOffset(local);
		if (stamp > observedAt + TimeSpan.FromDays(1))
		{
			try
			{
				stamp = new DateTimeOffset(new DateTime(year - 1, month, day, hour, minute, second, DateTimeKind.Local));
			}
			catch (ArgumentOutOfRangeException)
			{
				return observedAt;
			}
		}

		return stamp;
	}

	private static bool TryParseDouble(string text, out double value) =>
		double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
}

internal static class ConsoleLogPath
{
	public static string? Find() => Find(Gsi.GsiConfig.FindCsDirectory);

	internal static string? Find(Func<string?> csgoCfgDirectory)
	{
		string? cfg;
		try
		{
			cfg = csgoCfgDirectory();
		}
		catch (Exception)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(cfg))
		{
			return null;
		}

		string? parent;
		try
		{
			parent = Path.GetDirectoryName(cfg.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}
		catch (Exception)
		{
			return null;
		}

		return string.IsNullOrWhiteSpace(parent) ? null : Path.Combine(parent, "console.log");
	}
}

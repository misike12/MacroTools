using System.Text.Json;

namespace R6Md.Replays;

public sealed record R6PlayerEntry(
	string Name,
	int Team,
	string Operator,
	int Kills,
	int Deaths,
	int Assists,
	int Headshots,
	bool IsYou);

public sealed record R6FeedItem(string Key, MacroDeck.Localization.LocalizedString Text);

public sealed record R6Snapshot(
	bool Connected,
	bool HasMatch,
	string GameVersion,
	string MapName,
	string MapMode,
	string MatchType,
	string Site,
	int RoundNumber,
	int RoundsPerMatch,
	bool Overtime,
	string YourTeam,
	int YourScore,
	string YourRole,
	string OppTeam,
	int OppScore,
	string OppRole,
	int YourTeamIndex,
	string RoundHistory,
	IReadOnlyList<R6PlayerEntry> Players,
	string YourName,
	string YourOperator,
	int YourKills,
	int YourDeaths,
	int YourAssists,
	int YourHeadshots,
	string TopFragger,
	int TopFrags,
	string LastKiller,
	string LastVictim,
	bool LastHeadshot,
	bool HasLastKill,
	IReadOnlyList<R6FeedItem> FeedItems,
	bool HasFeed,
	int SessionKills,
	int SessionDeaths,
	int SessionAssists,
	int SessionHs,
	int Streak,
	int BestStreak,
	string MatchOutcome,
	bool HasOutcome,
	DateTimeOffset? LastParseAt,
	int RoundsTracked,
	int YourHp,
	bool OwConnected,
	string OwPhase,
	string SiteHistory,
	string Opener,
	double YourKost,
	int Dcs,
	double MatchDurationMinutes)
{
	public static R6Snapshot Empty { get; } = new(
		false, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
		0, 0, false, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty,
		[], string.Empty, string.Empty, 0, 0, 0, 0, string.Empty, 0,
		string.Empty, string.Empty, false, false, [], false,
		0, 0, 0, 0, 0, 0, string.Empty, false, null, 0,
		-1, false, string.Empty, string.Empty, string.Empty, 0, 0, 0);
}

public sealed record R6MatchEvent(string EventId, IReadOnlyDictionary<string, object?> Payload);

// One info frame or event from the optional Overwolf bridge (Tools/OverwolfBridge).
// The bridge forwards GEP data over localhost; the plugin never touches the
// game or Overwolf itself. Live frames only ever surface what the HUD already
// shows (score, roster, HP, phases) plus the same kill/round/match moments.
public sealed record LiveRosterEntry(
	string Name,
	string Team,
	string Operator,
	int Kills,
	int Deaths,
	int Hp,
	bool Local);

public sealed record LiveMatchFrame(
	string Phase,
	string Map,
	string Mode,
	int BlueScore,
	int OrangeScore,
	IReadOnlyList<LiveRosterEntry> Roster,
	DateTimeOffset At);

public static class R6EventIds
{
	public const string Kill = "kill";
	public const string Headshot = "headshot";
	public const string YourKill = "your-kill";
	public const string YourDeath = "your-death";
	public const string RoundWon = "round-won";
	public const string RoundLost = "round-lost";
	public const string MatchWon = "match-won";
	public const string MatchLost = "match-lost";
	public const string Ace = "ace";
	public const string Clutch = "clutch";
	public const string StreakMilestone = "streak-milestone";
}

public sealed class ReplayService : IDisposable
{
	private static readonly TimeSpan connectedWindow = TimeSpan.FromMinutes(30);
	private static readonly TimeSpan debounceDelay = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan rescanInterval = TimeSpan.FromMinutes(2);
	private static readonly int[] StreakMilestones = [3, 5, 10, 15, 20];

	private readonly Serilog.ILogger _logger;
	private readonly ReplayParser _parser;
	private readonly object _gate = new();
	private FileSystemWatcher? _watcher;
	private Timer? _debounceTimer;
	private Timer? _rescanTimer;
	private readonly Dictionary<string, TrackedFile> _files = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, TrackedRound> _rounds = new();
	private readonly HashSet<string> _seenKills = new();
	private readonly List<R6FeedItem> _feed = [];
	private string? _matchKey;
	private string? _you;
	private int _feedSeq;
	private string _lastKiller = string.Empty;
	private string _lastVictim = string.Empty;
	private bool _lastHeadshot;
	private LiveMatchFrame? _live;
	private readonly HashSet<string> _liveKillPairs = new(StringComparer.OrdinalIgnoreCase);
	private DateTimeOffset? _matchFirstSeen;
	private DateTimeOffset? _matchLastSeen;
	private int _sessionKills;
	private int _sessionDeaths;
	private int _sessionAssists;
	private int _sessionHs;
	private int _streak;
	private int _bestStreak;
	private string _matchOutcome = string.Empty;
	private DateTimeOffset? _lastParseAt;
	private string? _root;
	private bool _running;
	private bool _disposed;

	public ReplayService(Serilog.ILogger logger)
		: this(logger, new ReplayParser())
	{
	}

	// Tooling and tests: import pipeline with a caller-owned parser.
	public ReplayService(Serilog.ILogger logger, ReplayParser parser)
	{
		_logger = logger.ForContext<ReplayService>();
		_parser = parser;
	}

	public event EventHandler<R6MatchEvent>? MatchEvent;

	public string? ReplayRoot
	{
		get
		{
			lock (_gate)
			{
				return _root;
			}
		}

		private set
		{
			lock (_gate)
			{
				_root = value;
			}
		}
	}

	public void Start(string? rootOverride)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			StopLocked();
			var root = !string.IsNullOrWhiteSpace(rootOverride) && Directory.Exists(rootOverride)
				? rootOverride
				: ReplayDiscovery.DiscoverReplayRoot();
			_root = root;
			if (root is null)
			{
				return;
			}

			_running = true;
			_watcher = new FileSystemWatcher(root, "*.rec")
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
				EnableRaisingEvents = true,
			};
			_watcher.Created += OnFileTouched;
			_watcher.Changed += OnFileTouched;
			_watcher.Renamed += OnFileRenamed;
			_debounceTimer = new Timer(_ => _ = ImportDueAsync(), null, debounceDelay, debounceDelay);
			_rescanTimer = new Timer(_ => _ = RescanAsync(), null, rescanInterval, rescanInterval);
		}

		_ = RescanAsync();
	}

	public void Stop()
	{
		lock (_gate)
		{
			StopLocked();
		}
	}

	public async Task RescanNowAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await RescanAsync();
	}

	public R6Snapshot Snapshot()
	{
		Dictionary<int, TrackedRound> rounds;
		List<R6FeedItem> feed;
		string? matchKey;
		string? you;
		string root;
		string outcome;
		string killer;
		string victim;
		bool headshot;
		LiveMatchFrame? live;
		lock (_gate)
		{
			rounds = _rounds.ToDictionary(p => p.Key, p => p.Value);
			feed = _feed.ToList();
			matchKey = _matchKey;
			you = _you;
			root = _root ?? string.Empty;
			outcome = _matchOutcome;
			killer = _lastKiller;
			victim = _lastVictim;
			headshot = _lastHeadshot;
			live = _live;
		}

		var connected = !string.IsNullOrEmpty(root) && Directory.Exists(root);
		if (matchKey is null || rounds.Count == 0)
		{
			var fresh = live is not null && DateTimeOffset.UtcNow - live.At < LiveFreshness;
			if (!fresh)
			{
				// No rounds, no live frame: the session, feed and last outcome
				// still belong to the reader, not to a blank slate.
				return R6Snapshot.Empty with
				{
					Connected = connected,
					FeedItems = feed,
					HasFeed = feed.Count > 0,
					SessionKills = _sessionKills,
					SessionDeaths = _sessionDeaths,
					SessionAssists = _sessionAssists,
					SessionHs = _sessionHs,
					Streak = _streak,
					BestStreak = _bestStreak,
					MatchOutcome = outcome,
					HasOutcome = !string.IsNullOrEmpty(outcome),
					LastKiller = killer,
					LastVictim = victim,
					LastHeadshot = headshot,
					HasLastKill = !string.IsNullOrEmpty(killer),
					LastParseAt = _lastParseAt,
				};
			}
		}

		return BuildSnapshot(rounds, feed, you, connected);
	}

	public void ResetSessionStats()
	{
		lock (_gate)
		{
			_sessionKills = 0;
			_sessionDeaths = 0;
			_sessionAssists = 0;
			_sessionHs = 0;
			_streak = 0;
			_bestStreak = 0;
		}
	}

	public void InjectSample() => IntegrateMatch(SampleMatch(), "sample");

	public void IngestLiveInfo(LiveMatchFrame frame)
	{
		var events = new List<R6MatchEvent>();
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_live = frame;
		}

		EmitEvents(events);
	}

	public void IngestLiveEvent(string name, string player, string target, bool headshot)
	{
		var events = new List<R6MatchEvent>();
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			switch (name.ToLowerInvariant())
			{
				case "kill":
					// GEP kill events carry no names; the roster counters still
					// move live, and the replay pass attributes names later.
					// Nameless rows would read "? ▸ ?", so only attributed
					// kills reach the feed and the last-kill variables.
					if (!string.IsNullOrWhiteSpace(player) || !string.IsNullOrWhiteSpace(target))
					{
						PushFeedLocked(new R6FeedItem(
							$"live-{_feedSeq++}",
							headshot
								? R6Strings.FeedHeadshot(player, target)
								: R6Strings.FeedKill(player, target)));
						_lastKiller = player;
						_lastVictim = target;
						_lastHeadshot = headshot;
						_liveKillPairs.Add($"{player}|{target}");
					}

					events.Add(new R6MatchEvent(R6EventIds.Kill, new Dictionary<string, object?>
					{
						["player"] = player,
						["target"] = target,
						["headshot"] = headshot,
					}));
					if (headshot)
					{
						events.Add(new R6MatchEvent(R6EventIds.Headshot, new Dictionary<string, object?>
						{
							["player"] = player,
						}));
					}

					break;
				case "death":
					events.Add(new R6MatchEvent(R6EventIds.YourDeath, new Dictionary<string, object?>
					{
						["player"] = player,
					}));
					break;
			}
		}

		EmitEvents(events);
	}

	public void IngestLiveOutcome(string name, bool won)
	{
		var events = new List<R6MatchEvent>();
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			switch (name.ToLowerInvariant())
			{
				case "roundoutcome":
					events.Add(new R6MatchEvent(
						won ? R6EventIds.RoundWon : R6EventIds.RoundLost,
						new Dictionary<string, object?> { ["round"] = 0.0, ["condition"] = string.Empty }));
					break;
				case "matchoutcome":
					if (string.IsNullOrEmpty(_matchOutcome))
					{
						_matchOutcome = won ? "victory" : "defeat";
					}

					events.Add(new R6MatchEvent(
						won ? R6EventIds.MatchWon : R6EventIds.MatchLost,
						new Dictionary<string, object?> { ["your-score"] = 0.0, ["opp-score"] = 0.0 }));
					break;
			}
		}

		EmitEvents(events);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			StopLocked();
		}

		_parser.Dispose();
	}

	// Tooling and tests: folds one parsed match document into the state,
	// exactly as the file watcher does after a successful parse. Quiet
	// imports build state (scores, history, rosters) without events, feed or
	// session accrual, for backfilling old replays without spamming.
	public void IntegrateMatch(ReplayMatch match, string sourceKey, DateTimeOffset? fileTime = null, bool quiet = false)
	{
		var events = new List<R6MatchEvent>();
		lock (_gate)
		{
			var key = !string.IsNullOrWhiteSpace(match.MatchId) ? match.MatchId! : sourceKey;
			if (_matchKey is not null && !string.Equals(_matchKey, key, StringComparison.Ordinal))
			{
				FinalizeMatchLocked(events, quiet);
			}

			_matchKey = key;
			if (fileTime is { } seen)
			{
				_matchFirstSeen ??= seen;
				_matchLastSeen = seen;
			}
			_you ??= ResolveYou(match);
			var roundNo = (match.RoundNumber ?? 0) + 1;
			if (!_rounds.TryGetValue(roundNo, out var round))
			{
				round = new TrackedRound(roundNo);
				_rounds[roundNo] = round;
			}

			round.Update(match);
			if (!quiet)
			{
				AccrueSessionLocked(match, roundNo, events);
			}

			CollectKillsLocked(match, roundNo, events, quiet);
			_lastParseAt = DateTimeOffset.UtcNow;
		}

		EmitEvents(events);
	}

	private void FinalizeMatchLocked(List<R6MatchEvent> events, bool quiet = false)
	{
		if (_rounds.Count > 0 && string.IsNullOrEmpty(_matchOutcome))
		{
			var last = _rounds.Values.OrderBy(r => r.Number).Last();
			_matchOutcome = last.YourWon == true ? "victory" : last.YourWon == false ? "defeat" : string.Empty;
			if (!quiet && !string.IsNullOrEmpty(_matchOutcome))
			{
				events.Add(new R6MatchEvent(
					_matchOutcome == "victory" ? R6EventIds.MatchWon : R6EventIds.MatchLost,
					new Dictionary<string, object?> { ["your-score"] = last.YourScore, ["opp-score"] = last.OppScore }));
			}
		}

		_rounds.Clear();
		_seenKills.Clear();
		_liveKillPairs.Clear();
		_feed.Clear();
		_lastKiller = string.Empty;
		_lastVictim = string.Empty;
		_lastHeadshot = false;
		_matchFirstSeen = null;
		_matchLastSeen = null;
	}

	private void AccrueSessionLocked(ReplayMatch match, int roundNo, List<R6MatchEvent> events)
	{
		var you = _you ?? ResolveYou(match);
		_you = you;
		if (string.IsNullOrEmpty(you))
		{
			return;
		}

		var stat = match.Stats?.FirstOrDefault(s =>
			string.Equals(s.Username, you, StringComparison.OrdinalIgnoreCase));
		if (stat is null || _rounds[roundNo].SessionCounted)
		{
			return;
		}

		_rounds[roundNo].SessionCounted = true;
		_sessionKills += stat.Kills ?? 0;
		_sessionAssists += stat.Assists ?? 0;
		_sessionHs += stat.Headshots ?? 0;
		if (stat.Died == true)
		{
			_sessionDeaths += 1;
			_streak = 0;
		}
		else
		{
			_streak += stat.Kills ?? 0;
			_bestStreak = Math.Max(_bestStreak, _streak);
			if (StreakMilestones.Contains(_streak))
			{
				events.Add(new R6MatchEvent(R6EventIds.StreakMilestone,
					new Dictionary<string, object?> { ["streak"] = (double)_streak }));
			}
		}
	}

	private void CollectKillsLocked(ReplayMatch match, int roundNo, List<R6MatchEvent> events, bool quiet = false)
	{
		var kills = (match.MatchFeedback ?? [])
			.Where(f => string.Equals(f.TypeName, "Kill", StringComparison.OrdinalIgnoreCase))
			.ToList();
		foreach (var kill in kills)
		{
			var id = $"{roundNo}:{(kill.Username ?? "?").ToLowerInvariant()}:{(kill.Target ?? "?").ToLowerInvariant()}:{kill.TimeInSeconds ?? -1}";
			if (!_seenKills.Add(id))
			{
				continue;
			}

			// A kill already announced live is not announced twice when its
			// replay lands; the replay row still feeds history and session.
			if (_liveKillPairs.Contains($"{kill.Username}|{kill.Target}"))
			{
				continue;
			}

			if (quiet)
			{
				continue;
			}

			PushFeedLocked(new R6FeedItem(
				$"kill-{_feedSeq++}",
				kill.Headshot == true
					? R6Strings.FeedHeadshot(kill.Username ?? "?", kill.Target ?? "?")
					: R6Strings.FeedKill(kill.Username ?? "?", kill.Target ?? "?")));
			_lastKiller = kill.Username ?? string.Empty;
			_lastVictim = kill.Target ?? string.Empty;
			_lastHeadshot = kill.Headshot == true;
			events.Add(new R6MatchEvent(R6EventIds.Kill, new Dictionary<string, object?>
			{
				["player"] = kill.Username ?? string.Empty,
				["target"] = kill.Target ?? string.Empty,
				["headshot"] = kill.Headshot == true,
			}));
			if (kill.Headshot == true)
			{
				events.Add(new R6MatchEvent(R6EventIds.Headshot, new Dictionary<string, object?>
				{
					["player"] = kill.Username ?? string.Empty,
				}));
			}

			var you = _you;
			if (!string.IsNullOrEmpty(you))
			{
				if (string.Equals(kill.Username, you, StringComparison.OrdinalIgnoreCase))
				{
					events.Add(new R6MatchEvent(R6EventIds.YourKill, new Dictionary<string, object?>
					{
						["target"] = kill.Target ?? string.Empty,
					}));
				}

				if (string.Equals(kill.Target, you, StringComparison.OrdinalIgnoreCase))
				{
					events.Add(new R6MatchEvent(R6EventIds.YourDeath, new Dictionary<string, object?>
					{
						["player"] = kill.Username ?? string.Empty,
					}));
				}
			}
		}

		var byKiller = kills
			.GroupBy(k => k.Username ?? "?")
			.Select(g => (name: g.Key, count: g.Count()))
			.OrderByDescending(g => g.count)
			.FirstOrDefault();
		var round = _rounds[roundNo];
		if (!round.AceChecked && byKiller.count >= 5)
		{
			round.AceChecked = true;
			if (!quiet)
			{
				PushFeedLocked(new R6FeedItem($"ace-{_feedSeq++}", R6Strings.FeedAce(byKiller.name)));
				events.Add(new R6MatchEvent(R6EventIds.Ace, new Dictionary<string, object?>
				{
					["player"] = byKiller.name,
				}));
			}
		}

		DetectRoundOutcomeLocked(match, roundNo, events, quiet);
	}

	private void DetectRoundOutcomeLocked(ReplayMatch match, int roundNo, List<R6MatchEvent> events, bool quiet = false)
	{
		var yours = YourTeamIndex(match);
		var your = TeamAt(match, yours);
		var opp = TeamAt(match, yours == 0 ? 1 : 0);
		if (your is null || opp is null)
		{
			return;
		}

		var round = _rounds[roundNo];
		var yourScore = your.Score ?? 0;
		var oppScore = opp.Score ?? 0;
		var previous = _rounds.Values.Where(r => r.Number < roundNo).OrderBy(r => r.Number).LastOrDefault();
		var prevYour = previous?.YourScore ?? your.StartingScore ?? 0;
		var prevOpp = previous?.OppScore ?? opp.StartingScore ?? 0;
		var yourDelta = yourScore - prevYour;
		var oppDelta = oppScore - prevOpp;
		if (yourDelta <= 0 && oppDelta <= 0)
		{
			return;
		}

		var won = yourDelta > oppDelta;
		if (round.OutcomeCounted)
		{
			return;
		}

		round.OutcomeCounted = true;
		round.YourWon = won;
		if (quiet)
		{
			return;
		}

		var winner = won ? your : opp;
		PushFeedLocked(new R6FeedItem(
			$"round-{_feedSeq++}",
			R6Strings.FeedRound(roundNo, won, winner.WinCondition ?? string.Empty)));
		events.Add(new R6MatchEvent(
			won ? R6EventIds.RoundWon : R6EventIds.RoundLost,
			new Dictionary<string, object?> { ["round"] = (double)roundNo, ["condition"] = winner.WinCondition ?? string.Empty }));
		DetectClutchLocked(match, roundNo, won, events, quiet);
	}

	private void DetectClutchLocked(ReplayMatch match, int roundNo, bool won, List<R6MatchEvent> events, bool quiet = false)
	{
		if (!won || quiet)
		{
			return;
		}

		var survivors = (match.Stats ?? []).Where(s => !(s.Died ?? true)).ToList();
		if (survivors.Count != 1 || (survivors[0].Kills ?? 0) < 2)
		{
			return;
		}

		PushFeedLocked(new R6FeedItem($"clutch-{_feedSeq++}", R6Strings.FeedClutch(survivors[0].Username ?? "?")));
		events.Add(new R6MatchEvent(R6EventIds.Clutch, new Dictionary<string, object?>
		{
			["player"] = survivors[0].Username ?? string.Empty,
		}));
	}

	// teams[] is indexed by the players' teamIndex: the recording player's
	// entry is your team. Newer builds list ENEMY TEAM first, so positional
	// assumptions silently swap the scorebug.
	private static int YourTeamIndex(ReplayMatch match)
	{
		var key = ReplayJson.PlayerKey(match.RecordingPlayerId);
		if (!string.IsNullOrEmpty(key))
		{
			var entry = match.Players?.FirstOrDefault(p =>
				string.Equals(ReplayJson.PlayerKey(p.Id), key, StringComparison.Ordinal));
			if (entry?.TeamIndex is { } team)
			{
				return team;
			}
		}

		return 0;
	}

	private static ReplayTeam? TeamAt(ReplayMatch match, int index) =>
		match.Teams is { } teams && index >= 0 && index < teams.Count ? teams[index] : null;

	private void PushFeedLocked(R6FeedItem item)
	{
		_feed.Insert(0, item);
		while (_feed.Count > 30)
		{
			_feed.RemoveAt(_feed.Count - 1);
		}
	}

	private static readonly TimeSpan LiveFreshness = TimeSpan.FromSeconds(60);

	private R6Snapshot BuildSnapshot(
		Dictionary<int, TrackedRound> rounds,
		List<R6FeedItem> feed,
		string? you,
		bool connected)
	{
		LiveMatchFrame? live;
		lock (_gate)
		{
			live = _live;
		}

		if (live is not null && DateTimeOffset.UtcNow - live.At >= LiveFreshness)
		{
			live = null;
		}

		var ordered = rounds.Values.OrderBy(r => r.Number).ToList();
		if (ordered.Count == 0)
		{
			return live is not null
				? LiveOnlySnapshot(live, feed, connected)
				: R6Snapshot.Empty with { Connected = connected };
		}

		var last = ordered.Last();
		var history = string.Concat(ordered.Select(r => r.YourWon == true ? "W" : r.YourWon == false ? "L" : "?"));
		var siteHistory = string.Join(" · ", ordered
			.Select(r => r.Site)
			.Where(s => !string.IsNullOrWhiteSpace(s))
			.Distinct(StringComparer.OrdinalIgnoreCase));
		var kostRounds = string.IsNullOrEmpty(you) ? 0 : ordered.Count(r =>
			r.Stats.TryGetValue(you, out var stat) && (!stat.Died || stat.Kills > 0 || stat.Assists > 0));
		var kostTotal = string.IsNullOrEmpty(you) ? 0 : ordered.Count(r =>
			r.Stats.ContainsKey(you!));
		var roster = last.Players
			.Select(p => new R6PlayerEntry(
				p.Username,
				p.Team,
				OperatorNames.Display(p.Operator),
				last.Stats.GetValueOrDefault(p.Username, PlayerRoundStat.Zero).Kills,
				last.Stats.GetValueOrDefault(p.Username, PlayerRoundStat.Zero).Died ? 1 : 0,
				last.Stats.GetValueOrDefault(p.Username, PlayerRoundStat.Zero).Assists,
				last.Stats.GetValueOrDefault(p.Username, PlayerRoundStat.Zero).Headshots,
				!string.IsNullOrEmpty(you) && string.Equals(p.Username, you, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		var mine = string.IsNullOrEmpty(you)
			? null
			: roster.FirstOrDefault(p => string.Equals(p.Name, you, StringComparison.OrdinalIgnoreCase));
		var mineStat = string.IsNullOrEmpty(you) || !last.Stats.TryGetValue(you, out var found)
			? null
			: found;
		string lastKiller;
		string lastVictim;
		bool lastHeadshot;
		bool hasLastKill;
		lock (_gate)
		{
			lastKiller = _lastKiller;
			lastVictim = _lastVictim;
			lastHeadshot = _lastHeadshot;
			hasLastKill = !string.IsNullOrEmpty(_lastKiller);
		}

		// A fresh live frame wins for the fast-moving numbers (score, roster,
		// your KDA and HP); the replay stays authoritative for history, sites,
		// session totals and everything already settled.
		var liveLocal = live?.Roster.FirstOrDefault(p => p.Local)
			?? live?.Roster.FirstOrDefault(p => string.Equals(p.Name, you, StringComparison.OrdinalIgnoreCase));
		var liveColor = (liveLocal?.Team ?? "blue").ToLowerInvariant();
		var yourScore = live is not null
			? liveColor == "orange" ? live.OrangeScore : live.BlueScore
			: last.YourScore;
		var oppScore = live is not null
			? liveColor == "orange" ? live.BlueScore : live.OrangeScore
			: last.OppScore;
		if (live is not null)
		{
			var other = last.YourTeam == 0 ? 1 : 0;
			roster = live.Roster
				.OrderByDescending(p => p.Kills)
				.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
				.Select(p => new R6PlayerEntry(
					p.Name,
					string.Equals(p.Team, liveColor, StringComparison.OrdinalIgnoreCase) ? last.YourTeam : other,
					OperatorNames.Display(p.Operator),
					p.Kills, 0, 0, 0,
					p.Local || string.Equals(p.Name, you, StringComparison.OrdinalIgnoreCase)))
				.ToList();
			mine = roster.FirstOrDefault(p => p.IsYou);
		}

		var fragger = roster
			.Where(p => p.Team == last.YourTeam)
			.OrderByDescending(p => p.Kills)
			.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		var yourHp = liveLocal?.Hp ?? -1;
		return new R6Snapshot(
			connected, true,
			last.Match.GameVersion ?? string.Empty,
			live is not null && !string.IsNullOrWhiteSpace(live.Map) ? live.Map : MapNames.Display(last.Match.Map?.Name),
			live is not null && !string.IsNullOrWhiteSpace(live.Mode) ? live.Mode : MapNames.Mode(last.Match.Gamemode?.Name),
			last.Match.MatchType?.Name ?? string.Empty,
			last.Match.Site ?? string.Empty,
			last.Number, last.RoundsPerMatch, last.Overtime,
			"YOUR TEAM", yourScore, last.YourRole,
			"OPPONENTS", oppScore, last.OppRole, last.YourTeam,
			history, roster,
			mine?.Name ?? string.Empty,
			liveLocal is not null && !string.IsNullOrWhiteSpace(liveLocal.Operator)
				? OperatorNames.Display(liveLocal.Operator)
				: mine?.Operator ?? string.Empty,
			mine?.Kills ?? 0,
			mineStat?.Died == true || (live is not null && liveLocal?.Hp == 0) ? 1 : 0,
			live is not null ? mineStat?.Assists ?? 0 : mine?.Assists ?? 0,
			live is not null ? mineStat?.Headshots ?? 0 : mine?.Headshots ?? 0,
			fragger?.Name ?? string.Empty, fragger?.Kills ?? 0,
			lastKiller, lastVictim, lastHeadshot, hasLastKill,
			feed, feed.Count > 0,
			_sessionKills, _sessionDeaths, _sessionAssists, _sessionHs, _streak, _bestStreak,
			_matchOutcome, !string.IsNullOrEmpty(_matchOutcome),
			_lastParseAt, rounds.Count,
			yourHp, live is not null, live?.Phase ?? string.Empty,
			siteHistory, last.Opener,
			kostTotal > 0 ? (double)kostRounds / kostTotal : 0,
			last.Dcs,
			MatchDurationMinutes());
	}

	private double MatchDurationMinutes()
	{
		lock (_gate)
		{
			if (_matchFirstSeen is { } first && _matchLastSeen is { } last && last > first)
			{
				return (last - first).TotalMinutes;
			}

			return 0;
		}
	}

	private R6Snapshot LiveOnlySnapshot(LiveMatchFrame live, List<R6FeedItem> feed, bool connected)
	{
		var local = live.Roster.FirstOrDefault(p => p.Local)
			?? live.Roster.FirstOrDefault(p => string.Equals(p.Name, _you, StringComparison.OrdinalIgnoreCase));
		var yourColor = (local?.Team ?? "blue").ToLowerInvariant();
		var yourScore = yourColor == "orange" ? live.OrangeScore : live.BlueScore;
		var oppScore = yourColor == "orange" ? live.BlueScore : live.OrangeScore;
		var roster = live.Roster
			.OrderByDescending(p => p.Kills)
			.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.Select(p => new R6PlayerEntry(
				p.Name,
				string.Equals(p.Team, yourColor, StringComparison.OrdinalIgnoreCase) ? 0 : 1,
				OperatorNames.Display(p.Operator),
				p.Kills, 0, 0, 0,
				p.Local || string.Equals(p.Name, _you, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		var fragger = roster
			.Where(p => p.Team == 0)
			.OrderByDescending(p => p.Kills)
			.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();

		return new R6Snapshot(
			connected, true,
			string.Empty, live.Map, live.Mode, string.Empty, string.Empty,
			live.BlueScore + live.OrangeScore + 1, 0, false,
			"YOUR TEAM", yourScore, string.Empty,
			"OPPONENTS", oppScore, string.Empty, 0,
			string.Empty, roster,
			local?.Name ?? string.Empty, OperatorNames.Display(local?.Operator),
			local?.Kills ?? 0, 0, 0, 0,
			fragger?.Name ?? string.Empty, fragger?.Kills ?? 0,
			_lastKiller, _lastVictim, _lastHeadshot, !string.IsNullOrEmpty(_lastKiller),
			feed, feed.Count > 0,
			_sessionKills, _sessionDeaths, _sessionAssists, _sessionHs, _streak, _bestStreak,
			_matchOutcome, !string.IsNullOrEmpty(_matchOutcome),
			_lastParseAt, 0,
			local?.Hp ?? -1, true, live.Phase,
			string.Empty, string.Empty, 0, 0, 0);
	}

	private static string? ResolveYou(ReplayMatch match)
	{
		var key = ReplayJson.PlayerKey(match.RecordingPlayerId);
		if (string.IsNullOrEmpty(key))
		{
			return null;
		}

		return match.Players?.FirstOrDefault(p =>
			string.Equals(ReplayJson.PlayerKey(p.Id), key, StringComparison.Ordinal))?.Username;
	}

	private void EmitEvents(List<R6MatchEvent> events)
	{
		foreach (var matchEvent in events)
		{
			try
			{
				MatchEvent?.Invoke(this, matchEvent);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Match event handler failed.");
			}
		}
	}

	private void OnFileTouched(object? sender, FileSystemEventArgs e) => TouchFile(e.FullPath);

	private void OnFileRenamed(object? sender, RenamedEventArgs e) => TouchFile(e.FullPath);

	private void TouchFile(string path)
	{
		if (!path.EndsWith(".rec", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		lock (_gate)
		{
			if (_disposed || !_running)
			{
				return;
			}

			_files[path] = new TrackedFile(DateTimeOffset.UtcNow);
		}
	}

	private async Task ImportDueAsync()
	{
		List<string> due;
		lock (_gate)
		{
			if (_disposed || !_running)
			{
				return;
			}

			var now = DateTimeOffset.UtcNow;
			due = _files
				.Where(p => now - p.Value.TouchedAt >= debounceDelay)
				.Select(p => p.Key)
				.ToList();
			foreach (var path in due)
			{
				_files.Remove(path);
			}
		}

		foreach (var path in due)
		{
			try
			{
				if (!File.Exists(path))
				{
					continue;
				}

				var written = File.GetLastWriteTimeUtc(path);
				var match = await _parser.ParseAsync(path, CancellationToken.None);
				if (match is not null)
				{
					// Backfill is quiet: files older than a few minutes are
					// history, not news. No events, no feed, no session.
					var quiet = DateTimeOffset.UtcNow - written > TimeSpan.FromMinutes(10);
					IntegrateMatch(match, Path.GetFileNameWithoutExtension(path), written, quiet);
				}
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Replay import failed.");
			}
		}
	}

	private async Task RescanAsync()
	{
		List<string> found;
		string? root;
		lock (_gate)
		{
			if (_disposed || !_running)
			{
				return;
			}

			root = _root;
		}

		if (string.IsNullOrEmpty(root))
		{
			return;
		}

		try
		{
			found = Directory.EnumerateFiles(root, "*.rec", SearchOption.AllDirectories).ToList();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Replay rescan failed.");
			return;
		}

		lock (_gate)
		{
			if (_disposed || !_running)
			{
				return;
			}

			foreach (var path in found)
			{
				_files.TryAdd(path, new TrackedFile(DateTimeOffset.UtcNow - debounceDelay));
			}
		}

		await Task.CompletedTask;
	}

	private void StopLocked()
	{
		_running = false;
		if (_watcher is not null)
		{
			_watcher.EnableRaisingEvents = false;
			_watcher.Created -= OnFileTouched;
			_watcher.Changed -= OnFileTouched;
			_watcher.Renamed -= OnFileRenamed;
			_watcher.Dispose();
			_watcher = null;
		}

		_debounceTimer?.Dispose();
		_debounceTimer = null;
		_rescanTimer?.Dispose();
		_rescanTimer = null;
	}

	private static readonly JsonDocument s_id1 = JsonDocument.Parse("1");
	private static readonly JsonDocument s_id2 = JsonDocument.Parse("2");
	private static readonly JsonDocument s_id3 = JsonDocument.Parse("3");
	private static readonly JsonDocument s_id4 = JsonDocument.Parse("4");
	private static readonly JsonDocument s_killType = JsonDocument.Parse("{\"name\":\"Kill\"}");

	private static ReplayMatch SampleMatch() => new(
		"Y11S1", null,
		new ReplayNamed("Ranked", null),
		new ReplayNamed("CHALET", null),
		"2F Master Bedroom, 2F Office",
		new ReplayNamed("Bomb", null),
		6, null, 1, null,
		[
			new ReplayTeam("YOUR TEAM", 0, 2, true, "KilledOpponents", "Defense"),
			new ReplayTeam("OPPONENTS", 0, 1, false, null, "Attack"),
		],
		[
			new ReplayPlayer(s_id1.RootElement, "You.Siege", 0, new ReplayNamed("Jager", null)),
			new ReplayPlayer(s_id2.RootElement, "Mate.One", 0, new ReplayNamed("Mute", null)),
			new ReplayPlayer(s_id3.RootElement, "Rival.One", 1, new ReplayNamed("Ash", null)),
			new ReplayPlayer(s_id4.RootElement, "Rival.Two", 1, new ReplayNamed("Thermite", null)),
		],
		"sample-match",
		s_id1.RootElement,
		[
			new ReplayFeedback(s_killType.RootElement, "You.Siege", "Rival.One", true, 90, null, null),
			new ReplayFeedback(s_killType.RootElement, "Rival.Two", "Mate.One", false, 60, null, null),
		],
		[
			new ReplayPlayerStat("You.Siege", 295, 1, false, 0, 1, 100, 25, 100, 175),
			new ReplayPlayerStat("Mate.One", 100, 0, true, 0, 0, 0, 150, 0, 60),
			new ReplayPlayerStat("Rival.One", 120, 0, true, 0, 0, 0, 100, 20, 90),
			new ReplayPlayerStat("Rival.Two", 150, 1, false, 0, 0, 0, 0, 80, 175),
		]);

	private sealed record TrackedFile(DateTimeOffset TouchedAt);

	private sealed class TrackedRound(int number)
	{
		public int Number { get; } = number;
		public ReplayMatch Match { get; private set; } = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
		public int YourTeam { get; private set; }
		public string Site { get; private set; } = string.Empty;
		public string Opener { get; private set; } = string.Empty;
		public int Dcs { get; private set; }
		public int RoundsPerMatch { get; private set; }
		public bool Overtime { get; private set; }
		public int YourScore { get; private set; }
		public int OppScore { get; private set; }
		public string YourRole { get; private set; } = string.Empty;
		public string OppRole { get; private set; } = string.Empty;
		public bool? YourWon { get; set; }
		public bool OutcomeCounted { get; set; }
		public bool AceChecked { get; set; }
		public bool SessionCounted { get; set; }
		public List<RoundPlayer> Players { get; } = [];
		public Dictionary<string, PlayerRoundStat> Stats { get; } = new(StringComparer.OrdinalIgnoreCase);

		public void Update(ReplayMatch match)
		{
			Match = match;
			RoundsPerMatch = match.RoundsPerMatch ?? 0;
			Overtime = (match.OvertimeRoundNumber ?? 0) > 0;
			var yours = YourTeamIndex(match);
			YourTeam = yours;
			var your = TeamAt(match, yours);
			var opp = TeamAt(match, yours == 0 ? 1 : 0);
			YourScore = your?.Score ?? 0;
			OppScore = opp?.Score ?? 0;
			YourRole = your?.Role ?? string.Empty;
			OppRole = opp?.Role ?? string.Empty;
			Site = match.Site ?? string.Empty;
			Opener = (match.MatchFeedback ?? [])
				.Where(f => string.Equals(f.TypeName, "Kill", StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(f => f.TimeInSeconds ?? -1)
				.Select(f => f.Username ?? string.Empty)
				.FirstOrDefault() ?? string.Empty;
			Dcs = (match.MatchFeedback ?? [])
				.Count(f => f.TypeName.StartsWith("PlayerLeave", StringComparison.OrdinalIgnoreCase)
					|| f.TypeName.StartsWith("PlayerLeft", StringComparison.OrdinalIgnoreCase));
			Players.Clear();
			foreach (var player in match.Players ?? [])
			{
				if (string.IsNullOrWhiteSpace(player.Username))
				{
					continue;
				}

				Players.Add(new RoundPlayer(
					player.Username!,
					player.TeamIndex ?? 0,
					player.Operator?.Name ?? string.Empty));
			}

			Stats.Clear();
			foreach (var stat in match.Stats ?? [])
			{
				if (string.IsNullOrWhiteSpace(stat.Username))
				{
					continue;
				}

				Stats[stat.Username!] = new PlayerRoundStat(
					stat.Kills ?? 0,
					stat.Assists ?? 0,
					stat.Headshots ?? 0,
					stat.Score ?? 0,
					stat.Died ?? false);
			}
		}
	}

	private sealed record RoundPlayer(string Username, int Team, string Operator);

	private sealed record PlayerRoundStat(int Kills, int Assists, int Headshots, int Score, bool Died)
	{
		public static PlayerRoundStat Zero { get; } = new(0, 0, 0, 0, false);
	}
}

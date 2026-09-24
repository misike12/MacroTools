using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CsMd.Console;
using CsMd.Places;
using Serilog;

namespace CsMd.Gsi;

public sealed record GsiMatchEvent(string EventId, IReadOnlyDictionary<string, object?> Payload);

public static class GsiEventIds
{
	public const string RoundStarted = "round-started";
	public const string RoundEnded = "round-ended";
	public const string RoundWon = "round-won";
	public const string RoundLost = "round-lost";
	public const string BombPlanted = "bomb-planted";
	public const string BombDefused = "bomb-defused";
	public const string BombExploded = "bomb-exploded";
	public const string PlayerDied = "player-died";
	public const string PlayerKill = "player-kill";
	public const string MatchStarted = "match-started";
	public const string MatchEnded = "match-ended";
	public const string StreakMilestone = "streak-milestone";
	public const string PlaceChanged = "place-changed";
	public const string ChatMessage = "chat-message";
}

public sealed record GsiSnapshot(
	bool Connected,
	string? MapName,
	string? MapMode,
	string? MapPhase,
	int MapRound,
	int CtScore,
	int TScore,
	string? CtName,
	string? TName,
	string? RoundPhase,
	string? BombState,
	double? PhaseEndsIn,
	bool HasPlayer,
	string? PlayerName,
	string? PlayerTeam,
	bool Alive,
	int Health,
	int Armor,
	bool Helmet,
	bool Flashed,
	int Money,
	string? Weapon,
	int AmmoClip,
	int AmmoReserve,
	int Kills,
	int Deaths,
	int Assists,
	int Mvps,
	int Score,
	int SmokesActive,
	int FireActive,
	int SessionKills,
	int SessionDeaths,
	double SessionKd,
	double PosX,
	double PosY,
	double PosZ,
	bool HasPosition,
	string PositionSource,
	string? PlaceName,
	double? BombCountdown,
	string? BombCarrier,
	string? BombSite,
	int RoundKills,
	int RoundHeadshots,
	int RoundDamage,
	bool Smoked,
	bool Burning,
	bool DefuseKit,
	int EquipValue,
	string? Activity,
	string? WeaponType,
	string RoundHistory,
	double? FacingYaw,
	string? Clan,
	int TimeoutsCt,
	int TimeoutsT,
	string? CountdownPhase,
	int GrenadesActive,
	int KillStreak,
	int BestStreak,
	string? TopWeapon,
	int TopWeaponKills,
	int RoundsPlayed,
	int SessionDamage,
	int SessionHs,
double MatchElapsed,
		double SessionMatchTime,
		string? LastChat);

public sealed record PositionOptions(bool Enabled, int IntervalSeconds, int KeyCode)
{
	public static PositionOptions Default { get; } = new(false, 2, 104);
}

public static class PositionSources
{
	public const string Off = "off";
	public const string NoLog = "no-log";
	public const string Waiting = "waiting";
	public const string Console = "console";
	public const string Gsi = "gsi";
}

public sealed class GsiService : IDisposable
{
	private static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
	private const int MaxBodyBytes = 4 * 1024 * 1024;
	private const int MaxConnections = 8;

	// The game sends no match-start timestamp, so a client that connects mid-match
	// (late join, GOTV, plugin restart) would otherwise time only the observed tail.
	// Each decided round awards exactly one team point, so already-played rounds are
	// backfilled at this average length. Rough on purpose: round pace varies by mode.
	private const double EstimatedRoundSeconds = 100;

	private readonly ILogger _logger;
	private readonly object _gate = new();
	private readonly SemaphoreSlim _handlers = new(MaxConnections, MaxConnections);
	private readonly PlaceStore _places;
	private readonly ConsolePositionWatcher _console;
	private readonly IPositionTrigger _trigger;
	private TcpListener? _listener;
	private CancellationTokenSource? _cts;
	private Task? _acceptTask;
	private Task? _consumeTask;
	private Channel<GsiPayload>? _channel;
	private Timer? _positionTimer;
	private int _port;
	private string _authToken = string.Empty;
	private string _steamIdFilter = string.Empty;
	private PositionOptions _positionOptions = PositionOptions.Default;
	private bool _disposed;

	private GsiPayload? _last;
	private long _sequence;
	private DateTimeOffset? _lastReceivedAt;
	private int _sessionKills;
	private int _sessionDeaths;
	private int _streak;
	private int _bestStreak;
	private readonly Dictionary<string, int> _weaponKills = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<int> _roundsSeen = [];
	private int _sessionDamage;
	private int _sessionHs;
	private DateTimeOffset? _matchStartUtc;
	private double _sessionMatchTimeAccumulated; // cumulative match time across matches (excludes lobby/menu)
	private string? _lastPlace;
	private DateTimeOffset? _lastChatAt;

	public GsiService(ILogger logger)
		: this(logger, new PlaceStore(logger))
	{
	}

	public GsiService(ILogger logger, PlaceStore places)
		: this(logger, places, new PositionTrigger(), ConsoleLogPath.Find)
	{
	}

	public GsiService(ILogger logger, PlaceStore places, IPositionTrigger trigger, Func<string?> consoleLogPath)
	{
		_logger = logger.ForContext<GsiService>();
		_places = places;
		_trigger = trigger;
		_console = new ConsolePositionWatcher(consoleLogPath);
	}

	public event EventHandler<GsiMatchEvent>? MatchEvent;

	public bool IsListening
	{
		get
		{
			lock (_gate)
			{
				return _listener is not null;
			}
		}
	}

	public int Port
	{
		get
		{
			lock (_gate)
			{
				return _port;
			}
		}
	}

	public bool Start(int port, string? authToken)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return false;
			}

			if (_listener is not null && _port == port)
			{
				_authToken = authToken ?? string.Empty;
				return true;
			}

			StopLocked();
			TcpListener listener;
			try
			{
				listener = new TcpListener(IPAddress.Loopback, port);
				listener.Start(16);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "GSI listen failed.");
				return false;
			}

			_listener = listener;
			try
			{
				_port = ((IPEndPoint)listener.LocalEndpoint).Port;
			}
			catch (Exception)
			{
				_port = port;
			}

			_authToken = authToken ?? string.Empty;
			_cts = new CancellationTokenSource();
			_channel = Channel.CreateUnbounded<GsiPayload>(new UnboundedChannelOptions { SingleReader = true });
			_acceptTask = AcceptLoopAsync(_cts.Token);
			_consumeTask = ConsumeLoopAsync(_channel.Reader, _cts.Token);
			var interval = TimeSpan.FromSeconds(Math.Clamp(_positionOptions.IntervalSeconds, 1, 10));
			_positionTimer = new Timer(PollPosition, null, interval, interval);
			return true;
		}
	}

	public void Stop()
	{
		lock (_gate)
		{
			StopLocked();
		}
	}

	public void ResetSessionStats()
	{
		lock (_gate)
		{
			ResetSessionLocked();
		}
	}

	private void AnchorMatchStartLocked(DateTimeOffset now, GsiMap? map)
	{
		var completed = Math.Max(0, (map?.TeamCt?.Score ?? 0) + (map?.TeamT?.Score ?? 0));
		_matchStartUtc = now - TimeSpan.FromSeconds(completed * EstimatedRoundSeconds);
	}

	private static readonly int[] StreakMilestones = [3, 5, 10, 15, 20, 25, 30];

	private void ResetSessionLocked()
	{
		_sessionKills = 0;
		_sessionDeaths = 0;
		_streak = 0;
		_bestStreak = 0;
		_weaponKills.Clear();
		_roundsSeen.Clear();
		_sessionDamage = 0;
		_sessionHs = 0;
		_matchStartUtc = null;
		_sessionMatchTimeAccumulated = 0;
		_lastPlace = null;
		_lastChatAt = null;
	}

	public void InjectTestState() => ApplyPayload(TestPayload(), bypassAuth: true);

	public GsiSnapshot Snapshot()
	{
		GsiPayload? payload;
		SessionStats stats;
		bool connected;
		lock (_gate)
{
		var now = DateTimeOffset.UtcNow;
		connected = _lastReceivedAt is { } seen && now - seen <= ConnectedWindow;
		payload = _last;

		// Determine current match phase and whether to run the match timer
		var map = payload?.Map;
		var mapPhase = map?.Phase ?? string.Empty;
		var isLive = string.Equals(mapPhase, "live", StringComparison.OrdinalIgnoreCase);

		// Update match timer: only runs when phase is "live"
		if (isLive && _matchStartUtc is null)
		{
			AnchorMatchStartLocked(now, map);
		}
		else if (!isLive && _matchStartUtc is not null)
		{
			// Match ended (went from live to something else) - accumulate the match time
			var matchElapsed = (now - _matchStartUtc.Value).TotalSeconds;
			_sessionMatchTimeAccumulated += Math.Max(0, matchElapsed);
			_matchStartUtc = null;
		}

		var currentMatchElapsed = isLive && _matchStartUtc is { } started
			? Math.Max(0, (now - started).TotalSeconds)
			: 0;

		stats = new SessionStats(
			_sessionKills,
			_sessionDeaths,
			_streak,
			_bestStreak,
			_weaponKills.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
			_roundsSeen.Count,
			_sessionDamage,
			_sessionHs,
			currentMatchElapsed,
			_sessionMatchTimeAccumulated);
			if (!connected || payload is null)
			{
				return EmptySnapshot(false);
			}
		}

		// Built outside the gate on purpose: place lookup parses map files on first
		// use, and every variable read takes a snapshot. Holding the gate through
		// file IO would convoy all readers behind one slow extraction.
		return BuildSnapshot(payload, connected: true, stats);
	}

	private sealed record SessionStats(
		int Kills,
		int Deaths,
		int Streak,
		int BestStreak,
		Dictionary<string, int> WeaponKills,
		int RoundsPlayed,
		int Damage,
		int Hs,
		double Elapsed,
		double SessionMatchTime);

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
			_handlers.Dispose();
		}
	}

	private void StopLocked()
	{
		try
		{
			_cts?.Cancel();
		}
		catch (Exception)
		{
		}

		try
		{
			_listener?.Stop();
		}
		catch (Exception)
		{
		}

		try
		{
			_cts?.Dispose();
		}
		catch (Exception)
		{
		}

		try
		{
			_positionTimer?.Dispose();
		}
		catch (Exception)
		{
		}

		_listener = null;
		_cts = null;
		_channel = null;
		_acceptTask = null;
		_consumeTask = null;
		_positionTimer = null;
	}

	private async Task AcceptLoopAsync(CancellationToken cancellationToken)
	{
		TcpListener? listener;
		lock (_gate)
		{
			listener = _listener;
		}

		if (listener is null)
		{
			return;
		}

		while (!cancellationToken.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await listener.AcceptTcpClientAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "GSI accept failed.");
				try
				{
					await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				continue;
			}

			_ = HandleAsync(client, cancellationToken);
		}
	}

	private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
	{
		using (client)
		{
			var entered = false;
			try
			{
				entered = await _handlers.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (!entered)
			{
				return;
			}

			try
			{
				using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(ReadTimeout);
				await HandleRequestAsync(client, timeout.Token);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "GSI request failed.");
			}
			finally
			{
				_handlers.Release();
			}
		}
	}

	private async Task HandleRequestAsync(TcpClient client, CancellationToken cancellationToken)
	{
		var stream = client.GetStream();
		var reader = new RequestReader(stream);
		var requestLine = await reader.ReadLineAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(requestLine))
		{
			return;
		}

		var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 1)
		{
			return;
		}

		var method = parts[0];
		var bodyLength = 0;
		var chunked = false;
		var expectContinue = false;
		while (await reader.ReadLineAsync(cancellationToken) is string line && line.Length > 0)
		{
			var colon = line.IndexOf(':');
			if (colon <= 0)
			{
				continue;
			}

			var name = line.Substring(0, colon).Trim();
			var value = line.Substring(colon + 1).Trim();
			if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(value, out var parsed) && parsed >= 0)
			{
				bodyLength = parsed;
			}
			else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
				&& value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
			{
				chunked = true;
			}
			else if (name.Equals("Expect", StringComparison.OrdinalIgnoreCase)
				&& value.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
			{
				expectContinue = true;
			}
		}

		if (expectContinue)
		{
			await WriteContinueAsync(stream, cancellationToken);
		}

		byte[] body = [];
		if (chunked)
		{
			body = await reader.ReadChunkedBodyAsync(MaxBodyBytes, cancellationToken) ?? [];
		}
		else if (bodyLength > 0)
		{
			if (bodyLength > MaxBodyBytes)
			{
				await WriteResponseAsync(stream, 413, "Too big", cancellationToken);
				return;
			}

			body = await reader.ReadExactAsync(bodyLength, cancellationToken) ?? [];
		}

		if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && body.Length > 0)
		{
			EnqueueJson(body);
		}

		await WriteResponseAsync(stream, 200, "OK", cancellationToken);
	}

	private sealed class RequestReader(NetworkStream stream)
	{
		private readonly byte[] _buffer = new byte[8192];
		private int _start;
		private int _end;

		public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
		{
			var line = new StringBuilder();
			while (line.Length < 65536)
			{
				for (var i = _start; i < _end; i++)
				{
					if (_buffer[i] == (byte)'\n')
					{
						var raw = Encoding.ASCII.GetString(_buffer, _start, i - _start);
						_start = i + 1;
						return raw.TrimEnd('\r');
					}
				}

				line.Append(Encoding.ASCII.GetString(_buffer, _start, _end - _start));
				_start = _end;
				int read;
				try
				{
					read = await stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken);
				}
				catch (Exception)
				{
					return null;
				}

				if (read <= 0)
				{
					return line.Length > 0 ? line.ToString() : null;
				}

				_end += read;
			}

			return null;
		}

		public async Task<byte[]?> ReadExactAsync(int length, CancellationToken cancellationToken)
		{
			var body = new byte[length];
			var offset = 0;
			while (offset < length)
			{
				if (_start < _end)
				{
					var take = Math.Min(_end - _start, length - offset);
					Buffer.BlockCopy(_buffer, _start, body, offset, take);
					_start += take;
					offset += take;
					continue;
				}

				int read;
				try
				{
					read = await stream.ReadAsync(body.AsMemory(offset), cancellationToken);
				}
				catch (Exception)
				{
					return null;
				}

				if (read <= 0)
				{
					return null;
				}

				offset += read;
			}

			return body;
		}

		public async Task<byte[]?> ReadChunkedBodyAsync(int maxBytes, CancellationToken cancellationToken)
		{
			using var collected = new MemoryStream();
			while (collected.Length <= maxBytes)
			{
				var sizeLine = await ReadLineAsync(cancellationToken);
				if (sizeLine is null)
				{
					return null;
				}

				var size = sizeLine.Split(';')[0].Trim();
				if (!long.TryParse(size, System.Globalization.NumberStyles.HexNumber, null, out var chunk) || chunk < 0)
				{
					return null;
				}

				if (chunk == 0)
				{
					await ReadLineAsync(cancellationToken);
					break;
				}

				if (chunk > maxBytes)
				{
					return null;
				}

				var data = await ReadExactAsync((int)chunk, cancellationToken);
				if (data is null)
				{
					return null;
				}

				collected.Write(data, 0, data.Length);
				await ReadLineAsync(cancellationToken);
			}

			return collected.ToArray();
		}
	}

	private async Task ConsumeLoopAsync(ChannelReader<GsiPayload> reader, CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var payload in reader.ReadAllAsync(cancellationToken))
			{
				try
				{
					ApplyPayload(payload, bypassAuth: false);
				}
				catch (Exception ex)
				{
					_logger.Debug(ex, "GSI payload apply failed.");
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void EnqueueJson(byte[] body)
	{
		GsiPayload? payload;
		try
		{
			payload = JsonSerializer.Deserialize<GsiPayload>(body, GsiJson.Options);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "GSI payload ignored.");
			return;
		}

		if (payload is null || !HasGameData(payload))
		{
			return;
		}

		Channel<GsiPayload>? channel;
		string token;
		lock (_gate)
		{
			channel = _channel;
			token = _authToken;
		}

		if (channel is null)
		{
			return;
		}

		if (!string.IsNullOrEmpty(token)
			&& !string.Equals(payload.Auth?.Token, token, StringComparison.Ordinal))
		{
			return;
		}

		channel.Writer.TryWrite(payload);
	}

	private static async Task WriteContinueAsync(NetworkStream stream, CancellationToken cancellationToken)
	{
		try
		{
			await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), cancellationToken);
		}
		catch (Exception)
		{
		}
	}

	private static async Task WriteResponseAsync(NetworkStream stream, int status, string text, CancellationToken cancellationToken)
	{
		var payload = Encoding.ASCII.GetBytes(
			$"HTTP/1.1 {status} {text}\r\nContent-Length: 2\r\nConnection: close\r\nContent-Type: text/plain\r\n\r\nOK");
		try
		{
			await stream.WriteAsync(payload, cancellationToken);
		}
		catch (Exception)
		{
		}
	}

	private void ApplyPayload(GsiPayload payload, bool bypassAuth)
	{
		List<GsiMatchEvent> events = [];
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			if (!bypassAuth && !string.IsNullOrEmpty(_authToken)
				&& !string.Equals(payload.Auth?.Token, _authToken, StringComparison.Ordinal))
			{
				return;
			}

			Interlocked.Increment(ref _sequence);
			var now = DateTimeOffset.UtcNow;
			// The clock measures the match, not the lobby: anchor it at the
			// first non-warmup packet so veto and warmup do not count. A map
			// change nulls the anchor above through ResetSessionLocked.
			var warmedUp = !string.Equals(payload.Map?.Phase, "warmup", StringComparison.OrdinalIgnoreCase);
			if ((_matchStartUtc is null && warmedUp)
				|| (_lastReceivedAt is { } seen && now - seen > TimeSpan.FromMinutes(5)))
			{
				AnchorMatchStartLocked(now, payload.Map);
			}

			_lastReceivedAt = now;

			var previous = _last;
			_last = payload;
			if (MapNameOf(previous) is not null && MapNameOf(payload) is not null
				&& !string.Equals(MapNameOf(previous), MapNameOf(payload), StringComparison.OrdinalIgnoreCase))
			{
				ResetSessionLocked();
			}

			events.AddRange(DiffLocked(previous, payload));
		}

		EmitEvents(events);
	}

	private void EmitEvents(List<GsiMatchEvent> events)
	{
		if (events.Count == 0)
		{
			return;
		}

		Task.Run(() =>
		{
			foreach (var matchEvent in events)
			{
				try
				{
					MatchEvent?.Invoke(this, matchEvent);
				}
				catch (Exception)
				{
				}
			}
		});
	}

	private List<GsiMatchEvent> DiffLocked(GsiPayload? previous, GsiPayload current)
	{
		var events = new List<GsiMatchEvent>();
		var previousMapPhase = previous?.Map?.Phase;
		var mapPhase = current.Map?.Phase;
		if (!string.Equals(previousMapPhase, mapPhase, StringComparison.OrdinalIgnoreCase))
		{
			if (string.Equals(mapPhase, "live", StringComparison.OrdinalIgnoreCase))
			{
				events.Add(new GsiMatchEvent(GsiEventIds.MatchStarted, new Dictionary<string, object?>
				{
					["map"] = current.Map?.Name ?? string.Empty,
					["mode"] = current.Map?.Mode ?? string.Empty,
				}));
			}
			else if (string.Equals(mapPhase, "gameover", StringComparison.OrdinalIgnoreCase))
			{
				var ct = current.Map?.TeamCt?.Score ?? 0;
				var t = current.Map?.TeamT?.Score ?? 0;
				events.Add(new GsiMatchEvent(GsiEventIds.MatchEnded, new Dictionary<string, object?>
				{
					["map"] = current.Map?.Name ?? string.Empty,
					["winner"] = ct == t ? string.Empty : ct > t ? "CT" : "T",
					["ct-score"] = (double)ct,
					["t-score"] = (double)t,
				}));
			}
		}

		var previousRoundPhase = previous?.Round?.Phase;
		var roundPhase = current.Round?.Phase;
		if (!string.Equals(previousRoundPhase, roundPhase, StringComparison.OrdinalIgnoreCase))
		{
			if (string.Equals(roundPhase, "freezetime", StringComparison.OrdinalIgnoreCase))
			{
				events.Add(new GsiMatchEvent(GsiEventIds.RoundStarted, new Dictionary<string, object?>
				{
					["round"] = (double)(current.Map?.Round ?? 0),
				}));
			}
			else if (string.Equals(roundPhase, "over", StringComparison.OrdinalIgnoreCase))
			{
				var winner = NormalizeTeam(current.Round?.WinTeam);
				events.Add(new GsiMatchEvent(GsiEventIds.RoundEnded, new Dictionary<string, object?>
				{
					["round"] = (double)(current.Map?.Round ?? 0),
					["winner"] = winner,
				}));

				var myTeam = FocusedTeam(current, previous);
				if (!string.IsNullOrEmpty(winner) && !string.IsNullOrEmpty(myTeam))
				{
					events.Add(new GsiMatchEvent(
						string.Equals(winner, myTeam, StringComparison.OrdinalIgnoreCase) ? GsiEventIds.RoundWon : GsiEventIds.RoundLost,
						new Dictionary<string, object?>
						{
							["round"] = (double)(current.Map?.Round ?? 0),
							["winner"] = winner,
						}));
				}
			}
		}

		var previousBomb = previous is null ? string.Empty : CurrentBombState(previous);
		var bomb = CurrentBombState(current);
		if (!string.Equals(previousBomb, bomb, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(bomb))
		{
			var bombEvent = bomb switch
			{
				"planted" => GsiEventIds.BombPlanted,
				"defused" => GsiEventIds.BombDefused,
				"exploded" => GsiEventIds.BombExploded,
				_ => null,
			};
			if (bombEvent is not null)
			{
				events.Add(new GsiMatchEvent(bombEvent, new Dictionary<string, object?>
				{
					["site"] = BombSiteOf(current.Round?.Bomb),
				}));
			}
		}

var focus = FocusedPlayer(current, previous);
		var previousFocus = previous is not null ? FocusedPlayer(previous, null) : null;
		if (focus is not null)
		{
			// Reset streak if the spectated player changed so it only tracks one person
			if (previousFocus is not null
				&& focus.SteamId is string
				&& previousFocus.SteamId is string
				&& !string.Equals(focus.SteamId, previousFocus.SteamId, StringComparison.Ordinal))
			{
				_streak = 0;
				_bestStreak = 0;
				_weaponKills.Clear();
			}

			var position = ResolvePosition(current, focus, previous);
			var place = position is not null
				? SafeFindPlace(current.Map?.Name, position.X, position.Y, position.Z)
				: null;
			if (!string.IsNullOrEmpty(place) && !string.Equals(place, _lastPlace, StringComparison.Ordinal))
			{
				if (_lastPlace is not null)
				{
					events.Add(new GsiMatchEvent(GsiEventIds.PlaceChanged, new Dictionary<string, object?>
					{
						["place"] = place,
					}));
				}

				_lastPlace = place;
			}
			var deaths = focus.MatchStats?.Deaths ?? 0;
			var previousDeaths = previousFocus?.MatchStats?.Deaths ?? 0;
			if (previous is not null && deaths > previousDeaths)
			{
				events.Add(new GsiMatchEvent(GsiEventIds.PlayerDied, new Dictionary<string, object?>
				{
					["player"] = focus.Name ?? string.Empty,
					["pos-x"] = position?.X ?? 0.0,
					["pos-y"] = position?.Y ?? 0.0,
					["pos-z"] = position?.Z ?? 0.0,
					["place"] = place ?? string.Empty,
				}));
			}

			var kills = focus.MatchStats?.Kills ?? 0;
			var previousKills = previousFocus?.MatchStats?.Kills ?? 0;
			if (previous is not null && kills > previousKills)
			{
				var fresh = Math.Min(kills - previousKills, 5);
				var weapon = ActiveWeaponName(focus);
				for (var i = 0; i < fresh; i++)
				{
					events.Add(new GsiMatchEvent(GsiEventIds.PlayerKill, new Dictionary<string, object?>
					{
						["player"] = focus.Name ?? string.Empty,
						["weapon"] = weapon,
						["pos-x"] = position?.X ?? 0.0,
						["pos-y"] = position?.Y ?? 0.0,
						["pos-z"] = position?.Z ?? 0.0,
						["place"] = place ?? string.Empty,
					}));
				}

				_sessionKills += kills - previousKills;
				var previousStreak = _streak;
				_streak += kills - previousKills;
				if (_streak > _bestStreak)
				{
					_bestStreak = _streak;
				}

				foreach (var milestone in StreakMilestones)
				{
					if (previousStreak < milestone && _streak >= milestone)
					{
						events.Add(new GsiMatchEvent(GsiEventIds.StreakMilestone, new Dictionary<string, object?>
						{
							["streak"] = (double)milestone,
						}));
					}
				}

				if (!string.IsNullOrEmpty(weapon))
				{
					_weaponKills[weapon] = _weaponKills.TryGetValue(weapon, out var count) ? count + kills - previousKills : kills - previousKills;
				}
			}

			if (previous is not null && deaths > previousDeaths)
			{
				_sessionDeaths += deaths - previousDeaths;
				_streak = 0;
			}

			var round = current.Map?.Round ?? 0;
			if (round > 0)
			{
				_roundsSeen.Add(round);
			}

			var previousRound = previous?.Map?.Round ?? 0;
			if (previousRound > 0 && round != previousRound)
			{
				_sessionDamage += previousFocus?.State?.RoundDamage ?? 0;
				_sessionHs += previousFocus?.State?.RoundHeadshots ?? 0;
			}
		}

		return events;
	}

	private string? FocusedSteamId(GsiPayload current, GsiPayload? previous)
	{
		if (!string.IsNullOrWhiteSpace(_steamIdFilter))
		{
			return _steamIdFilter;
		}

		return current.Player?.SteamId ?? previous?.Player?.SteamId;
	}

	public void SetSteamIdFilter(string? steamId)
	{
		lock (_gate)
		{
			_steamIdFilter = steamId?.Trim() ?? string.Empty;
		}
	}

	public void UpdatePositionOptions(bool enabled, int intervalSeconds, int keyCode)
	{
		lock (_gate)
		{
			_positionOptions = new PositionOptions(
				enabled,
				Math.Clamp(intervalSeconds, 1, 10),
				Math.Clamp(keyCode, 1, 255));
			if (_positionTimer is not null)
			{
				var interval = TimeSpan.FromSeconds(_positionOptions.IntervalSeconds);
				try
				{
					_positionTimer.Change(interval, interval);
				}
				catch (Exception)
				{
				}
			}
		}
	}

	private void PollPosition(object? _)
	{
		try
		{
			PositionOptions options;
			bool connected;
			lock (_gate)
			{
				if (_disposed)
				{
					return;
				}

				options = _positionOptions;
				connected = _lastReceivedAt is { } seen && DateTimeOffset.UtcNow - seen <= ConnectedWindow;
			}

			if (!connected)
			{
				return;
			}

			_console.Poll();
			EmitChatIfNew();

			if (!options.Enabled)
			{
				return;
			}

			if (!_console.LogPresent || !LogActive())
			{
				return;
			}

			if (!_trigger.IsGameFocused())
			{
				return;
			}

			_trigger.Tap((byte)Math.Clamp(options.KeyCode, 1, 255));
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Position poll failed.");
		}
	}

	private void EmitChatIfNew()
	{
		try
		{
			var chat = _console.LatestChat;
			if (chat is null)
			{
				return;
			}

			lock (_gate)
			{
				if (_lastChatAt is not null && chat.Value.At <= _lastChatAt)
				{
					return;
				}

				_lastChatAt = chat.Value.At;
			}

			EmitEvents([new GsiMatchEvent(GsiEventIds.ChatMessage, new Dictionary<string, object?>
			{
				["player"] = chat.Value.Player,
				["scope"] = chat.Value.Scope,
				["text"] = chat.Value.Text,
			})]);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Chat poll failed.");
		}
	}

	private bool LogActive()
	{
		var modified = _console.LogModifiedUtc;
		return modified is not null && DateTimeOffset.UtcNow - modified <= TimeSpan.FromMinutes(2);
	}

	private GsiPlayer? FocusedPlayer(GsiPayload current, GsiPayload? previous)
	{
		var wanted = FocusedSteamId(current, previous);
		if (string.IsNullOrWhiteSpace(wanted))
		{
			return current.Player;
		}

		if (string.Equals(current.Player?.SteamId, wanted, StringComparison.Ordinal))
		{
			return current.Player;
		}

		if (current.AllPlayers is not null && current.AllPlayers.TryGetValue(wanted, out var tracked) && tracked is not null)
		{
			return tracked;
		}

		return null;
	}

	private string FocusedTeam(GsiPayload current, GsiPayload? previous)
	{
		var focus = FocusedPlayer(current, previous);
		return NormalizeTeam(focus?.Team);
	}

	private static string NormalizeTeam(string? team) => team?.ToUpperInvariant() switch
	{
		"CT" => "CT",
		"T" => "T",
		_ => string.Empty,
	};

	private static string NormalizeBomb(string? state) => state?.ToLowerInvariant() switch
	{
		"carried" => "carried",
		"dropped" => "dropped",
		"planting" => "planting",
		"planted" => "planted",
		"defusing" => "defusing",
		"defused" => "defused",
		"exploded" => "exploded",
		_ => string.Empty,
	};

	private string? SafeFindPlace(string? mapName, double x, double y, double z)
	{
		try
		{
			return _places.FindPlace(mapName, x, y, z);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Place lookup failed.");
			return null;
		}
	}

	private static string? FocusedPosition(GsiPayload payload, GsiPlayer? focus, string? wantedSteamId)
	{
		if (!string.IsNullOrWhiteSpace(focus?.Position))
		{
			return focus.Position;
		}

		if (!string.IsNullOrWhiteSpace(wantedSteamId)
			&& payload.AllPlayers is not null
			&& payload.AllPlayers.TryGetValue(wantedSteamId, out var tracked)
			&& tracked is not null)
		{
			return tracked.Position;
		}

		return null;
	}

	private sealed record ResolvedPosition(double X, double Y, double Z, double Yaw, bool FromConsole);

	private ResolvedPosition? ResolvePosition(GsiPayload payload, GsiPlayer? focus, GsiPayload? previous)
	{
		PositionOptions options;
		lock (_gate)
		{
			options = _positionOptions;
		}

		if (options.Enabled && IsLocalAliveFocus(payload, focus))
		{
			var fix = _console.LatestFix;
			var freshness = TimeSpan.FromSeconds(Math.Max(6, 3 * options.IntervalSeconds));
			if (fix is not null && DateTimeOffset.UtcNow - fix.Value.At <= freshness)
			{
				return new ResolvedPosition(fix.Value.X, fix.Value.Y, fix.Value.Z, fix.Value.Yaw, true);
			}
		}

		var gsi = ParsePosition(FocusedPosition(payload, focus, FocusedSteamId(payload, previous)));
		return gsi is null ? null : new ResolvedPosition(gsi.Value.X, gsi.Value.Y, gsi.Value.Z, double.NaN, false);
	}

	private static bool IsLocalAliveFocus(GsiPayload payload, GsiPlayer? focus)
	{
		var provider = payload.Provider?.SteamId;
		return !string.IsNullOrWhiteSpace(provider)
			&& focus is not null
			&& string.Equals(focus.SteamId, provider, StringComparison.Ordinal)
			&& focus.State is not null
			&& focus.State.Health > 0;
	}

	private string ResolvePositionSource(GsiPayload payload, GsiPlayer? focus, ResolvedPosition? position)
	{
		PositionOptions options;
		lock (_gate)
		{
			options = _positionOptions;
		}

		if (!options.Enabled)
		{
			return PositionSources.Off;
		}

		if (!_console.LogPresent || !LogActive())
		{
			return PositionSources.NoLog;
		}

		if (position is not null)
		{
			return position.FromConsole ? PositionSources.Console : PositionSources.Gsi;
		}

		return PositionSources.Waiting;
	}

	private static bool HasGameData(GsiPayload payload) =>
		payload.Provider is not null
		|| payload.Map is not null
		|| payload.Round is not null
		|| payload.Player is not null
		|| payload.AllPlayers is not null
		|| payload.PhaseCountdowns is not null
		|| payload.Grenades is not null
		|| payload.AllGrenades is not null
		|| payload.Bomb is not null;

	private static string BombSiteOf(string? site) => site?.Trim().ToUpperInvariant() switch
	{
		"A" => "A",
		"B" => "B",
		_ => string.Empty,
	};

	private static string CurrentBombState(GsiPayload payload)
	{
		var direct = NormalizeBomb(payload.Bomb?.State);
		return direct.Length > 0 ? direct : NormalizeBomb(payload.Round?.Bomb);
	}

	private static string DisplayBombState(GsiPayload payload)
	{
		var state = CurrentBombState(payload);
		if (state.Length == 0)
		{
			return state;
		}

		if ((state == "defused" || state == "exploded")
			&& !string.Equals(payload.Round?.Phase, "over", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		return state;
	}

	private static string RoundHistoryOf(GsiPayload payload)
	{
		var wins = payload.MapRoundWins ?? payload.Map?.RoundWins;
		if (wins is null || wins.Count == 0)
		{
			return string.Empty;
		}

		return string.Concat(wins
			.Where(entry => int.TryParse(entry.Key, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
			.OrderBy(entry => int.Parse(entry.Key, System.Globalization.CultureInfo.InvariantCulture))
			.Select(entry => RoundWinnerToken(entry.Value)));
	}

	private static char RoundWinnerToken(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return '?';
		}

		if (value.StartsWith("ct", StringComparison.OrdinalIgnoreCase))
		{
			return 'C';
		}

		return value.StartsWith("t", StringComparison.OrdinalIgnoreCase) ? 'T' : '?';
	}

	private static string? MapNameOf(GsiPayload? payload) =>
		string.IsNullOrWhiteSpace(payload?.Map?.Name) ? null : payload.Map.Name;

	private static (double X, double Y, double Z)? ParsePosition(string? position)
	{
		if (string.IsNullOrWhiteSpace(position))
		{
			return null;
		}

		var parts = position.Split(',', StringSplitOptions.TrimEntries);
		if (parts.Length != 3
			|| !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
			|| !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
			|| !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
		{
			return null;
		}

		return (x, y, z);
	}

	private static string? ResolveBombCarrier(GsiPayload payload, GsiPlayer? focus)
	{
		var carrier = payload.Bomb?.Player;
		if (string.IsNullOrWhiteSpace(carrier))
		{
			return null;
		}

		if (string.Equals(focus?.SteamId, carrier, StringComparison.Ordinal))
		{
			return focus?.Name;
		}

		if (payload.AllPlayers is not null && payload.AllPlayers.TryGetValue(carrier, out var holder) && holder is not null)
		{
			return holder.Name;
		}

		return null;
	}

	private static void CountGrenades(Dictionary<string, GsiGrenade> grenades, ref int smokes, ref int fire, ref int total)
	{
		foreach (var grenade in grenades.Values)
		{
			if (grenade is null)
			{
				continue;
			}

			total++;
			if (string.Equals(grenade.Type, "smoke", StringComparison.OrdinalIgnoreCase) && grenade.EffectTime > 0)
			{
				smokes++;
			}

			if (string.Equals(grenade.Type, "inferno", StringComparison.OrdinalIgnoreCase)
				&& grenade.Flames is { Count: > 0 })
			{
				fire++;
			}
		}
	}

	private string? ConsoleChatLine()
	{
		var chat = _console.LatestChat;
		if (chat is null || DateTimeOffset.UtcNow - chat.Value.At > TimeSpan.FromMinutes(5))
		{
			return null;
		}

		return $"{chat.Value.Player}: {chat.Value.Text}";
	}

	public static int LossBonusOf(string history, string? team)
	{
		if (string.IsNullOrEmpty(history) || (team != "CT" && team != "T"))
		{
			return 0;
		}

		var losses = 0;
		for (var i = history.Length - 1; i >= 0; i--)
		{
			var mine = (history[i] == 'C' && team == "CT") || (history[i] == 'T' && team == "T");
			if (mine)
			{
				break;
			}

			if (history[i] is 'C' or 'T')
			{
				losses++;
			}
		}

		return losses == 0 ? 0 : Math.Min(1900 + 500 * losses, 3400);
	}

	private static (string? Weapon, int Kills) TopWeaponOf(Dictionary<string, int> weaponKills)
	{
		string? best = null;
		var bestKills = 0;
		foreach (var pair in weaponKills)
		{
			if (pair.Value > bestKills
				|| (pair.Value == bestKills && best is not null && string.Compare(pair.Key, best, StringComparison.Ordinal) < 0))
			{
				best = pair.Key;
				bestKills = pair.Value;
			}
		}

		return bestKills > 0 ? (best, bestKills) : (null, 0);
	}

	private static string ActiveWeaponName(GsiPlayer player)
	{
		var entry = ActiveWeaponEntry(player);
		return entry is null ? string.Empty : WeaponNames.DisplayName(entry.Name);
	}

	private static GsiWeapon? ActiveWeaponEntry(GsiPlayer player)
	{
		if (player.Weapons is null)
		{
			return null;
		}

		GsiWeapon? reloading = null;
		GsiWeapon? first = null;
		foreach (var weapon in player.Weapons.Values)
		{
			if (weapon is null || string.IsNullOrWhiteSpace(weapon.Name))
			{
				continue;
			}

			first ??= weapon;
			if (string.Equals(weapon.State, "active", StringComparison.OrdinalIgnoreCase))
			{
				return weapon;
			}

			if (reloading is null && string.Equals(weapon.State, "reloading", StringComparison.OrdinalIgnoreCase))
			{
				reloading = weapon;
			}
		}

		return reloading ?? first;
	}

	private static GsiSnapshot EmptySnapshot(bool connected) => new(
		connected, null, null, null, 0, 0, 0, null, null, null, null, null,
		false, null, null, false, 0, 0, false, false, 0, null, -1, -1,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0,
		0, 0, 0, false, PositionSources.Off, null, null, null, null,
		0, 0, 0, false, false, false, 0, null, null, string.Empty,
		null, null, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0.0, 0.0, null);

	private GsiSnapshot BuildSnapshot(GsiPayload payload, bool connected, SessionStats session)
	{
		var focus = FocusedPlayer(payload, null);
		var state = focus?.State;
		var stats = focus?.MatchStats;
		var entry = focus is not null ? ActiveWeaponEntry(focus) : null;
		var active = WeaponNames.DisplayName(entry?.Name);
		var ammoClip = entry?.AmmoClip ?? -1;
		var ammoReserve = entry?.AmmoReserve ?? -1;
		var weaponType = PlaceStore.Prettify(entry?.Type ?? string.Empty);

		var smokes = 0;
		var fire = 0;
		var grenadesActive = 0;
		if (payload.Grenades is not null)
		{
			CountGrenades(payload.Grenades, ref smokes, ref fire, ref grenadesActive);
		}

		if (payload.AllGrenades is not null)
		{
			CountGrenades(payload.AllGrenades, ref smokes, ref fire, ref grenadesActive);
		}

		double? phaseEndsIn = payload.PhaseCountdowns?.PhaseEndsIn;
		var position = ResolvePosition(payload, focus, null);
		var bombCarrier = ResolveBombCarrier(payload, focus);
		var topWeapon = TopWeaponOf(session.WeaponKills);
		var placeName = position is not null
			? SafeFindPlace(payload.Map?.Name, position.X, position.Y, position.Z)
			: null;
		return new GsiSnapshot(
			connected,
			payload.Map?.Name, payload.Map?.Mode, payload.Map?.Phase, payload.Map?.Round ?? 0,
			payload.Map?.TeamCt?.Score ?? 0, payload.Map?.TeamT?.Score ?? 0,
			payload.Map?.TeamCt?.Name, payload.Map?.TeamT?.Name,
			payload.Round?.Phase, DisplayBombState(payload) is { Length: > 0 } b ? b : null,
			phaseEndsIn,
			focus is not null,
			focus?.Name, NormalizeTeam(focus?.Team),
			state is not null && state.Health > 0,
			state?.Health ?? 0, state?.Armor ?? 0, state?.Helmet ?? false, (state?.Flashed ?? 0) > 0,
			state?.Money ?? 0, active, ammoClip, ammoReserve,
			stats?.Kills ?? 0, stats?.Deaths ?? 0, stats?.Assists ?? 0, stats?.Mvps ?? 0, stats?.Score ?? 0,
			smokes, fire,
			session.Kills, session.Deaths,
			session.Deaths > 0 ? (double)session.Kills / session.Deaths : session.Kills,
			position?.X ?? 0, position?.Y ?? 0, position?.Z ?? 0, position is not null,
			ResolvePositionSource(payload, focus, position),
			placeName, payload.Bomb?.Countdown, bombCarrier, BombSiteOf(payload.Round?.Bomb),
			state?.RoundKills ?? 0, state?.RoundHeadshots ?? 0, state?.RoundDamage ?? 0,
			(state?.Smoked ?? 0) > 0, (state?.Burning ?? 0) > 0, state?.DefuseKit ?? false,
			state?.EquipmentValue ?? 0, focus?.Activity, weaponType, RoundHistoryOf(payload),
			position?.FromConsole == true && !double.IsNaN(position.Yaw) ? position.Yaw : (double?)null,
			focus?.Clan,
			payload.Map?.TeamCt?.TimeoutsRemaining ?? 0,
			payload.Map?.TeamT?.TimeoutsRemaining ?? 0,
			payload.PhaseCountdowns?.Phase,
			grenadesActive,
			session.Streak,
			session.BestStreak,
			topWeapon.Weapon,
			topWeapon.Kills,
			session.RoundsPlayed,
			session.Damage,
			session.Hs,
			session.Elapsed,
			session.SessionMatchTime,
			ConsoleChatLine());
	}

	private static GsiPayload TestPayload() => new(
		Auth: null,
		Provider: new GsiProvider("Counter-Strike 2", 730, 1, "76561198000000000", 1),
		Map: new GsiMap("competitive", "de_mirage", "live", 5,
			new GsiTeam(3, "CTs", 1, 0), new GsiTeam(1, "Ts", 1, 0), 13, null),
		Round: new GsiRound("live", null, null),
		// A genuine point inside Mirage Middle (center of its env_cs_place volume),
		// so Simulate demonstrates coordinates and place lookup the way a
		// spectator feed would. Real player feeds carry no position at all.
		Player: new GsiPlayer("76561198000000000", "TestPlayer", null, 1, "CT", "playing",
			new GsiPlayerState(100, 100, true, 0, 0, 0, 800, 0, 0, 0, 4700, false),
			new Dictionary<string, GsiWeapon>
			{
				["weapon_0"] = new("weapon_ak47", "default", "Rifle", "active", 30, 30, 90),
				["weapon_1"] = new("weapon_knife_karambit", null, "Knife", "holstered", null, null, null),
			},
			new GsiMatchStats(4, 1, 2, 0, 10), null, "-503, -735, -148"),
		AllPlayers: null,
		MapRoundWins: null,
		PhaseCountdowns: new GsiPhaseCountdowns("live", 95.5),
		Grenades: null,
		AllGrenades: null,
		Bomb: new GsiBomb("carried", null, null));
}

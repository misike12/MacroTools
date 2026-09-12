using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
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
	double? BombCountdown,
	string? BombCarrier);

public sealed class GsiService : IDisposable
{
	private static readonly TimeSpan ConnectedWindow = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
	private const int MaxBodyBytes = 4 * 1024 * 1024;
	private const int MaxConnections = 8;

	private readonly ILogger _logger;
	private readonly object _gate = new();
	private readonly SemaphoreSlim _handlers = new(MaxConnections, MaxConnections);
	private TcpListener? _listener;
	private CancellationTokenSource? _cts;
	private Task? _acceptTask;
	private Task? _consumeTask;
	private Channel<GsiPayload>? _channel;
	private int _port;
	private string _authToken = string.Empty;
	private string _steamIdFilter = string.Empty;
	private bool _disposed;

	private GsiPayload? _last;
	private long _sequence;
	private DateTimeOffset? _lastReceivedAt;
	private int _sessionKills;
	private int _sessionDeaths;

	public GsiService(ILogger logger)
	{
		_logger = logger.ForContext<GsiService>();
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
			_sessionKills = 0;
			_sessionDeaths = 0;
		}
	}

	public void InjectTestState() => ApplyPayload(TestPayload(), bypassAuth: true);

	public GsiSnapshot Snapshot()
	{
		lock (_gate)
		{
			var connected = _lastReceivedAt is { } seen && DateTimeOffset.UtcNow - seen <= ConnectedWindow;
			var payload = _last;
			if (!connected || payload is null)
			{
				return EmptySnapshot(false);
			}

			return BuildSnapshot(payload, connected: true);
		}
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

		_listener = null;
		_cts = null;
		_channel = null;
		_acceptTask = null;
		_consumeTask = null;
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
				break;
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

		if (payload is null)
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
			_lastReceivedAt = DateTimeOffset.UtcNow;

			var previous = _last;
			_last = payload;
			if (MapNameOf(previous) is not null && MapNameOf(payload) is not null
				&& !string.Equals(MapNameOf(previous), MapNameOf(payload), StringComparison.OrdinalIgnoreCase))
			{
				_sessionKills = 0;
				_sessionDeaths = 0;
			}

			events.AddRange(DiffLocked(previous, payload));
		}

		if (events.Count > 0)
		{
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

		var previousBomb = NormalizeBomb(previous?.Bomb?.State);
		var bomb = NormalizeBomb(current.Bomb?.State);
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
					["site"] = current.Round?.Bomb ?? string.Empty,
				}));
			}
		}

		var focus = FocusedPlayer(current, previous);
		var previousFocus = previous is not null ? FocusedPlayer(previous, null) : null;
		if (focus is not null)
		{
			var position = ParsePosition(focus.Position);
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
					}));
				}

				_sessionKills += kills - previousKills;
			}

			if (previous is not null && deaths > previousDeaths)
			{
				_sessionDeaths += deaths - previousDeaths;
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

		if (current.AllPlayers is not null && current.AllPlayers.TryGetValue(wanted, out var tracked))
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

		if (payload.AllPlayers is not null && payload.AllPlayers.TryGetValue(carrier, out var holder))
		{
			return holder.Name;
		}

		return null;
	}

	private static string ActiveWeaponName(GsiPlayer player)
	{
		if (player.Weapons is null)
		{
			return string.Empty;
		}

		foreach (var weapon in player.Weapons.Values)
		{
			if (string.Equals(weapon.State, "active", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrWhiteSpace(weapon.Name))
			{
				return StripWeaponPrefix(weapon.Name);
			}
		}

		foreach (var weapon in player.Weapons.Values)
		{
			if (!string.IsNullOrWhiteSpace(weapon.Name))
			{
				return StripWeaponPrefix(weapon.Name);
			}
		}

		return string.Empty;
	}

	private static string StripWeaponPrefix(string name) =>
		name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? name[7..] : name;

	private static GsiSnapshot EmptySnapshot(bool connected) => new(
		connected, null, null, null, 0, 0, 0, null, null, null, null, null,
		false, null, null, false, 0, 0, false, false, 0, null, -1, -1,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0,
		0, 0, 0, false, null, null);

	private GsiSnapshot BuildSnapshot(GsiPayload payload, bool connected)
	{
		var focus = FocusedPlayer(payload, null);
		var state = focus?.State;
		var stats = focus?.MatchStats;
		var active = focus is not null ? ActiveWeaponName(focus) : string.Empty;
		var ammoClip = -1;
		var ammoReserve = -1;
		if (focus?.Weapons is not null)
		{
			foreach (var weapon in focus.Weapons.Values)
			{
				if (string.Equals(weapon.State, "active", StringComparison.OrdinalIgnoreCase))
				{
					ammoClip = weapon.AmmoClip ?? -1;
					ammoReserve = weapon.AmmoReserve ?? -1;
					break;
				}
			}
		}

		var smokes = 0;
		var fire = 0;
		if (payload.Grenades is not null)
		{
			foreach (var grenade in payload.Grenades.Values)
			{
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

		double? phaseEndsIn = payload.PhaseCountdowns?.PhaseEndsIn;
		var position = ParsePosition(focus?.Position);
		var bombCarrier = ResolveBombCarrier(payload, focus);
		return new GsiSnapshot(
			connected,
			payload.Map?.Name, payload.Map?.Mode, payload.Map?.Phase, payload.Map?.Round ?? 0,
			payload.Map?.TeamCt?.Score ?? 0, payload.Map?.TeamT?.Score ?? 0,
			payload.Map?.TeamCt?.Name, payload.Map?.TeamT?.Name,
			payload.Round?.Phase, NormalizeBomb(payload.Bomb?.State) is { Length: > 0 } b ? b : null,
			phaseEndsIn,
			focus is not null,
			focus?.Name, NormalizeTeam(focus?.Team),
			state is not null && state.Health > 0,
			state?.Health ?? 0, state?.Armor ?? 0, state?.Helmet ?? false, (state?.Flashed ?? 0) > 0,
			state?.Money ?? 0, active, ammoClip, ammoReserve,
			stats?.Kills ?? 0, stats?.Deaths ?? 0, stats?.Assists ?? 0, stats?.Mvps ?? 0, stats?.Score ?? 0,
			smokes, fire,
			_sessionKills, _sessionDeaths,
			_sessionDeaths > 0 ? (double)_sessionKills / _sessionDeaths : _sessionKills,
			position?.X ?? 0, position?.Y ?? 0, position?.Z ?? 0, position is not null,
			payload.Bomb?.Countdown, bombCarrier);
	}

	private static GsiPayload TestPayload() => new(
		Auth: null,
		Provider: new GsiProvider("Counter-Strike 2", 730, 1, "76561198000000000", 1),
		Map: new GsiMap("competitive", "de_mirage", "live", 5,
			new GsiTeam(3, "CTs", 1, 0), new GsiTeam(1, "Ts", 1, 0), 13),
		Round: new GsiRound("live", null, null),
		Player: new GsiPlayer("76561198000000000", "TestPlayer", null, 1, "CT", "playing",
			new GsiPlayerState(100, 100, true, 0, 0, 0, 800, 0, 0, 0, 4700, false),
			new Dictionary<string, GsiWeapon>
			{
				["weapon_0"] = new("weapon_ak47", "default", "Rifle", "active", 30, 30, 90),
				["weapon_1"] = new("weapon_knife_karambit", null, "Knife", "holstered", null, null, null),
			},
			new GsiMatchStats(4, 1, 2, 0, 10), null, null),
		AllPlayers: null,
		PhaseCountdowns: new GsiPhaseCountdowns("live", 95.5),
		Grenades: null,
		Bomb: new GsiBomb("carried", null, null));
}

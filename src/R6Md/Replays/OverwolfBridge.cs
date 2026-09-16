using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace R6Md.Replays;

// Localhost HTTP receiver for the optional Overwolf bridge
// (Tools/OverwolfBridge). The bridge is a tiny private Overwolf app that
// forwards GEP match data over loopback; this listener turns those posts
// into the same snapshot/event pipeline the replay watcher feeds. Raw TCP
// on purpose: HttpListener would need an http.sys URL reservation, a plain
// socket just works. Disabled by default; binding is loopback-only, with an
// optional shared token when the user wants one.
public sealed class OverwolfBridge : IDisposable
{
	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

	private readonly ReplayService _replays;
	private readonly Serilog.ILogger _logger;
	private readonly object _gate = new();
	private TcpListener? _listener;
	private CancellationTokenSource? _cts;
	private Task? _acceptTask;
	private int _port;
	private string _token = string.Empty;
	private bool _disposed;

	public OverwolfBridge(ReplayService replays, Serilog.ILogger logger)
	{
		_replays = replays;
		_logger = logger.ForContext<OverwolfBridge>();
	}

	public bool Running
	{
		get
		{
			lock (_gate)
			{
				return _listener is not null;
			}
		}
	}

	public bool Start(int port, string token)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return false;
			}

			StopLocked();
			try
			{
				_listener = new TcpListener(IPAddress.Loopback, port);
				_listener.Start();
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Overwolf bridge could not bind port {Port}.", port);
				_listener = null;
				return false;
			}

			_port = port;
			_token = token;
			_cts = new CancellationTokenSource();
			_acceptTask = AcceptLoopAsync(_listener, _cts.Token);
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

		_cts?.Dispose();
		_cts = null;
		if (_listener is not null)
		{
			try
			{
				_listener.Stop();
			}
			catch (Exception)
			{
			}

			_listener = null;
		}

		_acceptTask = null;
	}

	private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
	{
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
			catch (Exception ex)
			{
				_logger.Debug(ex, "Overwolf bridge accept failed.");
				break;
			}

			_ = HandleAsync(client, cancellationToken);
		}
	}

	private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
	{
		using (client)
		{
			string head;
			string body;
			try
			{
				using var timeout = new CancellationTokenSource(ReadTimeout);
				using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
				(head, body) = await ReadRequestAsync(client, linked.Token);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Overwolf bridge read failed.");
				return;
			}

			var (status, text) = Route(head, body);
			var payload = $"{{\"ok\":{(status == 200 ? "true" : "false")},\"message\":\"{text}\"}}";
			var response = $"HTTP/1.1 {status} {(status == 200 ? "OK" : status == 401 ? "Unauthorized" : status == 404 ? "Not Found" : "Bad Request")}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(payload)}\r\nConnection: close\r\n\r\n{payload}";
			try
			{
				var bytes = Encoding.UTF8.GetBytes(response);
				await client.GetStream().WriteAsync(bytes, cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Overwolf bridge write failed.");
			}
		}
	}

	private (int Status, string Message) Route(string head, string body)
	{
		var lines = head.Split(["\r\n"], StringSplitOptions.None);
		if (lines.Length == 0)
		{
			return (400, "empty request");
		}

		var request = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (request.Length < 2 || !string.Equals(request[0], "POST", StringComparison.OrdinalIgnoreCase))
		{
			return (404, "POST /r6/event only");
		}

		var path = request[1].Split('?')[0];
		if (!string.Equals(path, "/r6/event", StringComparison.OrdinalIgnoreCase))
		{
			return (404, "POST /r6/event only");
		}

		string token;
		lock (_gate)
		{
			token = _token;
		}

		if (!string.IsNullOrEmpty(token))
		{
			var authorized = lines
				.Skip(1)
				.Select(line => line.Split(':', 2))
				.Any(pair => pair.Length == 2
					&& pair[0].Trim().Equals("Authorization", StringComparison.OrdinalIgnoreCase)
					&& pair[1].Trim() == $"Bearer {token}");
			if (!authorized)
			{
				return (401, "bad token");
			}
		}

		try
		{
			using var document = JsonDocument.Parse(body);
			Ingest(document.RootElement);
			return (200, "accepted");
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Overwolf bridge payload rejected.");
			return (400, "bad payload");
		}
	}

	private void Ingest(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new JsonException("Object expected.");
		}

		var type = root.TryGetProperty("type", out var kind) && kind.ValueKind == JsonValueKind.String
			? kind.GetString() ?? string.Empty
			: string.Empty;
		if (string.Equals(type, "info", StringComparison.OrdinalIgnoreCase))
		{
			_replays.IngestLiveInfo(new LiveMatchFrame(
				ReadString(root, "phase"),
				ReadString(root, "map"),
				ReadString(root, "mode"),
				ReadInt(root, "blue"),
				ReadInt(root, "orange"),
				ReadRoster(root),
				DateTimeOffset.UtcNow));
			return;
		}

		if (string.Equals(type, "event", StringComparison.OrdinalIgnoreCase))
		{
			var name = root.TryGetProperty("name", out var item) && item.ValueKind == JsonValueKind.String
				? item.GetString() ?? string.Empty
				: string.Empty;
			switch (name.ToLowerInvariant())
			{
				case "kill":
				case "headshot":
				case "death":
					_replays.IngestLiveEvent(
						name,
						ReadString(root, "player"),
						ReadString(root, "target"),
						root.TryGetProperty("headshot", out var hs) && hs.ValueKind == JsonValueKind.True);
					return;
				case "roundoutcome":
				case "matchoutcome":
					_replays.IngestLiveOutcome(
						name,
						root.TryGetProperty("outcome", out var outcome)
							&& outcome.ValueKind == JsonValueKind.String
							&& string.Equals(outcome.GetString(), "victory", StringComparison.OrdinalIgnoreCase));
					return;
			}
		}

		throw new JsonException($"Unknown payload type '{type}'.");
	}

	private static List<LiveRosterEntry> ReadRoster(JsonElement root)
	{
		var roster = new List<LiveRosterEntry>();
		if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
		{
			return roster;
		}

		foreach (var player in players.EnumerateArray())
		{
			if (player.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var name = player.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
				? nameElement.GetString() ?? string.Empty
				: string.Empty;
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			roster.Add(new LiveRosterEntry(
				name,
				player.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.String
					? team.GetString() ?? string.Empty
					: string.Empty,
				player.TryGetProperty("operator", out var op) && op.ValueKind == JsonValueKind.String
					? op.GetString() ?? string.Empty
					: string.Empty,
				ReadEntryInt(player, "kills"),
				ReadEntryInt(player, "deaths"),
				ReadEntryInt(player, "hp"),
				player.TryGetProperty("local", out var local) && local.ValueKind == JsonValueKind.True));
		}

		return roster;
	}

	private static string ReadString(JsonElement root, string name) =>
		root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString() ?? string.Empty
			: string.Empty;

	private static int ReadInt(JsonElement root, string name) =>
		root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
			? value
			: 0;

	private static int ReadEntryInt(JsonElement entry, string name) =>
		entry.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
			? Math.Max(0, value)
			: 0;

	private static async Task<(string Head, string Body)> ReadRequestAsync(TcpClient client, CancellationToken cancellationToken)
	{
		var stream = client.GetStream();
		var buffer = new byte[4096];
		var received = new List<byte>();
		var contentLength = 0;
		var headerEnd = -1;
		var continued = false;
		bool? chunked = null;

		while (!cancellationToken.IsCancellationRequested)
		{
			var read = await stream.ReadAsync(buffer, cancellationToken);
			if (read <= 0)
			{
				break;
			}

			received.AddRange(buffer.Take(read));
			if (headerEnd < 0)
			{
				headerEnd = IndexOfHeaderEnd(received);
				if (headerEnd >= 0)
				{
					var head = Encoding.ASCII.GetString(received.ToArray(), 0, headerEnd);
					contentLength = ParseContentLength(head);
					chunked = IsChunked(head);
				}
			}

			// HttpClient posts with Expect: 100-continue and holds the body
			// until answered; answer at once so the body actually arrives.
			if (headerEnd >= 0 && !continued && contentLength > 0 && WantsContinue(received, headerEnd))
			{
				continued = true;
				try
				{
					await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"), cancellationToken);
				}
				catch (Exception)
				{
					break;
				}
			}

			if (headerEnd >= 0 && chunked == true)
			{
				var decoded = TryDecodeChunks(received, headerEnd);
				if (decoded is not null)
				{
					return (
						Encoding.ASCII.GetString(received.ToArray(), 0, headerEnd),
						Encoding.UTF8.GetString(decoded));
				}
			}
			else if (headerEnd >= 0 && received.Count >= headerEnd + 4 + contentLength)
			{
				break;
			}

			if (received.Count > 2 * 1024 * 1024)
			{
				break;
			}
		}

		if (headerEnd < 0)
		{
			return (string.Empty, string.Empty);
		}

		var bodyBytes = received.Skip(headerEnd + 4).Take(contentLength).ToArray();
		return (
			Encoding.ASCII.GetString(received.ToArray(), 0, headerEnd),
			Encoding.UTF8.GetString(bodyBytes));
	}

	private static bool IsChunked(string head) =>
		head.Split(["\r\n"], StringSplitOptions.None)
			.Skip(1)
			.Select(line => line.Split(':', 2))
			.Any(pair => pair.Length == 2
				&& pair[0].Trim().Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
				&& pair[1].Split(',').Any(part => part.Trim().Equals("chunked", StringComparison.OrdinalIgnoreCase)));

	private static byte[]? TryDecodeChunks(List<byte> received, int headerEnd)
	{
		var body = received.Skip(headerEnd + 4).ToList();
		var decoded = new List<byte>();
		var at = 0;

		while (true)
		{
			var lineEnd = IndexOfCrLf(body, at);
			if (lineEnd < 0)
			{
				return null;
			}

			var sizeText = Encoding.ASCII.GetString(body.ToArray(), at, lineEnd - at).Split(';')[0].Trim();
			if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var size)
				|| size < 0
				|| size > 1024 * 1024)
			{
				return null;
			}

			if (body.Count < lineEnd + 2 + size + 2)
			{
				return null;
			}

			if (size == 0)
			{
				return decoded.ToArray();
			}

			decoded.AddRange(body.Skip(lineEnd + 2).Take(size));
			at = lineEnd + 2 + size + 2;
			if (decoded.Count > 1024 * 1024)
			{
				return null;
			}
		}
	}

	private static int IndexOfCrLf(List<byte> body, int start)
	{
		for (var i = start; i + 1 < body.Count; i++)
		{
			if (body[i] == (byte)'\r' && body[i + 1] == (byte)'\n')
			{
				return i;
			}
		}

		return -1;
	}

	private static bool WantsContinue(List<byte> received, int headerEnd)
	{
		var head = Encoding.ASCII.GetString(received.ToArray(), 0, headerEnd);
		return head.Split(["\r\n"], StringSplitOptions.None)
			.Skip(1)
			.Select(line => line.Split(':', 2))
			.Any(pair => pair.Length == 2
				&& pair[0].Trim().Equals("Expect", StringComparison.OrdinalIgnoreCase)
				&& pair[1].Trim().StartsWith("100-continue", StringComparison.OrdinalIgnoreCase));
	}

	private static int IndexOfHeaderEnd(List<byte> received)
	{
		for (var i = 0; i + 3 < received.Count; i++)
		{
			if (received[i] == (byte)'\r' && received[i + 1] == (byte)'\n'
				&& received[i + 2] == (byte)'\r' && received[i + 3] == (byte)'\n')
			{
				return i;
			}
		}

		return -1;
	}

	private static int ParseContentLength(string head)
	{
		foreach (var line in head.Split(["\r\n"], StringSplitOptions.None).Skip(1))
		{
			var pair = line.Split(':', 2);
			if (pair.Length == 2
				&& pair[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(pair[1].Trim(), out var length)
				&& length >= 0
				&& length <= 1024 * 1024)
			{
				return length;
			}
		}

		return 0;
	}
}

using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using R6Md.Replays;
using Serilog;

namespace R6Md.Tests;

[TestFixture]
public sealed class OverwolfBridgeTests
{
	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();

	private sealed class ScriptParser : ReplayParser
	{
		public ScriptParser()
			: base("r6-dissect.exe")
		{
		}

		public override Task<ReplayMatch?> ParseAsync(string path, CancellationToken cancellationToken) =>
			Task.FromResult<ReplayMatch?>(null);
	}

	private static int FreePort()
	{
		using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
		listener.Start();
		return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
	}

	[Test]
	public async Task Info_frame_surfaces_live_score_and_roster()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser());
		using var bridge = new OverwolfBridge(replays, TestLogger());
		var port = FreePort();
		Assert.That(bridge.Start(port, string.Empty), Is.True);
		using var http = new HttpClient();
		http.DefaultRequestHeaders.ExpectContinue = false;

		var response = await http.PostAsJsonAsync(
			$"http://127.0.0.1:{port}/r6/event",
			new
			{
				type = "info",
				phase = "action",
				map = "Kafe",
				mode = "Bomb",
				blue = 2,
				orange = 1,
				players = new[]
				{
					new { name = "Me.Live", team = "blue", @operator = "Jäger", kills = 3, deaths = 1, hp = 87, local = true },
					new { name = "Foe.Live", team = "orange", @operator = "Ash", kills = 2, deaths = 2, hp = 100, local = false },
				},
			},
			TestContext.CurrentContext.CancellationToken);

		var snapshot = replays.Snapshot();

		Assert.That(response.IsSuccessStatusCode, Is.True);
		Assert.That(snapshot.OwConnected, Is.True);
		Assert.That(snapshot.OwPhase, Is.EqualTo("action"));
		Assert.That(snapshot.YourScore, Is.EqualTo(2));
		Assert.That(snapshot.OppScore, Is.EqualTo(1));
		Assert.That(snapshot.YourName, Is.EqualTo("Me.Live"));
		Assert.That(snapshot.YourKills, Is.EqualTo(3));
		Assert.That(snapshot.YourHp, Is.EqualTo(87));
		Assert.That(snapshot.HasMatch, Is.True);
	}

	[Test]
	public async Task Kill_event_feeds_and_emits_without_session_accrual()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser());
		using var bridge = new OverwolfBridge(replays, TestLogger());
		var port = FreePort();
		bridge.Start(port, string.Empty);
		using var http = new HttpClient();
		var seen = new List<R6MatchEvent>();
		replays.MatchEvent += (_, e) => seen.Add(e);

		var response = await http.PostAsJsonAsync(
			$"http://127.0.0.1:{port}/r6/event",
			new { type = "event", name = "kill", player = "Me.Live", target = "Foe.Live", headshot = true },
			TestContext.CurrentContext.CancellationToken);

		var snapshot = replays.Snapshot();

		Assert.That(response.IsSuccessStatusCode, Is.True);
		Assert.That(seen.Select(e => e.EventId), Does.Contain(R6EventIds.Kill));
		Assert.That(seen.Select(e => e.EventId), Does.Contain(R6EventIds.Headshot));
		Assert.That(snapshot.LastKiller, Is.EqualTo("Me.Live"));
		Assert.That(snapshot.SessionKills, Is.EqualTo(0));
	}

	[Test]
	public async Task Raw_socket_post_is_accepted()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser());
		using var bridge = new OverwolfBridge(replays, TestLogger());
		var port = FreePort();
		bridge.Start(port, string.Empty);

		using var socket = new System.Net.Sockets.TcpClient();
		await socket.ConnectAsync(System.Net.IPAddress.Loopback, port, TestContext.CurrentContext.CancellationToken);
		var body = """{"type":"info","phase":"prep"}""";
		var request = $"POST /r6/event HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nContent-Type: application/json\r\nContent-Length: {System.Text.Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
		var bytes = System.Text.Encoding.UTF8.GetBytes(request);
		await socket.GetStream().WriteAsync(bytes, TestContext.CurrentContext.CancellationToken);

		var response = new List<byte>();
		var chunk = new byte[4096];
		int read;
		while ((read = await socket.GetStream().ReadAsync(chunk, TestContext.CurrentContext.CancellationToken)) > 0)
		{
			response.AddRange(chunk.Take(read));
		}

		var text = System.Text.Encoding.UTF8.GetString(response.ToArray());
		Assert.That(text, Does.Contain("200 OK"));
		Assert.That(replays.Snapshot().OwPhase, Is.EqualTo("prep"));
	}

	[Test]
	public async Task Wrong_token_and_paths_are_rejected()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser());
		using var bridge = new OverwolfBridge(replays, TestLogger());
		var port = FreePort();
		bridge.Start(port, "secret");
		using var http = new HttpClient();

		var open = await http.PostAsJsonAsync(
			$"http://127.0.0.1:{port}/r6/event",
			new { type = "info" },
			TestContext.CurrentContext.CancellationToken);
		var wrongPath = await http.PostAsJsonAsync(
			$"http://127.0.0.1:{port}/nope",
			new { type = "info" },
			TestContext.CurrentContext.CancellationToken);
		using var badRequest = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/r6/event")
		{
			Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
		};
		badRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret");
		var badPayload = await http.SendAsync(badRequest, TestContext.CurrentContext.CancellationToken);

		using var authed = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/r6/event")
		{
			Content = JsonContent.Create(new { type = "info", phase = "prep" }),
		};
		authed.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret");
		var ok = await http.SendAsync(authed, TestContext.CurrentContext.CancellationToken);

		Assert.That(open.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Unauthorized));
		Assert.That(wrongPath.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
		Assert.That(badPayload.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadRequest));
		Assert.That(ok.IsSuccessStatusCode, Is.True);
		Assert.That(replays.Snapshot().OwPhase, Is.EqualTo("prep"));
	}

	[Test]
	public void Live_outcome_sets_match_result_once()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser());
		var seen = new List<R6MatchEvent>();
		replays.MatchEvent += (_, e) => seen.Add(e);

		replays.IngestLiveOutcome("matchOutcome", won: true);
		replays.IngestLiveOutcome("matchOutcome", won: false);

		Assert.That(replays.Snapshot().MatchOutcome, Is.EqualTo("victory"));
		Assert.That(seen.Count(e => e.EventId == R6EventIds.MatchWon), Is.EqualTo(1));
		Assert.That(seen.Count(e => e.EventId == R6EventIds.MatchLost), Is.EqualTo(1));
	}
}

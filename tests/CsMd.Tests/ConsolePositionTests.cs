using System.Net.Http.Json;
using CsMd.Config;
using CsMd.Console;
using CsMd.Gsi;
using CsMd.Places;
using NUnit.Framework;
using Serilog;

namespace CsMd.Tests;

[TestFixture]
public sealed class ConsolePositionTests
{
	private string _directory = string.Empty;
	private string _log = string.Empty;

	[SetUp]
	public void SetUp()
	{
		_directory = Path.Combine(Path.GetTempPath(), "CsMdConsoleTest", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
		_log = Path.Combine(_directory, "console.log");
	}

	[TearDown]
	public void TearDown()
	{
		try
		{
			Directory.Delete(_directory, true);
		}
		catch (Exception)
		{
		}
	}

	[Test]
	public void Defaults_keep_tracking_off_and_F13()
	{
		Assert.That(CsSettings.Default.PositionTracking, Is.False);
		Assert.That(CsSettings.Default.PositionIntervalSeconds, Is.EqualTo(2));
		Assert.That(CsSettings.Default.PositionKeyCode, Is.EqualTo(124));
	}

	[Test]
	public void Missing_log_reads_not_present()
	{
		var watcher = new ConsolePositionWatcher(() => Path.Combine(_directory, "absent.log"));
		watcher.Poll();

		Assert.That(watcher.LogPresent, Is.False);
		Assert.That(watcher.LatestFix, Is.Null);
	}

	[Test]
	public void Setpos_exact_line_parses_with_timestamp()
	{
		WriteLog("09/13 18:40:02 setpos_exact 8.479980 -2165.968750 -167.968750;setang_exact 0.000000 84.010742 0.000000");
		var watcher = new ConsolePositionWatcher(() => _log);
		watcher.Poll();

		Assert.That(watcher.LogPresent, Is.True);
		var fix = watcher.LatestFix;
		Assert.That(fix, Is.Not.Null);
		Assert.That(fix!.Value.X, Is.EqualTo(8.47998).Within(0.0001));
		Assert.That(fix.Value.Y, Is.EqualTo(-2165.96875).Within(0.0001));
		Assert.That(fix.Value.Z, Is.EqualTo(-167.96875).Within(0.0001));
	}

	[Test]
	public void Yaw_parses_and_normalizes_to_compass()
	{
		WriteLog(Stamp(DateTimeOffset.UtcNow) + " setpos 1.0 2.0 3.0;setang 0.0 -90.0 0.0");
		var watcher = new ConsolePositionWatcher(() => _log);
		watcher.Poll();

		Assert.That(watcher.LatestFix!.Value.Yaw, Is.EqualTo(270.0).Within(0.001));

		WriteLog(Stamp(DateTimeOffset.UtcNow) + " setpos 1.0 2.0 3.0;setang 0.0 720.5 0.0");
		watcher.Poll();

		Assert.That(watcher.LatestFix!.Value.Yaw, Is.EqualTo(0.5).Within(0.001));
	}

	[Test]
	public void Missing_angles_leave_yaw_unknown()
	{
		WriteLog(Stamp(DateTimeOffset.UtcNow) + " setpos 1.0 2.0 3.0");
		var watcher = new ConsolePositionWatcher(() => _log);
		watcher.Poll();

		Assert.That(double.IsNaN(watcher.LatestFix!.Value.Yaw), Is.True);
	}

	[Test]
	public void Chat_lines_parse_with_scope_and_reject_noise()
	{
		var now = DateTimeOffset.UtcNow;
		WriteLog(string.Join("\n",
			Stamp(now.AddSeconds(-30)) + " [MINDENKI] misuuu5: what you doing man",
			Stamp(now.AddSeconds(-20)) + " ChangeGameUIState: CSGO_GAME_UI_STATE_INGAME -> CSGO_GAME_UI_STATE_PAUSEMENU",
			Stamp(now.AddSeconds(-10)) + " [Console] Unknown command 'single_player_pause'!",
			Stamp(now) + " [CSAPAT] Havoc: megyek A-ra",
			"misuuu5 csatlakozott."));
		var watcher = new ConsolePositionWatcher(() => _log);
		watcher.Poll();

		var chat = watcher.LatestChat;
		Assert.That(chat, Is.Not.Null);
		Assert.That(chat!.Value.Player, Is.EqualTo("Havoc"));
		Assert.That(chat.Value.Scope, Is.EqualTo("CSAPAT"));
		Assert.That(chat.Value.Text, Is.EqualTo("megyek A-ra"));
	}

	[Test]
	public void Chat_rejects_engine_lines_and_accepts_other_locales()
	{
		var now = DateTimeOffset.UtcNow;
		WriteLog(string.Join("\n",
			Stamp(now.AddSeconds(-40)) + " [Server] SV: Spawn Server: de_overpass",
			Stamp(now.AddSeconds(-30)) + " [Client] CL: Connected to 'loopback:1'",
			Stamp(now.AddSeconds(-25)) + " [ALL] SV: fake",
			Stamp(now.AddSeconds(-20)) + " [RenderSystem] TEXTURESTREAMING: Extremely low memory",
			Stamp(now.AddSeconds(-10)) + " [ALLE] Hans: weiter so",
			Stamp(now) + " [TOUS] Pierre: bien joue"));
		var watcher = new ConsolePositionWatcher(() => _log);
		watcher.Poll();

		var chat = watcher.LatestChat;
		Assert.That(chat, Is.Not.Null);
		Assert.That(chat!.Value.Player, Is.EqualTo("Pierre"));
		Assert.That(chat.Value.Scope, Is.EqualTo("TOUS"));
	}

	[Test]
	public void Chat_stays_empty_without_real_chat()
	{
		var now = DateTimeOffset.UtcNow;
		WriteLog(string.Join("\n",
			Stamp(now.AddSeconds(-20)) + " [Server] SV: Spawn Server: de_overpass",
			Stamp(now) + " [RenderSystem] TEXTURESTREAMING: Extremely low memory: hot"));
		var watcher = new ConsolePositionWatcher(() => _log);
		watcher.Poll();

		Assert.That(watcher.LatestChat, Is.Null);
	}

	[Test]
	public void Newest_line_wins_and_plain_setpos_parses()
	{
		WriteLog(string.Join("\n",
			Stamp(DateTimeOffset.UtcNow.AddSeconds(-30)) + " setpos 1.0 2.0 3.0;setang 0.0 0.0 0.0",
			"[something else entirely]",
			Stamp(DateTimeOffset.UtcNow) + " setpos 4.0 5.0 6.0;setang 0.0 0.0 0.0"));
		var watcher = new ConsolePositionWatcher(() => _log);
		watcher.Poll();

		var fix = watcher.LatestFix;
		Assert.That(fix, Is.Not.Null);
		Assert.That(fix!.Value.X, Is.EqualTo(4.0));
		Assert.That(fix.Value.Y, Is.EqualTo(5.0));
		Assert.That(fix.Value.Z, Is.EqualTo(6.0));
	}

	[Test]
	public void Truncated_log_resets_and_garbage_is_ignored()
	{
		WriteLog(Stamp(DateTimeOffset.UtcNow) + " setpos 7.0 8.0 9.0;setang 0.0 0.0 0.0");
		var watcher = new ConsolePositionWatcher(() => _log);
		watcher.Poll();
		Assert.That(watcher.LatestFix, Is.Not.Null);

		WriteLog("fresh start, nothing yet");
		watcher.Poll();

		Assert.That(watcher.LogPresent, Is.True);
		Assert.That(watcher.LatestFix!.Value.X, Is.EqualTo(7.0));
	}

	[Test]
	public async Task Autopoll_loop_tracks_the_local_player()
	{
		WriteLog(Stamp(DateTimeOffset.UtcNow) + " setpos_exact 100.0 -200.0 64.0;setang_exact 0.0 90.0 0.0");
		var trigger = new StubTrigger();
		using var gsi = new GsiService(TestLogger(), new PlaceStore(TestLogger()), trigger, () => _log);
		gsi.UpdatePositionOptions(true, 1, 104);
		Assert.That(gsi.Start(0, null), Is.True);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			provider = new { name = "Counter-Strike 2", appid = 730, version = 1, steamid = "76561198000000000", timestamp = 1 },
			map = new { mode = "casual", name = "de_dust2", phase = "live", round = 1 },
			round = new { phase = "live" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100, armor = 0, helmet = false, money = 800 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
		GsiSnapshot snapshot;
		do
		{
			await Task.Delay(100, ct);
			snapshot = gsi.Snapshot();
		}
		while ((!snapshot.HasPosition || snapshot.PositionSource != PositionSources.Console) && DateTimeOffset.UtcNow < deadline);

		Assert.That(snapshot.HasPosition, Is.True);
		Assert.That(snapshot.PositionSource, Is.EqualTo(PositionSources.Console));
		Assert.That(snapshot.PosX, Is.EqualTo(100.0));
		Assert.That(snapshot.PosY, Is.EqualTo(-200.0));
		Assert.That(snapshot.PosZ, Is.EqualTo(64.0));
		Assert.That(trigger.Taps, Is.GreaterThan(0));
	}

	[Test]
	public async Task Console_fix_does_not_leak_onto_spectated_players()
	{
		WriteLog(Stamp(DateTimeOffset.UtcNow) + " setpos_exact 100.0 -200.0 64.0;setang_exact 0.0 90.0 0.0");
		var trigger = new StubTrigger();
		using var gsi = new GsiService(TestLogger(), new PlaceStore(TestLogger()), trigger, () => _log);
		gsi.UpdatePositionOptions(true, 1, 104);
		Assert.That(gsi.Start(0, null), Is.True);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			provider = new { name = "Counter-Strike 2", appid = 730, version = 1, steamid = "76561198000000000", timestamp = 1 },
			map = new { mode = "casual", name = "de_dust2", phase = "live", round = 1 },
			player = new
			{
				steamid = "76561199000000000",
				name = "Teammate",
				team = "CT",
				state = new { health = 100, armor = 0, helmet = false, money = 800 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		await Task.Delay(2500, ct);
		var snapshot = gsi.Snapshot();

		Assert.That(snapshot.HasPosition, Is.False);
		Assert.That(snapshot.PositionSource, Is.EqualTo(PositionSources.Waiting));
	}

	private void WriteLog(string text) => File.WriteAllText(_log, text + "\n");

	[Test]
	public async Task Console_chat_lines_raise_chat_events()
	{
		WriteLog(Stamp(DateTimeOffset.UtcNow) + " [ALL] Buddy: gl hf");
		var trigger = new StubTrigger();
		using var gsi = new GsiService(TestLogger(), new PlaceStore(TestLogger()), trigger, () => _log);
		gsi.UpdatePositionOptions(false, 1, 124);
		Assert.That(gsi.Start(0, null), Is.True);
		using var http = new HttpClient();
		var uri = $"http://127.0.0.1:{gsi.Port}/gsi";
		var seen = new List<GsiMatchEvent>();
		gsi.MatchEvent += (_, e) =>
		{
			lock (seen)
			{
				seen.Add(e);
			}
		};
		var ct = TestContext.CurrentContext.CancellationToken;

		await http.PostAsync(uri, JsonContent.Create(new
		{
			map = new { mode = "casual", name = "de_dust2", phase = "live", round = 1 },
			round = new { phase = "live" },
			player = new
			{
				steamid = "76561198000000000",
				name = "Me",
				team = "CT",
				state = new { health = 100 },
				match_stats = new { kills = 0, assists = 0, deaths = 0, mvps = 0, score = 0 },
			},
		}), ct);

		var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
		GsiMatchEvent? chat = null;
		while (DateTimeOffset.UtcNow < deadline && chat is null)
		{
			await Task.Delay(100, ct);
			lock (seen)
			{
				chat = seen.FirstOrDefault(e => e.EventId == GsiEventIds.ChatMessage);
			}
		}

		Assert.That(chat, Is.Not.Null);
		Assert.That(chat!.Payload["player"], Is.EqualTo("Buddy"));
		Assert.That(chat.Payload["scope"], Is.EqualTo("ALL"));
		Assert.That(chat.Payload["text"], Is.EqualTo("gl hf"));
		Assert.That(gsi.Snapshot().LastChat, Is.EqualTo("Buddy: gl hf"));
	}

	private static string Stamp(DateTimeOffset at)
	{
		var local = at.ToLocalTime();
		return $"{local.Month:00}/{local.Day:00} {local.Hour:00}:{local.Minute:00}:{local.Second:00}";
	}

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();

	private sealed class StubTrigger : IPositionTrigger
	{
		public int Taps;

		public bool IsGameFocused() => true;

		public bool Tap(byte scanCode)
		{
			Interlocked.Increment(ref Taps);
			return true;
		}
	}
}

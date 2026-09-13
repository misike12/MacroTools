using System.Net.Http.Json;
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

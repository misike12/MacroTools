using System.Text.Json;
using NUnit.Framework;
using R6Md.Replays;
using Serilog;

namespace R6Md.Tests;

[TestFixture]
public sealed class ReplayMappingTests
{
	private static string Fixture(string name) => File.ReadAllText(
		Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name));

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();

	private sealed class ScriptParser : ReplayParser
	{
		private readonly Func<string, string?> _read;

		public ScriptParser(Func<string, string?> read)
			: base("r6-dissect.exe") => _read = read;

		public new Task<ReplayMatch?> ParseAsync(string path, CancellationToken cancellationToken) =>
			Task.FromResult(_read(path) is string json ? ReplayJson.ParseMatch(json) : null);
	}

	[Test]
	public void Real_parser_output_maps_to_snapshot()
	{
		using var service = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		service.IntegrateMatch(ReplayJson.ParseMatch(Fixture("ranked-r1.json"))!, "ranked-r1");

		var snapshot = service.Snapshot();

		Assert.That(snapshot.HasMatch, Is.True);
		Assert.That(snapshot.MapName, Is.EqualTo("Chalet"));
		Assert.That(snapshot.MapMode, Is.EqualTo("Bomb"));
		Assert.That(snapshot.MatchType, Is.EqualTo("Ranked"));
		Assert.That(snapshot.Site, Is.EqualTo("2F Master Bedroom, 2F Office"));
		Assert.That(snapshot.RoundNumber, Is.EqualTo(1));
		Assert.That(snapshot.YourScore, Is.EqualTo(0));
		Assert.That(snapshot.OppScore, Is.EqualTo(1));
		Assert.That(snapshot.YourRole, Is.EqualTo("Attack"));
		Assert.That(snapshot.RoundHistory, Is.EqualTo("L"));
		Assert.That(snapshot.Players.Count, Is.EqualTo(10));
		Assert.That(snapshot.YourName, Is.EqualTo("Knoblauch.SOOS"));
		Assert.That(snapshot.RoundsTracked, Is.EqualTo(1));
	}

	[Test]
	public void Kills_emit_events_and_feed_and_session()
	{
		using var service = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		var seen = new List<R6MatchEvent>();
		service.MatchEvent += (_, e) => seen.Add(e);
		service.IntegrateMatch(ReplayJson.ParseMatch(Fixture("ranked-r1.json"))!, "ranked-r1");

		var snapshot = service.Snapshot();

		Assert.That(snapshot.HasLastKill, Is.True);
		Assert.That(snapshot.LastKiller, Is.Not.Empty);
		Assert.That(snapshot.HasFeed, Is.True);
		Assert.That(seen.Select(e => e.EventId), Does.Contain(R6EventIds.Kill));
		Assert.That(seen.Select(e => e.EventId), Does.Contain(R6EventIds.RoundLost));
		Assert.That(snapshot.SessionKills + snapshot.SessionDeaths + snapshot.SessionAssists, Is.GreaterThanOrEqualTo(0));
	}

	[Test]
	public void Tolerant_dto_survives_unknown_shapes()
	{
		Assert.That(ReplayJson.ParseMatch("{}"), Is.Not.Null);
		Assert.That(ReplayJson.ParseMatch("not json"), Is.Null);
		Assert.That(ReplayJson.ParseMatch(string.Empty), Is.Null);

		var legacy = ReplayJson.ParseMatch("""{"map":{"name":"BANK"},"teams":[],"matchFeedback":[{"type":"Kill","username":"a","target":"b"}]}""");
		Assert.That(legacy, Is.Not.Null);
		Assert.That(legacy!.MatchFeedback, Is.Not.Null);
		Assert.That(legacy.MatchFeedback![0].TypeName, Is.EqualTo("Kill"));
		Assert.That(MapNames.Display("KAFE_DOSTOYEVSKY"), Is.EqualTo("Kafe Dostoyevsky"));
		Assert.That(MapNames.Display("BANK"), Is.EqualTo("Bank"));
		Assert.That(MapNames.Display(null), Is.Empty);
		Assert.That(OperatorNames.Display("Jager"), Is.EqualTo("Jäger"));
		Assert.That(OperatorNames.Display("SomeNewOp"), Is.EqualTo("SomeNewOp"));
		Assert.That(MapNames.WinCondition("KilledOpponents"), Is.EqualTo("Elimination"));
		Assert.That(MapNames.WinCondition("Time"), Is.EqualTo("Time"));
	}

	[Test]
	public void Match_change_finalizes_previous_match()
	{
		using var service = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		var seen = new List<R6MatchEvent>();
		service.MatchEvent += (_, e) => seen.Add(e);
		service.IntegrateMatch(ReplayJson.ParseMatch(Fixture("ranked-r1.json"))!, "match-a");
		service.IntegrateMatch(ReplayJson.ParseMatch(Fixture("ranked-r1.json"))! with { MatchId = "other" }, "match-b");

		Assert.That(seen.Select(e => e.EventId), Does.Contain(R6EventIds.MatchLost));
		Assert.That(service.Snapshot().RoundsTracked, Is.EqualTo(1));
	}

	[Test]
	public void Simulate_loads_sample_match()
	{
		using var service = new ReplayService(TestLogger(), new ScriptParser(_ => null));
		service.InjectSample();

		var snapshot = service.Snapshot();

		Assert.That(snapshot.HasMatch, Is.True);
		Assert.That(snapshot.MapName, Is.EqualTo("Chalet"));
		Assert.That(snapshot.YourName, Is.EqualTo("You.Siege"));
		Assert.That(snapshot.HasFeed, Is.True);
	}
}

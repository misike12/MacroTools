using System.Text.Json;
using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using R6Md.Messaging;
using R6Md.Replays;
using R6Md.Widgets;
using Serilog;

namespace R6Md.Tests;

[TestFixture]
public sealed class PluginIntegrationTests
{
	private static string Fixture(string name) => File.ReadAllText(
		Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name));

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();

	private static PluginTestHarness CreateHarness(ReplayService replays) =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddSingleton(replays);
			builder.Services.AddSingleton<R6Md.Replays.OverwolfBridge>();
			builder.Services.AddSingleton(new R6Md.Config.R6SettingsProvider());
			builder.UseLocalization(Strings.LocalizationCatalog);
			builder.RegisterIntegration<PluginIntegration>();
		});

	private sealed class ScriptParser : ReplayParser
	{
		private readonly Func<string, string?> _read;

		public ScriptParser(Func<string, string?> read)
			: base("r6-dissect.exe") => _read = read;

		public override Task<ReplayMatch?> ParseAsync(string path, CancellationToken cancellationToken) =>
			Task.FromResult(_read(path) is string json ? ReplayJson.ParseMatch(json) : null);
	}

	[Test]
	public async Task Actions_simulate_reset_and_rescan()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		await using var harness = CreateHarness(replays);
		await harness.InitializeIntegrationsAsync();

		var simulate = await harness.Actions.ExecuteAsync("simulate-match", new Dictionary<string, object?>());
		var snapshot = replays.Snapshot();
		var rescan = await harness.Actions.ExecuteAsync("rescan-replays", new Dictionary<string, object?>());
		replays.ResetSessionStats();
		var missing = await harness.Actions.ExecuteAsync("open-replay-folder", new Dictionary<string, object?>());

		Assert.That(simulate.Succeeded, Is.True);
		Assert.That(snapshot.HasMatch, Is.True);
		Assert.That(rescan.Succeeded, Is.True);
		Assert.That(missing.Succeeded, Is.False);
	}

	[Test]
	public async Task Variables_read_from_snapshot()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		await using var harness = CreateHarness(replays);
		await harness.InitializeIntegrationsAsync();
		replays.InjectSample();

		var integration = HarnessIntegration(harness);

		Assert.That((await integration.ReadAsync("map", TestContext.CurrentContext.CancellationToken)).Value, Is.EqualTo("Chalet"));
		Assert.That((await integration.ReadAsync("your-score", TestContext.CurrentContext.CancellationToken)).Value, Is.EqualTo(2.0));
		Assert.That((await integration.ReadAsync("round-history", TestContext.CurrentContext.CancellationToken)).Value, Is.EqualTo("W"));
		Assert.That(await integration.ReadAsync("nope", TestContext.CurrentContext.CancellationToken), Is.EqualTo(MacroDeck.Sdk.Variables.VariableReading.Unavailable));
	}

	[Test]
	public async Task Service_emits_kill_and_round_events_for_sample()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		var seen = new List<R6MatchEvent>();
		replays.MatchEvent += (_, e) => seen.Add(e);
		replays.InjectSample();

		Assert.That(seen.Select(e => e.EventId), Does.Contain(R6EventIds.Kill));
		Assert.That(seen.Select(e => e.EventId), Does.Contain(R6EventIds.RoundWon));
		var kill = seen.First(e => e.EventId == R6EventIds.Kill);
		Assert.That(kill.Payload["player"], Is.EqualTo("You.Siege"));
	}

	[Test]
	public async Task Simulate_action_reports_tracking_states()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		await using var harness = CreateHarness(replays);
		await harness.InitializeIntegrationsAsync();

		var idle = (await harness.Actions.GetActionStateAsync(
			"simulate-match", new Dictionary<string, object?>())).DataAs<MacroDeck.Sdk.Actions.ActionStateSnapshot>();
		Assert.That(idle!.States.Select(s => s.Id), Is.EquivalentTo(["tracking", "idle"]));
		Assert.That(idle.ActiveStateId, Is.EqualTo("idle"));

		var simulate = await harness.Actions.ExecuteAsync("simulate-match", new Dictionary<string, object?>());
		Assert.That(simulate.Succeeded, Is.True);

		var tracking = (await harness.Actions.GetActionStateAsync(
			"simulate-match", new Dictionary<string, object?>())).DataAs<MacroDeck.Sdk.Actions.ActionStateSnapshot>();
		Assert.That(tracking!.ActiveStateId, Is.EqualTo("tracking"));
	}

	[Test]
	public async Task Messaging_mirrors_match_events()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		await using var harness = CreateHarness(replays);
		await harness.InitializeIntegrationsAsync();
		replays.InjectSample();

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		while (DateTimeOffset.UtcNow < deadline
			&& !harness.Context.Messages.Published.Any(m => m.Topic == R6MessageTopics.ForEvent(R6EventIds.Kill)))
		{
			await Task.Delay(50, TestContext.CurrentContext.CancellationToken);
		}

		Assert.That(
			harness.Context.Messages.Published.Select(m => m.Topic),
			Does.Contain(R6MessageTopics.ForEvent(R6EventIds.Kill)));
		Assert.That(
			harness.Context.Messages.Published.Select(m => m.Topic),
			Does.Contain(R6MessageTopics.ForEvent(R6EventIds.RoundWon)));
	}

	[Test]
	public async Task Messaging_answers_state_requests()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		await using var harness = CreateHarness(replays);
		await harness.InitializeIntegrationsAsync();
		replays.InjectSample();

		var reply = await harness.Context.Messages.DeliverRequestAsync(R6MessageTopics.StateGet);

		Assert.That(reply.HasValue, Is.True);
		var snapshot = reply!.Value;
		Assert.That(snapshot.GetProperty("hasMatch").GetBoolean(), Is.True);
		Assert.That(snapshot.GetProperty("mapName").GetString(), Is.EqualTo("Chalet"));
		Assert.That(snapshot.GetProperty("yourScore").GetDouble(), Is.EqualTo(2.0));
	}

	[Test]
	public void Message_topics_are_valid()
	{
		foreach (var eventId in new[]
		{
			R6EventIds.Kill, R6EventIds.Headshot, R6EventIds.YourKill, R6EventIds.YourDeath,
			R6EventIds.RoundWon, R6EventIds.RoundLost, R6EventIds.MatchWon, R6EventIds.MatchLost,
			R6EventIds.Ace, R6EventIds.Clutch, R6EventIds.StreakMilestone,
		})
		{
			Assert.That(
				MacroDeck.Sdk.Messaging.MessageTopic.IsValidTopic(R6MessageTopics.ForEvent(eventId)),
				Is.True,
				eventId);
		}

		Assert.That(MacroDeck.Sdk.Messaging.MessageTopic.IsValidTopic(R6MessageTopics.StateGet), Is.True);
	}

	[Test]
	public void Widget_supports_flows_and_standard_appearance()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		var integration = new PluginIntegration(
			replays,
			new R6Md.Config.R6SettingsProvider(),
			new R6Md.Replays.OverwolfBridge(replays, TestLogger()),
			TestLogger());
		var descriptor = integration.GetWidgetTypes()[0];

		Assert.That(descriptor.SupportsFlows, Is.True);
		Assert.That(
			descriptor.AppearanceProperties,
			Does.Contain(MacroDeck.Sdk.Widgets.WidgetAppearanceProperty.BackgroundColor));
		Assert.That(descriptor.DataSchema, Does.Contain("flows"));
	}

	[Test]
	public async Task Issues_resolve_against_the_watched_folder()
	{
		var root = Directory.CreateTempSubdirectory("r6md-issue").FullName;
		try
		{
			using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
			var context = new FakeIntegrationContext();
			var entry = context.Config.AddEntry("r6md");
			context.Config.SeedString(entry, R6Md.Config.R6Keys.ReplayRoot, root);
			context.Config.SeedString(entry, R6Md.Config.R6Keys.WatchEnabled, "true");
			var integration = new PluginIntegration(
				replays,
				new R6Md.Config.R6SettingsProvider(),
				new R6Md.Replays.OverwolfBridge(replays, TestLogger()),
				TestLogger());
			await integration.InitializeAsync(context);

			Assert.That(
				await integration.GetIssuesAsync(TestContext.CurrentContext.CancellationToken),
				Is.Empty);

			var resolution = await integration.ResolveIssueAsync(
				"no-replay-folder", TestContext.CurrentContext.CancellationToken);
			Assert.That(resolution.Success, Is.True);

			var unknown = await integration.ResolveIssueAsync("nope", TestContext.CurrentContext.CancellationToken);
			Assert.That(unknown.Success, Is.False);
			await integration.ShutdownAsync();
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Test]
	public async Task Rescan_does_not_reparse_unchanged_files()
	{
		var root = Directory.CreateTempSubdirectory("r6md-rescan").FullName;
		try
		{
			await File.WriteAllTextAsync(
				Path.Combine(root, "round-1.rec"), "pending", TestContext.CurrentContext.CancellationToken);
			var parses = 0;
			using var replays = new ReplayService(
				TestLogger(),
				new CountingParser(_ =>
				{
					System.Threading.Interlocked.Increment(ref parses);
					return Fixture("ranked-r1.json");
				}));
			replays.Start(root);

			var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
			while (System.Threading.Volatile.Read(ref parses) == 0 && DateTimeOffset.UtcNow < deadline)
			{
				await Task.Delay(250, TestContext.CurrentContext.CancellationToken);
			}

			Assert.That(System.Threading.Volatile.Read(ref parses), Is.EqualTo(1));

			await replays.RescanNowAsync(TestContext.CurrentContext.CancellationToken);
			await Task.Delay(TimeSpan.FromSeconds(7), TestContext.CurrentContext.CancellationToken);

			Assert.That(System.Threading.Volatile.Read(ref parses), Is.EqualTo(1));
			replays.Stop();
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	private sealed class CountingParser(Func<string, string?> read) : ReplayParser("r6-dissect.exe")
	{
		private readonly Func<string, string?> _read = read;

		public override Task<ReplayMatch?> ParseAsync(string path, CancellationToken cancellationToken) =>
			Task.FromResult(_read(path) is string json ? ReplayJson.ParseMatch(json) : null);
	}

	[Test]
	public async Task Backfill_parses_concurrent_files_without_dropping_any()
	{
		var root = Directory.CreateTempSubdirectory("r6md-backfill").FullName;
		try
		{
			for (var i = 1; i <= 5; i++)
			{
				var path = Path.Combine(root, $"round-{i}.rec");
				await File.WriteAllTextAsync(path, "pending", TestContext.CurrentContext.CancellationToken);
				File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-i));
			}

			var parses = 0;
			using var replays = new ReplayService(
				TestLogger(),
				new CountingParser(_ =>
				{
					System.Threading.Interlocked.Increment(ref parses);
					return Fixture("ranked-r1.json");
				}));
			replays.Start(root);

			var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
			while (System.Threading.Volatile.Read(ref parses) < 5 && DateTimeOffset.UtcNow < deadline)
			{
				await Task.Delay(250, TestContext.CurrentContext.CancellationToken);
			}

			Assert.That(System.Threading.Volatile.Read(ref parses), Is.EqualTo(5));
			Assert.DoesNotThrow(() => replays.Snapshot());
			replays.Stop();
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Test]
	public async Task Picker_card_serves_a_sample_without_stored_data()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => Fixture("ranked-r1.json")));
		var widget = new MatchHudWidget(replays, TestLogger());
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Preview,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiWidgetSurfaceAttributes.Sample] = JsonDocument.Parse("true").RootElement.Clone(),
				},
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);

		Assert.That(session, Is.Not.Null);
		var tree = JsonSerializer.Serialize(session!.BuildTree());
		Assert.That(tree, Does.Contain("r6-hud-g"));
		if (session is IAsyncDisposable asyncDisposable)
		{
			await asyncDisposable.DisposeAsync();
		}
	}

	[Test]
	public async Task Config_flow_collects_replay_settings()
	{
		var flow = new R6Md.Config.R6ConfigFlow();
		var first = await flow.StartAsync(new FakeConfigFlowContext(), TestContext.CurrentContext.CancellationToken);
		var second = await flow.SubmitAsync(
			"replays",
			new Dictionary<string, object?> { ["replay-root"] = "", ["watch-enabled"] = true },
			new FakeConfigFlowContext(),
			TestContext.CurrentContext.CancellationToken);
		var live = await flow.SubmitAsync(
			"live",
			new Dictionary<string, object?> { ["overwolf-enabled"] = true, ["overwolf-port"] = 32175.0 },
			new FakeConfigFlowContext(),
			TestContext.CurrentContext.CancellationToken);
		var done = await flow.SubmitAsync(
			"events",
			new Dictionary<string, object?> { ["events-kill"] = true, ["events-round"] = false, ["events-match"] = true, ["events-streak"] = true },
			new FakeConfigFlowContext(),
			TestContext.CurrentContext.CancellationToken);

		Assert.That(first.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(second.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(live.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(done.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Complete));

		var badRoot = await flow.SubmitAsync(
			"replays",
			new Dictionary<string, object?> { ["replay-root"] = "C:\\no-such-dir-xyz" },
			new FakeConfigFlowContext(),
			TestContext.CurrentContext.CancellationToken);
		var badPort = await flow.SubmitAsync(
			"live",
			new Dictionary<string, object?> { ["overwolf-port"] = 80.0 },
			new FakeConfigFlowContext(),
			TestContext.CurrentContext.CancellationToken);

		Assert.That(badRoot.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Error));
		Assert.That(badPort.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Error));
	}

	[Test]
	public void Action_and_event_ids_are_unique_across_the_plugin()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => null));
		using var bridge = new OverwolfBridge(replays, TestLogger());
		var integration = new PluginIntegration(replays, new R6Md.Config.R6SettingsProvider(), bridge, TestLogger());

		var duplicates = integration.Actions
			.GroupBy(a => a.Id)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();

		Assert.That(duplicates, Is.Empty);
		Assert.That(integration.Actions.Count, Is.EqualTo(4));
		Assert.That(integration.EventDefinitions.Count, Is.EqualTo(11));
		Assert.That(integration.Variables.Count, Is.EqualTo(44));
		Assert.That(integration.GetWidgetTypes().Count, Is.EqualTo(1));
		Assert.That(integration.GetWidgetTypes()[0].Id, Is.EqualTo("match-hud"));
	}

	[Test]
	public void Every_key_the_default_culture_declares_resolves_to_text()
	{
		foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
		{
			Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text), Is.True, key);
			Assert.That(text, Is.Not.Empty, key);
		}
	}

	private sealed class FakeConfigFlowContext : MacroDeck.Sdk.ConfigFlow.IConfigFlowContext
	{
		public MacroDeck.Sdk.ConfigFlow.IOAuthSession OAuth => throw new NotSupportedException();
	}

	private static PluginIntegration HarnessIntegration(PluginTestHarness harness) =>
		harness.Services.GetRequiredService<PluginIntegration>();
}

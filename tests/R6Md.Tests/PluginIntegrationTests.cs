using System.Text.Json;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
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
	public async Task Config_flow_collects_replay_settings()
	{
		var flow = new R6Md.Config.R6ConfigFlow();
		var first = await flow.StartAsync(new FakeConfigFlowContext(), TestContext.CurrentContext.CancellationToken);
		var second = await flow.SubmitAsync(
			"replays",
			new Dictionary<string, object?> { ["replay-root"] = "", ["watch-enabled"] = true },
			new FakeConfigFlowContext(),
			TestContext.CurrentContext.CancellationToken);
		var done = await flow.SubmitAsync(
			"events",
			new Dictionary<string, object?> { ["events-kill"] = true, ["events-round"] = false, ["events-match"] = true, ["events-streak"] = true },
			new FakeConfigFlowContext(),
			TestContext.CurrentContext.CancellationToken);

		Assert.That(first.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(second.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(done.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Complete));

		var badRoot = await flow.SubmitAsync(
			"replays",
			new Dictionary<string, object?> { ["replay-root"] = "C:\\no-such-dir-xyz" },
			new FakeConfigFlowContext(),
			TestContext.CurrentContext.CancellationToken);

		Assert.That(badRoot.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Error));
	}

	[Test]
	public void Action_and_event_ids_are_unique_across_the_plugin()
	{
		using var replays = new ReplayService(TestLogger(), new ScriptParser(_ => null));
		var integration = new PluginIntegration(replays, new R6Md.Config.R6SettingsProvider(), TestLogger());

		var duplicates = integration.Actions
			.GroupBy(a => a.Id)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();

		Assert.That(duplicates, Is.Empty);
		Assert.That(integration.Actions.Count, Is.EqualTo(4));
		Assert.That(integration.EventDefinitions.Count, Is.EqualTo(11));
		Assert.That(integration.Variables.Count, Is.EqualTo(36));
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

using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Serilog;
using CsMd.Config;
using CsMd.Gsi;

namespace CsMd.Tests;

[TestFixture]
public sealed class PluginIntegrationTests
{
	private static PluginTestHarness CreateHarness(GsiService gsi) =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddSingleton(gsi);
			builder.Services.AddSingleton(new CsSettingsProvider());
			builder.UseLocalization(Strings.LocalizationCatalog);
			builder.RegisterIntegration<PluginIntegration>();
		});

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness(new GsiService(TestLogger()));

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public async Task Reset_and_simulate_drive_the_service()
	{
		var gsi = new GsiService(TestLogger());
		await using var harness = CreateHarness(gsi);
		await harness.InitializeIntegrationsAsync();

		var simulate = await harness.Actions.ExecuteAsync(
			"simulate-match",
			new Dictionary<string, object?>());
		var integration = new PluginIntegration(gsi, new CsSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That(simulate.Succeeded, Is.True);
		Assert.That((await integration.ReadAsync("health")).Value, Is.EqualTo(100.0));
		Assert.That((await integration.ReadAsync("weapon")).Value, Is.EqualTo("AK-47"));
		Assert.That((await integration.ReadAsync("weapon-type")).Value, Is.EqualTo("Rifle"));
		Assert.That((await integration.ReadAsync("map-name")).Value, Is.EqualTo("de_mirage"));
		Assert.That((await integration.ReadAsync("player-activity")).Value, Is.EqualTo("playing"));
		Assert.That((await integration.ReadAsync("round-kills")).Value, Is.EqualTo(0.0));
		Assert.That((await integration.ReadAsync("round-headshots")).Value, Is.EqualTo(0.0));
		Assert.That((await integration.ReadAsync("round-damage")).Value, Is.EqualTo(0.0));
		Assert.That((await integration.ReadAsync("smoked")).Value, Is.EqualTo(false));
		Assert.That((await integration.ReadAsync("burning")).Value, Is.EqualTo(false));
		Assert.That((await integration.ReadAsync("defusekit")).Value, Is.EqualTo(false));
		Assert.That((await integration.ReadAsync("equip-value")).Value, Is.EqualTo(4700.0));
		Assert.That((await integration.ReadAsync("pos-x")).Value, Is.EqualTo(-503.0));
		Assert.That((await integration.ReadAsync("pos-y")).Value, Is.EqualTo(-735.0));
		Assert.That((await integration.ReadAsync("pos-z")).Value, Is.EqualTo(-148.0));
		Assert.That((await integration.ReadAsync("gsi-connected")).Value, Is.EqualTo(true));

		var reset = await harness.Actions.ExecuteAsync(
			"reset-session-stats",
			new Dictionary<string, object?>());

		Assert.That(reset.Succeeded, Is.True);
		await integration.ShutdownAsync();
		gsi.Dispose();
	}

	[Test]
	public async Task Simulate_event_fires_known_events_and_rejects_unknown()
	{
		var gsi = new GsiService(TestLogger());
		await using var harness = CreateHarness(gsi);
		await harness.InitializeIntegrationsAsync();
		var integration = new PluginIntegration(gsi, new CsSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var kill = await harness.Actions.ExecuteAsync(
			"simulate-event",
			new Dictionary<string, object?> { ["event"] = "player-kill" });

		Assert.That(kill.Succeeded, Is.True);

		var bogus = await harness.Actions.ExecuteAsync(
			"simulate-event",
			new Dictionary<string, object?> { ["event"] = "nuke-everything" });

		Assert.That(bogus.Succeeded, Is.False);
		await integration.ShutdownAsync();
		gsi.Dispose();
	}

	[Test]
	public async Task Install_action_reports_game_presence_as_states()
	{
		using var gsi = new GsiService(TestLogger());
		var action = new CsMd.Actions.InstallGsiConfigAction(new CsSettingsProvider(), gsi);

		Assert.That(action, Is.InstanceOf<MacroDeck.Sdk.Actions.IStateProviderActionDefinition>());
		var snapshot = await ((MacroDeck.Sdk.Actions.IStateProviderActionDefinition)action)
			.GetActionStateAsync(new Dictionary<string, object?>(), TestContext.CurrentContext.CancellationToken);

		Assert.That(snapshot, Is.Not.Null);
		Assert.That(snapshot!.States.Select(state => state.Id), Is.EquivalentTo(["ready", "missing"]));
		Assert.That(snapshot.ActiveStateId, Is.EqualTo("ready").Or.EqualTo("missing"));
	}

	[Test]
	public async Task New_computed_variables_read()
	{
		using var gsi = new GsiService(TestLogger());
		var integration = new PluginIntegration(gsi, new CsSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());
		gsi.InjectTestState();

		Assert.That(await integration.ReadAsync("loss-bonus"), Is.EqualTo(VariableReading.Unavailable));
		Assert.That((await integration.ReadAsync("session-adr")).Value, Is.EqualTo(0.0));
		Assert.That((await integration.ReadAsync("session-hs")).Value, Is.EqualTo(0.0));
		Assert.That((await integration.ReadAsync("hs-rate")).Value, Is.EqualTo(0.0));
		Assert.That((await integration.ReadAsync("match-elapsed")).Value, Is.GreaterThanOrEqualTo(0.0));
		Assert.That(await integration.ReadAsync("last-chat"), Is.EqualTo(VariableReading.Unavailable));
		await integration.ShutdownAsync();
		gsi.Dispose();
	}

	[Test]
	public async Task Unknown_state_reads_unavailable()
	{
		using var gsi = new GsiService(TestLogger());
		var integration = new PluginIntegration(gsi, new CsSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That(await integration.ReadAsync("health"), Is.EqualTo(VariableReading.Unavailable));
		Assert.That((await integration.ReadAsync("session-kills")).Value, Is.EqualTo(0.0));
		Assert.That(await integration.ReadAsync("no-such-variable"), Is.EqualTo(VariableReading.Unavailable));

		var write = await integration.SetValueAsync("health", 50.0);

		Assert.That(write.Status, Is.EqualTo(VariableWriteStatus.NotWritable));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Config_flow_completes_with_values()
	{
		var flow = new CsConfigFlow();
		var context = new FakeConfigFlowContext();

		var first = await flow.StartAsync(context, TestContext.CurrentContext.CancellationToken);
		var second = await flow.SubmitAsync("connection",
			new Dictionary<string, object?> { ["port"] = 3000.0 }, context, TestContext.CurrentContext.CancellationToken);
		var third = await flow.SubmitAsync("player",
			new Dictionary<string, object?>(), context, TestContext.CurrentContext.CancellationToken);
		var fourth = await flow.SubmitAsync("position",
			new Dictionary<string, object?> { ["position-tracking"] = true, ["position-interval"] = 2.0, ["position-vkey"] = 124.0 }, context, TestContext.CurrentContext.CancellationToken);
		var done = await flow.SubmitAsync("events",
			new Dictionary<string, object?>(), context, TestContext.CurrentContext.CancellationToken);

		Assert.That(first.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(second.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(third.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(fourth.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(done.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Complete));

		var badPort = await flow.SubmitAsync("connection",
			new Dictionary<string, object?> { ["port"] = 80.0 }, context, TestContext.CurrentContext.CancellationToken);
		var badSteam = await flow.SubmitAsync("player",
			new Dictionary<string, object?> { ["steam-id"] = "abc" }, context, TestContext.CurrentContext.CancellationToken);
		var badInterval = await flow.SubmitAsync("position",
			new Dictionary<string, object?> { ["position-interval"] = 60.0 }, context, TestContext.CurrentContext.CancellationToken);

		Assert.That(badPort.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Error));
		Assert.That(badSteam.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Error));
		Assert.That(badInterval.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Error));
	}

	[Test]
	public void Action_and_event_ids_are_unique_across_the_plugin()
	{
		using var gsi = new GsiService(TestLogger());
		var integration = new PluginIntegration(gsi, new CsSettingsProvider(), TestLogger());

		var duplicates = integration.Actions
			.GroupBy(a => a.Id)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();

		Assert.That(duplicates, Is.Empty);
		Assert.That(integration.Actions.Count, Is.EqualTo(4));
		Assert.That(integration.EventDefinitions.Count, Is.EqualTo(14));
		Assert.That(integration.Variables.Count, Is.EqualTo(69));
		Assert.That(integration.GetWidgetTypes().Count, Is.EqualTo(1));
		Assert.That(integration.GetWidgetTypes()[0].Id, Is.EqualTo("match-hud"));
	}

	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.misu.csmd"));
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
}

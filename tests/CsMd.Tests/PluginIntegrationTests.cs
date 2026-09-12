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
		Assert.That((await integration.ReadAsync("weapon")).Value, Is.EqualTo("ak47"));
		Assert.That((await integration.ReadAsync("map-name")).Value, Is.EqualTo("de_mirage"));
		Assert.That((await integration.ReadAsync("gsi-connected")).Value, Is.EqualTo(true));

		var reset = await harness.Actions.ExecuteAsync(
			"reset-session-stats",
			new Dictionary<string, object?>());

		Assert.That(reset.Succeeded, Is.True);
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
		var done = await flow.SubmitAsync("events",
			new Dictionary<string, object?>(), context, TestContext.CurrentContext.CancellationToken);

		Assert.That(first.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(second.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(third.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Step));
		Assert.That(done.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Complete));

		var badPort = await flow.SubmitAsync("connection",
			new Dictionary<string, object?> { ["port"] = 80.0 }, context, TestContext.CurrentContext.CancellationToken);
		var badSteam = await flow.SubmitAsync("player",
			new Dictionary<string, object?> { ["steam-id"] = "abc" }, context, TestContext.CurrentContext.CancellationToken);

		Assert.That(badPort.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Error));
		Assert.That(badSteam.Kind, Is.EqualTo(MacroDeck.Sdk.ConfigFlow.ConfigFlowResultKind.Error));
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
		Assert.That(integration.Actions.Count, Is.EqualTo(3));
		Assert.That(integration.EventDefinitions.Count, Is.EqualTo(11));
		Assert.That(integration.Variables.Count, Is.EqualTo(33));
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

using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Localization;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Tests;

[TestFixture]
public sealed class LocalizationCapabilityTests
{
	[Test]
	public async Task Localization_capability_serves_describe_and_catalog()
	{
		var builder = MacroDeckPlugin.CreatePlugin([]);
		builder.Services.AddSingleton(new MediaSettingsProvider());
		builder.Services.AddSingleton<IMediaControlService>(new FakeMediaControlService());
		builder.UseLocalization(Strings.LocalizationCatalog);
		builder.RegisterIntegration<PluginIntegration>();

		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.HostAsync(
			builder,
			manifest: new PluginTestManifest(id: "com.misu.windows-media"));
		var session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		Assert.That(host.Sessions.TryPeek(out var request), Is.True);
		Assert.That(request!.Capabilities.Select(capability => capability.Kind),
			Does.Contain(CapabilityKinds.Localization));
		TestContext.Out.WriteLine("Declared capabilities: " + request!.Capabilities.Count);
		foreach (var group in request.Capabilities.GroupBy(capability => capability.Kind).OrderBy(group => group.Key))
		{
			TestContext.Out.WriteLine($"  {group.Key}: {group.Count()}");
		}

		var describe = await session.InvokeAsync(
			CapabilityKinds.Localization,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Localization.Describe);
		Assert.That(describe.Succeeded, Is.True);
		var info = describe.DataAs<LocalizationDescribeResult>();
		Assert.That(info, Is.Not.Null);
		Assert.That(info!.Scope, Is.EqualTo("plugin:com.misu.windows-media"));
		Assert.That(info.Cultures, Does.Contain(info.DefaultCulture));

		var catalog = await session.InvokeAsync(
			CapabilityKinds.Localization,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Localization.Catalog,
			new LocalizationCatalogArguments { Culture = info.DefaultCulture });
		Assert.That(catalog.Succeeded, Is.True);
		var entries = catalog.DataAs<LocalizationCatalogResult>();
		Assert.That(entries, Is.Not.Null);
		Assert.That(entries!.Entries, Does.ContainKey("Config.Playback.Title"));
		Assert.That(entries.Entries, Does.ContainKey("Variables.AppVolume.DisplayName"));
	}
}

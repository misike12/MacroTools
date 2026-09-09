using MacroDeck.Plugin.Testing.Fakes;
using NUnit.Framework;
using Serilog;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Tests;

[TestFixture]
[Explicit]
public sealed class CatalogLiveTests
{
	[Test]
	public async Task Discover_lists_live_apps()
	{
		using var logger = new LoggerConfiguration().CreateLogger();
		var integration = new PluginIntegration(new WindowsMediaControlService(), new MediaSettingsProvider(), logger);
		await integration.InitializeAsync(new FakeIntegrationContext());

		var page = await integration.DiscoverAsync(
			new MacroDeck.Sdk.Variables.VariableCatalogQuery { PageSize = 50 },
			TestContext.CurrentContext.CancellationToken);

		foreach (var item in page.Items)
		{
			TestContext.Out.WriteLine($"catalog: id={item.Id} name={item.Name}");
		}

		var read = await integration.ReadAsync("Spotify.exe", TestContext.CurrentContext.CancellationToken);
		TestContext.Out.WriteLine($"spotify volume reading: {read.Value ?? (object)"unavailable"}");
		await integration.ShutdownAsync();
	}
}

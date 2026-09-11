using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ScreenControl.Actions;
using ScreenControl.Monitors;
using ScreenControl.Windows;
using Serilog;

namespace ScreenControl.Tests;

[TestFixture]
public sealed class PluginIntegrationTests
{
	private static PluginTestHarness CreateHarness(FakeMonitorService monitors, FakeWindowService windows) =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddSingleton<IMonitorService>(monitors);
			builder.Services.AddSingleton<IWindowService>(windows);
			builder.UseLocalization(Strings.LocalizationCatalog);
			builder.RegisterIntegration<PluginIntegration>();
		});

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness(new FakeMonitorService(), new FakeWindowService());

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public async Task Monitor_brightness_round_trips()
	{
		var monitors = new FakeMonitorService();
		await using var harness = CreateHarness(monitors, new FakeWindowService());
		await harness.InitializeIntegrationsAsync();

		var set = await harness.Actions.ExecuteAsync(
			"set-monitor-brightness",
			new Dictionary<string, object?> { ["monitor"] = 1.0, ["brightness"] = 70.0 });
		var adjust = await harness.Actions.ExecuteAsync(
			"adjust-monitor-brightness",
			new Dictionary<string, object?> { ["monitor"] = 1.0, ["delta"] = 10.0 });
		var bad = await harness.Actions.ExecuteAsync(
			"set-monitor-brightness",
			new Dictionary<string, object?> { ["monitor"] = 1.0, ["brightness"] = 150.0 });
		var missing = await harness.Actions.ExecuteAsync(
			"set-monitor-brightness",
			new Dictionary<string, object?> { ["monitor"] = 9.0, ["brightness"] = 70.0 });

		Assert.That(set.Succeeded, Is.True);
		Assert.That(adjust.Succeeded, Is.True);
		Assert.That(bad.Succeeded, Is.False);
		Assert.That(missing.Succeeded, Is.False);
		Assert.That(monitors.Levels[0], Is.EqualTo(80));
	}

	[Test]
	public async Task Monitor_input_set_and_cycle()
	{
		var monitors = new FakeMonitorService();
		await using var harness = CreateHarness(monitors, new FakeWindowService());
		await harness.InitializeIntegrationsAsync();

		var set = await harness.Actions.ExecuteAsync(
			"set-monitor-input",
			new Dictionary<string, object?> { ["monitor"] = 1.0, ["input"] = "dp1" });
		var cycle = await harness.Actions.ExecuteAsync(
			"cycle-monitor-input",
			new Dictionary<string, object?> { ["monitor"] = 1.0 });
		var bad = await harness.Actions.ExecuteAsync(
			"set-monitor-input",
			new Dictionary<string, object?> { ["monitor"] = 1.0, ["input"] = "vga" });

		Assert.That(set.Succeeded, Is.True);
		Assert.That(cycle.Succeeded, Is.True);
		Assert.That(bad.Succeeded, Is.False);
		Assert.That(monitors.Inputs[0], Is.EqualTo(0x10));
	}

	[Test]
	public async Task Window_actions_drive_the_fake_desktop()
	{
		var windows = new FakeWindowService();
		await using var harness = CreateHarness(new FakeMonitorService(), windows);
		await harness.InitializeIntegrationsAsync();

		var focus = await harness.Actions.ExecuteAsync(
			"focus-window",
			new Dictionary<string, object?> { ["window"] = "code" });
		var minimize = await harness.Actions.ExecuteAsync(
			"minimize-window",
			new Dictionary<string, object?>());
		var unknown = await harness.Actions.ExecuteAsync(
			"focus-window",
			new Dictionary<string, object?> { ["window"] = "no-such-window" });

		Assert.That(focus.Succeeded, Is.True);
		Assert.That(minimize.Succeeded, Is.True);
		Assert.That(unknown.Succeeded, Is.False);
		Assert.That(windows.Focused, Is.EqualTo("Visual Studio Code"));
		Assert.That(windows.Minimized, Does.Contain("Visual Studio Code"));
	}

	[Test]
	public async Task Desktop_switch_actions_succeed()
	{
		await using var harness = CreateHarness(new FakeMonitorService(), new FakeWindowService());
		await harness.InitializeIntegrationsAsync();

		var next = await harness.Actions.ExecuteAsync("next-desktop", new Dictionary<string, object?>());
		var previous = await harness.Actions.ExecuteAsync("previous-desktop", new Dictionary<string, object?>());

		Assert.That(next.Succeeded, Is.True);
		Assert.That(previous.Succeeded, Is.True);
	}

	[Test]
	public async Task Variables_expose_monitors_and_focus()
	{
		var integration = new PluginIntegration(new FakeMonitorService(), new FakeWindowService(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("monitor-count")).Value, Is.EqualTo(2.0));
		Assert.That((await integration.ReadAsync("primary-brightness")).Value, Is.EqualTo(80.0));
		Assert.That((await integration.ReadAsync("focused-window-title")).Value, Is.EqualTo("Visual Studio Code"));
		Assert.That((await integration.ReadAsync("focused-window-process")).Value, Is.EqualTo("Code"));

		var written = await integration.SetValueAsync("primary-brightness", 60.0);

		Assert.That(written.Status, Is.EqualTo(VariableWriteStatus.Applied));
		await integration.ShutdownAsync();
	}

	[Test]
	public void Action_ids_are_unique_across_the_plugin()
	{
		var integration = new PluginIntegration(new FakeMonitorService(), new FakeWindowService(), TestLogger());

		var duplicates = integration.Actions
			.GroupBy(a => a.Id)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();

		Assert.That(duplicates, Is.Empty);
		Assert.That(integration.Actions.Count, Is.EqualTo(11));
	}

	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.misu.screen-control"));
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

	private sealed class FakeMonitorService : IMonitorService
	{
		public List<int> Levels { get; } = [80, 60];

		public List<int> Inputs { get; } = [0x11, 0x11];

		public IReadOnlyList<MonitorInfo> GetMonitors() => Levels
			.Select((level, index) => new MonitorInfo(index + 1, $"Display {index + 1}", index == 0, level, true))
			.ToList();

		public bool TrySetBrightness(int index, int percent)
		{
			if (index < 1 || index > Levels.Count)
			{
				return false;
			}

			Levels[index - 1] = Math.Clamp(percent, 0, 100);
			return true;
		}

		public bool TrySetInput(int index, int vcpValue)
		{
			if (index < 1 || index > Inputs.Count)
			{
				return false;
			}

			Inputs[index - 1] = vcpValue;
			return true;
		}

		public int? TryGetInput(int index) =>
			index >= 1 && index <= Inputs.Count ? Inputs[index - 1] : null;
	}

	private sealed class FakeWindowService : IWindowService
	{
		public string? Focused { get; private set; } = "Visual Studio Code";

		public List<string> Minimized { get; } = [];

		public WindowInfo? GetForeground() =>
			Focused is null ? null : new WindowInfo(new IntPtr(1), Focused, "Code", false);

		public WindowInfo? Find(string? filter)
		{
			if (string.IsNullOrWhiteSpace(filter))
			{
				return GetForeground();
			}

			return "Visual Studio Code".Contains(filter, StringComparison.OrdinalIgnoreCase)
				? GetForeground()
				: null;
		}

		public IReadOnlyList<WindowInfo> GetWindows() =>
			GetForeground() is { } foreground ? [foreground] : [];

		public bool Focus(IntPtr handle)
		{
			Focused = "Visual Studio Code";
			return true;
		}

		public bool Minimize(IntPtr handle)
		{
			Minimized.Add("Visual Studio Code");
			return true;
		}

		public bool Maximize(IntPtr handle) => true;

		public bool Restore(IntPtr handle) => true;

		public bool Close(IntPtr handle) => true;

		public bool NextDesktop() => true;

		public bool PreviousDesktop() => true;
	}
}

using System.Text.Json;
using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ScreenControl.Actions;
using ScreenControl.Monitors;
using ScreenControl.Widgets;
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
	public async Task Monitor_power_reports_states()
	{
		var monitors = new FakeMonitorService();
		var action = new SetMonitorPowerAction(monitors);

		var on = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["monitor"] = 1.0 },
			TestContext.CurrentContext.CancellationToken);

		monitors.Powers[0] = MonitorPowerModes.Off;
		var off = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["monitor"] = 1.0 },
			TestContext.CurrentContext.CancellationToken);

		var missing = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["monitor"] = 9.0 },
			TestContext.CurrentContext.CancellationToken);

		Assert.That(on, Is.Not.Null);
		Assert.That(on!.States.Select(s => s.Id), Is.EqualTo(["on", "standby", "off"]));
		Assert.That(on.ActiveStateId, Is.EqualTo("on"));
		Assert.That(off!.ActiveStateId, Is.EqualTo("off"));
		Assert.That(missing, Is.Null);
	}

	[Test]
	public async Task Monitor_input_reports_states()
	{
		var monitors = new FakeMonitorService();
		var action = new SetMonitorInputAction(monitors);

		var hdmi = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["monitor"] = 1.0 },
			TestContext.CurrentContext.CancellationToken);

		monitors.Inputs[0] = 0x0F;
		var dp = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["monitor"] = 1.0 },
			TestContext.CurrentContext.CancellationToken);

		var missing = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["monitor"] = 9.0 },
			TestContext.CurrentContext.CancellationToken);

		Assert.That(hdmi, Is.Not.Null);
		Assert.That(hdmi!.States.Select(s => s.Id), Is.EqualTo(["hdmi1", "hdmi2", "dp1", "dp2", "dvi"]));
		Assert.That(hdmi.ActiveStateId, Is.EqualTo("hdmi1"));
		Assert.That(dp!.ActiveStateId, Is.EqualTo("dp1"));
		Assert.That(missing, Is.Null);
	}

	[Test]
	public async Task Topmost_reports_states()
	{
		var windows = new FakeWindowService();
		var action = new ToggleAlwaysOnTopAction(windows);

		var off = await action.GetActionStateAsync(
			new Dictionary<string, object?>(),
			TestContext.CurrentContext.CancellationToken);

		windows.SetTopmost(new IntPtr(1), true);
		var on = await action.GetActionStateAsync(
			new Dictionary<string, object?>(),
			TestContext.CurrentContext.CancellationToken);

		var missing = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["window"] = "no such window" },
			TestContext.CurrentContext.CancellationToken);

		Assert.That(off, Is.Not.Null);
		Assert.That(off!.States.Select(s => s.Id), Is.EqualTo(["on", "off"]));
		Assert.That(off.ActiveStateId, Is.EqualTo("off"));
		Assert.That(on!.ActiveStateId, Is.EqualTo("on"));
		Assert.That(missing, Is.Null);
	}

	[Test]
	public void Brightness_previews_build()
	{
		Assert.DoesNotThrow(() => BrightnessPreviews.WithLevel());
		Assert.DoesNotThrow(() => BrightnessPreviews.NoMonitor());
	}

	[Test]
	public void Brightness_trees_fit_a_three_by_three_tile_without_squeezing_text()
	{
		foreach (var preview in new Func<UiElement>[]
		{
			BrightnessPreviews.WithLevel,
			BrightnessPreviews.NoMonitor,
		})
		{
			var surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>(),
			};
			var view = new UiView(surface, preview());
			var json = JsonSerializer.Serialize(view.Tree);
			var height = WidgetFitEstimator.MeasureRootHeight(json);
			Assert.That(
				height,
				Is.LessThanOrEqualTo(WidgetFitEstimator.BudgetUnits),
				$"Tree is {height:F1} ref units tall on a 3x3 tile with a {WidgetFitEstimator.BudgetUnits} budget, so the reader squeezes rows and clips glyph bottoms. Slim sizes, gaps or rows until it fits.");

			// The widget lives on 1x1 tiles too: same tree, smaller basis.
			var small = WidgetFitEstimator.MeasureRootHeight(json, 120, 120 - 2 * 0.06 * 120 - 12);
			Assert.That(
				small,
				Is.LessThanOrEqualTo(120 - 2 * 0.06 * 120 - 12),
				$"Tree is {small:F1} ref units tall on a 1x1 tile. Slim sizes, gaps or rows until it fits.");
		}
	}

	[Test]
	public async Task Brightness_slider_applies_to_primary_monitor()
	{
		var monitors = new FakeMonitorService();
		var widget = new BrightnessWidget(monitors, TestLogger());
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>(),
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);
		Assert.That(session, Is.Not.Null);
		try
		{
			var slider = SliderId(JsonSerializer.Serialize(session!.BuildTree()));

			session!.Dispatch(new UiEvent
			{
				NodeId = slider,
				Name = "adjust",
				Data = JsonDocument.Parse("0.5").RootElement.Clone(),
			});
			Assert.That(JsonSerializer.Serialize(session!.BuildTree()), Does.Contain("50"));

			session!.Dispatch(new UiEvent
			{
				NodeId = slider,
				Name = "change",
				Data = JsonDocument.Parse("0.5").RootElement.Clone(),
			});
			Assert.That(monitors.Levels[0], Is.EqualTo(50));
		}
		finally
		{
			if (session is IAsyncDisposable asyncDisposable)
			{
				await asyncDisposable.DisposeAsync();
			}
		}
	}

	[Test]
	public async Task Brightness_preset_applies_to_configured_monitor()
	{
		var monitors = new FakeMonitorService();
		var widget = new BrightnessWidget(monitors, TestLogger());
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse("""{"monitor":2,"showPresets":true}""").RootElement.Clone(),
				},
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);
		Assert.That(session, Is.Not.Null);
		try
		{
			// The name travels as a localization reference resolved reader-side.
			var tree = JsonSerializer.Serialize(session!.BuildTree());
			Assert.That(tree, Does.Contain("Widget.Brightness.Display"));
			Assert.That(tree, Does.Contain("\"n\":2"));
			var preset = FindNodeId(tree, "ui.button", "preset-50");

			session!.Dispatch(new UiEvent { NodeId = preset, Name = "press" });

			Assert.That(monitors.Levels[1], Is.EqualTo(50));
			Assert.That(monitors.Levels[0], Is.EqualTo(80));
		}
		finally
		{
			if (session is IAsyncDisposable asyncDisposable)
			{
				await asyncDisposable.DisposeAsync();
			}
		}
	}

	[Test]
	public void Brightness_options_default_and_clamp()
	{
		var fallback = BrightnessOptions.FromData(default);

		Assert.That(fallback, Is.EqualTo(BrightnessOptions.Default));

		var custom = BrightnessOptions.FromData(JsonDocument.Parse("""{"monitor":2,"showPresets":false}""").RootElement);

		Assert.That(custom.Monitor, Is.EqualTo(2));
		Assert.That(custom.ShowPresets, Is.False);

		var clamped = BrightnessOptions.FromData(JsonDocument.Parse("""{"monitor":99}""").RootElement);

		Assert.That(clamped.Monitor, Is.EqualTo(9));
	}

	[Test]
	public void Brightness_options_read_string_monitor_from_choice_inputs()
	{
		var choice = BrightnessOptions.FromData(JsonDocument.Parse("""{"monitor":"2","showPresets":true}""").RootElement);

		Assert.That(choice.Monitor, Is.EqualTo(2));
		Assert.That(choice.ShowPresets, Is.True);

		var number = BrightnessOptions.FromData(JsonDocument.Parse("""{"monitor":2}""").RootElement);

		Assert.That(number.Monitor, Is.EqualTo(2));

		var garbage = BrightnessOptions.FromData(JsonDocument.Parse("""{"monitor":"hdmi"}""").RootElement);

		Assert.That(garbage.Monitor, Is.EqualTo(BrightnessOptions.Default.Monitor));
	}

	private static string SliderId(string treeJson) => FindNodeId(treeJson, "ui.slider", string.Empty);

	private static string FindNodeId(string treeJson, string type, string idSuffix)
	{
		using var document = JsonDocument.Parse(treeJson);
		var queue = new Queue<JsonElement>();
		queue.Enqueue(document.RootElement.GetProperty("Root"));
		while (queue.Count > 0)
		{
			var node = queue.Dequeue();
			if (node.GetProperty("Type").GetString() == type
				&& node.GetProperty("Id").GetString()!.EndsWith(idSuffix, StringComparison.Ordinal))
			{
				return node.GetProperty("Id").GetString()!;
			}

			foreach (var child in node.GetProperty("Children").EnumerateArray())
			{
				queue.Enqueue(child);
			}
		}

		throw new InvalidOperationException($"No {type} node ending in '{idSuffix}' in the tree.");
	}

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
	public async Task Blank_brightness_falls_back_to_default()
	{
		var monitors = new FakeMonitorService();
		await using var harness = CreateHarness(monitors, new FakeWindowService());
		await harness.InitializeIntegrationsAsync();

		var set = await harness.Actions.ExecuteAsync(
			"set-monitor-brightness",
			new Dictionary<string, object?> { ["monitor"] = 1.0, ["brightness"] = "" });

		Assert.That(set.Succeeded, Is.True);
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
	public async Task Monitor_power_set_reports_honestly()
	{
		var monitors = new FakeMonitorService();
		await using var harness = CreateHarness(monitors, new FakeWindowService());
		await harness.InitializeIntegrationsAsync();

		var standby = await harness.Actions.ExecuteAsync(
			"set-monitor-power",
			new Dictionary<string, object?> { ["monitor"] = 1.0, ["mode"] = "standby" });
		var off = await harness.Actions.ExecuteAsync(
			"set-monitor-power",
			new Dictionary<string, object?> { ["monitor"] = 2.0, ["mode"] = "off" });
		var bad = await harness.Actions.ExecuteAsync(
			"set-monitor-power",
			new Dictionary<string, object?> { ["monitor"] = 1.0, ["mode"] = "hibernate" });
		var missing = await harness.Actions.ExecuteAsync(
			"set-monitor-power",
			new Dictionary<string, object?> { ["monitor"] = 9.0, ["mode"] = "on" });

		Assert.That(standby.Succeeded, Is.True);
		Assert.That(off.Succeeded, Is.True);
		Assert.That(bad.Succeeded, Is.False);
		Assert.That(missing.Succeeded, Is.False);
		Assert.That(monitors.Powers[0], Is.EqualTo(MonitorPowerModes.Standby));
		Assert.That(monitors.Powers[1], Is.EqualTo(MonitorPowerModes.Off));
	}

	[Test]
	public async Task Topmost_toggle_and_snap_drive_the_fake_desktop()
	{
		var windows = new FakeWindowService();
		await using var harness = CreateHarness(new FakeMonitorService(), windows);
		await harness.InitializeIntegrationsAsync();

		var pin = await harness.Actions.ExecuteAsync(
			"toggle-always-on-top",
			new Dictionary<string, object?> { ["window"] = "code" });
		var snap = await harness.Actions.ExecuteAsync(
			"snap-window",
			new Dictionary<string, object?> { ["window"] = "code", ["side"] = "right" });
		var badSide = await harness.Actions.ExecuteAsync(
			"snap-window",
			new Dictionary<string, object?> { ["side"] = "up" });
		var unknown = await harness.Actions.ExecuteAsync(
			"snap-window",
			new Dictionary<string, object?> { ["window"] = "no-such-window", ["side"] = "left" });

		Assert.That(pin.Succeeded, Is.True);
		Assert.That(snap.Succeeded, Is.True);
		Assert.That(badSide.Succeeded, Is.False);
		Assert.That(unknown.Succeeded, Is.False);
		Assert.That(windows.Topmost, Is.True);
		Assert.That(windows.Snapped, Is.EqualTo("right"));

		var unpin = await harness.Actions.ExecuteAsync(
			"toggle-always-on-top",
			new Dictionary<string, object?> { ["window"] = "code" });

		Assert.That(unpin.Succeeded, Is.True);
		Assert.That(windows.Topmost, Is.False);
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
		var monitors = new FakeMonitorService();
		var windows = new FakeWindowService();
		var integration = new PluginIntegration(monitors, windows, TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("monitor-count")).Value, Is.EqualTo(2.0));
		Assert.That((await integration.ReadAsync("primary-brightness")).Value, Is.EqualTo(80.0));
		Assert.That((await integration.ReadAsync("primary-input")).Value, Is.EqualTo("hdmi1"));
		Assert.That((await integration.ReadAsync("focused-window-title")).Value, Is.EqualTo("Visual Studio Code"));
		Assert.That((await integration.ReadAsync("focused-window-process")).Value, Is.EqualTo("Code"));
		Assert.That((await integration.ReadAsync("focused-window-topmost")).Value, Is.EqualTo(false));

		var written = await integration.SetValueAsync("primary-brightness", 60.0);
		var pinned = await integration.SetValueAsync("focused-window-topmost", true);
		var badPin = await integration.SetValueAsync("focused-window-topmost", "maybe");

		Assert.That(written.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(pinned.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(badPin.Status, Is.EqualTo(VariableWriteStatus.InvalidValue));
		Assert.That(windows.Topmost, Is.True);
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Monitor_brightness_catalog_round_trips()
	{
		var monitors = new FakeMonitorService();
		var integration = new PluginIntegration(monitors, new FakeWindowService(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var page = await integration.DiscoverAsync(new VariableCatalogQuery());
		var ids = page.Items.Select(i => i.Id).ToList();
		var resolved = await integration.ResolveAsync("monitor-2-brightness");
		var missing = await integration.ResolveAsync("monitor-9-brightness");
		var read = await integration.ReadAsync("monitor-2-brightness");
		var write = await integration.SetValueAsync("monitor-2-brightness", 30.0);
		var badWrite = await integration.SetValueAsync("monitor-2-brightness", 140.0);
		var missingWrite = await integration.SetValueAsync("monitor-9-brightness", 30.0);

		Assert.That(ids.Count, Is.EqualTo(2));
		Assert.That(ids[0], Is.EqualTo("monitor-1-brightness"));
		Assert.That(ids[1], Is.EqualTo("monitor-2-brightness"));
		Assert.That(resolved, Is.Not.Null);
		Assert.That(missing, Is.Null);
		Assert.That(read.Value, Is.EqualTo(60.0));
		Assert.That(write.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(badWrite.Status, Is.EqualTo(VariableWriteStatus.InvalidValue));
		Assert.That(missingWrite.Status, Is.EqualTo(VariableWriteStatus.Unavailable));
		Assert.That(monitors.Levels[1], Is.EqualTo(30));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Monitor_discovery_honors_search()
	{
		var integration = new PluginIntegration(new FakeMonitorService(), new FakeWindowService(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var page = await integration.DiscoverAsync(new VariableCatalogQuery { Search = "Display 2" });

		Assert.That(page.Items.Count, Is.EqualTo(1));
		Assert.That(page.Items[0].Id, Is.EqualTo("monitor-2-brightness"));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Unknown_catalog_ids_stay_unavailable()
	{
		var integration = new PluginIntegration(new FakeMonitorService(), new FakeWindowService(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var read = await integration.ReadAsync("monitor-9-brightness");
		var notCatalog = await integration.ReadAsync("monitor-counts");

		Assert.That(read, Is.EqualTo(VariableReading.Unavailable));
		Assert.That(notCatalog, Is.EqualTo(VariableReading.Unavailable));
		await integration.ShutdownAsync();
	}

	[Test]
	public void Monitor_catalog_watcher_reports_membership_changes()
	{
		var watcher = new MonitorCatalogWatcher();
		var pair = new List<MonitorInfo>
		{
			new(1, "\\\\.\\DISPLAY1", true, 80, true, false),
			new(2, "\\\\.\\DISPLAY2", false, 60, true, true),
		};

		Assert.That(watcher.CheckForChanges(pair), Is.False, "first check sets the baseline");
		Assert.That(watcher.CheckForChanges(pair), Is.False, "stable set stays silent");

		var solo = new List<MonitorInfo> { new(1, "\\\\.\\DISPLAY1", true, 80, true, false) };
		Assert.That(watcher.CheckForChanges(solo), Is.True, "unplug reports");
		Assert.That(watcher.CheckForChanges(solo), Is.False, "second sighting is the new baseline");
		Assert.That(watcher.CheckForChanges(pair), Is.True, "replug reports");

		watcher.Reset();
		Assert.That(watcher.CheckForChanges(pair), Is.False, "reset re-baselines silently");
	}

	[Test]
	public async Task Catalog_watch_lifecycle_starts_and_stops_cleanly()
	{
		var integration = new PluginIntegration(new FakeMonitorService(), new FakeWindowService(), TestLogger());

		await integration.InitializeAsync(new FakeIntegrationContext());
		await integration.ShutdownAsync();
		await integration.InitializeAsync(new FakeIntegrationContext());
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
		Assert.That(integration.Actions.Count, Is.EqualTo(14));
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

		public List<int> Powers { get; } = [MonitorPowerModes.On, MonitorPowerModes.On];

		public IReadOnlyList<MonitorInfo> GetMonitors() => Levels
			.Select((level, index) => new MonitorInfo(index + 1, $"Display {index + 1}", index == 0, level, true, false))
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

		public bool TrySetPower(int index, int dpmValue)
		{
			if (index < 1 || index > Powers.Count)
			{
				return false;
			}

			Powers[index - 1] = dpmValue;
			return true;
		}

		public int? TryGetPower(int index) =>
			index >= 1 && index <= Powers.Count ? Powers[index - 1] : null;

		public IReadOnlyList<int> GetSupportedInputs(int index) =>
			index >= 1 && index <= Inputs.Count ? [0x11, 0x12, 0x0F, 0x10, 0x03] : [];

		public void HideOverlays()
		{
		}
	}

	private sealed class FakeWindowService : IWindowService
	{
		public string? Focused { get; private set; } = "Visual Studio Code";

		public List<string> Minimized { get; } = [];

		public bool Topmost { get; private set; }

		public string? Snapped { get; private set; }

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

		public bool IsTopmost(IntPtr handle) => Topmost;

		public bool SetTopmost(IntPtr handle, bool topmost)
		{
			Topmost = topmost;
			return true;
		}

		public bool Snap(IntPtr handle, bool left)
		{
			Snapped = left ? "left" : "right";
			return true;
		}
	}
}

using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Serilog;
using Timers.Actions;
using Timers.Timing;

namespace Timers.Tests;

[TestFixture]
public sealed class PluginIntegrationTests
{
	private static PluginTestHarness CreateHarness(TimerService timers) =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddSingleton(timers);
			builder.Services.AddSingleton(new PomodoroService());
			builder.UseLocalization(Strings.LocalizationCatalog);
			builder.RegisterIntegration<PluginIntegration>();
		});

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness(new TimerService());

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public async Task Countdown_start_pause_resume_cancel()
	{
		var timers = new TimerService();
		await using var harness = CreateHarness(timers);
		await harness.InitializeIntegrationsAsync();

		var start = await harness.Actions.ExecuteAsync(
			"start-countdown",
			new Dictionary<string, object?> { ["hours"] = 0.0, ["minutes"] = 1.0, ["seconds"] = 30.0, ["label"] = "tea" });
		var pause = await harness.Actions.ExecuteAsync("pause-countdown", new Dictionary<string, object?>());
		var resume = await harness.Actions.ExecuteAsync("resume-countdown", new Dictionary<string, object?>());
		var cancel = await harness.Actions.ExecuteAsync("cancel-countdown", new Dictionary<string, object?>());
		var bad = await harness.Actions.ExecuteAsync(
			"start-countdown",
			new Dictionary<string, object?> { ["minutes"] = 70.0 });

		Assert.That(start.Succeeded, Is.True);
		Assert.That(pause.Succeeded, Is.True);
		Assert.That(resume.Succeeded, Is.True);
		Assert.That(cancel.Succeeded, Is.True);
		Assert.That(bad.Succeeded, Is.False);
		Assert.That(timers.CountdownRunning, Is.False);
		Assert.That(timers.CountdownRemaining, Is.EqualTo(TimeSpan.Zero));
	}

	[Test]
	public async Task Countdown_toggle_and_adjust()
	{
		var timers = new TimerService();
		await using var harness = CreateHarness(timers);
		await harness.InitializeIntegrationsAsync();

		var start = await harness.Actions.ExecuteAsync(
			"start-countdown",
			new Dictionary<string, object?> { ["minutes"] = 5.0 });
		var pauseToggle = await harness.Actions.ExecuteAsync("toggle-countdown", new Dictionary<string, object?>());
		var resumeToggle = await harness.Actions.ExecuteAsync("toggle-countdown", new Dictionary<string, object?>());
		var extend = await harness.Actions.ExecuteAsync(
			"adjust-countdown",
			new Dictionary<string, object?> { ["delta"] = 60.0 });
		var bad = await harness.Actions.ExecuteAsync(
			"adjust-countdown",
			new Dictionary<string, object?> { ["delta"] = 100000.0 });

		Assert.That(start.Succeeded, Is.True);
		Assert.That(pauseToggle.Succeeded, Is.True);
		Assert.That(resumeToggle.Succeeded, Is.True);
		Assert.That(extend.Succeeded, Is.True);
		Assert.That(bad.Succeeded, Is.False);
		Assert.That(timers.CountdownRunning, Is.True);
		Assert.That(timers.CountdownRemaining, Is.GreaterThan(TimeSpan.FromMinutes(5)));

		timers.CancelCountdown();
	}

	[Test]
	public async Task Countdown_finish_fires_the_event()
	{
		var timers = new TimerService();
		var context = new FakeIntegrationContext();
		var integration = new PluginIntegration(timers, new PomodoroService(), TestLogger());
		await integration.InitializeAsync(context);

		timers.StartCountdown(TimeSpan.FromMilliseconds(100), "quick");

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		while (DateTimeOffset.UtcNow < deadline
			&& !context.Events.Published.Any(e => e.EventId == "countdown-finished"))
		{
			await Task.Delay(50, TestContext.CurrentContext.CancellationToken);
		}

		var fired = context.Events.Published.FirstOrDefault(e => e.EventId == "countdown-finished");
		Assert.That(fired, Is.Not.Null);
		Assert.That(timers.CountdownRunning, Is.False);
		await integration.ShutdownAsync();
		timers.Dispose();
	}

	[Test]
	public async Task Blank_countdown_fields_fall_back_to_defaults()
	{
		var timers = new TimerService();
		await using var harness = CreateHarness(timers);
		await harness.InitializeIntegrationsAsync();

		var start = await harness.Actions.ExecuteAsync(
			"start-countdown",
			new Dictionary<string, object?> { ["minutes"] = "", ["seconds"] = 10.0 });
		var adjust = await harness.Actions.ExecuteAsync(
			"adjust-countdown",
			new Dictionary<string, object?> { ["delta"] = "  " });
		var garbage = await harness.Actions.ExecuteAsync(
			"start-countdown",
			new Dictionary<string, object?> { ["seconds"] = "ten" });

		Assert.That(start.Succeeded, Is.True);
		Assert.That(adjust.Succeeded, Is.True);
		Assert.That(garbage.Succeeded, Is.False);
		Assert.That(timers.CountdownRunning, Is.True);
		Assert.That(timers.CountdownRemaining, Is.GreaterThan(TimeSpan.FromMinutes(5)));

		timers.CancelCountdown();
	}

	[Test]
	public async Task Adjusting_past_zero_finishes_the_countdown()
	{
		var timers = new TimerService();
		var context = new FakeIntegrationContext();
		var integration = new PluginIntegration(timers, new PomodoroService(), TestLogger());
		await integration.InitializeAsync(context);

		timers.StartCountdown(TimeSpan.FromMinutes(5), "tea");
		timers.AdjustCountdown(TimeSpan.FromMinutes(-10));

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		while (DateTimeOffset.UtcNow < deadline
			&& !context.Events.Published.Any(e => e.EventId == "countdown-finished"))
		{
			await Task.Delay(50, TestContext.CurrentContext.CancellationToken);
		}

		var fired = context.Events.Published.FirstOrDefault(e => e.EventId == "countdown-finished");
		Assert.That(fired, Is.Not.Null);
		Assert.That(timers.CountdownRunning, Is.False);
		await integration.ShutdownAsync();
		timers.Dispose();
	}

	[Test]
	public async Task Stopwatch_start_stop_reset()
	{
		var timers = new TimerService();
		await using var harness = CreateHarness(timers);
		await harness.InitializeIntegrationsAsync();

		var start = await harness.Actions.ExecuteAsync("start-stopwatch", new Dictionary<string, object?>());
		await Task.Delay(150, TestContext.CurrentContext.CancellationToken);
		var toggleStop = await harness.Actions.ExecuteAsync("toggle-stopwatch", new Dictionary<string, object?>());
		var elapsed = timers.StopwatchElapsed;
		var toggleStart = await harness.Actions.ExecuteAsync("toggle-stopwatch", new Dictionary<string, object?>());
		var stop = await harness.Actions.ExecuteAsync("stop-stopwatch", new Dictionary<string, object?>());
		var reset = await harness.Actions.ExecuteAsync("reset-stopwatch", new Dictionary<string, object?>());

		Assert.That(start.Succeeded, Is.True);
		Assert.That(toggleStop.Succeeded, Is.True);
		Assert.That(toggleStart.Succeeded, Is.True);
		Assert.That(stop.Succeeded, Is.True);
		Assert.That(reset.Succeeded, Is.True);
		Assert.That(elapsed, Is.GreaterThan(TimeSpan.Zero));
		Assert.That(timers.StopwatchElapsed, Is.EqualTo(TimeSpan.Zero));
		Assert.That(timers.StopwatchRunning, Is.False);
	}

	[Test]
	public async Task Variables_expose_timer_state()
	{
		var timers = new TimerService();
		var integration = new PluginIntegration(timers, new PomodoroService(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		timers.StartCountdown(TimeSpan.FromMinutes(5), "tea");
		timers.StartStopwatch();

		Assert.That((await integration.ReadAsync("countdown-running")).Value, Is.EqualTo(true));
		Assert.That((await integration.ReadAsync("countdown-label")).Value, Is.EqualTo("tea"));
		Assert.That((await integration.ReadAsync("stopwatch-running")).Value, Is.EqualTo(true));
		Assert.That((await integration.ReadAsync("countdown-remaining-seconds")).Value, Is.GreaterThan(0.0));
		Assert.That((await integration.ReadAsync("countdown-progress-percent")).Value, Is.InRange(0.0, 100.0));

		var missing = await integration.ReadAsync("no-such-variable");

		Assert.That(missing, Is.EqualTo(VariableReading.Unavailable));
		await integration.ShutdownAsync();
		timers.Dispose();
	}

	[Test]
	public async Task Pomodoro_actions_drive_a_cycle()
	{
		var timers = new TimerService();
		await using var harness = CreateHarness(timers);
		await harness.InitializeIntegrationsAsync();

		var start = await harness.Actions.ExecuteAsync(
			"start-pomodoro",
			new Dictionary<string, object?> { ["work-minutes"] = 25.0, ["rounds"] = 4.0 });
		var toggle = await harness.Actions.ExecuteAsync("toggle-pomodoro", new Dictionary<string, object?>());
		var resume = await harness.Actions.ExecuteAsync("toggle-pomodoro", new Dictionary<string, object?>());
		var skip = await harness.Actions.ExecuteAsync("skip-pomodoro-phase", new Dictionary<string, object?>());
		var stop = await harness.Actions.ExecuteAsync("stop-pomodoro", new Dictionary<string, object?>());
		var bad = await harness.Actions.ExecuteAsync(
			"start-pomodoro",
			new Dictionary<string, object?> { ["work-minutes"] = 500.0 });

		Assert.That(start.Succeeded, Is.True);
		Assert.That(toggle.Succeeded, Is.True);
		Assert.That(resume.Succeeded, Is.True);
		Assert.That(skip.Succeeded, Is.True);
		Assert.That(stop.Succeeded, Is.True);
		Assert.That(bad.Succeeded, Is.False);
	}

	[Test]
	public async Task Pomodoro_variables_expose_phase_state()
	{
		var timers = new TimerService();
		var pomodoro = new PomodoroService();
		var integration = new PluginIntegration(timers, pomodoro, TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		pomodoro.Start(new PomodoroSettings(25, 5, 15, 4, true));

		Assert.That((await integration.ReadAsync("pomodoro-phase")).Value, Is.EqualTo("focus"));
		Assert.That((await integration.ReadAsync("pomodoro-round")).Value, Is.EqualTo(1.0));
		Assert.That((await integration.ReadAsync("pomodoro-running")).Value, Is.EqualTo(true));
		Assert.That((await integration.ReadAsync("pomodoro-label")).Value, Is.EqualTo("Focus 1"));
		Assert.That((await integration.ReadAsync("pomodoro-remaining-seconds")).Value, Is.GreaterThan(0.0));

		pomodoro.Stop();
		Assert.That((await integration.ReadAsync("pomodoro-phase")).Value, Is.EqualTo("idle"));
		await integration.ShutdownAsync();
		pomodoro.Dispose();
	}

	[Test]
	public async Task Pomodoro_phase_change_fires_the_event()
	{
		var timers = new TimerService();
		var pomodoro = new PomodoroService();
		var context = new FakeIntegrationContext();
		var integration = new PluginIntegration(timers, pomodoro, TestLogger());
		await integration.InitializeAsync(context);

		pomodoro.Start(new PomodoroSettings(25, 5, 15, 4, true));
		pomodoro.Skip();

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		while (DateTimeOffset.UtcNow < deadline
			&& !context.Events.Published.Any(e => e.EventId == "pomodoro-phase-changed"))
		{
			await Task.Delay(50, TestContext.CurrentContext.CancellationToken);
		}

		var fired = context.Events.Published.FirstOrDefault(e => e.EventId == "pomodoro-phase-changed");
		Assert.That(fired, Is.Not.Null);
		await integration.ShutdownAsync();
		pomodoro.Dispose();
	}

	[Test]
	public void The_widget_registers_one_focus_timer_type()
	{
		var integration = new PluginIntegration(new TimerService(), new PomodoroService(), TestLogger());

		Assert.That(integration.GetWidgetTypes().Count, Is.EqualTo(1));
		Assert.That(integration.GetWidgetTypes()[0].Id, Is.EqualTo("focus-timer"));
		Assert.That(integration.Surfaces.Count, Is.EqualTo(3));
	}

	[Test]
	public void Action_ids_are_unique_across_the_plugin()
	{
		var integration = new PluginIntegration(new TimerService(), new PomodoroService(), TestLogger());

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
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.misu.timers"));
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
}


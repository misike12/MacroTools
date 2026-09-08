using System.Text.Json;
using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Surfaces;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Serilog;
using WindowsMediaControl.Actions;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;
using WindowsMediaControl.Widgets;

namespace WindowsMediaControl.Tests;

[TestFixture]
public sealed class PluginIntegrationTests
{
	private static PluginTestHarness CreateHarness(FakeMediaControlService fake) =>
		PluginTestHarness.Create(builder =>
		{
			builder.Services.AddSingleton<IMediaControlService>(fake);
			builder.Services.AddSingleton(new MediaSettingsProvider());
			builder.UseLocalization(Strings.LocalizationCatalog);
			builder.RegisterIntegration<PluginIntegration>();
		});

	private static Serilog.Core.Logger TestLogger() => new LoggerConfiguration().CreateLogger();

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness(new FakeMediaControlService());

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public async Task Play_succeeds_when_a_session_is_active()
	{
		var fake = new FakeMediaControlService();
		await using var harness = CreateHarness(fake);
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync(
			"play",
			new Dictionary<string, object?>());

		Assert.That(outcome.Succeeded, Is.True);
	}

	[Test]
	public async Task Toggle_reports_success_and_flips_playback()
	{
		var fake = new FakeMediaControlService();
		await using var harness = CreateHarness(fake);
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync(
			"toggle-play-pause",
			new Dictionary<string, object?>());

		Assert.That(outcome.Succeeded, Is.True);
	}

	[Test]
	public async Task Play_fails_when_no_session_exists()
	{
		var fake = new FakeMediaControlService { Snapshot = MediaSnapshot.Empty, TransportResult = false };
		var action = new PlayAction(fake, new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task Pause_is_a_noop_success_when_nothing_is_playing()
	{
		var fake = new FakeMediaControlService
		{
			Snapshot = new MediaSnapshot { HasSession = true, Status = PlaybackStatus.Paused },
		};
		var action = new PauseAction(fake, new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Calls, Does.Not.Contain("PauseAsync"));
	}

	[Test]
	public async Task Seek_forward_rejects_an_unreadable_value()
	{
		var action = new SeekForwardAction(new FakeMediaControlService(), new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["seconds"] = new object() },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task Set_volume_rejects_out_of_range_values()
	{
		var action = new SetVolumeAction(new FakeMediaControlService());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["volume"] = 150.0 },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task Seek_to_rejects_negative_positions()
	{
		var action = new SeekToAction(new FakeMediaControlService(), new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["position"] = -5.0 },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task Toggle_state_reports_playing_for_a_playing_session()
	{
		var fake = new FakeMediaControlService();
		var action = new TogglePlayPauseAction(fake, new MediaSettingsProvider());

		var snapshot = await action.GetActionStateAsync(
			new Dictionary<string, object?>(),
			TestContext.CurrentContext.CancellationToken);

		Assert.That(snapshot, Is.Not.Null);
		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("playing"));
	}

	[Test]
	public async Task Variables_expose_the_current_track()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var title = await integration.ReadAsync("title");
		var artist = await integration.ReadAsync("artist");
		var status = await integration.ReadAsync("playback-status");
		var isPlaying = await integration.ReadAsync("is-playing");
		var hasMedia = await integration.ReadAsync("has-media");
		var isMuted = await integration.ReadAsync("is-muted");
		var progress = await integration.ReadAsync("progress-percent");

		Assert.That(title.Value, Is.EqualTo("Nightcall"));
		Assert.That(artist.Value, Is.EqualTo("Kavinsky"));
		Assert.That(status.Value, Is.EqualTo("playing"));
		Assert.That(isPlaying.Value, Is.EqualTo(true));
		Assert.That(hasMedia.Value, Is.EqualTo(true));
		Assert.That(isMuted.Value, Is.EqualTo(false));
		Assert.That(progress.Value, Is.EqualTo(19.5).Within(0.5));
	}

	[Test]
	public async Task Variables_report_unavailable_without_a_session()
	{
		var fake = new FakeMediaControlService { Snapshot = MediaSnapshot.Empty };
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var title = await integration.ReadAsync("title");

		Assert.That(title, Is.EqualTo(VariableReading.Unavailable));
	}

	[Test]
	public async Task Volume_variable_write_changes_the_volume()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var result = await integration.SetValueAsync("volume-percent", 80.0);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(fake.Snapshot.VolumePercent, Is.EqualTo(80));
	}

	[Test]
	public async Task Volume_variable_write_rejects_out_of_range_values()
	{
		var integration = new PluginIntegration(new FakeMediaControlService(), new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var result = await integration.SetValueAsync("volume-percent", 150.0);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.InvalidValue));
	}

	[Test]
	public async Task Position_variable_write_seeks_the_track()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var result = await integration.SetValueAsync("position-seconds", 60.0);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(fake.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(60)));
	}

	[Test]
	public async Task Position_variable_write_rejects_negative_values()
	{
		var integration = new PluginIntegration(new FakeMediaControlService(), new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var result = await integration.SetValueAsync("position-seconds", -5.0);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.InvalidValue));
	}

	[Test]
	public async Task Progress_variable_write_seeks_to_a_fraction_of_the_track()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var result = await integration.SetValueAsync("progress-percent", 50.0);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(fake.Snapshot.Position.TotalSeconds, Is.EqualTo(107.5).Within(0.5));
	}

	[Test]
	public async Task Progress_variable_write_is_unavailable_without_a_duration()
	{
		var fake = new FakeMediaControlService { Snapshot = MediaSnapshot.Empty };
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var result = await integration.SetValueAsync("progress-percent", 50.0);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Unavailable));
	}

	[Test]
	public async Task Track_changed_event_is_published_when_the_track_changes()
	{
		var fake = new FakeMediaControlService();
		var context = new FakeIntegrationContext();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(context);

		fake.Snapshot = fake.Snapshot with { Title = "Something else" };

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		while (DateTimeOffset.UtcNow < deadline
			&& !context.Events.Published.Any(e => e.EventId == "track-changed"))
		{
			await Task.Delay(100);
		}

		Assert.That(context.Events.Published.Any(e => e.EventId == "track-changed"), Is.True);
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Music_player_state_carries_the_artwork_id()
	{
		var fake = new FakeMediaControlService
		{
			Snapshot = new FakeMediaControlService().Snapshot with { ArtworkId = "art-1" },
			ArtworkBytes = [0x89, 0x50, 0x4E, 0x47],
		};
		var player = new SystemMusicPlayer(fake);

		var state = await player.GetStateAsync(TestContext.CurrentContext.CancellationToken);
		var art = await player.GetArtworkAsync("art-1", TestContext.CurrentContext.CancellationToken);

		Assert.That(state.ArtworkId, Is.EqualTo("art-1"));
		Assert.That(art, Is.Not.Null);
		Assert.That(art!.MimeType, Is.EqualTo("image/png"));
	}

	[Test]
	public async Task Music_player_artwork_is_null_for_unknown_ids()
	{
		var player = new SystemMusicPlayer(new FakeMediaControlService());

		var art = await player.GetArtworkAsync("nope", TestContext.CurrentContext.CancellationToken);

		Assert.That(art, Is.Null);
	}

	[Test]
	public async Task Toggle_icon_reports_the_current_artwork_version()
	{
		var fake = new FakeMediaControlService
		{
			Snapshot = new FakeMediaControlService().Snapshot with { ArtworkId = "art-1" },
			ArtworkBytes = [0x89, 0x50, 0x4E, 0x47],
		};
		var action = new TogglePlayPauseAction(fake, new MediaSettingsProvider());

		var icon = await action.GetActionIconAsync(
			new Dictionary<string, object?>(),
			TestContext.CurrentContext.CancellationToken);
		var content = await action.GetActionIconContentAsync(
			new Dictionary<string, object?>(),
			"art-1",
			TestContext.CurrentContext.CancellationToken);
		var stale = await action.GetActionIconContentAsync(
			new Dictionary<string, object?>(),
			"old-version",
			TestContext.CurrentContext.CancellationToken);

		Assert.That(icon, Is.Not.Null);
		Assert.That(icon!.Version, Is.EqualTo("art-1"));
		Assert.That(content, Is.Not.Null);
		Assert.That(content!.MediaType, Is.EqualTo("image/png"));
		Assert.That(stale, Is.Null);
	}

	[Test]
	public async Task Toggle_icon_falls_back_without_a_session()
	{
		var fake = new FakeMediaControlService { Snapshot = MediaSnapshot.Empty };
		var action = new TogglePlayPauseAction(fake, new MediaSettingsProvider());

		var icon = await action.GetActionIconAsync(
			new Dictionary<string, object?>(),
			TestContext.CurrentContext.CancellationToken);

		Assert.That(icon, Is.Null);
	}

	[Test]
	public async Task Widget_registers_one_now_playing_type()
	{
		var widget = new NowPlayingWidget(new FakeMediaControlService(), TestLogger());

		await widget.InitializeAsync(new FakeWidgetTypeProviderContext(), TestContext.CurrentContext.CancellationToken);

		var types = widget.GetWidgetTypes();
		Assert.That(types.Count, Is.EqualTo(1));
		Assert.That(types[0].Id, Is.EqualTo("now-playing"));
	}

	[Test]
	public async Task Double_initialization_is_safe()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());

		await integration.InitializeAsync(new FakeIntegrationContext());
		await integration.InitializeAsync(new FakeIntegrationContext());
		await ((IWidgetTypeProvider)integration).InitializeAsync(
			new FakeWidgetTypeProviderContext(), TestContext.CurrentContext.CancellationToken);
		await ((IWidgetTypeProvider)integration).InitializeAsync(
			new FakeWidgetTypeProviderContext(), TestContext.CurrentContext.CancellationToken);

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Widget_declines_surfaces_for_unknown_types()
	{
		var widget = new NowPlayingWidget(new FakeMediaControlService(), TestLogger());
		await widget.InitializeAsync(new FakeWidgetTypeProviderContext(), TestContext.CurrentContext.CancellationToken);
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiWidgetSurfaceAttributes.WidgetType] = JsonDocument.Parse("\"other::type\"").RootElement.Clone(),
				},
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);

		Assert.That(session, Is.Null);
	}

	[Test]
	public async Task Widget_serves_a_tree_for_its_own_type()
	{
		var widget = new NowPlayingWidget(new FakeMediaControlService(), TestLogger());
		await widget.InitializeAsync(new FakeWidgetTypeProviderContext(), TestContext.CurrentContext.CancellationToken);
		var typeId = widget.GetWidgetTypes()[0].Id;
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiWidgetSurfaceAttributes.WidgetType] = JsonDocument.Parse($"\"{typeId}\"").RootElement.Clone(),
					[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse("""{"showAlbum":false}""").RootElement.Clone(),
				},
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);

		Assert.That(session, Is.Not.Null);
		Assert.That(session!.BuildTree(), Is.Not.Null);
		if (session is IAsyncDisposable asyncDisposable)
		{
			await asyncDisposable.DisposeAsync();
		}
	}

	[Test]
	public async Task Widget_serves_a_configuration_tree()
	{
		var widget = new NowPlayingWidget(new FakeMediaControlService(), TestLogger());
		var request = new UiSessionRequest
		{
			UiModelVersion = 4,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Config,
				SessionMode = UiSessionModes.Exclusive,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiConfigSurfaceAttributes.EntryPoint] = JsonDocument.Parse($"\"{UiConfigEntryPoints.WidgetConfig}\"").RootElement.Clone(),
					[UiConfigSurfaceAttributes.WidgetData] = JsonDocument.Parse("""{"showControls":false}""").RootElement.Clone(),
				},
			},
		};

		var session = await widget.CreateSessionAsync(request, TestContext.CurrentContext.CancellationToken);

		Assert.That(session, Is.Not.Null);
		Assert.That(session!.BuildTree(), Is.Not.Null);
		if (session is IAsyncDisposable asyncDisposable)
		{
			await asyncDisposable.DisposeAsync();
		}
	}

	[Test]
	public void Widget_options_default_missing_flags_to_visible()
	{
		var options = WidgetOptions.FromData(JsonDocument.Parse("""{"showAlbum":false}""").RootElement);

		Assert.That(options.ShowAlbum, Is.False);
		Assert.That(options.ShowProgress, Is.True);
		Assert.That(options.ShowControls, Is.True);
		Assert.That(options.Compact, Is.False);
	}

	[Test]
	public void Widget_options_read_compact_mode()
	{
		var options = WidgetOptions.FromData(JsonDocument.Parse("""{"compactMode":true}""").RootElement);

		Assert.That(options.Compact, Is.True);
	}

	[Test]
	public async Task Play_with_matching_app_succeeds()
	{
		var action = new PlayAction(new FakeMediaControlService(), new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "spotify" },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task Play_with_unknown_app_fails()
	{
		var action = new PlayAction(new FakeMediaControlService(), new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "no-such-app" },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public void Action_ids_are_unique_across_the_plugin()
	{
		var integration = new PluginIntegration(new FakeMediaControlService(), new MediaSettingsProvider(), TestLogger());

		var duplicates = integration.Actions
			.GroupBy(a => a.Id)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();

		Assert.That(duplicates, Is.Empty);
		Assert.That(integration.Actions.Count, Is.GreaterThanOrEqualTo(15));
	}
}

[TestFixture]
public sealed class ConfigFlowTests
{
	private static MediaConfigFlow CreateFlow() => new();

	private static Dictionary<string, object?> PlaybackInput(double seekSeconds = 15) => new()
	{
		["preferred-app"] = "Spotify",
		["seek-seconds"] = seekSeconds,
		["ff-seconds"] = 10.0,
	};

	private static Dictionary<string, object?> VolumeInput() => new()
	{
		["volume-step"] = 5.0,
		["max-volume"] = 90.0,
		["unmute-on-volume"] = true,
	};

	private static Dictionary<string, object?> UpdatesInput() => new()
	{
		["poll-seconds"] = 2.0,
		["state-poll-seconds"] = 2.0,
		["icon-poll-seconds"] = 30.0,
		["extrapolate"] = true,
	};

	private static Dictionary<string, object?> EventsInput() => new()
	{
		["events-track"] = true,
		["events-playback"] = true,
		["events-volume"] = true,
		["events-mute"] = true,
	};

	private static Dictionary<string, object?> AdvancedInput(bool reset = false) => new()
	{
		["button-artwork"] = true,
		["snapshot-timeout"] = 3.0,
		["control-timeout"] = 6.0,
		["artwork-cache"] = 8.0,
		["reset-defaults"] = reset,
	};

	[Test]
	public async Task Start_returns_the_playback_step()
	{
		var result = await CreateFlow().StartAsync(
			new FakeConfigFlowContext(), TestContext.CurrentContext.CancellationToken);

		Assert.That(result.NextStep, Is.Not.Null);
		Assert.That(result.NextStep!.StepId, Is.EqualTo("playback"));
	}

	[Test]
	public async Task Full_flow_completes_with_valid_inputs()
	{
		var flow = CreateFlow();
		var ct = TestContext.CurrentContext.CancellationToken;
		var context = new FakeConfigFlowContext();

		var first = await flow.SubmitAsync("playback", PlaybackInput(), context, ct);
		var second = await flow.SubmitAsync("volume", VolumeInput(), context, ct);
		var third = await flow.SubmitAsync("updates", UpdatesInput(), context, ct);
		var fourth = await flow.SubmitAsync("events", EventsInput(), context, ct);
		var done = await flow.SubmitAsync("advanced", AdvancedInput(), context, ct);

		Assert.That(first.NextStep!.StepId, Is.EqualTo("volume"));
		Assert.That(second.NextStep!.StepId, Is.EqualTo("updates"));
		Assert.That(third.NextStep!.StepId, Is.EqualTo("events"));
		Assert.That(fourth.NextStep!.StepId, Is.EqualTo("advanced"));
		Assert.That(done.Values, Is.Not.Null);
		Assert.That(done.Values!["seek-seconds"].Value, Is.EqualTo("15"));
		Assert.That(done.Values!["preferred-app"].Value, Is.EqualTo("Spotify"));
		Assert.That(done.Values!["max-volume"].Value, Is.EqualTo("90"));
	}

	[Test]
	public async Task Out_of_range_values_return_field_errors()
	{
		var result = await CreateFlow().SubmitAsync(
			"playback", PlaybackInput(seekSeconds: 500), new FakeConfigFlowContext(), TestContext.CurrentContext.CancellationToken);

		Assert.That(result.FieldErrors!.ContainsKey("seek-seconds"), Is.True);
	}

	[Test]
	public async Task Reset_restores_defaults()
	{
		var flow = CreateFlow();
		var ct = TestContext.CurrentContext.CancellationToken;
		var context = new FakeConfigFlowContext();

		await flow.SubmitAsync("playback", PlaybackInput(), context, ct);
		await flow.SubmitAsync("volume", VolumeInput(), context, ct);
		await flow.SubmitAsync("updates", UpdatesInput(), context, ct);
		await flow.SubmitAsync("events", EventsInput(), context, ct);
		var done = await flow.SubmitAsync("advanced", AdvancedInput(reset: true), context, ct);

		Assert.That(done.Values!["seek-seconds"].Value, Is.EqualTo("10"));
		Assert.That(done.Values!["preferred-app"].Value, Is.Empty);
	}

	[Test]
	public async Task Reader_uses_defaults_without_entries()
	{
		var settings = await MediaSettingsReader.ReadAsync(new FakeIntegrationConfig());

		Assert.That(settings, Is.EqualTo(MediaSettings.Default));
	}

	[Test]
	public async Task Reader_reads_seeded_values()
	{
		var config = new FakeIntegrationConfig();
		var entry = config.AddEntry("test");
		config.SeedString(entry, "preferred-app", "Spotify");
		config.SeedString(entry, "max-volume", "80");

		var settings = await MediaSettingsReader.ReadAsync(config);

		Assert.That(settings.PreferredApp, Is.EqualTo("Spotify"));
		Assert.That(settings.MaxVolumeLimit, Is.EqualTo(80));
		Assert.That(settings.DefaultSeekSeconds, Is.EqualTo(MediaSettings.Default.DefaultSeekSeconds));
	}

	[Test]
	public void ClampVolume_respects_the_configured_limit()
	{
		var settings = MediaSettings.Default with { MaxVolumeLimit = 80 };

		Assert.That(settings.ClampVolume(90), Is.EqualTo(80));
		Assert.That(settings.ClampVolume(50), Is.EqualTo(50));
	}

	[Test]
	public async Task Preferred_app_default_flows_into_actions()
	{
		var holder = new MediaSettingsProvider();
		holder.Update(MediaSettings.Default with { PreferredApp = "Spotify" });
		var fake = new FakeMediaControlService();
		var action = new PlayAction(fake, holder);

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Calls, Does.Contain("PlayAsync"));
	}

	private sealed class FakeConfigFlowContext : MacroDeck.Sdk.ConfigFlow.IConfigFlowContext
	{
		public MacroDeck.Sdk.ConfigFlow.IOAuthSession OAuth => throw new NotSupportedException();
	}
}

[TestFixture]
public sealed class LocalizationTests
{
	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.misu.windows-media"));
	}

	[Test]
	public void English_is_the_default_culture()
	{
		Assert.That(Strings.LocalizationCatalog.DefaultCulture, Is.EqualTo("en"));
		Assert.That(Strings.LocalizationCatalog.Cultures, Does.Contain("en"));
	}

	[Test]
	public void Every_media_action_has_a_name()
	{
		foreach (var key in new[]
		{
			"Actions.Play.Name", "Actions.Pause.Name", "Actions.TogglePlayPause.Name",
			"Actions.Stop.Name", "Actions.Next.Name", "Actions.Previous.Name",
			"Actions.SeekForward.Name", "Actions.SeekTo.Name",
			"Actions.VolumeUp.Name", "Actions.SetVolume.Name", "Actions.ToggleMute.Name",
		})
		{
			Assert.That(Strings.LocalizationCatalog.KeysOf("en"), Does.Contain(key), key);
		}
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


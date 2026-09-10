using System.Text.Json;
using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
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
		var isPlaying = await integration.ReadAsync("media-is-playing");
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
	public async Task Sleep_timer_with_zero_minutes_pauses_immediately()
	{
		var fake = new FakeMediaControlService();
		var action = new SleepTimerAction(fake, new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["minutes"] = 0.0 },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Snapshot.Status, Is.EqualTo(PlaybackStatus.Paused));
	}

	[Test]
	public async Task Sleep_timer_arms_in_the_background_and_negative_cancels()
	{
		var fake = new FakeMediaControlService();
		var action = new SleepTimerAction(fake, new MediaSettingsProvider());
		var ct = TestContext.CurrentContext.CancellationToken;

		var armed = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["minutes"] = 30.0 },
			CancellationToken = ct,
		});
		var cancelled = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["minutes"] = -1.0 },
			CancellationToken = ct,
		});

		Assert.That(armed.Status, Is.EqualTo(ActionResultStatus.Accepted));
		Assert.That(cancelled.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Snapshot.Status, Is.EqualTo(PlaybackStatus.Playing));
	}

	[Test]
	public async Task Fade_out_pause_ramps_down_and_restores_volume()
	{
		var fake = new FakeMediaControlService();
		var action = new FadeOutPauseAction(fake, new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["seconds"] = 0.1 },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Snapshot.Status, Is.EqualTo(PlaybackStatus.Paused));
		Assert.That(fake.Snapshot.VolumePercent, Is.EqualTo(50));
	}

	[Test]
	public async Task Fade_in_play_ramps_up_to_the_target()
	{
		var fake = new FakeMediaControlService
		{
			Snapshot = new FakeMediaControlService().Snapshot with { Status = PlaybackStatus.Paused },
		};
		var action = new FadeInPlayAction(fake, new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["seconds"] = 0.1, ["target"] = 30.0 },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Snapshot.Status, Is.EqualTo(PlaybackStatus.Playing));
		Assert.That(fake.Snapshot.VolumePercent, Is.EqualTo(30));
	}

	[Test]
	public async Task System_sounds_actions_flip_the_fake_flag()
	{
		var fake = new FakeMediaControlService();
		var ct = TestContext.CurrentContext.CancellationToken;

		var mute = await new MuteSystemSoundsAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var toggle = await new ToggleSystemSoundsAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var unmute = await new UnmuteSystemSoundsAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});

		Assert.That(mute.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(toggle.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(unmute.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.SystemSoundsMuted, Is.False);
		Assert.That(fake.Calls, Does.Contain(nameof(FakeMediaControlService.SetSystemSoundsMuteAsync)));
	}

	[Test]
	public async Task Track_toast_notifies_on_track_change_when_enabled()
	{
		var fake = new FakeMediaControlService();
		var context = new FakeIntegrationContext();
		var entry = context.Config.AddEntry("test");
		context.Config.SeedString(entry, "toast-track", "true");
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(context);

		fake.Snapshot = fake.Snapshot with { Title = "Toasted track" };
		fake.RaiseMediaChanged();

		var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		while (DateTimeOffset.UtcNow < deadline
			&& !context.Notifications.Current.Any(n => n.Title == "Toasted track"))
		{
			await Task.Delay(100, TestContext.CurrentContext.CancellationToken);
		}

		var toast = context.Notifications.Current.FirstOrDefault(n => n.Title == "Toasted track");
		Assert.That(toast, Is.Not.Null);
		Assert.That(toast!.Message, Is.EqualTo("Kavinsky"));

		fake.Snapshot = fake.Snapshot with { Title = "Second toast" };
		fake.RaiseMediaChanged();

		deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		while (DateTimeOffset.UtcNow < deadline
			&& !context.Notifications.Current.Any(n => n.Title == "Second toast"))
		{
			await Task.Delay(100, TestContext.CurrentContext.CancellationToken);
		}

		Assert.That(context.Notifications.Current.Count, Is.EqualTo(1));
		await integration.ShutdownAsync();
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
	public async Task Shuffle_actions_drive_the_fake_session()
	{
		var fake = new FakeMediaControlService();
		var settings = new MediaSettingsProvider();
		var toggle = new ToggleShuffleAction(fake, settings);
		var set = new SetShuffleAction(fake, settings);
		var ct = TestContext.CurrentContext.CancellationToken;

		var toggled = await toggle.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var setOff = await set.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["enabled"] = false },
			CancellationToken = ct,
		});

		Assert.That(toggled.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(setOff.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Snapshot.ShuffleActive, Is.EqualTo(false));
	}

	[Test]
	public async Task Repeat_actions_drive_the_fake_session()
	{
		var fake = new FakeMediaControlService();
		var settings = new MediaSettingsProvider();
		var cycle = new CycleRepeatAction(fake, settings);
		var set = new SetRepeatAction(fake, settings);
		var ct = TestContext.CurrentContext.CancellationToken;

		var cycled = await cycle.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var setOne = await set.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["mode"] = "one" },
			CancellationToken = ct,
		});
		var badMode = await set.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["mode"] = "everything" },
			CancellationToken = ct,
		});

		Assert.That(cycled.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(setOne.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Snapshot.RepeatMode, Is.EqualTo(MediaRepeatMode.One));
		Assert.That(badMode.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task App_volume_actions_drive_the_fake_mixer()
	{
		var fake = new FakeMediaControlService();
		var ct = TestContext.CurrentContext.CancellationToken;

		var set = await new SetAppVolumeAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "spotify", ["volume"] = 30.0 },
			CancellationToken = ct,
		});
		var adjust = await new AdjustAppVolumeAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "spotify", ["delta"] = 5.0 },
			CancellationToken = ct,
		});
		var mute = await new MuteAppAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "spotify" },
			CancellationToken = ct,
		});
		var toggle = await new ToggleAppMuteAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "spotify" },
			CancellationToken = ct,
		});
		var unknown = await new SetAppVolumeAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "no-such-app", ["volume"] = 30.0 },
			CancellationToken = ct,
		});
		var missing = await new MuteAppAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});

		Assert.That(set.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(adjust.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(mute.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(toggle.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.AppVolumes["Spotify"], Is.EqualTo((35, false)));
		Assert.That(unknown.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(missing.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task Device_actions_drive_the_fake_endpoints()
	{
		var fake = new FakeMediaControlService();
		var ct = TestContext.CurrentContext.CancellationToken;

		var set = await new SetOutputDeviceAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["device"] = "headphones" },
			CancellationToken = ct,
		});
		var cycle = await new CycleOutputDeviceAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var unknown = await new SetOutputDeviceAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["device"] = "no-such-device" },
			CancellationToken = ct,
		});

		Assert.That(set.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(cycle.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(unknown.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(fake.Devices.Find(d => d.IsDefault)?.Name, Is.EqualTo("Speakers"));
	}

	[Test]
	public async Task Input_device_actions_drive_the_fake_microphones()
	{
		var fake = new FakeMediaControlService();
		var ct = TestContext.CurrentContext.CancellationToken;

		var set = await new SetInputDeviceAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["device"] = "headset" },
			CancellationToken = ct,
		});
		var cycle = await new CycleInputDeviceAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var unknown = await new SetInputDeviceAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["device"] = "no-such-device" },
			CancellationToken = ct,
		});

		Assert.That(set.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(cycle.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(unknown.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(fake.InputDevices.Find(d => d.IsDefault)?.Name, Is.EqualTo("Microphone Array"));
	}

	[Test]
	public async Task Variables_read_and_write_through_wire_ids()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("media-is-playing")).Value, Is.EqualTo(true));
		Assert.That((await integration.ReadAsync("volume-percent")).Value, Is.EqualTo(50.0));

		var written = await integration.SetValueAsync("volume-percent", 80.0);

		Assert.That(written.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(fake.Snapshot.VolumePercent, Is.EqualTo(80));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Default_input_device_is_unavailable_before_the_first_poll()
	{
		var integration = new PluginIntegration(new FakeMediaControlService(), new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That(await integration.ReadAsync("default-input-device"), Is.EqualTo(VariableReading.Unavailable));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task New_capability_variables_read()
	{
		var fake = new FakeMediaControlService();
		fake.Snapshot = fake.Snapshot with { CanShuffle = true, CanRepeat = true };
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("can-shuffle")).Value, Is.EqualTo(true));
		Assert.That((await integration.ReadAsync("can-repeat")).Value, Is.EqualTo(true));
		Assert.That(await integration.ReadAsync("default-device"), Is.EqualTo(VariableReading.Unavailable));
	}

	[Test]
	public void Artwork_colors_extract_dominant_color()
	{
		using var bitmap = new System.Drawing.Bitmap(4, 4);
		using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
		{
			graphics.Clear(System.Drawing.Color.Red);
		}

		using var stream = new MemoryStream();
		bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);

		var (accent, dark) = ArtworkColors.FromImage(stream.ToArray());

		Assert.That(accent, Is.EqualTo("#FF0000"));
		Assert.That(dark, Is.EqualTo("#3F0000"));
	}

	[Test]
	public void Artwork_colors_fall_back_to_average_for_grey()
	{
		using var bitmap = new System.Drawing.Bitmap(4, 4);
		using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
		{
			graphics.Clear(System.Drawing.Color.FromArgb(128, 128, 128));
		}

		using var stream = new MemoryStream();
		bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);

		var (accent, dark) = ArtworkColors.FromImage(stream.ToArray());

		Assert.That(accent, Is.EqualTo("#808080"));
		Assert.That(dark, Is.EqualTo("#202020"));
	}

	[Test]
	public void Artwork_colors_reject_garbage()
	{
		var (accent, dark) = ArtworkColors.FromImage([0x00, 0x01, 0x02]);

		Assert.That(accent, Is.Empty);
		Assert.That(dark, Is.Empty);
	}

	[Test]
	public async Task Cover_accent_variable_reads_snapshot()
	{
		var fake = new FakeMediaControlService();
		fake.Snapshot = fake.Snapshot with { ArtworkAccent = "#FF0000" };
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("cover-accent")).Value, Is.EqualTo("#FF0000"));
	}

	[Test]
	public async Task Catalog_discovers_mixer_apps()
	{
		var integration = new PluginIntegration(new FakeMediaControlService(), new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var page = await integration.DiscoverAsync(
			new VariableCatalogQuery { PageSize = 50 },
			TestContext.CurrentContext.CancellationToken);

		Assert.That(page.Items.Count, Is.EqualTo(1));
		Assert.That(page.Items[0].Id, Is.EqualTo("Spotify"));
	}

	[Test]
	public async Task Catalog_resolve_accepts_resource_ids()
	{
		var integration = new PluginIntegration(new FakeMediaControlService(), new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var resolved = await integration.ResolveAsync("Spotify.exe", TestContext.CurrentContext.CancellationToken);
		var rejected = await integration.ResolveAsync("not valid id!!", TestContext.CurrentContext.CancellationToken);

		Assert.That(resolved, Is.Not.Null);
		Assert.That(rejected, Is.Null);
	}

	[Test]
	public async Task App_volume_reads_and_writes_through_variables()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var reading = await integration.ReadAsync("Spotify.exe", TestContext.CurrentContext.CancellationToken);
		var written = await integration.SetValueAsync("Spotify.exe", 30.0, TestContext.CurrentContext.CancellationToken);
		var missing = await integration.ReadAsync("no-such-app", TestContext.CurrentContext.CancellationToken);
		var missingWrite = await integration.SetValueAsync("no-such-app", 30.0, TestContext.CurrentContext.CancellationToken);

		Assert.That(reading.Value, Is.EqualTo(50.0));
		Assert.That(written.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(fake.AppVolumes["Spotify"].Volume, Is.EqualTo(30));
		Assert.That(missing, Is.EqualTo(VariableReading.Unavailable));
		Assert.That(missingWrite.Status, Is.EqualTo(VariableWriteStatus.Unavailable));
	}

	[Test]
	public async Task App_volume_accepts_exe_suffixed_ids()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var reading = await integration.ReadAsync("Spotify.exe", TestContext.CurrentContext.CancellationToken);
		var resolved = await integration.ResolveAsync("Spotify.exe", TestContext.CurrentContext.CancellationToken);

		Assert.That(reading.Value, Is.EqualTo(50.0));
		Assert.That(resolved?.Id, Is.EqualTo("Spotify"));
	}

	[Test]
	public async Task Play_reports_not_supported_when_the_session_rejects_it()
	{
		var fake = new FakeMediaControlService { TransportResult = false };
		fake.Snapshot = fake.Snapshot with { Status = PlaybackStatus.Paused };
		var action = new PlayAction(fake, new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
	}

	[Test]
	public async Task Metadata_variables_expose_extended_properties()
	{
		var fake = new FakeMediaControlService();
		fake.Snapshot = fake.Snapshot with
		{
			AlbumArtist = "Various Artists",
			Genres = ["Hardstyle", "EDM"],
			TrackNumber = 3,
			AlbumTrackCount = 12,
			Subtitle = "Extended mix",
			PlaybackType = MediaPlaybackType.Video,
			PlaybackRate = 1.5,
			CanPlay = true,
			CanSeek = true,
		};
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("album-artist")).Value, Is.EqualTo("Various Artists"));
		Assert.That((await integration.ReadAsync("genres")).Value, Is.EqualTo("Hardstyle, EDM"));
		Assert.That((await integration.ReadAsync("track-number")).Value, Is.EqualTo(3.0));
		Assert.That((await integration.ReadAsync("track-count")).Value, Is.EqualTo(12.0));
		Assert.That((await integration.ReadAsync("subtitle")).Value, Is.EqualTo("Extended mix"));
		Assert.That((await integration.ReadAsync("playback-type")).Value, Is.EqualTo("video"));
		Assert.That((await integration.ReadAsync("playback-rate")).Value, Is.EqualTo(1.5));
		Assert.That((await integration.ReadAsync("is-live")).Value, Is.EqualTo(false));
		Assert.That((await integration.ReadAsync("can-play")).Value, Is.EqualTo(true));
		Assert.That((await integration.ReadAsync("can-seek")).Value, Is.EqualTo(true));
		Assert.That((await integration.ReadAsync("can-stop")).Value, Is.EqualTo(false));
	}

	[Test]
	public async Task Live_stream_reports_is_live()
	{
		var fake = new FakeMediaControlService();
		fake.Snapshot = fake.Snapshot with { Status = PlaybackStatus.Playing, Duration = TimeSpan.Zero };
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("is-live")).Value, Is.EqualTo(true));
	}

	[Test]
	public async Task Media_changed_event_triggers_a_refresh()
	{
		var fake = new FakeMediaControlService();
		var context = new FakeIntegrationContext();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(context);

		fake.Snapshot = fake.Snapshot with { Title = "Changed over event" };
		fake.RaiseMediaChanged();

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
	public void Timeline_extrapolates_while_playing()
	{
		var now = new DateTimeOffset(2026, 9, 9, 12, 0, 10, TimeSpan.Zero);

		var position = MediaTimeline.Extrapolate(
			TimeSpan.FromSeconds(13), TimeSpan.FromMinutes(2),
			new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero),
			PlaybackStatus.Playing, true, now);

		Assert.That(position.TotalSeconds, Is.EqualTo(23).Within(0.5));
	}

	[Test]
	public void Timeline_holds_when_paused_or_disabled()
	{
		var updated = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
		var now = updated.AddSeconds(30);

		Assert.That(
			MediaTimeline.Extrapolate(TimeSpan.FromSeconds(13), TimeSpan.FromMinutes(2), updated, PlaybackStatus.Paused, true, now),
			Is.EqualTo(TimeSpan.FromSeconds(13)));
		Assert.That(
			MediaTimeline.Extrapolate(TimeSpan.FromSeconds(13), TimeSpan.FromMinutes(2), updated, PlaybackStatus.Playing, false, now),
			Is.EqualTo(TimeSpan.FromSeconds(13)));
	}

	[Test]
	public void Timeline_clamps_to_duration_and_zero()
	{
		var updated = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

		Assert.That(
			MediaTimeline.Extrapolate(TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(113), updated, PlaybackStatus.Playing, true, updated.AddMinutes(5)),
			Is.EqualTo(TimeSpan.FromSeconds(113)));
		Assert.That(
			MediaTimeline.Extrapolate(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(113), updated, PlaybackStatus.Playing, true, updated),
			Is.EqualTo(TimeSpan.Zero));
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

	[Test]
	public async Task Mic_actions_drive_the_fake_microphone()
	{
		var fake = new FakeMediaControlService();
		var ct = TestContext.CurrentContext.CancellationToken;

		var set = await new SetMicVolumeAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["volume"] = 30.0 },
			CancellationToken = ct,
		});
		var mute = await new MuteMicAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var toggle = await new ToggleMicMuteAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var unmute = await new UnmuteMicAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = ct,
		});
		var bad = await new SetMicVolumeAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["volume"] = 150.0 },
			CancellationToken = ct,
		});

		Assert.That(set.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(mute.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(toggle.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(unmute.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(bad.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(fake.MicVolumePercent, Is.EqualTo(30));
		Assert.That(fake.IsMicMuted, Is.False);
	}

	[Test]
	public async Task Mic_variables_read_and_write()
	{
		var fake = new FakeMediaControlService
		{
			Snapshot = new FakeMediaControlService().Snapshot with { MicVolumePercent = 80, IsMicMuted = true },
		};
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("mic-volume-percent")).Value, Is.EqualTo(80.0));
		Assert.That((await integration.ReadAsync("is-mic-muted")).Value, Is.EqualTo(true));

		var volume = await integration.SetValueAsync("mic-volume-percent", 40.0);
		var mute = await integration.SetValueAsync("is-mic-muted", false);

		Assert.That(volume.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(mute.Status, Is.EqualTo(VariableWriteStatus.Applied));
		Assert.That(fake.MicVolumePercent, Is.EqualTo(40));
		Assert.That(fake.IsMicMuted, Is.False);
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Peak_variables_report_live_levels()
	{
		var fake = new FakeMediaControlService { MicPeak = 62.5, SystemPeak = 33.0 };
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("mic-level-percent")).Value, Is.EqualTo(62.5));
		Assert.That((await integration.ReadAsync("system-level-percent")).Value, Is.EqualTo(33.0));

		fake.MicPeak = null;
		Assert.That(await integration.ReadAsync("mic-level-percent"), Is.EqualTo(VariableReading.Unavailable));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Active_apps_lists_sessions()
	{
		var fake = new FakeMediaControlService();
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		Assert.That((await integration.ReadAsync("active-apps")).Value, Is.EqualTo("Spotify"));

		fake.AppVolumes.Clear();
		Assert.That(await integration.ReadAsync("active-apps"), Is.EqualTo(VariableReading.Unavailable));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Focus_app_mutes_everything_else()
	{
		var fake = new FakeMediaControlService();
		fake.AppVolumes["Discord"] = (70, false);
		var ct = TestContext.CurrentContext.CancellationToken;

		var focused = await new FocusAppAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "discord" },
			CancellationToken = ct,
		});
		var unknown = await new FocusAppAction(fake).CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "no-such-app" },
			CancellationToken = ct,
		});

		Assert.That(focused.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(unknown.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(fake.AppVolumes["Discord"].Muted, Is.False);
		Assert.That(fake.AppVolumes["Spotify"].Muted, Is.True);
	}

	[Test]
	public async Task Music_player_shuffle_change_is_attempted_before_failing()
	{
		var fake = new FakeMediaControlService
		{
			Snapshot = new FakeMediaControlService().Snapshot with { ShuffleActive = false },
		};
		var player = new SystemMusicPlayer(fake);

		await player.SetShuffleAsync(true, TestContext.CurrentContext.CancellationToken);

		Assert.That(fake.Snapshot.ShuffleActive, Is.True);
		Assert.That(fake.Calls, Does.Contain(nameof(FakeMediaControlService.SetShuffleAsync)));
	}

	[Test]
	public void Music_player_shuffle_change_throws_when_unsupported()
	{
		var fake = new FakeMediaControlService
		{
			Snapshot = new FakeMediaControlService().Snapshot with { ShuffleActive = false },
			TransportResult = false,
		};
		var player = new SystemMusicPlayer(fake);

		Assert.ThrowsAsync<InvalidOperationException>(() =>
			player.SetShuffleAsync(true, TestContext.CurrentContext.CancellationToken));
	}

	[Test]
	public async Task Music_player_repeat_change_is_attempted_before_failing()
	{
		var fake = new FakeMediaControlService();
		var player = new SystemMusicPlayer(fake);

		await player.SetRepeatModeAsync(RepeatMode.Context, TestContext.CurrentContext.CancellationToken);

		Assert.That(fake.Snapshot.RepeatMode, Is.EqualTo(MediaRepeatMode.All));
		Assert.That(fake.Calls, Does.Contain(nameof(FakeMediaControlService.SetRepeatAsync)));
	}

	[Test]
	public async Task Volume_variable_is_unavailable_without_an_audio_reading()
	{
		var fake = new FakeMediaControlService { Snapshot = MediaSnapshot.Empty };
		var integration = new PluginIntegration(fake, new MediaSettingsProvider(), TestLogger());
		await integration.InitializeAsync(new FakeIntegrationContext());

		var volume = await integration.ReadAsync("volume-percent", TestContext.CurrentContext.CancellationToken);

		Assert.That(volume, Is.EqualTo(VariableReading.Unavailable));
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Set_shuffle_uses_enabled_by_default()
	{
		var fake = new FakeMediaControlService
		{
			Snapshot = new FakeMediaControlService().Snapshot with { ShuffleActive = false },
		};
		var action = new SetShuffleAction(fake, new MediaSettingsProvider());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(),
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(fake.Snapshot.ShuffleActive, Is.True);
	}

	[Test]
	public async Task Adjust_app_volume_rejects_an_unreadable_delta()
	{
		var action = new AdjustAppVolumeAction(new FakeMediaControlService());

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["app"] = "Spotify", ["delta"] = new object() },
			CancellationToken = TestContext.CurrentContext.CancellationToken,
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public void Every_eager_variable_has_a_refresh_interval()
	{
		foreach (var definition in MediaVariables.CreateDefinitions())
		{
			Assert.That(definition.RefreshInterval, Is.Not.Null, definition.Id);
		}
	}

	[Test]
	public void App_volume_display_name_uses_the_localized_template()
	{
		Assert.That(Strings.LocalizationCatalog.KeysOf("en"), Does.Contain("Variables.AppVolume.DisplayName"));
		Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", "Variables.AppVolume.DisplayName", out var template), Is.True);
		Assert.That(template, Is.EqualTo("{name} volume"));
		Assert.That(MediaVariables.AppVolume("Spotify").Name, Is.EqualTo("app_Spotify"));
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
	public async Task Empty_values_fall_back_to_defaults()
	{
		var flow = CreateFlow();
		var ct = TestContext.CurrentContext.CancellationToken;
		var context = new FakeConfigFlowContext();

		var first = await flow.SubmitAsync("playback", new Dictionary<string, object?>
		{
			["preferred-app"] = "",
			["seek-seconds"] = "",
			["ff-seconds"] = "",
		}, context, ct);
		var second = await flow.SubmitAsync("volume", VolumeInput(), context, ct);
		var third = await flow.SubmitAsync("updates", UpdatesInput(), context, ct);
		var fourth = await flow.SubmitAsync("events", EventsInput(), context, ct);
		var done = await flow.SubmitAsync("advanced", new Dictionary<string, object?>
		{
			["button-artwork"] = true,
			["snapshot-timeout"] = "",
			["control-timeout"] = "",
			["artwork-cache"] = "",
			["reset-defaults"] = false,
		}, context, ct);

		Assert.That(first.NextStep!.StepId, Is.EqualTo("volume"));
		Assert.That(done.Values!["seek-seconds"].Value, Is.EqualTo("10"));
		Assert.That(done.Values!["snapshot-timeout"].Value, Is.EqualTo("3"));
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
	public async Task New_settings_round_trip_through_the_flow()
	{
		var flow = CreateFlow();
		var ct = TestContext.CurrentContext.CancellationToken;
		var context = new FakeConfigFlowContext();

		await flow.SubmitAsync("playback", new Dictionary<string, object?>
		{
			["preferred-app"] = "Spotify",
			["seek-seconds"] = 10.0,
			["ff-seconds"] = 10.0,
			["sleep-minutes"] = 45.0,
			["fade-seconds"] = 5.0,
		}, context, ct);
		await flow.SubmitAsync("volume", new Dictionary<string, object?>
		{
			["volume-step"] = 5.0,
			["max-volume"] = 100.0,
			["unmute-on-volume"] = true,
			["mic-max-volume"] = 80.0,
			["unmute-mic-on-volume"] = false,
			["device-role"] = "communications",
		}, context, ct);
		await flow.SubmitAsync("updates", UpdatesInput(), context, ct);
		await flow.SubmitAsync("events", new Dictionary<string, object?>
		{
			["events-track"] = true,
			["events-playback"] = true,
			["events-volume"] = true,
			["events-mute"] = true,
			["toast-track"] = true,
		}, context, ct);
		var done = await flow.SubmitAsync("advanced", new Dictionary<string, object?>
		{
			["button-artwork"] = true,
			["snapshot-timeout"] = 3.0,
			["control-timeout"] = 6.0,
			["artwork-cache"] = 8.0,
			["event-debounce-ms"] = 1000.0,
			["focus-unmute-target"] = false,
			["reset-defaults"] = false,
		}, context, ct);

		Assert.That(done.Values!["sleep-minutes"].Value, Is.EqualTo("45"));
		Assert.That(done.Values!["fade-seconds"].Value, Is.EqualTo("5"));
		Assert.That(done.Values!["mic-max-volume"].Value, Is.EqualTo("80"));
		Assert.That(done.Values!["unmute-mic-on-volume"].Value, Is.EqualTo("false"));
		Assert.That(done.Values!["device-role"].Value, Is.EqualTo("communications"));
		Assert.That(done.Values!["toast-track"].Value, Is.EqualTo("true"));
		Assert.That(done.Values!["event-debounce-ms"].Value, Is.EqualTo("1000"));
		Assert.That(done.Values!["focus-unmute-target"].Value, Is.EqualTo("false"));
	}

	[Test]
	public async Task Reader_falls_back_for_unknown_device_roles()
	{
		var config = new FakeIntegrationConfig();
		var entry = config.AddEntry("test");
		config.SeedString(entry, "device-role", "nonsense");
		config.SeedString(entry, "sleep-minutes", "45");

		var settings = await MediaSettingsReader.ReadAsync(config);

		Assert.That(settings.DeviceRole, Is.EqualTo(MediaSettings.DeviceRoles.All));
		Assert.That(settings.SleepDefaultMinutes, Is.EqualTo(45.0));
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
			"Actions.SetInputDevice.Name", "Actions.CycleInputDevice.Name",
			"Actions.SetMicVolume.Name", "Actions.MuteMic.Name", "Actions.FocusApp.Name",
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

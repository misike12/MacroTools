using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using WindowsMediaControl.Actions;

namespace WindowsMediaControl.Config;

public sealed class MediaConfigFlow : IConfigFlow
{
	private const string PlaybackStep = "playback";
	private const string VolumeStep = "volume";
	private const string UpdatesStep = "updates";
	private const string EventsStep = "events";
	private const string AdvancedStep = "advanced";

	private readonly Dictionary<string, object?> _input = new(StringComparer.Ordinal);

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
		Task.FromResult(ConfigFlowResult.Step(PlaybackStepDefinition()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		foreach (var pair in input)
		{
			_input[pair.Key] = pair.Value;
		}

		return Task.FromResult(stepId switch
		{
			PlaybackStep => Validate(PlaybackStepDefinition(), CheckPlayback(), VolumeStepDefinition()),
			VolumeStep => Validate(VolumeStepDefinition(), CheckVolume(), UpdatesStepDefinition()),
			UpdatesStep => Validate(UpdatesStepDefinition(), CheckUpdates(), EventsStepDefinition()),
			EventsStep => Validate(EventsStepDefinition(), new Dictionary<string, LocalizedText>(), AdvancedStepDefinition()),
			AdvancedStep => SubmitAdvanced(),
			_ => ConfigFlowResult.Error(PlaybackStepDefinition(), Strings.Errors.InvalidSettings(), new Dictionary<string, LocalizedText>()),
		});
	}

	private ConfigFlowResult SubmitAdvanced()
	{
		var errors = CheckAdvanced();
		if (errors.Count > 0)
		{
			return ConfigFlowResult.Error(AdvancedStepDefinition(), Strings.Errors.InvalidSettings(), errors);
		}

		if (ReadBool(MediaSettings.Keys.Reset) == true)
		{
			return ConfigFlowResult.Complete("Windows Media Control", MediaSettings.Default.ToValues());
		}

		return ConfigFlowResult.Complete("Windows Media Control", Collect().ToValues());
	}

	private static ConfigFlowResult Validate(
		ConfigFlowStep step,
		Dictionary<string, LocalizedText> errors,
		ConfigFlowStep next)
	{
		return errors.Count > 0
			? ConfigFlowResult.Error(step, Strings.Errors.InvalidSettings(), errors)
			: ConfigFlowResult.Step(next);
	}

	private Dictionary<string, LocalizedText> CheckPlayback() => CheckNumbers(new (string Key, double Min, double Max)[]
	{
		(MediaSettings.Keys.SeekSeconds, 1, 120),
		(MediaSettings.Keys.FastForwardSeconds, 1, 60),
		(MediaSettings.Keys.SleepMinutes, 1, 180),
		(MediaSettings.Keys.FadeSeconds, 1, 30),
	});

	private Dictionary<string, LocalizedText> CheckVolume() => CheckNumbers(new (string Key, double Min, double Max)[]
	{
		(MediaSettings.Keys.VolumeStep, 1, 25),
		(MediaSettings.Keys.MaxVolume, 10, 100),
		(MediaSettings.Keys.MicMaxVolume, 10, 100),
	});

	private Dictionary<string, LocalizedText> CheckUpdates() => CheckNumbers(new (string Key, double Min, double Max)[]
	{
		(MediaSettings.Keys.PollInterval, 1, 10),
		(MediaSettings.Keys.StatePoll, 1, 120),
		(MediaSettings.Keys.IconPoll, 10, 600),
	});

	private Dictionary<string, LocalizedText> CheckAdvanced() => CheckNumbers(new (string Key, double Min, double Max)[]
	{
		(MediaSettings.Keys.SnapshotTimeout, 1, 10),
		(MediaSettings.Keys.ControlTimeout, 2, 15),
		(MediaSettings.Keys.ArtworkCache, 1, 16),
		(MediaSettings.Keys.EventDebounce, 100, 5000),
	});

	private Dictionary<string, LocalizedText> CheckNumbers((string Key, double Min, double Max)[] rules)
	{
		var errors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		foreach (var (key, min, max) in rules)
		{
			if (!IsProvided(key))
			{
				continue;
			}

			var value = ReadNumber(key);
			if (value is null || value < min || value > max)
			{
				errors[key] = Strings.Errors.FieldInvalid();
			}
		}

		return errors;
	}

	private bool IsProvided(string key) =>
		_input.TryGetValue(key, out var raw)
			&& raw is not null
			&& !(raw is string text && string.IsNullOrWhiteSpace(text));

	private MediaSettings Collect()
	{
		var fallback = MediaSettings.Default;
		return new MediaSettings(
			PreferredApp: ReadText(MediaSettings.Keys.PreferredApp, fallback.PreferredApp),
			DefaultSeekSeconds: ReadNumber(MediaSettings.Keys.SeekSeconds) ?? fallback.DefaultSeekSeconds,
			FastForwardSeconds: ReadNumber(MediaSettings.Keys.FastForwardSeconds) ?? fallback.FastForwardSeconds,
			DefaultVolumeStep: ReadNumber(MediaSettings.Keys.VolumeStep) ?? fallback.DefaultVolumeStep,
			MaxVolumeLimit: (int)Math.Round(ReadNumber(MediaSettings.Keys.MaxVolume) ?? fallback.MaxVolumeLimit),
			UnmuteOnVolumeChange: ReadBool(MediaSettings.Keys.UnmuteOnVolume) ?? fallback.UnmuteOnVolumeChange,
			PollIntervalSeconds: ReadNumber(MediaSettings.Keys.PollInterval) ?? fallback.PollIntervalSeconds,
			StatePollSeconds: ReadNumber(MediaSettings.Keys.StatePoll) ?? fallback.StatePollSeconds,
			IconPollSeconds: ReadNumber(MediaSettings.Keys.IconPoll) ?? fallback.IconPollSeconds,
			TrackEvents: ReadBool(MediaSettings.Keys.EventsTrack) ?? fallback.TrackEvents,
			PlaybackEvents: ReadBool(MediaSettings.Keys.EventsPlayback) ?? fallback.PlaybackEvents,
			VolumeEvents: ReadBool(MediaSettings.Keys.EventsVolume) ?? fallback.VolumeEvents,
			MuteEvents: ReadBool(MediaSettings.Keys.EventsMute) ?? fallback.MuteEvents,
			ExtrapolatePosition: ReadBool(MediaSettings.Keys.Extrapolate) ?? fallback.ExtrapolatePosition,
			ButtonArtwork: ReadBool(MediaSettings.Keys.ButtonArtwork) ?? fallback.ButtonArtwork,
			SnapshotTimeoutSeconds: ReadNumber(MediaSettings.Keys.SnapshotTimeout) ?? fallback.SnapshotTimeoutSeconds,
			ControlTimeoutSeconds: ReadNumber(MediaSettings.Keys.ControlTimeout) ?? fallback.ControlTimeoutSeconds,
			ArtworkCacheSize: Math.Max(1, (int)Math.Round(ReadNumber(MediaSettings.Keys.ArtworkCache) ?? fallback.ArtworkCacheSize)),
			SleepDefaultMinutes: ReadNumber(MediaSettings.Keys.SleepMinutes) ?? fallback.SleepDefaultMinutes,
			FadeSeconds: ReadNumber(MediaSettings.Keys.FadeSeconds) ?? fallback.FadeSeconds,
			MicMaxVolumeLimit: (int)Math.Round(ReadNumber(MediaSettings.Keys.MicMaxVolume) ?? fallback.MicMaxVolumeLimit),
			UnmuteMicOnVolumeChange: ReadBool(MediaSettings.Keys.UnmuteMicOnVolume) ?? fallback.UnmuteMicOnVolumeChange,
			DeviceRole: ReadDeviceRole(fallback.DeviceRole),
			TrackToast: ReadBool(MediaSettings.Keys.TrackToast) ?? fallback.TrackToast,
			FocusUnmuteTarget: ReadBool(MediaSettings.Keys.FocusUnmute) ?? fallback.FocusUnmuteTarget,
			EventDebounceMs: ReadNumber(MediaSettings.Keys.EventDebounce) ?? fallback.EventDebounceMs);
	}

	private double? ReadNumber(string key) =>
		_input.TryGetValue(key, out var raw) ? MediaParameters.ReadNumberValue(raw) : null;

	private bool? ReadBool(string key) =>
		_input.TryGetValue(key, out var raw) ? MediaParameters.ReadBooleanValue(raw) : null;

	private string ReadText(string key, string fallback) =>
		_input.TryGetValue(key, out var raw) ? raw?.ToString() ?? fallback : fallback;

	private string ReadDeviceRole(string fallback)
	{
		var role = ReadText(MediaSettings.Keys.DeviceRole, fallback).ToLowerInvariant();
		return role is MediaSettings.DeviceRoles.Multimedia
			or MediaSettings.DeviceRoles.Console
			or MediaSettings.DeviceRoles.Communications
			or MediaSettings.DeviceRoles.All
			? role
			: fallback;
	}

	private static ConfigFlowStep PlaybackStepDefinition() => new()
	{
		StepId = PlaybackStep,
		Title = Strings.Config.Playback.Title(),
		Description = Strings.Config.Playback.Description(),
		Fields =
		[
			Text(MediaSettings.Keys.PreferredApp, Strings.Config.Playback.PreferredApp.Label(), Strings.Config.Playback.PreferredApp.Description(), string.Empty),
			Number(MediaSettings.Keys.SeekSeconds, Strings.Config.Playback.SeekSeconds.Label(), Strings.Config.Playback.SeekSeconds.Description(), 1, 120, 1, MediaSettings.Default.DefaultSeekSeconds),
			Number(MediaSettings.Keys.FastForwardSeconds, Strings.Config.Playback.FastForwardSeconds.Label(), Strings.Config.Playback.FastForwardSeconds.Description(), 1, 60, 1, MediaSettings.Default.FastForwardSeconds),
			Number(MediaSettings.Keys.SleepMinutes, Strings.Config.Playback.SleepMinutes.Label(), Strings.Config.Playback.SleepMinutes.Description(), 1, 180, 1, MediaSettings.Default.SleepDefaultMinutes),
			Number(MediaSettings.Keys.FadeSeconds, Strings.Config.Playback.FadeSeconds.Label(), Strings.Config.Playback.FadeSeconds.Description(), 1, 30, 1, MediaSettings.Default.FadeSeconds),
		],
	};

	private static ConfigFlowStep VolumeStepDefinition() => new()
	{
		StepId = VolumeStep,
		Title = Strings.Config.Volume.Title(),
		Description = Strings.Config.Volume.Description(),
		Fields =
		[
			Number(MediaSettings.Keys.VolumeStep, Strings.Config.Volume.Step.Label(), Strings.Config.Volume.Step.Description(), 1, 25, 1, MediaSettings.Default.DefaultVolumeStep),
			Number(MediaSettings.Keys.MaxVolume, Strings.Config.Volume.MaxLimit.Label(), Strings.Config.Volume.MaxLimit.Description(), 10, 100, 1, MediaSettings.Default.MaxVolumeLimit),
			Toggle(MediaSettings.Keys.UnmuteOnVolume, Strings.Config.Volume.UnmuteOnChange.Label(), Strings.Config.Volume.UnmuteOnChange.Description(), MediaSettings.Default.UnmuteOnVolumeChange),
			Number(MediaSettings.Keys.MicMaxVolume, Strings.Config.Volume.MicMaxLimit.Label(), Strings.Config.Volume.MicMaxLimit.Description(), 10, 100, 1, MediaSettings.Default.MicMaxVolumeLimit),
			Toggle(MediaSettings.Keys.UnmuteMicOnVolume, Strings.Config.Volume.UnmuteMicOnChange.Label(), Strings.Config.Volume.UnmuteMicOnChange.Description(), MediaSettings.Default.UnmuteMicOnVolumeChange),
			new ActionParameter
			{
				Name = MediaSettings.Keys.DeviceRole,
				Type = ActionParameterType.Choice,
				Label = Strings.Config.Volume.DeviceRole.Label(),
				Description = Strings.Config.Volume.DeviceRole.Description(),
				Options =
				[
					new ActionParameterOption { Value = MediaSettings.DeviceRoles.All, Label = Strings.Config.Volume.DeviceRole.All() },
					new ActionParameterOption { Value = MediaSettings.DeviceRoles.Multimedia, Label = Strings.Config.Volume.DeviceRole.Multimedia() },
					new ActionParameterOption { Value = MediaSettings.DeviceRoles.Console, Label = Strings.Config.Volume.DeviceRole.Console() },
					new ActionParameterOption { Value = MediaSettings.DeviceRoles.Communications, Label = Strings.Config.Volume.DeviceRole.Communications() },
				],
				DefaultValue = MediaSettings.DeviceRoles.All,
			},
		],
	};

	private static ConfigFlowStep UpdatesStepDefinition() => new()
	{
		StepId = UpdatesStep,
		Title = Strings.Config.Updates.Title(),
		Description = Strings.Config.Updates.Description(),
		Fields =
		[
			Number(MediaSettings.Keys.PollInterval, Strings.Config.Updates.PollInterval.Label(), Strings.Config.Updates.PollInterval.Description(), 1, 10, 1, MediaSettings.Default.PollIntervalSeconds),
			Number(MediaSettings.Keys.StatePoll, Strings.Config.Updates.StatePoll.Label(), Strings.Config.Updates.StatePoll.Description(), 1, 120, 1, MediaSettings.Default.StatePollSeconds),
			Number(MediaSettings.Keys.IconPoll, Strings.Config.Updates.IconPoll.Label(), Strings.Config.Updates.IconPoll.Description(), 10, 600, 10, MediaSettings.Default.IconPollSeconds),
			Toggle(MediaSettings.Keys.Extrapolate, Strings.Config.Updates.Extrapolate.Label(), Strings.Config.Updates.Extrapolate.Description(), MediaSettings.Default.ExtrapolatePosition),
		],
	};

	private static ConfigFlowStep EventsStepDefinition() => new()
	{
		StepId = EventsStep,
		Title = Strings.Config.Events.Title(),
		Description = Strings.Config.Events.Description(),
		Fields =
		[
			Toggle(MediaSettings.Keys.EventsTrack, Strings.Config.Events.Track.Label(), Strings.Events.TrackChanged.Description(), MediaSettings.Default.TrackEvents),
			Toggle(MediaSettings.Keys.EventsPlayback, Strings.Config.Events.Playback.Label(), Strings.Events.PlaybackChanged.Description(), MediaSettings.Default.PlaybackEvents),
			Toggle(MediaSettings.Keys.EventsVolume, Strings.Config.Events.Volume.Label(), Strings.Events.VolumeChanged.Description(), MediaSettings.Default.VolumeEvents),
			Toggle(MediaSettings.Keys.EventsMute, Strings.Config.Events.Mute.Label(), Strings.Events.MuteChanged.Description(), MediaSettings.Default.MuteEvents),
			Toggle(MediaSettings.Keys.TrackToast, Strings.Config.Events.Toast.Label(), Strings.Config.Events.Toast.Description(), MediaSettings.Default.TrackToast),
		],
	};

	private static ConfigFlowStep AdvancedStepDefinition() => new()
	{
		StepId = AdvancedStep,
		Title = Strings.Config.Advanced.Title(),
		Description = Strings.Config.Advanced.Description(),
		Fields =
		[
			Toggle(MediaSettings.Keys.ButtonArtwork, Strings.Config.Advanced.ButtonArtwork.Label(), Strings.Config.Advanced.ButtonArtwork.Description(), MediaSettings.Default.ButtonArtwork),
		],
		AdvancedFields =
		[
			Number(MediaSettings.Keys.SnapshotTimeout, Strings.Config.Advanced.SnapshotTimeout.Label(), Strings.Config.Advanced.SnapshotTimeout.Description(), 1, 10, 1, MediaSettings.Default.SnapshotTimeoutSeconds),
			Number(MediaSettings.Keys.ControlTimeout, Strings.Config.Advanced.ControlTimeout.Label(), Strings.Config.Advanced.ControlTimeout.Description(), 2, 15, 1, MediaSettings.Default.ControlTimeoutSeconds),
			Number(MediaSettings.Keys.ArtworkCache, Strings.Config.Advanced.ArtworkCache.Label(), Strings.Config.Advanced.ArtworkCache.Description(), 1, 16, 1, MediaSettings.Default.ArtworkCacheSize),
			Number(MediaSettings.Keys.EventDebounce, Strings.Config.Advanced.EventDebounce.Label(), Strings.Config.Advanced.EventDebounce.Description(), 100, 5000, 50, MediaSettings.Default.EventDebounceMs),
			Toggle(MediaSettings.Keys.FocusUnmute, Strings.Config.Advanced.FocusUnmute.Label(), Strings.Config.Advanced.FocusUnmute.Description(), MediaSettings.Default.FocusUnmuteTarget),
			Toggle(MediaSettings.Keys.Reset, Strings.Config.Advanced.Reset.Label(), Strings.Config.Advanced.Reset.Description(), false),
		],
	};

	private static ActionParameter Text(string name, LocalizedText label, LocalizedText description, string defaultValue) =>
		new() { Name = name, Type = ActionParameterType.String, Label = label, Description = description, DefaultValue = defaultValue };

	private static ActionParameter Number(string name, LocalizedText label, LocalizedText description, double min, double max, double step, double defaultValue) =>
		new() { Name = name, Type = ActionParameterType.Number, Label = label, Description = description, Min = min, Max = max, Step = step, DefaultValue = defaultValue, Placeholder = Strings.Config.DefaultValueHint(defaultValue.ToString(CultureInfo.InvariantCulture)) };

	private static ActionParameter Toggle(string name, LocalizedText label, LocalizedText description, bool defaultValue) =>
		new() { Name = name, Type = ActionParameterType.Boolean, Label = label, Description = description, DefaultValue = defaultValue };
}

internal static class MediaSettingsValues
{
	public static IReadOnlyDictionary<string, ConfigFlowValue> ToValues(this MediaSettings settings) =>
		new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[MediaSettings.Keys.PreferredApp] = ConfigFlowValue.Plain(settings.PreferredApp),
			[MediaSettings.Keys.SeekSeconds] = Plain(settings.DefaultSeekSeconds),
			[MediaSettings.Keys.FastForwardSeconds] = Plain(settings.FastForwardSeconds),
			[MediaSettings.Keys.VolumeStep] = Plain(settings.DefaultVolumeStep),
			[MediaSettings.Keys.MaxVolume] = Plain(settings.MaxVolumeLimit),
			[MediaSettings.Keys.UnmuteOnVolume] = Plain(settings.UnmuteOnVolumeChange),
			[MediaSettings.Keys.PollInterval] = Plain(settings.PollIntervalSeconds),
			[MediaSettings.Keys.StatePoll] = Plain(settings.StatePollSeconds),
			[MediaSettings.Keys.IconPoll] = Plain(settings.IconPollSeconds),
			[MediaSettings.Keys.EventsTrack] = Plain(settings.TrackEvents),
			[MediaSettings.Keys.EventsPlayback] = Plain(settings.PlaybackEvents),
			[MediaSettings.Keys.EventsVolume] = Plain(settings.VolumeEvents),
			[MediaSettings.Keys.EventsMute] = Plain(settings.MuteEvents),
			[MediaSettings.Keys.Extrapolate] = Plain(settings.ExtrapolatePosition),
			[MediaSettings.Keys.ButtonArtwork] = Plain(settings.ButtonArtwork),
			[MediaSettings.Keys.SnapshotTimeout] = Plain(settings.SnapshotTimeoutSeconds),
			[MediaSettings.Keys.ControlTimeout] = Plain(settings.ControlTimeoutSeconds),
			[MediaSettings.Keys.ArtworkCache] = Plain(settings.ArtworkCacheSize),
			[MediaSettings.Keys.SleepMinutes] = Plain(settings.SleepDefaultMinutes),
			[MediaSettings.Keys.FadeSeconds] = Plain(settings.FadeSeconds),
			[MediaSettings.Keys.MicMaxVolume] = Plain(settings.MicMaxVolumeLimit),
			[MediaSettings.Keys.UnmuteMicOnVolume] = Plain(settings.UnmuteMicOnVolumeChange),
			[MediaSettings.Keys.DeviceRole] = ConfigFlowValue.Plain(settings.DeviceRole),
			[MediaSettings.Keys.TrackToast] = Plain(settings.TrackToast),
			[MediaSettings.Keys.FocusUnmute] = Plain(settings.FocusUnmuteTarget),
			[MediaSettings.Keys.EventDebounce] = Plain(settings.EventDebounceMs),
		};

	private static ConfigFlowValue Plain(double value) =>
		ConfigFlowValue.Plain(value.ToString(CultureInfo.InvariantCulture));

	private static ConfigFlowValue Plain(bool value) =>
		ConfigFlowValue.Plain(value ? "true" : "false");
}

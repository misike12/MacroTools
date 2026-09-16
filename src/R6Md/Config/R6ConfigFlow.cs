using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using R6Md.Replays;

namespace R6Md.Config;

public sealed class R6ConfigFlow : IConfigFlow
{
	private const string ReplaysStep = "replays";
	private const string LiveStep = "live";
	private const string EventsStep = "events";

	private readonly Dictionary<string, object?> _input = new(StringComparer.Ordinal);

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
		Task.FromResult(ConfigFlowResult.Step(ReplaysStepDefinition()));

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
			ReplaysStep => Validate(ReplaysStepDefinition(), CheckReplays(), LiveStepDefinition()),
			LiveStep => Validate(LiveStepDefinition(), CheckLive(), EventsStepDefinition()),
			EventsStep => SubmitEvents(),
			_ => ConfigFlowResult.Error(ReplaysStepDefinition(), Strings.Errors.InvalidSettings(), new Dictionary<string, LocalizedText>()),
		});
	}

	private ConfigFlowResult SubmitEvents() =>
		ConfigFlowResult.Complete("R6MD", Collect().ToValues());

	private static ConfigFlowResult Validate(
		ConfigFlowStep step,
		Dictionary<string, LocalizedText> errors,
		ConfigFlowStep next)
	{
		return errors.Count > 0
			? ConfigFlowResult.Error(step, Strings.Errors.InvalidSettings(), errors)
			: ConfigFlowResult.Step(next);
	}

	private Dictionary<string, LocalizedText> CheckReplays()
	{
		var errors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (IsProvided(R6Keys.ReplayRoot) && !Directory.Exists(ReadText(R6Keys.ReplayRoot, string.Empty)))
		{
			errors[R6Keys.ReplayRoot] = Strings.Errors.FieldInvalid();
		}

		return errors;
	}

	private Dictionary<string, LocalizedText> CheckLive()
	{
		var errors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (IsProvided(R6Keys.OverwolfPort))
		{
			var port = ReadNumber(R6Keys.OverwolfPort);
			if (port is null || port < 1024 || port > 65535)
			{
				errors[R6Keys.OverwolfPort] = Strings.Errors.FieldInvalid();
			}
		}

		return errors;
	}

	private bool IsProvided(string key) =>
		_input.TryGetValue(key, out var raw)
			&& raw is not null
			&& !(raw is string text && string.IsNullOrWhiteSpace(text));

	private R6Settings Collect()
	{
		var fallback = R6Settings.Default;
		return new R6Settings(
			ReplayRoot: ReadText(R6Keys.ReplayRoot, fallback.ReplayRoot).Trim(),
			WatchEnabled: ReadBool(R6Keys.WatchEnabled) ?? fallback.WatchEnabled,
			OverwolfEnabled: ReadBool(R6Keys.OverwolfEnabled) ?? fallback.OverwolfEnabled,
			OverwolfPort: (int)Math.Round(ReadNumber(R6Keys.OverwolfPort) ?? fallback.OverwolfPort),
			OverwolfToken: ReadText(R6Keys.OverwolfToken, fallback.OverwolfToken),
			KillEvents: ReadBool(R6Keys.KillEvents) ?? fallback.KillEvents,
			RoundEvents: ReadBool(R6Keys.RoundEvents) ?? fallback.RoundEvents,
			MatchEvents: ReadBool(R6Keys.MatchEvents) ?? fallback.MatchEvents,
			StreakEvents: ReadBool(R6Keys.StreakEvents) ?? fallback.StreakEvents);
	}

	private bool? ReadBool(string key)
	{
		if (!_input.TryGetValue(key, out var raw) || raw is null)
		{
			return null;
		}

		return raw switch
		{
			bool b => b,
			string s when bool.TryParse(s, out var parsed) => parsed,
			_ => null,
		};
	}

	private double? ReadNumber(string key)
	{
		if (!_input.TryGetValue(key, out var raw) || raw is null)
		{
			return null;
		}

		if (raw is string text && string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		double? value = raw switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => null,
		};
		return value is { } finite && double.IsFinite(finite) ? finite : null;
	}

	private string ReadText(string key, string fallback) =>
		_input.TryGetValue(key, out var raw) ? raw?.ToString() ?? fallback : fallback;

	private static ConfigFlowStep ReplaysStepDefinition() => new()
	{
		StepId = ReplaysStep,
		Title = Strings.Config.Replays.Title(),
		Description = Strings.Config.Replays.Description(),
		Fields =
		[
			Text(R6Keys.ReplayRoot, Strings.Config.Replays.Folder.Label(), Strings.Config.Replays.Folder.Description(), string.Empty),
			Toggle(R6Keys.WatchEnabled, Strings.Config.Replays.Watch.Label(), Strings.Config.Replays.Watch.Description(), R6Settings.Default.WatchEnabled),
		],
	};

	private static ConfigFlowStep LiveStepDefinition() => new()
	{
		StepId = LiveStep,
		Title = Strings.Config.Live.Title(),
		Description = Strings.Config.Live.Description(),
		Fields =
		[
			Toggle(R6Keys.OverwolfEnabled, Strings.Config.Live.Overwolf.Label(), Strings.Config.Live.Overwolf.Description(), R6Settings.Default.OverwolfEnabled),
			Number(R6Keys.OverwolfPort, Strings.Config.Live.Port.Label(), Strings.Config.Live.Port.Description(), 1024, 65535, 1, R6Settings.Default.OverwolfPort),
			Text(R6Keys.OverwolfToken, Strings.Config.Live.Token.Label(), Strings.Config.Live.Token.Description(), string.Empty),
		],
	};

	private static ConfigFlowStep EventsStepDefinition() => new()
	{
		StepId = EventsStep,
		Title = Strings.Config.Events.Title(),
		Description = Strings.Config.Events.Description(),
		Fields =
		[
			Toggle(R6Keys.KillEvents, Strings.Config.Events.Kills.Label(), Strings.Config.Events.Kills.Description(), R6Settings.Default.KillEvents),
			Toggle(R6Keys.RoundEvents, Strings.Config.Events.Rounds.Label(), Strings.Config.Events.Rounds.Description(), R6Settings.Default.RoundEvents),
			Toggle(R6Keys.MatchEvents, Strings.Config.Events.Matches.Label(), Strings.Config.Events.Matches.Description(), R6Settings.Default.MatchEvents),
			Toggle(R6Keys.StreakEvents, Strings.Config.Events.Streaks.Label(), Strings.Config.Events.Streaks.Description(), R6Settings.Default.StreakEvents),
		],
	};

	private static ActionParameter Text(string name, LocalizedText label, LocalizedText description, string defaultValue) =>
		new() { Name = name, Type = ActionParameterType.String, Label = label, Description = description, DefaultValue = defaultValue };

	private static ActionParameter Number(string name, LocalizedText label, LocalizedText description, double min, double max, double step, double defaultValue) =>
		new() { Name = name, Type = ActionParameterType.Number, Label = label, Description = description, Min = min, Max = max, Step = step, DefaultValue = defaultValue };

	private static ActionParameter Toggle(string name, LocalizedText label, LocalizedText description, bool defaultValue) =>
		new() { Name = name, Type = ActionParameterType.Boolean, Label = label, Description = description, DefaultValue = defaultValue };
}

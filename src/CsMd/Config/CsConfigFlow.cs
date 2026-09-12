using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace CsMd.Config;

public sealed class CsConfigFlow : IConfigFlow
{
	private const string ConnectionStep = "connection";
	private const string PlayerStep = "player";
	private const string EventsStep = "events";

	private readonly Dictionary<string, object?> _input = new(StringComparer.Ordinal);

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
		Task.FromResult(ConfigFlowResult.Step(ConnectionStepDefinition()));

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
			ConnectionStep => Validate(ConnectionStepDefinition(), CheckConnection(), PlayerStepDefinition()),
			PlayerStep => Validate(PlayerStepDefinition(), CheckPlayer(), EventsStepDefinition()),
			EventsStep => SubmitEvents(),
			_ => ConfigFlowResult.Error(ConnectionStepDefinition(), Strings.Errors.InvalidSettings(), new Dictionary<string, LocalizedText>()),
		});
	}

	private ConfigFlowResult SubmitEvents() =>
		ConfigFlowResult.Complete("CS:MD", Collect().ToValues());

	private static ConfigFlowResult Validate(
		ConfigFlowStep step,
		Dictionary<string, LocalizedText> errors,
		ConfigFlowStep next)
	{
		return errors.Count > 0
			? ConfigFlowResult.Error(step, Strings.Errors.InvalidSettings(), errors)
			: ConfigFlowResult.Step(next);
	}

	private Dictionary<string, LocalizedText> CheckConnection()
	{
		var errors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (IsProvided(CsKeys.Port))
		{
			var port = ReadNumber(CsKeys.Port);
			if (port is null || port < 1024 || port > 65535)
			{
				errors[CsKeys.Port] = Strings.Errors.FieldInvalid();
			}
		}

		return errors;
	}

	private Dictionary<string, LocalizedText> CheckPlayer()
	{
		var errors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (IsProvided(CsKeys.SteamId))
		{
			var id = ReadText(CsKeys.SteamId, string.Empty).Trim();
			if (id.Length is < 15 or > 20 || !id.All(char.IsAsciiDigit))
			{
				errors[CsKeys.SteamId] = Strings.Errors.FieldInvalid();
			}
		}

		return errors;
	}

	private bool IsProvided(string key) =>
		_input.TryGetValue(key, out var raw)
			&& raw is not null
			&& !(raw is string text && string.IsNullOrWhiteSpace(text));

	private CsSettings Collect()
	{
		var fallback = CsSettings.Default;
		return new CsSettings(
			Port: (int)Math.Round(ReadNumber(CsKeys.Port) ?? fallback.Port),
			AuthToken: ReadText(CsKeys.Token, fallback.AuthToken),
			PlayerSteamId: ReadText(CsKeys.SteamId, fallback.PlayerSteamId).Trim(),
			KillEvents: ReadBool(CsKeys.KillEvents) ?? fallback.KillEvents,
			DeathEvents: ReadBool(CsKeys.DeathEvents) ?? fallback.DeathEvents,
			RoundEvents: ReadBool(CsKeys.RoundEvents) ?? fallback.RoundEvents,
			BombEvents: ReadBool(CsKeys.BombEvents) ?? fallback.BombEvents,
			MatchEvents: ReadBool(CsKeys.MatchEvents) ?? fallback.MatchEvents);
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
			string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => null,
		};
		return value is { } finite && double.IsFinite(finite) ? finite : null;
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

	private string ReadText(string key, string fallback) =>
		_input.TryGetValue(key, out var raw) ? raw?.ToString() ?? fallback : fallback;

	private static ConfigFlowStep ConnectionStepDefinition() => new()
	{
		StepId = ConnectionStep,
		Title = Strings.Config.Connection.Title(),
		Description = Strings.Config.Connection.Description(),
		Fields =
		[
			Number(CsKeys.Port, Strings.Config.Connection.Port.Label(), Strings.Config.Connection.Port.Description(), 1024, 65535, 1, CsSettings.Default.Port),
			Text(CsKeys.Token, Strings.Config.Connection.Token.Label(), Strings.Config.Connection.Token.Description(), string.Empty),
		],
	};

	private static ConfigFlowStep PlayerStepDefinition() => new()
	{
		StepId = PlayerStep,
		Title = Strings.Config.Player.Title(),
		Description = Strings.Config.Player.Description(),
		Fields =
		[
			Text(CsKeys.SteamId, Strings.Config.Player.SteamId.Label(), Strings.Config.Player.SteamId.Description(), string.Empty),
		],
	};

	private static ConfigFlowStep EventsStepDefinition() => new()
	{
		StepId = EventsStep,
		Title = Strings.Config.Events.Title(),
		Description = Strings.Config.Events.Description(),
		Fields =
		[
			Toggle(CsKeys.KillEvents, Strings.Config.Events.Kills.Label(), Strings.Config.Events.Kills.Description(), CsSettings.Default.KillEvents),
			Toggle(CsKeys.DeathEvents, Strings.Config.Events.Deaths.Label(), Strings.Config.Events.Deaths.Description(), CsSettings.Default.DeathEvents),
			Toggle(CsKeys.RoundEvents, Strings.Config.Events.Rounds.Label(), Strings.Config.Events.Rounds.Description(), CsSettings.Default.RoundEvents),
			Toggle(CsKeys.BombEvents, Strings.Config.Events.Bombs.Label(), Strings.Config.Events.Bombs.Description(), CsSettings.Default.BombEvents),
			Toggle(CsKeys.MatchEvents, Strings.Config.Events.Matches.Label(), Strings.Config.Events.Matches.Description(), CsSettings.Default.MatchEvents),
		],
	};

	private static ActionParameter Text(string name, LocalizedText label, LocalizedText description, string defaultValue) =>
		new() { Name = name, Type = ActionParameterType.String, Label = label, Description = description, DefaultValue = defaultValue };

	private static ActionParameter Number(string name, LocalizedText label, LocalizedText description, double min, double max, double step, double defaultValue) =>
		new() { Name = name, Type = ActionParameterType.Number, Label = label, Description = description, Min = min, Max = max, Step = step, DefaultValue = defaultValue, Placeholder = Strings.Config.DefaultValueHint(defaultValue.ToString(CultureInfo.InvariantCulture)) };

	private static ActionParameter Toggle(string name, LocalizedText label, LocalizedText description, bool defaultValue) =>
		new() { Name = name, Type = ActionParameterType.Boolean, Label = label, Description = description, DefaultValue = defaultValue };
}

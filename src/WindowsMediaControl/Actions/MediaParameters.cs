using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Config;

namespace WindowsMediaControl.Actions;

internal static class MediaParameters
{
	public const string AppParameter = "app";

	public static ActionParameter AppOption() => new()
	{
		Name = AppParameter,
		Type = ActionParameterType.String,
		Label = Strings.Params.App.Label(),
		Description = Strings.Params.App.Description(),
		Required = false,
	};

	public static string? ReadApp(IReadOnlyDictionary<string, object> parameters)
	{
		if (!parameters.TryGetValue(AppParameter, out var raw))
		{
			return null;
		}

		var text = raw?.ToString();
		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	public static string? ReadApp(IReadOnlyDictionary<string, object> parameters, MediaSettingsProvider settings)
	{
		var direct = ReadApp(parameters);
		if (!string.IsNullOrWhiteSpace(direct))
		{
			return direct;
		}

		var preferred = settings.Current.PreferredApp;
		return string.IsNullOrWhiteSpace(preferred) ? null : preferred.Trim();
	}

	public static double? ReadNumber(IReadOnlyDictionary<string, object> parameters, string name)
	{
		if (!parameters.TryGetValue(name, out var raw) || raw is null)
		{
			return null;
		}

		return raw switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => null,
		};
	}
}

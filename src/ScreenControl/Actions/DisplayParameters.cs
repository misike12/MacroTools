using MacroDeck.Sdk.Actions;

namespace ScreenControl.Actions;

internal static class DisplayParameters
{
	public const string MonitorParameter = "monitor";
	public const string WindowParameter = "window";
	public const string BrightnessParameter = "brightness";
	public const string InputParameter = "input";
	public const string PowerParameter = "mode";

	public static ActionParameter MonitorOption() => new()
	{
		Name = MonitorParameter,
		Type = ActionParameterType.Number,
		Label = Strings.Params.Monitor.Label(),
		Description = Strings.Params.Monitor.Description(),
		Min = 1,
		Max = 9,
		Step = 1,
		DefaultValue = 1.0,
	};

	public static ActionParameter WindowOption(bool required) => new()
	{
		Name = WindowParameter,
		Type = ActionParameterType.String,
		Label = Strings.Params.Window.Label(),
		Description = Strings.Params.Window.Description(),
		Required = required,
	};

	public static int ReadMonitor(IReadOnlyDictionary<string, object> parameters) =>
		ReadMonitorCore(parameters.TryGetValue(MonitorParameter, out var raw) ? raw : null);

	public static string? ReadWindow(IReadOnlyDictionary<string, object> parameters) =>
		ReadWindowCore(parameters.TryGetValue(WindowParameter, out var raw) ? raw : null);

	// State providers answer from IReadOnlyDictionary<string, object?>; the
	// readers above take non-nullable values, so nulls are dropped first.
	// A dropped value reads exactly like a missing one (defaults apply).
	public static Dictionary<string, object> WithoutNulls(IReadOnlyDictionary<string, object?> parameters)
	{
		var clean = new Dictionary<string, object>();
		foreach (var (key, value) in parameters)
		{
			if (value is not null)
			{
				clean[key] = value;
			}
		}

		return clean;
	}

	private static int ReadMonitorCore(object? raw)
	{
		if (raw is not null)
		{
			try
			{
				var value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
				if (!double.IsNaN(value) && !double.IsInfinity(value))
				{
					return Math.Clamp((int)value, 1, 9);
				}
			}
			catch (Exception)
			{
			}
		}

		return 1;
	}

	private static string? ReadWindowCore(object? raw)
	{
		var text = raw?.ToString();
		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	public static double? ReadNumber(IReadOnlyDictionary<string, object> parameters, string name) =>
		parameters.TryGetValue(name, out var raw) ? ReadNumberValue(raw) : null;

	public static double? ReadNumberValue(object? raw)
	{
		if (raw is null)
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
}

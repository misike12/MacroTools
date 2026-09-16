using MacroDeck.Sdk.ConfigFlow;

namespace R6Md.Config;

public static class R6Keys
{
	public const string ReplayRoot = "replay-root";
	public const string WatchEnabled = "watch-enabled";
	public const string KillEvents = "events-kill";
	public const string RoundEvents = "events-round";
	public const string MatchEvents = "events-match";
	public const string StreakEvents = "events-streak";
}

public sealed record R6Settings(
	string ReplayRoot,
	bool WatchEnabled,
	bool KillEvents,
	bool RoundEvents,
	bool MatchEvents,
	bool StreakEvents)
{
	public static R6Settings Default { get; } = new(
		ReplayRoot: string.Empty,
		WatchEnabled: true,
		KillEvents: true,
		RoundEvents: true,
		MatchEvents: true,
		StreakEvents: true);
}

public sealed class R6SettingsProvider
{
	private readonly object _gate = new();
	private R6Settings _current = R6Settings.Default;

	public R6Settings Current
	{
		get
		{
			lock (_gate)
			{
				return _current;
			}
		}
	}

	public void Update(R6Settings settings)
	{
		lock (_gate)
		{
			_current = settings;
		}
	}
}

public static class R6SettingsReader
{
	public static async Task<R6Settings> ReadAsync(MacroDeck.Sdk.ConfigFlow.IIntegrationConfig config)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		MacroDeck.Sdk.ConfigFlow.ConfigEntrySnapshot? entry;
		try
		{
			var entries = await config.GetEntriesAsync(timeout.Token);
			entry = entries.Count > 0 ? entries[0] : null;
		}
		catch (Exception)
		{
			return R6Settings.Default;
		}

		if (entry is null)
		{
			return R6Settings.Default;
		}

		var fallback = R6Settings.Default;
		return new R6Settings(
			ReplayRoot: await ReadTextAsync(config, entry.Id, R6Keys.ReplayRoot, fallback.ReplayRoot, timeout.Token),
			WatchEnabled: await ReadBoolAsync(config, entry.Id, R6Keys.WatchEnabled, fallback.WatchEnabled, timeout.Token),
			KillEvents: await ReadBoolAsync(config, entry.Id, R6Keys.KillEvents, fallback.KillEvents, timeout.Token),
			RoundEvents: await ReadBoolAsync(config, entry.Id, R6Keys.RoundEvents, fallback.RoundEvents, timeout.Token),
			MatchEvents: await ReadBoolAsync(config, entry.Id, R6Keys.MatchEvents, fallback.MatchEvents, timeout.Token),
			StreakEvents: await ReadBoolAsync(config, entry.Id, R6Keys.StreakEvents, fallback.StreakEvents, timeout.Token));
	}

	private static async Task<bool> ReadBoolAsync(
		MacroDeck.Sdk.ConfigFlow.IIntegrationConfig config, Guid entryId, string key, bool fallback, CancellationToken cancellationToken)
	{
		var raw = await ReadTextAsync(config, entryId, key, string.Empty, cancellationToken);
		return bool.TryParse(raw, out var value) ? value : fallback;
	}

	private static async Task<string> ReadTextAsync(
		MacroDeck.Sdk.ConfigFlow.IIntegrationConfig config, Guid entryId, string key, string fallback, CancellationToken cancellationToken)
	{
		try
		{
			return await config.GetStringAsync(entryId, key, cancellationToken) ?? fallback;
		}
		catch (Exception)
		{
			return fallback;
		}
	}
}

internal static class R6SettingsValues
{
	public static IReadOnlyDictionary<string, MacroDeck.Sdk.ConfigFlow.ConfigFlowValue> ToValues(this R6Settings settings) =>
		new Dictionary<string, MacroDeck.Sdk.ConfigFlow.ConfigFlowValue>(StringComparer.Ordinal)
		{
			[R6Keys.ReplayRoot] = MacroDeck.Sdk.ConfigFlow.ConfigFlowValue.Plain(settings.ReplayRoot),
			[R6Keys.WatchEnabled] = Plain(settings.WatchEnabled),
			[R6Keys.KillEvents] = Plain(settings.KillEvents),
			[R6Keys.RoundEvents] = Plain(settings.RoundEvents),
			[R6Keys.MatchEvents] = Plain(settings.MatchEvents),
			[R6Keys.StreakEvents] = Plain(settings.StreakEvents),
		};

	private static MacroDeck.Sdk.ConfigFlow.ConfigFlowValue Plain(bool value) =>
		MacroDeck.Sdk.ConfigFlow.ConfigFlowValue.Plain(value ? "true" : "false");
}

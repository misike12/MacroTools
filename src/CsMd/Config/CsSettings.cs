using System.Globalization;
using MacroDeck.Sdk.ConfigFlow;

namespace CsMd.Config;

public static class CsKeys
{
	public const string Port = "port";
	public const string Token = "token";
	public const string SteamId = "steam-id";
	public const string KillEvents = "events-kill";
	public const string DeathEvents = "events-death";
	public const string RoundEvents = "events-round";
	public const string BombEvents = "events-bomb";
	public const string MatchEvents = "events-match";
	public const string Reset = "reset";
}

public sealed record CsSettings(
	int Port,
	string AuthToken,
	string PlayerSteamId,
	bool KillEvents,
	bool DeathEvents,
	bool RoundEvents,
	bool BombEvents,
	bool MatchEvents)
{
	public static CsSettings Default { get; } = new(
		Port: 32075,
		AuthToken: string.Empty,
		PlayerSteamId: string.Empty,
		KillEvents: true,
		DeathEvents: true,
		RoundEvents: true,
		BombEvents: true,
		MatchEvents: true);
}

public sealed class CsSettingsProvider
{
	private readonly object _gate = new();
	private CsSettings _current = CsSettings.Default;

	public CsSettings Current
	{
		get
		{
			lock (_gate)
			{
				return _current;
			}
		}
	}

	public void Update(CsSettings settings)
	{
		lock (_gate)
		{
			_current = settings;
		}
	}
}

public static class CsSettingsReader
{
	public static async Task<CsSettings> ReadAsync(IIntegrationConfig config)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		ConfigEntrySnapshot? entry;
		try
		{
			var entries = await config.GetEntriesAsync(timeout.Token);
			entry = entries.Count > 0 ? entries[0] : null;
		}
		catch (Exception)
		{
			return CsSettings.Default;
		}

		if (entry is null)
		{
			return CsSettings.Default;
		}

		var fallback = CsSettings.Default;
		return new CsSettings(
			Port: ClampInt(await ReadNumberAsync(config, entry.Id, CsKeys.Port, fallback.Port, timeout.Token), 1024, 65535, fallback.Port),
			AuthToken: await ReadSecretAsync(config, entry.Id, CsKeys.Token, timeout.Token),
			PlayerSteamId: await ReadTextAsync(config, entry.Id, CsKeys.SteamId, fallback.PlayerSteamId, timeout.Token),
			KillEvents: await ReadBoolAsync(config, entry.Id, CsKeys.KillEvents, fallback.KillEvents, timeout.Token),
			DeathEvents: await ReadBoolAsync(config, entry.Id, CsKeys.DeathEvents, fallback.DeathEvents, timeout.Token),
			RoundEvents: await ReadBoolAsync(config, entry.Id, CsKeys.RoundEvents, fallback.RoundEvents, timeout.Token),
			BombEvents: await ReadBoolAsync(config, entry.Id, CsKeys.BombEvents, fallback.BombEvents, timeout.Token),
			MatchEvents: await ReadBoolAsync(config, entry.Id, CsKeys.MatchEvents, fallback.MatchEvents, timeout.Token));
	}

	private static int ClampInt(double value, int min, int max, int fallback)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return fallback;
		}

		return Math.Clamp((int)Math.Round(value), min, max);
	}

	private static async Task<double> ReadNumberAsync(
		IIntegrationConfig config, Guid entryId, string key, double fallback, CancellationToken cancellationToken)
	{
		var raw = await ReadTextAsync(config, entryId, key, string.Empty, cancellationToken);
		return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : fallback;
	}

	private static async Task<bool> ReadBoolAsync(
		IIntegrationConfig config, Guid entryId, string key, bool fallback, CancellationToken cancellationToken)
	{
		var raw = await ReadTextAsync(config, entryId, key, string.Empty, cancellationToken);
		return bool.TryParse(raw, out var value) ? value : fallback;
	}

	private static async Task<string> ReadSecretAsync(
		IIntegrationConfig config, Guid entryId, string key, CancellationToken cancellationToken)
	{
		try
		{
			return await config.GetSecretAsync(entryId, key, cancellationToken) ?? string.Empty;
		}
		catch (Exception)
		{
			return string.Empty;
		}
	}

	private static async Task<string> ReadTextAsync(
		IIntegrationConfig config, Guid entryId, string key, string fallback, CancellationToken cancellationToken)
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

internal static class CsSettingsValues
{
	public static IReadOnlyDictionary<string, ConfigFlowValue> ToValues(this CsSettings settings) =>
		new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[CsKeys.Port] = ConfigFlowValue.Plain(settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
			[CsKeys.Token] = ConfigFlowValue.Secret(settings.AuthToken),
			[CsKeys.SteamId] = ConfigFlowValue.Plain(settings.PlayerSteamId),
			[CsKeys.KillEvents] = Plain(settings.KillEvents),
			[CsKeys.DeathEvents] = Plain(settings.DeathEvents),
			[CsKeys.RoundEvents] = Plain(settings.RoundEvents),
			[CsKeys.BombEvents] = Plain(settings.BombEvents),
			[CsKeys.MatchEvents] = Plain(settings.MatchEvents),
		};

	private static ConfigFlowValue Plain(bool value) =>
		ConfigFlowValue.Plain(value ? "true" : "false");
}

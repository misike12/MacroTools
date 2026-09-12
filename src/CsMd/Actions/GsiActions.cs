using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using CsMd.Config;
using CsMd.Gsi;

namespace CsMd.Actions;

internal static class CsActionResults
{
	public static ActionResult ListenerDown() =>
		ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Errors.ListenerNotRunning());

	public static ActionResult ProviderError() =>
		ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.CommandFailed());
}

public sealed class InstallGsiConfigAction(CsSettingsProvider settings, GsiService gsi) : IActionDefinition
{
	public string Id => "install-gsi-config";
	public LocalizedText Name => Strings.Actions.InstallGsiConfig.Name();
	public LocalizedText Description => Strings.Actions.InstallGsiConfig.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(settings, gsi);

	private sealed class Executor(CsSettingsProvider settings, GsiService gsi) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var current = settings.Current;
				var (ok, detail) = GsiConfig.Install(current.Port, current.AuthToken);
				if (!ok)
				{
					return Task.FromResult(detail == "not-found"
						? ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.CsNotFound())
						: ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.ConfigWriteFailed()));
				}

				gsi.Start(current.Port, current.AuthToken);
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(CsActionResults.ProviderError());
			}
		}
	}
}

public sealed class ResetSessionStatsAction(GsiService gsi) : IActionDefinition
{
	public string Id => "reset-session-stats";
	public LocalizedText Name => Strings.Actions.ResetSessionStats.Name();
	public LocalizedText Description => Strings.Actions.ResetSessionStats.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(gsi);

	private sealed class Executor(GsiService gsi) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				gsi.ResetSessionStats();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(CsActionResults.ProviderError());
			}
		}
	}
}

public sealed class SimulateMatchAction(GsiService gsi) : IActionDefinition
{
	public string Id => "simulate-match";
	public LocalizedText Name => Strings.Actions.SimulateMatch.Name();
	public LocalizedText Description => Strings.Actions.SimulateMatch.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;
	public IActionExecutor CreateExecutor() => new Executor(gsi);

	private sealed class Executor(GsiService gsi) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				gsi.InjectTestState();
				return Task.FromResult(ActionResult.Success());
			}
			catch (Exception)
			{
				return Task.FromResult(CsActionResults.ProviderError());
			}
		}
	}
}

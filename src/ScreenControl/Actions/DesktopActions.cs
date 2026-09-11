using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScreenControl.Windows;

namespace ScreenControl.Actions;

public sealed class NextDesktopAction(IWindowService windows) : IActionDefinition
{
	public string Id => "next-desktop";
	public LocalizedText Name => Strings.Actions.NextDesktop.Name();
	public LocalizedText Description => Strings.Actions.NextDesktop.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return Task.FromResult(windows.NextDesktop()
					? ActionResult.Success()
					: DisplayActionResults.ProviderError());
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class PreviousDesktopAction(IWindowService windows) : IActionDefinition
{
	public string Id => "previous-desktop";
	public LocalizedText Name => Strings.Actions.PreviousDesktop.Name();
	public LocalizedText Description => Strings.Actions.PreviousDesktop.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return Task.FromResult(windows.PreviousDesktop()
					? ActionResult.Success()
					: DisplayActionResults.ProviderError());
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

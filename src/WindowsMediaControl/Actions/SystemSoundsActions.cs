using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

public sealed class MuteSystemSoundsAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "mute-system-sounds";
	public LocalizedText Name => Strings.Actions.MuteSystemSounds.Name();
	public LocalizedText Description => Strings.Actions.MuteSystemSounds.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await media.SetSystemSoundsMuteAsync(true, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.NoSystemSoundsSession());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class UnmuteSystemSoundsAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "unmute-system-sounds";
	public LocalizedText Name => Strings.Actions.UnmuteSystemSounds.Name();
	public LocalizedText Description => Strings.Actions.UnmuteSystemSounds.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await media.SetSystemSoundsMuteAsync(false, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.NoSystemSoundsSession());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class ToggleSystemSoundsAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "toggle-system-sounds";
	public LocalizedText Name => Strings.Actions.ToggleSystemSounds.Name();
	public LocalizedText Description => Strings.Actions.ToggleSystemSounds.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await media.ToggleSystemSoundsMuteAsync(context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.NoSystemSoundsSession());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

public sealed class SetMicVolumeAction(IMediaControlService media) : IActionDefinition
{
	private const string VolumeParameter = "volume";

	public string Id => "set-mic-volume";
	public LocalizedText Name => Strings.Actions.SetMicVolume.Name();
	public LocalizedText Description => Strings.Actions.SetMicVolume.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = VolumeParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.SetVolume.Volume.Label(),
			Description = Strings.Actions.SetVolume.Volume.Description(),
			Min = 0,
			Max = 100,
			Step = 1,
			DefaultValue = 50.0,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var volume = MediaParameters.ReadNumberOrDefault(context.Parameters, VolumeParameter, 50.0);
			if (volume is null || volume < 0 || volume > 100)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetVolume.Volume.Label());
			}

			try
			{
				await media.SetMicVolumeAsync((int)volume.Value, context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class MuteMicAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "mute-mic";
	public LocalizedText Name => Strings.Actions.MuteMic.Name();
	public LocalizedText Description => Strings.Actions.MuteMic.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				await media.MuteMicAsync(context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class UnmuteMicAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "unmute-mic";
	public LocalizedText Name => Strings.Actions.UnmuteMic.Name();
	public LocalizedText Description => Strings.Actions.UnmuteMic.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				await media.UnmuteMicAsync(context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class ToggleMicMuteAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "toggle-mic-mute";
	public LocalizedText Name => Strings.Actions.ToggleMicMute.Name();
	public LocalizedText Description => Strings.Actions.ToggleMicMute.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				await media.ToggleMicMuteAsync(context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

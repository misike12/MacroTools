using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

public sealed class SetOutputDeviceAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "set-output-device";
	public LocalizedText Name => Strings.Actions.SetOutputDevice.Name();
	public LocalizedText Description => Strings.Actions.SetOutputDevice.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = "device",
			Type = ActionParameterType.String,
			Label = Strings.Actions.SetOutputDevice.Device.Label(),
			Description = Strings.Actions.SetOutputDevice.Device.Description(),
			Required = true,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var device = context.Parameters.TryGetValue("device", out var raw) ? raw?.ToString()?.Trim() : null;
			if (string.IsNullOrWhiteSpace(device))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetOutputDevice.Device.Label());
			}

			try
			{
				return await media.SetDefaultDeviceAsync(device, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.AudioDeviceNotFound());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class CycleOutputDeviceAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "cycle-output-device";
	public LocalizedText Name => Strings.Actions.CycleOutputDevice.Name();
	public LocalizedText Description => Strings.Actions.CycleOutputDevice.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await media.CycleDefaultDeviceAsync(context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Errors.NoAudioDevices());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

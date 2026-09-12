using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

public sealed class VolumeUpAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	private const string StepParameter = "step";

	public string Id => "volume-up";
	public LocalizedText Name => Strings.Actions.VolumeUp.Name();
	public LocalizedText Description => Strings.Actions.VolumeUp.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = StepParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.VolumeUp.Step.Label(),
			Description = Strings.Actions.VolumeUp.Step.Description(),
			Min = 1,
			Max = 50,
			Step = 1,
			DefaultValue = 5.0,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var step = MediaParameters.ReadNumberOrDefault(
				context.Parameters, StepParameter, settings.Current.DefaultVolumeStep);
			if (step is null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.VolumeUp.Step.Label());
			}

			if (step == 0)
			{
				return ActionResult.Success();
			}

			try
			{
				await media.VolumeUpAsync((int)step.Value, context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class VolumeDownAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	private const string StepParameter = "step";

	public string Id => "volume-down";
	public LocalizedText Name => Strings.Actions.VolumeDown.Name();
	public LocalizedText Description => Strings.Actions.VolumeDown.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = StepParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.VolumeDown.Step.Label(),
			Description = Strings.Actions.VolumeDown.Step.Description(),
			Min = 1,
			Max = 50,
			Step = 1,
			DefaultValue = 5.0,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var step = MediaParameters.ReadNumberOrDefault(
				context.Parameters, StepParameter, settings.Current.DefaultVolumeStep);
			if (step is null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.VolumeDown.Step.Label());
			}

			if (step == 0)
			{
				return ActionResult.Success();
			}

			try
			{
				await media.VolumeDownAsync((int)step.Value, context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class SetVolumeAction(IMediaControlService media) : IActionDefinition
{
	private const string VolumeParameter = "volume";

	public string Id => "set-volume";
	public LocalizedText Name => Strings.Actions.SetVolume.Name();
	public LocalizedText Description => Strings.Actions.SetVolume.Description();
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
				await media.SetVolumeAsync((int)volume.Value, context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class MuteAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "mute";
	public LocalizedText Name => Strings.Actions.Mute.Name();
	public LocalizedText Description => Strings.Actions.Mute.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				await media.MuteAsync(context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class UnmuteAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "unmute";
	public LocalizedText Name => Strings.Actions.Unmute.Name();
	public LocalizedText Description => Strings.Actions.Unmute.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				await media.UnmuteAsync(context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class ToggleMuteAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "toggle-mute";
	public LocalizedText Name => Strings.Actions.ToggleMute.Name();
	public LocalizedText Description => Strings.Actions.ToggleMute.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				await media.ToggleMuteAsync(context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

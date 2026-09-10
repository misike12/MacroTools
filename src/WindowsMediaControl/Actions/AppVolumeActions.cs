using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

public sealed class SetAppVolumeAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "set-app-volume";
	public LocalizedText Name => Strings.Actions.SetAppVolume.Name();
	public LocalizedText Description => Strings.Actions.SetAppVolume.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = MediaParameters.AppParameter,
			Type = ActionParameterType.String,
			Label = Strings.Params.App.Label(),
			Description = Strings.Actions.SetAppVolume.App.Description(),
			Required = true,
		},
		new ActionParameter
		{
			Name = "volume",
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
			var app = MediaParameters.ReadApp(context.Parameters);
			var volume = context.Parameters.TryGetValue("volume", out var raw)
				? MediaParameters.ReadNumberValue(raw)
				: 50.0;
			if (string.IsNullOrWhiteSpace(app))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Params.App.Label());
			}

			if (volume is null || volume < 0 || volume > 100)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetVolume.Volume.Label());
			}

			try
			{
				return await media.SetAppVolumeAsync(app, (int)volume.Value, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.AppAudioSessionNotFound());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class AdjustAppVolumeAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "adjust-app-volume";
	public LocalizedText Name => Strings.Actions.AdjustAppVolume.Name();
	public LocalizedText Description => Strings.Actions.AdjustAppVolume.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = MediaParameters.AppParameter,
			Type = ActionParameterType.String,
			Label = Strings.Params.App.Label(),
			Description = Strings.Actions.AdjustAppVolume.App.Description(),
			Required = true,
		},
		new ActionParameter
		{
			Name = "delta",
			Type = ActionParameterType.Number,
			Label = Strings.Actions.AdjustAppVolume.Delta.Label(),
			Description = Strings.Actions.AdjustAppVolume.Delta.Description(),
			Min = -50,
			Max = 50,
			Step = 1,
			DefaultValue = 5.0,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var app = MediaParameters.ReadApp(context.Parameters);
			var delta = context.Parameters.TryGetValue("delta", out var raw)
				? MediaParameters.ReadNumberValue(raw)
				: 5.0;
			if (string.IsNullOrWhiteSpace(app))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Params.App.Label());
			}

			if (delta is null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.AdjustAppVolume.Delta.Label());
			}

			if (delta == 0)
			{
				return ActionResult.Success();
			}

			try
			{
				return await media.AdjustAppVolumeAsync(app, (int)delta.Value, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.AppAudioSessionNotFound());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class MuteAppAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "mute-app";
	public LocalizedText Name => Strings.Actions.MuteApp.Name();
	public LocalizedText Description => Strings.Actions.MuteApp.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = MediaParameters.AppParameter,
			Type = ActionParameterType.String,
			Label = Strings.Params.App.Label(),
			Description = Strings.Actions.MuteApp.App.Description(),
			Required = true,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var app = MediaParameters.ReadApp(context.Parameters);
			if (string.IsNullOrWhiteSpace(app))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Params.App.Label());
			}

			try
			{
				return await media.SetAppMuteAsync(app, true, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.AppAudioSessionNotFound());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class UnmuteAppAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "unmute-app";
	public LocalizedText Name => Strings.Actions.UnmuteApp.Name();
	public LocalizedText Description => Strings.Actions.UnmuteApp.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = MediaParameters.AppParameter,
			Type = ActionParameterType.String,
			Label = Strings.Params.App.Label(),
			Description = Strings.Actions.UnmuteApp.App.Description(),
			Required = true,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var app = MediaParameters.ReadApp(context.Parameters);
			if (string.IsNullOrWhiteSpace(app))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Params.App.Label());
			}

			try
			{
				return await media.SetAppMuteAsync(app, false, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.AppAudioSessionNotFound());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class ToggleAppMuteAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "toggle-app-mute";
	public LocalizedText Name => Strings.Actions.ToggleAppMute.Name();
	public LocalizedText Description => Strings.Actions.ToggleAppMute.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = MediaParameters.AppParameter,
			Type = ActionParameterType.String,
			Label = Strings.Params.App.Label(),
			Description = Strings.Actions.ToggleAppMute.App.Description(),
			Required = true,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var app = MediaParameters.ReadApp(context.Parameters);
			if (string.IsNullOrWhiteSpace(app))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Params.App.Label());
			}

			try
			{
				return await media.ToggleAppMuteAsync(app, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.AppAudioSessionNotFound());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class FocusAppAction(IMediaControlService media) : IActionDefinition
{
	public string Id => "focus-app";
	public LocalizedText Name => Strings.Actions.FocusApp.Name();
	public LocalizedText Description => Strings.Actions.FocusApp.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = MediaParameters.AppParameter,
			Type = ActionParameterType.String,
			Label = Strings.Params.App.Label(),
			Description = Strings.Actions.FocusApp.App.Description(),
			Required = true,
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media);

	private sealed class Executor(IMediaControlService media) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var app = MediaParameters.ReadApp(context.Parameters);
			if (string.IsNullOrWhiteSpace(app))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Params.App.Label());
			}

			try
			{
				return await media.SoloAppAsync(app, context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.AppAudioSessionNotFound());
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

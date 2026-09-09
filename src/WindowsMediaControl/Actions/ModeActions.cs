using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

public sealed class ToggleShuffleAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "toggle-shuffle";
	public LocalizedText Name => Strings.Actions.ToggleShuffle.Name();
	public LocalizedText Description => Strings.Actions.ToggleShuffle.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [MediaParameters.AppOption()];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var app = MediaParameters.ReadApp(context.Parameters, settings);
				return await MediaActionResults.FromControlResult(media, await media.ToggleShuffleAsync(context.CancellationToken, app), context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class SetShuffleAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "set-shuffle";
	public LocalizedText Name => Strings.Actions.SetShuffle.Name();
	public LocalizedText Description => Strings.Actions.SetShuffle.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = "enabled",
			Type = ActionParameterType.Boolean,
			Label = Strings.Actions.SetShuffle.Enabled.Label(),
			Description = Strings.Actions.SetShuffle.Enabled.Description(),
			DefaultValue = true,
		},
		MediaParameters.AppOption(),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var enabled = context.Parameters.TryGetValue("enabled", out var raw)
				? MediaParameters.ReadBooleanValue(raw)
				: true;
			if (enabled is null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetShuffle.Enabled.Label());
			}

			try
			{
				var app = MediaParameters.ReadApp(context.Parameters, settings);
				return await MediaActionResults.FromControlResult(media, await media.SetShuffleAsync(enabled.Value, context.CancellationToken, app), context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class CycleRepeatAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "cycle-repeat";
	public LocalizedText Name => Strings.Actions.CycleRepeat.Name();
	public LocalizedText Description => Strings.Actions.CycleRepeat.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [MediaParameters.AppOption()];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var app = MediaParameters.ReadApp(context.Parameters, settings);
				return await MediaActionResults.FromControlResult(media, await media.CycleRepeatAsync(context.CancellationToken, app), context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class SetRepeatAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "set-repeat";
	public LocalizedText Name => Strings.Actions.SetRepeat.Name();
	public LocalizedText Description => Strings.Actions.SetRepeat.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = "mode",
			Type = ActionParameterType.Choice,
			Label = Strings.Actions.SetRepeat.Mode.Label(),
			Description = Strings.Actions.SetRepeat.Mode.Description(),
			Options =
			[
				new ActionParameterOption { Value = "off", Label = Strings.RepeatModes.Off() },
				new ActionParameterOption { Value = "all", Label = Strings.RepeatModes.All() },
				new ActionParameterOption { Value = "one", Label = Strings.RepeatModes.One() },
			],
			DefaultValue = "all",
		},
		MediaParameters.AppOption(),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var mode = context.Parameters.TryGetValue("mode", out var raw) ? raw?.ToString()?.ToLowerInvariant() : null;
			var repeat = mode switch
			{
				"off" => MediaRepeatMode.Off,
				"all" => MediaRepeatMode.All,
				"one" => MediaRepeatMode.One,
				_ => (MediaRepeatMode?)null,
			};
			if (repeat is null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetRepeat.Mode.Label());
			}

			try
			{
				var app = MediaParameters.ReadApp(context.Parameters, settings);
				return await MediaActionResults.FromControlResult(media, await media.SetRepeatAsync(repeat.Value, context.CancellationToken, app), context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

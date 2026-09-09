using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

public sealed class SeekForwardAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	private const string SecondsParameter = "seconds";

	public string Id => "seek-forward";
	public LocalizedText Name => Strings.Actions.SeekForward.Name();
	public LocalizedText Description => Strings.Actions.SeekForward.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = SecondsParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.SeekForward.Seconds.Label(),
			Description = Strings.Actions.SeekForward.Seconds.Description(),
			Min = 1,
			Max = 300,
			Step = 1,
			DefaultValue = 10.0,
		},
		MediaParameters.AppOption(),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var seconds = context.Parameters.ContainsKey(SecondsParameter)
				? MediaParameters.ReadNumber(context.Parameters, SecondsParameter)
				: settings.Current.DefaultSeekSeconds;
			if (seconds is null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SeekForward.Seconds.Label());
			}

			if (seconds == 0)
			{
				return ActionResult.Success();
			}

			try
			{
				return await MediaActionResults.FromControlResult(media, await media.SeekByAsync(TimeSpan.FromSeconds(seconds.Value), context.CancellationToken, MediaParameters.ReadApp(context.Parameters, settings)), context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class SeekBackwardAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	private const string SecondsParameter = "seconds";

	public string Id => "seek-backward";
	public LocalizedText Name => Strings.Actions.SeekBackward.Name();
	public LocalizedText Description => Strings.Actions.SeekBackward.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = SecondsParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.SeekBackward.Seconds.Label(),
			Description = Strings.Actions.SeekBackward.Seconds.Description(),
			Min = 1,
			Max = 300,
			Step = 1,
			DefaultValue = 10.0,
		},
		MediaParameters.AppOption(),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var seconds = context.Parameters.ContainsKey(SecondsParameter)
				? MediaParameters.ReadNumber(context.Parameters, SecondsParameter)
				: settings.Current.DefaultSeekSeconds;
			if (seconds is null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SeekBackward.Seconds.Label());
			}

			if (seconds == 0)
			{
				return ActionResult.Success();
			}

			try
			{
				return await MediaActionResults.FromControlResult(media, await media.SeekByAsync(TimeSpan.FromSeconds(-seconds.Value), context.CancellationToken, MediaParameters.ReadApp(context.Parameters, settings)), context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class SeekToAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	private const string PositionParameter = "position";

	public string Id => "seek-to";
	public LocalizedText Name => Strings.Actions.SeekTo.Name();
	public LocalizedText Description => Strings.Actions.SeekTo.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = PositionParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.SeekTo.Position.Label(),
			Description = Strings.Actions.SeekTo.Position.Description(),
			Min = 0,
			Max = 86400,
			Step = 1,
			DefaultValue = 0.0,
		},
		MediaParameters.AppOption(),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var position = context.Parameters.TryGetValue(PositionParameter, out var raw)
				? MediaParameters.ReadNumberValue(raw)
				: 0.0;
			if (position is null || position < 0)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SeekTo.Position.Label());
			}

			try
			{
				return await MediaActionResults.FromControlResult(media, await media.SeekToAsync(TimeSpan.FromSeconds(position.Value), context.CancellationToken, MediaParameters.ReadApp(context.Parameters, settings)), context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

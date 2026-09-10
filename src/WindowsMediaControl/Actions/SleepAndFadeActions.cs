using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

public sealed class SleepTimerAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	private const string MinutesParameter = "minutes";

	private readonly object _timerGate = new();
	private CancellationTokenSource? _pendingTimer;

	public string Id => "sleep-timer";
	public LocalizedText Name => Strings.Actions.SleepTimer.Name();
	public LocalizedText Description => Strings.Actions.SleepTimer.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = MinutesParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.SleepTimer.Minutes.Label(),
			Description = Strings.Actions.SleepTimer.Minutes.Description(),
			Min = -1,
			Max = 180,
			Step = 1,
			DefaultValue = 30.0,
		},
		MediaParameters.AppOption(),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings, this);

	internal void CancelPendingTimer()
	{
		lock (_timerGate)
		{
			_pendingTimer?.Cancel();
			_pendingTimer?.Dispose();
			_pendingTimer = null;
		}
	}

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings, SleepTimerAction owner) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var minutes = context.Parameters.TryGetValue(MinutesParameter, out var raw)
				? MediaParameters.ReadNumberValue(raw)
				: settings.Current.SleepDefaultMinutes;
			if (minutes is null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SleepTimer.Minutes.Label());
			}

			var app = MediaParameters.ReadApp(context.Parameters, settings);
			if (minutes < 0)
			{
				owner.CancelPendingTimer();
				return ActionResult.Success();
			}

			if (minutes == 0)
			{
				try
				{
					return await MediaActionResults.FromControlResult(media, await media.PauseAsync(context.CancellationToken, app), context.CancellationToken);
				}
				catch (Exception)
				{
					return MediaActionResults.ProviderError();
				}
			}

			owner.ArmTimer(media, minutes.Value, app);
			return ActionResult.Accepted(Strings.Actions.SleepTimer.Started());
		}
	}

	private void ArmTimer(IMediaControlService media, double minutes, string? app)
	{
		CancellationTokenSource timer;
		lock (_timerGate)
		{
			_pendingTimer?.Cancel();
			_pendingTimer?.Dispose();
			_pendingTimer = new CancellationTokenSource();
			timer = _pendingTimer;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(TimeSpan.FromMinutes(minutes), timer.Token);
				await media.PauseAsync(CancellationToken.None, app);
			}
			catch (Exception)
			{
			}
		}, CancellationToken.None);
	}
}

public sealed class FadeOutPauseAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	private const string SecondsParameter = "seconds";

	public string Id => "fade-out-pause";
	public LocalizedText Name => Strings.Actions.FadeOutPause.Name();
	public LocalizedText Description => Strings.Actions.FadeOutPause.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = SecondsParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.FadeOutPause.Seconds.Label(),
			Description = Strings.Actions.FadeOutPause.Seconds.Description(),
			Min = 0,
			Max = 60,
			Step = 1,
			DefaultValue = 3.0,
		},
		MediaParameters.AppOption(),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var seconds = context.Parameters.TryGetValue(SecondsParameter, out var raw)
				? MediaParameters.ReadNumberValue(raw)
				: settings.Current.FadeSeconds;
			if (seconds is null || seconds < 0)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.FadeOutPause.Seconds.Label());
			}

			try
			{
				var app = MediaParameters.ReadApp(context.Parameters, settings);
				var start = (await media.GetSnapshotAsync(context.CancellationToken)).VolumePercent ?? 50;
				await FadeRamp.RampAsync(media, start, 0, seconds.Value, context.CancellationToken);
				var paused = await media.PauseAsync(context.CancellationToken, app);
				try
				{
					await media.SetVolumeAsync(start, context.CancellationToken);
				}
				catch (Exception)
				{
				}

				return await MediaActionResults.FromControlResult(media, paused, context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class FadeInPlayAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	private const string SecondsParameter = "seconds";
	private const string TargetParameter = "target";

	public string Id => "fade-in-play";
	public LocalizedText Name => Strings.Actions.FadeInPlay.Name();
	public LocalizedText Description => Strings.Actions.FadeInPlay.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		new ActionParameter
		{
			Name = SecondsParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.FadeInPlay.Seconds.Label(),
			Description = Strings.Actions.FadeInPlay.Seconds.Description(),
			Min = 0,
			Max = 60,
			Step = 1,
			DefaultValue = 3.0,
		},
		new ActionParameter
		{
			Name = TargetParameter,
			Type = ActionParameterType.Number,
			Label = Strings.Actions.FadeInPlay.Target.Label(),
			Description = Strings.Actions.FadeInPlay.Target.Description(),
			Min = 0,
			Max = 100,
			Step = 1,
		},
		MediaParameters.AppOption(),
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var seconds = context.Parameters.TryGetValue(SecondsParameter, out var rawSeconds)
				? MediaParameters.ReadNumberValue(rawSeconds)
				: settings.Current.FadeSeconds;
			if (seconds is null || seconds < 0)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.FadeInPlay.Seconds.Label());
			}

			var target = MediaParameters.ReadNumber(context.Parameters, TargetParameter);
			if (target is not null && (target < 0 || target > 100))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.FadeInPlay.Target.Label());
			}

			try
			{
				var app = MediaParameters.ReadApp(context.Parameters, settings);
				var end = (int)Math.Round(target
					?? (await media.GetSnapshotAsync(context.CancellationToken)).VolumePercent
					?? 50);
				if (end <= 0)
				{
					end = 50;
				}

				await media.SetVolumeAsync(0, context.CancellationToken);
				var played = await media.PlayAsync(context.CancellationToken, app);
				await FadeRamp.RampAsync(media, 0, end, seconds.Value, context.CancellationToken);
				return await MediaActionResults.FromControlResult(media, played, context.CancellationToken);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

internal static class FadeRamp
{
	public static async Task RampAsync(
		IMediaControlService media,
		int from,
		int to,
		double seconds,
		CancellationToken cancellationToken)
	{
		const int steps = 20;
		if (seconds <= 0 || from == to)
		{
			return;
		}

		var stepDelay = TimeSpan.FromSeconds(seconds / steps);
		for (var i = 1; i <= steps; i++)
		{
			await media.SetVolumeAsync(from + ((to - from) * i / steps), cancellationToken);
			if (i < steps)
			{
				await Task.Delay(stepDelay, cancellationToken);
			}
		}
	}
}

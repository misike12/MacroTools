using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using WindowsMediaControl.Config;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Actions;

internal static class MediaActionResults
{
	public static ActionResult NoSession() =>
		ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Errors.NoMediaSession());

	public static ActionResult ProviderError() =>
		ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.MediaCommandFailed());
}

public sealed class PlayAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "play";
	public LocalizedText Name => Strings.Actions.Play.Name();
	public LocalizedText Description => Strings.Actions.Play.Description();
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
				if (app is null)
				{
					var snapshot = await media.GetSnapshotAsync(context.CancellationToken);
					if (snapshot is { HasSession: true, Status: Media.PlaybackStatus.Playing })
					{
						return ActionResult.Success();
					}
				}

				return await media.PlayAsync(context.CancellationToken, app)
					? ActionResult.Success()
					: MediaActionResults.NoSession();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class PauseAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "pause";
	public LocalizedText Name => Strings.Actions.Pause.Name();
	public LocalizedText Description => Strings.Actions.Pause.Description();
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
				if (app is null)
				{
					var snapshot = await media.GetSnapshotAsync(context.CancellationToken);
					if (snapshot is { HasSession: true, Status: not Media.PlaybackStatus.Playing })
					{
						return ActionResult.Success();
					}
				}

				return await media.PauseAsync(context.CancellationToken, app)
					? ActionResult.Success()
					: MediaActionResults.NoSession();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class TogglePlayPauseAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition, IStateProviderActionDefinition, IIconProviderActionDefinition
{
	public string Id => "toggle-play-pause";
	public LocalizedText Name => Strings.Actions.TogglePlayPause.Name();
	public LocalizedText Description => Strings.Actions.TogglePlayPause.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [MediaParameters.AppOption()];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public TimeSpan StatePollInterval => TimeSpan.FromSeconds(Math.Clamp(settings.Current.StatePollSeconds, 1, 120));
	public TimeSpan IconPollInterval => TimeSpan.FromSeconds(Math.Clamp(settings.Current.IconPollSeconds, 10, 600));
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var states = new List<ActionStateDefinition>
		{
			new("playing", Strings.States.Playing()),
			new("paused", Strings.States.Paused()),
			new("stopped", Strings.States.Stopped()),
		};

		try
		{
			var snapshot = await media.GetSnapshotAsync(cancellationToken);
			if (!snapshot.HasSession)
			{
				return new ActionStateSnapshot(states, "stopped");
			}

			var active = snapshot.Status switch
			{
				Media.PlaybackStatus.Playing => "playing",
				Media.PlaybackStatus.Paused => "paused",
				_ => "stopped",
			};
			return new ActionStateSnapshot(states, active);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public async Task<ActionIconSnapshot?> GetActionIconAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		try
		{
			if (!settings.Current.ButtonArtwork)
			{
				return null;
			}

			var snapshot = await media.GetSnapshotAsync(cancellationToken);
			if (!snapshot.HasSession || string.IsNullOrEmpty(snapshot.ArtworkId))
			{
				return null;
			}

			return new ActionIconSnapshot { Version = snapshot.ArtworkId };
		}
		catch (Exception)
		{
			return null;
		}
	}

	public async Task<ActionIconContent?> GetActionIconContentAsync(
		IReadOnlyDictionary<string, object?> parameters,
		string version,
		CancellationToken cancellationToken)
	{
		try
		{
			var art = await media.GetArtworkAsync(version, cancellationToken);
			return art is null || art.Data.Length == 0
				? null
				: new ActionIconContent(art.Data, art.MimeType);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var app = MediaParameters.ReadApp(context.Parameters, settings);
				MediaSnapshot? before = app is null
					? await media.GetSnapshotAsync(context.CancellationToken)
					: null;
				if (!await media.TogglePlayPauseAsync(context.CancellationToken, app))
				{
					return MediaActionResults.NoSession();
				}

				var expected = before?.Status switch
				{
					Media.PlaybackStatus.Playing => "paused",
					Media.PlaybackStatus.Paused => "playing",
					Media.PlaybackStatus.Stopped => "playing",
					_ => (string?)null,
				};
				return expected is null
					? ActionResult.Success()
					: ActionResult.Success(expected);
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class StopAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "stop";
	public LocalizedText Name => Strings.Actions.Stop.Name();
	public LocalizedText Description => Strings.Actions.Stop.Description();
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
				if (app is null)
				{
					var snapshot = await media.GetSnapshotAsync(context.CancellationToken);
					if (!snapshot.HasSession || snapshot.Status is Media.PlaybackStatus.Stopped or Media.PlaybackStatus.NoMedia)
					{
						return ActionResult.Success();
					}
				}

				return await media.StopAsync(context.CancellationToken, app)
					? ActionResult.Success()
					: MediaActionResults.NoSession();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class NextAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "next";
	public LocalizedText Name => Strings.Actions.Next.Name();
	public LocalizedText Description => Strings.Actions.Next.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [MediaParameters.AppOption()];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await media.NextAsync(context.CancellationToken, MediaParameters.ReadApp(context.Parameters, settings))
					? ActionResult.Success()
					: MediaActionResults.NoSession();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class PreviousAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "previous";
	public LocalizedText Name => Strings.Actions.Previous.Name();
	public LocalizedText Description => Strings.Actions.Previous.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [MediaParameters.AppOption()];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await media.PreviousAsync(context.CancellationToken, MediaParameters.ReadApp(context.Parameters, settings))
					? ActionResult.Success()
					: MediaActionResults.NoSession();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class FastForwardAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "fast-forward";
	public LocalizedText Name => Strings.Actions.FastForward.Name();
	public LocalizedText Description => Strings.Actions.FastForward.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [MediaParameters.AppOption()];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await media.FastForwardAsync(context.CancellationToken, MediaParameters.ReadApp(context.Parameters, settings))
					? ActionResult.Success()
					: MediaActionResults.NoSession();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

public sealed class RewindAction(IMediaControlService media, MediaSettingsProvider settings) : IActionDefinition
{
	public string Id => "rewind";
	public LocalizedText Name => Strings.Actions.Rewind.Name();
	public LocalizedText Description => Strings.Actions.Rewind.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [MediaParameters.AppOption()];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(media, settings);

	private sealed class Executor(IMediaControlService media, MediaSettingsProvider settings) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await media.RewindAsync(context.CancellationToken, MediaParameters.ReadApp(context.Parameters, settings))
					? ActionResult.Success()
					: MediaActionResults.NoSession();
			}
			catch (Exception)
			{
				return MediaActionResults.ProviderError();
			}
		}
	}
}

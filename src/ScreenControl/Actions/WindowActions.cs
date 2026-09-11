using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScreenControl.Windows;

namespace ScreenControl.Actions;

public sealed class FocusWindowAction(IWindowService windows) : IActionDefinition
{
	public string Id => "focus-window";
	public LocalizedText Name => Strings.Actions.FocusWindow.Name();
	public LocalizedText Description => Strings.Actions.FocusWindow.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [DisplayParameters.WindowOption(required: true)];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var filter = DisplayParameters.ReadWindow(context.Parameters);
			if (string.IsNullOrWhiteSpace(filter))
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Params.Window.Label()));
			}

			try
			{
				var window = windows.Find(filter);
				return Task.FromResult(window is not null && windows.Focus(window.Handle)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.WindowNotFound()));
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class MinimizeWindowAction(IWindowService windows) : IActionDefinition
{
	public string Id => "minimize-window";
	public LocalizedText Name => Strings.Actions.MinimizeWindow.Name();
	public LocalizedText Description => Strings.Actions.MinimizeWindow.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [DisplayParameters.WindowOption(required: false)];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var window = windows.Find(DisplayParameters.ReadWindow(context.Parameters));
				return Task.FromResult(window is not null && windows.Minimize(window.Handle)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.WindowNotFound()));
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class MaximizeWindowAction(IWindowService windows) : IActionDefinition
{
	public string Id => "maximize-window";
	public LocalizedText Name => Strings.Actions.MaximizeWindow.Name();
	public LocalizedText Description => Strings.Actions.MaximizeWindow.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [DisplayParameters.WindowOption(required: false)];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var window = windows.Find(DisplayParameters.ReadWindow(context.Parameters));
				return Task.FromResult(window is not null && windows.Maximize(window.Handle)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.WindowNotFound()));
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class RestoreWindowAction(IWindowService windows) : IActionDefinition
{
	public string Id => "restore-window";
	public LocalizedText Name => Strings.Actions.RestoreWindow.Name();
	public LocalizedText Description => Strings.Actions.RestoreWindow.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [DisplayParameters.WindowOption(required: false)];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var window = windows.Find(DisplayParameters.ReadWindow(context.Parameters));
				return Task.FromResult(window is not null && windows.Restore(window.Handle)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.WindowNotFound()));
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class CloseWindowAction(IWindowService windows) : IActionDefinition
{
	public string Id => "close-window";
	public LocalizedText Name => Strings.Actions.CloseWindow.Name();
	public LocalizedText Description => Strings.Actions.CloseWindow.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [DisplayParameters.WindowOption(required: true)];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var filter = DisplayParameters.ReadWindow(context.Parameters);
			if (string.IsNullOrWhiteSpace(filter))
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Params.Window.Label()));
			}

			try
			{
				var window = windows.Find(filter);
				return Task.FromResult(window is not null && windows.Close(window.Handle)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.WindowNotFound()));
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

public sealed class ToggleAlwaysOnTopAction(IWindowService windows) : IActionDefinition
{
	public string Id => "toggle-always-on-top";
	public LocalizedText Name => Strings.Actions.ToggleAlwaysOnTop.Name();
	public LocalizedText Description => Strings.Actions.ToggleAlwaysOnTop.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } = [DisplayParameters.WindowOption(required: false)];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var window = windows.Find(DisplayParameters.ReadWindow(context.Parameters));
				if (window is null)
				{
					return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.WindowNotFound()));
				}

				return Task.FromResult(windows.SetTopmost(window.Handle, !windows.IsTopmost(window.Handle))
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

public sealed class SnapWindowAction(IWindowService windows) : IActionDefinition
{
	public string Id => "snap-window";
	public LocalizedText Name => Strings.Actions.SnapWindow.Name();
	public LocalizedText Description => Strings.Actions.SnapWindow.Description();
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		DisplayParameters.WindowOption(required: false),
		new ActionParameter
		{
			Name = "side",
			Type = ActionParameterType.Choice,
			Label = Strings.Actions.SnapWindow.Side.Label(),
			Description = Strings.Actions.SnapWindow.Side.Description(),
			Options =
			[
				new ActionParameterOption { Value = "left", Label = Strings.Sides.Left() },
				new ActionParameterOption { Value = "right", Label = Strings.Sides.Right() },
			],
			DefaultValue = "left",
		},
	];
	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows;
	public IActionExecutor CreateExecutor() => new Executor(windows);

	private sealed class Executor(IWindowService windows) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var side = context.Parameters.TryGetValue("side", out var raw)
				? raw?.ToString()?.ToLowerInvariant()
				: "left";
			if (side != "left" && side != "right")
			{
				return Task.FromResult(ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SnapWindow.Side.Label()));
			}

			try
			{
				var window = windows.Find(DisplayParameters.ReadWindow(context.Parameters));
				return Task.FromResult(window is not null && windows.Snap(window.Handle, side == "left")
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.WindowNotFound()));
			}
			catch (Exception)
			{
				return Task.FromResult(DisplayActionResults.ProviderError());
			}
		}
	}
}

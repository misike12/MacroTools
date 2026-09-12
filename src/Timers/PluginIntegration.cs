using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Serilog;
using Timers.Actions;
using Timers.Timing;
using Timers.Widgets;

namespace Timers;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IEventProvider, IWidgetTypeProvider, IUiProvider, IDisposable
{
	private readonly TimerService _timers;
	private readonly PomodoroService _pomodoro;
	private readonly ILogger _logger;
	private readonly FocusTimerWidget _widget;
	private IIntegrationContext? _context;
	private bool _disposed;

	public PluginIntegration(TimerService timers, PomodoroService pomodoro, ILogger logger)
	{
		_timers = timers;
		_pomodoro = pomodoro;
		_logger = logger.ForContext<PluginIntegration>();
		_widget = new FocusTimerWidget(timers, pomodoro, logger);
		Actions =
		[
			new StartCountdownAction(timers),
			new PauseCountdownAction(timers),
			new ResumeCountdownAction(timers),
			new CancelCountdownAction(timers),
			new ToggleCountdownAction(timers),
			new AdjustCountdownAction(timers),
			new StartStopwatchAction(timers),
			new StopStopwatchAction(timers),
			new ResetStopwatchAction(timers),
			new ToggleStopwatchAction(timers),
			new StartPomodoroAction(pomodoro),
			new StopPomodoroAction(pomodoro),
			new SkipPomodoroPhaseAction(pomodoro),
			new TogglePomodoroAction(pomodoro),
		];
		Variables = TimerVariables.CreateDefinitions();
		DeclaredVariables = Variables;
		EventDefinitions =
		[
			new EventDefinition
			{
				Id = "countdown-finished",
				Name = Strings.Events.CountdownFinished.Name(),
				Description = Strings.Events.CountdownFinished.Description(),
				PayloadParameters =
				[
					new ActionParameter { Name = "label", Type = ActionParameterType.String, Label = Strings.Events.CountdownFinished.LabelParameter.Label() },
					new ActionParameter { Name = "seconds", Type = ActionParameterType.Number, Label = Strings.Events.CountdownFinished.SecondsParameter.Label() },
				],
			},
			new EventDefinition
			{
				Id = "pomodoro-phase-changed",
				Name = Strings.Events.PomodoroPhaseChanged.Name(),
				Description = Strings.Events.PomodoroPhaseChanged.Description(),
				PayloadParameters =
				[
					new ActionParameter { Name = "phase", Type = ActionParameterType.String, Label = Strings.Events.PomodoroPhaseChanged.PhaseParameter.Label() },
					new ActionParameter { Name = "round", Type = ActionParameterType.Number, Label = Strings.Events.PomodoroPhaseChanged.RoundParameter.Label() },
					new ActionParameter { Name = "label", Type = ActionParameterType.String, Label = Strings.Events.PomodoroPhaseChanged.LabelParameter.Label() },
				],
			},
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<VariableDefinition> Variables { get; }

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; }

	public bool VariablesDependOnConfiguration => false;

	public bool SupportsCatalog => false;

	public bool SupportsPush => false;

	public bool SupportsSearch => false;

	public string CatalogName => "Timers";

	public int? CatalogEntryCount => null;

	public IReadOnlyList<EventDefinition> EventDefinitions { get; }

	public Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_timers.CountdownFinished -= OnCountdownFinished;
		_timers.CountdownFinished += OnCountdownFinished;
		_pomodoro.PhaseChanged -= OnPomodoroPhaseChanged;
		_pomodoro.PhaseChanged += OnPomodoroPhaseChanged;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		_timers.CountdownFinished -= OnCountdownFinished;
		_pomodoro.PhaseChanged -= OnPomodoroPhaseChanged;
		return Task.CompletedTask;
	}

	public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken) =>
		_widget.InitializeAsync(context, cancellationToken);

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => _widget.GetWidgetTypes();

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _widget.Surfaces;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken) =>
		_widget.CreateSessionAsync(request, cancellationToken);

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_timers.CountdownFinished -= OnCountdownFinished;
		_pomodoro.PhaseChanged -= OnPomodoroPhaseChanged;
	}

	private void OnCountdownFinished(object? sender, CountdownFinished finished)
	{
		try
		{
			_context?.Events.Publish("countdown-finished", new Dictionary<string, object?>
			{
				["label"] = finished.Label,
				["seconds"] = finished.Seconds,
			});
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Countdown finished publish failed.");
		}
	}

	private void OnPomodoroPhaseChanged(object? sender, PomodoroPhaseChanged changed)
	{
		try
		{
			_context?.Events.Publish("pomodoro-phase-changed", new Dictionary<string, object?>
			{
				["phase"] = FocusTimerWidget.PhaseToken(changed.Phase),
				["round"] = (double)changed.Round,
				["label"] = changed.Label,
			});
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Pomodoro phase publish failed.");
		}
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var remaining = _timers.CountdownRemaining;
		var elapsed = _timers.StopwatchElapsed;
		var pomo = _pomodoro.Snapshot();
		return ValueTask.FromResult(localId switch
		{
			"countdown-remaining-seconds" => VariableReading.Of(remaining.TotalSeconds),
			"countdown-text" => VariableReading.Of(FormatDuration(remaining)),
			"countdown-running" => VariableReading.Of(_timers.CountdownRunning),
			"countdown-label" => TextOrUnavailable(_timers.CountdownLabel),
			"countdown-progress-percent" => VariableReading.Of(_timers.CountdownProgressPercent, 0, 100, 1),
			"stopwatch-elapsed-seconds" => VariableReading.Of(elapsed.TotalSeconds),
			"stopwatch-text" => VariableReading.Of(FormatDuration(elapsed)),
			"stopwatch-running" => VariableReading.Of(_timers.StopwatchRunning),
			"pomodoro-phase" => VariableReading.Of(FocusTimerWidget.PhaseToken(pomo.Phase)),
			"pomodoro-remaining-seconds" => VariableReading.Of(pomo.Remaining.TotalSeconds),			"pomodoro-phase-text" => VariableReading.Of(FormatDuration(pomo.Remaining)),
			"pomodoro-label" => TextOrUnavailable(pomo.Label),
			"pomodoro-round" => VariableReading.Of((double)pomo.Round),
			"pomodoro-running" => VariableReading.Of(pomo.Running),
			"pomodoro-progress-percent" => VariableReading.Of(PomodoroProgress(pomo), 0, 100, 1),
			_ => VariableReading.Unavailable,
		});
	}

	private static double PomodoroProgress(PomodoroSnapshot pomo)
	{
		if (pomo.Total <= TimeSpan.Zero)
		{
			return 0;
		}

		var done = pomo.Total - pomo.Remaining;
		if (done < TimeSpan.Zero)
		{
			done = TimeSpan.Zero;
		}

		if (done > pomo.Total)
		{
			done = pomo.Total;
		}

		return done.TotalSeconds / pomo.Total.TotalSeconds * 100;
	}

	public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(VariableWriteResult.NotWritable(Strings.Errors.ReadOnlyVariable()));

	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(new VariableCatalogPage { Items = [] });

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<VariableDefinition?>(null);

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(IReadOnlyCollection<string> localIds, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<VariableValue>>([]);

	public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	private static string FormatDuration(TimeSpan value)
	{
		if (value < TimeSpan.Zero)
		{
			value = TimeSpan.Zero;
		}

		return value.TotalHours >= 1
			? $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}"
			: $"{value.Minutes}:{value.Seconds:D2}";
	}

	private static VariableReading TextOrUnavailable(string value) =>
		string.IsNullOrWhiteSpace(value) ? VariableReading.Unavailable : VariableReading.Of(value);
}

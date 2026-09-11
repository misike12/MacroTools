using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;
using Serilog;
using Timers.Actions;
using Timers.Timing;

namespace Timers;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IEventProvider, IDisposable
{
	private readonly TimerService _timers;
	private readonly ILogger _logger;
	private IIntegrationContext? _context;
	private bool _disposed;

	public PluginIntegration(TimerService timers, ILogger logger)
	{
		_timers = timers;
		_logger = logger.ForContext<PluginIntegration>();
		Actions =
		[
			new StartCountdownAction(timers),
			new PauseCountdownAction(timers),
			new ResumeCountdownAction(timers),
			new CancelCountdownAction(timers),
			new StartStopwatchAction(timers),
			new StopStopwatchAction(timers),
			new ResetStopwatchAction(timers),
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
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		_timers.CountdownFinished -= OnCountdownFinished;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_timers.CountdownFinished -= OnCountdownFinished;
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

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var remaining = _timers.CountdownRemaining;
		var elapsed = _timers.StopwatchElapsed;
		return ValueTask.FromResult(localId switch
		{
			"countdown-remaining-seconds" => VariableReading.Of(remaining.TotalSeconds),
			"countdown-text" => VariableReading.Of(FormatDuration(remaining)),
			"countdown-running" => VariableReading.Of(_timers.CountdownRunning),
			"countdown-label" => TextOrUnavailable(_timers.CountdownLabel),
			"stopwatch-elapsed-seconds" => VariableReading.Of(elapsed.TotalSeconds),
			"stopwatch-text" => VariableReading.Of(FormatDuration(elapsed)),
			"stopwatch-running" => VariableReading.Of(_timers.StopwatchRunning),
			_ => VariableReading.Unavailable,
		});
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

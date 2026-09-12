namespace Timers.Timing;

public enum PomodoroPhase
{
	Idle,
	Focus,
	ShortBreak,
	LongBreak,
}

public sealed record PomodoroSettings(
	double FocusMinutes = 25,
	double ShortBreakMinutes = 5,
	double LongBreakMinutes = 15,
	int Rounds = 4,
	bool AutoAdvance = true)
{
	public static PomodoroSettings Default { get; } = new();

	public TimeSpan FocusDuration => TimeSpan.FromMinutes(FocusMinutes);

	public TimeSpan ShortBreakDuration => TimeSpan.FromMinutes(ShortBreakMinutes);

	public TimeSpan LongBreakDuration => TimeSpan.FromMinutes(LongBreakMinutes);
}

public sealed record PomodoroSnapshot(
	PomodoroPhase Phase,
	int Round,
	int TotalRounds,
	bool Running,
	TimeSpan Remaining,
	TimeSpan Total,
	string Label);

public sealed record PomodoroPhaseChanged(PomodoroPhase Phase, int Round, string Label);

// A classic Pomodoro cycle (focus, short breaks, a long break every Nth round) on top of
// its own TimerService, so a Pomodoro never disturbs a manually started countdown and
// vice versa. Phase transitions ride the inner timer's finished event; every public
// member is safe to call from any thread.
public sealed class PomodoroService : IDisposable
{
	private readonly TimerService _timers = new();
	private readonly object _gate = new();
	private PomodoroSettings _settings = PomodoroSettings.Default;
	private PomodoroPhase _phase = PomodoroPhase.Idle;
	private int _round;
	private bool _disposed;

	public event EventHandler<PomodoroPhaseChanged>? PhaseChanged;

	public PomodoroService()
	{
		_timers.CountdownFinished += OnCountdownFinished;
	}

	public void Start(PomodoroSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		var normalized = Normalize(settings);
		lock (_gate)
		{
			ThrowIfDisposed();
			_settings = normalized;
			_round = 1;
			BeginPhaseLocked(PomodoroPhase.Focus, normalized.FocusDuration);
		}
	}

	public void Pause()
	{
		lock (_gate)
		{
			_timers.PauseCountdown();
		}
	}

	public void Resume()
	{
		lock (_gate)
		{
			if (_disposed || _phase == PomodoroPhase.Idle)
			{
				return;
			}

			_timers.ResumeCountdown();
		}
	}

	public void Toggle()
	{
		if (CountdownRunning)
		{
			Pause();
		}
		else
		{
			Resume();
		}
	}

	public void Stop()
	{
		PomodoroPhaseChanged? finished = null;
		lock (_gate)
		{
			if (_phase == PomodoroPhase.Idle)
			{
				return;
			}

			_timers.CancelCountdown();
			_phase = PomodoroPhase.Idle;
			_round = 0;
			finished = new PomodoroPhaseChanged(PomodoroPhase.Idle, 0, string.Empty);
		}

		QueuePhaseChanged(finished);
	}

	public void Skip()
	{
		PomodoroPhaseChanged? changed = null;
		lock (_gate)
		{
			if (_disposed || _phase == PomodoroPhase.Idle)
			{
				return;
			}

			_timers.CancelCountdown();
			changed = AdvanceLocked();
		}

		if (changed is not null)
		{
			QueuePhaseChanged(changed);
		}
	}

	public PomodoroSnapshot Snapshot()
	{
		lock (_gate)
		{
			if (_phase == PomodoroPhase.Idle)
			{
				return new PomodoroSnapshot(PomodoroPhase.Idle, 0, _settings.Rounds, false, TimeSpan.Zero, TimeSpan.Zero, string.Empty);
			}

			var total = DurationLocked(_phase);
			return new PomodoroSnapshot(
				_phase, _round, _settings.Rounds,
				_timers.CountdownRunning, _timers.CountdownRemaining, total,
				_timers.CountdownLabel);
		}
	}

	public bool CountdownRunning
	{
		get
		{
			lock (_gate)
			{
				return _timers.CountdownRunning;
			}
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_timers.CountdownFinished -= OnCountdownFinished;
			_timers.Dispose();
		}
	}

	private void OnCountdownFinished(object? sender, CountdownFinished finished)
	{
		PomodoroPhaseChanged? changed = null;
		lock (_gate)
		{
			if (_disposed || _phase == PomodoroPhase.Idle)
			{
				return;
			}

			if (_settings.AutoAdvance)
			{
				changed = AdvanceLocked();
			}
			else
			{
				changed = new PomodoroPhaseChanged(_phase, _round, _timers.CountdownLabel);
			}
		}

		if (changed is not null)
		{
			QueuePhaseChanged(changed);
		}
	}

	private PomodoroPhaseChanged AdvanceLocked()
	{
		var next = _phase switch
		{
			PomodoroPhase.Focus => _round >= _settings.Rounds ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak,
			PomodoroPhase.ShortBreak => NextRoundLocked(),
			PomodoroPhase.LongBreak => PomodoroPhase.Idle,
			_ => PomodoroPhase.Idle,
		};

		if (next == PomodoroPhase.Idle)
		{
			_phase = PomodoroPhase.Idle;
			_round = 0;
			return new PomodoroPhaseChanged(PomodoroPhase.Idle, 0, string.Empty);
		}

		BeginPhaseLocked(next, DurationLocked(next));
		return new PomodoroPhaseChanged(next, _round, _timers.CountdownLabel);
	}

	private PomodoroPhase NextRoundLocked()
	{
		_round++;
		return PomodoroPhase.Focus;
	}

	private void BeginPhaseLocked(PomodoroPhase phase, TimeSpan duration)
	{
		_phase = phase;
		_timers.StartCountdown(duration, LabelFor(phase, _round));
		if (!_settings.AutoAdvance)
		{
			_timers.PauseCountdown();
		}
	}

	private TimeSpan DurationLocked(PomodoroPhase phase) => phase switch
	{
		PomodoroPhase.Focus => _settings.FocusDuration,
		PomodoroPhase.ShortBreak => _settings.ShortBreakDuration,
		PomodoroPhase.LongBreak => _settings.LongBreakDuration,
		_ => TimeSpan.Zero,
	};

	private static string LabelFor(PomodoroPhase phase, int round) => phase switch
	{
		PomodoroPhase.Focus => $"Focus {round}",
		PomodoroPhase.ShortBreak => "Short break",
		PomodoroPhase.LongBreak => "Long break",
		_ => string.Empty,
	};

	private static PomodoroSettings Normalize(PomodoroSettings settings) => new(
		FocusMinutes: Math.Clamp(settings.FocusMinutes, 1.0 / 60, 180),
		ShortBreakMinutes: Math.Clamp(settings.ShortBreakMinutes, 1.0 / 60, 60),
		LongBreakMinutes: Math.Clamp(settings.LongBreakMinutes, 1.0 / 60, 90),
		Rounds: Math.Clamp(settings.Rounds, 1, 12),
		AutoAdvance: settings.AutoAdvance);

	private void QueuePhaseChanged(PomodoroPhaseChanged changed)
	{
		Task.Run(() =>
		{
			try
			{
				PhaseChanged?.Invoke(this, changed);
			}
			catch (Exception)
			{
			}
		});
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}

using System.Diagnostics;

namespace Timers.Timing;

public sealed record CountdownFinished(string Label, double Seconds);

public sealed class TimerService : IDisposable
{
	private readonly object _gate = new();
	private CancellationTokenSource? _countdownCts;
	private TimeSpan _countdownRemaining;
	private DateTimeOffset _countdownEndsAt;
	private bool _countdownRunning;
	private string _countdownLabel = string.Empty;
	private double _countdownSeconds;
	private readonly Stopwatch _stopwatch = new();
	private bool _disposed;

	public event EventHandler<CountdownFinished>? CountdownFinished;

	public void StartCountdown(TimeSpan duration, string label)
	{
		CancellationTokenSource? current = null;
		CountdownFinished? finished = null;
		lock (_gate)
		{
			ThrowIfDisposed();
			_countdownCts?.Cancel();
			_countdownCts?.Dispose();
			_countdownCts = null;
			_countdownLabel = label;
			_countdownSeconds = duration.TotalSeconds;
			if (duration <= TimeSpan.Zero)
			{
				_countdownRunning = false;
				_countdownRemaining = TimeSpan.Zero;
				finished = new CountdownFinished(label, _countdownSeconds);
			}
			else
			{
				_countdownRunning = true;
				_countdownRemaining = duration;
				_countdownEndsAt = DateTimeOffset.UtcNow + duration;
				_countdownCts = new CancellationTokenSource();
				current = _countdownCts;
			}
		}

		if (finished is not null)
		{
			QueueFinished(finished.Label, finished.Seconds);
			return;
		}

		_ = RunCountdownAsync(duration, label, current, null);
	}

	public void PauseCountdown()
	{
		lock (_gate)
		{
			if (!_countdownRunning)
			{
				return;
			}

			_countdownCts?.Cancel();
			_countdownCts?.Dispose();
			_countdownCts = null;
			_countdownRunning = false;
			var left = _countdownEndsAt - DateTimeOffset.UtcNow;
			_countdownRemaining = left > TimeSpan.Zero ? left : TimeSpan.Zero;
		}
	}

	public void ResumeCountdown()
	{
		CancellationTokenSource? current;
		TimeSpan remaining;
		string label;
		double seconds;
		lock (_gate)
		{
			if (_countdownRunning || _countdownRemaining <= TimeSpan.Zero)
			{
				return;
			}

			remaining = _countdownRemaining;
			label = _countdownLabel;
			seconds = _countdownSeconds;
			_countdownRunning = true;
			_countdownEndsAt = DateTimeOffset.UtcNow + remaining;
			_countdownCts?.Dispose();
			_countdownCts = new CancellationTokenSource();
			current = _countdownCts;
		}

		_ = RunCountdownAsync(remaining, label, current, seconds);
	}

	public void CancelCountdown()
	{
		lock (_gate)
		{
			_countdownCts?.Cancel();
			_countdownCts?.Dispose();
			_countdownCts = null;
			_countdownRunning = false;
			_countdownRemaining = TimeSpan.Zero;
			_countdownLabel = string.Empty;
		}
	}

	public void ToggleCountdown()
	{
		if (CountdownRunning)
		{
			PauseCountdown();
		}
		else
		{
			ResumeCountdown();
		}
	}

	public void AdjustCountdown(TimeSpan delta)
	{
		if (delta == TimeSpan.Zero)
		{
			return;
		}

		CancellationTokenSource? current = null;
		TimeSpan remaining = TimeSpan.Zero;
		string label = string.Empty;
		double seconds = 0;
		var restart = false;
		CountdownFinished? finished = null;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			var left = _countdownRunning
				? _countdownEndsAt - DateTimeOffset.UtcNow
				: _countdownRemaining;
			if (left < TimeSpan.Zero)
			{
				left = TimeSpan.Zero;
			}

			if (!_countdownRunning && _countdownRemaining <= TimeSpan.Zero)
			{
				return;
			}

			var updated = left + delta;
			if (updated <= TimeSpan.Zero)
			{
				_countdownCts?.Cancel();
				_countdownCts?.Dispose();
				_countdownCts = null;
				_countdownRunning = false;
				_countdownRemaining = TimeSpan.Zero;
				finished = new CountdownFinished(_countdownLabel, _countdownSeconds);
			}
			else if (_countdownRunning)
			{
				_countdownCts?.Cancel();
				_countdownCts?.Dispose();
				_countdownCts = new CancellationTokenSource();
				current = _countdownCts;
				_countdownRemaining = updated;
				_countdownEndsAt = DateTimeOffset.UtcNow + updated;
				remaining = updated;
				label = _countdownLabel;
				seconds = _countdownSeconds;
				restart = true;
			}
			else
			{
				_countdownRemaining = updated;
			}
		}

		if (finished is not null)
		{
			QueueFinished(finished.Label, finished.Seconds);
			return;
		}

		if (restart)
		{
			_ = RunCountdownAsync(remaining, label, current, seconds);
		}
	}

	public TimeSpan CountdownRemaining
	{
		get
		{
			lock (_gate)
			{
				if (!_countdownRunning)
				{
					return _countdownRemaining;
				}

				var left = _countdownEndsAt - DateTimeOffset.UtcNow;
				return left > TimeSpan.Zero ? left : TimeSpan.Zero;
			}
		}
	}

	public bool CountdownRunning
	{
		get
		{
			lock (_gate)
			{
				return _countdownRunning;
			}
		}
	}

	public string CountdownLabel
	{
		get
		{
			lock (_gate)
			{
				return _countdownLabel;
			}
		}
	}

	public double CountdownTotalSeconds
	{
		get
		{
			lock (_gate)
			{
				return _countdownSeconds;
			}
		}
	}

	public double CountdownProgressPercent
	{
		get
		{
			lock (_gate)
			{
				if (_countdownSeconds <= 0)
				{
					return 0;
				}

				var left = _countdownRunning
					? _countdownEndsAt - DateTimeOffset.UtcNow
					: _countdownRemaining;
				if (left < TimeSpan.Zero)
				{
					left = TimeSpan.Zero;
				}

				if (left > TimeSpan.FromSeconds(_countdownSeconds))
				{
					left = TimeSpan.FromSeconds(_countdownSeconds);
				}

				return (_countdownSeconds - left.TotalSeconds) / _countdownSeconds * 100;
			}
		}
	}

	public void StartStopwatch()
	{
		lock (_gate)
		{
			ThrowIfDisposed();
			_stopwatch.Start();
		}
	}

	public void StopStopwatch()
	{
		lock (_gate)
		{
			_stopwatch.Stop();
		}
	}

	public void ResetStopwatch()
	{
		lock (_gate)
		{
			_stopwatch.Reset();
		}
	}

	public void ToggleStopwatch()
	{
		lock (_gate)
		{
			ThrowIfDisposed();
			if (_stopwatch.IsRunning)
			{
				_stopwatch.Stop();
			}
			else
			{
				_stopwatch.Start();
			}
		}
	}

	public TimeSpan StopwatchElapsed
	{
		get
		{
			lock (_gate)
			{
				return _stopwatch.Elapsed;
			}
		}
	}

	public bool StopwatchRunning
	{
		get
		{
			lock (_gate)
			{
				return _stopwatch.IsRunning;
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
			_countdownCts?.Cancel();
			_countdownCts?.Dispose();
			_countdownCts = null;
			_stopwatch.Stop();
		}
	}

	private async Task RunCountdownAsync(TimeSpan duration, string label, CancellationTokenSource? owner, double? armedSeconds = null)
	{
		var token = owner?.Token ?? CancellationToken.None;
		try
		{
			await Task.Delay(duration, token);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception)
		{
			return;
		}

		var seconds = armedSeconds ?? duration.TotalSeconds;
		lock (_gate)
		{
			if (!ReferenceEquals(_countdownCts, owner) || !_countdownRunning)
			{
				return;
			}

			_countdownRunning = false;
			_countdownRemaining = TimeSpan.Zero;
			_countdownCts?.Dispose();
			_countdownCts = null;
		}

		QueueFinished(label, seconds);
	}

	private void QueueFinished(string label, double seconds)
	{
		Task.Run(() =>
		{
			try
			{
				CountdownFinished?.Invoke(this, new CountdownFinished(label, seconds));
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

using NUnit.Framework;
using Timers.Timing;
using Timers.Widgets;

namespace Timers.Tests;

[TestFixture]
public sealed class PomodoroTests
{
	private static PomodoroSettings QuickSettings(bool autoAdvance = true) =>
		new(FocusMinutes: 0.05, ShortBreakMinutes: 0.05, LongBreakMinutes: 0.05, Rounds: 2, AutoAdvance: autoAdvance);

	private static async Task<PomodoroPhaseChanged?> WaitForPhaseAsync(
		List<PomodoroPhaseChanged> seen,
		PomodoroPhase phase)
	{
		var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
		while (DateTimeOffset.UtcNow < deadline)
		{
			lock (seen)
			{
				var match = seen.FirstOrDefault(e => e.Phase == phase);
				if (match is not null)
				{
					return match;
				}
			}

			await Task.Delay(50, TestContext.CurrentContext.CancellationToken);
		}

		return null;
	}

	[Test]
	public void Start_arms_the_first_focus_round()
	{
		using var pomodoro = new PomodoroService();
		pomodoro.Start(QuickSettings());

		var snapshot = pomodoro.Snapshot();

		Assert.That(snapshot.Phase, Is.EqualTo(PomodoroPhase.Focus));
		Assert.That(snapshot.Round, Is.EqualTo(1));
		Assert.That(snapshot.TotalRounds, Is.EqualTo(2));
		Assert.That(snapshot.Running, Is.True);
		Assert.That(snapshot.Remaining, Is.GreaterThan(TimeSpan.Zero));
		Assert.That(snapshot.Label, Is.EqualTo("Focus 1"));
		pomodoro.Stop();
	}

	[Test]
	public async Task Auto_advance_walks_focus_break_focus_longbreak_idle()
	{
		using var pomodoro = new PomodoroService();
		var seen = new List<PomodoroPhaseChanged>();
		pomodoro.PhaseChanged += (_, e) =>
		{
			lock (seen)
			{
				seen.Add(e);
			}
		};

		pomodoro.Start(QuickSettings());

		Assert.That(await WaitForPhaseAsync(seen, PomodoroPhase.ShortBreak), Is.Not.Null);
		Assert.That(await WaitForPhaseAsync(seen, PomodoroPhase.Focus), Is.Not.Null);
		Assert.That(pomodoro.Snapshot().Round, Is.EqualTo(2));
		Assert.That(await WaitForPhaseAsync(seen, PomodoroPhase.LongBreak), Is.Not.Null);
		Assert.That(await WaitForPhaseAsync(seen, PomodoroPhase.Idle), Is.Not.Null);
		Assert.That(pomodoro.Snapshot().Phase, Is.EqualTo(PomodoroPhase.Idle));
	}

	[Test]
	public async Task Without_auto_advance_a_finished_phase_waits_paused()
	{
		using var pomodoro = new PomodoroService();
		pomodoro.Start(QuickSettings(autoAdvance: false));

		var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
		while (DateTimeOffset.UtcNow < deadline && pomodoro.Snapshot().Running)
		{
			await Task.Delay(50, TestContext.CurrentContext.CancellationToken);
		}

		var snapshot = pomodoro.Snapshot();
		Assert.That(snapshot.Phase, Is.EqualTo(PomodoroPhase.Focus));
		Assert.That(snapshot.Running, Is.False);

		pomodoro.Skip();
		var next = pomodoro.Snapshot();
		Assert.That(next.Phase, Is.EqualTo(PomodoroPhase.ShortBreak));
		Assert.That(next.Running, Is.False);
		Assert.That(next.Remaining, Is.GreaterThan(TimeSpan.Zero));
	}

	[Test]
	public void Skip_moves_forward_and_stop_returns_to_idle()
	{
		using var pomodoro = new PomodoroService();
		pomodoro.Start(QuickSettings());

		pomodoro.Skip();
		Assert.That(pomodoro.Snapshot().Phase, Is.EqualTo(PomodoroPhase.ShortBreak));

		pomodoro.Skip();
		var second = pomodoro.Snapshot();
		Assert.That(second.Phase, Is.EqualTo(PomodoroPhase.Focus));
		Assert.That(second.Round, Is.EqualTo(2));

		pomodoro.Stop();
		var idle = pomodoro.Snapshot();
		Assert.That(idle.Phase, Is.EqualTo(PomodoroPhase.Idle));
		Assert.That(idle.Round, Is.EqualTo(0));
		Assert.That(idle.Running, Is.False);
	}

	[Test]
	public void Skip_and_stop_are_silent_noops_when_idle()
	{
		using var pomodoro = new PomodoroService();
		var events = 0;
		pomodoro.PhaseChanged += (_, _) => Interlocked.Increment(ref events);

		Assert.DoesNotThrow(() => pomodoro.Skip());
		Assert.DoesNotThrow(() => pomodoro.Stop());
		Assert.DoesNotThrow(() => pomodoro.Toggle());
		Assert.That(events, Is.EqualTo(0));
		Assert.That(pomodoro.Snapshot().Phase, Is.EqualTo(PomodoroPhase.Idle));
	}

	[Test]
	public void Toggle_flips_pause_and_resume()
	{
		using var pomodoro = new PomodoroService();
		pomodoro.Start(QuickSettings());

		pomodoro.Toggle();
		Assert.That(pomodoro.Snapshot().Running, Is.False);

		pomodoro.Toggle();
		Assert.That(pomodoro.Snapshot().Running, Is.True);
		pomodoro.Stop();
	}

	[Test]
	public void Settings_clamp_to_sane_bounds()
	{
		using var pomodoro = new PomodoroService();
		pomodoro.Start(new PomodoroSettings(-5, 500, 0, 99, true));

		var snapshot = pomodoro.Snapshot();
		Assert.That(snapshot.Total, Is.GreaterThan(TimeSpan.Zero));
		Assert.That(snapshot.TotalRounds, Is.EqualTo(12));
		pomodoro.Stop();
	}

	[Test]
	public void Focus_timer_options_parse_and_clamp()
	{
		var fallback = FocusTimerOptions.FromData(default);
		Assert.That(fallback, Is.EqualTo(FocusTimerOptions.Default));

		using var document = System.Text.Json.JsonDocument.Parse(
			"""{"mode":"STOPWATCH","countdownMinutes":500,"workMinutes":-3,"rounds":99,"autoAdvance":false,"accentColor":"  "}""");
		var options = FocusTimerOptions.FromData(document.RootElement);
		Assert.That(options.Mode, Is.EqualTo("stopwatch"));
		Assert.That(options.CountdownMinutes, Is.EqualTo(180));
		Assert.That(options.WorkMinutes, Is.EqualTo(1));
		Assert.That(options.Rounds, Is.EqualTo(12));
		Assert.That(options.AutoAdvance, Is.False);
		Assert.That(options.AccentColor, Is.Empty);
	}

	[Test]
	public void Focus_timer_options_reject_unknown_modes()
	{
		using var document = System.Text.Json.JsonDocument.Parse("""{"mode":"egg"}""");
		Assert.That(FocusTimerOptions.FromData(document.RootElement).Mode, Is.EqualTo("pomodoro"));
	}

	[Test]
	public void Remaining_formats_like_a_clock()
	{
		Assert.That(FocusTimerContent.FormatRemaining(TimeSpan.FromSeconds(90)), Is.EqualTo("1:30"));
		Assert.That(FocusTimerContent.FormatRemaining(TimeSpan.FromSeconds(5)), Is.EqualTo("0:05"));
		Assert.That(FocusTimerContent.FormatRemaining(TimeSpan.FromHours(2)), Is.EqualTo("2:00:00"));
		Assert.That(FocusTimerContent.FormatRemaining(TimeSpan.FromSeconds(-3)), Is.EqualTo("0:00"));
	}

	[Test]
	public void Widget_view_builds_for_every_preview_state()
	{
		Assert.DoesNotThrow(() => FocusTimerPreviews.PomodoroFocus());
		Assert.DoesNotThrow(() => FocusTimerPreviews.ShortBreak());
		Assert.DoesNotThrow(() => FocusTimerPreviews.Idle());
	}
}

namespace WindowsMediaControl.Media;

internal static class MediaTimeline
{
	public static TimeSpan Extrapolate(
		TimeSpan reported,
		TimeSpan duration,
		DateTimeOffset? lastUpdated,
		PlaybackStatus status,
		bool extrapolate,
		DateTimeOffset now)
	{
		var position = reported < TimeSpan.Zero ? TimeSpan.Zero : reported;
		if (extrapolate && status == PlaybackStatus.Playing && lastUpdated is DateTimeOffset updated)
		{
			var elapsed = now - updated;
			if (elapsed is { TotalSeconds: >= 0 and < 3600 })
			{
				position += elapsed;
			}
		}

		if (duration > TimeSpan.Zero && position > duration)
		{
			position = duration;
		}

		return position < TimeSpan.Zero ? TimeSpan.Zero : position;
	}
}

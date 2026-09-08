namespace WindowsMediaControl.Media;

public sealed record MediaSnapshot
{
	public bool HasSession { get; init; }
	public string Title { get; init; } = string.Empty;
	public string Artist { get; init; } = string.Empty;
	public string Album { get; init; } = string.Empty;
	public string AppId { get; init; } = string.Empty;
	public string ArtworkId { get; init; } = string.Empty;
	public PlaybackStatus Status { get; init; } = PlaybackStatus.NoMedia;
	public TimeSpan Position { get; init; } = TimeSpan.Zero;
	public TimeSpan Duration { get; init; } = TimeSpan.Zero;
	public int VolumePercent { get; init; } = 50;
	public bool IsMuted { get; init; }
	public bool? ShuffleActive { get; init; }
	public MediaRepeatMode RepeatMode { get; init; } = MediaRepeatMode.Off;
	public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

	public static MediaSnapshot Empty { get; } = new();

	public double ProgressPercent
	{
		get
		{
			if (Duration <= TimeSpan.Zero || Position <= TimeSpan.Zero)
			{
				return 0;
			}

			return Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds * 100, 0, 100);
		}
	}

	public bool IsPlaying => Status == PlaybackStatus.Playing;
}

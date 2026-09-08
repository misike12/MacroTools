namespace WindowsMediaControl.Config;

public sealed record MediaSettings(
	string PreferredApp,
	double DefaultSeekSeconds,
	double FastForwardSeconds,
	double DefaultVolumeStep,
	int MaxVolumeLimit,
	bool UnmuteOnVolumeChange,
	double PollIntervalSeconds,
	double StatePollSeconds,
	double IconPollSeconds,
	bool TrackEvents,
	bool PlaybackEvents,
	bool VolumeEvents,
	bool MuteEvents,
	bool ExtrapolatePosition,
	bool ButtonArtwork,
	double SnapshotTimeoutSeconds,
	double ControlTimeoutSeconds,
	int ArtworkCacheSize)
{
	public static MediaSettings Default { get; } = new(
		PreferredApp: string.Empty,
		DefaultSeekSeconds: 10,
		FastForwardSeconds: 10,
		DefaultVolumeStep: 5,
		MaxVolumeLimit: 100,
		UnmuteOnVolumeChange: true,
		PollIntervalSeconds: 2,
		StatePollSeconds: 2,
		IconPollSeconds: 30,
		TrackEvents: true,
		PlaybackEvents: true,
		VolumeEvents: true,
		MuteEvents: true,
		ExtrapolatePosition: true,
		ButtonArtwork: true,
		SnapshotTimeoutSeconds: 3,
		ControlTimeoutSeconds: 6,
		ArtworkCacheSize: 8);

	public int ClampVolume(int percent) => Math.Clamp(percent, 0, MaxVolumeLimit);

	public static class Keys
	{
		public const string PreferredApp = "preferred-app";
		public const string SeekSeconds = "seek-seconds";
		public const string FastForwardSeconds = "ff-seconds";
		public const string VolumeStep = "volume-step";
		public const string MaxVolume = "max-volume";
		public const string UnmuteOnVolume = "unmute-on-volume";
		public const string PollInterval = "poll-seconds";
		public const string StatePoll = "state-poll-seconds";
		public const string IconPoll = "icon-poll-seconds";
		public const string EventsTrack = "events-track";
		public const string EventsPlayback = "events-playback";
		public const string EventsVolume = "events-volume";
		public const string EventsMute = "events-mute";
		public const string Extrapolate = "extrapolate";
		public const string ButtonArtwork = "button-artwork";
		public const string SnapshotTimeout = "snapshot-timeout";
		public const string ControlTimeout = "control-timeout";
		public const string ArtworkCache = "artwork-cache";
		public const string Reset = "reset-defaults";
	}
}

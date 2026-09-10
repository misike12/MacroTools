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
	int ArtworkCacheSize,
	double SleepDefaultMinutes,
	double FadeSeconds,
	int MicMaxVolumeLimit,
	bool UnmuteMicOnVolumeChange,
	string DeviceRole,
	bool TrackToast,
	bool FocusUnmuteTarget,
	double EventDebounceMs)
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
		ArtworkCacheSize: 8,
		SleepDefaultMinutes: 30,
		FadeSeconds: 3,
		MicMaxVolumeLimit: 100,
		UnmuteMicOnVolumeChange: true,
		DeviceRole: DeviceRoles.All,
		TrackToast: false,
		FocusUnmuteTarget: true,
		EventDebounceMs: 750);

	public int ClampVolume(int percent) => Math.Clamp(percent, 0, Math.Max(0, MaxVolumeLimit));

	public int ClampMicVolume(int percent) => Math.Clamp(percent, 0, Math.Max(0, MicMaxVolumeLimit));

	public static class DeviceRoles
	{
		public const string All = "all";
		public const string Multimedia = "multimedia";
		public const string Console = "console";
		public const string Communications = "communications";

		public static int[] ToNativeRoles(string? role) => role?.ToLowerInvariant() switch
		{
			Multimedia => [1],
			Console => [0],
			Communications => [2],
			_ => [0, 1, 2],
		};
	}

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
		public const string SleepMinutes = "sleep-minutes";
		public const string FadeSeconds = "fade-seconds";
		public const string MicMaxVolume = "mic-max-volume";
		public const string UnmuteMicOnVolume = "unmute-mic-on-volume";
		public const string DeviceRole = "device-role";
		public const string TrackToast = "toast-track";
		public const string FocusUnmute = "focus-unmute-target";
		public const string EventDebounce = "event-debounce-ms";
		public const string Reset = "reset-defaults";
	}
}

namespace WindowsMediaControl.Media;

public enum PlaybackStatus
{
	NoMedia = 0,
	Stopped = 1,
	Playing = 2,
	Paused = 3,
}

public enum MediaRepeatMode
{
	Off = 0,
	All = 1,
	One = 2,
}

public enum MediaPlaybackType
{
	Unknown = 0,
	Music = 1,
	Video = 2,
	Image = 3,
}

namespace WindowsMediaControl.Media;

public static class MediaControlServiceFactory
{
	public static IMediaControlService Create() =>
		OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
			? new WindowsMediaControlService()
			: new NoOpMediaControlService();
}

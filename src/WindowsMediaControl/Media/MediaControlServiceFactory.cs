using WindowsMediaControl.Config;

namespace WindowsMediaControl.Media;

public static class MediaControlServiceFactory
{
	public static IMediaControlService Create(MediaSettingsProvider? settings = null) =>
		OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
			? new WindowsMediaControlService(settings)
			: new NoOpMediaControlService();
}

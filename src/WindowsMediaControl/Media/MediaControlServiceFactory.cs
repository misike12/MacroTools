using WindowsMediaControl.Config;

namespace WindowsMediaControl.Media;

public static class MediaControlServiceFactory
{
	public const string NoopAudioEnvironmentVariable = "WINDOWS_MEDIA_CONTROL_NOOP_AUDIO";

	public static IMediaControlService Create(MediaSettingsProvider? settings = null)
	{
		if (string.Equals(
			Environment.GetEnvironmentVariable(NoopAudioEnvironmentVariable),
			"1",
			StringComparison.Ordinal))
		{
			return new NoOpMediaControlService();
		}

		return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
			? new WindowsMediaControlService(settings)
			: new NoOpMediaControlService();
	}
}

using NUnit.Framework;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Tests;

[TestFixture]
[Explicit]
public sealed class LiveAudioVerificationTests
{
	[Test]
	public async Task Enumerate_apps_and_devices()
	{
		var service = new WindowsMediaControlService();
		var ct = TestContext.CurrentContext.CancellationToken;

		var apps = await service.GetAudioAppsAsync(ct);
		var devices = await service.GetAudioDevicesAsync(ct);

		foreach (var app in apps)
		{
			TestContext.Out.WriteLine($"app: {app.ProcessName} pid={app.ProcessId} vol={app.VolumePercent} muted={app.IsMuted}");
		}

		foreach (var device in devices)
		{
			TestContext.Out.WriteLine($"device: {device.Name} default={device.IsDefault} id={device.Id}");
		}

		Assert.That(devices.Count, Is.GreaterThan(0));
		Assert.That(devices.Any(d => d.IsDefault), Is.True);
	}

	[Test]
	public async Task Rewrite_current_app_volume_is_a_noop()
	{
		var service = new WindowsMediaControlService();
		var ct = TestContext.CurrentContext.CancellationToken;

		var apps = await service.GetAudioAppsAsync(ct);
		AudioAppSession? target = null;
		foreach (var app in apps)
		{
			target ??= app;
			if (app.ProcessName.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
			{
				target = app;
				break;
			}
		}
		if (target is null)
		{
			Assert.Ignore("No audio sessions right now.");
			return;
		}

		var ok = await service.SetAppVolumeAsync(target.ProcessName, target.VolumePercent, ct);

		TestContext.Out.WriteLine($"rewrote {target.ProcessName} at {target.VolumePercent}: {ok}");
		Assert.That(ok, Is.True);
	}
}

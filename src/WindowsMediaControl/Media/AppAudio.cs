using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;

namespace WindowsMediaControl.Media;

public sealed record AudioAppSession(string ProcessName, int ProcessId, string DisplayName, int VolumePercent, bool IsMuted);

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

internal static class AppAudio
{
	public static IReadOnlyList<AudioAppSession> GetAppSessions()
	{
		var found = new List<AudioAppSession>();
		try
		{
			var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
			foreach (var device in EnumerateActiveRenderDevices(enumerator))
			{
				try
				{
					CollectSessions(device, found);
				}
				catch (COMException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}

		return found;
	}

	public static bool TryAdjustAppVolume(string app, Func<int, bool, (int Volume, bool Mute)> adjust)
	{
		var matched = false;
		foreach (var target in FindSessionVolumes(app))
		{
			try
			{
				var current = target.Volume;
				if (current.GetMasterVolume(out var level) != 0)
				{
					continue;
				}

				if (current.GetMute(out var muted) != 0)
				{
					continue;
				}

				var (volume, mute) = adjust((int)Math.Round(level * 100), muted);
				if (current.SetMute(mute, Guid.Empty) != 0)
				{
					continue;
				}

				if (current.SetMasterVolume(Math.Clamp(volume, 0, 100) / 100f, Guid.Empty) != 0)
				{
					continue;
				}

				matched = true;
			}
			catch (COMException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}

		return matched;
	}

	public static Task<IReadOnlyList<AudioDevice>> GetOutputDevicesAsync() =>
		FindDevicesAsync(DeviceClass.AudioRender, GetDefaultEndpointId(EDataFlow.Render));

	public static Task<IReadOnlyList<AudioDevice>> GetInputDevicesAsync() =>
		FindDevicesAsync(DeviceClass.AudioCapture, GetDefaultEndpointId(EDataFlow.Capture));

	private static async Task<IReadOnlyList<AudioDevice>> FindDevicesAsync(DeviceClass deviceClass, string? defaultId)
	{
		var devices = new List<AudioDevice>();
		var found = await DeviceInformation.FindAllAsync(deviceClass);
		foreach (var device in found)
		{
			if (!device.IsEnabled)
			{
				continue;
			}

			devices.Add(new AudioDevice(
				device.Id,
				string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
				defaultId is not null && device.Id.Contains(defaultId, StringComparison.OrdinalIgnoreCase)));
		}

		return devices;
	}

	public static bool TrySetDefaultDevice(string deviceId)
	{
		try
		{
			var policy = (IPolicyConfig)new PolicyConfigClient();
			foreach (var role in new[] { 0, 1, 2 })
			{
				if (policy.SetDefaultEndpoint(deviceId, role) != 0)
				{
					return false;
				}
			}

			return true;
		}
		catch (COMException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	public static string? GetDefaultRenderEndpointId() =>
		GetDefaultEndpointId(EDataFlow.Render);

	public static string? GetDefaultCaptureEndpointId() =>
		GetDefaultEndpointId(EDataFlow.Capture);

	private static string? GetDefaultEndpointId(EDataFlow flow)
	{
		try
		{
			var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
			if (enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var device) != 0 || device is null)
			{
				return null;
			}

			return device.GetId(out var id) == 0 ? id : null;
		}
		catch (COMException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static List<IMMDevice> EnumerateActiveRenderDevices(IMMDeviceEnumerator enumerator)
	{
		var devices = new List<IMMDevice>();
		if (enumerator.EnumAudioEndpoints(EDataFlow.Render, (int)DeviceState.Active, out var collection) != 0 || collection is null)
		{
			return devices;
		}

		if (collection.GetCount(out var count) != 0)
		{
			return devices;
		}

		for (var i = 0; i < count; i++)
		{
			if (collection.Item(i, out var device) == 0 && device is not null)
			{
				devices.Add(device);
			}
		}

		return devices;
	}

	private static void CollectSessions(IMMDevice device, List<AudioAppSession> found)
	{
		var sessionManagerId = typeof(IAudioSessionManager2).GUID;
		if (device.Activate(ref sessionManagerId, (int)CLSCTX.All, IntPtr.Zero, out var managerObject) != 0
			|| managerObject is not IAudioSessionManager2 manager)
		{
			return;
		}

		if (manager.GetSessionEnumerator(out var enumerator) != 0 || enumerator is null)
		{
			return;
		}

		if (enumerator.GetCount(out var count) != 0)
		{
			return;
		}

		for (var i = 0; i < count; i++)
		{
			IAudioSessionControl2? control = null;
			try
			{
				if (enumerator.GetSession(i, out control) != 0 || control is null)
				{
					continue;
				}

				if (control.IsSystemSoundsSession() == 0)
				{
					continue;
				}

				if (control.GetProcessId(out var pid) != 0)
				{
					continue;
				}

				string name;
				try
				{
					using var process = System.Diagnostics.Process.GetProcessById(pid);
					name = process.ProcessName;
				}
				catch (Exception)
				{
					continue;
				}

				var volume = control as ISimpleAudioVolume;
				if (volume is null)
				{
					continue;
				}

				if (volume.GetMasterVolume(out var level) != 0 || volume.GetMute(out var muted) != 0)
				{
					continue;
				}

				found.Add(new AudioAppSession(name, pid, name, (int)Math.Round(level * 100), muted));
			}
			catch (Exception)
			{
				continue;
			}
		}
	}

	private sealed record SessionVolume(ISimpleAudioVolume Volume, IAudioSessionControl2 Control);

	private static List<SessionVolume> FindSessionVolumes(string app)
	{
		var targets = new List<SessionVolume>();
		try
		{
			var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
			foreach (var device in EnumerateActiveRenderDevices(enumerator))
			{
				try
				{
					CollectMatchingSessions(device, app, targets);
				}
				catch (COMException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}

		return targets;
	}

	private static void CollectMatchingSessions(IMMDevice device, string app, List<SessionVolume> targets)
	{
		var sessionManagerId = typeof(IAudioSessionManager2).GUID;
		if (device.Activate(ref sessionManagerId, (int)CLSCTX.All, IntPtr.Zero, out var managerObject) != 0
			|| managerObject is not IAudioSessionManager2 manager)
		{
			return;
		}

		if (manager.GetSessionEnumerator(out var enumerator) != 0 || enumerator is null)
		{
			return;
		}

		if (enumerator.GetCount(out var count) != 0)
		{
			return;
		}

		for (var i = 0; i < count; i++)
		{
			IAudioSessionControl2? control = null;
			try
			{
				if (enumerator.GetSession(i, out control) != 0 || control is null)
				{
					continue;
				}

				if (control.IsSystemSoundsSession() == 0)
				{
					continue;
				}

				if (control.GetProcessId(out var pid) != 0)
				{
					continue;
				}

				string name;
				try
				{
					using var process = System.Diagnostics.Process.GetProcessById(pid);
					name = process.ProcessName;
				}
				catch (Exception)
				{
					continue;
				}

				if (!name.Contains(app, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (control is ISimpleAudioVolume volume)
				{
					targets.Add(new SessionVolume(volume, control));
				}
			}
			catch (Exception)
			{
				continue;
			}
		}
	}
}

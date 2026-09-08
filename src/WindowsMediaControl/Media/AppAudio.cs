using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;

namespace WindowsMediaControl.Media;

public sealed record AudioAppSession(string ProcessName, int ProcessId, string DisplayName, int VolumePercent, bool IsMuted);

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);

internal static class AppAudio
{
	public static IReadOnlyList<AudioAppSession> GetAppSessions()
	{
		var found = new List<AudioAppSession>();
		IMMDeviceEnumerator? enumerator = null;
		try
		{
			enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
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
				finally
				{
					Marshal.ReleaseComObject(device);
				}
			}
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		finally
		{
			if (enumerator is not null)
			{
				Marshal.ReleaseComObject(enumerator);
			}
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
			finally
			{
				Marshal.ReleaseComObject(target.Volume);
				Marshal.ReleaseComObject(target.Control);
			}
		}

		return matched;
	}

	public static async Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync()
	{
		var devices = new List<AudioOutputDevice>();
		string? defaultId = GetDefaultRenderEndpointId();
		var found = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender);
		foreach (var device in found)
		{
			if (!device.IsEnabled)
			{
				continue;
			}

			devices.Add(new AudioOutputDevice(
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
			try
			{
				foreach (var role in new[] { 0, 1, 2 })
				{
					if (policy.SetDefaultEndpoint(deviceId, role) != 0)
					{
						return false;
					}
				}

				return true;
			}
			finally
			{
				Marshal.ReleaseComObject(policy);
			}
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

	public static string? GetDefaultRenderEndpointId()
	{
		IMMDeviceEnumerator? enumerator = null;
		IMMDevice? device = null;
		try
		{
			enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
			if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device) != 0 || device is null)
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
		finally
		{
			if (device is not null)
			{
				Marshal.ReleaseComObject(device);
			}

			if (enumerator is not null)
			{
				Marshal.ReleaseComObject(enumerator);
			}
		}
	}

	private static List<IMMDevice> EnumerateActiveRenderDevices(IMMDeviceEnumerator enumerator)
	{
		var devices = new List<IMMDevice>();
		if (enumerator.EnumAudioEndpoints(EDataFlow.Render, (int)DeviceState.Active, out var collection) != 0 || collection is null)
		{
			return devices;
		}

		try
		{
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
		}
		finally
		{
			Marshal.ReleaseComObject(collection);
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

		try
		{
			if (manager.GetSessionEnumerator(out var enumerator) != 0 || enumerator is null)
			{
				return;
			}

			try
			{
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
							Marshal.ReleaseComObject(volume);
							continue;
						}

						Marshal.ReleaseComObject(volume);
						found.Add(new AudioAppSession(name, pid, name, (int)Math.Round(level * 100), muted));
					}
					catch (Exception)
					{
						continue;
					}
					finally
					{
						if (control is not null)
						{
							Marshal.ReleaseComObject(control);
						}
					}
				}
			}
			finally
			{
				Marshal.ReleaseComObject(enumerator);
			}
		}
		finally
		{
			Marshal.ReleaseComObject(manager);
		}
	}

	private sealed record SessionVolume(ISimpleAudioVolume Volume, IAudioSessionControl2 Control);

	private static List<SessionVolume> FindSessionVolumes(string app)
	{
		var targets = new List<SessionVolume>();
		IMMDeviceEnumerator? enumerator = null;
		try
		{
			enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
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
				finally
				{
					Marshal.ReleaseComObject(device);
				}
			}
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		finally
		{
			if (enumerator is not null)
			{
				Marshal.ReleaseComObject(enumerator);
			}
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

		try
		{
			if (manager.GetSessionEnumerator(out var enumerator) != 0 || enumerator is null)
			{
				return;
			}

			try
			{
				if (enumerator.GetCount(out var count) != 0)
				{
					return;
				}

				for (var i = 0; i < count; i++)
				{
					IAudioSessionControl2? control = null;
					var keep = false;
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
							keep = true;
						}
					}
					catch (Exception)
					{
						continue;
					}
					finally
					{
						if (!keep && control is not null)
						{
							Marshal.ReleaseComObject(control);
						}
					}
				}
			}
			finally
			{
				Marshal.ReleaseComObject(enumerator);
			}
		}
		finally
		{
			Marshal.ReleaseComObject(manager);
		}
	}

	private enum EDataFlow
	{
		Render = 0,
	}

	private enum ERole
	{
		Multimedia = 1,
	}

	private enum CLSCTX
	{
		All = 23,
	}

	private enum DeviceState
	{
		Active = 1,
	}

	[ComImport]
	[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	private class MMDeviceEnumerator
	{
	}

	[ComImport]
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IMMDeviceCollection? ppDevices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? ppDevice);
	}

	[ComImport]
	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDevice
	{
		[PreserveSig]
		int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

		[PreserveSig]
		int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);

		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
	}

	[ComImport]
	[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf06")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPropertyStore
	{
	}

	[ComImport]
	[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceCollection
	{
		[PreserveSig]
		int GetCount(out int pcDevices);

		[PreserveSig]
		int Item(int nDevice, out IMMDevice? ppDevice);
	}

	[ComImport]
	[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioSessionManager2
	{
		[PreserveSig]
		int GetAudioSessionControl(in Guid sessionId, uint streamFlags, out IntPtr sessionControl);

		[PreserveSig]
		int GetSimpleAudioVolume(in Guid sessionId, uint streamFlags, out IntPtr audioVolume);

		[PreserveSig]
		int GetSessionEnumerator(out IAudioSessionEnumerator? ppSessionEnum);
	}

	[ComImport]
	[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioSessionEnumerator
	{
		[PreserveSig]
		int GetCount(out int sessionCount);

		[PreserveSig]
		int GetSession(int sessionCount, out IAudioSessionControl2? session);
	}

	[ComImport]
	[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioSessionControl2
	{
		[PreserveSig]
		int GetState(out int pRetVal);

		[PreserveSig]
		int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

		[PreserveSig]
		int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

		[PreserveSig]
		int GetGroupingParam(out Guid pRetVal);

		[PreserveSig]
		int SetGroupingParam(ref Guid grouping, ref Guid eventContext);

		[PreserveSig]
		int RegisterAudioSessionNotification(IntPtr notifications);

		[PreserveSig]
		int UnregisterAudioSessionNotification(IntPtr notifications);

		[PreserveSig]
		int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int GetProcessId(out int pRetVal);

		[PreserveSig]
		int IsSystemSoundsSession();
	}

	[ComImport]
	[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface ISimpleAudioVolume
	{
		[PreserveSig]
		int SetMasterVolume(float level, Guid eventContext);

		[PreserveSig]
		int GetMasterVolume(out float level);

		[PreserveSig]
		int SetMute(bool mute, Guid eventContext);

		[PreserveSig]
		int GetMute(out bool mute);
	}

	[ComImport]
	[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
	private class PolicyConfigClient
	{
	}

	[ComImport]
	[Guid("F8679F50-850A-4EBC-BF62-5F5672649466")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPolicyConfig
	{
		[PreserveSig]
		int GetMixFormat();

		[PreserveSig]
		int GetDeviceFormat();

		[PreserveSig]
		int ResetDeviceFormat();

		[PreserveSig]
		int SetDeviceFormat();

		[PreserveSig]
		int GetProcessingPeriod();

		[PreserveSig]
		int SetProcessingPeriod();

		[PreserveSig]
		int GetShareMode();

		[PreserveSig]
		int SetShareMode();

		[PreserveSig]
		int GetPropertyValue();

		[PreserveSig]
		int SetPropertyValue();

		[PreserveSig]
		int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

		[PreserveSig]
		int SetEndpointVisibility();
	}
}

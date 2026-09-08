using System.Runtime.InteropServices;

namespace WindowsMediaControl.Media;

internal sealed record AudioState(int VolumePercent, bool IsMuted);

internal static class SystemAudio
{
	private static readonly Guid PolicyConfigClient = Guid.Empty;

	public static AudioState? TryRead()
	{
		try
		{
			var volume = GetEndpoint();
			if (volume is null)
			{
				return null;
			}

			try
			{
				volume.GetMasterVolumeLevelScalar(out var level);
				volume.GetMute(out var muted);
				return new AudioState((int)Math.Round(level * 100), muted);
			}
			finally
			{
				Marshal.ReleaseComObject(volume);
			}
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

	public static void SetVolume(int percent)
	{
		try
		{
			var volume = GetEndpoint();
			if (volume is null)
			{
				return;
			}

			try
			{
				volume.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, PolicyConfigClient);
			}
			finally
			{
				Marshal.ReleaseComObject(volume);
			}
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	public static void AdjustVolume(int delta)
	{
		var current = TryRead();
		SetVolume((current?.VolumePercent ?? 50) + delta);
	}

	public static void SetMute(bool muted)
	{
		try
		{
			var volume = GetEndpoint();
			if (volume is null)
			{
				return;
			}

			try
			{
				volume.SetMute(muted, PolicyConfigClient);
			}
			finally
			{
				Marshal.ReleaseComObject(volume);
			}
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	public static void ToggleMute()
	{
		var current = TryRead();
		SetMute(!(current?.IsMuted ?? false));
	}

	private static IAudioEndpointVolume? GetEndpoint()
	{
		IMMDeviceEnumerator? enumerator = null;
		IMMDevice? device = null;
		try
		{
			enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
			var result = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
			if (result != 0 || device is null)
			{
				return null;
			}

			var iid = typeof(IAudioEndpointVolume).GUID;
			result = device.Activate(ref iid, (int)CLSCTX.All, IntPtr.Zero, out var endpoint);
			return result == 0 ? (IAudioEndpointVolume?)endpoint : null;
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
		int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);

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
	}

	[ComImport]
	[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioEndpointVolume
	{
		[PreserveSig]
		int RegisterControlChangeNotify(IntPtr pNotify);

		[PreserveSig]
		int UnregisterControlChangeNotify(IntPtr pNotify);

		[PreserveSig]
		int GetChannelCount(out int pnChannelCount);

		[PreserveSig]
		int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);

		[PreserveSig]
		int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);

		[PreserveSig]
		int GetMasterVolumeLevel(out float pfLevelDB);

		[PreserveSig]
		int GetMasterVolumeLevelScalar(out float pfLevel);

		[PreserveSig]
		int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);

		[PreserveSig]
		int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
	}
}

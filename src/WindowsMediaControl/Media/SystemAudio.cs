using System.Runtime.InteropServices;

namespace WindowsMediaControl.Media;

internal sealed record AudioState(int VolumePercent, bool IsMuted);

internal static class SystemAudio
{
	private static readonly Guid EventContext = Guid.Empty;

	public static AudioState? TryRead()
	{
		try
		{
			var volume = GetEndpoint();
			if (volume is null)
			{
				return null;
			}

			if (volume.GetMasterVolumeLevelScalar(out var level) != 0)
			{
				return null;
			}

			if (volume.GetMute(out var muted) != 0)
			{
				return null;
			}

			return new AudioState((int)Math.Round(level * 100), muted);
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

			volume.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, EventContext);
		}
		catch (COMException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
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

			volume.SetMute(muted, EventContext);
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
		var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
		var result = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device);
		if (result != 0 || device is null)
		{
			return null;
		}

		var iid = typeof(IAudioEndpointVolume).GUID;
		result = device.Activate(ref iid, (int)CLSCTX.All, IntPtr.Zero, out var endpoint);
		return result == 0 ? (IAudioEndpointVolume?)endpoint : null;
	}
}

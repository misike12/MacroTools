using System.Runtime.InteropServices;

namespace WindowsMediaControl.Media;

internal sealed record AudioState(int? VolumePercent, bool IsMuted);

internal static class SystemAudio
{
	private static readonly Guid EventContext = Guid.Empty;

	public static AudioState? TryRead() => TryReadFor(EDataFlow.Render);

	public static AudioState? TryReadCapture() => TryReadFor(EDataFlow.Capture);

	private static AudioState? TryReadFor(EDataFlow flow)
	{
		try
		{
			var volume = GetEndpoint(flow);
			if (volume is null)
			{
				return null;
			}

			// Read independently: some capture devices fail the scalar query while mute
			// works (or vice versa), and one working half is still truthful data.
			int? percent = null;
			if (volume.GetMasterVolumeLevelScalar(out var level) == 0)
			{
				percent = (int)Math.Round(level * 100);
			}

			var muted = false;
			if (volume.GetMute(out var deviceMuted) == 0)
			{
				muted = deviceMuted;
			}

			return new AudioState(percent, muted);
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
			var volume = GetEndpoint(EDataFlow.Render);
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

	public static void SetCaptureVolume(int percent)
	{
		try
		{
			var volume = GetEndpoint(EDataFlow.Capture);
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
			var volume = GetEndpoint(EDataFlow.Render);
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

	public static void SetCaptureMute(bool muted)
	{
		try
		{
			var volume = GetEndpoint(EDataFlow.Capture);
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

	public static void ToggleCaptureMute()
	{
		var current = TryReadCapture();
		SetCaptureMute(!(current?.IsMuted ?? false));
	}

	public static double? TryReadPeak(EDataFlow flow)
	{
		try
		{
			var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
			if (enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var device) != 0 || device is null)
			{
				return null;
			}

			var iid = typeof(IAudioMeterInformation).GUID;
			if (device.Activate(ref iid, (int)CLSCTX.All, IntPtr.Zero, out var meterObject) != 0
				|| meterObject is not IAudioMeterInformation meter)
			{
				return null;
			}

			return meter.GetPeakValue(out var peak) != 0 ? null : Math.Round(Math.Clamp(peak, 0, 1) * 100, 1);
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

	private static IAudioEndpointVolume? GetEndpoint(EDataFlow flow)
	{
		var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
		var result = enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var device);
		if (result != 0 || device is null)
		{
			return null;
		}

		var iid = typeof(IAudioEndpointVolume).GUID;
		result = device.Activate(ref iid, (int)CLSCTX.All, IntPtr.Zero, out var endpoint);
		return result == 0 ? (IAudioEndpointVolume?)endpoint : null;
	}
}

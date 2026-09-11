using System.Runtime.InteropServices;

namespace ScreenControl.Monitors;

// Software brightness for panels without DDC/CI (VESA DDC 2B only, like early-2000s
// LCDs): instead of driving the backlight over VCP 0x10, this scales the GPU gamma
// ramp of the display's own device context. It works on any monitor Windows can draw
// to, at the cost of crushing color resolution instead of dimming the lamp. Writes are
// absolute (built from the identity ramp), so a previous color calibration is reset.
public static class GammaRamp
{
	public const int EntriesPerChannel = 256;

	public static ushort[] Build(int percent)
	{
		var clamped = Math.Clamp(percent, 0, 100);
		var ramp = new ushort[EntriesPerChannel * 3];
		for (var i = 0; i < EntriesPerChannel; i++)
		{
			var scaled = (ushort)Math.Min(65535, (i * 257 * clamped + 50) / 100);
			ramp[i] = scaled;
			ramp[i + EntriesPerChannel] = scaled;
			ramp[i + 2 * EntriesPerChannel] = scaled;
		}

		return ramp;
	}

	public static bool IsSupported(string deviceName)
	{
		var hdc = NativeMethods.CreateDC(null, deviceName, null, IntPtr.Zero);
		if (hdc == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			return NativeMethods.GetDeviceGammaRamp(hdc, new ushort[EntriesPerChannel * 3]);
		}
		catch (Exception)
		{
			return false;
		}
		finally
		{
			try
			{
				NativeMethods.DeleteDC(hdc);
			}
			catch (Exception)
			{
			}
		}
	}

	public static bool TrySet(string deviceName, int percent)
	{
		var hdc = NativeMethods.CreateDC(null, deviceName, null, IntPtr.Zero);
		if (hdc == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			return NativeMethods.SetDeviceGammaRamp(hdc, Build(percent));
		}
		catch (Exception)
		{
			return false;
		}
		finally
		{
			try
			{
				NativeMethods.DeleteDC(hdc);
			}
			catch (Exception)
			{
			}
		}
	}

	private static class NativeMethods
	{
		[DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr CreateDC(
			[MarshalAs(UnmanagedType.LPWStr)] string? driver,
			[MarshalAs(UnmanagedType.LPWStr)] string device,
			[MarshalAs(UnmanagedType.LPWStr)] string? output,
			IntPtr initData);

		[DllImport("gdi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DeleteDC(IntPtr hdc);

		[DllImport("gdi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] ramp);

		[DllImport("gdi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);
	}
}

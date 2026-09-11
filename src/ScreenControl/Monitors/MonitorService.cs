using System.Runtime.InteropServices;

namespace ScreenControl.Monitors;

public sealed record MonitorInfo(
	int Index,
	string Name,
	bool IsPrimary,
	int BrightnessPercent,
	bool SupportsBrightness);

public interface IMonitorService
{
	IReadOnlyList<MonitorInfo> GetMonitors();

	bool TrySetBrightness(int index, int percent);

	bool TrySetInput(int index, int vcpValue);

	int? TryGetInput(int index);
}

public sealed class MonitorService : IMonitorService
{
	private const byte VcpInputSelect = 0x60;

	public IReadOnlyList<MonitorInfo> GetMonitors()
	{
		var found = new List<MonitorInfo>();
		try
		{
			MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref NativeMethods.Rect __, IntPtr ___) =>
			{
				var info = Describe(hMonitor, found.Count + 1);
				if (info is not null)
				{
					found.Add(info);
				}

				return true;
			};
			NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
			GC.KeepAlive(callback);
		}
		catch (Exception)
		{
		}

		return found;
	}

	public bool TrySetBrightness(int index, int percent)
	{
		var handles = HandlesFor(index);
		if (handles is null)
		{
			return false;
		}

		try
		{
			foreach (var handle in handles)
			{
				if (BrightnessRange(handle) is (int min, int max))
				{
					return NativeMethods.SetMonitorBrightness(handle, ToNative(percent, min, max));
				}
			}
		}
		catch (Exception)
		{
			return false;
		}
		finally
		{
			ClosePhysicalMonitors(handles);
		}

		return false;
	}

	public bool TrySetInput(int index, int vcpValue)
	{
		try
		{
			return WithPhysicalMonitor(index, handle =>
				NativeMethods.SetVCPFeature(handle, VcpInputSelect, (uint)vcpValue));
		}
		catch (Exception)
		{
			return false;
		}
	}

	public int? TryGetInput(int index)
	{
		try
		{
			return WithPhysicalMonitor(index, handle =>
			{
				if (!NativeMethods.GetVCPFeature(handle, VcpInputSelect, out _, out var current, out _))
				{
					return (false, (int?)null);
				}

				return (true, (int?)current);
			}, out var value) ? value : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static MonitorInfo? Describe(IntPtr hMonitor, int index)
	{
		try
		{
			var info = new NativeMethods.MonitorInfoEx { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
			if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
			{
				return null;
			}

			var name = string.IsNullOrWhiteSpace(info.DeviceName) ? $"Display {index}" : info.DeviceName;
			var primary = (info.Flags & 1) != 0;
			var brightness = ReadBrightness(hMonitor);
			return new MonitorInfo(index, name, primary, brightness?.Percent ?? 0, brightness is not null);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static (int Percent, int Min, int Max)? ReadBrightness(IntPtr hMonitor)
	{
		var handles = OpenPhysicalMonitors(hMonitor);
		if (handles is null)
		{
			return null;
		}

		try
		{
			foreach (var handle in handles)
			{
				if (NativeMethods.GetMonitorBrightness(handle, out var min, out var current, out var max) && max > min)
				{
					return ((int)Math.Round((current - min) * 100.0 / (max - min)), min, max);
				}
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			ClosePhysicalMonitors(handles);
		}

		return null;
	}

	private static bool WithPhysicalMonitor(int index, Func<IntPtr, bool> use)
	{
		var handles = HandlesFor(index);
		if (handles is null)
		{
			return false;
		}

		try
		{
			foreach (var handle in handles)
			{
				if (use(handle))
				{
					return true;
				}
			}
		}
		catch (Exception)
		{
			return false;
		}
		finally
		{
			ClosePhysicalMonitors(handles);
		}

		return false;
	}

	private static bool WithPhysicalMonitor(int index, Func<IntPtr, (bool Ok, int? Value)> use, out int? value)
	{
		value = null;
		var handles = HandlesFor(index);
		if (handles is null)
		{
			return false;
		}

		try
		{
			foreach (var handle in handles)
			{
				var (ok, current) = use(handle);
				if (ok)
				{
					value = current;
					return true;
				}
			}
		}
		catch (Exception)
		{
			return false;
		}
		finally
		{
			ClosePhysicalMonitors(handles);
		}

		return false;
	}

	private static int ToNative(int percent, int min, int max) =>
		min + (int)Math.Round(Math.Clamp(percent, 0, 100) * (max - min) / 100.0);

	private static (int Min, int Max)? BrightnessRange(IntPtr handle)
	{
		try
		{
			if (NativeMethods.GetMonitorBrightness(handle, out var min, out _, out var max) && max > min)
			{
				return (min, max);
			}
		}
		catch (Exception)
		{
		}

		return null;
	}

	private static IntPtr[]? HandlesFor(int index)
	{
		IntPtr[]? target = null;
		var current = 0;
		try
		{
			MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref NativeMethods.Rect __, IntPtr ___) =>
			{
				current++;
				if (current == index)
				{
					target = OpenPhysicalMonitors(hMonitor);
				}

				return true;
			};
			NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
			GC.KeepAlive(callback);
		}
		catch (Exception)
		{
			return null;
		}

		return target;
	}

	private static IntPtr[]? OpenPhysicalMonitors(IntPtr hMonitor)
	{
		try
		{
			if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
			{
				return null;
			}

			var physical = new NativeMethods.PhysicalMonitor[count];
			if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical))
			{
				return null;
			}

			var handles = new IntPtr[count];
			for (var i = 0; i < count; i++)
			{
				handles[i] = physical[i].Handle;
			}

			return handles;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static void ClosePhysicalMonitors(IntPtr[]? handles)
	{
		if (handles is null || handles.Length == 0)
		{
			return;
		}

		try
		{
			var physical = new NativeMethods.PhysicalMonitor[handles.Length];
			for (var i = 0; i < handles.Length; i++)
			{
				physical[i] = new NativeMethods.PhysicalMonitor { Handle = handles[i] };
			}

			NativeMethods.DestroyPhysicalMonitors((uint)handles.Length, physical);
		}
		catch (Exception)
		{
		}
	}

	private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.Rect rect, IntPtr data);

	private static class NativeMethods
	{
		[StructLayout(LayoutKind.Sequential)]
		public struct Rect
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct MonitorInfoEx
		{
			public uint Size;
			public Rect Monitor;
			public Rect Work;
			public uint Flags;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string DeviceName;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct PhysicalMonitor
		{
			public IntPtr Handle;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string Description;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumDisplayMonitors(
			IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);

		[DllImport("dxva2.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

		[DllImport("dxva2.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetPhysicalMonitorsFromHMONITOR(
			IntPtr hMonitor, uint count, [Out] PhysicalMonitor[] monitors);

		[DllImport("dxva2.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);

		[DllImport("dxva2.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetMonitorBrightness(
			IntPtr handle, out int minimum, out int current, out int maximum);

		[DllImport("dxva2.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetMonitorBrightness(IntPtr handle, int brightness);

		[DllImport("dxva2.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetVCPFeature(IntPtr handle, byte code, uint value);

		[DllImport("dxva2.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetVCPFeature(
			IntPtr handle, byte code, out uint type, out uint current, out uint maximum);
	}
}

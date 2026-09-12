using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ScreenControl.Windows;

namespace ScreenControl.Monitors;

public sealed record MonitorInfo(
	int Index,
	string Name,
	bool IsPrimary,
	int BrightnessPercent,
	bool SupportsBrightness,
	bool SoftwareBrightness);

public interface IMonitorService
{
	IReadOnlyList<MonitorInfo> GetMonitors();

	bool TrySetBrightness(int index, int percent);

	bool TrySetInput(int index, int vcpValue);

	int? TryGetInput(int index);

	bool TrySetPower(int index, int dpmValue);

	int? TryGetPower(int index);

	IReadOnlyList<int> GetSupportedInputs(int index);

	void HideOverlays();
}

public static class MonitorPowerModes
{
	public const int On = 1;
	public const int Standby = 2;
	public const int Off = 4;
}

public sealed class MonitorService : IMonitorService, IDisposable
{
	private const byte VcpInputSelect = 0x60;
	private const byte VcpPowerMode = 0xD6;

	private readonly ConcurrentDictionary<string, bool> _gammaSupport = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, int> _gammaLevel = new(StringComparer.OrdinalIgnoreCase);
	private readonly DimmerOverlay _dimmer = new();
	private bool _disposed;

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
		var target = TargetFor(index);
		if (target is null)
		{
			return false;
		}

		try
		{
			foreach (var handle in target.Handles)
			{
				if (BrightnessRange(handle) is (int min, int max))
				{
					return NativeMethods.SetMonitorBrightness(handle, ToNative(percent, min, max));
				}
			}

			var clamped = Math.Clamp(percent, 0, 100);
			var gammaPart = Math.Max(clamped, DimmerMath.GammaFloorPercent);
			if (!GammaRamp.TrySet(target.DeviceName, gammaPart))
			{
				return false;
			}

			_dimmer.SetLevel(
				target.DeviceName, target.Left, target.Top, target.Right, target.Bottom,
				DimmerMath.OverlayAlpha(clamped));
			_gammaLevel[target.DeviceName] = clamped;
			return true;
		}
		catch (Exception)
		{
			return false;
		}
		finally
		{
			ClosePhysicalMonitors(target.Handles);
		}
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
				if (!NativeMethods.GetVCPFeatureAndVCPFeatureReply(handle, VcpInputSelect, IntPtr.Zero, out var current, out _))
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

	public bool TrySetPower(int index, int dpmValue)
	{
		try
		{
			return WithPhysicalMonitor(index, handle =>
				NativeMethods.SetVCPFeature(handle, VcpPowerMode, (uint)dpmValue));
		}
		catch (Exception)
		{
			return false;
		}
	}

	public int? TryGetPower(int index)
	{
		try
		{
			return WithPhysicalMonitor(index, handle =>
			{
				if (!NativeMethods.GetVCPFeatureAndVCPFeatureReply(handle, VcpPowerMode, IntPtr.Zero, out var current, out _))
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

	public IReadOnlyList<int> GetSupportedInputs(int index)	{
		var target = TargetFor(index);
		if (target is null)
		{
			return [];
		}

		try
		{
			foreach (var handle in target.Handles)
			{
				if (TryReadCapabilities(handle) is string caps
					&& ParseInputValues(caps) is { Count: > 0 } values)
				{
					return values;
				}
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			ClosePhysicalMonitors(target.Handles);
		}

		return [];
	}

	public void HideOverlays()
	{
		try
		{
			_dimmer.HideAll();
		}
		catch (Exception)
		{
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		try
		{
			_dimmer.Dispose();
		}
		catch (Exception)
		{
		}
	}

	private MonitorInfo? Describe(IntPtr hMonitor, int index)
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
			if (brightness is not null)
			{
				return new MonitorInfo(index, name, primary, brightness.Value.Percent, true, false);
			}

			if (SupportsGamma(name))
			{
				return new MonitorInfo(index, name, primary, _gammaLevel.GetOrAdd(name, 100), true, true);
			}

			return new MonitorInfo(index, name, primary, 0, false, false);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private bool SupportsGamma(string deviceName)
	{
		try
		{
			return _gammaSupport.GetOrAdd(deviceName, static name => GammaRamp.IsSupported(name));
		}
		catch (Exception)
		{
			return false;
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
		var target = TargetFor(index);
		if (target is null)
		{
			return false;
		}

		try
		{
			foreach (var handle in target.Handles)
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
			ClosePhysicalMonitors(target.Handles);
		}

		return false;
	}

	private static bool WithPhysicalMonitor(int index, Func<IntPtr, (bool Ok, int? Value)> use, out int? value)
	{
		value = null;
		var target = TargetFor(index);
		if (target is null)
		{
			return false;
		}

		try
		{
			foreach (var handle in target.Handles)
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
			ClosePhysicalMonitors(target.Handles);
		}

		return false;
	}

	private static string? TryReadCapabilities(IntPtr handle)
	{
		try
		{
			if (!NativeMethods.GetCapabilitiesStringLength(handle, out var length) || length == 0 || length > 64000)
			{
				return null;
			}

			var buffer = new char[length];
			if (!NativeMethods.CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length))
			{
				return null;
			}

			var caps = new string(buffer).TrimEnd('\0');
			return string.IsNullOrWhiteSpace(caps) ? null : caps;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static List<int> ParseInputValues(string caps)
	{
		try
		{
			var start = caps.IndexOf("60(", StringComparison.OrdinalIgnoreCase);
			if (start < 0)
			{
				return [];
			}

			var end = caps.IndexOf(')', start + 3);
			if (end < 0)
			{
				return [];
			}

			var values = new List<int>();
			foreach (var token in caps.Substring(start + 3, end - start - 3).Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				if (token.Contains('('))
				{
					continue;
				}

				try
				{
					values.Add(Convert.ToInt32(token, 16));
				}
				catch (Exception)
				{
				}
			}

			return values;
		}
		catch (Exception)
		{
			return [];
		}
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

	private sealed record Target(IntPtr[] Handles, string DeviceName, int Left, int Top, int Right, int Bottom);

	private static Target? TargetFor(int index)
	{
		Target? target = null;
		var current = 0;
		try
		{
			MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref NativeMethods.Rect __, IntPtr ___) =>
			{
				current++;
				if (current == index)
				{
					var handles = OpenPhysicalMonitors(hMonitor);
					var geometry = GeometryOf(hMonitor);
					if (handles is not null && geometry is not null)
					{
						target = new Target(
							handles, geometry.Value.Device,
							geometry.Value.Left, geometry.Value.Top,
							geometry.Value.Right, geometry.Value.Bottom);
					}
					else
					{
						ClosePhysicalMonitors(handles);
					}
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

	private readonly record struct Geometry(string Device, int Left, int Top, int Right, int Bottom);

	private static Geometry? GeometryOf(IntPtr hMonitor)
	{
		try
		{
			var info = new NativeMethods.MonitorInfoEx { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
			if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
			{
				return null;
			}

			if (string.IsNullOrWhiteSpace(info.DeviceName))
			{
				return null;
			}

			return new Geometry(
				info.DeviceName,
				info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string? DeviceNameOf(IntPtr hMonitor) => GeometryOf(hMonitor)?.Device;

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
		public static extern bool GetVCPFeatureAndVCPFeatureReply(
			IntPtr handle, byte code, IntPtr type, out uint current, out uint maximum);

		[DllImport("dxva2.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetCapabilitiesStringLength(IntPtr handle, out uint length);

		[DllImport("dxva2.dll", CharSet = CharSet.Ansi)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool CapabilitiesRequestAndCapabilitiesReply(
			IntPtr handle,
			[Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[] capabilities,
			uint length);
	}
}

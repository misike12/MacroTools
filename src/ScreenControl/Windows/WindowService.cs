using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenControl.Windows;

public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, bool IsMinimized);

public interface IWindowService
{
	WindowInfo? GetForeground();

	WindowInfo? Find(string? filter);

	IReadOnlyList<WindowInfo> GetWindows();

	bool Focus(IntPtr handle);

	bool Minimize(IntPtr handle);

	bool Maximize(IntPtr handle);

	bool Restore(IntPtr handle);

	bool Close(IntPtr handle);

	bool NextDesktop();

	bool PreviousDesktop();
}

public sealed class WindowService : IWindowService
{
	private const int SwRestore = 9;
	private const int SwMinimize = 6;
	private const int SwMaximize = 3;
	private const uint WmClose = 0x0010;
	private const byte VkControl = 0x11;
	private const byte VkLeftWindows = 0x5B;
	private const byte VkLeft = 0x25;
	private const byte VkRight = 0x27;
	private const uint KeyEventKeyUp = 0x0002;

	private readonly int _ownProcessId = Environment.ProcessId;

	public WindowInfo? GetForeground()
	{
		try
		{
			return Describe(NativeMethods.GetForegroundWindow());
		}
		catch (Exception)
		{
			return null;
		}
	}

	public WindowInfo? Find(string? filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
		{
			return GetForeground();
		}

		try
		{
			foreach (var window in GetWindows())
			{
				if (window.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
					|| window.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
				{
					return window;
				}
			}

			return null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	public IReadOnlyList<WindowInfo> GetWindows()
	{
		var found = new List<WindowInfo>();
		try
		{
			NativeMethods.EnumWindowsProc callback = (handle, _) =>
			{
				var info = Describe(handle);
				if (info is not null)
				{
					found.Add(info);
				}

				return true;
			};
			NativeMethods.EnumWindows(callback, IntPtr.Zero);
			GC.KeepAlive(callback);
		}
		catch (Exception)
		{
		}

		return found;
	}

	public bool Focus(IntPtr handle)
	{
		if (handle == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			if (NativeMethods.IsIconic(handle))
			{
				NativeMethods.ShowWindow(handle, SwRestore);
			}

			BringToFront(handle);
			return NativeMethods.GetForegroundWindow() == handle;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool Minimize(IntPtr handle)
	{
		if (handle == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			NativeMethods.ShowWindow(handle, SwMinimize);
			return NativeMethods.IsIconic(handle);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool Maximize(IntPtr handle)
	{
		if (handle == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			NativeMethods.ShowWindow(handle, SwMaximize);
			return NativeMethods.IsZoomed(handle);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool Restore(IntPtr handle)
	{
		if (handle == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			NativeMethods.ShowWindow(handle, SwRestore);
			return !NativeMethods.IsIconic(handle) && !NativeMethods.IsZoomed(handle);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool Close(IntPtr handle)
	{
		if (handle == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			return NativeMethods.PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool NextDesktop() => SwitchDesktop(VkRight);

	public bool PreviousDesktop() => SwitchDesktop(VkLeft);

	private WindowInfo? Describe(IntPtr handle)
	{
		try
		{
			if (handle == IntPtr.Zero || !NativeMethods.IsWindowVisible(handle))
			{
				return null;
			}

			_ = NativeMethods.GetWindowThreadProcessId(handle, out var pid);
			if (pid == _ownProcessId)
			{
				return null;
			}
			var title = WindowTitle(handle);
			if (string.IsNullOrWhiteSpace(title))
			{
				return null;
			}

			string process;
			try
			{
				using var owner = Process.GetProcessById(pid);
				process = owner.ProcessName;
			}
			catch (Exception)
			{
				return null;
			}

			return new WindowInfo(handle, title, process, NativeMethods.IsIconic(handle));
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string WindowTitle(IntPtr handle)
	{
		var length = NativeMethods.GetWindowTextLength(handle);
		if (length <= 0 || length > 512)
		{
			return string.Empty;
		}

		var buffer = new char[length + 1];
		var written = NativeMethods.GetWindowText(handle, buffer, buffer.Length);
		return written > 0 ? new string(buffer, 0, written) : string.Empty;
	}

	private static void BringToFront(IntPtr handle)
	{
		var foreground = NativeMethods.GetForegroundWindow();
		if (foreground == handle)
		{
			return;
		}

		var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
		var currentThread = NativeMethods.GetCurrentThreadId();
		var attached = false;
		try
		{
			attached = foregroundThread != 0
				&& foregroundThread != currentThread
				&& NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
			NativeMethods.SetForegroundWindow(handle);
		}
		catch (Exception)
		{
		}
		finally
		{
			if (attached)
			{
				try
				{
					NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
				}
				catch (Exception)
				{
				}
			}
		}
	}

	private static bool SwitchDesktop(byte arrow)
	{
		try
		{
			var inputs = new[]
			{
				KeyDown(VkLeftWindows),
				KeyDown(VkControl),
				KeyDown(arrow),
				KeyUp(arrow),
				KeyUp(VkControl),
				KeyUp(VkLeftWindows),
			};
			return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) == inputs.Length;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static NativeMethods.Input KeyDown(byte key) => new()
	{
		Type = 1,
		Union = new NativeMethods.InputUnion
		{
			Keyboard = new NativeMethods.KeyboardInput { VirtualKey = key },
		},
	};

	private static NativeMethods.Input KeyUp(byte key) => new()
	{
		Type = 1,
		Union = new NativeMethods.InputUnion
		{
			Keyboard = new NativeMethods.KeyboardInput { VirtualKey = key, Flags = KeyEventKeyUp },
		},
	};

	private static class NativeMethods
	{
		public delegate bool EnumWindowsProc(IntPtr handle, IntPtr data);

		[StructLayout(LayoutKind.Sequential)]
		public struct KeyboardInput
		{
			public ushort VirtualKey;
			public ushort Scan;
			public uint Flags;
			public uint Time;
			public IntPtr ExtraInfo;
		}

		[StructLayout(LayoutKind.Explicit)]
		public struct InputUnion
		{
			[FieldOffset(0)]
			public KeyboardInput Keyboard;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct Input
		{
			public uint Type;
			public InputUnion Union;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindowVisible(IntPtr handle);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsIconic(IntPtr handle);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsZoomed(IntPtr handle);

		[DllImport("user32.dll")]
		public static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetWindowTextLength(IntPtr handle);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetWindowText(
			IntPtr handle,
			[In, Out, MarshalAs(UnmanagedType.LPArray)] char[] text,
			int capacity);

		[DllImport("user32.dll")]
		public static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

		[DllImport("kernel32.dll")]
		public static extern uint GetCurrentThreadId();

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool AttachThreadInput(uint attachTo, uint attachFrom, [MarshalAs(UnmanagedType.Bool)] bool attach);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetForegroundWindow(IntPtr handle);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ShowWindow(IntPtr handle, int command);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		public static extern uint SendInput(uint count, Input[] inputs, int structSize);
	}
}

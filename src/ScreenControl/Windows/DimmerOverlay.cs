using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ScreenControl.Windows;

// Click-through black veil per display, extending software dimming below the floor
// some GPU drivers enforce on SetDeviceGammaRamp (NVIDIA rejects ramps darker than
// 50%, so gamma alone can never dim further). One background thread owns every
// overlay HWND; all operations marshal to it through the message queue. Windows are
// borderless, topmost, invisible to Alt-Tab, never activate, and pass mouse and
// touch input through (WS_EX_TRANSPARENT), so the veil is purely visual. At full
// brightness the window hides; every window dies with the process.
public sealed class DimmerOverlay : IDisposable
{
	private const int WsPopup = unchecked((int)0x80000000);
	private const int WsVisible = 0x10000000;
	private const int WsExLayered = 0x00080000;
	private const int WsExTransparent = 0x00000020;
	private const int WsExTopmost = 0x00000008;
	private const int WsExToolwindow = 0x00000080;
	private const int WsExNoactivate = 0x08000000;
	private const int SwHide = 0;
	private const int SwShownoactivate = 4;
	private const uint SwpNoactivate = 0x0010;
	private const uint SwpShowwindow = 0x0040;
	private const uint LwaAlpha = 0x00000002;
	private const uint WmAppWork = 0x8000;
	private const uint WmDisplaychange = 0x007E;
	private const uint WmNccreate = 0x0081;
	private const uint WmNcdestroy = 0x0082;
	private const uint WmQuit = 0x0012;
	private const int GwlpUserdata = -21;
	private static readonly IntPtr HwndTopmost = new(-1);

	private static readonly WndProc StaticProc = StaticWndProc;

	private readonly object _gate = new();
	private readonly ManualResetEventSlim _ready = new(false);
	private readonly ConcurrentQueue<Action> _pending = new();
	private readonly ConcurrentDictionary<string, OverlayRequest> _levels =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, IntPtr> _windows = new(StringComparer.OrdinalIgnoreCase);
	private Thread? _ui;
	private uint _uiThreadId;
	private bool _disposed;

	public void SetLevel(string deviceName, int left, int top, int right, int bottom, byte alpha)
	{
		if (_disposed)
		{
			return;
		}

		_levels[deviceName] = new OverlayRequest(left, top, right, bottom, alpha);
		Post(() => Apply(deviceName));
	}

	public void HideAll()
	{
		Post(() =>
		{
			foreach (var handle in _windows.Values)
			{
				try
				{
					NativeMethods.ShowWindow(handle, SwHide);
				}
				catch (Exception)
				{
				}
			}
		});
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
		}

		_ready.Dispose();
		try
		{
			if (_uiThreadId != 0)
			{
				NativeMethods.PostThreadMessage(_uiThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
			}

			_ui?.Join(1000);
		}
		catch (Exception)
		{
		}
	}

	private void Post(Action work)
	{
		_pending.Enqueue(work);
		try
		{
			EnsureStarted();
			if (_uiThreadId != 0)
			{
				NativeMethods.PostThreadMessage(_uiThreadId, WmAppWork, IntPtr.Zero, IntPtr.Zero);
			}
		}
		catch (Exception)
		{
		}
	}

	private void EnsureStarted()
	{
		lock (_gate)
		{
			if (_ui is not null || _disposed)
			{
				return;
			}

			_ui = new Thread(UiLoop) { IsBackground = true, Name = "ScreenControl dimmer" };
			_ui.Start();
		}

		_ready.Wait(5000);
	}

	private void UiLoop()
	{
		try
		{
			var atom = RegisterClass();
			if (atom == 0)
			{
				return;
			}

			_uiThreadId = NativeMethods.GetCurrentThreadId();
			NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
			_ready.Set();
			while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
			{
				// Thread messages (posted with PostThreadMessage) carry no HWND, so
				// DispatchMessage would drop them on the floor. Handle them inline.
				if (message.Handle == IntPtr.Zero)
				{
					if (message.Id == WmAppWork)
					{
						DrainQueue();
					}

					continue;
				}

				NativeMethods.TranslateMessage(ref message);
				NativeMethods.DispatchMessage(ref message);
			}
		}
		catch (Exception)
		{
		}
	}

	private void Apply(string deviceName)
	{
		if (!_levels.TryGetValue(deviceName, out var request))
		{
			return;
		}

		try
		{
			if (!_windows.TryGetValue(deviceName, out var handle) || handle == IntPtr.Zero)
			{
				handle = CreateOverlay(deviceName);
				if (handle == IntPtr.Zero)
				{
					return;
				}

				_windows[deviceName] = handle;
			}

			if (request.Alpha == 0)
			{
				NativeMethods.ShowWindow(handle, SwHide);
				return;
			}

			var width = Math.Max(1, request.Right - request.Left);
			var height = Math.Max(1, request.Bottom - request.Top);
			NativeMethods.SetWindowPos(
				handle, HwndTopmost, request.Left, request.Top, width, height,
				SwpNoactivate | SwpShowwindow);
			NativeMethods.SetLayeredWindowAttributes(handle, 0, request.Alpha, LwaAlpha);
			NativeMethods.InvalidateRect(handle, IntPtr.Zero, true);
		}
		catch (Exception)
		{
		}
	}

	private IntPtr CreateOverlay(string deviceName)
	{
		try
		{
			var self = GCHandle.Alloc(this);
			var handle = NativeMethods.CreateWindowEx(
				WsExLayered | WsExTransparent | WsExTopmost | WsExToolwindow | WsExNoactivate,
				"ScreenControlDimmer",
				"ScreenControl dimmer",
				WsPopup,
				0, 0, 1, 1,
				IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
				GCHandle.ToIntPtr(self));
			if (handle == IntPtr.Zero)
			{
				self.Free();
			}

			return handle;
		}
		catch (Exception)
		{
			return IntPtr.Zero;
		}
	}

	private void Resync()
	{
		try
		{
			var rects = CurrentRects();
			foreach (var (device, handle) in _windows)
			{
				try
				{
					if (rects.TryGetValue(device, out var rect))
					{
						NativeMethods.SetWindowPos(
							handle, HwndTopmost, rect.Left, rect.Top,
							Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top),
							SwpNoactivate | SwpShowwindow);
					}
					else
					{
						NativeMethods.ShowWindow(handle, SwHide);
					}
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static Dictionary<string, OverlayRect> CurrentRects()
	{
		var rects = new Dictionary<string, OverlayRect>(StringComparer.OrdinalIgnoreCase);
		try
		{
			NativeMethods.EnumMonitorsProc callback = (IntPtr hMonitor, IntPtr _, ref NativeMethods.Rect __, IntPtr ___) =>
			{
				try
				{
					var info = new NativeMethods.MonitorInfoEx
					{
						Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
					};
					if (NativeMethods.GetMonitorInfo(hMonitor, ref info)
						&& !string.IsNullOrWhiteSpace(info.DeviceName))
					{
						rects[info.DeviceName] = new OverlayRect(
							info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
					}
				}
				catch (Exception)
				{
				}

				return true;
			};
			NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
			GC.KeepAlive(callback);
		}
		catch (Exception)
		{
		}

		return rects;
	}

	private IntPtr InstanceWndProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
	{
		try
		{
			if (message == WmDisplaychange)
			{
				Resync();
				return IntPtr.Zero;
			}
		}
		catch (Exception)
		{
		}

		return NativeMethods.DefWindowProc(handle, message, wParam, lParam);
	}

	private void DrainQueue()
	{
		try
		{
			while (_pending.TryDequeue(out var work))
			{
				try
				{
					work();
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static ushort RegisterClass()
	{
		try
		{
			var definition = new NativeMethods.WndClassEx
			{
				Size = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
				WndProc = StaticProc,
				Cursor = NativeMethods.LoadCursor(IntPtr.Zero, 32512),
				Background = NativeMethods.GetStockObject(NativeMethods.BlackBrush),
				ClassName = "ScreenControlDimmer",
			};
			return NativeMethods.RegisterClassEx(ref definition);
		}
		catch (Exception)
		{
			return 0;
		}
	}

	private static IntPtr StaticWndProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
	{
		try
		{
			if (message == WmNccreate)
			{
				var create = Marshal.PtrToStructure<NativeMethods.CreateStruct>(lParam);
				NativeMethods.SetWindowLongPtr(handle, GwlpUserdata, create.CreateParams);
			}

			var userdata = NativeMethods.GetWindowLongPtr(handle, GwlpUserdata);
			if (userdata != IntPtr.Zero
				&& GCHandle.FromIntPtr(userdata).Target is DimmerOverlay owner)
			{
				var result = owner.InstanceWndProc(handle, message, wParam, lParam);
				if (message == WmNcdestroy)
				{
					GCHandle.FromIntPtr(userdata).Free();
				}

				return result;
			}
		}
		catch (Exception)
		{
		}

		return NativeMethods.DefWindowProc(handle, message, wParam, lParam);
	}

	private sealed record OverlayRequest(int Left, int Top, int Right, int Bottom, byte Alpha);

	private sealed record OverlayRect(int Left, int Top, int Right, int Bottom);

	private delegate IntPtr WndProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

	private static class NativeMethods
	{
		public delegate bool EnumMonitorsProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);

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
		public struct WndClassEx
		{
			public uint Size;
			public uint Style;
			[MarshalAs(UnmanagedType.FunctionPtr)]
			public WndProc WndProc;
			public int ClassExtra;
			public int WindowExtra;
			public IntPtr Instance;
			public IntPtr Icon;
			public IntPtr Cursor;
			public IntPtr Background;
			[MarshalAs(UnmanagedType.LPTStr)]
			public string? MenuName;
			[MarshalAs(UnmanagedType.LPTStr)]
			public string ClassName;
			public IntPtr IconSmall;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct CreateStruct
		{
			public IntPtr CreateParams;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct Message
		{
			public IntPtr Handle;
			public uint Id;
			public IntPtr WParam;
			public IntPtr LParam;
			public uint Time;
			public int X;
			public int Y;
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern ushort RegisterClassEx([In] ref WndClassEx definition);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr CreateWindowEx(
			int exStyle,
			[MarshalAs(UnmanagedType.LPWStr)] string className,
			[MarshalAs(UnmanagedType.LPWStr)] string windowName,
			int style,
			int x, int y, int width, int height,
			IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

		[DllImport("user32.dll")]
		public static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ShowWindow(IntPtr handle, int command);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetWindowPos(
			IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetLayeredWindowAttributes(IntPtr handle, uint key, byte alpha, uint flags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool InvalidateRect(IntPtr handle, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		public static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
		public static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll")]
		public static extern uint GetCurrentThreadId();

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool PeekMessage(out Message message, IntPtr handle, uint filterMin, uint filterMax, uint remove);

		[DllImport("user32.dll")]
		public static extern int GetMessage(out Message message, IntPtr handle, uint filterMin, uint filterMax);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool TranslateMessage(ref Message message);

		[DllImport("user32.dll")]
		public static extern IntPtr DispatchMessage(ref Message message);

		[DllImport("user32.dll")]
		public static extern IntPtr LoadCursor(IntPtr instance, int cursorId);

		[DllImport("gdi32.dll")]
		public static extern IntPtr GetStockObject(int brush);

		public const int BlackBrush = 4;

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumDisplayMonitors(
			IntPtr dc, IntPtr clip, EnumMonitorsProc callback, IntPtr data);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
	}
}



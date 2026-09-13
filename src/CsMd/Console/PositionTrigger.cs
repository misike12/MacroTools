using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CsMd.Console;

// Sends the getpos trigger key to the game. The game runs the bound exec, which prints
// the local player's coordinates to console.log, where the watcher picks them up.
// Same pairing community tools use: a launch-option bind on an otherwise unused key,
// pressed synthetically. Only ever fires while CS2 itself is the foreground window.
public interface IPositionTrigger
{
	bool IsGameFocused();

	bool Tap(byte scanCode);
}

public sealed class PositionTrigger : IPositionTrigger
{
	private const uint InputKeyboard = 1;
	private const uint KeyEventKeyUp = 0x0002;
	private const uint KeyEventScancode = 0x0008;

	public bool IsGameFocused()
	{
		try
		{
			var foreground = GetForegroundWindow();
			if (foreground == IntPtr.Zero)
			{
				return false;
			}

			_ = GetWindowThreadProcessId(foreground, out var pid);
			using var process = Process.GetProcessById(unchecked((int)pid));
			return string.Equals(process.ProcessName, "cs2", StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public bool Tap(byte scanCode)
	{
		try
		{
			var inputs = new Input[]
			{
				new() { Type = InputKeyboard, Vk = 0, Scan = scanCode, Flags = KeyEventScancode },
				new() { Type = InputKeyboard, Vk = 0, Scan = scanCode, Flags = KeyEventScancode | KeyEventKeyUp },
			};
			return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
		}
		catch (Exception)
		{
			return false;
		}
	}

	[StructLayout(LayoutKind.Explicit, Size = 32)]
	private struct Input
	{
		[FieldOffset(0)]
		public uint Type;
		[FieldOffset(8)]
		public ushort Vk;
		[FieldOffset(10)]
		public ushort Scan;
		[FieldOffset(12)]
		public uint Flags;
		[FieldOffset(16)]
		public uint Time;
		[FieldOffset(24)]
		public IntPtr ExtraInfo;
	}

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);
}

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CsMd.Console;

// Sends the getpos trigger key to the game. The game runs the bound exec, which prints
// the local player's coordinates to console.log, where the watcher picks them up.
// The launch-option bind (scancode104) shows up in-game as F13, so this sends a plain
// F13 virtual-key event and lets Windows translate it, the same way every F-key sender
// does. Only ever fires while CS2 itself is the foreground window.
public interface IPositionTrigger
{
	bool IsGameFocused();

	bool Tap(byte virtualKey);
}

public sealed class PositionTrigger : IPositionTrigger
{
	private const uint InputKeyboard = 1;
	private const uint KeyEventKeyUp = 0x0002;

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

	public bool Tap(byte virtualKey)
	{
		try
		{
			var inputs = new Input[]
			{
				new() { Type = InputKeyboard, Vk = virtualKey, Scan = 0, Flags = 0 },
				new() { Type = InputKeyboard, Vk = virtualKey, Scan = 0, Flags = KeyEventKeyUp },
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

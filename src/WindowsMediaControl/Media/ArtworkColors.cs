using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WindowsMediaControl.Media;

internal static class ArtworkColors
{
	public static (string Accent, string Dark) FromImage(byte[] data)
	{
		try
		{
			using var stream = new MemoryStream(data, writable: false);
			using var bitmap = new Bitmap(stream);
			if (bitmap.Width <= 0 || bitmap.Height <= 0)
			{
				return (string.Empty, string.Empty);
			}

			var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
			var locked = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try
			{
				var buffer = new byte[Math.Abs(locked.Stride) * locked.Height];
				Marshal.Copy(locked.Scan0, buffer, 0, buffer.Length);

				var stride = locked.Stride;
				var height = locked.Height;
				var step = Math.Abs(stride);
				int RowStart(int y) => stride > 0 ? y * step : (height - 1 - y) * step;

				var colorful = new List<(int R, int G, int B)>();
				var all = new List<(int R, int G, int B)>();
				var stepX = Math.Max(1, bitmap.Width / 24);
				var stepY = Math.Max(1, bitmap.Height / 24);
				for (var y = 0; y < bitmap.Height; y += stepY)
				{
					var row = RowStart(y);
					for (var x = 0; x < bitmap.Width; x += stepX)
					{
						var index = row + (x * 4);
						var sample = (R: (int)buffer[index + 2], G: (int)buffer[index + 1], B: (int)buffer[index]);
						all.Add(sample);
						var max = Math.Max(sample.R, Math.Max(sample.G, sample.B));
						var min = Math.Min(sample.R, Math.Min(sample.G, sample.B));
						if (max - min >= 48 && max >= 64)
						{
							colorful.Add(sample);
						}
					}
				}

				var source = colorful.Count > 0 ? colorful : all;
				if (source.Count == 0)
				{
					return (string.Empty, string.Empty);
				}

				var red = (int)source.Average(p => p.R);
				var green = (int)source.Average(p => p.G);
				var blue = (int)source.Average(p => p.B);
				return ($"#{red:X2}{green:X2}{blue:X2}", $"#{(int)(red * 0.25):X2}{(int)(green * 0.25):X2}{(int)(blue * 0.25):X2}");
			}
			finally
			{
				bitmap.UnlockBits(locked);
			}
		}
		catch (Exception)
		{
			return (string.Empty, string.Empty);
		}
	}
}

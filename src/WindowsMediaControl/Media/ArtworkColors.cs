using System.Drawing;

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

			var colorful = new List<(int R, int G, int B)>();
			var all = new List<(int R, int G, int B)>();
			var stepX = Math.Max(1, bitmap.Width / 24);
			var stepY = Math.Max(1, bitmap.Height / 24);
			for (var y = 0; y < bitmap.Height; y += stepY)
			{
				for (var x = 0; x < bitmap.Width; x += stepX)
				{
					var pixel = bitmap.GetPixel(x, y);
					var sample = (R: (int)pixel.R, G: (int)pixel.G, B: (int)pixel.B);
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
		catch (Exception)
		{
			return (string.Empty, string.Empty);
		}
	}
}

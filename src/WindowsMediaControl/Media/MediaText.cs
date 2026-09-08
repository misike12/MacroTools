namespace WindowsMediaControl.Media;

internal static class MediaText
{
	public static string FormatDuration(TimeSpan value)
	{
		if (value < TimeSpan.Zero)
		{
			value = TimeSpan.Zero;
		}

		return value.TotalHours >= 1
			? $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}"
			: $"{value.Minutes}:{value.Seconds:D2}";
	}
}

namespace ScreenControl.Windows;

// Maps a software brightness level to the overlay alpha that produces it on top of
// the gamma floor: levels at or above GammaFloorPercent need no overlay, anything
// below fades a black veil in linearly down to fully opaque at zero.
public static class DimmerMath
{
	public const int GammaFloorPercent = 50;

	public static byte OverlayAlpha(int brightnessPercent) =>
		brightnessPercent >= GammaFloorPercent
			? (byte)0
			: (byte)((GammaFloorPercent - brightnessPercent) * 255 / GammaFloorPercent);
}

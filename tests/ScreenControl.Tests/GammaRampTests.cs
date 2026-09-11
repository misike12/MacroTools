using NUnit.Framework;
using ScreenControl.Monitors;

namespace ScreenControl.Tests;

[TestFixture]
public sealed class GammaRampTests
{
	[Test]
	public void Full_brightness_is_the_identity_ramp()
	{
		var ramp = GammaRamp.Build(100);

		Assert.That(ramp.Length, Is.EqualTo(256 * 3));
		for (var i = 0; i < 256; i++)
		{
			var expected = (ushort)(i * 257);
			Assert.That(ramp[i], Is.EqualTo(expected), $"red {i}");
			Assert.That(ramp[i + 256], Is.EqualTo(expected), $"green {i}");
			Assert.That(ramp[i + 512], Is.EqualTo(expected), $"blue {i}");
		}
	}

	[Test]
	public void Zero_brightness_is_black()
	{
		var ramp = GammaRamp.Build(0);

		Assert.That(ramp, Has.All.EqualTo((ushort)0));
	}

	[Test]
	public void Half_brightness_halves_every_entry()
	{
		var ramp = GammaRamp.Build(50);

		Assert.That(ramp[0], Is.EqualTo((ushort)0));
		Assert.That(ramp[255], Is.EqualTo((ushort)32768).Within(1));
		Assert.That(ramp[256 + 128], Is.EqualTo(ramp[128]));
		Assert.That(ramp[512 + 128], Is.EqualTo(ramp[128]));
	}

	[Test]
	public void Out_of_range_values_clamp()
	{
		Assert.That(GammaRamp.Build(-20), Has.All.EqualTo((ushort)0));
		Assert.That(GammaRamp.Build(250)[255], Is.EqualTo((ushort)65535));
	}

	[Test]
	public void Ramp_is_monotonic()
	{
		var ramp = GammaRamp.Build(80);

		for (var i = 1; i < 256; i++)
		{
			Assert.That(ramp[i], Is.GreaterThanOrEqualTo(ramp[i - 1]), $"entry {i}");
		}
	}
}

using NUnit.Framework;
using WindowsMediaControl.Media;

namespace WindowsMediaControl.Tests;

[TestFixture]
[Explicit]
public sealed class PositionLiveTests
{
	[Test]
	public async Task Position_advances_while_playing()
	{
		var service = new WindowsMediaControlService();
		var ct = TestContext.CurrentContext.CancellationToken;

		var first = await service.GetSnapshotAsync(ct);
		if (!first.HasSession || first.Status != PlaybackStatus.Playing)
		{
			Assert.Ignore("Nothing playing right now.");
			return;
		}

		await Task.Delay(3000, ct);
		var second = await service.GetSnapshotAsync(ct);

		TestContext.Out.WriteLine($"pos {first.Position} -> {second.Position} (dur {second.Duration})");
		Assert.That(second.Position, Is.GreaterThan(first.Position));
	}
}

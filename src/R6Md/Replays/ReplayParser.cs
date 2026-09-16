using System.Diagnostics;

namespace R6Md.Replays;

// Runs the vendored r6-dissect build (Tools/README.md) against one replay
// file or match folder and reads the JSON document off stdout. Logs stay on
// stderr by the parser's own contract, so stdout is either a full document
// or nothing parseable. Slow path by design: one parse at a time, bounded
// waits, never a throw for a corrupt or half-written replay.
public class ReplayParser : IDisposable
{
	private static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(30);

	private readonly string? _parserPath;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private bool _disposed;

	public ReplayParser(string? parserPath = null)
	{
		_parserPath = parserPath ?? FindParser();
	}

	public string? ParserPath => string.IsNullOrEmpty(_parserPath) ? null : _parserPath;

	public static string? FindParser()
	{
		foreach (var candidate in new[]
		{
			Path.Combine(AppContext.BaseDirectory, "Tools", "r6-dissect.exe"),
			Path.Combine(AppContext.BaseDirectory, "r6-dissect.exe"),
			"r6-dissect.exe",
		})
		{
			try
			{
				if (File.Exists(candidate))
				{
					return Path.GetFullPath(candidate);
				}
			}
			catch (Exception)
			{
			}
		}

		return null;
	}

	public virtual async Task<ReplayMatch?> ParseAsync(string path, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(_parserPath) || !File.Exists(_parserPath))
		{
			return null;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			using var process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = _parserPath,
					Arguments = $"\"{path}\"",
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true,
				},
			};
			if (!process.Start())
			{
				return null;
			}

			var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
			using var timeout = new CancellationTokenSource(ParseTimeout);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
			try
			{
				await process.WaitForExitAsync(linked.Token);
			}
			catch (OperationCanceledException) when (timeout.IsCancellationRequested)
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch (Exception)
				{
				}

				return null;
			}

			if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
			{
				return null;
			}

			return ReplayJson.ParseMatch(stdout);
		}
		catch (Exception)
		{
			return null;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_gate.Dispose();
		GC.SuppressFinalize(this);
	}
}

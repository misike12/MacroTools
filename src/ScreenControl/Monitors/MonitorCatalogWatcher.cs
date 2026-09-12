namespace ScreenControl.Monitors;

// Detects membership changes in the monitor set (plug/unplug, which also renumbers
// the catalog ids) so the integration can tell the host its Monitors catalog went
// stale. The first check only establishes the baseline and never reports a change.
public sealed class MonitorCatalogWatcher
{
	private string? _lastSignature;

	public bool CheckForChanges(IReadOnlyList<MonitorInfo> monitors)
	{
		var signature = string.Join("\n", monitors
			.Select(monitor => monitor.Index + ":" + monitor.Name)
			.Where(entry => !string.IsNullOrWhiteSpace(entry))
			.OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase));
		if (_lastSignature is null)
		{
			_lastSignature = signature;
			return false;
		}

		if (string.Equals(_lastSignature, signature, StringComparison.Ordinal))
		{
			return false;
		}

		_lastSignature = signature;
		return true;
	}

	public void Reset() => _lastSignature = null;
}

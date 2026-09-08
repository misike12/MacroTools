namespace WindowsMediaControl.Config;

public sealed class MediaSettingsProvider
{
	private readonly object _gate = new();
	private MediaSettings _current = MediaSettings.Default;

	public MediaSettings Current
	{
		get
		{
			lock (_gate)
			{
				return _current;
			}
		}
	}

	public void Update(MediaSettings settings)
	{
		lock (_gate)
		{
			_current = settings;
		}
	}
}

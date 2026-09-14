namespace CsMd;

// Display-safe text for anything the game sends us (player names, clans, chat):
// markup characters become lookalikes so the deck renderer never shows escape
// garbage, and invisible/control characters are stripped. Data stays raw
// everywhere else; only human-facing surfaces sanitize.
public static class DisplayText
{
	public static string Sanitize(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		var builder = new System.Text.StringBuilder(value.Length);
		foreach (var ch in value)
		{
			switch (ch)
			{
				case '<':
					builder.Append('‹');
					break;
				case '>':
					builder.Append('›');
					break;
				case '&':
					builder.Append('＆');
					break;
				case '"':
					builder.Append('″');
					break;
				case '\'':
					builder.Append('′');
					break;
				default:
					if (!char.IsControl(ch) && !IsInvisibleFormat(ch))
					{
						builder.Append(ch);
					}

					break;
			}
		}

		return builder.ToString().Trim();
	}

	private static bool IsInvisibleFormat(char ch) =>
		(ch >= (char)0x200B && ch <= (char)0x200F)
		|| (ch >= (char)0x202A && ch <= (char)0x202E)
		|| (ch >= (char)0x2066 && ch <= (char)0x2069)
		|| ch == (char)0xFEFF;
}

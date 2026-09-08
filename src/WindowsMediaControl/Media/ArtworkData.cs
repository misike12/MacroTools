namespace WindowsMediaControl.Media;

public sealed record ArtworkData(byte[] Data, string MimeType, string Accent = "", string AccentDark = "");

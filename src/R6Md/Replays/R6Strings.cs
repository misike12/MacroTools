namespace R6Md.Replays;

// Feed-line templates. Dynamic names stay arguments so the reader resolves
// every line in its own language; nothing here pre-renders display text.
internal static class R6Strings
{
	public static MacroDeck.Localization.LocalizedString FeedKill(string player, string target) =>
		Strings.Widget.Feed.Kill(player, target);

	public static MacroDeck.Localization.LocalizedString FeedHeadshot(string player, string target) =>
		Strings.Widget.Feed.KillHeadshot(player, target);

	public static MacroDeck.Localization.LocalizedString FeedRound(int round, bool won, string condition) =>
		won ? Strings.Widget.Feed.RoundWon(round, condition) : Strings.Widget.Feed.RoundLost(round, condition);

	public static MacroDeck.Localization.LocalizedString FeedAce(string player) =>
		Strings.Widget.Feed.Ace(player);

	public static MacroDeck.Localization.LocalizedString FeedClutch(string player) =>
		Strings.Widget.Feed.Clutch(player);
}

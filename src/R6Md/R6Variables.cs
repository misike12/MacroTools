using MacroDeck.Sdk.Variables;

namespace R6Md;

internal static class R6Variables
{
	private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(2);

	public static IReadOnlyList<VariableDefinition> CreateDefinitions() =>
	[
		Eager("replay-connected", VariableType.Boolean, Strings.Variables.ReplayConnected.DisplayName(), Strings.Variables.ReplayConnected.Description(), refresh: Refresh),
		Eager("replay-root", VariableType.Text, Strings.Variables.ReplayRoot.DisplayName(), Strings.Variables.ReplayRoot.Description(), refresh: TimeSpan.FromSeconds(30)),
		Eager("match-active", VariableType.Boolean, Strings.Variables.MatchActive.DisplayName(), Strings.Variables.MatchActive.Description(), refresh: Refresh),
		Eager("game-version", VariableType.Text, Strings.Variables.GameVersion.DisplayName(), Strings.Variables.GameVersion.Description(), refresh: TimeSpan.FromSeconds(30)),
		Eager("map", VariableType.Text, Strings.Variables.Map.DisplayName(), Strings.Variables.Map.Description(), refresh: Refresh),
		Eager("mode", VariableType.Text, Strings.Variables.Mode.DisplayName(), Strings.Variables.Mode.Description(), refresh: Refresh),
		Eager("match-type", VariableType.Text, Strings.Variables.MatchType.DisplayName(), Strings.Variables.MatchType.Description(), refresh: Refresh),
		Eager("site", VariableType.Text, Strings.Variables.Site.DisplayName(), Strings.Variables.Site.Description(), refresh: Refresh),
		Eager("round", VariableType.Numeric, Strings.Variables.Round.DisplayName(), Strings.Variables.Round.Description(), refresh: Refresh),
		Eager("rounds-per-match", VariableType.Numeric, Strings.Variables.RoundsPerMatch.DisplayName(), Strings.Variables.RoundsPerMatch.Description(), refresh: Refresh),
		Eager("overtime", VariableType.Boolean, Strings.Variables.Overtime.DisplayName(), Strings.Variables.Overtime.Description(), refresh: Refresh),
		Eager("your-score", VariableType.Numeric, Strings.Variables.YourScore.DisplayName(), Strings.Variables.YourScore.Description(), refresh: Refresh),
		Eager("opp-score", VariableType.Numeric, Strings.Variables.OppScore.DisplayName(), Strings.Variables.OppScore.Description(), refresh: Refresh),
		Eager("your-role", VariableType.Text, Strings.Variables.YourRole.DisplayName(), Strings.Variables.YourRole.Description(), refresh: Refresh),
		Eager("opp-role", VariableType.Text, Strings.Variables.OppRole.DisplayName(), Strings.Variables.OppRole.Description(), refresh: Refresh),
		Eager("round-history", VariableType.Text, Strings.Variables.RoundHistory.DisplayName(), Strings.Variables.RoundHistory.Description(), refresh: Refresh),
		Eager("players-count", VariableType.Numeric, Strings.Variables.PlayersCount.DisplayName(), Strings.Variables.PlayersCount.Description(), refresh: Refresh),
		Eager("top-fragger", VariableType.Text, Strings.Variables.TopFragger.DisplayName(), Strings.Variables.TopFragger.Description(), refresh: Refresh),
		Eager("top-frags", VariableType.Numeric, Strings.Variables.TopFrags.DisplayName(), Strings.Variables.TopFrags.Description(), refresh: Refresh),
		Eager("last-killer", VariableType.Text, Strings.Variables.LastKiller.DisplayName(), Strings.Variables.LastKiller.Description(), refresh: Refresh),
		Eager("last-victim", VariableType.Text, Strings.Variables.LastVictim.DisplayName(), Strings.Variables.LastVictim.Description(), refresh: Refresh),
		Eager("last-headshot", VariableType.Boolean, Strings.Variables.LastHeadshot.DisplayName(), Strings.Variables.LastHeadshot.Description(), refresh: Refresh),
		Eager("your-name", VariableType.Text, Strings.Variables.YourName.DisplayName(), Strings.Variables.YourName.Description(), refresh: Refresh),
		Eager("your-operator", VariableType.Text, Strings.Variables.YourOperator.DisplayName(), Strings.Variables.YourOperator.Description(), refresh: Refresh),
		Eager("your-kills", VariableType.Numeric, Strings.Variables.YourKills.DisplayName(), Strings.Variables.YourKills.Description(), refresh: Refresh),
		Eager("your-deaths", VariableType.Numeric, Strings.Variables.YourDeaths.DisplayName(), Strings.Variables.YourDeaths.Description(), refresh: Refresh),
		Eager("your-assists", VariableType.Numeric, Strings.Variables.YourAssists.DisplayName(), Strings.Variables.YourAssists.Description(), refresh: Refresh),
		Eager("your-headshots", VariableType.Numeric, Strings.Variables.YourHeadshots.DisplayName(), Strings.Variables.YourHeadshots.Description(), refresh: Refresh),
		Eager("session-kills", VariableType.Numeric, Strings.Variables.SessionKills.DisplayName(), Strings.Variables.SessionKills.Description(), refresh: Refresh),
		Eager("session-deaths", VariableType.Numeric, Strings.Variables.SessionDeaths.DisplayName(), Strings.Variables.SessionDeaths.Description(), refresh: Refresh),
		Eager("session-assists", VariableType.Numeric, Strings.Variables.SessionAssists.DisplayName(), Strings.Variables.SessionAssists.Description(), refresh: Refresh),
		Eager("session-hs", VariableType.Numeric, Strings.Variables.SessionHs.DisplayName(), Strings.Variables.SessionHs.Description(), refresh: Refresh),
		Eager("streak", VariableType.Numeric, Strings.Variables.Streak.DisplayName(), Strings.Variables.Streak.Description(), refresh: Refresh),
		Eager("best-streak", VariableType.Numeric, Strings.Variables.BestStreak.DisplayName(), Strings.Variables.BestStreak.Description(), refresh: Refresh),
		Eager("match-outcome", VariableType.Text, Strings.Variables.MatchOutcome.DisplayName(), Strings.Variables.MatchOutcome.Description(), refresh: Refresh),
		Eager("rounds-tracked", VariableType.Numeric, Strings.Variables.RoundsTracked.DisplayName(), Strings.Variables.RoundsTracked.Description(), refresh: Refresh),
	];

	private static VariableDefinition Eager(
		string id,
		VariableType type,
		MacroDeck.Localization.LocalizedText displayName,
		MacroDeck.Localization.LocalizedText description,
		TimeSpan? refresh = null) =>
		VariableDefinition.Eager(id, type) with
		{
			Name = id.Replace("-", "_"),
			DisplayName = displayName,
			Description = description,
			Unit = string.Empty,
			SemanticKind = VariableSemanticKinds.None,
			RefreshInterval = refresh,
		};
}

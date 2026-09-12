using MacroDeck.Sdk.Variables;

namespace CsMd;

internal static class CsVariables
{
	private static readonly TimeSpan FastRefresh = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan SlowRefresh = TimeSpan.FromSeconds(5);

	public static IReadOnlyList<VariableDefinition> CreateDefinitions() =>
	[
		Eager("gsi-connected", VariableType.Boolean, Strings.Variables.GsiConnected.DisplayName(), Strings.Variables.GsiConnected.Description(), refresh: TimeSpan.FromSeconds(2)),
		Eager("map-name", VariableType.Text, Strings.Variables.MapName.DisplayName(), Strings.Variables.MapName.Description(), refresh: SlowRefresh),
		Eager("map-mode", VariableType.Text, Strings.Variables.MapMode.DisplayName(), Strings.Variables.MapMode.Description(), refresh: SlowRefresh),
		Eager("map-phase", VariableType.Text, Strings.Variables.MapPhase.DisplayName(), Strings.Variables.MapPhase.Description(), refresh: FastRefresh),
		Eager("map-round", VariableType.Numeric, Strings.Variables.MapRound.DisplayName(), Strings.Variables.MapRound.Description(), refresh: FastRefresh),
		Eager("ct-score", VariableType.Numeric, Strings.Variables.CtScore.DisplayName(), Strings.Variables.CtScore.Description(), refresh: FastRefresh),
		Eager("t-score", VariableType.Numeric, Strings.Variables.TScore.DisplayName(), Strings.Variables.TScore.Description(), refresh: FastRefresh),
		Eager("ct-name", VariableType.Text, Strings.Variables.CtName.DisplayName(), Strings.Variables.CtName.Description(), refresh: SlowRefresh),
		Eager("t-name", VariableType.Text, Strings.Variables.TName.DisplayName(), Strings.Variables.TName.Description(), refresh: SlowRefresh),
		Eager("round-phase", VariableType.Text, Strings.Variables.RoundPhase.DisplayName(), Strings.Variables.RoundPhase.Description(), refresh: FastRefresh),
		Eager("bomb-state", VariableType.Text, Strings.Variables.BombState.DisplayName(), Strings.Variables.BombState.Description(), refresh: FastRefresh),
		Eager("phase-ends-in", VariableType.Numeric, Strings.Variables.PhaseEndsIn.DisplayName(), Strings.Variables.PhaseEndsIn.Description(), unit: "s", semanticKind: VariableSemanticKinds.Duration, refresh: FastRefresh),
		Eager("my-team", VariableType.Text, Strings.Variables.MyTeam.DisplayName(), Strings.Variables.MyTeam.Description(), refresh: SlowRefresh),
		Eager("player-name", VariableType.Text, Strings.Variables.PlayerName.DisplayName(), Strings.Variables.PlayerName.Description(), refresh: SlowRefresh),
		Eager("alive", VariableType.Boolean, Strings.Variables.Alive.DisplayName(), Strings.Variables.Alive.Description(), refresh: FastRefresh),
		Eager("health", VariableType.Numeric, Strings.Variables.Health.DisplayName(), Strings.Variables.Health.Description(), unit: "hp", refresh: FastRefresh),
		Eager("armor", VariableType.Numeric, Strings.Variables.Armor.DisplayName(), Strings.Variables.Armor.Description(), refresh: FastRefresh),
		Eager("helmet", VariableType.Boolean, Strings.Variables.Helmet.DisplayName(), Strings.Variables.Helmet.Description(), refresh: SlowRefresh),
		Eager("flashed", VariableType.Boolean, Strings.Variables.Flashed.DisplayName(), Strings.Variables.Flashed.Description(), refresh: FastRefresh),
		Eager("money", VariableType.Numeric, Strings.Variables.Money.DisplayName(), Strings.Variables.Money.Description(), unit: "$", refresh: FastRefresh),
		Eager("weapon", VariableType.Text, Strings.Variables.Weapon.DisplayName(), Strings.Variables.Weapon.Description(), refresh: FastRefresh),
		Eager("ammo-clip", VariableType.Numeric, Strings.Variables.AmmoClip.DisplayName(), Strings.Variables.AmmoClip.Description(), refresh: FastRefresh),
		Eager("ammo-reserve", VariableType.Numeric, Strings.Variables.AmmoReserve.DisplayName(), Strings.Variables.AmmoReserve.Description(), refresh: FastRefresh),
		Eager("kills", VariableType.Numeric, Strings.Variables.Kills.DisplayName(), Strings.Variables.Kills.Description(), refresh: SlowRefresh),
		Eager("deaths", VariableType.Numeric, Strings.Variables.Deaths.DisplayName(), Strings.Variables.Deaths.Description(), refresh: SlowRefresh),
		Eager("assists", VariableType.Numeric, Strings.Variables.Assists.DisplayName(), Strings.Variables.Assists.Description(), refresh: SlowRefresh),
		Eager("mvps", VariableType.Numeric, Strings.Variables.Mvps.DisplayName(), Strings.Variables.Mvps.Description(), refresh: SlowRefresh),
		Eager("score", VariableType.Numeric, Strings.Variables.Score.DisplayName(), Strings.Variables.Score.Description(), refresh: SlowRefresh),
		Eager("smokes-active", VariableType.Numeric, Strings.Variables.SmokesActive.DisplayName(), Strings.Variables.SmokesActive.Description(), refresh: FastRefresh),
		Eager("fire-active", VariableType.Numeric, Strings.Variables.FireActive.DisplayName(), Strings.Variables.FireActive.Description(), refresh: FastRefresh),
		Eager("session-kills", VariableType.Numeric, Strings.Variables.SessionKills.DisplayName(), Strings.Variables.SessionKills.Description(), refresh: FastRefresh),
		Eager("session-deaths", VariableType.Numeric, Strings.Variables.SessionDeaths.DisplayName(), Strings.Variables.SessionDeaths.Description(), refresh: FastRefresh),
		Eager("session-kd", VariableType.Numeric, Strings.Variables.SessionKd.DisplayName(), Strings.Variables.SessionKd.Description(), refresh: FastRefresh),
	];

	private static VariableDefinition Eager(
		string id,
		VariableType type,
		MacroDeck.Localization.LocalizedText displayName,
		MacroDeck.Localization.LocalizedText description,
		string? unit = null,
		string? semanticKind = null,
		TimeSpan? refresh = null) =>
		VariableDefinition.Eager(id, type) with
		{
			Name = id.Replace("-", "_"),
			DisplayName = displayName,
			Description = description,
			Unit = unit ?? string.Empty,
			SemanticKind = semanticKind ?? VariableSemanticKinds.None,
			RefreshInterval = refresh,
		};
}

namespace C7GameData;

/// <summary>
/// Exact odds of a battle as <see cref="MapUnit.Fight"/> resolves it: each round
/// the attacker wins with a fixed chance and the loser of the round loses a hit
/// point, until one side has none left.
///
/// Retreats don't change who wins: a unit can only retreat on its last hit
/// point, when losing the round would otherwise kill it, and a retreat counts as
/// a loss for the side that retreats. A defensive bombard before the battle takes
/// a hit point from the attacker with its own chance.
/// </summary>
public static class CombatOdds {
	public static double AttackerWinChance(double roundWinChance, int attackerHitPoints, int defenderHitPoints, double defensiveBombardHitChance = 0) {
		double unharmed = RoundsWinChance(roundWinChance, attackerHitPoints, defenderHitPoints);
		if (defensiveBombardHitChance <= 0) {
			return unharmed;
		}
		double bombarded = RoundsWinChance(roundWinChance, attackerHitPoints - 1, defenderHitPoints);
		return defensiveBombardHitChance * bombarded + (1 - defensiveBombardHitChance) * unharmed;
	}

	// win[a, d] = the chance the attacker wins with a hit points left against d.
	private static double RoundsWinChance(double roundWinChance, int attackerHitPoints, int defenderHitPoints) {
		if (defenderHitPoints <= 0) return 1;
		if (attackerHitPoints <= 0) return 0;

		double[,] win = new double[attackerHitPoints + 1, defenderHitPoints + 1];
		for (int a = 1; a <= attackerHitPoints; a++) {
			win[a, 0] = 1;
		}
		for (int a = 1; a <= attackerHitPoints; a++) {
			for (int d = 1; d <= defenderHitPoints; d++) {
				win[a, d] = roundWinChance * win[a, d - 1] + (1 - roundWinChance) * win[a - 1, d];
			}
		}
		return win[attackerHitPoints, defenderHitPoints];
	}
}

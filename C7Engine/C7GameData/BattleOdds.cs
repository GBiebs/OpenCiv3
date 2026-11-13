namespace C7GameData;

/// <summary>
/// The odds of a battle as <see cref="MapUnit.Fight"/> resolves it, for the
/// statistics' battle luck: each round the attacker wins with a fixed chance and
/// the loser of the round loses a hit point, until one side has none left.
///
/// A unit that may retreat gets its roll as it drops from 2 hit points to 1
/// while its opponent has more than 1, and a retreat counts as a loss for the
/// side that retreats (see CombatResultExtensions). A defensive bombard before
/// the battle takes a hit point from the attacker with its own chance.
///
/// The chance is taken as the battle starts. An army's later members fight with
/// other strengths, which isn't followed, nor whether a defender still has a
/// tile to retreat to.
///
/// Adapted from GBiebs/OpenCiv3.
/// </summary>
public static class BattleOdds {
	public static double AttackerWinChance(double roundWinChance, int attackerHitPoints, int defenderHitPoints,
			double defensiveBombardHitChance = 0, double attackerRetreatChance = 0, double defenderRetreatChance = 0) {
		double unharmed = RoundsWinChance(roundWinChance, attackerHitPoints, defenderHitPoints, attackerRetreatChance, defenderRetreatChance);
		if (defensiveBombardHitChance <= 0 || attackerHitPoints <= 1) {
			return unharmed;
		}
		double bombarded = RoundsWinChance(roundWinChance, attackerHitPoints - 1, defenderHitPoints, attackerRetreatChance, defenderRetreatChance);
		return defensiveBombardHitChance * bombarded + (1 - defensiveBombardHitChance) * unharmed;
	}

	// win[a, d] = the chance the attacker wins with a hit points left against d.
	private static double RoundsWinChance(double p, int attackerHitPoints, int defenderHitPoints, double attackerRetreat, double defenderRetreat) {
		if (defenderHitPoints <= 0) return 1;
		if (attackerHitPoints <= 0) return 0;

		double[,] win = new double[attackerHitPoints + 1, defenderHitPoints + 1];
		for (int a = 1; a <= attackerHitPoints; a++) {
			win[a, 0] = 1;
		}
		for (int a = 1; a <= attackerHitPoints; a++) {
			for (int d = 1; d <= defenderHitPoints; d++) {
				// The defender, hit from 2 to 1, may withdraw: the attacker's win.
				double afterHit = d == 2 && a > 1 ? defenderRetreat + (1 - defenderRetreat) * win[a, 1] : win[a, d - 1];
				// The attacker, hit from 2 to 1, may withdraw: its loss.
				double afterLoss = a == 2 && d > 1 ? (1 - attackerRetreat) * win[1, d] : win[a - 1, d];
				win[a, d] = p * afterHit + (1 - p) * afterLoss;
			}
		}
		return win[attackerHitPoints, defenderHitPoints];
	}
}

using System;
using C7GameData;

namespace C7Engine.AI {
	// Battle odds, for the AI's decisions.
	public static class CombatOdds {
		// The chance that `attacker` beats `defender` where it stands, played
		// out as MapUnit.Fight does: each round the attacker wins with A / (A
		// + D) and the round's loser loses a hit point, until one side has
		// none. It leaves out defensive bombard and retreats, and takes an
		// army's strength from its current fighting member.
		public static double AttackerWinChance(MapUnit attacker, MapUnit defender) {
			double a = attacker.StrengthVersus(defender, CombatRole.Attack, null);
			double d = defender.StrengthVersus(attacker, CombatRole.Defense, null);
			if (a <= 0) {
				return 0;
			}
			if (d <= 0) {
				return 1;
			}
			return WinChance(a / (a + d), attacker.hitPointsRemaining, defender.hitPointsRemaining);
		}

		// The chance of winning `needed` rounds before losing `lives`, winning
		// each round with chance `p`: the sum over k lost rounds (0 to lives -
		// 1) of C(needed - 1 + k, k) p^needed (1 - p)^k.
		public static double WinChance(double p, int lives, int needed) {
			if (needed <= 0) {
				return 1;
			}
			double total = 0;
			double term = Math.Pow(p, needed);
			for (int k = 0; k < lives; k++) {
				total += term;
				term *= (1 - p) * (needed + k) / (k + 1);
			}
			return total;
		}
	}
}

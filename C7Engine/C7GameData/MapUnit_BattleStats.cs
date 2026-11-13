using C7Engine;

namespace C7GameData;

// Each battle, for the statistics' battle luck (see PlayerStats.RecordBattle).
public partial class MapUnit {
	// Records the battle for both sides, from the attacker's chance of winning
	// it as it began.
	private static void RecordBattleStats(MapUnit attacker, MapUnit defender, CombatResult result, double attackerWinChance) {
		if (result == CombatResult.Impossible) {
			return;
		}
		GameData gameData = EngineStorage.gameData;
		attacker.owner.stats.RecordBattle(attacker.owner, gameData, result.AttackerWon(), attackerWinChance, attacker, defender, attacking: true);
		defender.owner.stats.RecordBattle(defender.owner, gameData, result.DefenderWon(), 1 - attackerWinChance, defender, attacker, attacking: false);
	}
}

using System.Collections.Generic;
using System.Linq;

namespace C7GameData;

// Conquests' victory point scoring: the first civ to reach the game's
// victory point limit wins (see VictoryPoints).
public class VictoryPointsVictory : IVictory {
	private readonly int _limit;

	public VictoryPointsVictory(int limit) {
		_limit = limit;
	}

	public string Header() => "Victory Points";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		return new VictoryStatus {
			Player = player,
			VictoryPoints = player.victoryPoints,
		};
	}

	public bool HasVictory(VictoryStatus status) {
		return status.VictoryPoints >= _limit;
	}

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		VictoryStatus top = rivalStatuses.OrderByDescending(r => r.VictoryPoints).FirstOrDefault();
		yield return [
			"Victory points:",
			$"{_limit}",
			"Your victory points:",
			$"{status.VictoryPoints}",
			top?.Player?.civilization?.name ?? "",
			top == null ? "" : $"{top.VictoryPoints}"
		];
	}
}

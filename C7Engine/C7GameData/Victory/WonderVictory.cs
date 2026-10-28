using System.Collections.Generic;
using System.Linq;

namespace C7GameData;

// Conquests' wonder victory, "actually a Time Limit type of victory
// condition" (readme): the game ends as soon as the last great wonder is
// built, and like at the time limit, the best score (or the most victory
// points, in a game that scores them) wins.
public class WonderVictory : IVictory {
	public string Header() => "Wonders";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		return new VictoryStatus {
			Player = player,
			GreatWondersBuilt = gameData.GreatWondersBuilt.Count,
			GreatWonders = gameData.Buildings.Count(b => b.IsGreatWonder()),
		};
	}

	public bool HasVictory(VictoryStatus status) {
		return status.GreatWonders > 0 && status.GreatWondersBuilt >= status.GreatWonders;
	}

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		yield return [
			"Great wonders:",
			$"{status.GreatWonders}",
			"Built so far:",
			$"{status.GreatWondersBuilt}",
			"",
			""
		];
	}
}

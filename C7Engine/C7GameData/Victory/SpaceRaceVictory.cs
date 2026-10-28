using System.Collections.Generic;
using System.Linq;

namespace C7GameData;

// Civ3's space race: launching a complete spaceship (see Spaceship).
public class SpaceRaceVictory : IVictory {
	public string Header() => "Space Race";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		Rules rules = player.rules ?? gameData.rules;
		return new VictoryStatus {
			Player = player,
			SpaceshipParts = Spaceship.PartsBuilt(player, rules),
			SpaceshipPartsNeeded = Spaceship.PartsNeeded(rules),
			SpaceshipLaunched = player.spaceshipLaunched,
		};
	}

	public bool HasVictory(VictoryStatus status) {
		return status.SpaceshipLaunched;
	}

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		VictoryStatus top = rivalStatuses.OrderByDescending(r => r.SpaceshipParts).FirstOrDefault();
		yield return [
			"Spaceship parts:",
			$"{status.SpaceshipPartsNeeded}",
			"Your spaceship:",
			$"{status.SpaceshipParts}",
			top?.Player?.civilization?.name ?? "",
			top == null ? "" : $"{top.SpaceshipParts}"
		];
	}
}

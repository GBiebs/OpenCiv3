using System.Collections.Generic;
using System.Linq;

namespace C7GameData;

// Civ3's diplomatic victory: being elected Secretary-General of the United
// Nations (see UnitedNations).
public class DiplomaticVictory : IVictory {
	public string Header() => "Diplomatic";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		City seat = UnitedNations.Seat(gameData);
		return new VictoryStatus {
			Player = player,
			UnitedNationsOwner = seat?.owner,
			ElectedSecretaryGeneral = gameData.unitedNations.secretaryGeneral == player.id,
		};
	}

	public bool HasVictory(VictoryStatus status) {
		return status.ElectedSecretaryGeneral;
	}

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		yield return [
			"Win a United Nations election",
			"",
			"United Nations built by:",
			status.UnitedNationsOwner?.civilization?.name ?? "nobody",
			"",
			""
		];
	}
}

using System.Linq;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// Embassies (Civ3's "Build an Embassy" mission). Once a civ knows an
	// advance that enables diplomats (Writing), it can pay to open an embassy
	// in the capital of a civ it has met and is at peace with (the Conquests
	// Civilopedia: "Embassies cost gold from your treasury to build"). An
	// embassy shows the other civ's government, treasury and advances, and is
	// needed for diplomatic agreements. It goes both ways: in the user's Civ3
	// saves every one of 102 embassies is stored for both civs, even where
	// one of them doesn't know Writing (so couldn't have built its own).
	//
	// The cost is CivFanatics' measured formula ("A Study of Espionage
	// Missions"): distance × level + base + size, where distance is from their
	// capital to our nearest city, level is the capital's size level (1 town, 2
	// city, 3 metropolis), size its citizens and base the mission's base cost
	// (Conquests: 20).
	public static class Embassies {
		private static ILogger log = Log.ForContext(typeof(Embassies));

		public static bool Has(Player us, Player them) {
			return PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship) && relationship.hasEmbassy;
		}

		// Whether `us` may build an embassy with `them` now (gold aside).
		public static bool CanEstablish(Player us, Player them) {
			return us.HasTechWithFlag(SaveTech.Flag.EnablesDiplomats)
				&& PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)
				&& !relationship.hasEmbassy
				&& !relationship.AtWar()
				&& us.cities.Count > 0
				&& Capital(them) != null;
		}

		public static City Capital(Player player) {
			return player.cities.Find(c => c.IsCapital());
		}

		public static int Cost(GameData gameData, Player us, Player them) {
			City capital = Capital(them);
			if (capital == null || us.cities.Count == 0) {
				return 0;
			}
			int distance = us.cities.Min(c => c.location.DistanceTo(capital.location));
			int baseCost = gameData.rules.Mission(EspionageMission.Embassy)?.baseCost ?? 0;
			return distance * capital.SizeLevel() + baseCost + capital.residents.Count;
		}

		// Builds the embassy if `us` can and has the gold.
		public static bool Establish(GameData gameData, Player us, Player them) {
			if (!CanEstablish(us, them)) {
				return false;
			}
			int cost = Cost(gameData, us, them);
			if (us.gold < cost) {
				return false;
			}
			us.gold -= cost;
			Open(us, them);
			log.Information($"{us} built an embassy with {them} for {cost} gold");
			return true;
		}

		// Gives the two civs embassies with each other.
		public static void Open(Player us, Player them) {
			us.playerRelationships[them.id].hasEmbassy = true;
			them.playerRelationships[us.id].hasEmbassy = true;
		}
	}
}

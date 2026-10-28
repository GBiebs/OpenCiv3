using System.Linq;
using Serilog;

namespace C7GameData {
	// Conquests' victory point scoring (the GAME option "victory locations";
	// Civilopedia "Short Game Modes"; CivFanatics, "Victory Point Scoring"):
	// a civ scores for each great wonder it builds, each enemy unit it kills
	// ("worth their cost, or for barbarians half"), each advance ("the cost in
	// the editor multiplied by" the advance multiple), each city it conquers
	// and each turn it holds a victory location with a defending unit. The
	// GAME limits' multiples weight each; reaching the victory point limit
	// wins, and when the game ends otherwise (the time limit, the last great
	// wonder) the most points win. Only in a game with victory point scoring.
	public static class VictoryPoints {
		private static ILogger log = Log.ForContext(typeof(VictoryPoints));

		public static bool Scoring(GameData gameData) {
			return gameData.victoryConditions?.VictoryLocations == true;
		}

		private static void Award(GameData gameData, Player player, int points, string why) {
			if (!Scoring(gameData) || player == null || player.isBarbarians || points <= 0) {
				return;
			}
			player.victoryPoints += points;
			log.Debug($"{player} scores {points} victory points for {why} ({player.victoryPoints})");
		}

		// `by` killed `dead`.
		public static void OnUnitKilled(GameData gameData, MapUnit dead, Player by) {
			int points = dead.unitType.shieldCost * gameData.rules.victory.unitKillPointsMultiple / 10;
			Award(gameData, by, dead.owner.isBarbarians ? points / 2 : points, $"killing {dead}");
		}

		public static void OnAdvance(GameData gameData, Player player, Tech tech) {
			Award(gameData, player, tech.Cost * gameData.rules.victory.advancePointsMultiple, tech.Name);
		}

		public static void OnGreatWonderBuilt(GameData gameData, Player player, Building wonder) {
			Award(gameData, player, wonder.shieldCost * gameData.rules.victory.wonderPointsMultiple / 10, wonder.name);
		}

		// `winner` took a city of this many citizens.
		public static void OnCityConquered(GameData gameData, Player winner, int citizens) {
			Award(gameData, winner, citizens * gameData.rules.victory.cityConquestPointsMultiple, "a conquest");
		}

		public static void OnPrincessDelivered(GameData gameData, Player player) {
			Award(gameData, player, gameData.rules.victory.princessPoints, "a princess");
		}

		// At the end of every turn, a victory location held by a unit that can
		// defend (Civilopedia: "a unit with a defensive military stat") scores
		// for its owner.
		public static void UpdateForNewTurn(GameData gameData) {
			if (!Scoring(gameData)) {
				return;
			}
			foreach (Tile tile in gameData.map.tiles.Where(t => t.victoryLocation)) {
				MapUnit holder = tile.unitsOnTile.FirstOrDefault(u => !u.owner.isBarbarians && u.unitType.defense > 0);
				if (holder != null) {
					Award(gameData, holder.owner, gameData.rules.victory.victoryLocationPoints, $"holding {tile}");
				}
			}
		}

		// In single player games, every player's starting location is a
		// victory location (Civilopedia).
		public static void MarkStartingLocations(GameData gameData) {
			if (!Scoring(gameData)) {
				return;
			}
			foreach (Player player in gameData.players.Where(p => !p.isBarbarians)) {
				Tile start = player.units.FirstOrDefault(u => u.unitType.isSettler)?.location ?? player.units.FirstOrDefault()?.location;
				if (start != null) {
					start.victoryLocation = true;
				}
			}
		}
	}
}

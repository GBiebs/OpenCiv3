using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// Conquests' elimination and regicide games (Civilopedia "Short Game
	// Modes"). Elimination: a civ that loses a city to an enemy (or as many
	// as the game's limit) is out. Regicide: every civ has a king (its RACE
	// king unit, Caesar for Rome), and a civ whose king is killed is out; in
	// mass regicide every civ has several and is out when the last is
	// killed. A civ that's out loses everything: "all units, colonies,
	// airbases, and cities belonging to that player are destroyed, becoming
	// ruins".
	public static class Elimination {
		private static ILogger log = Log.ForContext(typeof(Elimination));

		public static bool Regicide(GameData gameData) {
			VictoryConditions vc = gameData.victoryConditions;
			return vc?.Regicide == true || vc?.MassRegicide == true;
		}

		// Puts every civ's kings at its start (one, or several in mass
		// regicide), when a regicide game begins.
		public static void PlaceKings(GameData gameData) {
			if (!Regicide(gameData)) {
				return;
			}
			int kings = gameData.victoryConditions.MassRegicide ? gameData.rules.victory.massRegicideKings : 1;
			foreach (Player player in gameData.players.Where(p => !p.isBarbarians)) {
				UnitPrototype king = gameData.unitPrototypes.Find(u => u.name == player.civilization.kingUnitType);
				Tile start = player.units.FirstOrDefault(u => u.unitType.isSettler)?.location ?? player.units.FirstOrDefault()?.location;
				if (king == null || start == null) {
					continue;
				}
				for (int i = 0; i < kings; i++) {
					gameData.SpawnUnit(player, king, start);
				}
			}
		}

		// `dead` was killed by `by` (and has left the map).
		public static void AfterUnitKilled(GameData gameData, MapUnit dead, Player by) {
			Player owner = dead.owner;
			if (!Regicide(gameData) || !dead.unitType.isKing || owner.defeated) {
				return;
			}
			bool kingsLeft = owner.units.Any(u => u.unitType.isKing);
			if (gameData.victoryConditions.Regicide || !kingsLeft) {
				log.Information($"{by} killed {owner}'s king {dead}");
				Eliminate(gameData, owner);
			}
		}

		// `winner` took one of `loser`'s cities.
		public static void OnCityLost(GameData gameData, Player loser, Player winner) {
			loser.citiesLostToEnemies++;
			if (gameData.victoryConditions?.CityElimination == true && !loser.defeated
				&& loser.citiesLostToEnemies >= gameData.rules.victory.cityEliminationCount) {
				log.Information($"{loser} has lost {loser.citiesLostToEnemies} cities to enemies");
				Eliminate(gameData, loser);
			}
		}

		// Everything `loser` has is destroyed, and it's out of the game.
		public static void Eliminate(GameData gameData, Player loser) {
			foreach (MapUnit unit in loser.units.ToList()) {
				gameData.RemoveUnit(unit);
			}
			foreach (City city in loser.cities.ToList()) {
				CityInteractions.DestroyCity(city);
			}
			// Losing the last city or unit usually did it already.
			if (!loser.defeated) {
				gameData.CheckForCivDestructionAndNotifyUi(loser);
			}
		}
	}
}

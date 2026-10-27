using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// Conquests' disease (Civilopedia "Disease"): it "kills citizens and
	// units that spend too much time in unhealthy areas like floodplains,
	// jungles and marsh. Military units that fortify in these terrains are
	// susceptible to disease." Clearing the jungle or marsh ends it, and on
	// flood plains so does Sanitation (TERR's "disease cured by technology",
	// TECH's "disables diseases"). Civ3 doesn't document the chance; TERR
	// holds a disease strength (50 for every stock terrain), which the rules
	// turn into a chance (Rules.CityDiseaseChancePerStrength,
	// UnitDiseaseChancePerStrength).
	public static class Disease {
		private static ILogger log = Log.ForContext(typeof(Disease));

		// Whether `tile` is unhealthy for `player`.
		public static bool IsDiseased(Tile tile, Player player) {
			TerrainType terrain = tile.overlayTerrainType;
			if (terrain == null || !terrain.causesDisease || terrain.diseaseStrength <= 0) {
				return false;
			}
			return !(terrain.diseaseCuredByTech && player.HasTechWithFlag(SaveTech.Flag.DisablesDiseases));
		}

		// The chance, between 0 and 1, that a city loses a citizen to disease
		// this turn: each citizen working an unhealthy tile is a chance.
		public static double CityChance(City city, GameData gameData) {
			double healthy = 1;
			foreach (CityResident resident in city.residents) {
				Tile tile = resident.tileWorked;
				if (tile != null && tile != Tile.NONE && IsDiseased(tile, city.owner)) {
					healthy *= 1 - Percent(tile.overlayTerrainType.diseaseStrength * gameData.rules.CityDiseaseChancePerStrength / 100.0);
				}
			}
			return 1 - healthy;
		}

		// The chance, between 0 and 1, that a unit dies of disease this turn:
		// only a fortified military land unit, outside a city, on an unhealthy
		// tile.
		public static double UnitChance(MapUnit unit, GameData gameData) {
			Tile tile = unit.location;
			if (!unit.isFortified || !unit.IsLandUnit() || !unit.IsCombatUnit() || unit.IsInArmy() || tile.HasCity() || !IsDiseased(tile, unit.owner)) {
				return 0;
			}
			return Percent(tile.overlayTerrainType.diseaseStrength * gameData.rules.UnitDiseaseChancePerStrength / 100.0);
		}

		private static double Percent(double percent) {
			return System.Math.Clamp(percent / 100, 0, 1);
		}

		// Once a turn, after production: each of the player's cities may lose
		// a citizen (never its last), and each of its fortified units out in
		// unhealthy land may die.
		public static void UpdateForNewTurn(GameData gameData, Player player) {
			if (player.isBarbarians) {
				return;
			}
			foreach (City city in player.cities) {
				if (city.residents.Count < 2) {
					continue;
				}
				double chance = CityChance(city, gameData);
				if (chance <= 0 || GameData.rng.NextDouble() >= chance) {
					continue;
				}
				TerrainType where = city.residents.Select(r => r.tileWorked)
					.First(t => t != null && t != Tile.NONE && IsDiseased(t, player)).overlayTerrainType;
				city.RemoveCitizens(1);
				log.Information($"Disease strikes {city.name} ({where.DisplayName} worked), now size {city.residents.Count}");
				new MsgDisease(player.id, city, null, where.DisplayName).send();
			}
			foreach (MapUnit unit in player.units.ToList()) {
				double chance = UnitChance(unit, gameData);
				if (chance <= 0 || GameData.rng.NextDouble() >= chance) {
					continue;
				}
				string where = unit.location.overlayTerrainType.DisplayName;
				log.Information($"{unit} dies of disease at {unit.location}");
				gameData.RemoveUnit(unit);
				new MsgDisease(player.id, null, unit, where).send();
			}
		}
	}
}

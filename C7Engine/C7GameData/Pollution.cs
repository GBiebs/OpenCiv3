using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// Civ3's pollution (Civilopedia "Pollution"; CivFanatics' Civ3 FAQ, "How
	// does pollution work?"). A city's pollution points are its buildings'
	// pollution values, at most 1 with a Recycling Center, plus one for each
	// citizen above metropolis size (12), at most 1 with a Mass Transit
	// System (Rules.PollutionLeftByReducingBuildings). Each point is a chance each turn that one of the city's tiles
	// gets polluted. A polluted tile produces nothing until a worker clears
	// the damage.
	//
	// The city screen's "pollution" in Civ3's saves is the buildings' part
	// alone (every city in the user's saves matches). How much chance a point
	// is isn't documented anywhere we found; Rules.PollutionChancePerPoint
	// holds our figure, a few percent as the user's multiplayer saves suggest.
	public static class Pollution {
		private static ILogger log = Log.ForContext(typeof(Pollution));

		private static IEnumerable<Building> WorkingBuildings(City city) {
			return city.GetBuildings().Select(cb => cb.building).Where(b => !b.IsObsolete(city.owner));
		}

		public static int BuildingPoints(City city) {
			List<Building> buildings = WorkingBuildings(city).ToList();
			int points = buildings.Sum(b => Math.Max(0, b.pollution));
			return buildings.Any(b => b.reducesBuildingPollution) ? Math.Min(points, LeftByReducingBuildings(city)) : points;
		}

		public static int PopulationPoints(City city, GameData gameData) {
			int points = Math.Max(0, city.residents.Count - gameData.rules.MaximumLevel2CitySize);
			return WorkingBuildings(city).Any(b => b.removesPopulationPollution) ? Math.Min(points, LeftByReducingBuildings(city)) : points;
		}

		private static int LeftByReducingBuildings(City city) {
			return Math.Max(0, city.owner.rules?.PollutionLeftByReducingBuildings ?? 1);
		}

		public static int Points(City city, GameData gameData) {
			return BuildingPoints(city) + PopulationPoints(city, gameData);
		}

		// The chance, between 0 and 1, that the city pollutes a tile this turn.
		public static double Chance(City city, GameData gameData) {
			return Math.Clamp(Points(city, gameData) * gameData.rules.PollutionChancePerPoint / 100.0, 0, 1);
		}

		// The tiles pollution can strike: the city's land tiles that can take it
		// (not the city's own, nor one polluted already).
		public static List<Tile> TilesAtRisk(City city, GameData gameData) {
			TerrainImprovement pollution = PollutionOverlay(gameData);
			if (pollution == null) {
				return [];
			}
			return city.location.GetTilesWithinRankDistance(gameData.rules.MaxRankOfWorkableTiles)
				.Where(t => t.IsLand() && t.overlays.CanAdd(t, pollution))
				.ToList();
		}

		private static TerrainImprovement PollutionOverlay(GameData gameData) {
			return gameData.terrainImprovements.Find(i => i.key == Tile.TileOverlays.POLLUTION);
		}

		// Once a turn, after production: each of the player's cities may
		// pollute one of its tiles.
		public static void UpdateForNewTurn(GameData gameData, Player player) {
			TerrainImprovement pollution = PollutionOverlay(gameData);
			if (pollution == null || player.isBarbarians) {
				return;
			}
			foreach (City city in player.cities) {
				double chance = Chance(city, gameData);
				if (chance <= 0 || GameData.rng.NextDouble() >= chance) {
					continue;
				}
				List<Tile> tiles = TilesAtRisk(city, gameData);
				if (tiles.Count == 0) {
					continue;
				}
				Tile struck = tiles[GameData.rng.Next(tiles.Count)];
				struck.overlays.Add(pollution);
				log.Information($"Pollution strikes {city.name} at {struck}");
				new MsgPollution(player.id, city).send();
			}
		}
	}
}

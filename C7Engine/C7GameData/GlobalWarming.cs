using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// Global warming (Civilopedia "Pollution"): "If world pollution reaches
	// sufficiently critical levels, there is a chance that global warming
	// will occur. It can cause forests and jungles to disappear, and
	// grasslands to become plains, and plains to become desert." What each
	// terrain becomes is Civ3's TERR "pollution effect"
	// (TerrainType.globalWarmingResult); tundra and the rest are spared.
	// Civ3's threshold and chance aren't documented, so they're rules: world
	// pollution here is the number of polluted land tiles (players report
	// that the polluted tiles, not the cities' pollution, bring it on, and
	// that it strikes the most polluted areas), and each one beyond a share
	// of the land (Rules.GlobalWarmingThresholdPerThousandLandTiles) is a
	// chance a turn (GlobalWarmingChancePerExcessTile) that warming changes
	// tiles (GlobalWarmingTilesChanged) where the pollution is. Marsh's
	// result in Civ3 is coast ("Coastlines are rising!"); land turning to
	// water isn't done yet, so marsh is spared too.
	public static class GlobalWarming {
		private static ILogger log = Log.ForContext(typeof(GlobalWarming));

		public static int PollutedTiles(GameData gameData) {
			return gameData.map.tiles.Count(t => t.IsLand() && t.HasPollution());
		}

		public static int Threshold(GameData gameData) {
			int land = gameData.map.tiles.Count(t => t.IsLand());
			return land * gameData.rules.GlobalWarmingThresholdPerThousandLandTiles / 1000;
		}

		// The chance, between 0 and 1, of global warming this turn.
		public static double Chance(GameData gameData) {
			int excess = PollutedTiles(gameData) - Threshold(gameData);
			return Math.Clamp(excess * gameData.rules.GlobalWarmingChancePerExcessTile / 100.0, 0, 1);
		}

		// What warming would turn `tile` into, or null if it's spared.
		public static TerrainType ResultFor(Tile tile, GameData gameData) {
			if (!tile.IsLand() || tile.HasCity()) {
				return null;
			}
			string result = tile.overlayTerrainType.globalWarmingResult;
			if (string.IsNullOrEmpty(result)) {
				return null;
			}
			TerrainType to = result == TerrainType.BaseTerrainResult ? tile.baseTerrainType : gameData.terrainTypes.Find(t => t.Key == result);
			if (to == null || to == tile.overlayTerrainType || to.isWater()) {
				return null;
			}
			// Civ3's tundra textures only join grassland and coast.
			bool nextToTundra = tile.neighbors.Values.Append(tile).Any(n => n.baseTerrainType?.Key == "tundra");
			if (nextToTundra && to != tile.baseTerrainType && to.Key != "grassland") {
				return null;
			}
			return to;
		}

		// Warming strikes where the pollution is: a polluted tile it can
		// change, else one within two tiles of pollution, else anywhere.
		private static Tile PickTile(GameData gameData) {
			List<Tile> polluted = gameData.map.tiles.Where(t => t.IsLand() && t.HasPollution()).ToList();
			List<Tile> candidates = polluted.Where(t => ResultFor(t, gameData) != null).ToList();
			if (candidates.Count == 0) {
				candidates = polluted.SelectMany(t => t.GetTilesWithinRankDistance(2)).Distinct()
					.Where(t => ResultFor(t, gameData) != null).ToList();
			}
			if (candidates.Count == 0) {
				candidates = gameData.map.tiles.Where(t => ResultFor(t, gameData) != null).ToList();
			}
			return candidates.Count == 0 ? null : candidates[GameData.rng.Next(candidates.Count)];
		}

		// Once a turn.
		public static void UpdateForNewTurn(GameData gameData) {
			double chance = Chance(gameData);
			if (chance <= 0 || GameData.rng.NextDouble() >= chance) {
				return;
			}
			for (int i = 0; i < gameData.rules.GlobalWarmingTilesChanged; ++i) {
				Tile tile = PickTile(gameData);
				if (tile == null) {
					break;
				}
				Change(gameData, tile);
			}
		}

		// Warming changes `tile`'s terrain. Returns false if it's spared.
		public static bool Change(GameData gameData, Tile tile) {
			TerrainType to = ResultFor(tile, gameData);
			if (to == null) {
				return false;
			}
			TerrainType from = tile.overlayTerrainType;
			bool vegetation = from != tile.baseTerrainType;
			if (to != tile.baseTerrainType) {
				tile.baseTerrainType = to;
			}
			tile.overlayTerrainType = to;
			// A resource the new terrain can't have goes (furs with the forest).
			if (tile.Resource != null && tile.Resource != Resource.NONE && !to.allowedResources.Contains(tile.Resource.Name)) {
				tile.Resource = Resource.NONE;
				tile.ResourceKey = Resource.NONE.Key;
			}
			foreach (Tile shown in TerrainTextureFiles.TilesShowing(tile)) {
				TerrainTextureFiles.AssignTextureDetails(GameData.rng, gameData.terrainTypes, shown);
			}
			gameData.InvalidateCachedTradeNetwork();
			log.Information($"Global warming turns {from.DisplayName} into {to.DisplayName} at {tile}");
			List<ID> knownBy = gameData.players.Where(p => !p.isBarbarians && p.tileKnowledge.isTileKnown(tile)).Select(p => p.id).ToList();
			new MsgGlobalWarming(tile, from.DisplayName, to.DisplayName, vegetation, knownBy).send();
			return true;
		}
	}
}

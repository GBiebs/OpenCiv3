using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;
using static C7GameData.Tile.TileOverlays;

namespace C7GameData {
	// Structures a worker gives itself up to build (Civilopedia), each
	// belonging to its builder's civ (TileOverlays.structureOwner):
	// - a colony, on a luxury or strategic resource outside anyone's borders,
	//   gives the resource to the cities a road joins it to; a border that
	//   takes it in removes it ("no longer needed"); it sees the tiles next
	//   to it;
	// - an outpost, on neutral or our land, sees 2 tiles around, 3 from hills
	//   and 4 from mountains;
	// - an airfield, on neutral or our land with nothing but a road, is an air
	//   base like a city with an airport; in another civ's territory it
	//   becomes theirs if they know Flight, else it's destroyed;
	// - a radar tower, on our land, gives our units within 2 tiles +25%
	//   attack and defense, once however many towers there are, and sees 2
	//   tiles around.
	// Outposts and radar towers in another civ's territory are destroyed. A
	// structure needs a tile with no fortress or other structure on it.
	public static class Structures {
		private static ILogger log = Log.ForContext(typeof(Structures));

		public static bool IsStructure(TerrainImprovement improvement) => improvement?.consumesWorker == true;

		// The structure on `tile`, if any.
		public static TerrainImprovement On(Tile tile) {
			TerrainImprovement holding = tile.overlays.ImprovementAtLayer(TerrainImprovement.Layer.Holdings);
			return IsStructure(holding) ? holding : null;
		}

		// Whether `owner` has a `key` structure on `tile`.
		public static bool Has(Tile tile, string key, Player owner) {
			return On(tile)?.key == key && tile.overlays.structureOwner == owner;
		}

		// Whether `player` may build `structure` on `tile`.
		public static bool CanBuild(Tile tile, TerrainImprovement structure, Player player) {
			if (!tile.IsLand() || tile.HasCity() || tile.overlays.ImprovementAtLayer(TerrainImprovement.Layer.Holdings) != null) {
				return false;
			}
			Player territory = tile.OwningPlayer();
			return structure.key switch {
				COLONY => territory == null && (tile.Resource.Category is ResourceCategory.LUXURY or ResourceCategory.STRATEGIC)
					&& player.KnowsAboutResource(tile.Resource),
				OUTPOST => territory == null || territory == player,
				AIRFIELD => (territory == null || territory == player)
					&& tile.overlays.ImprovementAtLayer(TerrainImprovement.Layer.ResourceDevelopment) == null,
				RADAR_TOWER => territory == player,
				_ => true,
			};
		}

		// `owner`'s worker has built the structure on `tile`.
		public static void OnBuilt(GameData gameData, Tile tile, Player owner) {
			tile.overlays.structureOwner = owner;
			gameData.InvalidateCachedTradeNetwork();
			owner.tileKnowledge.Learn(View(gameData, tile));
			log.Information($"{owner} builds a {On(tile)?.key} at {tile}");
		}

		// After borders change: structures in someone's territory go, or
		// change hands (see above).
		public static void OnTerritoryChanged(GameData gameData) {
			foreach (Tile tile in gameData.map.tiles) {
				Player owner = tile.overlays.structureOwner;
				Player territory = owner == null ? null : tile.OwningPlayer();
				if (territory == null || On(tile) is not TerrainImprovement structure) {
					continue;
				}
				if (structure.key == AIRFIELD && territory != owner) {
					if (territory.HasTech(gameData.Terraforms.Find(t => t.Improvement == structure)?.RequiredTech)) {
						log.Information($"{owner}'s airfield at {tile} passes to {territory}");
						tile.overlays.structureOwner = territory;
						continue;
					}
				} else if (structure.key != COLONY && territory == owner) {
					continue;
				}
				log.Information($"{owner}'s {structure.key} at {tile} is gone: it's in {territory}'s territory");
				tile.overlays.Remove(structure);
			}
		}

		// What the structure on `tile` shows its owner: an outpost further from
		// hills and mountains; a colony and a radar tower the same on any
		// terrain (Civ3's saves); an airfield nothing.
		public static IEnumerable<Tile> View(GameData gameData, Tile tile) {
			StructureRules rules = tile.overlays.structureOwner?.rules?.structures ?? gameData.rules.structures;
			int range = On(tile)?.key switch {
				OUTPOST => tile.overlayTerrainType.Key switch {
					"mountains" or "volcano" => rules.outpostVisionOnMountains,
					"hills" => rules.outpostVisionOnHills,
					_ => rules.outpostVision,
				},
				COLONY => rules.colonyVision,
				RADAR_TOWER => rules.radarVision,
				_ => -1,
			};
			return range < 0 ? [] : tile.GetTilesWithinTileSquare(range).Where(Tile.IsTileValid);
		}

		// The radar tower bonus, if one of its owner's towers covers `unit`.
		public static StrengthBonus? RadarBonus(GameData gameData, MapUnit unit) {
			if (unit.owner == null || !Tile.IsTileValid(unit.location)) {
				return null;
			}
			StructureRules rules = unit.owner.rules?.structures ?? gameData.rules.structures;
			if (unit.location.GetTilesWithinTileSquare(rules.radarRange).Any(t => Tile.IsTileValid(t) && Has(t, RADAR_TOWER, unit.owner))) {
				return new StrengthBonus("Radar tower", rules.radarBonusPercent / 100.0);
			}
			return null;
		}
	}
}

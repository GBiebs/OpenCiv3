using System.Collections.Generic;
using C7GameData;

namespace C7Engine.Editor;

// Everything the map editor can change on a tile, taken at one moment, so an
// edit can be undone and redone (see EditHistory). Cities and units aren't
// here: the brushes that place them keep their own records. Nor are the
// continents and fresh water, which are worked out again for the whole map
// after each edit (MapPainter.Settle).
public sealed class TileState {
	private TerrainType baseTerrain, overlayTerrain;
	private bool bonusShield, snowCapped, pineForest;
	private bool riverN, riverNE, riverE, riverSE, riverS, riverSW, riverW, riverNW;
	private Resource resource;
	private string resourceKey;
	private bool goodyHut, barbarianCamp, victoryLocation, startingLocation;
	private List<TerrainImprovement> improvements;
	private Civ3ExtraInfo art;

	public static TileState Of(Tile tile, GameMap map) {
		return new TileState {
			baseTerrain = tile.baseTerrainType,
			overlayTerrain = tile.overlayTerrainType,
			bonusShield = tile.isBonusShield,
			snowCapped = tile.isSnowCapped,
			pineForest = tile.isPineForest,
			riverN = tile.riverNorth,
			riverNE = tile.riverNortheast,
			riverE = tile.riverEast,
			riverSE = tile.riverSoutheast,
			riverS = tile.riverSouth,
			riverSW = tile.riverSouthwest,
			riverW = tile.riverWest,
			riverNW = tile.riverNorthwest,
			resource = tile.Resource,
			resourceKey = tile.ResourceKey,
			goodyHut = tile.hasGoodyHut,
			barbarianCamp = tile.hasBarbarianCamp,
			victoryLocation = tile.isVictoryLocation,
			startingLocation = map.startingLocations.Contains(tile),
			improvements = [.. tile.overlays.GetImprovements()],
			// The art is replaced, never changed in place (see
			// TerrainTextureFiles), so keeping the reference keeps the art.
			art = tile.ExtraInfo,
		};
	}

	// Puts the tile back as it was when this was taken.
	public void Restore(Tile tile, GameMap map) {
		tile.baseTerrainType = baseTerrain;
		tile.overlayTerrainType = overlayTerrain;
		tile.isBonusShield = bonusShield;
		tile.isSnowCapped = snowCapped;
		tile.isPineForest = pineForest;
		tile.riverNorth = riverN;
		tile.riverNortheast = riverNE;
		tile.riverEast = riverE;
		tile.riverSoutheast = riverSE;
		tile.riverSouth = riverS;
		tile.riverSouthwest = riverSW;
		tile.riverWest = riverW;
		tile.riverNorthwest = riverNW;
		tile.Resource = resource;
		tile.ResourceKey = resourceKey;
		tile.hasGoodyHut = goodyHut;
		tile.hasBarbarianCamp = barbarianCamp;
		tile.isVictoryLocation = victoryLocation;
		bool listed = map.startingLocations.Contains(tile);
		if (startingLocation && !listed) {
			map.startingLocations.Add(tile);
		} else if (!startingLocation && listed) {
			map.startingLocations.Remove(tile);
		}
		foreach (TerrainImprovement improvement in new List<TerrainImprovement>(tile.overlays.GetImprovements())) {
			if (!improvements.Contains(improvement)) {
				tile.overlays.Remove(improvement);
			}
		}
		foreach (TerrainImprovement improvement in improvements) {
			if (!tile.overlays.HasImprovement(improvement)) {
				tile.overlays.Add(improvement);
			}
		}
		tile.ExtraInfo = art;
	}

	// Whether the two states are the same tile, as far as the editor goes.
	public bool SameAs(TileState other) {
		return baseTerrain == other.baseTerrain && overlayTerrain == other.overlayTerrain
			&& bonusShield == other.bonusShield && snowCapped == other.snowCapped && pineForest == other.pineForest
			&& riverN == other.riverN && riverNE == other.riverNE && riverE == other.riverE && riverSE == other.riverSE
			&& riverS == other.riverS && riverSW == other.riverSW && riverW == other.riverW && riverNW == other.riverNW
			&& resource == other.resource && goodyHut == other.goodyHut && barbarianCamp == other.barbarianCamp
			&& victoryLocation == other.victoryLocation && startingLocation == other.startingLocation
			&& improvements.Count == other.improvements.Count && improvements.TrueForAll(other.improvements.Contains)
			&& art == other.art;
	}
}

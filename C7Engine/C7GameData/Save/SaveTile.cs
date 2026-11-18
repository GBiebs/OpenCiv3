using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Linq;

namespace C7GameData.Save {

	public class SaveTile {
		public SaveTile() { }

		// A copy sharing everything with this one, for a LAN host to change
		// a little for one guest (see C7Engine.Network.SnapshotFilter).
		internal SaveTile ShallowCopy() {
			return (SaveTile)MemberwiseClone();
		}

		public SaveTile(Tile tile) {
			id = tile.Id;
			extraInfo = tile.ExtraInfo;
			X = tile.XCoordinate;
			Y = tile.YCoordinate;
			continent = tile.continent;
			isFreshWater = tile.isFreshWater;
			baseTerrain = tile.baseTerrainType.Key;
			overlayTerrain = tile.overlayTerrainType.Key;
			if (tile.Resource != Resource.NONE) {
				resource = tile.ResourceKey;
			}
			if (tile.isBonusShield) {
				features.Add("bonusShield");
			}
			if (tile.isSnowCapped) {
				features.Add("snowCapped");
			}
			if (tile.isPineForest) {
				features.Add("pineForest");
			}
			// In the order the fields are declared on Tile.
			if (tile.riverNorth) features.Add("riverNorth");
			if (tile.riverNortheast) features.Add("riverNortheast");
			if (tile.riverEast) features.Add("riverEast");
			if (tile.riverSoutheast) features.Add("riverSoutheast");
			if (tile.riverSouth) features.Add("riverSouth");
			if (tile.riverSouthwest) features.Add("riverSouthwest");
			if (tile.riverWest) features.Add("riverWest");
			if (tile.riverNorthwest) features.Add("riverNorthwest");
			if (tile.hasBarbarianCamp) {
				features.Add("barbarianCamp");
			}
			if (tile.hasGoodyHut) {
				features.Add("goodyHut");
			}
			if (tile.hasHadForestCleared) {
				features.Add("hasHadForestCleared");
			}
			if (tile.isVictoryLocation) {
				features.Add(VictoryLocationFeature);
			}
			if (tile.isLandmark) {
				features.Add(LandmarkFeature);
			}

			overlays.AddRange(tile.overlays.GetImprovements().Select(i => i.key));
			holdingOwner = HoldingOwnerOf(tile);
		}

		// The ID of the civ whose outpost, colony, airfield or radar tower is
		// on the tile, or null.
		private static ID HoldingOwnerOf(Tile tile) {
			return tile.holdingOwner != null && tile.OwnedHolding() != null ? tile.holdingOwner.id : null;
		}

		// Whether new SaveTile(tile) would be the same as this, field for
		// field, so this can stand in for it. A LAN host saves the map for
		// every snapshot, and most tiles don't change between them.
		internal bool Describes(Tile tile) {
			if (id != tile.Id || extraInfo != tile.ExtraInfo || X != tile.XCoordinate || Y != tile.YCoordinate
				|| continent != tile.continent || isFreshWater != tile.isFreshWater
				|| baseTerrain != tile.baseTerrainType.Key || overlayTerrain != tile.overlayTerrainType.Key
				|| resource != (tile.Resource != Resource.NONE ? tile.ResourceKey : null)
				|| holdingOwner != HoldingOwnerOf(tile)) {
				return false;
			}
			// The features in the order the constructor adds them.
			int feature = 0;
			bool same = HasFeature(tile.isBonusShield, "bonusShield", ref feature)
				&& HasFeature(tile.isSnowCapped, "snowCapped", ref feature)
				&& HasFeature(tile.isPineForest, "pineForest", ref feature)
				&& HasFeature(tile.riverNorth, "riverNorth", ref feature)
				&& HasFeature(tile.riverNortheast, "riverNortheast", ref feature)
				&& HasFeature(tile.riverEast, "riverEast", ref feature)
				&& HasFeature(tile.riverSoutheast, "riverSoutheast", ref feature)
				&& HasFeature(tile.riverSouth, "riverSouth", ref feature)
				&& HasFeature(tile.riverSouthwest, "riverSouthwest", ref feature)
				&& HasFeature(tile.riverWest, "riverWest", ref feature)
				&& HasFeature(tile.riverNorthwest, "riverNorthwest", ref feature)
				&& HasFeature(tile.hasBarbarianCamp, "barbarianCamp", ref feature)
				&& HasFeature(tile.hasGoodyHut, "goodyHut", ref feature)
				&& HasFeature(tile.hasHadForestCleared, "hasHadForestCleared", ref feature)
				&& HasFeature(tile.isVictoryLocation, VictoryLocationFeature, ref feature)
				&& HasFeature(tile.isLandmark, LandmarkFeature, ref feature);
			if (!same || feature != features.Count) {
				return false;
			}
			int overlay = 0;
			foreach (TerrainImprovement improvement in tile.overlays.GetImprovements()) {
				if (overlay >= overlays.Count || overlays[overlay++] != improvement.key) {
					return false;
				}
			}
			return overlay == overlays.Count;
		}

		// The feature a Victory Location has.
		public const string VictoryLocationFeature = "victoryLocation";
		// The feature a landmark has (Tile.isLandmark).
		public const string LandmarkFeature = "landmark";

		private bool HasFeature(bool has, string name, ref int next) {
			return !has || (next < features.Count && features[next++] == name);
		}

		// Lookup tables for converting many tiles, built once rather than
		// searching the lists for every tile. Like List.Find, the first match
		// wins.
		internal class Lookups {
			internal readonly Dictionary<string, TerrainType> terrainTypesByKey = new();
			internal readonly Dictionary<string, Resource> resourcesByKey = new();
			internal readonly Dictionary<string, TerrainImprovement> improvementsByKey = new();

			internal Lookups(List<TerrainType> terrainTypes, List<Resource> resources, List<TerrainImprovement> improvements) {
				foreach (TerrainType tt in terrainTypes) {
					if (tt.Key != null) terrainTypesByKey.TryAdd(tt.Key, tt);
				}
				foreach (Resource r in resources) {
					if (r.Key != null) resourcesByKey.TryAdd(r.Key, r);
				}
				foreach (TerrainImprovement ti in improvements) {
					if (ti.key != null) improvementsByKey.TryAdd(ti.key, ti);
				}
			}

			internal static T Find<T>(Dictionary<string, T> dict, string key) where T : class {
				return key != null && dict.TryGetValue(key, out T value) ? value : null;
			}
		}

		public Tile ToTile(List<TerrainType> terrainTypes, List<Resource> resources, List<TerrainImprovement> improvements) {
			return ToTile(new Lookups(terrainTypes, resources, improvements));
		}

		internal Tile ToTile(Lookups lookups) {
			// A tile can't do without its terrain, so an unknown one is an
			// error in the save.
			TerrainType baseTerrainType = Lookups.Find(lookups.terrainTypesByKey, baseTerrain)
				?? throw new KeyNotFoundException($"The tile at ({X}, {Y}) has unknown base terrain {baseTerrain}");
			TerrainType overlayTerrainType = overlayTerrain == null ? baseTerrainType
				: Lookups.Find(lookups.terrainTypesByKey, overlayTerrain)
					?? throw new KeyNotFoundException($"The tile at ({X}, {Y}) has unknown overlay terrain {overlayTerrain}");
			Tile tile = new Tile(id){
				ExtraInfo = extraInfo,
				XCoordinate = X,
				YCoordinate = Y,
				continent = continent,
				isFreshWater = isFreshWater,
				baseTerrainType = baseTerrainType,
				overlayTerrainType = overlayTerrainType,
				// TODO: load working tile
				ResourceKey = resource is null ? Resource.NONE.Key : resource,
			};
			// One pass over the features, rather than a search for each.
			// Unknown ones are ignored.
			foreach (string feature in features) {
				switch (feature) {
					case "barbarianCamp": tile.hasBarbarianCamp = true; break;
					case "goodyHut": tile.hasGoodyHut = true; break;
					case "hasHadForestCleared": tile.hasHadForestCleared = true; break;
					case "riverNorth": tile.riverNorth = true; break;
					case "riverNortheast": tile.riverNortheast = true; break;
					case "riverEast": tile.riverEast = true; break;
					case "riverSoutheast": tile.riverSoutheast = true; break;
					case "riverSouth": tile.riverSouth = true; break;
					case "riverSouthwest": tile.riverSouthwest = true; break;
					case "riverWest": tile.riverWest = true; break;
					case "riverNorthwest": tile.riverNorthwest = true; break;
					case "bonusShield": tile.isBonusShield = true; break;
					case "snowCapped": tile.isSnowCapped = true; break;
					case "pineForest": tile.isPineForest = true; break;
					case VictoryLocationFeature: tile.isVictoryLocation = true; break;
					case LandmarkFeature: tile.isLandmark = true; break;
				}
			}

			if (tile.ResourceKey == Resource.NONE.Key) {
				tile.Resource = Resource.NONE;
			} else {
				tile.Resource = Lookups.Find(lookups.resourcesByKey, tile.ResourceKey);
				if (tile.Resource == null) {
					Serilog.Log.Warning("The tile at ({X}, {Y}) has unknown resource {Resource}, which is left out", X, Y, tile.ResourceKey);
					tile.Resource = Resource.NONE;
					tile.ResourceKey = Resource.NONE.Key;
				}
			}
			foreach (string key in overlays) {
				TerrainImprovement improvement = Lookups.Find(lookups.improvementsByKey, key);
				if (improvement == null) {
					Serilog.Log.Warning("The tile at ({X}, {Y}) has unknown terrain improvement {Improvement}, which is left out", X, Y, key);
					continue;
				}
				tile.overlays.Add(improvement);
			}

			return tile;
		}
		public Civ3ExtraInfo extraInfo;

		public ID id;
		public int X;
		public int Y;
		public int continent;
		public bool isFreshWater;
		[JsonRequired]
		public string baseTerrain;
		public string overlayTerrain;
		public string resource;
		public List<string> features = new List<string>();
		public List<string> overlays = new List<string>();
		// The civ whose outpost, colony, airfield or radar tower is on the
		// tile, if any. Players
		// are made after the map, so SaveGame fills in Tile.holdingOwner.
		public ID holdingOwner;
	}

}

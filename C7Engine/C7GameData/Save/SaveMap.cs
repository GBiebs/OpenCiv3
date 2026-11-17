using System.Collections.Generic;
using System.Linq;

namespace C7GameData.Save {

	public class SaveMap {
		public int tilesWide, tilesTall;
		public bool wrapHorizontally, wrapVertically;
		// Civ3's polar ice caps (WMAP, WRLD), kept for the scenario editor;
		// the game doesn't use them yet.
		public bool polarIceCaps;
		public int techRate;
		public int optimalNumberOfCities;
		// Null for a map the game didn't generate, and in saves from before
		// it was kept (see MapGeneration).
		public MapGeneration generation;
		public List<SaveTile> tiles = new List<SaveTile>();
		public List<SaveTile> startingLocations = new();
		public SaveMap() { }

		// A copy sharing everything with this one, for a LAN host to change
		// a little for one guest (see C7Engine.Network.SnapshotFilter).
		internal SaveMap ShallowCopy() {
			return (SaveMap)MemberwiseClone();
		}

		public SaveMap(GameMap map) : this(map, null) { }

		// With the map saved before, its tiles that are still the same are
		// used again rather than saved afresh. They're shared, so nothing may
		// change the tiles of either save.
		internal SaveMap(GameMap map, SaveMap before) {
			tilesWide = map.numTilesWide;
			tilesTall = map.numTilesTall;
			wrapHorizontally = map.wrapHorizontally;
			wrapVertically = map.wrapVertically;
			polarIceCaps = map.polarIceCaps;
			techRate = map.techRate;
			optimalNumberOfCities = map.optimalNumberOfCities;
			generation = map.generation;
			List<SaveTile> previous = before?.tiles.Count == map.tiles.Count ? before.tiles : null;
			tiles = new List<SaveTile>(map.tiles.Count);
			for (int i = 0; i < map.tiles.Count; ++i) {
				Tile tile = map.tiles[i];
				SaveTile saved = previous?[i];
				tiles.Add(saved != null && saved.Describes(tile) ? saved : new SaveTile(tile));
			}
			startingLocations = map.startingLocations.ConvertAll(x => new SaveTile(x));
		}
		public GameMap ToGameMap(GameData gd) {
			GameMap gameMap = new GameMap{
				numTilesWide = tilesWide,
				numTilesTall = tilesTall,
				wrapHorizontally = wrapHorizontally,
				wrapVertically = wrapVertically,
				polarIceCaps = polarIceCaps,
				techRate = techRate,
				optimalNumberOfCities = optimalNumberOfCities,
				generation = generation,
			};
			SaveTile.Lookups lookups = new(gd.terrainTypes, gd.Resources, gd.terrainImprovements);
			gameMap.tiles = tiles.ConvertAll(tile => tile.ToTile(lookups));
			foreach (SaveTile st in startingLocations) {
				gameMap.startingLocations.Add(FindTile(gameMap, st.X, st.Y));
			}
			gameMap.computeNeighbors();
			gameMap.barbarianCamps = gameMap.tiles.Where(tile => tile.hasBarbarianCamp).ToList();
			return gameMap;
		}

		// Finds the tile with exactly the given coordinates, or null.
		private static Tile FindTile(GameMap map, int x, int y) {
			if (x >= 0 && y >= 0 && x < map.numTilesWide && y < map.numTilesTall) {
				int index = map.tileCoordsToIndex(x, y);
				if (index >= 0 && index < map.tiles.Count) {
					Tile t = map.tiles[index];
					if (t.XCoordinate == x && t.YCoordinate == y) {
						return t;
					}
				}
			}
			// The tiles aren't laid out as expected, so search for it.
			return map.tiles.Find(t => t.XCoordinate == x && t.YCoordinate == y);
		}
	}

}

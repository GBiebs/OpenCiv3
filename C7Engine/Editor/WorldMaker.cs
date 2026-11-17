using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// Civ3's editor's World Setup (Map > Generate Map) and Clear Map dialogs:
// what they choose, and the scenario with its new map. A new map replaces
// the old one whole (cities and units on it go: they stood on tiles that
// no longer exist), so the editor opens the scenario afresh, as File > New
// does.
public static class WorldMaker {
	// World Setup's choices, as Civ3's dialog has them.
	public sealed class WorldSetup {
		public WorldSize worldSize;
		// A width and height of the player's own (Civ3's Width and Height
		// boxes), or 0 for the world size's.
		public int width, height;
		public WorldCharacteristics.OceanCoverage ocean = WorldCharacteristics.OceanCoverage.Percent_70;
		public WorldCharacteristics.Landform landform = WorldCharacteristics.Landform.Continents;
		public WorldCharacteristics.Climate climate = WorldCharacteristics.Climate.Normal;
		public WorldCharacteristics.Temperature temperature = WorldCharacteristics.Temperature.Temperate;
		public WorldCharacteristics.Age age = WorldCharacteristics.Age.Billion_4;
		public BarbarianActivity barbarians = BarbarianActivity.Roaming;
		// Civ3's World seed: -1 for a random one (its box unticked).
		public int seed = -1;
		public bool polarIceCaps = true, wrapX = true, wrapY = false;
	}

	// The world characteristics a new game would generate its map from,
	// with these choices and the scenario's rules.
	public static WorldCharacteristics Characteristics(SaveGame rules, WorldSetup setup) {
		WorldSize size = setup.worldSize ?? rules.WorldSizes.Find(w => w.name == "Standard") ?? rules.WorldSizes.First();
		if (setup.width > 0 && setup.height > 0 && (setup.width != size.width || setup.height != size.height)) {
			size = size.Clone();
			size.width = setup.width;
			size.height = setup.height;
		}
		return new WorldCharacteristics(rules) {
			worldSize = size,
			oceanCoverage = setup.ocean,
			landform = setup.landform,
			climate = setup.climate,
			temperature = setup.temperature,
			age = setup.age,
			barbarianActivity = setup.barbarians,
			mapSeed = setup.seed,
			wrapX = setup.wrapX,
			wrapY = setup.wrapY,
		};
	}

	// The scenario with a map generated as a new game's would be with the
	// same seed and settings (the seed chosen, or a random one, is kept as
	// the scenario's). Its starting locations are the generator's.
	public static SaveGame Generate(SaveGame scenario, WorldSetup setup) {
		WorldCharacteristics wc = Characteristics(scenario, setup);
		GameMap map = MapGenerator.GenerateMap(wc);
		map.polarIceCaps = setup.polarIceCaps;
		scenario.Map = new SaveMap(map);
		scenario.Seed = wc.mapSeed;
		scenario.BarbarianInfo.barbarianActivity = setup.barbarians;
		ForgetWhatStoodOnTheMap(scenario);
		return scenario;
	}

	// The scenario with a blank map of one terrain (Civ3's Clear Map: a base
	// terrain and a size).
	public static SaveGame Clear(SaveGame scenario, int width, int height, string terrainKey, bool wrapX = true, bool wrapY = false, bool polarIceCaps = false) {
		scenario.Map = ScenarioFiles.BlankMap(scenario, width, height, terrainKey);
		scenario.Map.wrapHorizontally = wrapX;
		scenario.Map.wrapVertically = wrapY;
		scenario.Map.polarIceCaps = polarIceCaps;
		ForgetWhatStoodOnTheMap(scenario);
		return scenario;
	}

	// The scenario with the Map menu's switches changed: X and Y wrapping,
	// polar ice caps.
	public static SaveGame SetMapFlags(SaveGame scenario, bool wrapX, bool wrapY, bool polarIceCaps) {
		scenario.Map.wrapHorizontally = wrapX;
		scenario.Map.wrapVertically = wrapY;
		scenario.Map.polarIceCaps = polarIceCaps;
		return scenario;
	}

	private static void ForgetWhatStoodOnTheMap(SaveGame scenario) {
		scenario.Cities.Clear();
		scenario.Units.Clear();
	}

	// The world characteristics for Redistribute on the editor's map: the
	// game data's rules, and the world size nearest the map's.
	internal static WorldCharacteristics CharacteristicsOf(GameData gameData) {
		GameMap map = gameData.map;
		WorldSize size = gameData.worldSizes.OrderBy(w => Math.Abs(w.width * w.height - map.numTilesWide * map.numTilesTall)).FirstOrDefault()
			?? new WorldSize { width = map.numTilesWide, height = map.numTilesTall, numberOfCivs = 8, distanceBetweenCivs = 12 };
		WorldCharacteristics wc = new() {
			worldSize = size,
			terrainTypes = gameData.terrainTypes,
			resources = gameData.Resources.Where(r => r != null && r.Category != ResourceCategory.NONE).ToList(),
			requiredResources = gameData.unitPrototypes.SelectMany(u => u.requiredResources).Concat(gameData.Buildings.SelectMany(b => b.requiredResources))
				.Select(r => r.Key).ToHashSet(),
			barbarianActivity = BarbarianActivity.Roaming,
		};
		foreach (TerrainType t in gameData.terrainTypes) {
			int Bonus(string key, Tile.YieldType yield) => gameData.terrainImprovements.Find(i => i.key == key)?.GetYieldBonus(t, yield) ?? 0;
			wc.terrainBonuses[t.Key] = (Bonus(Tile.TileOverlays.IRRIGATION, Tile.YieldType.Food), Bonus(Tile.TileOverlays.MINE, Tile.YieldType.Production),
				Bonus(Tile.TileOverlays.ROAD, Tile.YieldType.Commerce));
		}
		return wc;
	}
}

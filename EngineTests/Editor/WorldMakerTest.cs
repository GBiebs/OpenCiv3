using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Editor;

// Phase 2, making a world: Generate Map, Clear Map, the map's switches and
// Redistribute.
public class WorldMakerTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public WorldMakerTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private static SaveGame Rules() => SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave();

	private C7GameData.GameData Open(SaveGame scenario) {
		C7GameData.GameData gameData = ScenarioEditing.Open(scenario, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		return gameData;
	}

	private static string Tiles(SaveMap map) {
		return string.Join(";", map.tiles.OrderBy(t => t.Y).ThenBy(t => t.X).Select(t =>
			$"{t.X},{t.Y}:{t.baseTerrain}/{t.overlayTerrain}/{t.resource}/{string.Join(".", t.features.Order())}"));
	}

	private static WorldMaker.WorldSetup Setup(SaveGame rules, int seed) => new() {
		worldSize = rules.WorldSizes.Find(w => w.name == "Small"),
		ocean = WorldCharacteristics.OceanCoverage.Percent_60,
		landform = WorldCharacteristics.Landform.Pangaea,
		climate = WorldCharacteristics.Climate.Wet,
		temperature = WorldCharacteristics.Temperature.Warm,
		age = WorldCharacteristics.Age.Billion_3,
		barbarians = BarbarianActivity.Restless,
		seed = seed,
	};

	// Phase 2's measure: the editor's Generate Map with a seed gives the
	// same map as a new game with that seed and those settings: every
	// tile's terrain, rivers, resource, hut and flags, and the starts.
	[Fact]
	public void GenerateGivesANewGamesMap() {
		SaveGame rules = Rules();
		WorldMaker.WorldSetup setup = Setup(rules, 5150);
		SaveGame edited = WorldMaker.Generate(ScenarioFiles.NewScenario(rules), setup);

		SaveGame game = Rules();
		Civilization rome = game.Civilizations.First(c => !c.isBarbarian && !c.traits.Contains(Civilization.Trait.Seafaring));
		WorldSize size = game.WorldSizes.Find(w => w.name == "Small");
		new GameSetup {
			playerCivilization = rome,
			difficulty = game.Difficulties.First(),
			worldCharacteristics = new WorldCharacteristics(game) {
				worldSize = size,
				oceanCoverage = setup.ocean,
				landform = setup.landform,
				climate = setup.climate,
				temperature = setup.temperature,
				age = setup.age,
				barbarianActivity = setup.barbarians,
				mapSeed = 5150,
			},
			opponents = Enumerable.Repeat(new SelectedOpponent { isRandom = true }, size.numberOfCivs - 1).ToList(),
			victoryConditions = game.VictoryConditions ?? new VictoryConditions(),
		}.Populate(game);

		Assert.Equal(5150, game.Seed);
		Assert.Equal(5150, edited.Seed);
		Assert.Equal((game.Map.tilesWide, game.Map.tilesTall), (edited.Map.tilesWide, edited.Map.tilesTall));
		Assert.Equal(Tiles(game.Map), Tiles(edited.Map));
		Assert.Equal(game.Map.startingLocations.Select(t => (t.X, t.Y)).Order(), edited.Map.startingLocations.Select(t => (t.X, t.Y)).Order());
		Assert.Equal(size.numberOfCivs, edited.Map.startingLocations.Count);
	}

	// Without a seed, a random one, kept as the scenario's; a custom size,
	// Y wrapping and polar ice caps go with the map and survive a save.
	[Fact]
	public void ARandomSeedACustomSizeAndTheSwitchesAreKept() {
		SaveGame rules = Rules();
		WorldMaker.WorldSetup setup = Setup(rules, -1);
		setup.width = 80;
		setup.height = 60;
		setup.wrapY = true;
		setup.polarIceCaps = true;
		SaveGame scenario = WorldMaker.Generate(ScenarioFiles.NewScenario(rules), setup);
		Assert.True(scenario.Seed > 0);
		Assert.Equal((80, 60), (scenario.Map.tilesWide, scenario.Map.tilesTall));

		using TempDirectory dir = new();
		string path = dir.File("world.json");
		ScenarioFiles.Save(scenario, path);
		SaveGame again = SaveGame.Load(path, s => s);
		Assert.True(again.Map.wrapHorizontally && again.Map.wrapVertically && again.Map.polarIceCaps);
		Assert.Equal(scenario.Seed, again.Seed);
		C7GameData.GameData gameData = Open(ScenarioFiles.ForEditing(again));
		Assert.True(gameData.map.wrapVertically);
		Assert.True(gameData.map.polarIceCaps);
		Assert.Equal(80 * 60 / 2, gameData.map.tiles.Count);
	}

	// Clear Map: one terrain, the size chosen, nothing on it.
	[Fact]
	public void ClearMakesABlankMap() {
		SaveGame rules = Rules();
		SaveGame scenario = WorldMaker.Generate(ScenarioFiles.NewScenario(rules), Setup(rules, 77));
		WorldMaker.Clear(scenario, 60, 40, "grassland", wrapX: false);
		Assert.All(scenario.Map.tiles, t => Assert.Equal("grassland", t.overlayTerrain));
		Assert.Equal(30 * 40, scenario.Map.tiles.Count);
		Assert.Empty(scenario.Map.startingLocations);
		Assert.False(scenario.Map.wrapHorizontally);
		C7GameData.GameData gameData = Open(ScenarioFiles.ForEditing(scenario));
		Assert.False(gameData.map.wrapHorizontally);
		Assert.All(gameData.map.tiles, t => Assert.NotNull(t.ExtraInfo));
	}

	// Redistribute: each places afresh by the generator's rules (resources
	// on terrain that allows them, huts on land, as many starts as the
	// world size has civs), is one undo step, and the seed decides.
	[Fact]
	public void RedistributeRerollsAndIsUndone() {
		SaveGame rules = Rules();
		C7GameData.GameData gameData = Open(ScenarioFiles.ForEditing(WorldMaker.Generate(ScenarioFiles.NewScenario(rules), Setup(rules, 31337))));
		MapPainter painter = new(gameData);
		GameMap map = gameData.map;
		string Snapshot() => string.Join(";", map.tiles.Select(t => $"{t.Resource?.Key}/{t.hasGoodyHut}/{t.isBonusShield}"))
			+ "|" + string.Join(";", map.startingLocations.Select(t => $"{t.XCoordinate},{t.YCoordinate}"));
		string before = Snapshot();

		foreach (MapGenerator.Redistribution what in System.Enum.GetValues<MapGenerator.Redistribution>()) {
			string was = Snapshot();
			EditHistory.Step step = painter.Redistribute(what, 1234);
			Assert.NotNull(step);
			Assert.NotEqual(was, Snapshot());
		}
		Assert.All(map.tiles.Where(t => t.Resource != Resource.NONE && t.Resource.Category != ResourceCategory.NONE),
			t => Assert.Contains(t.Resource.Key, t.overlayTerrainType.allowedResources));
		Assert.All(map.tiles.Where(t => t.hasGoodyHut), t => Assert.False(t.overlayTerrainType.IsWater));
		Assert.All(map.tiles.Where(t => t.isBonusShield), t => Assert.True(t.baseTerrainType.IsGrassland));
		Assert.Equal(gameData.worldSizes.Find(w => w.name == "Small").numberOfCivs, map.startingLocations.Count);
		Assert.Equal(4, painter.History.UndoCount);
		string after = Snapshot();

		while (painter.Undo() != null) { }
		Assert.Equal(before, Snapshot());

		// The same seed places the same again.
		C7GameData.GameData other = Open(ScenarioFiles.ForEditing(WorldMaker.Generate(ScenarioFiles.NewScenario(rules), Setup(rules, 31337))));
		MapPainter otherPainter = new(other);
		foreach (MapGenerator.Redistribution what in System.Enum.GetValues<MapGenerator.Redistribution>()) {
			otherPainter.Redistribute(what, 1234);
		}
		map = other.map;
		Assert.Equal(after, Snapshot());
	}
	// Redistribute places afresh: what was there before makes no difference
	// (the same seed gives the same huts and resources on a map that had
	// them as on one that hadn't).
	[Fact]
	public void RedistributeIgnoresWhatWasThere() {
		SaveGame rules = Rules();
		string Place(bool emptyFirst, MapGenerator.Redistribution what) {
			C7GameData.GameData gameData = Open(ScenarioFiles.ForEditing(WorldMaker.Generate(ScenarioFiles.NewScenario(rules), Setup(rules, 99))));
			if (emptyFirst) {
				foreach (Tile t in gameData.map.tiles) {
					t.hasGoodyHut = false;
					t.Resource = Resource.NONE;
				}
			}
			new MapPainter(gameData).Redistribute(what, 4321);
			return string.Join(";", gameData.map.tiles.Select(t => $"{t.Resource?.Key}/{t.hasGoodyHut}"));
		}
		Assert.Equal(Place(false, MapGenerator.Redistribution.GoodyHuts), Place(true, MapGenerator.Redistribution.GoodyHuts),
			new RedistributedHuts());
		Assert.Equal(Place(false, MapGenerator.Redistribution.Resources).Split(';').Select(x => x.Split('/')[0]),
			Place(true, MapGenerator.Redistribution.Resources).Split(';').Select(x => x.Split('/')[0]));
	}

	// Compares only the huts of two tile lists (the resources differ where
	// one map's were taken off).
	private sealed class RedistributedHuts : IEqualityComparer<string> {
		public bool Equals(string a, string b) => a.Split(';').Select(x => x.Split('/')[1]).SequenceEqual(b.Split(';').Select(x => x.Split('/')[1]));
		public int GetHashCode(string s) => 0;
	}
	// Civ3's View > Options "randomize bonus grassland": painted grassland
	// is bonus grassland about a third of the time; a drag over it again
	// doesn't roll again; off, never.
	[Fact]
	public void PaintedGrasslandCanBeBonusGrassland() {
		SaveGame rules = Rules();
		C7GameData.GameData gameData = Open(ScenarioFiles.NewScenario(rules));
		MapPainter painter = new(gameData) { RandomizeBonusGrassland = true };
		TerrainBrush grass = painter.TerrainBrushes().First(b => b.Name == "Grassland");
		painter.BeginStroke("Paint Grassland");
		for (int y = 20; y < 80; y += 8) {
			for (int x = 20; x < 80; x += 8) {
				painter.PaintTerrain(gameData.map.tileAt(x, y), 7, grass);
			}
		}
		painter.EndStroke();
		List<Tile> land = gameData.map.tiles.Where(t => t.overlayTerrainType.IsGrassland).ToList();
		double share = land.Count(t => t.isBonusShield) / (double)land.Count;
		Assert.InRange(share, 0.25, 0.42);
		string bonus = string.Join(",", land.Select(t => t.isBonusShield));
		painter.BeginStroke("Paint Grassland");
		painter.PaintTerrain(gameData.map.tileAt(44, 44), 3, grass);
		Assert.Null(painter.EndStroke());
		Assert.Equal(bonus, string.Join(",", land.Select(t => t.isBonusShield)));

		painter.RandomizeBonusGrassland = false;
		painter.BeginStroke("Paint Grassland");
		painter.PaintTerrain(gameData.map.tileAt(44, 44), 3, grass);
		painter.EndStroke();
		Assert.All(MapPainter.TilesUnder(gameData.map.tileAt(44, 44), 3), t => Assert.False(t.isBonusShield));
	}
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Editor;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Editor;

// Phase 1's measures: a map painted from an ocean plays in the game, and
// every overlay survives save and load.
public class PaintedMapPlaysTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public PaintedMapPlaysTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private (C7GameData.GameData, MapPainter) NewScenario() {
		SaveGame rules = SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave();
		C7GameData.GameData gameData = ScenarioEditing.Open(ScenarioFiles.NewScenario(rules), fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		return (gameData, new MapPainter(gameData));
	}

	private static void Paint(MapPainter painter, string terrain, int size, params Tile[] tiles) {
		TerrainBrush brush = painter.TerrainBrushes().First(b => b.Name == terrain);
		painter.BeginStroke("Paint " + terrain);
		foreach (Tile tile in tiles) {
			painter.PaintTerrain(tile, size, brush);
		}
		painter.EndStroke();
	}

	private static bool Place(MapPainter painter, string overlay, Tile tile) {
		OverlayBrush brush = painter.OverlayBrushes().First(b => b.Name == overlay);
		painter.BeginStroke("Place " + overlay);
		painter.PlaceOverlay(tile, brush);
		return painter.EndStroke() != null;
	}

	private static SaveGame SaveAndReopen(C7GameData.GameData gameData, string name) {
		using TempDirectory dir = new();
		string path = dir.File(name);
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, new ScenarioInfo { customMap = true, title = "Painted" }), path);
		return SaveGame.Load(path, s => s);
	}

	// Every overlay of the brush, each on its own tile, and a river: after a
	// save and a reopening, the same things on the same tiles.
	[Fact]
	public void EveryOverlaySurvivesSaveAndLoad() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Paint(painter, "Grassland", 7, gameData.map.tileAt(50, 50), gameData.map.tileAt(60, 50));
		Paint(painter, "Hills", 1, gameData.map.tileAt(48, 50));
		List<Tile> land = gameData.map.tiles.Where(t => t.overlayTerrainType.IsGrassland).OrderBy(t => t.YCoordinate).ThenBy(t => t.XCoordinate).ToList();
		int next = 0;
		foreach (OverlayBrush brush in painter.OverlayBrushes().Where(b => b.What != OverlayBrush.Kind.River)) {
			Tile tile = brush.Name == "Mine" ? gameData.map.tileAt(48, 50) : land[next++];
			Assert.True(Place(painter, brush.Name, tile), $"{brush.Name} wasn't placed on {tile}");
		}
		painter.BeginStroke("Place River");
		painter.PlaceRiver(gameData.map.tileAt(54, 50), TileDirection.NORTHEAST);
		painter.PlaceRiver(gameData.map.tileAt(55, 49), TileDirection.SOUTHEAST);
		painter.EndStroke();
		Dictionary<(int, int), string> before = gameData.map.tiles.ToDictionary(t => (t.XCoordinate, t.YCoordinate), Describe(painter));

		C7GameData.GameData again = ScenarioEditing.Open(ScenarioFiles.ForEditing(SaveAndReopen(gameData, "overlays.json")), fixture.behaviors);
		MapPainter reopened = new(again);
		Dictionary<(int, int), string> after = again.map.tiles.ToDictionary(t => (t.XCoordinate, t.YCoordinate), Describe(reopened));

		Assert.Equal(15, painter.OverlayBrushes().Count(b => b.What != OverlayBrush.Kind.River));
		Assert.Equal(before.Count(kv => kv.Value != ""), after.Count(kv => kv.Value != ""));
		Assert.All(before, kv => Assert.Equal(kv.Value, after[kv.Key]));
		Assert.Equal(gameData.map.startingLocations.Select(t => (t.XCoordinate, t.YCoordinate)), again.map.startingLocations.Select(t => (t.XCoordinate, t.YCoordinate)));
	}

	private static System.Func<Tile, string> Describe(MapPainter painter) {
		return t => string.Join(", ", painter.ErasablesOn(t).Select(e => e.Name))
			+ (t.riverNorth ? " N" : "") + (t.riverEast ? " E" : "") + (t.riverSouth ? " S" : "") + (t.riverWest ? " W" : "");
	}

	// Two islands painted on the ocean, with starting locations: saved as a
	// scenario with a map and no players, then started as the game's
	// new-game screens start it (its map kept, civs on its starting
	// locations) and played 5 turns by the computer.
	[Fact]
	public async Task AMapPaintedFromTheOceanPlays() {
		(C7GameData.GameData gameData, MapPainter painter) = NewScenario();
		Tile west = gameData.map.tileAt(30, 50), east = gameData.map.tileAt(70, 50);
		Paint(painter, "Grassland", 7, west, gameData.map.tileAt(34, 50), east, gameData.map.tileAt(74, 50));
		Paint(painter, "Plains", 3, gameData.map.tileAt(32, 54), gameData.map.tileAt(72, 46));
		Paint(painter, "Hills", 1, gameData.map.tileAt(36, 50));
		Assert.True(Place(painter, "Starting Location", west));
		Assert.True(Place(painter, "Starting Location", east));
		Assert.True(Place(painter, "Road", gameData.map.tileAt(31, 51)));
		Assert.True(Place(painter, "Goody Hut", gameData.map.tileAt(28, 52)));

		SaveGame scenario = SaveAndReopen(gameData, "painted.json");
		Assert.Empty(scenario.Players);
		Assert.Equal(2, scenario.Map.startingLocations.Count);

		Difficulty difficulty = scenario.Difficulties.First();
		new GameSetup {
			playerCivilization = scenario.Civilizations.First(c => !c.isBarbarian),
			difficulty = difficulty,
			worldCharacteristics = new WorldCharacteristics(scenario) { worldSize = scenario.WorldSizes.First(), mapSeed = 1 },
			opponents = [new SelectedOpponent { isRandom = true }],
			victoryConditions = scenario.VictoryConditions ?? new VictoryConditions(),
		}.Populate(scenario);
		await CreateGame.createGame(scenario, _ => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;

		List<Player> civs = game.players.Where(p => !p.isBarbarians).ToList();
		Assert.Equal(2, civs.Count);
		Assert.Equal(100, game.map.numTilesWide);
		HashSet<(int, int)> startTiles = [(30, 50), (70, 50)];
		Assert.All(civs, p => Assert.Contains((p.units[0].location.XCoordinate, p.units[0].location.YCoordinate), startTiles));
		Assert.True(game.map.tileAt(31, 51).HasRoad());

		game.observerMode = true;
		foreach (Player player in game.players) {
			player.isHuman = false;
		}
		try {
			TurnHandling.OnBeginTurn();
			TurnHandling.InitTurnData();
			Task firstTurn = TurnHandling.AdvanceTurn();
			EngineWaits.WaitForStartTurnMessage(_ => { });
			firstTurn.GetAwaiter().GetResult();
			for (int turn = 0; turn < 5 && !game.gameOver; ++turn) {
				new MsgEndTurn { turn = game.turn }.send();
				EngineWaits.WaitForStartTurnMessage(_ => { });
			}
		} finally {
			game.observerMode = false;
		}
		Assert.True(game.turn >= 5);
		Assert.All(civs, p => Assert.NotEmpty(p.cities));
	}
}

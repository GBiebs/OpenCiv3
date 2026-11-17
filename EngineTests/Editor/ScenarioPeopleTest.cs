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

// Phase 3: the scenario's players, and the city and unit brushes.
public class ScenarioPeopleTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public ScenarioPeopleTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	// A new scenario with land at (50, 50) and two players, the Romans and
	// the Greeks (and the barbarians who come with them).
	private (C7GameData.GameData, MapPainter, Player rome, Player greece) TwoPlayers() {
		SaveGame scenario = ScenarioFiles.NewScenario(SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave());
		ScenarioInfo info = new();
		ScenarioPlayers.Add(scenario, scenario.Civilizations.First(c => c.name == "Rome"), info);
		ScenarioPlayers.Add(scenario, scenario.Civilizations.First(c => c.name == "Greece"), info);
		Assert.True(info.customPlayerData);
		C7GameData.GameData gameData = ScenarioEditing.Open(scenario, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		MapPainter painter = new(gameData);
		painter.BeginStroke("Paint Grassland");
		painter.PaintTerrain(gameData.map.tileAt(50, 50), 7, painter.TerrainBrushes().First(b => b.Name == "Grassland"));
		painter.EndStroke();
		return (gameData, painter, gameData.players.First(p => p.civilization.name == "Rome"), gameData.players.First(p => p.civilization.name == "Greece"));
	}

	private static T Act<T>(MapPainter painter, string name, System.Func<T> act) {
		painter.BeginStroke(name);
		T result = act();
		painter.EndStroke();
		return result;
	}

	[Fact]
	public void PlayersAreAddedWithTheBarbarians() {
		(C7GameData.GameData gameData, _, _, _) = TwoPlayers();
		Assert.Equal(["Barbarians", "Rome", "Greece"], gameData.players.Select(ScenarioPlayers.NameOf));
		Assert.All(gameData.players, p => Assert.Empty(p.units));
		Assert.Equal(3, gameData.players.Select(p => p.id).Distinct().Count());
	}

	// A city is placed for the active player: named from its civ's list,
	// the first its capital with the palace, its borders the game's; not
	// on water, next to a city or for the barbarians. Undo takes it away
	// and its borders with it; redo brings the same city back.
	[Fact]
	public void ACityIsPlacedForThePlayerAndUndone() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, _) = TwoPlayers();
		Tile tile = gameData.map.tileAt(50, 50);
		City city = Act(painter, "Place City", () => painter.PlaceCity(tile, rome));
		Assert.Equal(rome.civilization.cityNames[0], city.name);
		Assert.True(city.IsCapital());
		Assert.Contains(city.constructed_buildings, b => b.building.isCenterOfEmpire);
		Assert.Single(city.residents);
		Assert.Equal(rome, tile.OwningPlayer());
		Assert.Equal(rome, tile.neighbors[TileDirection.NORTH].OwningPlayer());

		Assert.NotNull(MapPainter.CantPlaceCity(tile.neighbors[TileDirection.EAST], rome));
		Assert.NotNull(MapPainter.CantPlaceCity(gameData.map.tileAt(10, 10), rome));
		Assert.NotNull(MapPainter.CantPlaceCity(gameData.map.tileAt(54, 50), gameData.players.First(p => p.isBarbarians)));
		City second = Act(painter, "Place City", () => painter.PlaceCity(gameData.map.tileAt(54, 50), rome));
		Assert.Equal(rome.civilization.cityNames[1], second.name);
		Assert.False(second.IsCapital());

		painter.Undo();
		painter.Undo();
		Assert.Empty(gameData.cities);
		Assert.Null(tile.cityAtTile);
		Assert.Null(tile.OwningPlayer());
		painter.Redo();
		Assert.Same(city, tile.cityAtTile);
		Assert.Equal(rome, tile.OwningPlayer());
	}

	// A unit for the active player: land units on land, ships at sea or in
	// a coastal city, never with another player's.
	[Fact]
	public void UnitsArePlacedWhereTheyCanBe() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, Player greece) = TwoPlayers();
		UnitPrototype warrior = gameData.unitPrototypes.First(p => p.name == "Warrior");
		UnitPrototype galley = gameData.unitPrototypes.First(p => p.name == "Galley");
		Tile land = gameData.map.tileAt(50, 50), sea = gameData.map.tileAt(10, 10);

		MapUnit unit = Act(painter, "Place Warrior", () => painter.PlaceUnit(land, warrior, rome));
		Act(painter, "Place Warrior", () => painter.PlaceUnit(land, warrior, rome));
		Assert.Equal(2, land.unitsOnTile.Count);
		Assert.Equal(rome, unit.owner);
		Assert.Contains(unit, rome.units);
		Assert.NotNull(MapPainter.CantPlaceUnit(land, warrior, greece));
		Assert.NotNull(MapPainter.CantPlaceUnit(sea, warrior, rome));
		Assert.NotNull(MapPainter.CantPlaceUnit(land, galley, rome));
		Assert.Null(MapPainter.CantPlaceUnit(sea, galley, greece));

		// A city on the island's edge is coastal: ships may be in it.
		Tile edge = gameData.map.tileAt(53, 53);
		Act(painter, "Place City", () => painter.PlaceCity(edge, greece));
		Assert.True(edge.NeighborsWater());
		Assert.Null(MapPainter.CantPlaceUnit(edge, galley, greece));
		Assert.NotNull(MapPainter.CantPlaceUnit(edge, galley, rome));

		painter.Undo();
		painter.Undo();
		painter.Undo();
		Assert.Empty(land.unitsOnTile);
		Assert.Empty(rome.units);
		Assert.DoesNotContain(unit, gameData.mapUnits);
		painter.Redo();
		Assert.Same(unit, land.unitsOnTile.Single());
		Assert.Contains(unit, gameData.mapUnits);
	}

	// Players, cities and units placed in the editor are saved, opened
	// again, and played: Load Scenario's way (a player picked, the others
	// the computer's), 3 turns.
	[Fact]
	public async Task APopulatedScenarioIsSavedAndPlays() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, Player greece) = TwoPlayers();
		Act(painter, "Paint", () => { painter.PaintTerrain(gameData.map.tileAt(70, 50), 7, painter.TerrainBrushes().First(b => b.Name == "Plains")); return 0; });
		Act(painter, "Place City", () => painter.PlaceCity(gameData.map.tileAt(50, 50), rome));
		Act(painter, "Place City", () => painter.PlaceCity(gameData.map.tileAt(70, 50), greece));
		UnitPrototype spearman = gameData.unitPrototypes.First(p => p.name == "Spearman");
		UnitPrototype settler = gameData.unitPrototypes.First(p => p.name == "Settler");
		Act(painter, "Place", () => painter.PlaceUnit(gameData.map.tileAt(50, 50), spearman, rome));
		Act(painter, "Place", () => painter.PlaceUnit(gameData.map.tileAt(52, 50), settler, rome));
		Act(painter, "Place", () => painter.PlaceUnit(gameData.map.tileAt(70, 50), spearman, greece));

		using TempDirectory dir = new();
		string path = dir.File("people.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, new ScenarioInfo { customMap = true, customPlayerData = true }), path);
		SaveGame scenario = SaveGame.Load(path, s => s);
		Assert.Equal(3, scenario.Players.Count);
		Assert.Equal(2, scenario.Cities.Count);
		Assert.Equal(3, scenario.Units.Count);

		C7GameData.GameData reopened = ScenarioEditing.Open(ScenarioFiles.ForEditing(SaveGame.Load(path, s => s)), fixture.behaviors);
		Assert.Equal(rome.civilization.cityNames[0], reopened.map.tileAt(50, 50).cityAtTile.name);
		Assert.Equal("Rome", reopened.map.tileAt(52, 50).unitsOnTile.Single().owner.civilization.name);

		foreach (SavePlayer player in scenario.Players) {
			player.human = player.civilization == "Rome";
		}
		scenario.GameDifficulty = scenario.Difficulties.First();
		GameSetup.PlaceScenarioKings(scenario);
		await CreateGame.createGame(scenario, _ => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
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
			for (int turn = 0; turn < 3 && !game.gameOver; ++turn) {
				new MsgEndTurn { turn = game.turn }.send();
				EngineWaits.WaitForStartTurnMessage(_ => { });
			}
		} finally {
			game.observerMode = false;
		}
		Assert.True(game.turn >= 3);
		Assert.Equal(2, game.players.Count(p => !p.isBarbarians && p.cities.Count > 0));
	}
}

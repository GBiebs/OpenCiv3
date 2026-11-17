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
	// City Properties: name, owner, size, culture, buildings as one undo
	// step; a great wonder in one city of the world, the palace in one of
	// a player's.
	[Fact]
	public void CityPropertiesAreEditedAndUndone() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, Player greece) = TwoPlayers();
		City capital = Act(painter, "Place City", () => painter.PlaceCity(gameData.map.tileAt(50, 50), rome));
		City other = Act(painter, "Place City", () => painter.PlaceCity(gameData.map.tileAt(54, 50), rome));
		Building pyramids = gameData.Buildings.First(b => b.name == "The Pyramids");
		Building temple = gameData.Buildings.First(b => b.name == "Temple");
		Building palace = gameData.Buildings.First(b => b.isCenterOfEmpire);
		ScenarioInfo info = new();
		CityFacts before = painter.FactsOf(other, info);

		CityFacts facts = before with { Name = "Sparta", Owner = greece, Size = 6, Culture = 40, Buildings = [temple, pyramids] };
		Assert.True(painter.EditCity(other, facts, info));
		Assert.Equal("Sparta", other.name);
		Assert.Equal(greece, other.owner);
		Assert.Contains(other, greece.cities);
		Assert.DoesNotContain(other, rome.cities);
		Assert.Equal(6, other.residents.Count);
		Assert.Equal(40, other.perPlayerCulture[greece]);
		Assert.Equal([temple, pyramids], other.constructed_buildings.Select(b => b.building));

		Assert.NotNull(painter.CantEdit(capital, painter.FactsOf(capital, info) with { Buildings = [palace, pyramids] }));
		// Back to Rome with a palace: Rome's capital has one.
		Assert.NotNull(painter.CantEdit(other, painter.FactsOf(other, info) with { Owner = rome, Buildings = [palace, temple] }));
		Assert.Null(painter.CantEdit(other, painter.FactsOf(other, info) with { Buildings = [palace] }));
		Assert.NotNull(painter.CantEdit(other, facts with { Size = 0 }));

		painter.Undo();
		Assert.Equal(before.Name, other.name);
		Assert.Equal(rome, other.owner);
		Assert.Single(other.residents);
		Assert.Empty(other.constructed_buildings);
		painter.Redo();
		Assert.Equal(6, other.residents.Count);
		Assert.Equal(greece, other.owner);
	}

	// Unit Properties: type, owner, name, experience, AI strategy, the
	// civ-specific king, as one undo step; saved and opened again.
	[Fact]
	public void UnitPropertiesAreEditedAndKept() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, Player greece) = TwoPlayers();
		MapUnit unit = Act(painter, "Place", () => painter.PlaceUnit(gameData.map.tileAt(50, 50), gameData.unitPrototypes.First(p => p.name == "Warrior"), rome));
		UnitPrototype spearman = gameData.unitPrototypes.First(p => p.name == "Spearman");
		ExperienceLevel elite = gameData.experienceLevels.Last();
		string strategy = MapPainter.StrategiesOf(spearman).First().ToString();
		UnitFacts facts = new(spearman, greece, "Leonidas", elite, strategy, true);
		Assert.True(painter.EditUnit(unit, facts));
		Assert.Equal((spearman, greece, "Leonidas", elite, strategy, true),
			(unit.unitType, unit.owner, unit.name, unit.experienceLevel, unit.aiStrategy, unit.useCivSpecificKing));
		Assert.Equal(unit.maxHitPoints, unit.hitPointsRemaining);
		Assert.Contains(unit, greece.units);
		Assert.NotNull(painter.CantEdit(unit, facts with { Type = gameData.unitPrototypes.First(p => p.name == "Galley") }));
		Assert.NotNull(painter.CantEdit(unit, facts with { AiStrategy = "NoSuchStrategy" }));

		using TempDirectory dir = new();
		string path = dir.File("unit.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, new ScenarioInfo()), path);
		C7GameData.GameData again = ScenarioEditing.Open(ScenarioFiles.ForEditing(SaveGame.Load(path, s => s)), fixture.behaviors);
		MapUnit kept = again.map.tileAt(50, 50).unitsOnTile.Single();
		Assert.Equal(("Spearman", "Greece", "Leonidas", elite.key, strategy, true),
			(kept.unitType.name, kept.owner.civilization.name, kept.name, kept.experienceLevelKey, kept.aiStrategy, kept.useCivSpecificKing));

		painter.Undo();
		Assert.Equal(("Warrior", rome), (unit.unitType.name, unit.owner));
		Assert.Contains(unit, rome.units);
	}

	// Delete takes a city or a unit off, undoably; the city's borders go.
	[Fact]
	public void CitiesAndUnitsAreDeleted() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, _) = TwoPlayers();
		Tile tile = gameData.map.tileAt(50, 50);
		City city = Act(painter, "Place City", () => painter.PlaceCity(tile, rome));
		MapUnit unit = Act(painter, "Place", () => painter.PlaceUnit(tile, gameData.unitPrototypes.First(p => p.name == "Warrior"), rome));
		Assert.Equal([$"City: {city.name}", "Warrior (Rome)"], painter.ErasablesOn(tile).Take(2).Select(e => e.Name));

		painter.Erase(tile, painter.ErasablesOn(tile).Take(2).ToList());
		Assert.Null(tile.cityAtTile);
		Assert.Empty(tile.unitsOnTile);
		Assert.Null(tile.OwningPlayer());
		painter.Undo();
		Assert.Same(city, tile.cityAtTile);
		Assert.Same(unit, tile.unitsOnTile.Single());
		Assert.Equal(rome, tile.OwningPlayer());
	}

	// Civ3's "Auto": the game names the city from its owner's list when
	// the scenario starts.
	[Fact]
	public void AutoNamedCitiesAreNamedAtTheStart() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, _) = TwoPlayers();
		City city = Act(painter, "Place City", () => painter.PlaceCity(gameData.map.tileAt(50, 50), rome));
		ScenarioInfo info = new() { customPlayerData = true };
		Assert.True(painter.EditCity(city, painter.FactsOf(city, info) with { Name = "Placeholder", AutoName = true }, info));
		Assert.Contains(city.id.ToString(), info.autoNamedCities);

		using TempDirectory dir = new();
		string path = dir.File("auto.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, info), path);
		SaveGame scenario = SaveGame.Load(path, s => s);
		Assert.Equal("Placeholder", scenario.Cities.Single().name);
		ScenarioStart.Apply(scenario);
		Assert.Equal(rome.civilization.cityNames[0], scenario.Cities.Single().name);
	}
	// Civ3's Reassign: the tile's city and units go to the active player,
	// as one undo step.
	[Fact]
	public void ReassignGivesTheTileToTheActivePlayer() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, Player greece) = TwoPlayers();
		Tile tile = gameData.map.tileAt(50, 50);
		City city = Act(painter, "Place City", () => painter.PlaceCity(tile, rome));
		MapUnit unit = Act(painter, "Place", () => painter.PlaceUnit(tile, gameData.unitPrototypes.First(p => p.name == "Warrior"), rome));
		ScenarioInfo info = new();
		Assert.NotNull(painter.CantReassign(tile, rome, info));

		Assert.NotNull(painter.Reassign(tile, greece, info));
		Assert.Equal((greece, greece), (city.owner, unit.owner));
		Assert.Equal(greece, tile.OwningPlayer());
		Assert.Contains(city, greece.cities);
		painter.Undo();
		Assert.Equal((rome, rome), (city.owner, unit.owner));
		Assert.Equal(rome, tile.OwningPlayer());
	}
	// A player's own starting location: one each (placing another moves
	// it), keeping huts off it, undone and deleted as the rest; at the start
	// the player gets the rules' start units there.
	[Fact]
	public void PlayerStartsGiveStartUnits() {
		(C7GameData.GameData gameData, MapPainter painter, Player rome, Player greece) = TwoPlayers();
		Tile a = gameData.map.tileAt(48, 50), b = gameData.map.tileAt(52, 50);
		Assert.True(Act(painter, "Place Start", () => painter.PlacePlayerStart(a, rome)));
		Assert.True(Act(painter, "Place Start", () => painter.PlacePlayerStart(b, rome)));
		Assert.Null(painter.PlayerStartAt(a));
		Assert.Equal(rome, painter.PlayerStartAt(b));
		Assert.NotNull(painter.CantPlaceStart(b, greece));
		Assert.NotNull(painter.CantPlaceOverlay(b, painter.OverlayBrushes().First(o => o.Name == "Goody Hut")));
		painter.Undo();
		Assert.Equal(rome, painter.PlayerStartAt(a));
		painter.Redo();
		Assert.True(Act(painter, "Place Start", () => painter.PlacePlayerStart(a, greece)));
		Assert.Equal(2, painter.Info.playerStarts.Count);

		using TempDirectory dir = new();
		string path = dir.File("starts.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, painter.Info), path);
		SaveGame scenario = SaveGame.Load(path, s => s);
		Assert.Empty(scenario.Units);
		ScenarioStart.Apply(scenario);
		string romeId = rome.id.ToString();
		Assert.Equal([gameData.rules.StartUnitType1, gameData.rules.StartUnitType2],
			scenario.Units.Where(u => u.owner.ToString() == romeId).Select(u => u.prototype));
		Assert.All(scenario.Units.Where(u => u.owner.ToString() == romeId), u => Assert.Equal((52, 50), (u.currentLocation.X, u.currentLocation.Y)));
		Assert.Equal(4, scenario.Units.Count);

		painter.Erase(b, painter.ErasablesOn(b).Where(e => e.StartOf != null).ToList());
		Assert.Null(painter.PlayerStartAt(b));
	}

	// Civ3's Fog of War tool: tiles painted clear (any brush size), undone
	// a stroke at a time; every player knows them when the game starts.
	[Fact]
	public void FogPaintedClearIsKnownAtTheStart() {
		(C7GameData.GameData gameData, MapPainter painter, _, _) = TwoPlayers();
		Tile tile = gameData.map.tileAt(50, 50);
		painter.BeginStroke("Remove Fog");
		painter.PaintFog(tile, 3, reveal: true);
		painter.PaintFog(gameData.map.tileAt(56, 50), 1, reveal: true);
		painter.EndStroke();
		Assert.Equal(10, painter.Info.revealedTiles.Count);
		Assert.True(painter.IsRevealed(tile));
		painter.BeginStroke("Place Fog");
		painter.PaintFog(tile, 1, reveal: false);
		painter.EndStroke();
		Assert.False(painter.IsRevealed(tile));
		painter.Undo();
		Assert.True(painter.IsRevealed(tile));
		painter.Undo();
		Assert.Empty(painter.Info.revealedTiles);
		painter.Redo();
		Assert.Equal(10, painter.Info.revealedTiles.Count);

		using TempDirectory dir = new();
		string path = dir.File("fog.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, painter.Info), path);
		SaveGame scenario = SaveGame.Load(path, s => s);
		ScenarioStart.Apply(scenario);
		Assert.All(scenario.Players.Where(p => !p.isBarbarian), p => Assert.Contains(p.tileKnowledge, t => t.X == 56 && t.Y == 50));
		Assert.All(scenario.Players.Where(p => !p.isBarbarian), p => Assert.Equal(10, p.tileKnowledge.Count));
	}
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Lua;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.Editor;

// The scenario editor's files: a new scenario, a rules mod, a scenario
// saved and opened again, and Civ3's own scenarios as starting points.
public class ScenarioFilesTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public ScenarioFilesTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private static SaveGame Rules() => SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave();

	// The scenario as the editor has it (see ScenarioEditing.Open).
	private C7GameData.GameData Edit(SaveGame save) {
		C7GameData.GameData gameData = ScenarioEditing.Open(save, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		return gameData;
	}

	private static SaveGame SaveAndOpen(SaveGame scenario, TempDirectory dir) {
		string path = dir.File("scenario.json");
		ScenarioFiles.Save(scenario, path);
		return ScenarioFiles.ForEditing(SaveGame.Load(path, s => s));
	}

	// As Civ3's editor starts: the rules on 100 by 100 tiles of ocean, every
	// other row shifted by one, and nobody on it.
	[Fact]
	public void ANewScenarioIsTheRulesOnABlankMap() {
		SaveGame scenario = ScenarioFiles.NewScenario(Rules());
		Assert.Equal(100 / 2 * 100, scenario.Map.tiles.Count);
		Assert.All(scenario.Map.tiles, t => Assert.Equal("ocean", t.baseTerrain));
		Assert.All(scenario.Map.tiles, t => Assert.Equal(0, (t.X + t.Y) % 2));
		Assert.Empty(scenario.Players);
		Assert.True(scenario.Scenario.customMap);

		C7GameData.GameData gameData = Edit(scenario);
		Assert.Equal(5000, gameData.map.tiles.Count);
		Assert.Equal("ocean", gameData.map.tileAt(99, 99).baseTerrainType.Key);
		Assert.True(gameData.map.wrapHorizontally);
		Assert.True(gameData.observerMode);
		// Each tile has its art chosen, as a generated map's tiles do, so the
		// map view can draw it: open ocean is Civ3's ocean file.
		Assert.All(gameData.map.tiles, t => Assert.NotNull(t.ExtraInfo));
		Assert.Equal(8, gameData.map.tileAt(50, 50).ExtraInfo.BaseTerrainFileID);
	}

	[Fact]
	public void AMapMustBeAnEvenNumberOfTilesWide() {
		Assert.Throws<System.ArgumentException>(() => ScenarioFiles.NewScenario(Rules(), 99, 100));
		Assert.Throws<System.ArgumentException>(() => ScenarioFiles.NewScenario(Rules(), 100, 100, "lava"));
	}

	// A ruleset, or a Civ3 scenario without a map, is a rules mod: the editor
	// shows a blank map, and saves the rules alone, for the game to make the
	// map when it starts.
	[Fact]
	public void ARulesModGetsAMapToShowButSavesNone() {
		SaveGame mod = ScenarioFiles.ForEditing(Rules(), "My Mod");
		Assert.False(mod.Scenario.customMap);
		Assert.Equal("My Mod", mod.Scenario.title);
		Assert.Equal(5000, mod.Map.tiles.Count);

		C7GameData.GameData gameData = Edit(mod);
		SaveGame saved = ScenarioFiles.ToScenario(gameData, mod.Scenario);
		Assert.Empty(saved.Map.tiles);
		Assert.Empty(saved.Players);
		Assert.Empty(saved.Units);
		Assert.Equal(Rules().UnitPrototypes.Count, saved.UnitPrototypes.Count);
	}

	// What's on the map, and what the scenario says about itself, come back
	// as they were saved.
	[Fact]
	public void AScenarioComesBackAsItWasSaved() {
		SaveGame game = ScenarioFiles.ForEditing(fixture.saveGame);
		Assert.True(game.Scenario.customMap);
		game.Scenario.title = "Test Scenario";
		game.Scenario.description = "Two lines,\nof description.";
		C7GameData.GameData gameData = Edit(game);
		TerrainType grassland = gameData.terrainTypes.Find(t => t.Key == "grassland");
		Tile changed = gameData.map.tiles.First(t => t.baseTerrainType.Key == "ocean");
		changed.baseTerrainType = changed.overlayTerrainType = grassland;

		using TempDirectory dir = new("scenario-editor");
		SaveGame opened = SaveAndOpen(ScenarioFiles.ToScenario(gameData, game.Scenario), dir);
		Assert.Equal("Test Scenario", opened.Scenario.title);
		Assert.Equal("Two lines,\nof description.", opened.Scenario.description);

		C7GameData.GameData reopened = Edit(opened);
		Assert.Equal(gameData.map.tiles.Count, reopened.map.tiles.Count);
		for (int i = 0; i < gameData.map.tiles.Count; ++i) {
			Assert.Equal(gameData.map.tiles[i].baseTerrainType.Key, reopened.map.tiles[i].baseTerrainType.Key);
			Assert.Equal(gameData.map.tiles[i].overlayTerrainType.Key, reopened.map.tiles[i].overlayTerrainType.Key);
		}
		Assert.Equal("grassland", reopened.map.tileAt(changed.XCoordinate, changed.YCoordinate).baseTerrainType.Key);
		Assert.Equal(gameData.cities.Select(c => (c.name, c.location.XCoordinate, c.location.YCoordinate, c.residents.Count)),
			reopened.cities.Select(c => (c.name, c.location.XCoordinate, c.location.YCoordinate, c.residents.Count)));
		Assert.Equal(gameData.mapUnits.Select(u => (u.unitType.name, u.location.XCoordinate, u.location.YCoordinate)).OrderBy(x => x),
			reopened.mapUnits.Select(u => (u.unitType.name, u.location.XCoordinate, u.location.YCoordinate)).OrderBy(x => x));
		Assert.Equal(gameData.players.Select(p => p.civilization?.name), reopened.players.Select(p => p.civilization?.name));
	}

	// A scenario starts a game: whatever the game it was made from had
	// played (turns, history, the computer players' plans, its random
	// numbers) stays behind.
	[Fact]
	public void AScenarioCarriesNothingFromAGamePlayedSoFar() {
		SaveGame game = fixture.saveGame;
		game.TurnNumber = 12;
		game.GameOver = true;
		game.RngState = 12345;
		game.Replay.Add(new ReplayEvent());
		game.History["somebody"] = new List<HistTurnRecord> { new() };
		game.PlayLog = new PlayLogInfo { name = "played" };
		game.Players[0].ai = new SaveAiState();

		SaveGame scenario = ScenarioFiles.ForEditing(game);
		Assert.Equal(0, scenario.TurnNumber);
		Assert.False(scenario.GameOver);
		Assert.Null(scenario.RngState);
		Assert.Empty(scenario.Replay);
		Assert.Empty(scenario.History);
		Assert.Null(scenario.PlayLog);
		Assert.All(scenario.Players, p => Assert.Null(p.ai));

		C7GameData.GameData gameData = Edit(scenario);
		gameData.turn = 7;
		gameData.gameOver = true;
		SaveGame saved = ScenarioFiles.ToScenario(gameData, scenario.Scenario);
		Assert.Equal(0, saved.TurnNumber);
		Assert.False(saved.GameOver);
		Assert.Null(saved.RngState);
		Assert.Empty(saved.History);
	}

	// A scenario with a map starts a game as it is, as Load Scenario does.
	[Fact]
	public async Task AGameStartsFromAScenario() {
		C7GameData.GameData edited = Edit(ScenarioFiles.ForEditing(fixture.saveGame));
		SaveGame scenario = ScenarioFiles.ToScenario(edited, new ScenarioInfo { title = "Start" });
		using TempDirectory dir = new("scenario-editor");
		string path = dir.File("start.json");
		ScenarioFiles.Save(scenario, path);

		Player human = await CreateGame.createGame(SaveGame.Load(path, s => s), _ => fixture.behaviors);
		Assert.True(human.isHuman);
		Assert.Equal(edited.cities.Count, EngineStorage.gameData.cities.Count);
		Assert.Equal(0, EngineStorage.gameData.turn);
	}

	// Civ3's own scenarios open as starting points: a scenario with a map
	// and its players, and a rules mod. What the import gave comes back
	// after saving and opening the scenario again.
	[SkippableTheory]
	[InlineData("2 MP Rise of Rome.biq", true)]
	[InlineData("No Civ Traits.biq", false)]
	public void ACiv3ScenarioIsAStartingPoint(string name, bool hasMap) {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		string scenarios = Path.Join(conquests, "Scenarios");
		SaveGame imported = ScenarioFiles.Open(Path.Join(scenarios, name), PathUtils.defaultBicPath, modPath => {
			if (string.IsNullOrEmpty(modPath)) {
				return Path.GetFullPath(Path.Combine(conquests, "Text", "PediaIcons.txt"));
			}
			if (!System.OperatingSystem.IsWindows()) {
				modPath = modPath.Replace("\\conquests\\", "/Conquests/");
			}
			return Path.GetFullPath(Path.Combine(scenarios, modPath, "Text", "PediaIcons.txt"));
		});
		Assert.Equal(hasMap, imported.Scenario.customMap);
		Assert.Equal(Path.GetFileNameWithoutExtension(name), imported.Scenario.title);

		C7GameData.GameData gameData = Edit(imported);
		using TempDirectory dir = new("scenario-editor");
		SaveGame opened = SaveAndOpen(ScenarioFiles.ToScenario(gameData, imported.Scenario), dir);
		Assert.Equal(imported.UnitPrototypes.Count, opened.UnitPrototypes.Count);
		Assert.Equal(imported.Buildings.Count, opened.Buildings.Count);
		Assert.Equal(imported.Techs.Count, opened.Techs.Count);
		Assert.Equal(imported.Civilizations.Count, opened.Civilizations.Count);
		Assert.Equal(imported.Map.tiles.Count, opened.Map.tiles.Count);
		if (hasMap) {
			Assert.Equal(imported.Cities.Count, opened.Cities.Count);
			Assert.Equal(imported.Units.Count, opened.Units.Count);
			Assert.Equal(imported.Players.Count, opened.Players.Count);
			Assert.Equal(imported.Map.tiles.Select(t => t.baseTerrain), opened.Map.tiles.Select(t => t.baseTerrain));
		}
	}
}

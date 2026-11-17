using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.Editor;

// Phase 3's measure: Civ3's "9 MP WWII in the Pacific" opens with all its
// cities and units, and survives an edit, a save and a game. Skipped without
// Civ3.
public class Civ3ScenarioEditTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public Civ3ScenarioEditTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	[SkippableFact]
	public async Task WWIIInThePacificOpensIsEditedSavedAndPlayed() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		string path = Path.Join(conquests, "Conquests", "9 WWII in the Pacific.biq");
		Skip.IfNot(File.Exists(path), "WWII in the Pacific isn't installed");
		BiqData biq = BiqData.LoadFile(path);

		// The scenario's own PediaIcons, in its art folders (as the game finds
		// them), else Civ3's.
		string PediaIcons(string modPath) {
			foreach (string folder in (modPath ?? "").Split(';', System.StringSplitOptions.RemoveEmptyEntries)) {
				foreach (string root in new[] { conquests, Path.Join(conquests, "Scenarios"), Path.Join(conquests, "Conquests") }) {
					string own = Path.GetFullPath(Path.Combine(root, folder, "Text", "PediaIcons.txt"));
					if (File.Exists(own)) {
						return own;
					}
				}
			}
			return Path.GetFullPath(Path.Combine(conquests, "Text", "PediaIcons.txt"));
		}
		SaveGame imported = ScenarioFiles.Open(path, PathUtils.defaultBicPath, PediaIcons);
		C7GameData.GameData gameData = ScenarioEditing.Open(imported, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);

		// Every city and unit of the scenario's (and the start units its
		// player starts get on import).
		Assert.Equal(biq.City.Length, gameData.cities.Count);
		Assert.True(gameData.mapUnits.Count >= biq.Unit.Length, $"{gameData.mapUnits.Count} units of {biq.Unit.Length}");
		Assert.Equal(imported.Units.Count, gameData.mapUnits.Count);
		Assert.All(gameData.cities, c => Assert.Same(c, c.location.cityAtTile));
		Assert.All(gameData.mapUnits, u => Assert.Contains(u, u.location.unitsOnTile));

		// An edit: a city renamed and grown, a unit placed beside it, a hill
		// painted where it's allowed.
		MapPainter painter = new(gameData);
		City city = gameData.cities.First(c => !c.owner.isBarbarians);
		ScenarioInfo info = imported.Scenario ?? new ScenarioInfo();
		Assert.True(painter.EditCity(city, painter.FactsOf(city, info) with { Name = "Edited City", Size = city.residents.Count + 2 }, info));
		UnitPrototype infantry = gameData.unitPrototypes.First(p => p.IsLandUnit() && p.attack > 0);
		painter.BeginStroke("Place");
		MapUnit placed = painter.PlaceUnit(city.location, infantry, city.owner);
		painter.EndStroke();
		Assert.NotNull(placed);
		int cities = gameData.cities.Count, units = gameData.mapUnits.Count;

		using TempDirectory dir = new();
		string saved = dir.File("pacific.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, info), saved);
		C7GameData.GameData reopened = ScenarioEditing.Open(ScenarioFiles.ForEditing(SaveGame.Load(saved, s => s)), fixture.behaviors);
		Assert.Equal(cities, reopened.cities.Count);
		Assert.Equal(units, reopened.mapUnits.Count);
		Assert.Contains(reopened.cities, c => c.name == "Edited City" && c.residents.Count == city.residents.Count);

		// The game: Load Scenario's way, the first civ that can be picked
		// the human's, then the computer plays 2 turns.
		SaveGame scenario = SaveGame.Load(saved, s => s);
		SavePlayer human = scenario.Players.First(p => p.canBePicked && !p.isBarbarian);
		foreach (SavePlayer player in scenario.Players) {
			player.human = player == human;
		}
		scenario.GameDifficulty = scenario.Difficulties.First();
		GameSetup.PlaceScenarioKings(scenario);
		ScenarioStart.Apply(scenario);
		await CreateGame.createGame(scenario, _ => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		Assert.Equal(cities, game.cities.Count);
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
			for (int turn = 0; turn < 2 && !game.gameOver; ++turn) {
				new MsgEndTurn { turn = game.turn }.send();
				EngineWaits.WaitForStartTurnMessage(_ => { });
			}
		} finally {
			game.observerMode = false;
		}
		Assert.True(game.turn >= 2);
		Assert.Contains(game.cities, c => c.name == "Edited City");
	}
}

using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Editor;

// Phase 4: Civ3's Scenario Properties, Scenario tab.
public class ScenarioSettingsTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public ScenarioSettingsTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private (C7GameData.GameData, MapPainter) Scenario() {
		SaveGame scenario = ScenarioFiles.NewScenario(SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave());
		ScenarioInfo info = new();
		ScenarioPlayers.Add(scenario, scenario.Civilizations.First(c => c.name == "Rome"), info);
		C7GameData.GameData gameData = ScenarioEditing.Open(scenario, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		return (gameData, new MapPainter(gameData) { Info = info });
	}

	private static ScenarioSettings Changed(ScenarioSettings s) => s with {
		Title = "The Punic Wars",
		Description = "Rome against Carthage.",
		SearchFolders = "Punic;Lush Cities",
		Barbarians = BarbarianActivity.Raging,
		DebugMode = true,
		NumberOfPlayers = 2,
		PlayableCivs = ["Rome", "Carthage"],
		Domination = false,
		SpaceRace = false,
		Wonder = true,
		Regicide = true,
		AcceleratedProduction = true,
		CulturalConversion = false,
		RevealMap = true,
		RetainCulture = true,
		UseTurnLimit = true,
		TurnLimit = 150,
		TimeLimitMinutes = 90,
		BaseUnit = TimeUnit.Months,
		StartYear = -264,
		StartMonth = 3,
		ScaleTurns = [10, 20, 30, 40, 50, 60, 70],
		ScaleUnits = [12, 6, 3, 2, 1, 1, 1],
		Campaign = 4,
		WonderPoints = 20,
		DefeatPoints = 3,
		VictoryPointLimit = 5000,
		CultureOneCity = 5000,
		DominationLand = 50,
		DominationPopulation = 40,
		AnyoneCapturesAnyFlag = true,
	};

	// The settings go where the game reads them (victory conditions, rules,
	// calendar, barbarians, search folders, debug mode) or onto the
	// scenario, as one undo step.
	[Fact]
	public void TheScenarioTabIsOneUndoStep() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		ScenarioSettings before = ScenarioSettings.Of(gameData, painter.Info);
		ScenarioSettings after = Changed(before);
		Assert.Null(painter.EditScenario(after));

		Assert.Equal("The Punic Wars", painter.Info.title);
		Assert.Equal(BarbarianActivity.Raging, gameData.barbarianInfo.barbarianActivity);
		Assert.Equal("Punic;Lush Cities", gameData.scenarioSearchPath);
		Assert.True(gameData.debugMode);
		Assert.False(gameData.victoryConditions.AllowDominationVictory);
		Assert.True(gameData.victoryConditions.Regicide);
		Assert.True(gameData.rules.AcceleratedProduction);
		Assert.False(gameData.rules.AllowCultureFlips);
		Assert.Equal((TimeUnit.Months, -264, 150), (gameData.timeOptions.baseUnit, gameData.timeOptions.startYear, gameData.timeOptions.turnLimit));
		Assert.Equal(12, gameData.timeOptions.timeScale[1, 0]);
		Assert.Equal((20, 5000, 50, 5000), (gameData.victoryConditions.WonderVictoryPoints, gameData.victoryConditions.VictoryPointLimit,
			gameData.victoryConditions.DominationTerritoryPercent, gameData.victoryConditions.CultureOneCityWin));
		Assert.Equal(["Rome", "Carthage"], painter.Info.playableCivs);

		painter.Undo();
		ScenarioSettings undone = ScenarioSettings.Of(gameData, painter.Info);
		Assert.Equal(before.Title, undone.Title);
		Assert.Equal(before.Barbarians, undone.Barbarians);
		Assert.Equal(before.StartYear, undone.StartYear);
		Assert.True(undone.Domination);
		Assert.Empty(undone.PlayableCivs);
		Assert.NotNull(painter.EditScenario(after with { NumberOfPlayers = 17 }));
		Assert.NotNull(painter.EditScenario(after with { DominationLand = 0 }));
		Assert.NotNull(painter.EditScenario(after with { VictoryPointLimit = -1 }));
	}

	// Saved and opened again, every setting is as it was.
	[Fact]
	public void TheScenarioTabIsSaved() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		ScenarioSettings after = Changed(ScenarioSettings.Of(gameData, painter.Info));
		painter.EditScenario(after);

		using TempDirectory dir = new();
		string path = dir.File("settings.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, painter.Info), path);
		SaveGame saved = SaveGame.Load(path, s => s);
		C7GameData.GameData again = ScenarioEditing.Open(ScenarioFiles.ForEditing(saved), fixture.behaviors);
		ScenarioSettings kept = ScenarioSettings.Of(again, saved.Scenario);
		Assert.Equal(after with {
			PlayableCivs = kept.PlayableCivs,
			World = kept.World,
			ScaleTurns = kept.ScaleTurns,
			ScaleUnits = kept.ScaleUnits,
			AllianceNames = kept.AllianceNames,
			AllianceOf = kept.AllianceOf,
			AllianceWars = kept.AllianceWars,
			Players = kept.Players,
		}, kept);
		Assert.Equal(after.PlayableCivs, kept.PlayableCivs);
		Assert.Equal(after.ScaleTurns, kept.ScaleTurns);
		Assert.Equal(after.ScaleUnits, kept.ScaleUnits);
	}

	// Reveal Entire Map: every player knows every tile when the game starts.
	[Fact]
	public void RevealMapIsHonouredAtTheStart() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		painter.EditScenario(ScenarioSettings.Of(gameData, painter.Info) with { RevealMap = true });
		using TempDirectory dir = new();
		string path = dir.File("reveal.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, painter.Info), path);
		SaveGame scenario = SaveGame.Load(path, s => s);
		ScenarioStart.Apply(scenario);
		Assert.All(scenario.Players.Where(p => !p.isBarbarian), p => Assert.Equal(scenario.Map.tiles.Count, p.tileKnowledge.Count));
	}
	// What a scenario allows at setup: the victory conditions and rules it
	// has on, which a player may turn off; and its playable civilizations.
	[Fact]
	public void SetupOffersWhatTheScenarioAllows() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		painter.EditScenario(Changed(ScenarioSettings.Of(gameData, painter.Info)));
		SaveGame scenario = ScenarioFiles.ToScenario(gameData, painter.Info);

		System.Collections.Generic.List<ScenarioRules.Toggle> allowed = ScenarioRules.Allowed(scenario);
		Assert.Contains(allowed, t => t.Name == "Regicide");
		Assert.Contains(allowed, t => t.Name == "Wonder victory");
		Assert.DoesNotContain(allowed, t => t.Name == "Domination victory");
		Assert.DoesNotContain(allowed, t => t.Name == "Culture flipping");
		ScenarioRules.TurnOff(scenario, allowed.Where(t => t.Name == "Regicide"));
		Assert.False(scenario.VictoryConditions.Regicide);
		Assert.True(scenario.VictoryConditions.AllowWonderVictory);

		Assert.True(ScenarioRules.IsPlayable(scenario, "Carthage"));
		Assert.False(ScenarioRules.IsPlayable(scenario, "Greece"));
		Assert.True(ScenarioRules.IsPlayable(new SaveGame(), "Greece"));
		Assert.Empty(ScenarioRules.Allowed(new SaveGame()));
	}
}

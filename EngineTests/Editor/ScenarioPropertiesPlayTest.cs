using System;
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

// Phase 4's measure: every Scenario Properties setting, from the editor
// through the scenario file and Load Scenario's steps (ScenarioSetup) into a
// started game, where the game reads it. (Load Scenario's screen itself is
// checked in the program: the skill's ZzScenarioHarness and
// ZzPlayersHarness.) The settings the game doesn't use yet are only kept
// with the scenario (ScenarioSettingsTest).
public class ScenarioPropertiesPlayTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public ScenarioPropertiesPlayTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	[Fact]
	public void EverySettingReachesTheStartedGame() {
		SaveGame blank = ScenarioFiles.NewScenario(SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave());
		ScenarioInfo info = new();
		foreach (string civ in new[] { "Rome", "Carthage", "Greece", "Egypt" }) {
			ScenarioPlayers.Add(blank, blank.Civilizations.First(c => c.name == civ), info);
		}
		C7GameData.GameData editing = ScenarioEditing.Open(blank, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(editing);
		MapPainter painter = new(editing) { Info = info };
		Tile start = editing.map.tiles[0];
		ScenarioSettings s = ScenarioSettings.Of(editing, info).Copy();
		string Id(string civ) => s.Players.Find(p => p.Civilization == civ).Id;
		string rome = Id("Rome"), carthage = Id("Carthage"), greece = Id("Greece"), egypt = Id("Egypt");
		info.playerStarts.Add(new PlayerStart { player = rome, x = start.XCoordinate, y = start.YCoordinate });

		// Scenario tab.
		s = s with {
			Title = "The Punic Wars",
			Description = "Rome against Carthage.",
			SearchFolders = "Punic",
			Barbarians = BarbarianActivity.Raging,
			DebugMode = true,
			NumberOfPlayers = 4,
			PlayableCivs = ["Rome", "Egypt", "Babylon"],
			Domination = false,
			SpaceRace = false,
			Diplomatic = true,
			Conquest = true,
			Cultural = true,
			Wonder = true,
			AcceleratedProduction = true,
			CityElimination = false,
			Regicide = true,
			MassRegicide = false,
			VictoryPointScoring = true,
			CaptureTheFlag = false,
			ReverseCaptureTheFlag = false,
			CulturalConversion = false,
			ScientificLeaders = true,
			AutoPlaceKings = true,
			AutoPlaceVictoryLocations = true,
			RevealMap = true,
			UseTurnLimit = true,
			TurnLimit = 150,
			BaseUnit = TimeUnit.Months,
			StartYear = -264,
			StartMonth = 3,
			ScaleTurns = [10, 20, 30, 40, 50, 60, 70],
			ScaleUnits = [12, 6, 3, 2, 1, 1, 1],
			// Victory Point Limits.
			WonderPoints = 20,
			DefeatPoints = 3,
			AdvancePoints = 7,
			CityConquestPoints = 90,
			LocationPoints = 30,
			VictoryPointLimit = 5000,
			CultureOneCity = 6000,
			CultureAllCities = 70000,
			DominationLand = 50,
			DominationPopulation = 40,
			// Locked Alliances.
			AllianceNames = ["Republic", "Punic League", "Hellenes", "Neutrals"],
			AllianceOf = new() { [rome] = 1, [egypt] = 1, [carthage] = 2, [greece] = 3 },
			AllianceWars = [(1, 2), (1, 3)],
			CoalitionVictory = true,
			// Disasters.
			PermitPlagues = true,
			PlagueName = "Antonine Plague",
			PlagueEarliestStart = -200,
			PlagueVariance = 25,
			PlagueDuration = 10,
			PlagueStrength = 60,
			PlagueGracePeriod = 50,
			PlagueMaxOccurrences = 3,
			VolcanoEruptionPeriod = 3000,
		};
		// Players.
		PlayerSettings romans = s.Players.Find(p => p.Id == rome);
		romans.Treasury = 500;
		romans.Government = "Monarchy";
		romans.Era = 1;
		romans.FreeTechs = [.. romans.FreeTechs.Union(["Bronze Working"])];
		romans.Color = 7;
		romans.SkipFirstTurn = true;
		romans.LeaderName = "Scipio";
		romans.LeaderGender = Gender.Female;
		romans.Difficulty = "Monarch";
		romans.StartUnits = [new StartUnit { type = "Warrior", count = 3 }];
		romans.StartWithEmbassies = true;
		s.Players.Find(p => p.Id == carthage).Choice = CivChoice.Random;
		s.Players.Find(p => p.Id == carthage).Human = false;
		s.Players.Find(p => p.Id == greece).Human = false;
		PlayerSettings any = s.Players.Find(p => p.Id == egypt);
		any.Choice = CivChoice.Any;
		any.StartWithEmbassies = true;
		any.CivDefaults = true;
		Assert.Null(painter.EditScenario(s));

		using TempDirectory dir = new();
		string path = dir.File("punic.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(editing, info), path);

		// Load Scenario: the civilizations offered, the human's pick (the
		// Any player as Babylon), Regicide unticked, then the start.
		SaveGame save = SaveGame.Load(path, x => x);
		List<ScenarioStart.HumanChoice> choices = ScenarioStart.HumanChoices(save, new Random(3));
		Assert.Equal(["Babylon", "Egypt", "Rome"], choices.Select(c => c.Civilization).Order());
		Assert.Equal("Monarch", ScenarioStart.DifficultyFor(save, rome));
		ScenarioStart.AssignCivilizations(save, choices.First(c => c.Civilization == "Babylon"), new Random(3));
		save.GameDifficulty = save.Difficulties.First(d => d.Name == "Emperor");
		ScenarioRules.TurnOff(save, ScenarioRules.Allowed(save).Where(t => t.Name == "Regicide"));
		GameSetup.PlaceScenarioKings(save);
		ScenarioStart.Apply(save);
		Player human = CreateGame.createGame(save, _ => fixture.behaviors).GetAwaiter().GetResult();
		C7GameData.GameData game = EngineStorage.gameData;
		Player P(string id) => game.players.Find(p => p.id.ToString() == id);

		// Scenario tab.
		Assert.Equal("Punic", game.scenarioSearchPath);
		Assert.Equal(BarbarianActivity.Raging, game.barbarianInfo.barbarianActivity);
		Assert.True(game.debugMode);
		VictoryConditions vc = game.victoryConditions;
		Assert.Equal((false, false, true, true, true, true), (vc.AllowDominationVictory, vc.AllowSpaceRaceVictory, vc.AllowDiplomaticVictory,
			vc.AllowConquestVictory, vc.AllowCulturalVictory, vc.AllowWonderVictory));
		Assert.False(vc.Regicide); // turned off at setup
		Assert.Equal((false, true, false, false), (vc.CityElimination, vc.VictoryLocations, vc.CaptureTheFlag, vc.MassRegicide));
		Assert.Equal((true, false, true), (game.rules.AcceleratedProduction, game.rules.AllowCultureFlips, game.rules.ScientificLeaders));
		Assert.True(vc.AutoPlaceKings && vc.AutoPlaceVictoryLocations && vc.UseTurnLimit);
		Assert.All(game.players.Where(p => !p.IsIndependent), p => Assert.Equal(game.map.tiles.Count, p.tileKnowledge.knownTiles.Count));
		Assert.Equal((TimeUnit.Months, -264, 3, 150), (game.timeOptions.baseUnit, game.timeOptions.startYear, game.timeOptions.startMonth, game.timeOptions.turnLimit));
		Assert.Equal((10, 12), (game.timeOptions.timeScale[0, 0], game.timeOptions.timeScale[1, 0]));
		// Victory Point Limits.
		Assert.Equal((20, 3, 7, 90, 30), (vc.WonderVictoryPoints, vc.DefeatVictoryPoints, vc.AdvanceVictoryPoints, vc.CityConquestVictoryPoints, vc.VictoryLocationPoints));
		Assert.Equal((5000, 6000, 70000, 50, 40), (vc.VictoryPointLimit, vc.CultureOneCityWin, vc.CultureAllCitiesWin, vc.DominationTerritoryPercent, vc.DominationPopulationPercent));
		// Locked Alliances.
		Assert.True(game.AreInLockedWar(P(rome), P(carthage)));
		Assert.True(game.AreInLockedWar(P(egypt), P(greece)));
		Assert.False(game.AreInLockedWar(P(carthage), P(greece)));
		Assert.True(game.AreInLockedPeace(P(rome), P(egypt)));
		Assert.True(game.AreTeammates(P(rome), P(egypt)));
		Assert.False(P(rome).IsAtPeaceWith(P(carthage)));
		Assert.True(P(rome).IsAtPeaceWith(P(egypt)));
		// Players.
		Assert.Equal((egypt, "Babylon"), (human.id.ToString(), human.civilization.name));
		Assert.Equal("Emperor", game.gameDifficulty.Name);
		Assert.DoesNotContain(P(carthage).civilization.name, new[] { "Rome", "Babylon", "Greece" });
		Player r = P(rome);
		Assert.Equal(("Scipio", Gender.Female), (r.civilization.leader, r.civilization.leaderGender));
		Assert.Equal((500, "Monarchy", "ERAS_Middle_Ages", 7, true), (r.gold, r.government.name, r.eraCivilopediaName, r.primaryColorIndex, r.skipFirstTurn));
		Assert.Contains(game.techs.Find(t => t.Name == "Bronze Working").id, r.knownTechs);
		Assert.Equal(3, game.mapUnits.Count(u => u.owner == r && u.unitType.name == "Warrior"));
		Assert.True(r.playerRelationships[P(egypt).id].hasEmbassy);
		Assert.True(P(egypt).playerRelationships[r.id].hasEmbassy);
		Civilization babylon = game.civilizations.Find(c => c.name == "Babylon");
		Assert.Equal(babylon.primaryColorIndex, human.primaryColorIndex); // Civilization Defaults
																		  // Disasters.
		Assert.Equal((true, "Antonine Plague", -200, 25, 10, 60, 50, 3, 3000), (game.rules.PermitPlagues, game.rules.PlagueName,
			game.rules.PlagueEarliestStart, game.rules.PlagueVariance, game.rules.PlagueDuration, game.rules.PlagueStrength,
			game.rules.PlagueGracePeriod, game.rules.PlagueMaxOccurrences, game.rules.VolcanoEruptionPeriod));
	}
}

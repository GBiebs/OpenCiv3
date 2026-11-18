using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using QueryCiv3;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Editor;

// Phase 6's measure: the QC mod in the Civ3 Scenarios folder opens, its
// rules are edited on the big pages (a unit type renamed and strengthened,
// one added, an improvement, an advance, a civilization, a government, a
// terrain and a resource changed), it saves, and a game on its rules plays.
public class Civ3ModRulesEditTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public Civ3ModRulesEditTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private static void Set(SaveGame s, string page, string field, object item, object value) {
		RulePage p = RulePages.Find(page);
		Assert.Null(RuleEditing.SetValue(s, p.Fields.First(f => f.Label == field), item, value));
	}

	[SkippableFact]
	public async Task QCOpensIsEditedOnTheBigPagesSavedAndPlayed() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string conquests = Path.Join(Civ3Location.GetCiv3Path(), "Conquests");
		string path = Directory.GetFiles(Path.Join(conquests, "Scenarios"), "QC_1.*.biq").OrderBy(f => f).LastOrDefault();
		Skip.If(path == null, "No QC mod in the Scenarios folder");

		SaveGame opened = ScenarioFiles.Open(path, PathUtils.defaultBicPath, _ => Path.Join(conquests, "Text", "PediaIcons.txt"));
		C7GameData.GameData editing = ScenarioEditing.Open(opened, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(editing);
		// What the rules dialog edits: the scenario as it would be saved.
		SaveGame s = ScenarioFiles.ToScenario(editing, opened.Scenario);

		SaveUnitPrototype fighter = s.UnitPrototypes.First(u => u.categories.Contains("Land") && u.attack > 0 && !u.unproducible);
		string oldName = fighter.name;
		Assert.Null(RulePages.Find("Units").Rename(s, fighter, "Edited Fighter"));
		Set(s, "Units", "Attack:", fighter, fighter.attack + 3);
		SaveUnitPrototype added = (SaveUnitPrototype)RulePages.Find("Units").Add(s, fighter);
		Assert.Null(RulePages.Find("Units").Rename(s, added, "Added Unit"));
		Set(s, "Units", "Defense:", added, 7);
		SaveBuilding building = s.Buildings.First(b => b.greatWonderProperties == null && !b.isSmallWonder);
		Set(s, "Improvements and Wonders", "Cost (shields):", building, building.shieldCost + 11);
		SaveTech tech = s.Techs[0];
		Set(s, "Civilization Advances", "Cost:", tech, tech.Cost + 5);
		Civilization civ = s.Civilizations.First(c => !c.isBarbarian);
		Set(s, "Civilizations", "Leader:", civ, "Edited Leader");
		Government government = s.Governments.First(g => g.defaultType);
		Set(s, "Governments", "Rate Cap (tenths):", government, 7);
		TerrainType grassland = s.TerrainTypes.First(t => t.baseFoodProduction > 0);
		Set(s, "Terrain", "Food:", grassland, grassland.baseFoodProduction + 1);
		Resource resource = s.Resources.First(r => r.Category == ResourceCategory.BONUS);
		Set(s, "Natural Resources", "Shields:", resource, resource.ShieldsBonus + 1);
		Assert.Null(RuleEditing.Problem(s));

		using TempDirectory dir = new();
		string saved = dir.File("qc.json");
		ScenarioFiles.Save(s, saved);
		SaveGame scenario = SaveGame.Load(saved, x => x);
		Assert.DoesNotContain(scenario.UnitPrototypes, u => u.name == oldName);

		// Load Scenario's way for a rules mod: a new game on its rules.
		WorldSize size = scenario.WorldSizes.OrderBy(w => w.width * w.height).First();
		new GameSetup {
			playerCivilization = scenario.Civilizations.First(c => c.name == civ.name),
			difficulty = scenario.Difficulties.First(),
			worldCharacteristics = new WorldCharacteristics(scenario) { worldSize = size, mapSeed = 1 },
			opponents = Enumerable.Repeat(new SelectedOpponent { isRandom = true }, Math.Max(1, size.numberOfCivs - 1)).ToList(),
			victoryConditions = scenario.VictoryConditions ?? new VictoryConditions(),
		}.Populate(scenario);
		await CreateGame.createGame(scenario, _ => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;

		Assert.Equal(fighter.attack, game.unitPrototypes.First(u => u.name == "Edited Fighter").attack);
		Assert.Equal(7, game.unitPrototypes.First(u => u.name == "Added Unit").defense);
		Assert.Equal(building.shieldCost, game.Buildings.First(b => b.name == building.name).shieldCost);
		Assert.Equal(tech.Cost, game.techs.First(t => t.Name == tech.Name).Cost);
		Assert.Equal("Edited Leader", game.civilizations.First(c => c.name == civ.name).leader);
		Assert.Equal(7, game.governments.First(g => g.name == government.name).rateCap);
		Assert.Equal(grassland.baseFoodProduction, game.terrainTypes.First(t => t.Key == grassland.Key).baseFoodProduction);
		Assert.Equal(resource.ShieldsBonus, game.Resources.First(r => r.Key == resource.Key).ShieldsBonus);

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
		Assert.True(game.turn >= 3, $"turn {game.turn}");
	}
}

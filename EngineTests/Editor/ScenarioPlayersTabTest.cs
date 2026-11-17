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

// Phase 4: Scenario Properties' Locked Alliances, Players and Disasters
// tabs, and what the game makes of them at the start.
public class ScenarioPlayersTabTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public ScenarioPlayersTabTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private static readonly string[] Civs = ["Rome", "Carthage", "Greece", "Egypt"];

	private (C7GameData.GameData, MapPainter) Scenario() {
		SaveGame scenario = ScenarioFiles.NewScenario(SaveGameFixture.LoadGameMode(new GameMode.Config("civ3")).GetSave());
		ScenarioInfo info = new();
		foreach (string civ in Civs) {
			ScenarioPlayers.Add(scenario, scenario.Civilizations.First(c => c.name == civ), info);
		}
		C7GameData.GameData gameData = ScenarioEditing.Open(scenario, fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		return (gameData, new MapPainter(gameData) { Info = info });
	}

	private static string IdOf(ScenarioSettings s, string civ) => s.Players.Find(p => p.Civilization == civ).Id;

	private static Player PlayerOf(C7GameData.GameData gameData, string civ) => gameData.players.Find(p => p.civilization?.name == civ);

	private static SaveGame SavedAndLoaded(C7GameData.GameData gameData, ScenarioInfo info, TempDirectory dir) {
		string path = dir.File("scenario.json");
		ScenarioFiles.Save(ScenarioFiles.ToScenario(gameData, info), path);
		return SaveGame.Load(path, s => s);
	}

	// Rome and Egypt against Carthage, and both against Greece: three wars
	// between three alliances, which the save once couldn't hold (one war
	// per alliance).
	private static ScenarioSettings Alliances(ScenarioSettings s) => s with {
		AllianceNames = ["Republic", "Punic League", "Hellenes", "Neutrals"],
		AllianceOf = new() { [IdOf(s, "Rome")] = 1, [IdOf(s, "Egypt")] = 1, [IdOf(s, "Carthage")] = 2, [IdOf(s, "Greece")] = 3 },
		AllianceWars = [(1, 2), (1, 3), (2, 3)],
		CoalitionVictory = true,
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

	[Fact]
	public void LockedAlliancesAndDisastersAreOneUndoStepTheGamePlays() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		ScenarioSettings before = ScenarioSettings.Of(gameData, painter.Info);
		ScenarioSettings after = Alliances(before);
		Assert.Null(painter.EditScenario(after));

		Player rome = PlayerOf(gameData, "Rome"), egypt = PlayerOf(gameData, "Egypt");
		Player carthage = PlayerOf(gameData, "Carthage"), greece = PlayerOf(gameData, "Greece");
		Assert.True(gameData.AreInLockedPeace(rome, egypt));
		Assert.True(gameData.AreInLockedWar(rome, carthage));
		Assert.True(gameData.AreInLockedWar(egypt, greece));
		Assert.True(gameData.AreInLockedWar(carthage, greece));
		Assert.True(gameData.AreTeammates(rome, egypt));
		Assert.Equal(("Antonine Plague", 60, 3000), (gameData.rules.PlagueName, gameData.rules.PlagueStrength, gameData.rules.VolcanoEruptionPeriod));
		Assert.True(gameData.rules.PermitPlagues);

		painter.Undo();
		Assert.Null(PlayerOf(gameData, "Rome").alliance);
		Assert.False(gameData.AreInLockedWar(rome, carthage));
		Assert.False(gameData.rules.TeamVictory);
		Assert.Equal(before.VolcanoEruptionPeriod, gameData.rules.VolcanoEruptionPeriod);
		Assert.NotNull(painter.EditScenario(after with { AllianceNames = ["A", "A", "B", "C"] }));
		Assert.NotNull(painter.EditScenario(after with { PlagueStrength = 101 }));
		Assert.NotNull(painter.EditScenario(after with { AllianceWars = [(2, 2)] }));
	}

	[Fact]
	public void LockedAlliancesAreSavedAndLockedAtTheStart() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		ScenarioSettings after = Alliances(ScenarioSettings.Of(gameData, painter.Info));
		painter.EditScenario(after);
		using TempDirectory dir = new();
		SaveGame saved = SavedAndLoaded(gameData, painter.Info, dir);

		C7GameData.GameData again = ScenarioEditing.Open(ScenarioFiles.ForEditing(saved), fixture.behaviors);
		ScenarioSettings kept = ScenarioSettings.Of(again, saved.Scenario);
		Assert.Equal(after.AllianceNames, kept.AllianceNames);
		Assert.Equal(after.AllianceOf.OrderBy(kv => kv.Key), kept.AllianceOf.OrderBy(kv => kv.Key));
		Assert.Equal(after.AllianceWars.OrderBy(w => w), kept.AllianceWars.OrderBy(w => w));
		Assert.True(kept.CoalitionVictory);
		Assert.Equal((after.PlagueName, after.PlagueEarliestStart, after.PlagueVariance, after.PlagueDuration, after.PlagueGracePeriod, after.PlagueMaxOccurrences),
			(kept.PlagueName, kept.PlagueEarliestStart, kept.PlagueVariance, kept.PlagueDuration, kept.PlagueGracePeriod, kept.PlagueMaxOccurrences));

		// At the start: allies have met, at peace with a pact; enemies have
		// met at war.
		ScenarioStart.Apply(saved);
		SavePlayer Of(string civ) => saved.Players.Find(p => p.civilization == civ);
		PlayerRelationship allies = Of("Rome").playerRelationships[Of("Egypt").id.ToString()];
		Assert.Contains(allies.multiTurnDeals, d => d.dealSubType == DealSubType.Peace);
		Assert.Contains(allies.multiTurnDeals, d => d.dealSubType == DealSubType.MutualProtectionPact);
		Assert.Empty(Of("Rome").playerRelationships[Of("Carthage").id.ToString()].multiTurnDeals);
		Assert.Empty(Of("Greece").playerRelationships[Of("Carthage").id.ToString()].multiTurnDeals);
		Assert.Empty(Of("Egypt").playerRelationships[Of("Greece").id.ToString()].multiTurnDeals);
	}

	// Player Properties: what each player starts with goes onto the player;
	// the rest onto the scenario.
	private static ScenarioSettings Players(ScenarioSettings s) {
		ScenarioSettings changed = s.Copy();
		PlayerSettings rome = changed.Players.Find(p => p.Civilization == "Rome");
		rome.Treasury = 500;
		rome.Government = "Monarchy";
		rome.Era = 1;
		// A civilization always knows its own free advances (the game adds them
		// on loading), so they stay.
		rome.FreeTechs = [.. rome.FreeTechs.Union(["Bronze Working", "Alphabet"])];
		rome.Color = 7;
		rome.SkipFirstTurn = true;
		rome.LeaderName = "Scipio";
		rome.Difficulty = "Monarch";
		rome.StartUnits = [new StartUnit { type = "Warrior", count = 3 }];
		rome.StartWithEmbassies = true;
		PlayerSettings carthage = changed.Players.Find(p => p.Civilization == "Carthage");
		carthage.Choice = CivChoice.Random;
		carthage.Human = false;
		PlayerSettings greece = changed.Players.Find(p => p.Civilization == "Greece");
		greece.Civilization = "Babylon";
		greece.Human = false;
		greece.LeaderName = "Hammurabi";
		PlayerSettings egypt = changed.Players.Find(p => p.Civilization == "Egypt");
		egypt.Choice = CivChoice.Any;
		egypt.StartWithEmbassies = true;
		rome.Human = false;
		return changed;
	}

	[Fact]
	public void PlayerPropertiesAreOneUndoStepAndSaved() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		ScenarioSettings before = ScenarioSettings.Of(gameData, painter.Info);
		ScenarioSettings after = Players(before);
		Assert.Null(painter.EditScenario(after));

		Player rome = PlayerOf(gameData, "Rome");
		Assert.Equal((500, "Monarchy", "ERAS_Middle_Ages", 7, true), (rome.gold, rome.government.name, rome.eraCivilopediaName, rome.primaryColorIndex, rome.skipFirstTurn));
		Assert.Equal(["Alphabet", "Bronze Working", "Warrior Code"], gameData.techs.Where(t => rome.knownTechs.Contains(t.id)).Select(t => t.Name).Order());
		Assert.NotNull(PlayerOf(gameData, "Babylon"));
		Assert.Null(PlayerOf(gameData, "Greece"));
		Assert.Equal("Scipio", painter.Info.players.Find(s => s.player == rome.id.ToString()).leaderName);

		painter.Undo();
		Assert.Equal(before.Players.Find(p => p.Civilization == "Rome").Treasury, PlayerOf(gameData, "Rome").gold);
		Assert.NotNull(PlayerOf(gameData, "Greece"));
		Assert.Empty(painter.Info.players);
		Assert.NotNull(painter.EditScenario(Players(before) with { Players = [.. Players(before).Players.Select(p => p with { Civilization = "Rome", Choice = CivChoice.Fixed })] }));

		painter.EditScenario(after);
		// As the editor shows it (the civilizations' own advances added).
		ScenarioSettings shown = ScenarioSettings.Of(gameData, painter.Info);
		using TempDirectory dir = new();
		SaveGame saved = SavedAndLoaded(gameData, painter.Info, dir);
		C7GameData.GameData again = ScenarioEditing.Open(ScenarioFiles.ForEditing(saved), fixture.behaviors);
		ScenarioSettings kept = ScenarioSettings.Of(again, saved.Scenario);
		foreach (PlayerSettings want in shown.Players) {
			PlayerSettings got = kept.Players.Find(p => p.Id == want.Id);
			Assert.Equal(want with { StartUnits = got.StartUnits, FreeTechs = got.FreeTechs }, got);
			Assert.Equal(want.StartUnits.Select(u => (u.type, u.count)), got.StartUnits.Select(u => (u.type, u.count)));
			Assert.Equal(want.FreeTechs.Order(), got.FreeTechs.Order());
		}
	}

	// A player added to a scenario opened from its file doesn't bring a
	// second barbarian player (found by the harness: a loaded save doesn't
	// keep SavePlayer.isBarbarian).
	[Fact]
	public void AddingAPlayerToASavedScenarioKeepsOneBarbarianPlayer() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		using TempDirectory dir = new();
		SaveGame saved = SavedAndLoaded(gameData, painter.Info, dir);
		ScenarioPlayers.Add(saved, saved.Civilizations.First(c => c.name == "Babylon"), saved.Scenario);
		HashSet<string> barbarians = saved.Civilizations.Where(c => c.isBarbarian).Select(c => c.name).ToHashSet();
		Assert.Single(saved.Players, p => barbarians.Contains(p.civilization));
	}

	// At the start: the human picks the Any player's civilization from those
	// no fixed player has, a Random player gets another, Rome's leader is
	// Scipio, its own start gets its starting units, players with Start
	// with Embassies have embassies with each other, and Rome's difficulty
	// is the scenario's.
	[Fact]
	public void PlayerPropertiesAreHonouredAtTheStart() {
		(C7GameData.GameData gameData, MapPainter painter) = Scenario();
		ScenarioSettings after = Players(ScenarioSettings.Of(gameData, painter.Info));
		painter.EditScenario(after);
		Tile land = gameData.map.tiles[0];
		string romeId = IdOf(after, "Rome"), anyId = IdOf(after, "Egypt"), randomId = IdOf(after, "Carthage");
		painter.Info.playerStarts.Add(new PlayerStart { player = romeId, x = land.XCoordinate, y = land.YCoordinate });
		using TempDirectory dir = new();
		SaveGame saved = SavedAndLoaded(gameData, painter.Info, dir);

		List<ScenarioStart.HumanChoice> choices = ScenarioStart.HumanChoices(saved, new Random(1));
		Assert.All(choices, c => Assert.Equal(anyId, c.Player));
		Assert.Contains(choices, c => c.Civilization == "Greece");
		Assert.DoesNotContain(choices, c => c.Civilization is "Rome" or "Babylon");
		Assert.Equal("Monarch", ScenarioStart.DifficultyFor(saved, romeId));
		Assert.Null(ScenarioStart.DifficultyFor(saved, anyId));

		ScenarioStart.AssignCivilizations(saved, choices.First(c => c.Civilization == "Greece"), new Random(1));
		SavePlayer human = saved.Players.Single(p => p.human);
		Assert.Equal((anyId, "Greece"), (human.id.ToString(), human.civilization));
		string random = saved.Players.Find(p => p.id.ToString() == randomId).civilization;
		Assert.DoesNotContain(random, new[] { "Rome", "Babylon", "Greece" });
		Assert.Equal(saved.Players.Count, saved.Players.Select(p => p.civilization).Distinct().Count());
		// Random is random: over ten games, more than one civilization.
		HashSet<string> drawn = [];
		for (int seed = 0; seed < 10; ++seed) {
			SaveGame game = SaveGame.Load(dir.File("scenario.json"), s => s);
			ScenarioStart.AssignCivilizations(game, choices.First(c => c.Civilization == "Greece"), new Random(seed));
			drawn.Add(game.Players.Find(p => p.id.ToString() == randomId).civilization);
		}
		Assert.True(drawn.Count > 1, $"always {string.Join(", ", drawn)}");

		ScenarioStart.Apply(saved);
		Assert.Equal("Scipio", saved.Civilizations.Find(c => c.name == "Rome").leader);
		Assert.Equal("Hammurabi", saved.Civilizations.Find(c => c.name == "Babylon").leader);
		Assert.Equal(3, saved.Units.Count(u => u.owner.ToString() == romeId && u.prototype == "Warrior"));
		Assert.True(saved.Players.Find(p => p.id.ToString() == romeId).playerRelationships[anyId].hasEmbassy);
		Assert.True(saved.Players.Find(p => p.id.ToString() == anyId).playerRelationships[romeId].hasEmbassy);
		Assert.False(saved.Players.Find(p => p.id.ToString() == romeId).playerRelationships.ContainsKey(randomId));
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using QueryCiv3;
using Xunit;

namespace EngineTests.Editor;

// Phase 7: Civ3 settings the editor kept that the game now plays by. Each
// test sets the setting and watches the game do what Civ3's editor help
// says it does.
public class SettingsNowUsedTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public SettingsNowUsedTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] civs = gameData.players.Where(p => !p.isBarbarians && !p.isHuman).Take(2).ToArray();
		us = civs[0];
		them = civs[1];
	}

	private static bool IsEmptyLand(Tile t) {
		return t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.IsImpassable();
	}

	private City BuildCity(Player owner) {
		Tile tile = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities()
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity()));
		return CityInteractions.BuildCity(tile, owner, $"City {owner.cities.Count}");
	}

	private void AddCitizens(City city, Civilization nationality, int count) {
		for (int i = 0; i < count; ++i) {
			city.AddCitizen(new CityResident {
				city = city,
				nationality = nationality,
				citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen),
			});
		}
	}

	private City CapturedCity() {
		BuildCity(them);
		City city = BuildCity(them);
		AddCitizens(city, them.civilization, 3);
		us.EnsureRelationshipExists(them);
		PlayerRelationship.DeclareWar(us, them, false, gameData.turn + 10);
		CityInteractions.CaptureCity(city, us);
		return city;
	}

	// ---- Difficulty Levels: Corruption Modifier ----

	private List<float> CorruptionAt(int percent) {
		foreach (Difficulty d in gameData.difficulties.Append(gameData.gameDifficulty)) {
			d.CorruptionPercentage = percent;
		}
		us.DoCorruptionCalculations(gameData);
		return us.cities.OrderBy(c => c.rankIndex).Select(c => c.corruption).ToList();
	}

	[Fact]
	public void TheDifficultysCorruptionModifierScalesCorruption() {
		Tile capitalTile = us.units.First(u => u.unitType.isSettler).location;
		CityInteractions.BuildCity(capitalTile, us, us.GetNextCityName());
		foreach (Tile site in gameData.map.tiles.Where(t => IsEmptyLand(t) && t.IsAllowCities() && !t.HasCity() && t.RankDistanceTo(capitalTile) >= 3)
				.OrderBy(t => t.RankDistanceTo(capitalTile)).Take(6).ToList()) {
			CityInteractions.BuildCity(site, us, us.GetNextCityName());
		}
		List<float> full = CorruptionAt(100);
		List<float> half = CorruptionAt(50);
		Assert.Contains(full, c => c > 0);
		for (int i = 0; i < full.Count; ++i) {
			if (full[i] < 0.9f) {
				Assert.Equal(full[i] / 2, half[i], 4);
			}
		}
	}

	// ---- General Settings: Default Difficulty Level ----

	[Fact]
	public void TheRulesDefaultDifficultyIsTheStartingChoice() {
		List<Difficulty> levels = gameData.difficulties;
		// Nothing chosen before: the rules' default, not Regent.
		Assert.Equal("Deity", new NewGameChoices().ChosenDifficulty(levels, null, "Deity").Name);
		// The last game's choice comes before the rules'.
		Assert.Equal("Monarch", new NewGameChoices { Difficulty = "Monarch" }.ChosenDifficulty(levels, null, "Deity").Name);
		// A scenario's own default comes first.
		Assert.Equal("Chieftain", new NewGameChoices { Difficulty = "Monarch" }.ChosenDifficulty(levels, "Chieftain", "Deity").Name);
		Assert.Equal("Regent", new NewGameChoices().ChosenDifficulty(levels).Name);

		SaveGame scenario = fixture.saveGame.Clone();
		scenario.Scenario = new ScenarioInfo { customRules = true };
		scenario.Rules.DefaultDifficulty = "Emperor";
		Assert.Equal("Emperor", NewGameChoices.ScenarioDifficulty(scenario));
		scenario.Scenario.customRules = false;
		Assert.Null(NewGameChoices.ScenarioDifficulty(scenario));
	}

	// ---- General Settings: Default Money Resource ----

	[Fact]
	public void TheMoneyResourceIsLeftWhereAHutWasOpened() {
		Resource money = gameData.Resources.First(r => r.Category == ResourceCategory.LUXURY);
		gameData.rules.DefaultMoneyResource = money.Name;
		MapUnit unit = us.units.First(u => !u.unitType.isSettler);
		Tile hut = unit.location.neighbors.Values.First(t => t != Tile.NONE && IsEmptyLand(t) && t.Resource == Resource.NONE);
		hut.hasGoodyHut = true;
		Assert.NotNull(GoodyHuts.Enter(gameData, unit, hut));
		Assert.False(hut.hasGoodyHut);
		Assert.Equal(money, hut.Resource);

		// Without one, nothing is left.
		gameData.rules.DefaultMoneyResource = null;
		Tile other = unit.location.neighbors.Values.First(t => t != Tile.NONE && t != hut && IsEmptyLand(t) && t.Resource == Resource.NONE);
		other.hasGoodyHut = true;
		GoodyHuts.Enter(gameData, unit, other);
		Assert.Equal(Resource.NONE, other.Resource);
	}

	// ---- Culture: resistance and propaganda chances ----

	[Fact]
	public void ResistanceFollowsTheRulesChancesAndGovernmentModifiers() {
		City city = CapturedCity();
		foreach (City c in us.cities) {
			c.perPlayerCulture[us] = 0;
		}
		// No culture of ours: the lowest opinion.
		CultureOpinion lowest = gameData.rules.CultureOpinions[^1];
		lowest.InitialResistanceChance = 33;
		lowest.ContinuedResistanceChance = 22;
		Government ours = us.government;
		GovernmentRelation relation = ours.relations.First(r => r.government == them.government.name);
		relation.resistanceModifier = 7;
		Assert.Equal(40, city.ResistancePercent(them, continuing: false));
		Assert.Equal(29, city.ResistancePercent(them, continuing: true));
	}

	[Fact]
	public void PropagandaFollowsTheRulesChancesAndGovernments() {
		foreach (CultureOpinion o in gameData.rules.CultureOpinions) {
			o.PropagandaChance = 17;
		}
		Assert.Equal(17, C7Engine.Espionage.CultureFactor(gameData, us, them));

		GovernmentRelation relation = us.government.relations.First(r => r.government == them.government.name);
		relation.briberyModifier = 12;
		Assert.Equal(12, C7Engine.Espionage.GovernmentModifier(us, them));
		Assert.True(C7Engine.Espionage.CanBribe(us, them));
		relation.canBribe = false;
		Assert.False(C7Engine.Espionage.CanBribe(us, them));
	}

	[Fact]
	public void AGovernmentIsImmuneToTheMissionItNames() {
		Government theirs = them.government;
		int propaganda = gameData.rules.EspionageMissions.FindIndex(m => m.Mission == nameof(EspionageMission.Propaganda));
		int investigate = gameData.rules.EspionageMissions.FindIndex(m => m.Mission == nameof(EspionageMission.InvestigateCity));
		theirs.immuneTo = -1;
		Assert.False(C7Engine.Espionage.ImmuneTo(them, EspionageMission.Propaganda));
		theirs.immuneTo = investigate;
		Assert.True(C7Engine.Espionage.ImmuneTo(them, EspionageMission.InvestigateCity));
		Assert.False(C7Engine.Espionage.ImmuneTo(them, EspionageMission.Propaganda));
		theirs.immuneTo = propaganda;
		Assert.True(C7Engine.Espionage.ImmuneToPropaganda(them));
		// The standard rules: Democracy is.
		Assert.Equal(propaganda, gameData.governments.First(g => g.name == "Democracy").immuneTo);
	}

	// ---- Governments: Spies Are, Diplomats Are ----

	[Fact]
	public void SpiesAndDiplomatsHaveTheirGovernmentsExperience() {
		Government g = us.government;
		g.spyLevel = "Elite";
		Assert.Equal(C7Engine.Espionage.Agent.EliteSpy, C7Engine.Espionage.SpyLevel(us));
		Assert.Equal(75, C7Engine.Espionage.PlantSpyPercent(C7Engine.Espionage.SpyLevel(us)));
		g.spyLevel = "Conscript";
		Assert.Equal(C7Engine.Espionage.Agent.ConscriptSpy, C7Engine.Espionage.SpyLevel(us));
		Assert.Equal(20, C7Engine.Espionage.PlantSpyPercent(C7Engine.Espionage.SpyLevel(us)));
		g.spyLevel = "Veteran";
		Assert.Equal(C7Engine.Espionage.Agent.VeteranSpy, C7Engine.Espionage.SpyLevel(us));

		g.diplomatLevel = "Regular";
		Assert.Equal(40, C7Engine.Espionage.GetawayPercent(C7Engine.Espionage.Agent.Embassy, EspionageRisk.Immediately, C7Engine.Espionage.DiplomatStep(us)));
		g.diplomatLevel = "Veteran";
		Assert.Equal(50, C7Engine.Espionage.GetawayPercent(C7Engine.Espionage.Agent.Embassy, EspionageRisk.Immediately, C7Engine.Espionage.DiplomatStep(us)));

		// The standard rules: Communism's spies are veterans.
		Assert.Equal("Veteran", gameData.governments.First(x => x.name == "Communism").spyLevel);
	}

	// ---- Governments: Assimilation Chance ----

	[Fact]
	public void ForeignCitizensAssimilateWithTheGovernmentsChance() {
		City city = BuildCity(us);
		AddCitizens(city, them.civilization, 4);
		us.government.assimilationChance = 0;
		Assert.Equal(0, city.Assimilate());
		us.government.assimilationChance = 100;
		Assert.Equal(4, city.Assimilate());
		Assert.All(city.residents, r => Assert.Equal(us.civilization, r.nationality));
	}

	[Fact]
	public void ResistersDontAssimilate() {
		City city = CapturedCity();
		city.resisters = city.residents.Count;
		city.resistanceFrom = them;
		us.government.assimilationChance = 100;
		Assert.Equal(0, city.Assimilate());
	}

	// ---- Scenario: Retain Culture on Capture ----

	[Fact]
	public void ACapturedCityKeepsItsCultureWhenTheScenarioSaysSo() {
		BuildCity(them);
		City kept = BuildCity(them);
		kept.perPlayerCulture[them] = 500;
		City lost = BuildCity(them);
		lost.perPlayerCulture[them] = 500;
		us.EnsureRelationshipExists(them);
		PlayerRelationship.DeclareWar(us, them, false, gameData.turn + 10);

		gameData.rules.RetainCultureOnCapture = false;
		CityInteractions.CaptureCity(lost, us);
		Assert.Equal(0, lost.GetCulture());
		gameData.rules.RetainCultureOnCapture = true;
		CityInteractions.CaptureCity(kept, us);
		Assert.Equal(500, kept.GetCulture());
	}

	// ---- Scenario: Civ-Specific Abilities, Preserve Random Seed ----

	[Fact]
	public void WithoutCivSpecificAbilitiesNoCivilizationHasTraits() {
		SaveGame save = fixture.saveGame.Clone();
		save.Rules.CivSpecificAbilities = false;
		CreateGame.createGame(save, _ => fixture.behaviors).GetAwaiter().GetResult();
		Assert.All(EngineStorage.gameData.players.Where(p => p.civilization != null), p => Assert.Empty(p.civilization.traits));

		save = fixture.saveGame.Clone();
		CreateGame.createGame(save, _ => fixture.behaviors).GetAwaiter().GetResult();
		Assert.Contains(EngineStorage.gameData.players, p => p.civilization?.traits.Count > 0);
	}

	private List<int> Draws(SaveGame save) {
		C7GameData.GameData g = save.ToGameData(fixture.behaviors);
		return Enumerable.Range(0, 8).Select(_ => g.random.Next(1000000)).ToList();
	}

	[Fact]
	public void ALoadedGameKeepsItsRandomNumbersOnlyIfTheSeedIsPreserved() {
		C7GameData.GameData played = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(played);
		played.random.Next();
		SaveGame saved = SaveGame.FromJSON(SaveGame.FromGameData(played).ToCompactJSON());
		Assert.NotNull(saved.RngState);

		saved.Rules.PreserveRandomSeed = true;
		Assert.Equal(Draws(saved), Draws(saved));
		saved.Rules.PreserveRandomSeed = false;
		Assert.NotEqual(Draws(saved), Draws(saved));
	}

	// ---- Scenario: Respawn AI Players ----

	private void Destroy(Player player) {
		foreach (City c in player.cities.ToList()) {
			CityInteractions.DestroyCity(c);
		}
		foreach (MapUnit u in player.units.ToList()) {
			gameData.RemoveUnit(u);
		}
		gameData.CheckForCivDestructionAndNotifyUi(player);
		Assert.True(player.defeated);
	}

	[Fact]
	public void AFallenComputerCivComesBackAsAnotherWhenTheScenarioSaysSo() {
		Civilization old = them.civilization;
		gameData.rules.RespawnAiPlayers = false;
		Destroy(them);
		AiRespawn.ProcessEndOfRound(gameData);
		Assert.True(them.defeated);

		gameData.rules.RespawnAiPlayers = true;
		AiRespawn.ProcessEndOfRound(gameData);
		Assert.False(them.defeated);
		Assert.NotEqual(old, them.civilization);
		Assert.DoesNotContain(gameData.players, p => p != them && p.civilization == them.civilization);
		Assert.Contains(them.units, u => u.unitType.isSettler);
		Tile start = them.units[0].location;
		Assert.All(gameData.cities, c => Assert.True(c.location.DistanceTo(start) >= AiRespawn.MinimumDistanceFromCities));
		Assert.Contains(old, gameData.fallenCivilizations);

		// The old civilization stays gone, saved and loaded too.
		SaveGame saved = SaveGame.FromJSON(SaveGame.FromGameData(gameData).ToCompactJSON());
		Assert.Contains(old.name, saved.FallenCivilizations);
	}

	[Fact]
	public void AHumanNeverRespawns() {
		gameData.rules.RespawnAiPlayers = true;
		Player human = gameData.players.First(p => p.isHuman);
		Destroy(human);
		AiRespawn.ProcessEndOfRound(gameData);
		Assert.True(human.defeated);
	}

	// ---- Scenario: the time limit in minutes ----

	[Fact]
	public void TheGameEndsWhenItsMinutesAreUp() {
		gameData.rules.TimeLimitMinutes = 10;
		TimeLimitVictory limit = new(0);
		gameData.playedSeconds = 599;
		Assert.False(limit.HasVictory(limit.Evaluate(us, gameData)));
		gameData.playedSeconds = 600;
		Assert.True(limit.HasVictory(limit.Evaluate(us, gameData)));
		gameData.rules.TimeLimitMinutes = 0;
		Assert.False(limit.HasVictory(limit.Evaluate(us, gameData)));

		// The time played is saved, and a scenario starts with none.
		gameData.playedSeconds = 1234;
		SaveGame saved = SaveGame.FromJSON(SaveGame.FromGameData(gameData).ToCompactJSON());
		Assert.Equal(1234, saved.PlayedSeconds);
		Assert.Equal(1234, saved.ToGameData(fixture.behaviors).playedSeconds);

		// A game with a time limit has the time-limit victory.
		SaveGame timed = fixture.saveGame.Clone();
		timed.Rules.TimeLimitMinutes = 5;
		Assert.Contains(timed.ToGameData(fixture.behaviors).victories, v => v is TimeLimitVictory);
	}

	// ---- Scenario: Culturally-Linked Start ----

	[Fact]
	public void CivilizationsOfACultureGroupStartNearEachOther() {
		SaveGame save = fixture.saveGame.Clone();
		save.Map.wrapHorizontally = false;
		save.Map.wrapVertically = false;
		// Starts in a row; civs alternating between two groups.
		List<SaveTile> starts = Enumerable.Range(0, 6).Select(i => new SaveTile { X = 10 * i, Y = 0 }).ToList();
		List<GameSetup.PlannedPlayer> planned = Enumerable.Range(0, 6).Select(i => new GameSetup.PlannedPlayer {
			civ = new Civilization { name = $"Civ {i}", cultureGroupKey = i % 2 == 0 ? "East" : "West" },
		}).ToList();
		List<SaveTile> linked = GameSetup.LinkStartsByCulture(save.Map, starts, planned);
		Assert.Equal(6, linked.Distinct().Count());
		int[] east = [linked[0].X, linked[2].X, linked[4].X];
		int[] west = [linked[1].X, linked[3].X, linked[5].X];
		// Each group takes three neighbouring starts.
		Assert.Equal(20, east.Max() - east.Min());
		Assert.Equal(20, west.Max() - west.Min());
	}

	// ---- Importing Civ3's switches ----

	[SkippableFact]
	public void Civ3ScenariosSwitchesAreImported() {
		Skip.If(Civ3TestData.ShouldSkipCiv3DependentTests(), Civ3TestData.SkipReason);
		string scenarios = Path.Join(Civ3Location.GetCiv3Path(), "Conquests", "Scenarios");
		string rome = Path.Join(scenarios, "2 MP Rise of Rome.biq");
		Skip.If(!File.Exists(rome), "No Rise of Rome");
		Func<string, string> icons = _ => Path.Join(Civ3Location.GetCiv3Path(), "Conquests", "Text", "PediaIcons.txt");
		SaveGame save = ScenarioFiles.Open(rome, PathUtils.defaultBicPath, icons);
		Assert.True(save.Rules.RetainCultureOnCapture);
		Assert.True(save.Scenario.retainCulture);
		Assert.True(ScenarioSettings.Of(ScenarioEditing.Open(save, fixture.behaviors), save.Scenario).RetainCulture);
	}
}

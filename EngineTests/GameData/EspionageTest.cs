using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.AI;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's diplomatic and spy missions (CivFanatics' "A Study of Espionage
// Missions"): costs distance × level + the mission's term, chances by risk
// and by the embassy or the spy's experience.
public class EspionageTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City ourCity;
	private readonly City theirCity;

	// Hands out the given rolls in turn, then the last one again.
	private class Rolls(params int[] values) : Random {
		private int next;
		public override int Next(int maxValue) => Math.Min(values[Math.Min(next++, values.Length - 1)], maxValue - 1);
	}

	public EspionageTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		ourCity = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		theirCity = CityInteractions.BuildCity(them.units[0].location, them, "Abroad");
		us.EnsureRelationshipExists(them);
		Embassies.Open(us, them);
		us.knownTechs.Add(gameData.techs.Find(t => t.Name == "Writing").id);
		us.gold = 10000;
		gameData.turn = 30;
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private EspionageMission Mission(string key) => gameData.rules.Mission(key);
	private bool Can(string key) => Espionage.CanAttempt(us, them, Mission(key), out _);
	private int Distance => ourCity.location.DistanceTo(theirCity.location);

	private void Agency() {
		ourCity.AddBuilding(gameData.Buildings.Find(b => b.name == "Intelligence Agency"), gameData);
	}

	private Government Government(string name) => gameData.governments.Find(g => g.name == name);

	private Espionage.Result Attempt(string key, Random rng, Espionage.Risk risk = Espionage.Risk.Immediate, City city = null) {
		return Espionage.Attempt(gameData, us, them, key, city, risk, rng);
	}

	[Fact]
	public void GovernmentsHaveCiv3sSpyData() {
		Assert.Equal(2, Government("Communism").spiesAre);
		Assert.Equal(1, Government("Republic").spiesAre);
		Assert.Equal(EspionageMission.Propaganda, Government("Democracy").immuneTo);
		Assert.Equal(-30, Government("Despotism").briberyModifiers["Democracy"]);
		Assert.Equal(30, Government("Democracy").briberyModifiers["Despotism"]);
	}

	[Fact]
	public void AnEmbassyAloneAllowsTheDiplomaticMissions() {
		Assert.True(Can(EspionageMission.InvestigateCity));
		Assert.True(Can(EspionageMission.StealTechnology));
		Assert.False(Can(EspionageMission.PlantSpy));
		Assert.False(Can(EspionageMission.SabotageProduction));
		Assert.False(Can(EspionageMission.Embassy));

		us.playerRelationships[them.id].hasEmbassy = false;
		Assert.False(Can(EspionageMission.StealTechnology));
		Assert.True(Can(EspionageMission.Embassy));
	}

	[Fact]
	public void AnIntelligenceAgencyPlantsSpiesAndSpiesDoTheRest() {
		Agency();
		Assert.True(Can(EspionageMission.PlantSpy));
		Assert.True(Can(EspionageMission.ExposeEnemySpy));
		Assert.False(Can(EspionageMission.SabotageProduction));
		us.playerRelationships[them.id].hasSpy = true;
		Assert.False(Can(EspionageMission.PlantSpy));
		foreach (string key in new[] { EspionageMission.StealTechnology, EspionageMission.StealWorldMap, EspionageMission.StealPlans,
			EspionageMission.Propaganda, EspionageMission.SabotageProduction }) {
			Assert.True(Espionage.CanAttempt(us, them, Mission(key), out bool bySpy));
			Assert.True(bySpy);
		}
		// A democracy is immune to propaganda.
		them.government = Government("Democracy");
		Assert.False(Can(EspionageMission.Propaganda));
	}

	[Fact]
	public void TheCostIsDistanceTimesLevelPlusTheMissionsTerm() {
		Agency();
		us.playerRelationships[them.id].hasSpy = true;
		int dl = Distance * theirCity.SizeLevel();
		int size = theirCity.residents.Count;
		int Cost(string key, Espionage.Risk risk = Espionage.Risk.Immediate) =>
			Espionage.Cost(gameData, us, them, Mission(key), theirCity, risk);
		Assert.Equal(dl + 10 * size, Cost(EspionageMission.InvestigateCity));
		Assert.Equal(dl + 60, Cost(EspionageMission.PlantSpy));
		int steal = dl + 10 * them.knownTechs.Count * gameData.map.techRate / 100;
		Assert.Equal(steal, Cost(EspionageMission.StealTechnology));
		Assert.Equal(steal * 150 / 100, Cost(EspionageMission.StealTechnology, Espionage.Risk.Carefully));
		Assert.Equal(steal * 2, Cost(EspionageMission.StealTechnology, Espionage.Risk.Safely));
		Assert.Equal(dl + 1 * (gameData.map.numTilesWide + gameData.map.numTilesTall) / 2, Cost(EspionageMission.StealWorldMap));
		Assert.Equal(dl + 10 * them.units.Count, Cost(EspionageMission.StealPlans));
		theirCity.SetStoredShields(7);
		Assert.Equal(dl + 10 * 7, Cost(EspionageMission.SabotageProduction));
		// Planting has no risk to choose.
		Assert.Equal(dl + 60, Cost(EspionageMission.PlantSpy, Espionage.Risk.Safely));
	}

	[Fact]
	public void TheChancesFollowTheStudy() {
		int Chance(string key, bool bySpy, Espionage.Risk risk) => Espionage.SuccessPercent(gameData, us, key, bySpy, risk);
		Assert.Equal([32, 44, 52], new[] { Espionage.Risk.Immediate, Espionage.Risk.Carefully, Espionage.Risk.Safely }
			.Select(r => Chance(EspionageMission.StealTechnology, false, r)));
		Assert.Equal([48, 60, 68], new[] { Espionage.Risk.Immediate, Espionage.Risk.Carefully, Espionage.Risk.Safely }
			.Select(r => Chance(EspionageMission.StealTechnology, true, r)));
		Assert.Equal(50, Chance(EspionageMission.PlantSpy, true, Espionage.Risk.Immediate));
		Assert.Equal(100, Chance(EspionageMission.InvestigateCity, false, Espionage.Risk.Immediate));
		us.government = Government("Communism");
		Assert.Equal([56, 68, 76], new[] { Espionage.Risk.Immediate, Espionage.Risk.Carefully, Espionage.Risk.Safely }
			.Select(r => Chance(EspionageMission.StealTechnology, true, r)));
		Assert.Equal(60, Chance(EspionageMission.PlantSpy, true, Espionage.Risk.Immediate));
	}

	[Fact]
	public void AnInvestigationAlwaysWorksAndIsPaidFor() {
		int cost = Espionage.Cost(gameData, us, them, Mission(EspionageMission.InvestigateCity), theirCity, Espionage.Risk.Immediate);
		Espionage.Result result = Attempt(EspionageMission.InvestigateCity, new Rolls(99), city: theirCity);
		Assert.Equal(Espionage.Outcome.Succeeded, result.outcome);
		Assert.Contains(theirCity.name, result.text);
		// Shown on the city screen.
		Assert.Equal(theirCity, result.city);
		Assert.Equal(10000 - cost, us.gold);
	}

	[Fact]
	public void NoMissionWithoutTheGold() {
		us.gold = 0;
		Assert.Null(Attempt(EspionageMission.InvestigateCity, new Rolls(0), city: theirCity));
	}

	[Fact]
	public void StealingATechnology() {
		Tech tech = gameData.techs.First(t => !us.knownTechs.Contains(t.id) && !them.knownTechs.Contains(t.id));
		them.knownTechs.Add(tech.id);
		foreach (ID known in them.knownTechs.Where(t => t != tech.id)) {
			us.knownTechs.Add(known);
		}
		// Gets away (0), then works (0).
		Espionage.Result result = Attempt(EspionageMission.StealTechnology, new Rolls(0));
		Assert.Equal(Espionage.Outcome.Succeeded, result.outcome);
		Assert.Contains(tech.id, us.knownTechs);
	}

	[Fact]
	public void AFailureAngersTheTarget() {
		int before = them.playerRelationships[us.id].attitudePenalty;
		// Gets away (0), then fails (99).
		Espionage.Result result = Attempt(EspionageMission.StealTechnology, new Rolls(0, 99));
		Assert.Equal(Espionage.Outcome.Failed, result.outcome);
		Assert.Equal(before + gameData.rules.attitude.failedEspionage, them.playerRelationships[us.id].attitudePenalty);
		Assert.Equal(gameData.rules.attitude.failedEspionageForAWhile, them.playerRelationships[us.id].spyAnger);
		Assert.DoesNotContain(EngineStorage.messagesToUI, m => m is MsgSpyCaught);
	}

	[Fact]
	public void ACaughtSpyIsLostAndTheTargetIsTold() {
		Agency();
		us.playerRelationships[them.id].hasSpy = true;
		Espionage.Result result = Attempt(EspionageMission.StealWorldMap, new Rolls(99));
		Assert.Equal(Espionage.Outcome.Caught, result.outcome);
		Assert.False(us.playerRelationships[them.id].hasSpy);
		MsgSpyCaught caught = Assert.IsType<MsgSpyCaught>(EngineStorage.messagesToUI.Single(m => m is MsgSpyCaught));
		Assert.Equal(them.id, caught.victimId);
		Assert.Equal(us, caught.spymaster);
	}

	[Fact]
	public void PlantingASpyAndAFailedPlantsFlag() {
		Agency();
		Assert.Equal(Espionage.Outcome.Failed, Attempt(EspionageMission.PlantSpy, new Rolls(50)).outcome);
		Assert.True(us.playerRelationships[them.id].spyPlantFailed);
		Assert.False(Can(EspionageMission.PlantSpy));
		gameData.rules.espionage.plantFailedClearsOneIn = 1;
		Espionage.UpdateForNewTurn(gameData);
		Assert.True(Can(EspionageMission.PlantSpy));
		Assert.Equal(Espionage.Outcome.Succeeded, Attempt(EspionageMission.PlantSpy, new Rolls(49)).outcome);
		Assert.True(Espionage.HasSpy(us, them));
	}

	[Fact]
	public void ASpyStealsMapsAndPlansAndSabotages() {
		Agency();
		us.playerRelationships[them.id].hasSpy = true;
		List<Tile> map = them.tileKnowledge.knownTiles.Where(t => !us.tileKnowledge.isTileKnown(t)).ToList();
		Assert.Equal(Espionage.Outcome.Succeeded, Attempt(EspionageMission.StealWorldMap, new Rolls(0)).outcome);
		Assert.All(map, t => Assert.True(us.tileKnowledge.isTileKnown(t)));

		Tile far = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !us.tileKnowledge.isTileKnown(t));
		gameData.SpawnUnit(them, gameData.unitPrototypes.Find(u => u.name == "Warrior"), far);
		Assert.Equal(Espionage.Outcome.Succeeded, Attempt(EspionageMission.StealPlans, new Rolls(0)).outcome);
		Assert.True(us.tileKnowledge.isTileKnown(far));

		theirCity.SetStoredShields(12);
		Assert.Equal(Espionage.Outcome.Succeeded, Attempt(EspionageMission.SabotageProduction, new Rolls(0), city: theirCity).outcome);
		Assert.Equal(0, theirCity.shieldsStored);
	}

	[Fact]
	public void ExposingTheirSpyAmongUs() {
		Agency();
		Assert.Equal(Espionage.Outcome.Failed, Attempt(EspionageMission.ExposeEnemySpy, new Rolls(0)).outcome);
		them.playerRelationships[us.id].hasSpy = true;
		Assert.Equal(Espionage.Outcome.Succeeded, Attempt(EspionageMission.ExposeEnemySpy, new Rolls(0)).outcome);
		Assert.False(Espionage.HasSpy(them, us));
	}

	[Fact]
	public void PropagandaPersuadesCitizensAndMayTurnTheCity() {
		Agency();
		us.playerRelationships[them.id].hasSpy = true;
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (theirCity.residents.Count < 4) {
			CityResident resident = new() { city = theirCity, nationality = them.civilization, citizenType = laborer };
			theirCity.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
		// Not the capital, for this; and a government ours has a modifier
		// against (Despotism against Monarchy: -10).
		theirCity.capital = false;
		them.government = Government("Monarchy");
		Assert.NotEqual(0, us.government.briberyModifiers[them.government.name]);
		CityResident one = theirCity.residents[0];
		int chance = Espionage.PropagandaPercent(gameData, us, theirCity, one);
		int expected = Resistance.OpinionOf(gameData, them, us).propagandaChance
			+ us.government.briberyModifiers[them.government.name]
			- 5 * theirCity.location.unitsOnTile.Count(u => u.owner == them);
		Assert.Equal(expected, chance);
		one.nationality = us.civilization;
		Assert.Equal(expected + 20, Espionage.PropagandaPercent(gameData, us, theirCity, one));
		theirCity.capital = true;
		Assert.Equal(expected + 20 - 40, Espionage.PropagandaPercent(gameData, us, theirCity, one));
		theirCity.capital = false;

		// Two of four persuaded: confused, not enough to defect. (A 50%
		// chance for their own people.)
		gameData.rules.espionage.propagandaPerUnit = 0;
		us.government.briberyModifiers[them.government.name] = 0;
		us.government.briberyModifiers[them.government.name] = 50 - Espionage.PropagandaPercent(gameData, us, theirCity, theirCity.residents[1]);
		Espionage.Result result = Attempt(EspionageMission.Propaganda, new Rolls(0, 0, 99, 99), city: theirCity);
		Assert.Equal(Espionage.Outcome.Failed, result.outcome);
		Assert.Equal(2, theirCity.confusedCitizens);
		Espionage.UpdateForNewTurn(gameData);
		Assert.Equal(1, theirCity.confusedCitizens);

		// Three of four: it joins us.
		Assert.Equal(Espionage.Outcome.Succeeded, Attempt(EspionageMission.Propaganda, new Rolls(0, 0, 0, 99), city: theirCity).outcome);
		Assert.Equal(us, theirCity.owner);
	}

	[Fact]
	public void ConfusedCitizensAreUnhappy() {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (theirCity.residents.Count < 3) {
			CityResident resident = new() { city = theirCity, nationality = them.civilization, citizenType = laborer };
			theirCity.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
		theirCity.RecalculateCitizenMoods(gameData);
		int unhappy = theirCity.residents.Count(r => r.mood == CityResident.Mood.Unhappy);
		theirCity.confusedCitizens = 1;
		theirCity.RecalculateCitizenMoods(gameData);
		Assert.Equal(unhappy + 1, theirCity.residents.Count(r => r.mood == CityResident.Mood.Unhappy));
	}

	[Fact]
	public void AngerAtFailedMissionsFades() {
		them.playerRelationships[us.id].spyAnger = 3;
		gameData.turn = 40;
		Attitude.UpdateForNewTurn(gameData);
		Assert.Equal(2, them.playerRelationships[us.id].spyAnger);
		gameData.turn = 41;
		Attitude.UpdateForNewTurn(gameData);
		Assert.Equal(2, them.playerRelationships[us.id].spyAnger);
	}

	[Fact]
	public void ThePlayersMessageTriesTheMissionAndTellsTheUI() {
		new MsgEspionageMission(us.id, them.id, theirCity.id, EspionageMission.InvestigateCity, Espionage.Risk.Immediate).process();
		MsgEspionageResult result = Assert.IsType<MsgEspionageResult>(EngineStorage.messagesToUI.Single(m => m is MsgEspionageResult));
		Assert.Equal(us.id, result.playerId);
		Assert.Equal(Espionage.Outcome.Succeeded, result.result.outcome);
	}

	[Fact]
	public void TheAIPlantsASpyThenStealsFromCivsItDislikes() {
		Agency();
		Assert.Null(EspionageAI.Choose(gameData, us, them));
		us.playerRelationships[them.id].attitudePenalty = 50;
		Assert.Equal(EspionageMission.PlantSpy, EspionageAI.Choose(gameData, us, them));
		us.playerRelationships[them.id].hasSpy = true;
		them.knownTechs.Add(gameData.techs.First(t => !us.knownTechs.Contains(t.id)).id);
		Assert.Equal(EspionageMission.StealTechnology, EspionageAI.Choose(gameData, us, them));
		us.gold = 0;
		Assert.Null(EspionageAI.Choose(gameData, us, them));
	}

	[Fact]
	public void TheAIExposesSpiesAmongIt() {
		Agency();
		them.playerRelationships[us.id].hasSpy = true;
		Assert.Equal(EspionageMission.ExposeEnemySpy, EspionageAI.Choose(gameData, us, them));
	}

	[Fact]
	public void SpiesAndConfusionAreSaved() {
		PlayerRelationship relationship = us.playerRelationships[them.id];
		relationship.hasSpy = true;
		relationship.spyPlantFailed = true;
		relationship.spyAnger = 2;
		relationship.lastEspionageTurn = 25;
		theirCity.confusedCitizens = 1;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		PlayerRelationship again = reloaded.GetPlayer(us.id).playerRelationships[them.id];
		Assert.True(again.hasSpy);
		Assert.True(again.spyPlantFailed);
		Assert.Equal(2, again.spyAnger);
		Assert.Equal(25, again.lastEspionageTurn);
		Assert.Equal(1, reloaded.cities.Find(c => c.id == theirCity.id).confusedCitizens);
	}
}

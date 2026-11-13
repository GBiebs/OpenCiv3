using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using Xunit;

namespace EngineTests.GameData;

// Regression tests for fixes to the economy, diplomacy and city code that
// don't need a full game.
public class FixEconomyTests {
	private readonly C7GameData.GameData gameData = new() {
		gameDifficulty = new Difficulty(),
		rules = new Rules(),
		history = new Dictionary<string, List<HistTurnRecord>>(),
		timeOptions = new TimeOptions(),
	};

	private int nextPlayer = 1;

	public FixEconomyTests() {
		EngineStorage.InitializeGameDataForTests(gameData);
	}

	private Player MakePlayer(string civName = "Testers") {
		Player player = new() {
			id = ID.FromString($"player-{nextPlayer++}"),
			isHuman = true,
			civilization = new Civilization(civName),
			government = new Government(),
			rules = gameData.rules,
		};
		gameData.players.Add(player);
		return player;
	}

	private static City MakeCity(Player owner, string name = "Testville") {
		Tile tile = new(ID.None("tile")) { continent = 1 };
		City city = new(tile, owner, name, ID.None("city"));
		tile.cityAtTile = city;
		owner.cities.Add(city);
		return city;
	}

	private Building MakeBuilding(string name, bool greatWonder = false) {
		SaveBuilding sb = new() { name = name };
		if (greatWonder) {
			sb.greatWonderProperties = new();
		}
		return new Building(sb, gameData);
	}

	// One-way deals

	[Fact]
	public void OneWayDealCounterpartKeepsTheStartTurn() {
		Player giver = MakePlayer("Givers");
		Player taker = MakePlayer("Takers");
		giver.EnsureRelationshipExists(taker);

		MultiTurnDeal deal = new(DealType.Gold, DealSubType.GoldPerTurn, DealDetails.Outbound,
			goldPerTurn: 3, dealDuration: 20, turnStartDeal: 100);
		PlayerRelationship.RegisterMultiTurnDeal(giver, taker, deal);

		MultiTurnDeal counterpart = MultiTurnDeal.GetCounterpartDeal(giver, taker, deal);
		Assert.NotNull(counterpart);
		Assert.Equal(DealDetails.Inbound, counterpart.dealDetails);
		Assert.Equal(100, counterpart.turnStartDeal);
		Assert.Equal(120, counterpart.turnEndDeal);
		Assert.Equal(3, counterpart.goldPerTurn);

		// Both sides run for the whole deal, and are counted while they do.
		PlayerRelationship.CheckForObsoleteDeals(giver, gameData.players, 110);
		PlayerRelationship.CheckForObsoleteDeals(taker, gameData.players, 110);
		Assert.Contains(deal, giver.playerRelationships[taker.id].multiTurnDeals);
		Assert.Contains(counterpart, taker.playerRelationships[giver.id].multiTurnDeals);

		PlayerRelationship.CheckForObsoleteDeals(giver, gameData.players, 120);
		PlayerRelationship.CheckForObsoleteDeals(taker, gameData.players, 120);
		Assert.DoesNotContain(deal, giver.playerRelationships[taker.id].multiTurnDeals);
		Assert.DoesNotContain(counterpart, taker.playerRelationships[giver.id].multiTurnDeals);
	}

	// Deal expiry

	private static MultiTurnDeal Exchange(DealSubType subType, int start, int duration = 20) {
		return new MultiTurnDeal(DealType.DiplomaticAgreement, subType, DealDetails.Exchange,
			dealDuration: duration, turnStartDeal: start);
	}

	[Fact]
	public void MutualProtectionPactsExpireOutsideLockedAlliances() {
		Player a = MakePlayer("A");
		Player b = MakePlayer("B");
		a.EnsureRelationshipExists(b);
		MultiTurnDeal pact = Exchange(DealSubType.MutualProtectionPact, 10);
		PlayerRelationship.RegisterMultiTurnDeal(a, b, pact);

		PlayerRelationship.CheckForObsoleteDeals(a, gameData.players, 29);
		Assert.Contains(pact, a.playerRelationships[b.id].multiTurnDeals);

		PlayerRelationship.CheckForObsoleteDeals(a, gameData.players, 30);
		Assert.DoesNotContain(pact, a.playerRelationships[b.id].multiTurnDeals);
	}

	[Fact]
	public void OnlyMutualProtectionPactsOfLockedAlliesNeverExpire() {
		Player a = MakePlayer("A");
		Player b = MakePlayer("B");
		Alliance alliance = new(0, "Allies");
		a.alliance = alliance;
		b.alliance = alliance;
		a.EnsureRelationshipExists(b);
		Assert.True(gameData.AreInLockedPeace(a, b));

		// Locked alliances' pacts are made without an end turn.
		MultiTurnDeal pact = MultiTurnDeal.DEFAULT_MUTUAL_PROTECTION_PACT;
		MultiTurnDeal passage = Exchange(DealSubType.RightOfPassage, 10);
		PlayerRelationship.RegisterMultiTurnDeal(a, b, pact);
		PlayerRelationship.RegisterMultiTurnDeal(a, b, passage);

		PlayerRelationship.CheckForObsoleteDeals(a, gameData.players, 500);

		List<MultiTurnDeal> deals = a.playerRelationships[b.id].multiTurnDeals;
		Assert.Contains(pact, deals);
		Assert.DoesNotContain(passage, deals);
		Assert.Contains(deals, d => d.dealSubType == DealSubType.Peace);
	}

	// Score

	private static CitizenType laborer = new() { IsDefaultCitizen = true };

	private static void SetContentCitizens(City city, int count) {
		city.residents.Clear();
		for (int i = 0; i < count; ++i) {
			city.residents.Add(new CityResident { city = city, citizenType = laborer, mood = CityResident.Mood.Content });
		}
	}

	[Fact]
	public void ScoreIsTheRoundedAverageOfTurnScores() {
		Player player = MakePlayer();
		City city = MakeCity(player);
		List<HistTurnRecord> history = new();
		gameData.history[player.HistoryKey] = history;

		// One turn scoring 1, then ten scoring 2: the average is 21/11.
		// Rounding a running average each turn got stuck at 1.
		SetContentCitizens(city, 1);
		player.UpdateHistory(gameData);
		SetContentCitizens(city, 2);
		for (int i = 0; i < 10; ++i) {
			player.UpdateHistory(gameData);
		}

		Assert.Equal(11, history.Count);
		Assert.Equal(21, history[^1].TurnScoreSum);
		Assert.Equal(2, history[^1].Score);

		// Falling scores pull the average down as they should.
		SetContentCitizens(city, 0);
		for (int i = 0; i < 11; ++i) {
			player.UpdateHistory(gameData);
		}
		Assert.Equal(21, history[^1].TurnScoreSum);
		Assert.Equal(1, history[^1].Score); // 21/22 rounds to 1
	}

	[Fact]
	public void ScoreFromAnOlderSaveCarriesOn() {
		Player player = MakePlayer();
		City city = MakeCity(player);
		// Records from before the sum was kept.
		List<HistTurnRecord> history = [new() { Score = 10 }, new() { Score = 10 }];
		gameData.history[player.HistoryKey] = history;

		SetContentCitizens(city, 4);
		player.UpdateHistory(gameData);

		Assert.Equal(24, history[^1].TurnScoreSum);
		Assert.Equal(8, history[^1].Score);
	}

	// War weariness

	[Fact]
	public void AnAIAttackGivesItsVictimWarHappinessAgainstIt() {
		Player human = MakePlayer("Humans");
		Player ai = MakePlayer("Robots");
		ai.isHuman = false;
		human.EnsureRelationshipExists(ai);

		ai.DeclareWarOn(human, 1);
		Assert.Equal(-30, human.WarWearinessPointsAgainst(ai));
		Assert.Equal(0, ai.WarWearinessPointsAgainst(human));
		Assert.Equal(-1, Player.WarWearinessLevel(human.WarWearinessPointsAgainst(ai)));
	}

	// Per the project owner, any civ war is declared on gains war
	// happiness against the aggressor, whoever declared it.
	[Fact]
	public void AHumanDeclaringWarGivesItsVictimWarHappinessAgainstIt() {
		Player a = MakePlayer("A");
		Player b = MakePlayer("B");
		a.EnsureRelationshipExists(b);

		a.DeclareWarOn(b, 1);
		Assert.Equal(0, a.WarWearinessPointsAgainst(b));
		Assert.Equal(-30, b.WarWearinessPointsAgainst(a));
		Assert.Equal(-1, Player.WarWearinessLevel(b.WarWearinessPointsAgainst(a)));
	}

	// The war's weariness wears the war happiness away, and then turns into
	// ordinary war weariness.
	[Fact]
	public void WarHappinessTurnsIntoWarWearinessAsTheWarGoesOn() {
		Player a = MakePlayer("A");
		Player b = MakePlayer("B");
		a.EnsureRelationshipExists(b);
		a.DeclareWarOn(b, 1);

		Player.AddWarWearinessForLostCity(b, a, size: 5);
		Assert.Equal(-13, b.WarWearinessPointsAgainst(a));
		Assert.Equal(-1, Player.WarWearinessLevel(b.WarWearinessPointsAgainst(a)));

		Player.AddWarWearinessForLostCity(b, a, size: 5);
		Assert.Equal(4, b.WarWearinessPointsAgainst(a));
		Assert.Equal(0, Player.WarWearinessLevel(b.WarWearinessPointsAgainst(a)));

		Player.AddWarWearinessForLostCity(b, a, size: 5);
		Player.AddWarWearinessForLostCity(b, a, size: 5);
		Assert.Equal(38, b.WarWearinessPointsAgainst(a));
		Assert.Equal(1, Player.WarWearinessLevel(b.WarWearinessPointsAgainst(a)));
	}

	[Fact]
	public void AHumanProvokedByANuclearAtrocityGivesNoWarHappiness() {
		Player a = MakePlayer("A");
		Player b = MakePlayer("B");
		a.EnsureRelationshipExists(b);
		a.playerRelationships[b.id].nuclearAtrocityCount = 1;

		a.DeclareWarOn(b, 1);
		Assert.Equal(0, b.WarWearinessPointsAgainst(a));
	}

	[Fact]
	public void AnAIProvokedBySpiesGivesNoWarHappiness() {
		Player human = MakePlayer("Humans");
		Player ai = MakePlayer("Robots");
		ai.isHuman = false;
		human.EnsureRelationshipExists(ai);
		ai.playerRelationships[human.id].espionageIncidents = 1;

		ai.DeclareWarOn(human, 1);
		Assert.Equal(0, human.WarWearinessPointsAgainst(ai));
	}

	// Per the project owner, a human whose refusal of an AI's demand to leave
	// its territory directly caused the war provoked it (see
	// TerritoryDemands), but only then.
	[Fact]
	public void AnAIProvokedByTrespassersGivesNoWarHappiness() {
		Player human = MakePlayer("Humans");
		Player ai = MakePlayer("Robots");
		ai.isHuman = false;
		human.EnsureRelationshipExists(ai);

		ai.DeclareWarOn(human, 1, provoked: true);
		Assert.Equal(0, human.WarWearinessPointsAgainst(ai));
		Assert.Equal(0, ai.WarWearinessPointsAgainst(human));
	}

	[Fact]
	public void AnUnprovokedWarOnTrespassersGivesWarHappiness() {
		Player human = MakePlayer("Humans");
		Player ai = MakePlayer("Robots");
		ai.isHuman = false;
		human.EnsureRelationshipExists(ai);

		ai.DeclareWarOn(human, 1, provoked: false);
		Assert.Equal(Player.WarWearinessWhenWarIsDeclaredOnUs, human.WarWearinessPointsAgainst(ai));
	}

	// City names

	[Fact]
	public void CivsWithoutCityNamesGetNumberedOnes() {
		Player player = MakePlayer("Nameless");
		player.civilization.adjective = "Nameless";
		player.civilization.cityNames = new();
		gameData.cities.Add(MakeCity(player, "Nameless 1"));

		Assert.Equal("Nameless 2", player.GetNextCityName());

		player.civilization.cityNames = null;
		Assert.Equal("Nameless 2", player.GetNextCityName());
	}

	// Wonders in production

	[Fact]
	public void AWonderStaysAmongTheOptionsOfTheCityBuildingIt() {
		Player player = MakePlayer();
		City city = MakeCity(player);
		City other = MakeCity(player, "Otherville");
		Building pyramids = MakeBuilding("Pyramids", greatWonder: true);

		city.SetItemBeingProduced(pyramids);
		Assert.True(pyramids.CanProduce(city, new()));
		Assert.True(pyramids.CanProduce(city, new(), new Building.ProductionContext(city)));
		Assert.False(pyramids.CanProduce(other, new()));
		Assert.False(pyramids.CanProduce(other, new(), new Building.ProductionContext(other)));
	}

	// Granted buildings

	[Fact]
	public void TwoWondersGrantingTheSameBuildingGrantItOnce() {
		Player player = MakePlayer();
		City city = MakeCity(player);
		Building granary = MakeBuilding("Granary");
		Building first = MakeBuilding("First", greatWonder: true);
		Building second = MakeBuilding("Second", greatWonder: true);
		first.greatWonderProperties.buildingGainedInEveryCity = granary;
		second.greatWonderProperties.buildingGainedInEveryCity = granary;
		city.AddBuilding(first);
		city.AddBuilding(second);

		Assert.Equal(1, city.GetBuildings().Count(cb => cb.building == granary));
	}

	// Governments

	[Fact]
	public void SettingAGovernmentFlagAgainDoesNotStackItsModifier() {
		Government government = new();
		government.hasTradeBonus = true;
		government.hasTradeBonus = true;
		government.hasTilePenalty = true;
		government.hasTilePenalty = true;

		Tile.Yield commerce = new(Tile.NONE, 5, Tile.YieldType.Commerce);
		government.tileModifier(commerce);
		Assert.Equal(1, commerce.bonus);
		Assert.Equal(1, commerce.penalty);

		government.hasTradeBonus = false;
		government.hasTilePenalty = false;
		Assert.Null(government.tileModifier);
	}

	[Fact]
	public void DespotismPenaltyDoesNotCancelBuildingTileBonuses() {
		Player player = MakePlayer();
		player.government.hasTilePenalty = true;
		City city = MakeCity(player);
		Building colossus = MakeBuilding("Colossus", greatWonder: true);
		colossus.tileModifier = yield => {
			if (yield.type == Tile.YieldType.Commerce && yield.baseYield > 0) {
				yield.bonus += 1;
			}
		};
		city.AddBuilding(colossus);

		// A tile's 2 commerce isn't penalized, and the Colossus bonus on top
		// of it isn't either.
		Tile tile = new(ID.None("tile"));
		Tile.Yield commerce = Tile.Yield.CalculateForCity(tile, 2, Tile.YieldType.Commerce, city, city.EffectiveBuildings());
		Assert.Equal(3, commerce.yield);

		// Tiles already over 2 before the bonus are still penalized.
		commerce = Tile.Yield.CalculateForCity(tile, 3, Tile.YieldType.Commerce, city, city.EffectiveBuildings());
		Assert.Equal(3, commerce.yield);

		// Tiles without commerce get nothing.
		commerce = Tile.Yield.CalculateForCity(tile, 0, Tile.YieldType.Commerce, city, city.EffectiveBuildings());
		Assert.Equal(0, commerce.yield);
	}

	// Time

	[Fact]
	public void TimePassesOneUnitPerTurnAfterTheLastSegment() {
		TimeOptions options = new() { startYear = -4000 };
		options.timeScale = new int[,] { { 2, 3 }, { 10, 5 } };

		Assert.Equal(-4000, options.GetRawNumber(0));
		Assert.Equal(-3980, options.GetRawNumber(2));
		Assert.Equal(-3965, options.GetRawNumber(5));
		Assert.Equal(-3964, options.GetRawNumber(6));
		Assert.Equal(-3960, options.GetRawNumber(10));

		Assert.Equal(5, options.GetTurnFromRaw(-3965));
		Assert.Equal(10, options.GetTurnFromRaw(-3960));

		// The standard time scale runs out too, eventually.
		TimeOptions standard = new();
		int end = 25 + 25 + 40 + 50 + 100 + 100 + 100 + 50000;
		Assert.Equal(standard.GetRawNumber(end) + 7, standard.GetRawNumber(end + 7));
	}

	// Citizens

	[Fact]
	public void RemovingARandomCitizenFromAnEmptyCityDoesNothing() {
		Player player = MakePlayer();
		City city = MakeCity(player);
		city.RemoveRandomCitizen();
		Assert.Empty(city.residents);
	}

	// Research queue

	// Each queued tech's prerequisites are known or earlier in the queue.
	internal static void AssertPrerequisitesFirst(Player player) {
		HashSet<ID> done = new(player.knownTechs);
		foreach (Tech tech in player.ResearchQueue) {
			foreach (Tech prereq in tech.Prerequisites) {
				Assert.True(done.Contains(prereq.id), $"{tech.id} is queued before its prerequisite {prereq.id}");
			}
			done.Add(tech.id);
		}
	}

	// A needs B and C, and C needs B: B, then C, then A (the queue used to
	// come out C, B, A, putting C before its prerequisite).
	[Fact]
	public void ResearchQueuePutsPrerequisitesFirst() {
		Player player = MakePlayer();
		Tech b = new() { id = ID.FromString("tech-12"), Cost = 1 };
		Tech c = new() { id = ID.FromString("tech-13"), Cost = 2, Prerequisites = { b } };
		Tech a = new() { id = ID.FromString("tech-11"), Cost = 3, Prerequisites = { b, c } };
		gameData.techs.AddRange([a, b, c]);

		player.CalculateFreshTechQueueAndAssignNewCurrent(a);

		Assert.Equal([b, c, a], player.ResearchQueue.ToList());
		Assert.Equal(b.id, player.currentlyResearchedTech);
	}

	[Fact]
	public void ResearchQueueOfADiamondPutsPrerequisitesFirst() {
		Player player = MakePlayer();
		Tech a = new() { id = ID.FromString("tech-1"), Cost = 1 };
		Tech b = new() { id = ID.FromString("tech-2"), Cost = 3, Prerequisites = { a } };
		Tech c = new() { id = ID.FromString("tech-3"), Cost = 2, Prerequisites = { a } };
		Tech d = new() { id = ID.FromString("tech-4"), Cost = 4, Prerequisites = { b, c } };
		Tech e = new() { id = ID.FromString("tech-5"), Cost = 5, Prerequisites = { d, a, c } };
		gameData.techs.AddRange([a, b, c, d, e]);

		foreach (Tech target in gameData.techs) {
			player.CalculateFreshTechQueueAndAssignNewCurrent(target);
			AssertPrerequisitesFirst(player);
			Assert.Equal(target, player.ResearchQueue.Last());
		}
		player.CalculateFreshTechQueueAndAssignNewCurrent(e);
		Assert.Equal([a, c, b, d, e], player.ResearchQueue.ToList());
	}

	[Fact]
	public void ResearchQueueOfManyStackedDiamondsIsQuick() {
		Player player = MakePlayer();
		Tech bottom = new() { id = ID.FromString("tech-0"), Cost = 0 };
		gameData.techs.Add(bottom);
		Tech top = bottom;
		for (int i = 1; i <= 40; ++i) {
			Tech left = new() { id = ID.FromString($"tech-{3 * i}"), Cost = 3 * i, Prerequisites = { top } };
			Tech right = new() { id = ID.FromString($"tech-{3 * i + 1}"), Cost = 3 * i + 1, Prerequisites = { top } };
			top = new Tech { id = ID.FromString($"tech-{3 * i + 2}"), Cost = 3 * i + 2, Prerequisites = { left, right } };
			gameData.techs.AddRange([left, right, top]);
		}

		// 2^40 paths lead from the top to the bottom.
		// Walking them would never finish; a linear walk takes milliseconds.
		// The bound is generous so a loaded CI machine doesn't fail it.
		Stopwatch watch = Stopwatch.StartNew();
		player.CalculateFreshTechQueueAndAssignNewCurrent(top);
		Assert.True(watch.ElapsedMilliseconds < 30000, $"Took {watch.ElapsedMilliseconds} ms");

		Assert.Equal(gameData.techs.Count, player.ResearchQueue.Count);
		Assert.Equal(bottom, player.ResearchQueue.Peek());
		Assert.Equal(top, player.ResearchQueue.Last());
	}

	// Tech lookups

	[Fact]
	public void TechLookupsSeeEntriesReplacedInPlace() {
		List<Tech> techs = new();
		for (int i = 0; i < 5; ++i) {
			techs.Add(new Tech { id = ID.FromString($"tech-{i}") });
		}
		Assert.Null(Tech.FindById(techs, ID.FromString("tech-9")));
		Assert.Null(Tech.FindBridgeTech(techs));

		// Same list, same size, different contents.
		techs[2] = new Tech { id = ID.FromString("tech-9"), EnablesBridges = true };

		Assert.Same(techs[2], Tech.FindById(techs, ID.FromString("tech-9")));
		Assert.Null(Tech.FindById(techs, ID.FromString("tech-2")));
		Assert.Same(techs[2], Tech.FindBridgeTech(techs));
	}

	// Building caches

	[Fact]
	public void BuildingSnapshotSeesCitiesSwappedWithoutTheCountChanging() {
		Player player = MakePlayer();
		City plain = MakeCity(player);
		Player other = MakePlayer("Other");
		City wonderCity = MakeCity(other);
		wonderCity.AddBuilding(MakeBuilding("Colossus", greatWonder: true));
		Assert.Empty(player.GetActiveWonders());

		// Swapped in place, without touching any owner.
		player.cities[0] = wonderCity;
		Assert.Single(player.GetActiveWonders());

		player.cities[0] = plain;
		Assert.Empty(player.GetActiveWonders());
	}

	[Fact]
	public void BuildingSnapshotSeesKnownTechsSwapped() {
		Player player = MakePlayer();
		City city = MakeCity(player);
		Building pyramids = MakeBuilding("Pyramids", greatWonder: true);
		pyramids.renderedObsoleteBy = new Tech { id = ID.FromString("tech-7") };
		city.AddBuilding(pyramids);
		player.knownTechs.Add(ID.FromString("tech-1"));
		Assert.Single(player.GetActiveWonders());

		player.knownTechs.Remove(ID.FromString("tech-1"));
		player.knownTechs.Add(ID.FromString("tech-7"));
		Assert.Empty(player.GetActiveWonders());
	}

	[Fact]
	public void BuildingsInOtherPlayersCitiesDoNotInvalidateOurSnapshot() {
		Player us = MakePlayer("Us");
		Player them = MakePlayer("Them");
		MakeCity(us);
		City theirCity = MakeCity(them);
		Player.BuildingSnapshot before = us.GetBuildingSnapshot();

		theirCity.AddBuilding(MakeBuilding("Temple"));

		Assert.Same(before, us.GetBuildingSnapshot());
	}

	[Fact]
	public void ReplacingABuildingListUpdatesTheOwnersSnapshot() {
		Player player = MakePlayer();
		City city = MakeCity(player);
		City other = MakeCity(player, "Otherville");
		Building granary = MakeBuilding("Granary");
		Building pyramids = MakeBuilding("Pyramids", greatWonder: true);
		pyramids.greatWonderProperties.buildingGainedInEveryCity = granary;
		city.AddBuilding(pyramids);
		Assert.True(other.HasEffectiveBuilding(granary));

		city.constructed_buildings = new();

		Assert.Empty(player.GetActiveWonders());
		Assert.False(other.HasEffectiveBuilding(granary));
	}

	[Fact]
	public void ChangingACitysOwnerUpdatesBothSnapshots() {
		Player us = MakePlayer("Us");
		Player them = MakePlayer("Them");
		City city = MakeCity(us);
		city.AddBuilding(MakeBuilding("Colossus", greatWonder: true));
		Assert.Single(us.GetActiveWonders());
		Assert.Empty(them.GetActiveWonders());

		us.cities.Remove(city);
		them.cities.Add(city);
		city.owner = them;

		Assert.Empty(us.GetActiveWonders());
		Assert.Single(them.GetActiveWonders());
	}
}

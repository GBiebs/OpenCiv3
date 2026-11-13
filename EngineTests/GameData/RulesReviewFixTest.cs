using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3 rules fixed after a review: hurrying, wonder costs, empire size
// unhappiness, resistance, corruption, research and so on.
public class RulesReviewFixTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public RulesReviewFixTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && !p.isHuman).Take(2).ToArray();
		us = civs[0];
		them = civs[1];
	}

	private UnitPrototype Prototype(string name) {
		return gameData.unitPrototypes.Single(p => p.name == name);
	}

	private Building BuildingNamed(string name) {
		return gameData.Buildings.Single(b => b.name == name);
	}

	private static bool IsEmptyLand(Tile t) {
		return t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.IsImpassable();
	}

	private City BuildCity(Player owner) {
		Tile tile = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities()
			&& t.neighbors.Values.All(n => n == Tile.NONE || !n.HasCity()));
		return CityInteractions.BuildCity(tile, owner, $"City {owner.cities.Count}");
	}

	// ---- Hurrying ----

	[Fact]
	public void WondersCannotBeHurried() {
		City city = BuildCity(us);
		us.gold = 100000;
		us.government.hurryingType = Government.HurryProductionType.PaidLabor;

		city.SetItemBeingProduced(BuildingNamed("The Pyramids"));
		city.SetStoredShields(10);
		Assert.NotNull(city.GetHurryProductionDetails().errorMessage);

		city.SetItemBeingProduced(BuildingNamed("Heroic Epic"));
		Assert.NotNull(city.GetHurryProductionDetails().errorMessage);

		us.government.hurryingType = Government.HurryProductionType.ForcedLabor;
		Assert.NotNull(city.GetHurryProductionDetails().errorMessage);
	}

	// Civ3 charges the BIQ's shield value in gold (4) for every shield still
	// needed, units and improvements alike, and double from an empty box.
	[Fact]
	public void BuyingAnImprovementCostsTheShieldValueInGoldAShield() {
		City city = BuildCity(us);
		us.gold = 100000;
		us.government.hurryingType = Government.HurryProductionType.PaidLabor;
		Building temple = BuildingNamed("Temple");
		city.SetItemBeingProduced(temple);
		city.SetStoredShields(10);

		int remaining = us.ShieldCost(temple) - 10;
		Assert.Equal(4, gameData.rules.ShieldValueInGold);
		Assert.Equal(4 * remaining, city.GetHurryProductionDetails().goldCost);

		// Nothing built yet: double.
		city.SetStoredShields(0);
		Assert.Equal(8 * us.ShieldCost(temple), city.GetHurryProductionDetails().goldCost);
	}

	[Fact]
	public void BuyingAUnitCostsTheSameAShieldAsAnImprovement() {
		City city = BuildCity(us);
		us.gold = 100000;
		us.government.hurryingType = Government.HurryProductionType.PaidLabor;
		UnitPrototype settler = Prototype("Settler");
		city.SetItemBeingProduced(settler);
		city.SetStoredShields(1);

		int s = us.ShieldCost(settler) - 1;
		Assert.Equal(gameData.rules.ShieldValueInGold * s, city.GetHurryProductionDetails().goldCost);
	}

	// Industrious civs' city tiles get their extra shield only from size 13
	// (community tests for PTW/C3C; 98.7% of the shields of 24k cities in
	// Civ3 saves).
	[Fact]
	public void IndustriousCitiesGetAShieldFromSizeThirteen() {
		City city = BuildCity(us);
		// No Despotism penalty to take the extra shield off again.
		us.government = gameData.governments.First(g => g.name == "Monarchy");
		HashSet<Civilization.Trait> traits = us.civilization.traits;
		bool had = traits.Contains(Civilization.Trait.Industrious);
		int Difference() {
			traits.Remove(Civilization.Trait.Industrious);
			int plain = city.location.ProductionYield(city).yield;
			traits.Add(Civilization.Trait.Industrious);
			return city.location.ProductionYield(city).yield - plain;
		}
		try {
			while (city.residents.Count < gameData.rules.MaximumLevel1CitySize + 1) {
				AddResident(city, us.civilization);
			}
			Assert.Equal(0, Difference());
			while (city.residents.Count < gameData.rules.MaximumLevel2CitySize + 1) {
				AddResident(city, us.civilization);
			}
			Assert.Equal(1, Difference());
		} finally {
			if (!had) {
				traits.Remove(Civilization.Trait.Industrious);
			}
		}
	}

	// Each citizen sacrificed produces the BIQ's shields per citizen, so a
	// pop rush takes enough citizens to finish the item: rounded up (the
	// owner chose Civ3's own text, 2026-10-20).
	[Fact]
	public void PopRushingRoundsUpToFinishTheItem() {
		Assert.Equal(1, City.PopRushCost(20, 20));
		Assert.Equal(2, City.PopRushCost(21, 20));
		Assert.Equal(2, City.PopRushCost(40, 20));
		Assert.Equal(3, City.PopRushCost(41, 20));
		// Never free.
		Assert.Equal(1, City.PopRushCost(5, 20));

		City city = BuildCity(us);
		for (int i = 0; i < 7; ++i) {
			AddResident(city, us.civilization);
		}
		us.government.hurryingType = Government.HurryProductionType.ForcedLabor;
		Building temple = BuildingNamed("Temple");
		city.SetItemBeingProduced(temple);
		int perCitizen = gameData.rules.CitizenValueInShields;
		// Just over one citizen's worth of shields still needed.
		city.SetStoredShields(us.ShieldCost(temple) - (perCitizen + 1));
		Assert.Equal(2, city.GetHurryProductionDetails().popCost);
		// Exactly one.
		city.SetStoredShields(us.ShieldCost(temple) - perCitizen);
		Assert.Equal(1, city.GetHurryProductionDetails().popCost);
	}

	// ---- Gold ----

	[Fact]
	public void OverspendingEmptiesTheTreasuryInsteadOfThrowing() {
		us.gold = 10;
		us.gold -= 25;
		Assert.Equal(0, us.gold);

		us.SetGold(10);
		us.SetGold(-30, add: true);
		Assert.Equal(0, us.gold);
	}

	// ---- Research ----

	[Fact]
	public void UnfundedTurnsDontCountTowardsTheMaximumResearchTime() {
		Tech tech = us.GetAvailableTechsToResearch(gameData.techs).First();
		us.freeTechsRemaining = 0;
		us.SetCurrentlyResearchedTech(tech.id);
		us.taxRate = 10;
		us.scienceRate = 0;
		us.luxuryRate = 0;

		for (int i = 0; i < 3; ++i) {
			us.DoPerTurnFinanceUpdates(gameData);
			us.DoPerTurnScienceUpdates(gameData);
		}
		Assert.Equal(0, us.turnsResearched);
		Assert.Equal(tech.id, us.currentlyResearchedTech);
	}

	// "A tech is gained on the turn *after* progress >= cost" (Perez's
	// decompile, Round 2b "Research"; owner's ruling 2026-11-12): the turn
	// that pays for it doesn't learn it, the next one does, before its own
	// beakers are added.
	[Fact]
	public void ATechIsLearnedTheTurnAfterItIsPaidFor() {
		Tech tech = us.GetAvailableTechsToResearch(gameData.techs).First();
		us.freeTechsRemaining = 0;
		us.SetCurrentlyResearchedTech(tech.id);
		us.turnsResearched = gameData.rules.MinimumResearchTime;
		us.beakers = 0;
		int cost = gameData.TechCostFor(tech, us);
		Assert.True(gameData.rules.MaximumResearchTime > us.turnsResearched + 1);

		us.beakers = cost - 1;
		us.CompletePaidResearch(gameData);
		Assert.DoesNotContain(tech.id, us.knownTechs);

		// This turn's beakers pay for it: it isn't learned yet.
		us.beakers = cost;
		us.DoPerTurnScienceUpdates(gameData);
		Assert.DoesNotContain(tech.id, us.knownTechs);
		Assert.Equal(1, us.EstimateTurnsToResearch(gameData, tech));

		// The next turn learns it.
		us.CompletePaidResearch(gameData);
		Assert.Contains(tech.id, us.knownTechs);
		Assert.NotEqual(tech.id, us.currentlyResearchedTech);
	}

	// A human asked what to research next keeps the beakers of the turn
	// that taught them the last tech.
	[Fact]
	public void BeakersGatheredWhileChoosingGoToTheTechChosen() {
		Tech tech = us.GetAvailableTechsToResearch(gameData.techs).First();
		us.freeTechsRemaining = 0;
		us.SetCurrentlyResearchedTech(null);
		us.beakers = 7;
		us.turnsResearched = 1;

		us.SetCurrentlyResearchedTech(tech.id);
		Assert.Equal(7, us.beakers);
		Assert.Equal(1, us.turnsResearched);
	}

	// ---- Specialists ----

	// Turns the city's only citizen into the given specialist.
	private static void MakeSpecialist(City city, CitizenType type) {
		CityResident resident = city.residents[0];
		resident.tileWorked.personWorkingTile = null;
		resident.tileWorked = Tile.NONE;
		resident.citizenType = type;
	}

	[Fact]
	public void SpecialistsProduceNothingInDisorder() {
		City city = BuildCity(us);
		MakeSpecialist(city, gameData.citizenTypes.First(c => c.Research > 0));
		Assert.True(city.CurrentCommerceYieldRaw().beakers > 0);

		city.isInCivilDisorder = true;
		CommerceBreakdown commerce = city.CurrentCommerceYieldRaw();
		Assert.Equal(0, commerce.beakers);
		Assert.Equal(0, commerce.taxes);
	}

	[Fact]
	public void SpecialistsStillWorkUnderAnarchy() {
		City city = BuildCity(us);
		CitizenType scientist = gameData.citizenTypes.First(c => c.Research > 0);
		MakeSpecialist(city, scientist);

		// The city's commerce is lost, but not the scientist's beakers.
		Government government = us.government;
		try {
			us.government = gameData.governments.First(g => g.transitionType);
			Assert.Equal(scientist.Research, city.CurrentCommerceYieldRaw().beakers);
		} finally {
			us.government = government;
		}
	}

	[Fact]
	public void PolicemenWinBackWaste() {
		City city = BuildCity(us);
		CitizenType entertainer = gameData.citizenTypes.First(c => c.Luxuries > 0);
		CitizenType policeman = gameData.citizenTypes.First(c => c.Corruption > 0);
		MakeSpecialist(city, entertainer);
		city.corruption = 0.9f;
		CorruptableValue withoutPolice = city.CurrentProductionYield();

		city.residents[0].citizenType = policeman;
		CorruptableValue withPolice = city.CurrentProductionYield();
		Assert.Equal(System.Math.Max(0, withoutPolice.corrupt - policeman.Corruption), withPolice.corrupt);
	}

	[Fact]
	public void CivilEngineersOnlyHelpBuildings() {
		City city = BuildCity(us);
		CitizenType entertainer = gameData.citizenTypes.First(c => c.Luxuries > 0);
		CitizenType engineer = new() { SingularName = "Civil Engineer", Construction = 2 };
		MakeSpecialist(city, entertainer);
		city.corruption = 0;
		city.SetItemBeingProduced(BuildingNamed("Temple"));
		int without = city.CurrentProductionYield().useful;

		city.residents[0].citizenType = engineer;
		Assert.Equal(without + 2, city.CurrentProductionYield().useful);

		city.SetItemBeingProduced(Prototype("Warrior"));
		Assert.Equal(without, city.CurrentProductionYield().useful);
	}

	[Fact]
	public void TheSecretPoliceOnlyWorkUnderTheirGovernment() {
		Building forbiddenPalace = BuildingNamed("Forbidden Palace");
		Assert.True(forbiddenPalace.WorksAsForbiddenPalaceFor(us));

		Government other = gameData.governments.First(g => g.id != us.government.id);
		Government? required = forbiddenPalace.requiredGovernment;
		try {
			forbiddenPalace.requiredGovernment = other;
			Assert.False(forbiddenPalace.WorksAsForbiddenPalaceFor(us));
			forbiddenPalace.requiredGovernment = us.government;
			Assert.True(forbiddenPalace.WorksAsForbiddenPalaceFor(us));
		} finally {
			forbiddenPalace.requiredGovernment = required;
		}
	}

	// ---- Resistance ----

	// A city of `them`'s with four of their citizens, taken by us.
	private City CapturedCity() {
		// A second city keeps them in the game.
		BuildCity(them);
		City city = BuildCity(them);
		for (int i = 1; i < 4; ++i) {
			city.AddCitizen(new CityResident() {
				city = city,
				nationality = them.civilization,
				citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen),
			});
		}
		// Taken in a war, which goes on: peace ends resistance.
		us.EnsureRelationshipExists(them);
		PlayerRelationship.DeclareWar(us, them, false, gameData.turn + 10);
		CityInteractions.CaptureCity(city, us);
		// Who resists is random; make it everyone.
		city.resisters = city.residents.Count;
		city.resistanceFrom = them;
		return city;
	}

	[Fact]
	public void ConqueredCitiesResist() {
		City city = CapturedCity();
		Assert.True(city.IsInResistance);
		Assert.Equal(them, city.resistanceFrom);

		// Resisters work no tile and eat nothing.
		Assert.Equal(0, city.FoodConsumedPerTurn());
		Assert.Empty(city.WorkingResidents());
		int resisting = city.residents.Count;
		city.resisters = 0;
		Assert.Equal(2 * resisting, city.FoodConsumedPerTurn());
		Assert.Equal(resisting, city.WorkingResidents().Count());
	}

	// A captured city starts growing again from an empty food box (owner's
	// ruling, 2026-11-08).
	[Fact]
	public void ACapturedCitysFoodBoxEmpties() {
		BuildCity(them);
		City city = BuildCity(them);
		city.AddCitizen(new CityResident() { city = city, nationality = them.civilization, citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen) });
		city.foodStored = 15;
		us.EnsureRelationshipExists(them);
		PlayerRelationship.DeclareWar(us, them, false, gameData.turn + 10);
		CityInteractions.CaptureCity(city, us);
		Assert.Equal(us, city.owner);
		Assert.Equal(0, city.foodStored);
	}

	// Resisters "refuse to work": they're on no tile, can't be made
	// specialists, and aren't moved onto a tile; once they stop
	// resisting, they go back to work.
	[Fact]
	public void ResistersWorkNoTileAndAreNoSpecialists() {
		City city = CapturedCity();
		CityResident resister = city.residents[^1];
		CitizenType entertainer = us.GetKnownSpecialists(gameData)[0];
		// As a city taken before resisters were idled would have them.
		Tile tile = city.GetWorkableTiles().First(t => t != city.location && t.personWorkingTile == null);
		resister.tileWorked = tile;
		tile.personWorkingTile = resister;
		city.residents[0].citizenType = entertainer;
		city.RecalculateCitizenMoods(gameData);
		Assert.All(city.residents, r => Assert.Equal(Tile.NONE, r.tileWorked));
		Assert.All(city.residents, r => Assert.True(r.citizenType.IsDefaultCitizen));
		Assert.Null(tile.personWorkingTile);

		CityInteractions.CycleSpecialist(gameData, city, city.residents.Count - 1);
		CityInteractions.ReassignCitizen(gameData, city, tile);
		CityInteractions.ReassignCitizen(gameData, city, city.location);
		Assert.All(city.residents, r => Assert.Equal(Tile.NONE, r.tileWorked));
		Assert.All(city.residents, r => Assert.True(r.citizenType.IsDefaultCitizen));

		// Peace ends the resistance, and they go to work.
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		city.UpdateResistance(gameData);
		Assert.All(city.residents, r => Assert.True(r.tileWorked != Tile.NONE || !r.citizenType.IsDefaultCitizen));
	}

	// Resisters are neither happy nor unhappy, and their nation's war
	// doesn't make anyone else unhappy on their account.
	[Fact]
	public void ResistersAreNeitherHappyNorUnhappy() {
		City city = CapturedCity();
		city.RecalculateCitizenMoods(gameData);
		Assert.All(city.residents, r => Assert.Equal(CityResident.Mood.Resisting, r.mood));
		Assert.Equal(0, city.MotherlandUnhappiness(gameData));
		Assert.False(city.isInCivilDisorder);

		city.resisters = 1;
		city.RecalculateCitizenMoods(gameData);
		Assert.Equal(CityResident.Mood.Resisting, city.residents[^1].mood);
		Assert.DoesNotContain(city.residents.Take(city.residents.Count - 1), r => r.mood == CityResident.Mood.Resisting);
		Assert.Equal((city.residents.Count - 1 + 1) / 2, city.MotherlandUnhappiness(gameData));
	}

	// The free palace jump goes to the city with the most points: 3 per
	// national citizen, 1 per foreign one, and our cities around it
	// (DaveMcW's "Free Palace Jump" article).
	[Fact]
	public void TheFreePalaceGoesToTheCityWithTheMostPoints() {
		City small = BuildCity(us);
		City big = BuildCity(us);
		for (int i = 0; i < 4; ++i) {
			AddResident(big, us.civilization);
		}
		City foreign = BuildCity(us);
		for (int i = 0; i < 6; ++i) {
			AddResident(foreign, them.civilization);
		}
		us.cities.RemoveAll(c => c != small && c != big && c != foreign);
		foreach (City c in us.cities) {
			c.capital = false;
		}
		City chosen = us.RelocatePalace(gameData, small.location);
		Assert.Equal(big, chosen);
	}

	// "Determines the percentage chance of rioting occurring in any city
	// experiencing disorder" (C3C editor help, Chance of Rioting). Wonders,
	// small wonders, the palace and aqueduct/sewer-class buildings are
	// never destroyed, and the capital never riots (Civ3 binary,
	// doc/perez-findings/Civ3_Findings.md, Round 2b).
	[Fact]
	public void RiotsDestroyAnImprovementButNotWondersOrGrowthBuildings() {
		City city = BuildCity(us);
		city.capital = false;
		city.AddBuilding(BuildingNamed("Temple"));
		city.AddBuilding(BuildingNamed("The Pyramids"));
		city.AddBuilding(BuildingNamed("Aqueduct"));
		int saved = gameData.rules.ChanceOfRioting;
		try {
			gameData.rules.ChanceOfRioting = 0;
			city.MaybeRiot(gameData);
			Assert.Contains(city.constructed_buildings, cb => cb.building.name == "Temple");
			gameData.rules.ChanceOfRioting = 100;
			city.capital = true;
			for (int i = 0; i < 20; ++i) {
				city.MaybeRiot(gameData);
			}
			Assert.Contains(city.constructed_buildings, cb => cb.building.name == "Temple");
			city.capital = false;
			for (int i = 0; i < 20; ++i) {
				city.MaybeRiot(gameData);
			}
			Assert.DoesNotContain(city.constructed_buildings, cb => cb.building.name == "Temple");
			Assert.Contains(city.constructed_buildings, cb => cb.building.name == "The Pyramids");
			Assert.Contains(city.constructed_buildings, cb => cb.building.name == "Aqueduct");
		} finally {
			gameData.rules.ChanceOfRioting = saved;
		}
	}

	// A town needs a second roll, at twice the chance, as well as the first.
	[Fact]
	public void ATownRiotsOnlyIfItPassesASecondRoll() {
		City city = BuildCity(us);
		city.capital = false;
		city.AddBuilding(BuildingNamed("Temple"));
		Assert.True(city.residents.Count <= gameData.rules.MaximumLevel1CitySize);
		int saved = gameData.rules.ChanceOfRioting;
		System.Random original = C7GameData.GameData.rng;
		try {
			gameData.rules.ChanceOfRioting = 20;
			// 19 passes the first roll, 39 the second; 40 fails it.
			C7GameData.GameData.rng = new SequenceRandom(19, 40);
			city.MaybeRiot(gameData);
			Assert.Contains(city.constructed_buildings, cb => cb.building.name == "Temple");
			C7GameData.GameData.rng = new SequenceRandom(19, 39, 0);
			city.MaybeRiot(gameData);
			Assert.DoesNotContain(city.constructed_buildings, cb => cb.building.name == "Temple");
		} finally {
			C7GameData.GameData.rng = original;
			gameData.rules.ChanceOfRioting = saved;
		}
	}

	private class SequenceRandom(params int[] values) : System.Random {
		private int next;
		public override int Next(int maxValue) => System.Math.Min(values[next++ % values.Length], maxValue - 1);
	}

	// Fascism's Forced Resettlement: "each town in the nation loses 1
	// population, each city loses 2 population, and each metropolis loses 3
	// population" (C3C editor help, Flags).
	[Fact]
	public void FascismResettlesCitizens() {
		Government fascism = gameData.governments.First(g => g.name == "Fascism");
		Assert.True(fascism.forcedResettlement && fascism.xenophobic);
		City town = BuildCity(us);
		AddResident(town, us.civilization);
		AddResident(town, us.civilization);
		City city = BuildCity(us);
		while (city.residents.Count < gameData.rules.MaximumLevel1CitySize + 2) {
			AddResident(city, us.civilization);
		}
		int townSize = town.residents.Count, citySize = city.residents.Count;
		us.AdoptGovernment(fascism);
		Assert.Equal(townSize - 1, town.residents.Count);
		Assert.Equal(citySize - 2, city.residents.Count);
	}

	// Fascism is Xenophobic: "no culture is gained in cities where less than
	// half of the population of the city is from the same race as the
	// controlling civilization" (C3C editor help, Flags).
	[Fact]
	public void FascistCitiesOfForeignersMakeNoCulture() {
		City city = BuildCity(us);
		city.AddBuilding(BuildingNamed("Temple"));
		us.government = gameData.governments.First(g => g.name == "Fascism");
		Assert.True(city.GetCulturePerTurn() > 0);
		foreach (CityResident r in city.residents) {
			r.nationality = them.civilization;
		}
		AddResident(city, them.civilization);
		AddResident(city, us.civilization);
		Assert.Equal(0, city.GetCulturePerTurn());
		// Half our own is enough.
		AddResident(city, us.civilization);
		Assert.True(city.GetCulturePerTurn() > 0);
	}

	// So its borders don't grow either: "you don't get any cultural
	// expansion from a captured city until your Civ's citizens outnumber
	// the 'foreigners'" (tjs282,
	// https://forums.civfanatics.com/threads/problem-culture-border-of-city-doesnt-expand.537508/ #2).
	[Fact]
	public void FascistCitiesOfForeignersDontExpandTheirBorders() {
		City city = BuildCity(us);
		city.AddBuilding(BuildingNamed("Temple"));
		us.government = gameData.governments.First(g => g.name == "Fascism");
		foreach (CityResident r in city.residents) {
			r.nationality = them.civilization;
		}
		// One culture short of the next border level.
		city.perPlayerCulture[us] = 9;
		int level = city.GetBorderExpansionLevel();
		Assert.False(city.UpdateCultureAndCheckForExpansion());
		Assert.Equal(9, city.GetCulture());
		Assert.Equal(level, city.GetBorderExpansionLevel());

		// With our own people the majority, it grows.
		AddResident(city, us.civilization);
		AddResident(city, us.civilization);
		Assert.True(city.UpdateCultureAndCheckForExpansion());
	}

	// Per the project owner, foreign citizens never become ours, but a
	// starving city loses them first.
	[Fact]
	public void StarvationTakesForeignersFirst() {
		City city = BuildCity(us);
		AddResident(city, them.civilization);
		AddResident(city, us.civilization);
		int foreigners = city.residents.Count(r => r.nationality == them.civilization);
		int ours = city.residents.Count(r => r.nationality == us.civilization);
		city.RemoveStarvingCitizen();
		Assert.Equal(foreigners - 1, city.residents.Count(r => r.nationality == them.civilization));
		Assert.Equal(ours, city.residents.Count(r => r.nationality == us.civilization));
	}

	// "If it gets to one turn left you can switch to other
	// units/improvements, but NOT wonders" (Travathian, Apolyton).
	[Fact]
	public void ABuildFinishingNextTurnCantBecomeAWonder() {
		City city = BuildCity(us);
		Building temple = BuildingNamed("Temple");
		city.SetItemBeingProduced(temple);
		city.SetStoredShields(us.ShieldCost(temple) - 1);
		Assert.Equal(1, city.TurnsUntilProductionFinished());
		Assert.NotNull(city.WhyCannotChangeProduction(BuildingNamed("The Pyramids")));
		Assert.Null(city.WhyCannotChangeProduction(BuildingNamed("Barracks")));

		city.SetStoredShields(0);
		Assert.Null(city.WhyCannotChangeProduction(BuildingNamed("The Pyramids")));
	}

	// With disband shields in the box "that particular city won't be able
	// to switch to a wonder" (https://forums.civfanatics.com/threads/disbanding-question.18365/).
	[Fact]
	public async System.Threading.Tasks.Task DisbandShieldsKeepTheCityOffWonders() {
		City city = BuildCity(us);
		city.SetItemBeingProduced(BuildingNamed("Temple"));
		city.SetStoredShields(0);
		MapUnit warrior = gameData.SpawnUnit(us, gameData.unitPrototypes.First(u => u.name == "Warrior"), city.location);
		await gameData.DisbandUnit(warrior);
		Assert.True(city.shieldsStored > 0);
		Assert.NotNull(city.WhyCannotChangeProduction(BuildingNamed("The Pyramids")));
	}

	// "PC = (C * 600) / OCN", between 300 and 1000 shields
	// (https://civfanatics.com/civ3/strategy/game-mechanics/cost-of-the-palace-for-c3c/).
	[Fact]
	public void ThePalaceCostsMoreForABiggerEmpire() {
		us.isHuman = true;
		Building palace = gameData.Buildings.First(b => b.isCenterOfEmpire);
		gameData.map.optimalNumberOfCities = 20;
		City sample = BuildCity(us);
		us.cities.RemoveAll(c => c != sample);
		Assert.Equal(300, us.ShieldCost(palace));
		while (us.cities.Count < 17) {
			us.cities.Add(sample);
		}
		Assert.Equal(500, us.ShieldCost(palace));
		while (us.cities.Count < 40) {
			us.cities.Add(sample);
		}
		Assert.Equal(1000, us.ShieldCost(palace));
	}

	// "The bonuses from a Golden Age, however, are still subject to the
	// Despotism tile penalty" (https://civfanatics.com/civ3/faq/).
	[Fact]
	public void TheDespotismPenaltyAppliesInAGoldenAge() {
		us.government = gameData.governments.First(g => g.name == "Despotism");
		us.goldenAgeTurnsRemaining = 10;
		Tile tile = gameData.map.tiles.First(t => t.IsLand());
		Assert.Equal(2, Tile.Yield.CalculateForPlayer(tile, 2, Tile.YieldType.Production, us).yield);
		Assert.Equal(2, Tile.Yield.CalculateForPlayer(tile, 1, Tile.YieldType.Production, us).yield);
	}

	// On capture "all possible buildings with a culture value > 0 are
	// still destroyed" (C3C editor help, Retain Culture on Capture).
	[Fact]
	public void CapturedCitiesLoseTheirCultureBuildings() {
		BuildCity(them);
		City city = BuildCity(them);
		city.AddBuilding(gameData.Buildings.First(b => b.name == "Temple"));
		city.AddCitizen(new CityResident() { city = city, nationality = them.civilization, citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen) });
		CityInteractions.CaptureCity(city, us);
		Assert.DoesNotContain(city.constructed_buildings, cb => cb.building.name == "Temple");
	}

	// "Resisters can be quelled by ending the war" (Civilopedia,
	// GCON_Resistance).
	[Fact]
	public void PeaceEndsResistance() {
		City city = CapturedCity();
		PlayerRelationship.SignPeaceAfterWar(us, them, gameData);
		city.UpdateResistance(gameData);
		Assert.False(city.IsInResistance);
	}

	// "Resistors, content people, and Specialists are ignored in the
	// calculation" of civil disorder (Civ3 manual).
	[Fact]
	public void ResistersDontRiot() {
		City city = CapturedCity();
		Assert.Equal(City.Mood.Happy, city.RecalculateCitizenMoods(gameData));
		Assert.All(city.residents.Skip(city.residents.Count - city.resisters), r => Assert.Equal(CityResident.Mood.Resisting, r.mood));
		Assert.False(city.QualifiesForCelebration(gameData.rules));
	}

	[Fact]
	public void AnUngarrisonedCityGoesOnResisting() {
		City city = CapturedCity();
		int resisters = city.resisters;
		foreach (MapUnit u in city.location.unitsOnTile.ToList()) {
			u.RemoveFromPlay();
		}
		for (int i = 0; i < 10; ++i) {
			city.UpdateResistance(gameData);
		}
		Assert.Equal(resisters, city.resisters);
	}

	// conquests.biq's Resistance Modifier covers Feudalism and Fascism too:
	// a Fascist captor of a Communist city faces 10% less resistance.
	[Fact]
	public void FascismCutsResistanceFromCommunists() {
		City city = CapturedCity();
		Government original = us.government;
		Government theirs = them.government;
		try {
			us.government = them.government;
			int same = city.ResistancePercent(them, continuing: true);
			us.government = gameData.governments.First(g => g.name == "Fascism");
			them.government = gameData.governments.First(g => g.name == "Communism");
			Assert.Equal(same - 10, city.ResistancePercent(them, continuing: true));
		} finally {
			us.government = original;
			them.government = theirs;
		}
	}

	[Fact]
	public void ResistanceFollowsTheCultureComparison() {
		City city = CapturedCity();
		// Without any culture of our own, they are disdainful of us: 90%,
		// less 10% for carrying on resisting (both despotisms here).
		foreach (City c in us.cities) {
			c.perPlayerCulture[us] = 0;
		}
		Assert.Equal(us.government, them.government);
		Assert.Equal(90, city.ResistancePercent(them, continuing: false));
		Assert.Equal(80, city.ResistancePercent(them, continuing: true));

		// With three times their culture we hold them in awe: 40%.
		int theirs = them.cities.Sum(c => c.GetCulture());
		us.cities[0].perPlayerCulture[us] = 3 * System.Math.Max(1, theirs);
		Assert.Equal(40, city.ResistancePercent(them, continuing: false));
	}

	[Fact]
	public void ResistanceSurvivesASave() {
		City city = CapturedCity();
		int resisters = city.resisters;

		C7GameData.GameData loaded = SaveGame.FromGameData(gameData).ToGameData(fixture.behaviors);
		City loadedCity = loaded.cities.Single(c => c.id == city.id);
		Assert.Equal(resisters, loadedCity.resisters);
		Assert.Equal(them.id, loadedCity.resistanceFrom.id);
	}

	// Per the project owner, resistance goes on after the old owner is
	// destroyed.
	[Fact]
	public void ResistanceGoesOnAfterTheOldOwnerIsGone() {
		City city = CapturedCity();
		int resisters = city.resisters;
		them.defeated = true;
		foreach (MapUnit u in city.location.unitsOnTile.ToList()) {
			u.RemoveFromPlay();
		}
		city.UpdateResistance(gameData);
		Assert.Equal(resisters, city.resisters);
		Assert.Equal(them, city.resistanceFrom);
	}

	// Makes every random roll come out as 0, so every chance comes true.
	private class ZeroRandom : System.Random {
		protected override double Sample() => 0.0;
	}

	private void AddResident(City city, Civilization nationality) {
		city.AddCitizen(new CityResident() {
			city = city,
			nationality = nationality,
			citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen),
		});
	}

	// Per the project owner, every citizen not of the captor's nationality
	// may resist, not only the old owner's.
	[Fact]
	public void EveryForeignCitizenMayResist() {
		Player third = gameData.players.First(p => !p.isBarbarians && p != us && p != them);
		BuildCity(them);
		City city = BuildCity(them);
		AddResident(city, third.civilization);
		AddResident(city, us.civilization);
		AddResident(city, us.civilization);
		AddResident(city, them.civilization);
		// Capturing takes the last citizen.
		AddResident(city, them.civilization);
		CityInteractions.CaptureCity(city, us);
		Assert.Equal(5, city.residents.Count);

		gameData.random = new ZeroRandom();
		city.StartResistance(them);
		// Two of theirs and one of the third civ's; ours don't.
		Assert.Equal(3, city.resisters);
		Assert.All(city.WorkingResidents(), r => Assert.Equal(us.civilization, r.nationality));
		Assert.All(city.residents.Skip(2), r => Assert.NotEqual(us.civilization, r.nationality));

		// A new citizen doesn't resist.
		AddResident(city, us.civilization);
		Assert.Equal(3, city.resisters);
		Assert.Equal(3, city.WorkingResidents().Count());
		Assert.All(city.WorkingResidents(), r => Assert.Equal(us.civilization, r.nationality));
	}

	// Per the project owner, a civ's last city doesn't resist when taken.
	[Fact]
	public void ALastCityDoesNotResist() {
		foreach (City c in them.cities.ToList()) {
			CityInteractions.DestroyCity(c);
		}
		City city = BuildCity(them);
		AddResident(city, them.civilization);
		AddResident(city, them.civilization);
		AddResident(city, them.civilization);
		CityInteractions.CaptureCity(city, us);
		Assert.Empty(them.cities);

		gameData.random = new ZeroRandom();
		city.StartResistance(them);
		Assert.False(city.IsInResistance);
	}

	// ---- We Love the King Day ----

	// In Conquests a celebration lowers waste only: "For waste calculations
	// only, when the city is in a WLTKD celebration, divide da by 2" and
	// "add OCN/4 to Nopt"
	// (https://civfanatics.com/civ3/strategy/game-mechanics/everything-about-corruption-c3c-edition/).
	[Fact]
	public void CelebrationsHalveTheDistanceAndRaiseTheOptimalCityNumberForWasteOnly() {
		gameData.rules.CoreCitiesFreeOfCorruption = false;
		BuildCity(us);
		City city = BuildCity(us);
		int adjusted = us.GetAdjustedOptimalCityNumber(gameData);
		// Some rank corruption, but not up to the cap.
		city.rankIndex = adjusted;
		city.CalculateCorruption(gameData, adjusted);
		Assert.True(city.corruption > 0);

		float distance = city.CalculateDistanceCorruption(gameData, 0);
		Assert.Equal(distance / 2, city.CalculateDistanceCorruption(gameData, 1), 5);
		// One "courthouse" raises Nopt by OCN/4.
		float rank = city.CalculateRankCorruption(adjusted, gameData.map.optimalNumberOfCities, 1);
		float expected = System.Math.Clamp((distance / 2 + rank) * gameData.rules.CorruptionRate, 0, 0.9f);
		Assert.Equal(expected, city.celebrationWaste, 5);
		Assert.True(city.celebrationWaste < city.corruption);

		// Commerce is corrupted the same either way.
		CommerceBreakdown before = city.CurrentCommerceYieldRaw();
		city.celebrating = true;
		Assert.Equal(before.corrupted, city.CurrentCommerceYieldRaw().corrupted);
	}

	// ---- Difficulty ----

	// "n_com = floor(b/4) if L has the trait else 0", added to the map's
	// number before the difficulty's percentage (C3X's notes on the stock
	// OCN formula): Regent's 90% of 17 + 4 is 18, not 90% of 17 * 1.25.
	[Fact]
	public void TheCommercialTraitAddsAQuarterOfTheMapsCitiesRoundedDown() {
		gameData.map.optimalNumberOfCities = 17;
		us.government = new Government { name = "Test", corruptionType = Government.CorruptionType.Rampant };
		us.civilization.traits.Remove(Civilization.Trait.Commercial);
		Assert.Equal(15, us.GetAdjustedOptimalCityNumber(gameData));
		us.civilization.traits.Add(Civilization.Trait.Commercial);
		Assert.Equal(18, us.GetAdjustedOptimalCityNumber(gameData));
	}

	// The executable's notes add more optimal cities for the AI on the
	// harder levels ("n_ai"), but Civ3 saves fit far better without them:
	// the AI gets Regent's percentage at every level, nothing more.
	[Fact]
	public void TheAIGetsRegentsOptimalCitiesOnEveryLevel() {
		gameData.map.optimalNumberOfCities = 20;
		us.government = new Government { name = "Test", corruptionType = Government.CorruptionType.Rampant };
		us.civilization.traits.Remove(Civilization.Trait.Commercial);
		Player human = gameData.players.First(p => p.isHuman);
		human.government = us.government;
		human.civilization.traits.Remove(Civilization.Trait.Commercial);
		gameData.gameDifficulty = gameData.difficulties[4];
		// A human at Emperor: 80% of 20.
		Assert.Equal(16, human.GetAdjustedOptimalCityNumber(gameData));
		foreach (int level in new[] { 2, 3, 4, 6 }) {
			gameData.gameDifficulty = gameData.difficulties[level];
			Assert.Equal(18, us.GetAdjustedOptimalCityNumber(gameData));
		}
	}

	// The executable's government term: floor(b/8) for minimal or nuisance
	// corruption, floor(b/16) for problematic (C3X's notes on the stock OCN
	// formula; owner's decision, 2026-10-20).
	[Fact]
	public void TheGovernmentAddsAnEighthOrASixteenth() {
		gameData.map.optimalNumberOfCities = 20;
		us.civilization.traits.Remove(Civilization.Trait.Commercial);
		gameData.gameDifficulty = gameData.difficulties[2];
		us.government = new Government { name = "Test", corruptionType = Government.CorruptionType.Nuisance };
		// Regent's 90% of (20 + 2).
		Assert.Equal(19, us.GetAdjustedOptimalCityNumber(gameData));
		us.government = new Government { name = "Test", corruptionType = Government.CorruptionType.Problematic };
		// 90% of (20 + 1).
		Assert.Equal(18, us.GetAdjustedOptimalCityNumber(gameData));
	}

	// The AI plays by its default level's per-player fields, Regent's,
	// whatever the game's; a human by the game's.
	[Fact]
	public void TheAIPlaysByRegentsDifficultyFields() {
		Player human = gameData.players.First(p => p.isHuman);
		gameData.gameDifficulty = gameData.difficulties[6];
		Assert.Same(gameData.difficulties[6], gameData.DifficultyFor(human));
		Assert.Equal("Regent", gameData.DifficultyFor(us).Name);
	}

	[Fact]
	public void HumansGetTheDifficultyBonusAgainstBarbarians() {
		Player human = gameData.players.First(p => p.isHuman);
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		// On flat land: not hills or mountains, which defend better.
		bool flat(Tile t) => IsEmptyLand(t) && t.overlayTerrainType.defenseBonus.amount < 0.5;
		Tile tile = gameData.map.tiles.First(flat);
		MapUnit ours = gameData.SpawnUnit(human, Prototype("Warrior"), tile);
		MapUnit theirs = gameData.SpawnUnit(barbarians, Prototype("Warrior"), gameData.map.tiles.Where(flat).Skip(1).First());
		MapUnit aiUnit = gameData.SpawnUnit(us, Prototype("Warrior"), tile);

		gameData.gameDifficulty.AttackBonusAgainstBarbarians = 100;
		Assert.Contains(ours.ListStrengthBonusesVersus(theirs, CombatRole.Attack, null), b => b.amount == 1.0);
		// The AI gets its own 200%, whatever the difficulty.
		Assert.DoesNotContain(aiUnit.ListStrengthBonusesVersus(theirs, CombatRole.Attack, null), b => b.amount == 1.0);
		// It counts when defending too, but not in bombardment.
		Assert.Contains(ours.ListStrengthBonusesVersus(theirs, CombatRole.Defense, null), b => b.amount == 1.0);
		Assert.DoesNotContain(ours.ListStrengthBonusesVersus(theirs, CombatRole.BombardDefense, null), b => b.amount == 1.0);
	}

	[Fact]
	public void TheAIGetsTwoHundredPercentAgainstBarbariansAtAnyDifficulty() {
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		Tile tile = gameData.map.tiles.First(IsEmptyLand);
		MapUnit aiUnit = gameData.SpawnUnit(us, Prototype("Warrior"), tile);
		MapUnit theirs = gameData.SpawnUnit(barbarians, Prototype("Warrior"), gameData.map.tiles.Where(IsEmptyLand).Skip(1).First());

		foreach (int humanBonus in new[] { 0, 800 }) {
			gameData.gameDifficulty.AttackBonusAgainstBarbarians = humanBonus;
			Assert.Contains(aiUnit.ListStrengthBonusesVersus(theirs, CombatRole.Attack, null), b => b.amount == 2.0);
			Assert.Contains(aiUnit.ListStrengthBonusesVersus(theirs, CombatRole.Defense, null), b => b.amount == 2.0);
		}
		// Only against barbarians, and the barbarians get nothing.
		Assert.DoesNotContain(theirs.ListStrengthBonusesVersus(aiUnit, CombatRole.Attack, null), b => b.amount == 2.0);
	}

	// ---- Workers ----

	[Fact]
	public void WorkersWorkAtTheGovernmentRateTimesTheirStrength() {
		us.civilization.traits.Remove(Civilization.Trait.Industrious);
		Tile tile = gameData.map.tiles.First(IsEmptyLand);
		MapUnit worker = gameData.SpawnUnit(us, Prototype("Worker"), tile);

		us.government.workerRate = 3;
		Assert.Equal(3f, worker.workerSpeed());

		worker.unitType.workerStrength = 2;
		try {
			Assert.Equal(6f, worker.workerSpeed());
		} finally {
			worker.unitType.workerStrength = 1;
		}

		// A tech like Replaceable Parts doubles it.
		Tech tech = gameData.techs.First(t => !us.knownTechs.Contains(t.id));
		tech.DoublesWorkerRate = true;
		us.knownTechs.Add(tech.id);
		Assert.Equal(6f, worker.workerSpeed());
	}

	// ---- Anarchy ----

	[Fact]
	public void AnarchyFollowsTheConquestsFormula() {
		gameData.gameDifficulty.MaxAiGovernmentTransitionTime = 0;
		us.civilization.traits.Remove(Civilization.Trait.Religious);
		for (int i = 0; i < 50; ++i) {
			int turns = us.GetTurnsOfAnarchyForTransition(gameData);
			Assert.InRange(turns, 2, 9);
		}

		us.civilization.traits.Add(Civilization.Trait.Religious);
		Assert.Equal(2, us.GetTurnsOfAnarchyForTransition(gameData));
	}

	// ---- Barbarians ----

	// Rolls the given number for every Next(max), and 0 otherwise.
	private class FixedRandom(int roll) : System.Random {
		public override int Next(int maxValue) => System.Math.Min(roll, maxValue - 1);
		protected override double Sample() => 0.0;
	}

	private City CityToSack(int size) {
		City city = BuildCity(us);
		for (int i = 1; i < size; ++i) {
			AddResident(city, us.civilization);
		}
		return city;
	}

	// Per the project owner: gold 40, citizens 30, production 30, out of
	// what's possible. The gold is the treasury divided by the number of
	// the owner's cities (CivWiki; owner's decision, 2026-10-20).
	[Fact]
	public void BarbariansStealTheTreasuryDividedByTheNumberOfCities() {
		City city = CityToSack(size: 4);
		while (us.cities.Count < 4) {
			BuildCity(us);
		}
		Assert.Equal(4, us.cities.Count);
		us.gold = 7;
		Assert.Equal(1, City.BarbarianRaidGold(us));
		us.gold = 3;
		Assert.Equal(0, City.BarbarianRaidGold(us));
		city.SetStoredShields(10);
		us.gold = 502;
		gameData.random = new FixedRandom(39);
		Assert.Equal(new City.BarbarianSack(City.BarbarianSackOutcome.Gold, 125), city.SackedByBarbarians());
		Assert.Equal(377, us.gold);
		Assert.Equal(4, city.residents.Count);
		Assert.Equal(10, city.shieldsStored);
	}

	[Fact]
	public void BarbariansKillACitizen() {
		City city = CityToSack(size: 4);
		city.SetStoredShields(10);
		us.gold = 500;
		gameData.random = new FixedRandom(40);
		Assert.Equal(City.BarbarianSackOutcome.Citizens, city.SackedByBarbarians().outcome);
		Assert.Equal(500, us.gold);
		Assert.Equal(3, city.residents.Count);
		Assert.Equal(10, city.shieldsStored);
	}

	[Fact]
	public void BarbariansDestroyStoredProduction() {
		City city = CityToSack(size: 4);
		city.SetStoredShields(10);
		us.gold = 500;
		gameData.random = new FixedRandom(70);
		Assert.Equal(new City.BarbarianSack(City.BarbarianSackOutcome.Production, 10), city.SackedByBarbarians());
		Assert.Equal(500, us.gold);
		Assert.Equal(4, city.residents.Count);
		Assert.Equal(0, city.shieldsStored);
	}

	// What isn't possible isn't rolled for: no gold in the treasury, a
	// size 1 city, and no shields stored.
	[Fact]
	public void BarbariansOnlyDoWhatTheyCan() {
		City city = CityToSack(size: 1);
		us.gold = 0;
		city.SetStoredShields(0);
		Assert.Equal(City.BarbarianSackOutcome.Nothing, city.SackedByBarbarians().outcome);
		Assert.Equal(0, us.gold);
		Assert.Single(city.residents);

		// Only production is possible, whatever the roll.
		city.SetStoredShields(5);
		gameData.random = new FixedRandom(0);
		Assert.Equal(City.BarbarianSackOutcome.Production, city.SackedByBarbarians().outcome);

		// Only a citizen.
		AddResident(city, us.civilization);
		Assert.Equal(City.BarbarianSackOutcome.Citizens, city.SackedByBarbarians().outcome);
		Assert.Single(city.residents);
	}

	// ---- Captives ----

	[Fact]
	public void CaptivesAreKnownByNationNotName() {
		Tile tile = gameData.map.tiles.First(IsEmptyLand);
		MapUnit worker = gameData.SpawnUnit(us, Prototype("Worker"), tile);
		Assert.False(worker.IsCaptive());

		worker.nationality = them.civilization;
		Assert.True(worker.IsCaptive());

		// A different nation with the same name is still foreign.
		worker.nationality = new Civilization(us.civilization.name);
		Assert.True(worker.IsCaptive());
	}

	// ---- Trait discounts ----

	[Fact]
	public void TraitDiscountSkipsWonders() {
		HashSet<Civilization.Trait> religious = [Civilization.Trait.Religious];
		Building temple = BuildingNamed("Temple");
		Building oracle = BuildingNamed("The Oracle");
		Building forbiddenPalace = BuildingNamed("Forbidden Palace");
		Assert.Equal((int)(temple.shieldCost * gameData.rules.BuildingDiscountForCivTraits), temple.ShieldCost(religious, 1));
		Assert.Equal(oracle.shieldCost, oracle.ShieldCost(religious, 1));
		Assert.Equal(forbiddenPalace.shieldCost, forbiddenPalace.ShieldCost(religious, 1));
	}
}

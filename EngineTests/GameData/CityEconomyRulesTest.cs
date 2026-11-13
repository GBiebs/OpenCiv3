using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// City economy rules checked against doc/civ3-compendium/02-city-economy.md.
public class CityEconomyRulesTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public CityEconomyRulesTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians && !p.isHuman);
	}

	private static bool IsEmptyLand(Tile t) {
		return t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.IsImpassable();
	}

	private TerrainType Terrain(string key) {
		return gameData.terrainTypes.First(t => t.Key == key);
	}

	private static void SetTerrain(Tile tile, TerrainType terrain) {
		tile.baseTerrainType = terrain;
		tile.overlayTerrainType = terrain;
		tile.isBonusShield = false;
		tile.Resource = Resource.NONE;
	}

	// A size 1 city on plain grassland, with no river, whose citizen works
	// the given neighbouring terrain.
	private City TownOnGrassland(TerrainType worked) {
		Tile site = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities() && !t.BordersRiver()
			&& t.neighbors.Values.All(n => n != Tile.NONE && IsEmptyLand(n) && !n.BordersRiver()));
		SetTerrain(site, Terrain("grassland"));
		Tile workedTile = site.neighbors.Values.First();
		SetTerrain(workedTile, worked);
		City city = CityInteractions.BuildCity(site, us, "Test Town");
		// The capital, so it loses nothing to corruption or waste.
		Assert.True(city.IsCapital());
		city.residents[0].tileWorked = workedTile;
		return city;
	}

	// A town's centre gets its minimum shield only if the terrain and the
	// resource together make none: "+Shields from bonus resources" comes
	// before "+1 if shields==0 so far" (DaveMcW,
	// https://codehappy.net/apolyton/threads/78438-1.htm).
	[Fact]
	public void TownCentreMinimumShieldIsCheckedAfterTheResource() {
		City city = TownOnGrassland(Terrain("grassland"));
		Tile centre = city.location;
		Assert.Equal(1, centre.ProductionYield(city).yield);

		Resource shieldResource = gameData.Resources.First(r => r.ShieldsBonus == 1 && r.Prerequisite == null);
		centre.Resource = shieldResource;
		Assert.Equal(1, centre.ProductionYield(city).yield);
	}

	// Wealth turns 4 shields into a gold in C3C, and 2 once Economics is
	// known (Conquests Civilopedia, BLDG_Wealth: "a ratio of four to one",
	// "reduced to two to one"), whole gold only, at least 1.
	[Fact]
	public void WealthPaysAGoldForEachFullShieldRatio() {
		us.government = gameData.governments.First(g => g.name == "Monarchy");
		City city = TownOnGrassland(Terrain("forest"));
		Inflow wealth = gameData.Inflows.First(i => i.name == "Wealth");
		city.SetItemBeingProduced(wealth);
		us.DoCorruptionCalculations(gameData);

		Assert.Equal(3, city.CurrentProductionYield().useful);
		Assert.Equal(4, gameData.rules.ShieldCostPerGold);
		Assert.Equal(1, city.CurrentCommerceYield().wealth);

		Tech economics = gameData.techs.First(t => t.DoublesWealthProduction);
		us.knownTechs.Add(economics.id);
		// 3 shields at 2 a gold is 1 gold, not 1.5 rounded up.
		Assert.Equal(1, city.CurrentCommerceYield().wealth);
	}

	// Cities as far from the capital as each other rank in founding order
	// (alexman, CFC 76619 #79: "founding date is a tie-breaker for cities at
	// the same distance"), whatever order the player's own list is in.
	[Fact]
	public void CorruptionRankTiesGoToTheOlderCity() {
		Tile capitalSite = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities()
			&& t.GetTilesWithinRankDistance(6).Count(n => n != Tile.NONE && IsEmptyLand(n) && n.RankDistanceTo(t) == 5) >= 2);
		Tile[] ring = capitalSite.GetTilesWithinRankDistance(6)
			.Where(n => n != Tile.NONE && IsEmptyLand(n) && n.IsAllowCities() && n.RankDistanceTo(capitalSite) == 5)
			.ToArray();
		Tile first = ring[0];
		Tile second = ring.First(n => n.RankDistanceTo(first) >= 2);

		City capital = CityInteractions.BuildCity(capitalSite, us, "Capital");
		Assert.True(capital.IsCapital());
		City older = CityInteractions.BuildCity(first, us, "Older");
		City newer = CityInteractions.BuildCity(second, us, "Newer");

		// As after a capture, the newer city comes first in the player's list.
		us.cities.Remove(older);
		us.cities.Add(older);
		Assert.True(us.cities.IndexOf(newer) < us.cities.IndexOf(older));

		us.DoCorruptionCalculations(gameData);
		Assert.Equal(older.rankIndex + 1, newer.rankIndex);
		Assert.True(capital.rankIndex < older.rankIndex);
	}

	// "Players are not supposed to pay maintenance for obsolete city
	// improvements" (C3X's notes on the 1.22 executable,
	// https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini);
	// the stock game's failure to recompute it is a bug we don't copy.
	[Fact]
	public void ObsoleteImprovementsCostNoUpkeep() {
		// Without the AI's free upkeep house rule (see AiHouseRules).
		us.isHuman = true;
		City city = TownOnGrassland(Terrain("grassland"));
		Building temple = gameData.Buildings.First(b => b.name == "Temple");
		Tech obsoleting = gameData.techs.First(t => !us.knownTechs.Contains(t.id));
		temple.renderedObsoleteBy = obsoleting;
		city.AddBuilding(temple);
		int upkeep = city.MaintenanceCostsRaw();
		Assert.True(temple.maintenanceCost > 0);

		us.knownTechs.Add(obsoleting.id);

		Assert.Equal(upkeep - temple.maintenanceCost, city.MaintenanceCostsRaw());
	}

	// Any obsolete building, not only a great wonder, loses its effects but
	// keeps making culture (see Building.IsObsoleteFor): a Temple whose
	// obsoleting tech is known no longer makes anyone content.
	[Fact]
	public void ObsoleteImprovementsStopWorkingButKeepTheirCulture() {
		City city = TownOnGrassland(Terrain("grassland"));
		Building temple = gameData.Buildings.First(b => b.name == "Temple");
		Assert.True(temple.contentFacesInCity > 0 && temple.culturePerTurn > 0);
		Tech obsoleting = gameData.techs.First(t => !us.knownTechs.Contains(t.id));
		temple.renderedObsoleteBy = obsoleting;
		city.AddBuilding(temple);
		int faces = city.BuildingContentFaces(city.EffectiveBuildings());
		int culture = city.GetCulturePerTurnBySource().improvements;
		Assert.True(faces >= temple.contentFacesInCity);
		Assert.True(temple.TakesEffectFor(us));

		us.knownTechs.Add(obsoleting.id);

		Assert.False(temple.TakesEffectFor(us));
		Assert.DoesNotContain(city.EffectiveBuildings(), cb => cb.building == temple);
		Assert.Equal(faces - temple.contentFacesInCity, city.BuildingContentFaces(city.EffectiveBuildings()));
		Assert.Equal(culture, city.GetCulturePerTurnBySource().improvements);
		// The city still has it, so it isn't offered again, and nowhere can
		// build it any more.
		Assert.True(city.HasEffectiveBuilding(temple));
		Assert.False(temple.CanProduce(city, new()));
	}

	// A building a wonder grants doesn't pollute: C3X offers, as a change
	// to the base game, making "improvements granted for free by great or
	// small wonders" pollute "as though they had been built in the city"
	// (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
	[Fact]
	public void BuildingsGrantedByWondersDontPollute() {
		City city = TownOnGrassland(Terrain("grassland"));
		Building factory = gameData.Buildings.First(b => b.name == "Factory");
		Assert.True(factory.pollution > 0);
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder()
			&& b.greatWonderProperties.buildingGainedInEveryCityOnContinent == null);
		wonder.greatWonderProperties.buildingGainedInEveryCity = factory;
		city.AddBuilding(wonder);
		Assert.True(city.HasEffectiveBuilding(factory));

		Assert.Equal(0, city.BuildingPollution());
		Assert.Equal(0, city.PollutionIcons(gameData.rules));
	}

	// ---- Civ3 binary (doc/perez-findings/Civ3_Findings.md), owner's ruling 2026-11-12 ----

	// Luxuries and science are each rounded halves up; if they come to more
	// than there is, science keeps its share and luxuries take the
	// shortfall, and taxes get the rest (Economy, City_RecalcTrade).
	[Theory]
	[InlineData(5, 5, 5, 2, 3, 0)]
	[InlineData(1, 3, 3, 0, 0, 1)]
	[InlineData(8, 0, 7, 0, 6, 2)]
	[InlineData(3, 5, 5, 1, 2, 0)]
	public void TradeSplitGivesScienceItsRoundedShare(int commerce, int luxuryRate, int scienceRate, int luxuries, int beakers, int taxes) {
		City city = TownOnGrassland(Terrain("grassland"));
		us.luxuryRate = luxuryRate;
		us.scienceRate = scienceRate;
		us.taxRate = 10 - luxuryRate - scienceRate;
		Assert.Equal((luxuries, beakers, taxes), city.SplitCommerce(commerce));
	}

	// Corruption rounds the amount lost halves up, then caps it at 90% less
	// 10% for each courthouse-class building, rounded down (Economy,
	// City_CalcCorruption).
	[Fact]
	public void CorruptionRoundsTheLossHalfUpAndCapsIt() {
		// 25 at the 90% cap: 22 lost, 3 kept (not 2).
		CorruptableValue capped = new(25, .95f, 9);
		Assert.Equal(3, capped.useful);
		Assert.Equal(22, capped.corrupt);
		// 10 at 25%: 2.5 lost rounds up to 3.
		CorruptableValue half = new(10, .25f, 9);
		Assert.Equal(3, half.corrupt);
		Assert.Equal(7, half.useful);
		// A courthouse lowers the cap to 80%.
		Assert.Equal(16, new CorruptableValue(20, .9f, 8).corrupt);
		// The cap leaves a city that makes anything at least one.
		Assert.Equal(1, new CorruptableValue(1, .9f, 9).useful);
		Assert.Equal(1, new CorruptableValue(5, .9f, 9).useful);
		Assert.Equal(0, new CorruptableValue(0, .9f, 9).useful);
	}

	// Pop rushing rounds the citizens up for the shields still needed, then
	// doubles them from an empty box (3.4 Hurry): 30 shields at 20 a citizen
	// is 4 citizens, not the 3 that doubling the shields first would give.
	[Fact]
	public void PopRushDoublesTheCitizensFromAnEmptyBox() {
		us.government = gameData.governments.First(g => g.hurryingType == Government.HurryProductionType.ForcedLabor);
		City city = TownOnGrassland(Terrain("grassland"));
		UnitPrototype unit = gameData.unitPrototypes.First(p => p.name == "Warrior");
		city.SetItemBeingProduced(unit);
		int cost = us.ShieldCost(unit);
		int saved = gameData.rules.CitizenValueInShields;
		try {
			// A citizen worth two thirds of the cost, as 20 is of 30.
			int value = (2 * cost + 2) / 3;
			gameData.rules.CitizenValueInShields = value;
			int expected = 2 * ((cost + value - 1) / value);
			Assert.NotEqual(expected, (2 * cost + value - 1) / value);
			Assert.Equal(expected, city.HurryPopCost());

			// With shields in the box, nothing is doubled.
			city.SetStoredShields(1);
			Assert.Equal((cost - 1 + value - 1) / value, city.HurryPopCost());
		} finally {
			gameData.rules.CitizenValueInShields = saved;
		}
	}

	// A city in disorder neither stores food nor starves: its citizens eat
	// what it makes (Food, growth, starvation).
	[Fact]
	public void ACityInDisorderNeitherGrowsNorStarves() {
		City city = TownOnGrassland(Terrain("mountains"));
		Assert.Single(city.residents);
		int saved = us.rules.CityCenterFood;
		try {
			us.rules.CityCenterFood = 0;
			Assert.True(city.FoodGrowthPerTurn() < 0);
			city.foodStored = 0;
			city.isInCivilDisorder = true;
			Assert.False(city.HandleCityGrowth(gameData));
			Assert.Single(city.residents);
			Assert.Equal(0, city.foodStored);
		} finally {
			us.rules.CityCenterFood = saved;
		}
	}

	// A size 1 city that runs out of food is destroyed (Food, growth,
	// starvation), and its owner is told "Starvation at ...!" (Civ3's
	// Script.txt, STARVE).
	[Fact]
	public void ASizeOneCityStarvesToNothing() {
		City other = TownOnGrassland(Terrain("grassland"));
		Tile site = gameData.map.tiles.First(t => IsEmptyLand(t) && t.IsAllowCities()
			&& t.neighbors.Values.All(n => n != Tile.NONE && IsEmptyLand(n)) && t.RankDistanceTo(other.location) > 4);
		City city = CityInteractions.BuildCity(site, us, "Starving Town");
		int saved = us.rules.CityCenterFood;
		try {
			us.rules.CityCenterFood = 0;
			foreach (Tile t in site.neighbors.Values) {
				SetTerrain(t, Terrain("mountains"));
			}
			city.residents[0].tileWorked = site.neighbors.Values.First();
			city.foodStored = 0;
			Assert.True(city.FoodGrowthPerTurn() < 0);
			us.HandleCityUpdates(gameData);
		} finally {
			us.rules.CityCenterFood = saved;
		}
		Assert.DoesNotContain(city, us.cities);
		Assert.DoesNotContain(city, gameData.cities);
		Assert.Null(site.cityAtTile);
	}

	// A unit never costs less than a shield, whatever the factors (Round
	// 2b, Costs).
	[Fact]
	public void AUnitCostsAtLeastAShield() {
		UnitPrototype unit = gameData.unitPrototypes.First(p => p.name == "Warrior");
		Assert.Equal(1, unit.ShieldCost(new(), 0.01f));
	}
}

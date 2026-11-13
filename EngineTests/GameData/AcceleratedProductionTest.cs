using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

public class AcceleratedProductionTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly City city;

	public AcceleratedProductionTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		Player player = gameData.players.First(p => !p.isBarbarians && p.units.Any(u => u.unitType.isSettler));
		Tile tile = player.units.First(u => u.unitType.isSettler).location;
		city = CityInteractions.BuildCity(tile, player, player.GetNextCityName());
		player.DoCorruptionCalculations(gameData);
	}

	// Civ3's Accelerated Production "simply halves the capacity of every
	// player's food- and shield-boxes" (tjs282, C3X thread, post-16624265).
	// The city's yields stay the same: it grows and builds twice as fast on
	// the same surplus, and makes no more commerce.
	[Fact]
	public void TheFoodAndShieldBoxesAreHalvedAndYieldsUnchanged() {
		gameData.rules.AcceleratedProduction = false;
		int food = city.CurrentFoodYield();
		int shields = Shields(city);
		int surplus = city.FoodGrowthPerTurn();
		int commerce = TotalCommerce(city.CurrentCommerceYieldRaw());
		int box = city.FoodNeededToGrow();
		UnitPrototype warrior = gameData.unitPrototypes.First(u => u.name == "Warrior");
		int cost = city.owner.ShieldCost(warrior);

		gameData.rules.AcceleratedProduction = true;
		Assert.Equal(food, city.CurrentFoodYield());
		Assert.Equal(shields, Shields(city));
		Assert.Equal(surplus, city.FoodGrowthPerTurn());
		Assert.Equal(commerce, TotalCommerce(city.CurrentCommerceYieldRaw()));
		Assert.Equal(box / 2, city.FoodNeededToGrow());
		Assert.Equal(cost / 2, city.owner.ShieldCost(warrior));
	}

	// Accelerated Production halves tech costs too: flag 0x200 "halves the
	// food box, unit cost, improvement cost and tech cost base" (Perez's
	// decompile, Round 2b "Costs"; owner's ruling 2026-11-12).
	[Fact]
	public void TechCostsAreHalved() {
		Player owner = city.owner;
		Tech tech = owner.GetAvailableTechsToResearch(gameData.techs).First(t => t.id != owner.currentlyResearchedTech);
		gameData.rules.AcceleratedProduction = false;
		int full = gameData.TechCostFor(tech, owner);

		gameData.rules.AcceleratedProduction = true;
		int halved = gameData.TechCostFor(tech, owner);
		gameData.rules.AcceleratedProduction = false;

		Assert.True(full > 1);
		Assert.InRange(halved, full / 2 - 1, full / 2 + 1);
	}

	private static int Shields(City c) {
		CorruptableValue shields = c.CurrentProductionYield();
		return shields.useful + shields.corrupt;
	}

	private static int TotalCommerce(CommerceBreakdown c) {
		return c.corrupted + c.taxes + c.beakers + c.happiness;
	}

	[Fact]
	public void TheChoiceAtSetupIsSaved() {
		SaveGame save = fixture.saveGame.Clone();
		new GameSetup { acceleratedProduction = true, difficulty = save.GameDifficulty }.Populate(save);
		Assert.True(save.Rules.AcceleratedProduction);

		SaveGame loaded = SaveGame.FromJSON(save.ToCompactJSON());
		Assert.True(loaded.Rules.AcceleratedProduction);
	}

	[Fact]
	public void SetupWithoutAChoiceKeepsTheScenarioSetting() {
		SaveGame save = fixture.saveGame.Clone();
		save.Rules.AcceleratedProduction = true;
		new GameSetup { difficulty = save.GameDifficulty }.Populate(save);
		Assert.True(save.Rules.AcceleratedProduction);
	}
}

using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Switching a city's build keeps all the shields (user, 2026-11-11), but
// the box never holds more than the new item costs: switching to
// something cheaper loses the rest, also when switching back. Civ3 lost
// half between units, improvements and wonders, which is still a rule
// (ProductionSwitchKeptPercent): within a category it costs nothing,
// switching back the same turn returns the loss. Wealth is an improvement
// (Civ3's Capitalization); small wonders and spaceship parts count as
// wonders.
public class ProductionSwitchTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public ProductionSwitchTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		// A clone, because a test changes a unit type, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Workshop");
		city.SetItemBeingProduced(Unit("Warrior"));
		city.SetStoredShields(9);
	}

	private UnitPrototype Unit(string name) => gameData.unitPrototypes.Find(u => u.name == name);
	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);

	[Fact]
	public void TheCategories() {
		Assert.Equal(City.ProductionCategory.Unit, City.CategoryOf(Unit("Settler")));
		Assert.Equal(City.ProductionCategory.Improvement, City.CategoryOf(Building("Temple")));
		Assert.Equal(City.ProductionCategory.Improvement, City.CategoryOf(gameData.Inflows.First()));
		Assert.Equal(City.ProductionCategory.Wonder, City.CategoryOf(Building("The Pyramids")));
		Assert.Equal(City.ProductionCategory.Wonder, City.CategoryOf(Building("Heroic Epic")));
	}

	[Fact]
	public void WithinACategoryNothingIsLost() {
		city.SetItemBeingProduced(Unit("Spearman"));
		Assert.Equal(9, city.shieldsStored);
		city.SetItemBeingProduced(Unit("Settler"));
		Assert.Equal(9, city.shieldsStored);
	}

	[Fact]
	public void AllTheShieldsGoToAnotherCategory() {
		Assert.Equal(100, gameData.rules.ProductionSwitchKeptPercent);
		city.SetItemBeingProduced(Building("Temple"));
		Assert.Equal(9, city.shieldsStored);
		city.SetItemBeingProduced(Building("The Pyramids"));
		Assert.Equal(9, city.shieldsStored);
		city.SetItemBeingProduced(Unit("Settler"));
		Assert.Equal(9, city.shieldsStored);
	}

	[Fact]
	public void SomethingCheaperKeepsOnlyItsCostEvenSwitchingBack() {
		city.SetItemBeingProduced(Building("Temple"));
		int warrior = us.ShieldCost(Unit("Warrior"));
		city.SetStoredShields(warrior + 15);
		city.SetItemBeingProduced(Unit("Warrior"));
		Assert.Equal(warrior, city.shieldsStored);
		city.SetItemBeingProduced(Building("Temple"));
		Assert.Equal(warrior, city.shieldsStored);
	}

	[Fact]
	public void WealthCostsNothingSoItEmptiesTheBox() {
		city.SetItemBeingProduced(gameData.Inflows.First());
		Assert.Equal(0, city.shieldsStored);
		city.SetItemBeingProduced(Unit("Warrior"));
		Assert.Equal(0, city.shieldsStored);
	}

	[Fact]
	public void WithCiv3sRuleHalfIsLostToAnotherCategory() {
		gameData.rules.ProductionSwitchKeptPercent = 50;
		city.SetItemBeingProduced(Building("Temple"));
		// Half of 9, rounded down, is kept.
		Assert.Equal(4, city.shieldsStored);
	}

	[Fact]
	public void TheShareKeptIsARule() {
		gameData.rules.ProductionSwitchKeptPercent = 75;
		city.SetItemBeingProduced(Building("Temple"));
		Assert.Equal(6, city.shieldsStored);
	}

	[Fact]
	public void SwitchingBackTheSameTurnReturnsThemAndAThirdCategoryCostsNoMore() {
		gameData.rules.ProductionSwitchKeptPercent = 50;
		city.SetItemBeingProduced(Building("Temple"));
		city.SetItemBeingProduced(Building("The Pyramids"));
		Assert.Equal(4, city.shieldsStored);

		city.SetItemBeingProduced(Unit("Spearman"));
		Assert.Equal(9, city.shieldsStored);
	}

	[Fact]
	public void NextTurnASwitchCostsAgain() {
		gameData.rules.ProductionSwitchKeptPercent = 50;
		city.SetItemBeingProduced(Building("Temple"));
		city.ComputeTurnProduction();
		int stored = city.shieldsStored;
		Assert.True(stored > 4);

		city.SetItemBeingProduced(Unit("Spearman"));
		Assert.Equal(stored / 2, city.shieldsStored);
	}

	[Fact]
	public void ACityThatLostAWonderRaceMovesToAnotherWonder() {
		Player rival = gameData.players.First(p => !p.isHuman && !p.isBarbarians && p != us);
		foreach (Tech tech in gameData.techs.Where(t => t.Name is "Bronze Working" or "Mysticism" or "Masonry")) {
			us.knownTechs.Add(tech.id);
		}
		// The most expensive thing we can build is a unit, so the city has
		// to look for a wonder.
		Unit("Warrior").shieldCost = 100000;
		city.SetItemBeingProduced(Building("The Colossus"));
		city.SetStoredShields(100);
		City theirs = CityInteractions.BuildCity(rival.units[0].location, rival, "Rhodes");
		theirs.SetItemBeingProduced(Building("The Colossus"));
		theirs.SetStoredShields(rival.ShieldCost(Building("The Colossus")));

		theirs.HandleCityProduction(gameData);

		Assert.Equal(City.ProductionCategory.Wonder, City.CategoryOf(city.itemBeingProduced));
		Assert.NotEqual(Building("The Colossus"), city.itemBeingProduced);
		Assert.Equal(100, city.shieldsStored);
	}

	[Fact]
	public void TheSwitchIsSaved() {
		gameData.rules.ProductionSwitchKeptPercent = 50;
		city.SetItemBeingProduced(Building("Temple"));

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		City loaded = reloaded.cities.Find(c => c.id == city.id);
		Assert.Equal(4, loaded.shieldsStored);
		loaded.SetItemBeingProduced(reloaded.unitPrototypes.Find(u => u.name == "Spearman"));
		Assert.Equal(9, loaded.shieldsStored);
	}
}

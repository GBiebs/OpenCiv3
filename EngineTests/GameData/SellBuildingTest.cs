using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Selling buildings by choice, as in Civ3: one a turn per city, never a wonder,
// the palace or a building that lets the city grow past a size limit.
public class SellBuildingTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public SellBuildingTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Seller");
		us.gold = 0;
	}

	private Building Named(string name) {
		return gameData.Buildings.Find(b => b.name == name);
	}

	[Fact]
	public void SellingABuildingPaysItsValueOnceATurn() {
		Building temple = Named("Temple");
		Building barracks = Named("Barracks");
		city.AddBuilding(temple);
		city.AddBuilding(barracks);
		int value = city.SaleValue(temple);
		Assert.True(value > 0);

		Assert.Equal(value, city.SellBuildingByChoice(temple));

		Assert.Equal(value, us.gold);
		Assert.DoesNotContain(city.constructed_buildings, cb => cb.building == temple);
		Assert.Equal("We have already sold a building in Seller this turn.", city.WhyBuildingCannotBeSold(barracks));
		Assert.Equal(0, city.SellBuildingByChoice(barracks));
		Assert.Contains(city.constructed_buildings, cb => cb.building == barracks);

		gameData.turn++;
		Assert.Null(city.WhyBuildingCannotBeSold(barracks));
		Assert.True(city.SellBuildingByChoice(barracks) > 0);
	}

	[Fact]
	public void WondersThePalaceAndSizeLimitBuildingsCannotBeSold() {
		Building aqueduct = gameData.Buildings.First(b => b.allowsCitySize2 && !b.IsGreatWonder() && !b.isSmallWonder);
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder());
		Building palace = gameData.Buildings.First(b => b.isCenterOfEmpire);
		city.AddBuilding(aqueduct);
		city.AddBuilding(wonder);

		foreach (Building b in new[] { aqueduct, wonder, palace }) {
			Assert.False(City.CanBeSold(b));
			Assert.Equal($"We can't sell the {b.name}.", city.WhyBuildingCannotBeSold(b));
			Assert.Equal(0, city.SellBuildingByChoice(b));
		}
		Assert.Equal(0, us.gold);
	}

	[Fact]
	public void ABuildingNotBuiltHereCannotBeSold() {
		Assert.Equal("We can't sell the Temple.", city.WhyBuildingCannotBeSold(Named("Temple")));
	}

	[Fact]
	public void TheTurnOfTheLastSaleIsSaved() {
		city.AddBuilding(Named("Temple"));
		city.SellBuildingByChoice(Named("Temple"));

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);

		Assert.Equal(gameData.turn, reloaded.cities.Find(c => c.name == "Seller").buildingSoldOnTurn);
		Assert.All(reloaded.cities.Where(c => c.name != "Seller"), c => Assert.Equal(-1, c.buildingSoldOnTurn));
	}

	[Fact]
	public void ABrokeCivNeverSellsItsAqueducts() {
		Building aqueduct = gameData.Buildings.First(b => b.allowsCitySize2 && !b.IsGreatWonder() && !b.isSmallWonder);
		aqueduct.maintenanceCost = 20;
		city.AddBuilding(aqueduct);
		us.taxRate = 10;
		us.scienceRate = 0;
		us.luxuryRate = 0;

		us.DoPerTurnFinanceUpdates(gameData);

		Assert.Contains(city.constructed_buildings, cb => cb.building == aqueduct);
		Assert.Equal(0, us.gold);
	}
}

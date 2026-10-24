using System;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// When the treasury can't pay the bills, Civ3 sells buildings. OpenCiv3 used to
// throw instead ("unable to get the budget under control"), which ended an
// all-AI game in the start-rating study (seed 102, turn 296).
public class BrokeTreasuryTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player player;
	private readonly City city;

	public BrokeTreasuryTest(SaveGameFixture fixture) {
		// A clone, because tests change the rules, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		player = gameData.players.First(p => p.isHuman);
		city = CityInteractions.BuildCity(player.units[0].location, player, "Broke");
		// Everything already goes to taxes, so moving the sliders can't help,
		// and no units cost support.
		player.taxRate = 10;
		player.scienceRate = 0;
		player.luxuryRate = 0;
		player.gold = 0;
		Assert.Equal(0, player.TotalUnitsAllowedUnitsAndSupportCost().Item3);
	}

	private Building Sellable(Func<Building, bool> predicate) {
		return gameData.Buildings.First(b => City.CanBeSold(b) && predicate(b));
	}

	[Fact]
	public void ABrokeCivSellsTheBuildingWithTheHighestUpkeep() {
		Building cheapToRun = Sellable(b => b.maintenanceCost == 1);
		Building dearToRun = Sellable(b => b.maintenanceCost == 1 && b != cheapToRun);
		dearToRun.maintenanceCost = 20; // far more than a size 1 city's taxes
		city.AddBuilding(cheapToRun, gameData);
		city.AddBuilding(dearToRun, gameData);
		int saleValue = city.SaleValue(dearToRun);
		Assert.True(player.CalculateGoldPerTurn() < 0);

		player.DoPerTurnFinanceUpdates(gameData);

		Assert.DoesNotContain(city.constructed_buildings, cb => cb.building == dearToRun);
		Assert.Contains(city.constructed_buildings, cb => cb.building == cheapToRun);
		Assert.Equal(saleValue + player.CalculateGoldPerTurn(), player.gold);
		MsgBuildingSold message = Assert.Single(EngineStorage.messagesToUI.OfType<MsgBuildingSold>());
		Assert.Equal(player.id, message.playerId);
		Assert.Equal("Broke", message.cityName);
		Assert.Equal(dearToRun.name, message.buildingName);
		Assert.Equal(saleValue, message.goldReceived);
	}

	[Fact]
	public void WondersAndThePalaceAreNeverSoldAndTheTreasuryStaysAtZero() {
		Building palace = gameData.Buildings.Find(b => b.isCenterOfEmpire);
		Building wonder = gameData.Buildings.First(b => b.IsGreatWonder());
		wonder.maintenanceCost = 20;
		city.AddBuilding(wonder, gameData);
		Assert.True(player.CalculateGoldPerTurn() < 0);

		player.DoPerTurnFinanceUpdates(gameData);

		Assert.Contains(city.constructed_buildings, cb => cb.building == wonder);
		Assert.Contains(city.constructed_buildings, cb => cb.building == palace);
		Assert.Equal(0, player.gold);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgBuildingSold>());
	}

	[Fact]
	public void ABuildingSellsForItsShieldCostAtTheShieldsPerGoldRate() {
		Building building = Sellable(b => b.ShieldCost(player, 1.0f) >= 40);
		Assert.Equal(4, gameData.rules.ShieldCostPerGold); // Civ3's rate: a quarter of the cost

		Assert.Equal(building.ShieldCost(player, 1.0f) / 4, city.SaleValue(building));

		gameData.rules.ShieldCostPerGold = 8;
		Assert.Equal(building.ShieldCost(player, 1.0f) / 8, city.SaleValue(building));
	}

	[Fact]
	public void SellingABuildingRemovesItAndPaysTheOwner() {
		Building building = Sellable(b => b.ShieldCost(player, 1.0f) >= 40);
		city.AddBuilding(building, gameData);
		player.gold = 10;

		int received = city.SellBuilding(city.constructed_buildings.First(cb => cb.building == building));

		Assert.Equal(city.SaleValue(building), received);
		Assert.Equal(10 + received, player.gold);
		Assert.DoesNotContain(city.constructed_buildings, cb => cb.building == building);
	}
}

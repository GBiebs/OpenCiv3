using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;
using static C7GameData.AdvisorAdvice;

namespace EngineTests.GameData;

// What the advisors say on their screens: the most pressing matter first, and
// a mood to match.
public class AdvisorAdviceTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public AdvisorAdviceTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.messagesToUI.Clear();
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		// Away from fresh water, so the city needs an aqueduct to pass town size.
		Tile site = gameData.map.tiles.Where(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity() && t.unitsOnTile.Count == 0
			&& t.overlayTerrainType.Key == "grassland" && !t.BordersRiver()
			&& t.neighbors.Values.All(n => n.IsLand() || !n.isFreshWater)
			&& t.neighbors.Values.Count(n => Tile.IsTileValid(n) && n.IsLand()) == 8)
			.OrderBy(t => t.DistanceTo(us.units[0].location)).First();
		city = CityInteractions.BuildCity(site, us, "Home");
		us.gold = 100;
	}

	private void AddCitizens(int count) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		for (int i = 0; i < count; i++) {
			city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = laborer });
		}
	}

	[Fact]
	public void AllCalmAtHome() {
		Assert.True(city.FoodGrowthPerTurn() >= 0);
		Advice advice = Domestic(gameData, us);
		Assert.Equal(Mood.Happy, advice.mood);
		Assert.StartsWith("All is calm at home", advice.text);
	}

	[Fact]
	public void AnarchyComesFirst() {
		us.inAnarchyUntilTurn = gameData.turn + 2;
		city.isInCivilDisorder = true;
		Advice advice = Domestic(gameData, us);
		Assert.Equal(Mood.Sad, advice.mood);
		Assert.Contains("2 turns until our new government", advice.text);
	}

	[Fact]
	public void CitiesInDisorder() {
		city.isInCivilDisorder = true;
		Advice advice = Domestic(gameData, us);
		Assert.Equal(Mood.Angry, advice.mood);
		Assert.StartsWith("Home is in disorder!", advice.text);
	}

	[Fact]
	public void TheTreasuryRunningOut() {
		// All commerce to science, and a building to pay for.
		us.scienceRate = 10;
		us.luxuryRate = 0;
		city.AddBuilding(gameData.Buildings.First(b => b.maintenanceCost > 0 && !b.IsWonder()), gameData);
		int net = us.CalculateGoldPerTurn();
		Assert.True(net < 0);

		us.gold = -net * 3;
		Advice soon = Domestic(gameData, us);
		Assert.Equal(Mood.Sad, soon.mood);
		Assert.Contains($"We lose {-net} gold a turn: the treasury lasts only 3 turns more.", soon.text);

		us.gold = -net * TreasuryWarningTurns;
		Assert.Equal(Mood.Happy, Domestic(gameData, us).mood);

		us.gold = 0;
		Advice broke = Domestic(gameData, us);
		Assert.Equal(Mood.Angry, broke.mood);
		Assert.Contains("runs dry next turn", broke.text);
	}

	[Fact]
	public void StarvingCities() {
		// Citizens with no tiles to work eat more than the city grows.
		AddCitizens(6);
		Assert.True(city.FoodGrowthPerTurn() < 0);
		Advice advice = Domestic(gameData, us);
		Assert.Equal(Mood.Sad, advice.mood);
		Assert.Equal("Home is starving.", advice.text);
	}

	[Fact]
	public void ACityThatNeedsABuildingToGrow() {
		Assert.True(city.FoodGrowthPerTurn() > 0);
		gameData.rules.MaximumLevel1CitySize = city.residents.Count;
		Advice advice = Domestic(gameData, us);
		Assert.Equal(Mood.Surprised, advice.mood);
		Assert.Equal("Home can grow no larger without an Aqueduct.", advice.text);

		// With an aqueduct, a hospital to pass city size.
		city.AddBuilding(gameData.Buildings.First(b => b.allowsCitySize2), gameData);
		gameData.rules.MaximumLevel2CitySize = city.residents.Count;
		Assert.Equal("Home can grow no larger without a Hospital.", Domestic(gameData, us).text);
	}
}

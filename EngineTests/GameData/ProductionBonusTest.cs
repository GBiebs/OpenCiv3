using System;
using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's production buildings (Conquests Civilopedia): a Factory adds 50%
// to a city's shields, a power plant 50% more of the Factory's ("increases
// factory output", so nothing without one; the Nuclear Plant 100%), a
// Manufacturing Plant 50% and the Iron Works 100%. The plants replace each
// other, and the Hoover Dam acts as a Hydro Plant in every city on its
// continent. The bonus multiplies the shields left after waste.
public class ProductionBonusTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;
	private readonly int shields;

	public ProductionBonusTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Forge");
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < 4) {
			CityResident resident = new() { city = city, nationality = us.civilization, citizenType = laborer };
			city.AddCitizen(resident);
			C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, resident);
		}
		// Forest under the workers, for shields to multiply.
		TerrainType forest = gameData.terrainTypes.Find(t => t.Key == "forest");
		foreach (CityResident r in city.residents.Where(r => r.tileWorked != Tile.NONE)) {
			r.tileWorked.overlayTerrainType = forest;
		}
		shields = city.CurrentProductionYield().useful;
	}

	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);

	private void Add(string name) => city.AddBuilding(Building(name), gameData);

	private int Useful() => city.CurrentProductionYield().useful;

	private int Times(double factor) => (int)Math.Floor(shields * factor);

	[Fact]
	public void AFactoryAddsHalf() {
		Assert.True(shields >= 4);
		int corrupt = city.CurrentProductionYield().corrupt;
		Add("Factory");
		Assert.Equal(Times(1.5), Useful());
		// The extra shields aren't wasted.
		Assert.Equal(corrupt, city.CurrentProductionYield().corrupt);
	}

	[Fact]
	public void APowerPlantAddsHalfMoreButOnlyWithAFactory() {
		Add("Coal Plant");
		Assert.Equal(shields, Useful());
		Add("Factory");
		Assert.Equal(Times(2), Useful());
	}

	[Fact]
	public void TheNuclearPlantAddsAWhole() {
		Add("Factory");
		Add("Nuclear Plant");
		Assert.Equal(Times(2.5), Useful());
	}

	[Fact]
	public void OnlyTheBestPlantCounts() {
		Add("Factory");
		Add("Coal Plant");
		Add("Nuclear Plant");
		Assert.Equal(Times(2.5), Useful());
	}

	[Fact]
	public void AManufacturingPlantAddsToTheRest() {
		Add("Factory");
		Add("Manufacturing Plant");
		Assert.Equal(Times(2), Useful());
		Add("Coal Plant");
		Assert.Equal(Times(2.5), Useful());
	}

	[Fact]
	public void TheHooverDamPowersTheFactoriesOnItsContinent() {
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && !t.HasCity()
			&& t.continent == city.location.continent && gameData.cities.All(c => c.location.DistanceTo(t) > 3));
		City dam = CityInteractions.BuildCity(site, us, "Dam");
		dam.AddBuilding(Building("Hoover Dam"), gameData);
		Assert.Equal(shields, Useful());

		Add("Factory");
		Assert.Equal(Times(2), Useful());
	}

	[Fact]
	public void TheIronWorksAddsAWhole() {
		Add("Iron Works");
		Assert.Equal(Times(2), Useful());
	}
}

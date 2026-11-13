using C7GameData;
using Xunit;

namespace EngineTests.GameData;

public class CityTest {
	[Fact]
	public void CityWith2ProductionPerTurn_ShouldReturn1TurnIf9_of_10ProductionDone() {
		C7Engine.EngineStorage.InitializeGameDataForTests(new C7GameData.GameData() {
			gameDifficulty = new Difficulty(),
		});
		Player player = new() {
			isHuman = true,
		};
		player.civilization = new Civilization();
		player.government = new Government();
		UnitPrototype warrior = new UnitPrototype();
		warrior.shieldCost = 10;

		City city = new City(Tile.NONE, player, "Fighter Town, USA", ID.None("city"));
		city.itemBeingProduced = warrior;
		city.SetStoredShields(9);

		TerrainType oneShield = new TerrainType();
		oneShield.baseShieldProduction = 1;

		Tile workedTile = new Tile(ID.None("tile"));
		workedTile.overlayTerrainType = oneShield;

		CityResident maverick = new CityResident();
		maverick.tileWorked = workedTile;
		city.residents.Add(maverick);

		int turnsUntilFinished = city.TurnsUntilProductionFinished();
		Assert.Equal(1, turnsUntilFinished);
	}

	[Fact]
	public void CityWith2ProductionPerTurn_ShouldReturn1TurnIf19_of_20FoodDone() {
		// Human, so the food box doesn't depend on the difficulty another
		// test left behind (the AI's is scaled by its cost factor).
		Player player = new() { isHuman = true };
		player.government = new Government();
		player.rules = new() { MaximumLevel1CitySize = 6 };
		TerrainType oneShield = new TerrainType();
		oneShield.baseShieldProduction = 1;
		Tile tile = new Tile(ID.None("tile"));

		City city = new City(tile, player, "Gotham", ID.None("city"));
		city.foodStored = 19;
		tile.cityAtTile = city;

		TerrainType grassland = new TerrainType();
		grassland.baseFoodProduction = 2;

		Tile workedTile = new Tile(ID.None("test-tile"));
		workedTile.overlayTerrainType = grassland;

		CityResident robin = new CityResident();
		robin.tileWorked = workedTile;
		city.residents.Add(robin);

		int turnsUntilGrowth = city.TurnsUntilGrowth();
		Assert.Equal(1, turnsUntilGrowth);
	}

	[Fact]
	public void CityShouldShrinkWhenItRunsOutOfFood() {
		C7GameData.GameData gameData = new();
		Player player = new();
		player.government = new Government();
		player.rules = new() { MaximumLevel1CitySize = 6 };
		TerrainType oneShield = new TerrainType();
		oneShield.baseShieldProduction = 1;
		Tile tile = new Tile(ID.None("tile"));

		City city = new City(tile, player, "Gotham", ID.None("city"));
		city.foodStored = 0;
		tile.cityAtTile = city;

		CityResident resident1 = new();
		city.residents.Add(resident1);

		CityResident resident2 = new();
		city.residents.Add(resident2);

		// Confirm we lose population without enough food.
		Assert.Equal(2, city.residents.Count);
		city.HandleCityGrowth(gameData);
		Assert.Single(city.residents);
	}

	// "stock < 0 => stock 0 and lose 1 citizen; if size reaches 0 the city
	// is destroyed" (Civ3 binary, doc/perez-findings/Civ3_Findings.md, Food,
	// growth, starvation): HandleCityGrowth tells the caller to destroy it.
	[Fact]
	public void SizeOneCityStarvesToNothing() {
		C7GameData.GameData gameData = new();
		Player player = new();
		player.government = new Government();
		player.rules = new() { MaximumLevel1CitySize = 6, CityCenterFood = 0 };
		Tile tile = new Tile(ID.None("tile"));

		City city = new City(tile, player, "Gotham", ID.None("city"));
		city.foodStored = 0;
		tile.cityAtTile = city;

		city.residents.Add(new CityResident());

		Assert.True(city.HandleCityGrowth(gameData));
		Assert.Equal(0, city.foodStored);
	}

	private static City MakeProductionCity() {
		C7Engine.EngineStorage.InitializeGameDataForTests(new C7GameData.GameData() {
			gameDifficulty = new Difficulty(),
		});
		Player player = new() { isHuman = true, civilization = new Civilization(), government = new Government() };
		City city = new(Tile.NONE, player, "Pittsburgh", ID.None("city"));
		city.SetItemBeingProduced(new UnitPrototype() { name = "Warrior", shieldCost = 100 });
		city.SetStoredShields(20);
		return city;
	}

	private static Building MakeBuilding(string name) {
		return new Building(new C7GameData.Save.SaveBuilding() { name = name, shieldCost = 100 }, new C7GameData.GameData());
	}

	[Fact]
	public void SwitchingProductionKeepsTheShields() {
		City city = MakeProductionCity();

		city.ChangeProduction(MakeBuilding("Temple"));
		Assert.Equal(20, city.shieldsStored);
	}

	[Fact]
	public void SwitchingToACheaperItemLosesTheExtraShields() {
		City city = MakeProductionCity();

		city.ChangeProduction(new UnitPrototype() { name = "Worker", shieldCost = 10 });
		Assert.Equal(10, city.shieldsStored);
	}
}

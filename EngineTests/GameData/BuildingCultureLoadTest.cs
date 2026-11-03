using System.Linq;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// A city building's culture is saved with it (#1066); one from a save made
// before that, or from a Civ3 SAV, makes its own culture once loaded.
public class BuildingCultureLoadTest : IClassFixture<SaveGameFixture> {
	private readonly Building temple;

	public BuildingCultureLoadTest(SaveGameFixture fixture) {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		temple = gameData.Buildings.First(b => b.culturePerTurn > 0 && !b.IsWonder());
	}

	[Fact]
	public void ABuildingFromAnOlderSaveMakesItsCulture() {
		SaveCityBuilding saved = new() { building = temple.name, year = -3000, totalCulture = 10 };
		CityBuilding cb = saved.ToCityBuilding([temple], []);
		Assert.Equal(temple.culturePerTurn, cb.baseCulturePerTurn);
		Assert.Equal(temple.culturePerTurn, cb.currentCulturePerTurn);
		Assert.Equal(10, cb.totalCulture);
	}

	[Fact]
	public void ASavedBuildingKeepsItsCultureAndAgeBonus() {
		SaveCityBuilding saved = new() { building = temple.name, year = -3000, turnBuilt = 12, baseCulturePerTurn = 2, currentCulturePerTurn = 4 };
		CityBuilding cb = saved.ToCityBuilding([temple], []);
		Assert.Equal(12, cb.turnBuilt);
		Assert.Equal(2, cb.baseCulturePerTurn);
		Assert.Equal(4, cb.currentCulturePerTurn);
	}
}

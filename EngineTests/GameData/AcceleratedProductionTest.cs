using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's Accelerated Production game setting: builds and upgrades cost
// half the shields (Rules.AcceleratedProductionPercent), and it's saved.
public class AcceleratedProductionTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;

	public AcceleratedProductionTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		us.isHuman = true;
	}

	[Fact]
	public void BuildsCostHalfWithIt() {
		Building barracks = gameData.Buildings.Find(b => b.name == "Barracks");
		UnitPrototype warrior = gameData.unitPrototypes.Find(u => u.name == "Warrior");
		int barracksCost = us.ShieldCost(barracks);
		int warriorCost = us.ShieldCost(warrior);
		Assert.False(gameData.acceleratedProduction);

		gameData.acceleratedProduction = true;
		Assert.Equal((barracksCost + 1) / 2, us.ShieldCost(barracks));
		Assert.Equal((warriorCost + 1) / 2, us.ShieldCost(warrior));

		gameData.rules.AcceleratedProductionPercent = 100;
		Assert.Equal(barracksCost, us.ShieldCost(barracks));
	}

	[Fact]
	public void UpgradesCostHalfWithIt() {
		Assert.Equal(15, gameData.ProductionCost(15));
		gameData.acceleratedProduction = true;
		Assert.Equal(8, gameData.ProductionCost(15));
		Assert.Equal(0, gameData.ProductionCost(0));
	}

	[Fact]
	public void TheSettingIsSaved() {
		gameData.acceleratedProduction = true;
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.True(reloaded.acceleratedProduction);

		SaveGame fresh = fixture.saveGame.Clone();
		Assert.False(fresh.AcceleratedProduction);
	}
}

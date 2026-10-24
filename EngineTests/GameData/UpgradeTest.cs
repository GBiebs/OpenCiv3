using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Upgrading units, as in Civ3: in one of our cities with a Barracks (Harbor
// for ships, Airport for aircraft), to the best unit of the line the city can
// build, for the difference in shields times the rules' upgrade cost (3), half
// with Leonardo's Workshop.
public class UpgradeTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;
	private readonly UnitPrototype oldType;
	private readonly UnitPrototype newType;

	public UpgradeTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		city = CityInteractions.BuildCity(us.units[0].location, us, "Garrison");
		// A ground unit of ours whose direct upgrade needs no resource; we learn
		// the upgrade's tech.
		(oldType, newType) = gameData.unitPrototypes
			.Where(u => u.IsLandUnit() && u.actions.Contains(UnitAction.Upgrade) && u.producibleBy.Contains(us.civilization))
			.SelectMany(u => u.upgradesTo.Select(t => (u, t)))
			.First(pair => pair.t.requiredResources.Count == 0 && pair.t.producibleBy.Contains(us.civilization));
		us.knownTechs.Add(newType.requiredTech.id);
		us.gold = 1000;
	}

	private Building Barracks() {
		return gameData.Buildings.First(b => b.providesVeteranGroundUnits && !b.IsGreatWonder());
	}

	private MapUnit Spawn() {
		return gameData.SpawnUnit(us, oldType, city.location);
	}

	[Fact]
	public void AUnitInACityWithABarracksUpgradesForThreeGoldAShield() {
		city.AddBuilding(Barracks());
		MapUnit unit = Spawn();
		unit.hitPointsRemaining = 2;
		int expectedCost = (newType.shieldCost - oldType.shieldCost) * 3;

		Assert.Equal((newType, expectedCost), unit.UpgradeOffer());
		Assert.Contains(UnitAction.Upgrade, unit.GetAvailableActions());
		Assert.True(unit.Upgrade());

		Assert.Equal(newType, unit.unitType);
		Assert.Equal(newType.name, unit.name);
		Assert.Equal(1000 - expectedCost, us.gold);
		Assert.Equal(2, unit.hitPointsRemaining);
		Assert.Equal(us.civilization, unit.nationality);
	}

	[Fact]
	public void WithoutABarracksThereIsNoUpgrade() {
		MapUnit unit = Spawn();

		Assert.Null(unit.UpgradeOffer());
		Assert.False(unit.Upgrade());
		Assert.Equal(oldType, unit.unitType);
		Assert.DoesNotContain(UnitAction.Upgrade, unit.GetAvailableActions());
	}

	[Fact]
	public void OutsideOurCitiesThereIsNoUpgrade() {
		city.AddBuilding(Barracks());
		MapUnit unit = gameData.SpawnUnit(us, oldType, city.location.neighbors.Values.First(t => t.IsLand()));

		Assert.Null(unit.UpgradeOffer());
	}

	[Fact]
	public void UpgradingNeedsTheGold() {
		city.AddBuilding(Barracks());
		MapUnit unit = Spawn();
		us.gold = 1;

		Assert.NotNull(unit.UpgradeOffer());
		Assert.False(unit.CanUpgrade());
		Assert.False(unit.Upgrade());
		Assert.Equal(1, us.gold);
		// The button still shows, so the player can see the price.
		Assert.Contains(UnitAction.Upgrade, unit.GetAvailableActions());
	}

	[Fact]
	public void LeonardosWorkshopHalvesThePrice() {
		city.AddBuilding(Barracks());
		Building workshop = gameData.Buildings.First(b => b.IsGreatWonder() && b.HasWonderFlag(C7GameData.Save.SaveBuilding.WonderFlag.CheaperUpgrades));
		city.AddBuilding(workshop);
		MapUnit unit = Spawn();

		Assert.Equal((newType.shieldCost - oldType.shieldCost) * 3 / 2, unit.UpgradeOffer()?.cost);
	}

	[Fact]
	public void TheAiUpgradesWhenItCanSpareTheGold() {
		city.AddBuilding(Barracks());
		MapUnit rich = Spawn();
		int cost = rich.UpgradeOffer().Value.cost;
		us.gold = cost + C7Engine.PlayerAI.UPGRADE_GOLD_RESERVE;

		C7Engine.PlayerAI.UpgradeUnits(us);

		Assert.Equal(newType, rich.unitType);
		Assert.Equal(C7Engine.PlayerAI.UPGRADE_GOLD_RESERVE, us.gold);

		MapUnit poor = Spawn();
		C7Engine.PlayerAI.UpgradeUnits(us);
		Assert.Equal(oldType, poor.unitType);
	}

	[Fact]
	public void TheCiv3RulesetHasConquestsUpgradeRules() {
		Assert.Equal(3, gameData.rules.UpgradeCost);
		Assert.Contains(UnitAction.Upgrade, gameData.unitPrototypes.Find(u => u.name == "Spearman").actions);
		Assert.DoesNotContain(UnitAction.Upgrade, gameData.unitPrototypes.Find(u => u.name == "Settler").actions);
	}
}

using System.Linq;
using C7GameData;
using C7GameData.Save;
using Xunit;

namespace EngineTests.GameData;

public class UnitUpgradeTest {
	private readonly C7GameData.GameData gameData = new() { gameDifficulty = new Difficulty(), rules = new Rules() };
	private readonly Player player;
	private readonly City city;
	private readonly UnitPrototype spearman;
	private readonly UnitPrototype pikeman;

	public UnitUpgradeTest() {
		C7Engine.EngineStorage.InitializeGameDataForTests(gameData);

		Civilization civ = new() { name = "Testers" };
		player = new() { isHuman = true, civilization = civ, government = new Government(), gold = 100 };
		gameData.players.Add(player);

		spearman = new UnitPrototype() { name = "Spearman", shieldCost = 20 };
		pikeman = new UnitPrototype() { name = "Pikeman", shieldCost = 30 };
		foreach (UnitPrototype p in new[] { spearman, pikeman }) {
			p.categories.Add("Land");
			p.actions.Add(UnitAction.Upgrade);
			p.producibleBy.Add(civ);
			gameData.unitPrototypes.Add(p);
		}
		spearman.upgradesTo.Add(pikeman);

		Tile tile = new(ID.None("tile"));
		city = new City(tile, player, "Testville", ID.None("city"));
		tile.cityAtTile = city;
		player.cities.Add(city);
	}

	private MapUnit MakeSpearman() {
		MapUnit unit = spearman.GetInstance(ID.None("unit"), spearman, player, location: city.location);
		unit.experienceLevel = new ExperienceLevel("veteran", "Veteran", 4, 0, 0);
		unit.hitPointsRemaining = 4;
		return unit;
	}

	private void AddBarracks() {
		SaveBuilding barracks = new() { name = "Barracks" };
		barracks.flags.Add(SaveBuilding.Flag.VeteranGroundUnits);
		city.AddBuilding(new Building(barracks, new C7GameData.GameData()));
	}

	[Fact]
	public void UpgradingNeedsBarracks() {
		Assert.Null(MakeSpearman().GetAvailableUpgrade());
	}

	[Fact]
	public void UpgradeCostsGoldPerShieldOfDifference() {
		AddBarracks();
		MapUnit unit = MakeSpearman();

		Assert.Equal(pikeman, unit.GetAvailableUpgrade());
		Assert.Equal(30, unit.UpgradeCost(pikeman));

		Assert.True(unit.Upgrade());
		Assert.Equal(pikeman, unit.unitType);
		Assert.Equal("Veteran", unit.experienceLevel.displayName);
		Assert.Equal(70, player.gold);
	}

	[Fact]
	public void UpgradingEndsTheUnitsTurn() {
		AddBarracks();
		MapUnit unit = MakeSpearman();
		unit.movementPoints.reset(1);
		Assert.True(unit.movementPoints.canMove);

		Assert.True(unit.Upgrade());
		Assert.False(unit.movementPoints.canMove);
	}

	[Fact]
	public void CannotUpgradeWithoutEnoughGold() {
		AddBarracks();
		player.gold = 10;
		MapUnit unit = MakeSpearman();

		Assert.False(unit.Upgrade());
		Assert.Equal(spearman, unit.unitType);
	}

	// Conquests charges 3 gold a shield of difference ("the costs have gone
	// from 2 * shield_difference to 3 * shield_difference",
	// https://forums.civfanatics.com/threads/civ3-conquests-additions-changes-list.104294/),
	// which is what its BIQ's RULE says and so what the bundled rules have.
	[Fact]
	public void ConquestsChargesThreeGoldAShield() {
		Assert.Equal(3, new Rules().UpgradeCostPerShield);
		using System.Text.Json.JsonDocument ruleset = EngineTests.Utils.JsonUtils.LoadBaseRuleset();
		Assert.Equal(3, ruleset.RootElement.GetProperty("rules").GetProperty("upgradeCostPerShield").GetInt32());
	}

	// "A city with an [Airport] can be used to upgrade air units"
	// (Conquests Civilopedia, BLDG_Airport); barracks don't do.
	[Fact]
	public void AircraftUpgradeAtAnAirport() {
		UnitPrototype biplane = new() { name = "Biplane", shieldCost = 40 };
		UnitPrototype fighter = new() { name = "Fighter", shieldCost = 50 };
		foreach (UnitPrototype p in new[] { biplane, fighter }) {
			p.categories.Add("Air");
			p.actions.Add(UnitAction.Upgrade);
			p.producibleBy.Add(player.civilization);
			gameData.unitPrototypes.Add(p);
		}
		biplane.upgradesTo.Add(fighter);
		MapUnit plane = biplane.GetInstance(ID.None("plane"), biplane, player, location: city.location);

		AddBarracks();
		Assert.Null(plane.GetAvailableUpgrade());

		SaveBuilding airport = new() { name = "Airport" };
		airport.flags.Add(SaveBuilding.Flag.VeteranAirUnits);
		city.AddBuilding(new Building(airport, new C7GameData.GameData()));
		Assert.Equal(fighter, plane.GetAvailableUpgrade());
		Assert.Equal(30, plane.UpgradeCost(fighter));
	}
	// The four Conquests levels, so that elite is the top one.
	private void AddExperienceLevels() {
		gameData.experienceLevels.Add(new ExperienceLevel("Conscript", "Conscript", 2, 0.34, 0.5));
		gameData.experienceLevels.Add(new ExperienceLevel("Regular", "Regular", 3, 0.5, 0.25));
		gameData.experienceLevels.Add(new ExperienceLevel("Veteran", "Veteran", 4, 0.58, 0.125));
		gameData.experienceLevels.Add(new ExperienceLevel("Elite", "Elite", 5, 0.66, 0));
	}

	// "elites are dowgraded to vets but all others hold their status"
	// (https://forums.civfanatics.com/threads/upgrade-or-disband.231677, #13),
	// and the upgraded unit is fully healed and may make a leader again.
	[Fact]
	public void AnUpgradedEliteBecomesAFullyHealedVeteranThatMayMakeALeaderAgain() {
		AddExperienceLevels();
		AddBarracks();
		MapUnit unit = MakeSpearman();
		unit.experienceLevel = gameData.experienceLevels[3];
		unit.experienceLevelKey = "Elite";
		unit.hitPointsRemaining = 1;
		unit.hasProducedLeader = true;

		Assert.True(unit.Upgrade());

		Assert.Equal(pikeman, unit.unitType);
		Assert.Equal("Veteran", unit.experienceLevel.key);
		Assert.Equal("Veteran", unit.experienceLevelKey);
		Assert.Equal(4, unit.hitPointsRemaining);
		Assert.False(unit.hasProducedLeader);
	}

	[Fact]
	public void AnUpgradedVeteranStaysVeteranAndIsFullyHealed() {
		AddExperienceLevels();
		AddBarracks();
		MapUnit unit = MakeSpearman();
		unit.experienceLevel = gameData.experienceLevels[2];
		unit.hitPointsRemaining = 2;

		Assert.True(unit.Upgrade());

		Assert.Equal("Veteran", unit.experienceLevel.key);
		Assert.Equal(4, unit.hitPointsRemaining);
	}

	// "Units in armies cannot be upgraded."
	// (https://civfanatics.com/civ3/strategy/introductory/the-basics-of-armies/).
	[Fact]
	public void UnitsInAnArmyCantBeUpgraded() {
		AddBarracks();
		UnitPrototype armyType = new() { name = "Army", isArmy = true };
		MapUnit army = armyType.GetInstance(ID.None("army"), armyType, player, location: city.location);
		MapUnit unit = MakeSpearman();
		city.location.unitsOnTile.Add(army);
		city.location.unitsOnTile.Add(unit);
		unit.loadedOnUnitId = army.id;

		Assert.Null(unit.GetAvailableUpgrade());
		Assert.False(unit.Upgrade());
		Assert.Equal(spearman, unit.unitType);
		Assert.Equal(100, player.gold);
	}

	private MapUnit OwnedSpearman(Tile location) {
		MapUnit unit = MakeSpearman();
		unit.location = location;
		player.units.Add(unit);
		return unit;
	}

	// Upgrade All (Shift-U): "All units of this type will then be upgraded"
	// (Civ3 README), those in a city with barracks; others, and units of
	// other types, are left alone. Fortified units stay fortified (README
	// 1.21f).
	[Fact]
	public void UpgradeAllUpgradesEveryUnitOfTheTypeThatCan() {
		AddBarracks();
		MapUnit first = OwnedSpearman(city.location);
		MapUnit second = OwnedSpearman(city.location);
		second.isFortified = true;
		MapUnit afield = OwnedSpearman(new Tile(ID.None("tile")));
		MapUnit pike = pikeman.GetInstance(ID.None("unit"), pikeman, player, location: city.location);
		player.units.Add(pike);

		Assert.Equal([first, second], MapUnit.UpgradeAllCandidates(player, spearman).Select(c => c.unit));
		Assert.Equal([first, second], MapUnit.UpgradeAll(player, spearman));

		Assert.Equal(pikeman, first.unitType);
		Assert.Equal(pikeman, second.unitType);
		Assert.True(second.isFortified);
		Assert.Equal(spearman, afield.unitType);
		Assert.Equal(40, player.gold);
	}

	// "We would need $NUM0 gold in order to complete all the upgrades!"
	// (script.txt NO_GOLD_TO_UPGRADE_ALL): not enough for all, none goes.
	[Fact]
	public void UpgradeAllUpgradesNoneWithoutGoldForAll() {
		AddBarracks();
		player.gold = 50;
		MapUnit first = OwnedSpearman(city.location);
		MapUnit second = OwnedSpearman(city.location);

		Assert.Empty(MapUnit.UpgradeAll(player, spearman));
		Assert.Equal(spearman, first.unitType);
		Assert.Equal(spearman, second.unitType);
		Assert.Equal(50, player.gold);
	}

	// Shift-N: a unit's own name, kept through an upgrade; a blank name
	// gives back the type's.
	[Fact]
	public void ARenamedUnitKeepsItsNameThroughAnUpgrade() {
		AddBarracks();
		MapUnit unit = OwnedSpearman(city.location);

		Assert.True(unit.Rename("  The\tImmortals  "));
		Assert.Equal("TheImmortals", unit.name);
		Assert.True(unit.Rename(new string('x', 40)));
		Assert.Equal(MapUnit.MaxNameLength, unit.name.Length);
		Assert.True(unit.Rename("Old Guard"));

		Assert.True(unit.Upgrade());
		Assert.Equal("Old Guard", unit.name);

		Assert.True(unit.Rename("   "));
		Assert.Equal("Pikeman", unit.name);
		Assert.False(unit.Rename("Pikeman"));
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's armies (Conquests Civilopedia "Armies", the Conquests readme, upstream
// proposal #1011): a leader or the Military Academy builds one, four cities
// for each; up to three land units that can attack load into it (four with
// the Pentagon) and can't leave; it moves as fast as its slowest member plus
// one; its members' hit points are pooled and it fights with one member at a
// time, strongest first; it dies with all its members. See MapUnit_Army.cs.
public class ArmyTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City home;

	public ArmyTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		// A clone, because some tests change unit types, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.DeclareWarOn(them, gameData.turn);
		home = CityInteractions.BuildCity(us.units[0].location, us, "Home");
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp
			&& t.baseTerrainType.allowCities && t.overlayTerrainType.allowCities;
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) {
		return gameData.SpawnUnit(owner, Type(type), tile);
	}

	private void SetExperience(MapUnit unit, int level) {
		unit.experienceLevel = gameData.experienceLevels[level];
		unit.experienceLevelKey = unit.experienceLevel.key;
		unit.hitPointsRemaining = unit.maxHitPoints;
	}

	// Gives `player` cities until it has `count`, on empty land at least four
	// tiles from any other city.
	private void GiveCities(Player player, int count) {
		foreach (Tile tile in gameData.map.tiles) {
			if (player.cities.Count >= count) {
				return;
			}
			if (IsEmptyLand(tile) && gameData.cities.All(c => c.location.DistanceTo(tile) >= 4)) {
				CityInteractions.BuildCity(tile, player, $"Town {player.cities.Count}");
			}
		}
		throw new Exception($"No room for {count} cities");
	}

	private MapUnit Army(Tile tile, params string[] members) {
		MapUnit army = Spawn(us, "Army", tile);
		foreach (string member in members) {
			Join(Spawn(us, member, tile), army);
		}
		return army;
	}

	// Loads `unit` as the load order does: into what its tile offers, or
	// into `into` when there's a choice.
	private static void Join(MapUnit unit, MapUnit into = null) {
		new MsgLoadToTransport(unit.id, into?.id).process();
	}

	// An empty land tile next to `tile`, and the direction to it.
	private static (Tile, TileDirection) EmptyNeighbor(Tile tile) {
		(TileDirection direction, Tile neighbor) = tile.neighbors.First(n => IsEmptyLand(n.Value));
		return (neighbor, direction);
	}

	[Fact]
	public void ALeaderBuildsAnArmyInOurCityWhenWeHaveFourCitiesForIt() {
		MapUnit leader = Spawn(us, "Leader", home.location);
		GiveCities(us, 3);
		Assert.False(leader.CanBuildArmy());

		GiveCities(us, 4);
		Assert.Contains(UnitAction.BuildArmy, leader.GetAvailableActions());
		MapUnit army = leader.BuildArmy();

		Assert.True(army.IsArmy());
		Assert.Equal(us, army.owner);
		Assert.Equal(home.location, army.location);
		Assert.DoesNotContain(leader, us.units);
		Assert.Equal(1, us.ArmyCount());

		// A second army needs eight cities.
		MapUnit second = Spawn(us, "Leader", home.location);
		GiveCities(us, 7);
		Assert.False(second.CanBuildArmy());
		GiveCities(us, 8);
		Assert.True(second.CanBuildArmy());
	}

	[Fact]
	public void ALeaderBuildsArmiesOnlyInOurCitiesWithMovesLeft() {
		GiveCities(us, 4);
		City theirs = CityInteractions.BuildCity(them.units[0].location, them, "Theirs");
		(Tile field, _) = EmptyNeighbor(home.location);

		Assert.False(Spawn(us, "Leader", field).CanBuildArmy());
		Assert.False(Spawn(us, "Leader", theirs.location).CanBuildArmy());
		Assert.False(Spawn(us, "Swordsman", home.location).CanBuildArmy());
		MapUnit tired = Spawn(us, "Leader", home.location);
		tired.movementPoints.onConsumeAll();
		Assert.False(tired.CanBuildArmy());
		Assert.Null(tired.BuildArmy());
	}

	[Fact]
	public void UpToThreeUnitsJoinWithTheLoadOrderAndThePentagonMakesItFour() {
		MapUnit army = Spawn(us, "Army", home.location);
		MapUnit[] swordsmen = Enumerable.Range(0, 4).Select(_ => Spawn(us, "Swordsman", home.location)).ToArray();

		foreach (MapUnit swordsman in swordsmen.Take(3)) {
			Assert.Contains(UnitAction.Load, swordsman.GetAvailableActions());
			Join(swordsman);
		}
		Assert.Equal(swordsmen.Take(3), army.Passengers());
		Assert.All(swordsmen.Take(3), s => Assert.True(s.IsInArmy()));
		Assert.DoesNotContain(UnitAction.Load, swordsmen[3].GetAvailableActions());

		home.AddBuilding(gameData.Buildings.Find(b => b.name == "The Pentagon"));
		Assert.Equal(4, army.ArmyCapacity());
		Join(swordsmen[3]);
		Assert.Equal(4, army.Passengers().Count);
	}

	[Fact]
	public void OnlyOurLandUnitsThatCanAttackJoin() {
		MapUnit army = Spawn(us, "Army", home.location);

		foreach (string type in new[] { "Worker", "Catapult", "Leader", "Army", "Galley" }) {
			MapUnit unit = Spawn(us, type, home.location);
			Assert.False(army.CanTakeIntoArmy(unit), type);
		}
		Assert.False(army.CanTakeIntoArmy(Spawn(them, "Swordsman", home.location)));
		Assert.True(army.CanTakeIntoArmy(Spawn(us, "Warrior", home.location)));
	}

	[Fact]
	public async Task MovingOntoAnArmyDoesNotJoinIt() {
		(Tile field, TileDirection there) = EmptyNeighbor(home.location);
		MapUnit army = Army(field);
		MapUnit swordsman = Spawn(us, "Swordsman", home.location);

		Assert.True(await swordsman.Move(there));

		Assert.Equal(field, swordsman.location);
		Assert.False(swordsman.IsLoaded());
		Assert.Empty(army.Passengers());
	}

	[Fact]
	public void MembersPoolTheirHitPointsAndCantActAlone() {
		MapUnit army = Spawn(us, "Army", home.location);
		MapUnit regular = Spawn(us, "Warrior", home.location);
		MapUnit veteran = Spawn(us, "Warrior", home.location);
		SetExperience(veteran, 2);
		veteran.hitPointsRemaining = 2;
		Join(regular);
		Join(veteran);

		Assert.Equal(3 + 4, army.maxHitPoints);
		Assert.Equal(3 + 2, army.hitPointsRemaining);

		Assert.Empty(regular.GetAvailableActions());
		Assert.False(regular.CanBeActive());
		regular.OnBeginTurn();
		Assert.False(regular.movementPoints.canMove);
	}

	[Fact]
	public void MembersCantBeUpgraded() {
		// As UpgradeTest: a land unit of ours whose upgrade needs no resource,
		// with its tech, a Barracks and the gold.
		(UnitPrototype oldType, UnitPrototype newType) = gameData.unitPrototypes
			.Where(u => u.IsLandUnit() && u.attack > 0 && u.actions.Contains(UnitAction.Upgrade) && u.producibleBy.Contains(us.civilization))
			.SelectMany(u => u.upgradesTo.Select(t => (u, t)))
			.First(pair => pair.t.requiredResources.Count == 0 && pair.t.producibleBy.Contains(us.civilization));
		us.knownTechs.Add(newType.requiredTech.id);
		home.AddBuilding(gameData.Buildings.First(b => b.providesVeteranGroundUnits && !b.IsGreatWonder()));
		us.gold = 1000;
		MapUnit army = Spawn(us, "Army", home.location);
		MapUnit outside = gameData.SpawnUnit(us, oldType, home.location);
		MapUnit member = gameData.SpawnUnit(us, oldType, home.location);
		Join(member, army);

		Assert.NotNull(outside.UpgradeOffer());
		Assert.Null(member.UpgradeOffer());
		Assert.False(member.Upgrade());
	}

	[Fact]
	public async Task AnArmyMovesAsFastAsItsSlowestMemberPlusOneAndTakesItsMembers() {
		MapUnit army = Spawn(us, "Army", home.location);
		Assert.Equal(1, army.MovementPerTurn());
		Join(Spawn(us, "Horseman", home.location));
		Assert.Equal(3, army.MovementPerTurn());
		Join(Spawn(us, "Swordsman", home.location));
		Assert.Equal(2, army.MovementPerTurn());

		army.OnBeginTurn();
		Assert.Equal(2, army.movementPoints.remaining);
		(Tile field, TileDirection there) = EmptyNeighbor(home.location);
		Assert.True(await army.Move(there));

		Assert.All(army.Passengers(), m => Assert.Equal(field, m.location));
		Assert.Equal(2, field.unitsOnTile.Count(u => u.IsInArmy()));
		Assert.DoesNotContain(home.location.unitsOnTile, u => u.IsInArmy());
	}

	[Fact]
	public void AnArmyFightsWithItsStrongestMemberWhileItHasItsHitPoints() {
		MapUnit army = Army(home.location, "Warrior", "Swordsman", "Horseman");
		MapUnit[] members = army.Passengers().ToArray();
		MapUnit swordsman = members.First(m => m.unitType.name == "Swordsman");
		MapUnit horseman = members.First(m => m.unitType.name == "Horseman");
		MapUnit warrior = members.First(m => m.unitType.name == "Warrior");
		Assert.Equal(9, army.hitPointsRemaining);

		// Attack 3, then 2, then 1, each standing for three hit points.
		Assert.Equal([swordsman, swordsman, swordsman, horseman, horseman, horseman, warrior, warrior, warrior],
			Enumerable.Range(1, 9).Reverse().Select(hp => { army.hitPointsRemaining = hp; return army.ArmyFighter(CombatRole.Attack); }));
		army.hitPointsRemaining = 9;
		Assert.Equal(3, army.CombatType(CombatRole.Attack).attack);
		Assert.True(army.IsCombatUnit());
		Assert.True(army.CanTakeCities());

		// The same members defend: the Swordsman's defense of 2 comes first.
		Assert.Equal(2, army.CombatType(CombatRole.Defense).defense);
	}

	// Conquests (Arathorn, CivFanatics; the Steam guide "The Army unit"): an
	// army's bonus is its members' total attack (or defense) over 6, or over
	// 4 with the Military Academy, rounded down.
	[Fact]
	public void ArmiesFightWithABonusOfTheirMembersTotal() {
		MapUnit swordsmen = Army(home.location, "Swordsman", "Swordsman", "Swordsman");
		Assert.Equal(3 + 9 / 6, swordsmen.BaseCombatStrength(CombatRole.Attack));
		Assert.Equal(2 + 6 / 6, swordsmen.BaseCombatStrength(CombatRole.Defense));
		Assert.Equal(swordsmen.BaseCombatStrength(CombatRole.Attack) * StrengthBonus.ListToMultiplier(swordsmen.ListStrengthBonusesVersus(null, CombatRole.Attack, null)),
			swordsmen.StrengthVersus(null, CombatRole.Attack, null));

		MapUnit horsemen = Army(home.location, "Horseman", "Horseman");
		Assert.Equal(2, horsemen.BaseCombatStrength(CombatRole.Attack));

		home.AddBuilding(gameData.Buildings.Find(b => b.name == "Military Academy"));
		Assert.Equal(3 + 9 / 4, swordsmen.BaseCombatStrength(CombatRole.Attack));
		Assert.Equal(2 + 6 / 4, swordsmen.BaseCombatStrength(CombatRole.Defense));
		Assert.Equal(2 + 4 / 4, horsemen.BaseCombatStrength(CombatRole.Attack));

		// Ordinary units fight with their own strength.
		Assert.Equal(3, Spawn(us, "Swordsman", home.location).BaseCombatStrength(CombatRole.Attack));
	}

	[Fact]
	public void AnEmptyArmyCantFightOrTakeCities() {
		MapUnit army = Spawn(us, "Army", home.location);

		Assert.False(army.IsCombatUnit());
		Assert.False(army.CanDefendItself());
		Assert.False(army.CanTakeCities());
		Assert.Equal(0, army.CombatType(CombatRole.Attack).attack);
	}

	[Fact]
	public void MembersNeverDefendOrGetBombardedAlone() {
		(Tile field, _) = EmptyNeighbor(home.location);
		MapUnit army = Army(field, "Swordsman", "Swordsman");
		MapUnit enemy = Spawn(them, "Swordsman", home.location);
		Assert.Equal(army, field.FindTopDefender(enemy));
		Assert.Equal(army, field.FindTopDefenderForBombard(enemy));

		// Even when a fresh member alone would outrank the battered army.
		army.hitPointsRemaining = 1;
		Assert.Equal(army, field.FindTopDefender(enemy));
		// A bombard that can't kill skips the army on its last hit point, and
		// so its members.
		Assert.Equal(MapUnit.NONE, field.FindTopDefenderForBombard(enemy));
	}

	[Fact]
	public async Task AnArmyDiesWhenItsPoolIsEmptyAndItsMembersWithIt() {
		(Tile field, TileDirection there) = EmptyNeighbor(home.location);
		MapUnit army = Army(field, "Warrior", "Warrior", "Warrior");
		MapUnit[] members = army.Passengers().ToArray();
		Type("Knight").attack = 1000;
		MapUnit knight = Spawn(them, "Knight", home.location);

		Assert.Equal(CombatResult.DefenderKilled, await knight.Fight(army));

		Assert.DoesNotContain(army, us.units);
		Assert.All(members, m => Assert.DoesNotContain(m, us.units));
		Assert.All(members, m => Assert.DoesNotContain(m, gameData.mapUnits));
		Assert.Empty(field.unitsOnTile);
	}

	[Fact]
	public async Task AWinningArmyIsVictoriousAndItsFighterIsPromotedWithinThePool() {
		Type("Swordsman").attack = 1000;
		(Tile field, _) = EmptyNeighbor(home.location);
		MapUnit army = Army(home.location, "Swordsman", "Warrior");
		Assert.False(us.hasVictoriousArmy);

		int promotions = 0;
		for (int i = 0; i < 100; i++) {
			int levelsBefore = army.Passengers().Sum(m => gameData.experienceLevels.IndexOf(m.experienceLevel));
			int hitPointsBefore = army.hitPointsRemaining;
			Assert.Equal(CombatResult.DefenderKilled, await army.Fight(Spawn(them, "Warrior", field)));
			int gained = army.Passengers().Sum(m => gameData.experienceLevels.IndexOf(m.experienceLevel)) - levelsBefore;
			promotions += gained;
			Assert.Equal(hitPointsBefore + gained, army.hitPointsRemaining);
		}

		Assert.True(us.hasVictoriousArmy);
		Assert.True(promotions > 0);
		Assert.Equal(army.maxHitPoints, army.hitPointsRemaining);
		Assert.Null(us.units.FirstOrDefault(u => u.unitType.isLeader));
	}

	[Fact]
	public void TheMilitaryAcademyBuildsArmiesWhileWeHaveTheCitiesForThem() {
		UnitPrototype armyType = Type("Army");
		HashSet<Resource> resources = home.GetAccessibleResources(gameData);
		GiveCities(us, 4);
		Assert.False(armyType.CanProduce(home, resources));

		home.AddBuilding(gameData.Buildings.Find(b => b.name == "Military Academy"));
		Assert.True(armyType.CanProduce(home, resources));

		// Finished while we lack the cities for it, it waits with its shields.
		home.SetItemBeingProduced(armyType);
		home.SetStoredShields(us.ShieldCost(armyType));
		Spawn(us, "Army", home.location);
		Assert.False(armyType.CanProduce(home, resources));
		Assert.Null(home.ComputeTurnProduction());
		Assert.Equal(us.ShieldCost(armyType), home.shieldsStored);

		GiveCities(us, 8);
		Assert.Equal(armyType, home.ComputeTurnProduction());
	}

	[Fact]
	public void TheAcademyAndTheHeroicEpicNeedAVictoriousArmy() {
		HashSet<Resource> resources = home.GetAccessibleResources(gameData);
		foreach (string name in new[] { "Military Academy", "Heroic Epic" }) {
			Building building = gameData.Buildings.Find(b => b.name == name);
			if (building.requiredTech != null) {
				us.knownTechs.Add(building.requiredTech.id);
			}
			us.hasVictoriousArmy = false;
			Assert.False(building.CanProduce(home, resources), name);
			us.hasVictoriousArmy = true;
			Assert.True(building.CanProduce(home, resources), name);
		}
	}

	[Fact]
	public async Task AnArmyOnAShipTakesAPlaceForEachMemberAndSailsWithThem() {
		Tile coast = gameData.map.tiles.First(t => IsEmptyLand(t) && gameData.cities.All(c => c.location.DistanceTo(t) >= 4)
			&& t.neighbors.Values.Any(n => Tile.IsTileValid(n) && n.IsWater() && n.unitsOnTile.Count == 0));
		City port = CityInteractions.BuildCity(coast, us, "Port");
		MapUnit galley = Spawn(us, "Galley", port.location);
		MapUnit army = Army(port.location, "Swordsman");
		MapUnit swordsman = army.Passengers().Single();
		Assert.Equal(2, galley.FreeCapacity());

		// Two transports here, so the order asks; pick the galley.
		new MsgLoadToTransport(army.id, galley.id).process();
		Assert.True(army.IsLoadedIn(galley));
		Assert.Equal(0, galley.FreeCapacity());

		TileDirection toSea = port.location.neighbors.First(n => Tile.IsTileValid(n.Value) && n.Value.IsWater() && n.Value.unitsOnTile.Count == 0).Key;
		Assert.True(await galley.Move(toSea));
		Tile sea = galley.location;
		Assert.Equal(sea, army.location);
		Assert.Equal(sea, swordsman.location);
		Assert.True(swordsman.IsInArmy());

		// Sunk at sea, the galley takes them down.
		galley.RemoveFromPlay();
		Assert.DoesNotContain(army, us.units);
		Assert.DoesNotContain(swordsman, us.units);
		Assert.Empty(sea.unitsOnTile);
	}

	[Fact]
	public void AShipRemovedInPortSetsItsCargoDown() {
		MapUnit galley = Spawn(us, "Galley", home.location);
		MapUnit warrior = Spawn(us, "Warrior", home.location);
		new MsgLoadToTransport(warrior.id, galley.id).process();
		Assert.True(warrior.IsLoadedIn(galley));

		galley.RemoveFromPlay();

		Assert.Contains(warrior, us.units);
		Assert.False(warrior.IsLoaded());
	}

	[Fact]
	public void ArmiesAndTheirMembersAreSaved() {
		MapUnit army = Army(home.location, "Swordsman", "Horseman");
		army.hitPointsRemaining = 4;

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);

		MapUnit loaded = reloaded.mapUnits.Find(u => u.id == army.id);
		EngineStorage.InitializeGameDataForTests(reloaded);
		Assert.Equal(["Horseman", "Swordsman"], loaded.Passengers().Select(m => m.unitType.name).Order());
		Assert.All(loaded.Passengers(), m => Assert.True(m.IsInArmy()));
		Assert.Equal(4, loaded.hitPointsRemaining);
		Assert.Equal(6, loaded.maxHitPoints);
	}
	[Fact]
	public void AMemberRemovedFromAnArmyTakesItsHitPointsWithIt() {
		MapUnit army = Army(home.location, "Swordsman", "Swordsman");
		army.hitPointsRemaining = army.maxHitPoints;
		int full = army.hitPointsRemaining;
		MapUnit member = army.Passengers()[0];
		int memberHitPoints = member.maxHitPoints;

		gameData.RemoveUnit(member);

		Assert.Single(army.Passengers());
		Assert.Equal(full - memberHitPoints, army.maxHitPoints);
		Assert.Equal(army.maxHitPoints, army.hitPointsRemaining);
	}

	[Fact]
	public void ABrokeCivDisbandsWholeUnitsNeverAnArmyMemberAlone() {
		// Eight full armies, so most units are army members, far more units
		// than the free ones, and no gold to pay for them.
		List<MapUnit> armies = Enumerable.Range(0, 8).Select(_ => Army(home.location, "Swordsman", "Swordsman", "Swordsman")).ToList();
		int units = us.units.Count;
		us.gold = 0;
		us.taxRate = 10;
		us.scienceRate = 0;
		us.luxuryRate = 0;

		us.DoPerTurnFinanceUpdates(gameData);

		Assert.True(us.units.Count < units);
		Assert.All(armies.Where(gameData.mapUnits.Contains), army => {
			Assert.Equal(3, army.Passengers().Count);
			Assert.True(army.hitPointsRemaining <= army.maxHitPoints);
		});
	}
}

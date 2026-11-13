using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

public class CombatTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;

	public CombatTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;

		Player[] civs = gameData.players.Where(p => !p.isBarbarians && !p.isHuman).Take(2).ToArray();
		us = civs[0];
		them = civs[1];
	}

	// Finds two neighboring empty land tiles, outside anyone's borders.
	// Flat land: no hills or mountains, which defend better and cost more
	// moves to enter.
	private static bool IsFlat(Tile t) {
		return t.overlayTerrainType.defenseBonus.amount < 0.5 && t.overlayTerrainType.movementCost <= 1;
	}

	private (Tile from, TileDirection dir, Tile to) FindAdjacentLand() {
		foreach (Tile from in gameData.map.tiles.Where(t => IsEmptyLand(t) && IsFlat(t) && t.OwningPlayer() == null)) {
			foreach ((TileDirection dir, Tile to) in from.neighbors) {
				if (to != Tile.NONE && IsEmptyLand(to) && IsFlat(to) && to.OwningPlayer() == null) {
					return (from, dir, to);
				}
			}
		}
		throw new System.Exception("No adjacent land tiles found");
	}

	// Finds three empty land tiles in a line, outside anyone's borders.
	private (Tile from, TileDirection dir, Tile to, Tile behind) FindLandInALine() {
		foreach (Tile from in gameData.map.tiles.Where(t => IsEmptyLand(t) && t.OwningPlayer() == null)) {
			foreach ((TileDirection dir, Tile to) in from.neighbors) {
				if (to == Tile.NONE || !IsEmptyLand(to) || to.OwningPlayer() != null) {
					continue;
				}
				if (to.neighbors.TryGetValue(dir, out Tile behind) && behind != Tile.NONE
					&& IsEmptyLand(behind) && behind.OwningPlayer() == null) {
					return (from, dir, to, behind);
				}
			}
		}
		throw new System.Exception("No line of land tiles found");
	}

	// Makes every random roll come out as 0, so attackers win every round
	// and every retreat roll succeeds.
	private class ZeroRandom : System.Random {
		protected override double Sample() => 0.0;
	}

	private async Task WithZeroRandom(System.Func<Task> action) {
		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = new ZeroRandom();
		try {
			await action();
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	private void GoToWar() {
		us.DeclareWarOn(them, gameData.turn);
	}

	private UnitPrototype Prototype(string name) {
		return gameData.unitPrototypes.Single(p => p.name == name);
	}

	private MapUnit Spawn(Player owner, string prototype, Tile tile) {
		gameData.SpawnUnit(owner, Prototype(prototype), tile);
		return tile.unitsOnTile.Last();
	}

	private static bool IsEmptyLand(Tile t) {
		return t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	// A unit with a move left can board a transport on a neighbouring tile:
	// it steps onto the transport's tile, which boards it.
	[Fact]
	public void AUnitBoardsATransportOnANeighbouringTile() {
		(Tile land, Tile sea) = FindCoast();
		MapUnit galley = Spawn(us, "Galley", sea);
		MapUnit warrior = Spawn(us, "Warrior", land);
		EngineStorage.pendingMessages.Clear();
		EngineStorage.activePlayerID = us.id;
		try {
			new MsgLoadToTransport(warrior.id, galley.id) { playerID = us.id }.send();
			while (EngineStorage.HasPendingMessagesToEngine()) {
				EngineStorage.ProcessNextMessageToEngine();
			}
		} finally {
			EngineStorage.activePlayerID = null;
		}

		Assert.Equal(sea, warrior.location);
		Assert.Equal(galley.id, warrior.loadedOnUnitId);
	}

	// A unit with no moves left can't.
	[Fact]
	public void AUnitWithNoMovesLeftCantBoardANeighbouringTransport() {
		(Tile land, Tile sea) = FindCoast();
		MapUnit galley = Spawn(us, "Galley", sea);
		MapUnit warrior = Spawn(us, "Warrior", land);
		warrior.movementPoints.onUnitMove(warrior.movementPoints.remaining);
		EngineStorage.pendingMessages.Clear();
		EngineStorage.activePlayerID = us.id;
		try {
			new MsgLoadToTransport(warrior.id, galley.id) { playerID = us.id }.send();
			while (EngineStorage.HasPendingMessagesToEngine()) {
				EngineStorage.ProcessNextMessageToEngine();
			}
		} finally {
			EngineStorage.activePlayerID = null;
		}

		Assert.Equal(land, warrior.location);
		Assert.Null(warrior.loadedOnUnitId);
	}

	// An empty land tile outside anyone's borders, next to empty coastal sea.
	private (Tile land, Tile sea) FindCoast() {
		foreach (Tile land in gameData.map.tiles.Where(t => IsEmptyLand(t) && t.OwningPlayer() == null)) {
			foreach (Tile sea in land.neighbors.Values) {
				if (sea != Tile.NONE && sea.IsWater() && !sea.isFreshWater && sea.unitsOnTile.Count == 0 && sea.OwningPlayer() == null) {
					return (land, sea);
				}
			}
		}
		throw new System.Exception("No coast found");
	}

	[Fact]
	public void CargoSinksWithItsTransport() {
		Tile sea = gameData.map.tiles.First(t => t.IsWater() && !t.isFreshWater && t.unitsOnTile.Count == 0);
		MapUnit galley = Spawn(us, "Galley", sea);
		MapUnit warrior = Spawn(us, "Warrior", sea);
		warrior.loadedOnUnitId = galley.id;

		gameData.RemoveUnit(galley);

		Assert.Empty(sea.unitsOnTile);
		Assert.DoesNotContain(warrior, gameData.mapUnits);
		Assert.DoesNotContain(warrior, us.units);
	}

	[Fact]
	public async Task WorkersAreCapturedNotFought() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit worker = Spawn(them, "Worker", to);
		ExperienceLevel startingLevel = warrior.experienceLevel;
		int startingHitPoints = warrior.hitPointsRemaining;

		Assert.True(await warrior.Move(dir));

		Assert.Equal(to, warrior.location);
		Assert.Equal(us, worker.owner);
		Assert.Contains(worker, us.units);
		Assert.DoesNotContain(worker, them.units);
		Assert.True(worker.IsCaptive());
		Assert.True(worker.movementPoints.canMove);
		Assert.Equal(startingLevel, warrior.experienceLevel);
		Assert.Equal(startingHitPoints, warrior.hitPointsRemaining);
	}

	[Fact]
	public async Task BeatingTheLastDefenderCapturesTheWorkersBehindIt() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit attacker = Spawn(us, "Warrior", from);
		MapUnit defender = Spawn(them, "Warrior", to);
		MapUnit worker = Spawn(them, "Worker", to);

		await WithZeroRandom(async () => Assert.True(await attacker.Move(dir)));

		Assert.DoesNotContain(defender, gameData.mapUnits);
		Assert.Equal(to, attacker.location);
		Assert.Equal(us, worker.owner);
	}

	[Fact]
	public async Task WorkersCanBeCapturedAfterAttacking() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit worker = Spawn(them, "Worker", to);
		warrior.hasAttackedThisTurn = true;

		Assert.True(await warrior.Move(dir));

		Assert.Equal(to, warrior.location);
		Assert.Equal(us, worker.owner);
	}

	// Capturing the last settler of a civ with no cities destroys the civ,
	// which removes its other units, among them a worker on the same tile:
	// that one mustn't then be captured too, which would leave the captor a
	// unit on no tile.
	[Fact]
	public async Task NoWorkerIsCapturedAfterItsCivIsDestroyed() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		Spawn(them, "Settler", to);
		Spawn(them, "Worker", to);
		foreach (MapUnit u in them.units.Where(u => u.location != to).ToList()) {
			gameData.RemoveUnit(u);
		}
		them.cities.Clear();

		Assert.True(await warrior.Move(dir));

		Assert.True(them.defeated);
		Assert.All(us.units, u => Assert.Contains(u, u.location.unitsOnTile));
	}

	private static List<MsgShowMilitaryAdvisorPopup> MilitaryNews() {
		List<MsgShowMilitaryAdvisorPopup> news = new();
		while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI msg)) {
			if (msg is MsgShowMilitaryAdvisorPopup n) {
				news.Add(n);
			}
		}
		return news;
	}

	// Civ3's UNITCAPTURED (script.txt), told where it happened, so the
	// map is centred there as it's shown (see LossNews).
	[Fact]
	public async Task APlayerIsToldTheirUnitWasCapturedWhereItWas() {
		GoToWar();
		them.isHuman = true;
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		Spawn(them, "Worker", to);
		MilitaryNews();

		Assert.True(await warrior.Move(dir));

		MsgShowMilitaryAdvisorPopup news = Assert.Single(MilitaryNews(), n => n.recipient == them);
		Assert.Equal("Our Worker was captured!", news.message);
		Assert.Equal(to, news.location);
		Assert.False(news.happy);
	}

	// UNITSCAPTURED, for more than one.
	[Fact]
	public async Task SeveralUnitsCapturedAreToldTogether() {
		GoToWar();
		them.isHuman = true;
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		Spawn(them, "Worker", to);
		Spawn(them, "Settler", to);
		MilitaryNews();

		Assert.True(await warrior.Move(dir));

		MsgShowMilitaryAdvisorPopup news = Assert.Single(MilitaryNews(), n => n.recipient == them);
		Assert.Equal("Our units were captured!", news.message);
		Assert.Equal(to, news.location);
	}

	[Fact]
	public async Task CapturedSettlerBecomesTwoSlaveWorkers() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit settler = Spawn(them, "Settler", to);

		Assert.True(await warrior.Move(dir));

		Assert.DoesNotContain(settler, gameData.mapUnits);
		Assert.DoesNotContain(settler, them.units);
		List<MapUnit> slaves = to.unitsOnTile.Where(u => u.unitType.isWorker).ToList();
		Assert.Equal(2, slaves.Count);
		Assert.All(slaves, s => {
			Assert.Equal(us, s.owner);
			Assert.Contains(s, us.units);
			Assert.True(s.IsCaptive());
			Assert.True(s.movementPoints.canMove);
			Assert.Equal(them.civilization.name, s.nationality.name);
		});
	}

	// Civ3 captures an undefended zero-defence unit if the captor could
	// build it, and destroys it otherwise.
	// https://forums.civfanatics.com/threads/capturing-units-with-zero-defense.631539/
	[Fact]
	public async Task ACatapultIsCapturedByACivThatCanBuildIt() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit catapult = Spawn(them, "Catapult", to);
		us.knownTechs.Add(catapult.unitType.requiredTech.id);

		Assert.True(await warrior.Move(dir));

		Assert.Equal(to, warrior.location);
		Assert.Equal(us, catapult.owner);
		Assert.Contains(catapult, us.units);
		Assert.DoesNotContain(catapult, them.units);
		Assert.Contains(catapult, to.unitsOnTile);
	}

	[Fact]
	public async Task ACatapultIsDestroyedByACivThatCantBuildIt() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit catapult = Spawn(them, "Catapult", to);
		us.knownTechs.Remove(catapult.unitType.requiredTech.id);

		Assert.True(await warrior.Move(dir));

		Assert.Equal(to, warrior.location);
		Assert.DoesNotContain(catapult, gameData.mapUnits);
		Assert.DoesNotContain(catapult, us.units);
		Assert.DoesNotContain(catapult, to.unitsOnTile);
	}

	// Only the best single building defense bonus counts, and walls only in
	// a town (C3X's patch_City_get_building_defense_bonus,
	// https://github.com/maxpetul/C3X/blob/master/injected_code.c, which
	// keeps the largest bonus and skips buildings with a bombard defense
	// above "MaximumSize_Town").
	[Fact]
	public void WallsAndCivilDefenseDontAddUpAndWallsStopAtCitySize() {
		(Tile from, _, Tile to) = FindAdjacentLand();
		City city = CityInteractions.BuildCity(to, them, them.GetNextCityName());
		city.AddBuilding(gameData.Buildings.Single(b => b.name == "Walls"));
		city.AddBuilding(gameData.Buildings.Single(b => b.name == "Civil Defense"));
		MapUnit warrior = Spawn(us, "Warrior", from);
		static double BuildingBonuses(City c, MapUnit attacker) =>
			c.GetDefenseBonuses(attacker).Where(b => b.description is "Walls" or "Civil Defense").Sum(b => b.amount);

		Assert.Equal(0.5, BuildingBonuses(city, warrior), 6);

		city.RemoveBuilding(city.constructed_buildings.Single(cb => cb.building.name == "Civil Defense"));
		Assert.Equal(0.5, BuildingBonuses(city, warrior), 6);
		while (city.residents.Count <= gameData.rules.MaximumLevel1CitySize) {
			city.residents.Add(new CityResident() { city = city });
		}
		Assert.Equal(0.0, BuildingBonuses(city, warrior), 6);
	}

	// City Walls give "+50% to defensive bonus against land attacks", a
	// Coastal Fortress "+50% against naval attacks"
	// (https://civfanatics.com/civ3/civilopedia/improvements/).
	[Fact]
	public void WallsDefendAgainstLandAttacksAndCoastalFortressesAgainstShips() {
		(Tile land, Tile sea) = FindCoast();
		City city = CityInteractions.BuildCity(land, them, them.GetNextCityName());
		Building walls = gameData.Buildings.Single(b => b.name == "Walls");
		Building fortress = gameData.Buildings.Single(b => b.name == "Coastal Fortress");
		city.AddBuilding(walls);
		city.AddBuilding(fortress);
		MapUnit galley = Spawn(us, "Galley", sea);
		(Tile from, _, _) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);

		List<string> versusShip = city.GetDefenseBonuses(galley).Select(b => b.description).ToList();
		List<string> versusLand = city.GetDefenseBonuses(warrior).Select(b => b.description).ToList();

		Assert.Contains("Coastal Fortress", versusShip);
		Assert.DoesNotContain("Walls", versusShip);
		Assert.Contains("Walls", versusLand);
		Assert.DoesNotContain("Coastal Fortress", versusLand);
		Assert.Equal(0.5, city.GetDefenseBonuses(galley).Single(b => b.description == "Coastal Fortress").amount);
	}

	// "When a land tile with a bombard capable unit in it is attacked, that
	// bombard unit will take one free shot at the attacking unit" (Civ3
	// readme, "Artillery Free Shot"). The bombarder must be of the
	// attacker's class ("attacker_class == candidate_type->Unit_Class",
	// C3X's re-creation of the stock Fighter_find_defensive_bombarder,
	// https://github.com/maxpetul/C3X/blob/master/injected_code.c), so a
	// ship fires it at an attacking ship on a sea tile too.
	[Fact]
	public async Task DefensiveBombardDefendsLandTilesAndShipsAgainstShips() {
		GoToWar();
		(Tile from, _, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit spearman = Spawn(them, "Spearman", to);
		MapUnit catapult = Spawn(them, "Catapult", to);
		catapult.defensiveBombardsRemaining = 1;
		await WithZeroRandom(async () => await warrior.Fight(spearman));
		Assert.Equal(0, catapult.defensiveBombardsRemaining);

		(Tile seaFrom, Tile seaTo) = gameData.map.tiles
			.Where(t => t.IsWater() && t.unitsOnTile.Count == 0)
			.SelectMany(t => t.neighbors.Values.Where(n => n != Tile.NONE && n.IsWater() && n.unitsOnTile.Count == 0).Select(n => (t, n)))
			.First();
		MapUnit ours = Spawn(us, "Frigate", seaFrom);
		MapUnit theirs = Spawn(them, "Frigate", seaTo);
		MapUnit escort = Spawn(them, "Frigate", seaTo);
		escort.defensiveBombardsRemaining = 1;
		await WithZeroRandom(async () => await ours.Fight(theirs));
		Assert.Equal(0, escort.defensiveBombardsRemaining);
	}

	// Each attack draws at most one shot, from the bombarder with the
	// highest bombard strength; another attack can draw the next one. None
	// is fired at an attacker that "cannot defend" (defence below 1) or that
	// has one hit point left (C3X's re-creation of the stock
	// Fighter_find_defensive_bombarder,
	// https://github.com/maxpetul/C3X/blob/master/injected_code.c).
	[Fact]
	public async Task EachAttackDrawsOneDefensiveBombardFromTheStrongest() {
		GoToWar();
		(Tile from, _, Tile to) = FindAdjacentLand();
		MapUnit spearman = Spawn(them, "Spearman", to);
		MapUnit catapult = Spawn(them, "Catapult", to);
		MapUnit cannon = Spawn(them, "Cannon", to);
		Assert.True(Prototype("Cannon").bombard > Prototype("Catapult").bombard);
		catapult.defensiveBombardsRemaining = 1;
		cannon.defensiveBombardsRemaining = 1;

		// The attackers lose every round, so the spearman survives for the
		// next attack.
		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = new AlmostOneRandom();
		try {
			MapUnit first = Spawn(us, "Warrior", from);
			await first.Fight(spearman);
			Assert.Equal(1, catapult.defensiveBombardsRemaining);
			Assert.Equal(0, cannon.defensiveBombardsRemaining);

			// No shot at an attacker with one hit point left.
			MapUnit weak = Spawn(us, "Warrior", from);
			weak.hitPointsRemaining = 1;
			await weak.Fight(spearman);
			Assert.Equal(1, catapult.defensiveBombardsRemaining);

			// Nor at one that can't defend.
			int defence = Prototype("Warrior").defense;
			Prototype("Warrior").defense = 0;
			MapUnit defenceless = Spawn(us, "Warrior", from);
			await defenceless.Fight(spearman);
			Assert.Equal(1, catapult.defensiveBombardsRemaining);
			Prototype("Warrior").defense = defence;

			MapUnit second = Spawn(us, "Warrior", from);
			await second.Fight(spearman);
			Assert.Equal(0, catapult.defensiveBombardsRemaining);
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	// Makes every random roll come out just under 1, so attackers lose
	// every round and every roll against a chance fails.
	private class AlmostOneRandom : System.Random {
		protected override double Sample() => 0.999999;
	}

	// In the base game aircraft don't fire defensive bombard, nor do ships
	// in port at land attackers: the C3X mod offers "aerial: Air units may
	// perform defensive bombard" and "docked-vs-land: Docked ships (i.e. in
	// a city) can perform defensive bombard against land units attacking
	// their city" as changes
	// (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
	[Fact]
	public async Task AircraftAndShipsInPortDontFireDefensiveBombardAtLandUnits() {
		GoToWar();
		(Tile from, _, Tile to) = FindAdjacentLand();
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit spearman = Spawn(them, "Spearman", to);
		MapUnit bomber = Spawn(them, "Bomber", to);
		MapUnit frigate = Spawn(them, "Frigate", to);
		bomber.defensiveBombardsRemaining = 1;
		frigate.defensiveBombardsRemaining = 1;
		int hitPoints = warrior.hitPointsRemaining;

		await WithZeroRandom(async () => await warrior.Fight(spearman));

		Assert.Equal(1, bomber.defensiveBombardsRemaining);
		Assert.Equal(1, frigate.defensiveBombardsRemaining);
		// Not hit (winning may promote it, adding one).
		Assert.True(warrior.hitPointsRemaining >= hitPoints);
	}

	// "Cities with resisters do not give defensive bonuses" (Conquests
	// Civilopedia, GCON_Terrain_Combat).
	[Fact]
	public void ACityInResistanceGivesNoDefenseBonuses() {
		(Tile from, _, _) = FindAdjacentLand();
		(Tile land, _) = FindCoast();
		City city = CityInteractions.BuildCity(land, them, them.GetNextCityName());
		city.AddBuilding(gameData.Buildings.Single(b => b.name == "Walls"));
		MapUnit warrior = Spawn(us, "Warrior", from);
		Assert.NotEmpty(city.GetDefenseBonuses(warrior));

		city.resisters = 1;
		Assert.Empty(city.GetDefenseBonuses(warrior));
	}

	// The map asks which unit of a stack would defend against the selected
	// unit, and with none selected that's MapUnit.NONE, which has no unit
	// type. Asking whether it was a ship threw, so the map's units stopped
	// being drawn on any city with two or more units.
	[Fact]
	public void ACityDefendsAgainstNoUnitAsAgainstALandAttack() {
		(Tile land, _) = FindCoast();
		City city = CityInteractions.BuildCity(land, them, them.GetNextCityName());
		city.AddBuilding(gameData.Buildings.Single(b => b.name == "Walls"));
		List<string> bonuses = city.GetDefenseBonuses(MapUnit.NONE).Select(b => b.description).ToList();
		Assert.Contains("Walls", bonuses);
	}

	// "There is no defensive bonus for fortified ships"
	// (https://forums.civfanatics.com/threads/do-ships-get-a-defense-bonus-when-fortified.95400/).
	[Fact]
	public void FortifiedShipsGetNoFortificationBonus() {
		(Tile land, Tile sea) = FindCoast();
		MapUnit galley = Spawn(us, "Galley", sea);
		MapUnit warrior = Spawn(us, "Warrior", land);
		MapUnit attacker = Spawn(them, "Warrior", land);
		galley.isFortified = true;
		warrior.isFortified = true;

		string fortified = gameData.fortificationBonus.description;
		Assert.DoesNotContain(galley.ListStrengthBonusesVersus(attacker, CombatRole.Defense, null), b => b.description == fortified);
		Assert.Contains(warrior.ListStrengthBonusesVersus(attacker, CombatRole.Defense, null), b => b.description == fortified);
	}

	// "the fortified bonus requires the unit to be fortified *and have
	// movement points remaining*" (Perez's decompile, 3.2; owner's ruling
	// 2026-11-12): a unit that fortified after spending its moves gets it
	// only once its moves come back as the next turn begins.
	[Fact]
	public void TheFortificationBonusNeedsMovesLeft() {
		(Tile land, _) = FindCoast();
		MapUnit warrior = Spawn(us, "Warrior", land);
		MapUnit attacker = Spawn(them, "Warrior", land);
		warrior.movementPoints.skipTurn();
		warrior.Fortify();

		string fortified = gameData.fortificationBonus.description;
		Assert.DoesNotContain(warrior.ListStrengthBonusesVersus(attacker, CombatRole.Defense, null), b => b.description == fortified);

		warrior.OnBeginTurn();
		Assert.True(warrior.isFortified);
		Assert.Contains(warrior.ListStrengthBonusesVersus(attacker, CombatRole.Defense, null), b => b.description == fortified);
	}

	// Units like the Javelin Thrower have "a 33% chance" to make a unit
	// (a Worker for it) of a foe they beat attacking
	// (https://forums.civfanatics.com/threads/anyone-know-exactly-how-enslavement-works.107253/).
	[Fact]
	public async Task AJavelinThrowerCanEnslaveTheUnitItBeats() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit javelin = Spawn(us, "Javelin Thrower", from);
		MapUnit warrior = Spawn(them, "Warrior", to);
		warrior.hitPointsRemaining = 1;

		// Every roll is 0, under the 1 in 3 chance.
		await WithZeroRandom(async () => Assert.True(await javelin.Move(dir)));

		Assert.DoesNotContain(warrior, gameData.mapUnits);
		MapUnit slave = Assert.Single(us.units, u => u.unitType.name == "Worker" && u.IsCaptive());
		Assert.Equal(from, slave.location);
		Assert.True(slave.IsCaptive());
		Assert.Same(them.civilization, slave.nationality);
	}

	// A kill by bombardment can enslave: C3X offers, as a change to the base
	// game, an option that "Stops units from enslaving a target they've
	// destroyed by bombarding as opposed to direct combat"
	// (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
	[Fact]
	public async Task ABombardmentKillCanEnslave() {
		GoToWar();
		(Tile from, _, Tile to) = FindAdjacentLand();
		Prototype("Artillery").isLandBombardmentLethal = true;
		Prototype("Artillery").enslavesTo = "Worker";
		MapUnit artillery = Spawn(us, "Artillery", from);
		MapUnit warrior = Spawn(them, "Warrior", to);
		warrior.hitPointsRemaining = 1;
		Assert.True(artillery.CanBombardTile(to));

		await WithZeroRandom(async () => await artillery.Bombard(to));

		Assert.DoesNotContain(warrior, gameData.mapUnits);
		MapUnit slave = Assert.Single(us.units, u => u.unitType.name == "Worker" && u.IsCaptive());
		Assert.Same(them.civilization, slave.nationality);
	}

	// A bombardment costs one move, but bombarding a barricade ends the
	// unit's turn (see MapUnit.Bombard).
	[Theory]
	[InlineData(false, 2)]
	[InlineData(true, 0)]
	public async Task BombardingABarricadeEndsTheTurn(bool barricade, float movesLeft) {
		GoToWar();
		(Tile from, _, Tile to) = FindAdjacentLand();
		MapUnit artillery = Spawn(us, "Artillery", from);
		artillery.movementPoints.reset(3);
		Spawn(them, "Warrior", to);
		if (barricade) {
			to.overlays.Add(gameData.terrainImprovements.Find(ti => ti.key == C7GameData.Tile.TileOverlays.BARRICADE));
		}

		await WithZeroRandom(async () => await artillery.Bombard(to));
		Assert.Equal(movesLeft, artillery.movementPoints.remaining);
	}

	private class FixedRandom(double value) : System.Random {
		protected override double Sample() => value;
	}

	// The roll is out of 100 and succeeds below 33 ("lim is 100, enslaving
	// happens if the return value is < 33", C3X patch_rand_int_to_enslave).
	[Fact]
	public void EnslavementSucceedsBelow33OutOf100() {
		(Tile from, _, Tile to) = FindAdjacentLand();
		MapUnit javelin = Spawn(us, "Javelin Thrower", from);
		MapUnit warrior = Spawn(them, "Warrior", to);
		System.Random original = C7GameData.GameData.rng;
		try {
			// 33 out of 100: no slave, though it's under a third.
			C7GameData.GameData.rng = new FixedRandom(0.332);
			Assert.Null(javelin.RollToEnslave(warrior));
			C7GameData.GameData.rng = new FixedRandom(0.325);
			Assert.NotNull(javelin.RollToEnslave(warrior));
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	// "Barbarian units are not able to enslave other units"
	// (https://forums.civfanatics.com/threads/allowing-barbarians-to-enslave-units.677365/).
	[Fact]
	public void BarbariansDontEnslave() {
		Player barbarians = gameData.players.Single(p => p.isBarbarians);
		(Tile from, _, Tile to) = FindAdjacentLand();
		MapUnit javelin = Spawn(barbarians, "Javelin Thrower", from);
		MapUnit warrior = Spawn(them, "Warrior", to);
		System.Random original = C7GameData.GameData.rng;
		try {
			C7GameData.GameData.rng = new FixedRandom(0);
			Assert.Null(javelin.RollToEnslave(warrior));
		} finally {
			C7GameData.GameData.rng = original;
		}
	}

	// A won fight costs a full movement point, even along a road or
	// railroad (owner's decision, 2026-10-21); the move into the tile costs
	// no more than that.
	[Fact]
	public async Task WinningAttackerPaysAFullMoveForTheFight() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		// A tank against a badly wounded warrior, so the attack can't fail.
		MapUnit tank = Spawn(us, "Tank", from);
		MapUnit warrior = Spawn(them, "Warrior", to);
		warrior.hitPointsRemaining = 1;
		float moveCost = System.Math.Max(1, TilePath.GetMovementCost(us, from, dir, to));
		float startingMovement = tank.movementPoints.remaining;

		Assert.True(await tank.Move(dir));

		Assert.Equal(to, tank.location);
		Assert.DoesNotContain(warrior, gameData.mapUnits);
		Assert.Equal(startingMovement - moveCost, tank.movementPoints.remaining, 3);
	}

	[Fact]
	public async Task AFightAlongARoadStillCostsAFullMove() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		TerrainImprovement road = gameData.terrainImprovements.Single(ti => ti.key == Tile.TileOverlays.ROAD);
		from.overlays.Add(road);
		to.overlays.Add(road);
		Assert.True(TilePath.GetMovementCost(us, from, dir, to) < 1);
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit target = Spawn(them, "Warrior", to);
		target.hitPointsRemaining = 1;
		warrior.hitPointsRemaining = warrior.maxHitPoints;
		System.Random original = C7GameData.GameData.rng;
		try {
			// Every round goes to the attacker.
			C7GameData.GameData.rng = new FixedRandom(0);
			Assert.True(await warrior.Move(dir));
		} finally {
			C7GameData.GameData.rng = original;
		}
		Assert.Equal(to, warrior.location);
		Assert.False(warrior.movementPoints.canMove);
	}

	[Fact]
	public async Task DefenderRetreatsEvenWithoutMovementPoints() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to, Tile behind) = FindLandInALine();
		MapUnit warrior = Spawn(us, "Warrior", from);
		// Horsemen are fast enough to retreat from a warrior.
		MapUnit horseman = Spawn(them, "Horseman", to);
		horseman.movementPoints.onConsumeAll();

		await WithZeroRandom(async () => Assert.True(await warrior.Move(dir)));

		Assert.Equal(behind, horseman.location);
		Assert.Contains(horseman, behind.unitsOnTile);
		Assert.Contains(horseman, gameData.mapUnits);
		Assert.Equal(1, horseman.hitPointsRemaining);
	}

	[Fact]
	public async Task DefenderDoesNotRetreatIntoAnotherBattle() {
		GoToWar();
		(Tile from, TileDirection dir, Tile to, Tile behind) = FindLandInALine();
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit horseman = Spawn(them, "Horseman", to);
		// One of our units holds the tile the horseman would retreat to.
		MapUnit blocker = Spawn(us, "Warrior", behind);
		int blockerHitPoints = blocker.hitPointsRemaining;

		await WithZeroRandom(async () => Assert.True(await warrior.Move(dir)));

		Assert.DoesNotContain(horseman, gameData.mapUnits);
		Assert.Equal(behind, blocker.location);
		Assert.Equal(blockerHitPoints, blocker.hitPointsRemaining);
		Assert.Contains(blocker, gameData.mapUnits);
	}
	// No defender withdraws out of a city, fortified or not: under the base
	// game rules "units cannot retreat when they start combat with 1 HP or
	// are defending a city" (C3X's notes on the 1.22 executable,
	// https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
	// Out in the open the same horseman withdraws (BomberEscort,
	// https://forums.civfanatics.com/threads/comprehensive-retreat-study.64755/, #51).
	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public async Task NoDefenderRetreatsOutOfACity(bool inCity, bool fortified) {
		GoToWar();
		(Tile from, TileDirection dir, Tile to, Tile behind) = FindLandInALine();
		if (inCity) {
			CityInteractions.BuildCity(to, them, them.GetNextCityName());
		}
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit horseman = Spawn(them, "Horseman", to);
		horseman.isFortified = fortified;

		await WithZeroRandom(async () => Assert.True(await warrior.Move(dir)));

		if (inCity) {
			Assert.DoesNotContain(horseman, gameData.mapUnits);
		} else {
			Assert.Equal(behind, horseman.location);
			Assert.Contains(horseman, gameData.mapUnits);
			Assert.Equal(1, horseman.hitPointsRemaining);
		}
	}

	// "Cavalry retreats only if random(Hoplite.Retreat_Bonus + 50) <
	// Cavalry.Retreat_Bonus" (Antal1987, the 1.22 executable's
	// process_Combat,
	// https://forums.civfanatics.com/threads/useful-patches-for-civ-3-conquests-v1-22.526285/page-4,
	// #62), with the BIQ's retreat bonuses 34, 50, 58 and 66.
	[Theory]
	[InlineData(1, 1, 50.0 / 100)]
	[InlineData(3, 0, 66.0 / 84)]
	[InlineData(0, 3, 34.0 / 116)]
	[InlineData(2, 1, 58.0 / 100)]
	public void TheRetreatChanceIsTheBonusOverTheOpponentsPlusFifty(int attackerLevel, int defenderLevel, double chance) {
		(Tile from, _, Tile to) = FindAdjacentLand();
		MapUnit horseman = Spawn(us, "Horseman", from);
		MapUnit spearman = Spawn(them, "Spearman", to);
		horseman.experienceLevel = gameData.experienceLevels[attackerLevel];
		spearman.experienceLevel = gameData.experienceLevels[defenderLevel];

		Assert.Equal(chance, horseman.RetreatChance(spearman, true), 6);
	}

	// Every round goes to the defender, and every retreat roll succeeds.
	private class DefenderWinsAndRetreatsSucceed : System.Random {
		public override double NextDouble() => 0.999;
		public override int Next(int maxValue) => 0;
	}

	// "Retreat (only the attacker, never barbarians)" (Perez's decompile,
	// 3.2; owner's ruling 2026-11-12): a barbarian horseman that loses fights
	// to the death, where ours withdraws.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task BarbariansNeverRetreatFromAnAttack(bool barbarian) {
		Player barbarians = gameData.players.Single(p => p.isBarbarians);
		Player attacker = barbarian ? barbarians : us;
		GoToWar();
		(Tile from, TileDirection dir, Tile to) = FindAdjacentLand();
		MapUnit horseman = Spawn(attacker, "Horseman", from);
		MapUnit warrior = Spawn(them, "Warrior", to);
		Assert.Equal(barbarian ? 0.0 : 0.5, horseman.RetreatChance(warrior, true), 6);

		System.Random original = C7GameData.GameData.rng;
		C7GameData.GameData.rng = new DefenderWinsAndRetreatsSucceed();
		try {
			CombatResult result = await horseman.Fight(warrior);
			Assert.Equal(barbarian ? CombatResult.AttackerKilled : CombatResult.AttackerRetreated, result);
		} finally {
			C7GameData.GameData.rng = original;
		}
	}
}

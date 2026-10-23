using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3: a unit that moves into an undefended enemy city takes it (before, the
// city was destroyed). See CityInteractions.CaptureCity.
public class CityCaptureTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile cityTile;
	private readonly MapUnit warrior;

	public CityCaptureTest(SaveGameFixture fixture) {
		// A clone, because some tests change unit types, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.DeclareWarOn(them, gameData.turn);
		// A land tile for their city with land to its east for our warrior,
		// away from everyone's units.
		cityTile = gameData.map.tiles.First(t => IsEmptyLand(t)
			&& t.neighbors.TryGetValue(TileDirection.EAST, out Tile east) && IsEmptyLand(east)
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 6));
		warrior = gameData.SpawnUnit(us, Prototype("Warrior"), cityTile.neighbors[TileDirection.EAST]);
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	private UnitPrototype Prototype(string name) {
		return gameData.unitPrototypes.Find(u => u.name == name);
	}

	private Building BuildingWhere(System.Func<Building, bool> predicate) {
		return gameData.Buildings.First(predicate);
	}

	// Founds a city for `owner` with `size` citizens of `owner`'s nationality.
	private City Found(Player owner, Tile tile, int size, string name = "Target") {
		City city = CityInteractions.BuildCity(tile, owner, name);
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			city.AddCitizen(new CityResident { city = city, nationality = owner.civilization, citizenType = laborer });
		}
		return city;
	}

	// A second city for them, so they survive losing the target.
	private City FoundElsewhere(Player owner, int size) {
		Tile tile = gameData.map.tiles.First(t => IsEmptyLand(t) && t.DistanceTo(cityTile) >= 4 && t.DistanceTo(cityTile) <= 8);
		return Found(owner, tile, size, "Elsewhere");
	}

	private async Task Attack() {
		Assert.True(await warrior.Move(TileDirection.WEST));
	}

	[Fact]
	public async Task AnUndefendedCityChangesHandsAndLosesACitizen() {
		City target = Found(them, cityTile, 3);
		FoundElsewhere(them, 1);

		await Attack();

		Assert.Equal(us, target.owner);
		Assert.Contains(target, us.cities);
		Assert.DoesNotContain(target, them.cities);
		Assert.Contains(target, gameData.cities);
		Assert.Equal(2, target.residents.Count);
		Assert.Equal(cityTile, warrior.location);
		Assert.Equal(target, cityTile.owningCity);
		Assert.Equal(gameData.turn, target.capturedOnTurn);
		Assert.Equal(them, target.capturedFrom);
		Assert.Equal(0, target.shieldsStored);
		// Its citizens now work our land, if any.
		Assert.All(target.residents, r => Assert.True(r.tileWorked == Tile.NONE || r.tileWorked.owningCity.owner == us));
		MsgCityCaptured message = Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityCaptured>());
		Assert.Equal(us.id, message.conquerorId);
		Assert.Equal(them.id, message.formerOwnerId);
	}

	[Fact]
	public async Task TheConquerorTakesTheCitysShareOfTheTreasury() {
		City target = Found(them, cityTile, 3);
		FoundElsewhere(them, 1);
		them.gold = 100;
		int ourGold = us.gold;

		await Attack();

		// 3 of their 4 citizens lived there.
		Assert.Equal(25, them.gold);
		Assert.Equal(ourGold + 75, us.gold);
		Assert.Equal(75, Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityCaptured>()).goldTaken);
	}

	[Fact]
	public async Task ASizeOneCityWithNoCultureIsDestroyed() {
		City target = Found(them, cityTile, 1);
		FoundElsewhere(them, 1);

		await Attack();

		Assert.DoesNotContain(target, gameData.cities);
		Assert.Null(cityTile.cityAtTile);
		Assert.Contains(warrior, gameData.mapUnits);
		Assert.True(Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityRazed>()).destroyedOnCapture);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgCityCaptured>());
	}

	[Fact]
	public async Task ASizeOneCityWithCultureIsCapturedWhole() {
		City target = Found(them, cityTile, 1);
		FoundElsewhere(them, 1);
		target.perPlayerCulture[them] = 5;

		await Attack();

		Assert.Equal(us, target.owner);
		Assert.Single(target.residents);
	}

	[Fact]
	public async Task ASizeOneCityOfForeignersIsCaptured() {
		City target = Found(them, cityTile, 1);
		FoundElsewhere(them, 1);
		target.residents[0].nationality = gameData.players.First(p => p != us && p != them && !p.isBarbarians).civilization;

		await Attack();

		Assert.Equal(us, target.owner);
		Assert.Single(target.residents);
	}

	[Fact]
	public async Task CapitalsLoseThePalaceSmallWondersAndCultureBuildingsButKeepGreatWonders() {
		City target = Found(them, cityTile, 4);
		City elsewhere = FoundElsewhere(them, 2);
		Assert.True(target.IsCapital());
		Building palace = BuildingWhere(b => b.isCenterOfEmpire);
		Building smallWonder = BuildingWhere(b => b.isSmallWonder && !b.isCenterOfEmpire);
		Building temple = BuildingWhere(b => !b.IsGreatWonder() && !b.isSmallWonder && b.culturePerTurn > 0);
		Building aqueduct = BuildingWhere(b => !b.IsGreatWonder() && !b.isSmallWonder && b.allowsCitySize2 && b.culturePerTurn == 0);
		Building wonder = BuildingWhere(b => b.IsGreatWonder() && b.culturePerTurn > 0);
		target.AddBuilding(smallWonder);
		target.AddBuilding(temple);
		target.AddBuilding(aqueduct);
		target.AddBuilding(wonder);

		await Attack();

		List<Building> left = target.constructed_buildings.Select(cb => cb.building).ToList();
		Assert.DoesNotContain(palace, left);
		Assert.DoesNotContain(smallWonder, left);
		Assert.DoesNotContain(temple, left);
		Assert.Contains(aqueduct, left);
		Assert.Contains(wonder, left);
		// The wonder works for us but its culture isn't ours.
		Assert.Equal(them, target.constructed_buildings.First(cb => cb.building == wonder).builtByPlayer);
		Assert.Equal(0, target.GetCulturePerTurnRaw());
		Assert.False(target.IsCapital());
		// Their palace moved to the city they have left.
		Assert.True(elsewhere.IsCapital());
		Assert.Contains(elsewhere.constructed_buildings, cb => cb.building == palace);
		Assert.Equal([wonder.name], Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityCaptured>()).wonders);
	}

	[Fact]
	public async Task ACapturedCitysBordersShrinkToItsNewOwnersCulture() {
		City target = Found(them, cityTile, 3);
		FoundElsewhere(them, 1);
		target.perPlayerCulture[them] = 150;
		gameData.UpdateTileOwners();
		Assert.Equal(3, target.GetBorderExpansionLevel());

		await Attack();

		Assert.Equal(1, target.GetBorderExpansionLevel());
		Assert.DoesNotContain(gameData.map.tiles, t => t.owningCity == target && t.RankDistanceTo(cityTile) > 1);
	}

	[Fact]
	public async Task RazingACapturedCityTakesSlaves() {
		City target = Found(them, cityTile, 6);
		FoundElsewhere(them, 1);
		await Attack();
		Assert.Equal(5, target.residents.Count);

		Assert.True(CityInteractions.RazeCity(target, us));

		Assert.DoesNotContain(target, gameData.cities);
		Assert.Null(cityTile.cityAtTile);
		Assert.Contains(warrior, gameData.mapUnits);
		MapUnit[] slaves = cityTile.unitsOnTile.Where(u => u.unitType.name == gameData.rules.SlaveUnitType).ToArray();
		Assert.Equal(2, slaves.Length);
		Assert.All(slaves, s => Assert.Equal(us, s.owner));
		Assert.All(slaves, s => Assert.Equal(them.civilization, s.nationality));
		MsgCityRazed message = Assert.Single(EngineStorage.messagesToUI.OfType<MsgCityRazed>());
		Assert.Equal(2, message.slavesTaken);
		Assert.Equal(them.id, message.formerOwnerId);
		Assert.False(message.destroyedOnCapture);
	}

	[Fact]
	public async Task ACityCanOnlyBeRazedOnTheTurnItWasCaptured() {
		City target = Found(them, cityTile, 3);
		FoundElsewhere(them, 1);
		await Attack();
		gameData.turn++;

		Assert.False(CityInteractions.RazeCity(target, us));

		Assert.Contains(target, gameData.cities);
		Assert.False(CityInteractions.RazeCity(FoundElsewhere(us, 3), us));
	}

	[Fact]
	public async Task UnitsThatCannotFightCannotTakeACity() {
		City target = Found(them, cityTile, 3);
		warrior.unitType.attack = 0;
		warrior.unitType.canCapture = false;

		await Attack();

		Assert.Equal(them, target.owner);
		Assert.NotEqual(cityTile, warrior.location);
	}

	[Fact]
	public async Task TakingTheLastCityEliminatesTheLoser() {
		City target = Found(them, cityTile, 3);
		foreach (MapUnit unit in them.units.ToList()) {
			unit.RemoveFromPlay();
		}

		await Attack();

		Assert.Equal(us, target.owner);
		Assert.True(them.defeated);
	}

	[Fact]
	public async Task WorkersInACapturedCityAreCaptured() {
		City target = Found(them, cityTile, 3);
		FoundElsewhere(them, 1);
		MapUnit worker = gameData.SpawnUnit(them, Prototype("Worker"), cityTile);

		await Attack();

		Assert.Equal(us, target.owner);
		Assert.Equal(us, worker.owner);
	}

	[Fact]
	public void AbandoningACityKeepsTheLandUnitsInIt() {
		City ours = Found(us, cityTile, 1);
		MapUnit guard = gameData.SpawnUnit(us, Prototype("Spearman"), cityTile);

		CityInteractions.DestroyCity(ours);

		Assert.Contains(guard, gameData.mapUnits);
		Assert.Equal(cityTile, guard.location);
	}
}

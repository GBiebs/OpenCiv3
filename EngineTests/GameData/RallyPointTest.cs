using System;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's rally points: the units a city builds head for its rally point,
// else for its continent's (the continental rally point).
public class RallyPointTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City home;

	public RallyPointTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isBarbarians);
		home = CityInteractions.BuildCity(us.units.First(u => u.unitType.isSettler).location, us, "Home");
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Warrior => gameData.unitPrototypes.Find(u => u.name == "Warrior");

	// Open land on the city's continent, this far away.
	private Tile LandAway(Tile from, int distance) {
		return gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && t.continent == from.continent && !t.HasCity()
			&& t.unitsOnTile.Count == 0 && !t.hasBarbarianCamp && !t.hasGoodyHut && from.DistanceTo(t) == distance);
	}

	private MapUnit Build(City city) {
		city.SetItemBeingProduced(Warrior);
		city.SetStoredShields(us.ShieldCost(Warrior));
		city.HandleCityProduction(gameData);
		return us.units.Last();
	}

	[Fact]
	public void NewUnitsHeadForTheRallyPoint() {
		Assert.Null(Build(home).path);

		Tile rally = LandAway(home.location, 3);
		new MsgSetRallyPoint(home.id, rally, continental: false).process();
		MapUnit built = Build(home);
		Assert.Same(home.location, built.location);
		Assert.Same(rally, built.path.destination);

		// Cleared, they stay home.
		new MsgSetRallyPoint(home.id, null, continental: false).process();
		Assert.Null(home.rallyPoint);
		Assert.Null(Build(home).path);
	}

	[Fact]
	public void TheContinentsRallyPointUnlessTheCityHasItsOwn() {
		Tile site = LandAway(home.location, 5);
		City other = CityInteractions.BuildCity(site, us, "Other");
		Tile gathering = LandAway(home.location, 3);
		new MsgSetRallyPoint(home.id, gathering, continental: true).process();
		Assert.Same(gathering, us.continentalRallyPoints[home.location.continent]);
		Assert.Same(gathering, Build(other).path.destination);

		Tile own = LandAway(site, 2);
		new MsgSetRallyPoint(other.id, own, continental: false).process();
		Assert.Same(own, Build(other).path.destination);
		Assert.Same(gathering, Build(home).path.destination);

		new MsgSetRallyPoint(home.id, null, continental: true).process();
		Assert.Empty(us.continentalRallyPoints);
	}

	[Fact]
	public void NotWhereTheUnitCantGo() {
		Tile sea = gameData.map.tiles.First(t => Tile.IsTileValid(t) && !t.IsLand());
		new MsgSetRallyPoint(home.id, sea, continental: false).process();
		Assert.Null(Build(home).path);
	}

	[Fact]
	public void RallyPointsAreSaved() {
		Tile rally = LandAway(home.location, 3);
		Tile gathering = LandAway(home.location, 4);
		new MsgSetRallyPoint(home.id, rally, continental: false).process();
		new MsgSetRallyPoint(home.id, gathering, continental: true).process();

		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		City again = reloaded.cities.Find(c => c.id == home.id);
		Assert.Equal((rally.XCoordinate, rally.YCoordinate), (again.rallyPoint.XCoordinate, again.rallyPoint.YCoordinate));
		Tile continental = reloaded.GetPlayer(us.id).continentalRallyPoints[home.location.continent];
		Assert.Equal((gathering.XCoordinate, gathering.YCoordinate), (continental.XCoordinate, continental.YCoordinate));
	}
}

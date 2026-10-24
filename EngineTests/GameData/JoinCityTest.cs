using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's "join city": settlers and workers add their population cost to one
// of our cities, within its size limit.
public class JoinCityTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly City city;

	public JoinCityTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		Tile site = gameData.map.tiles.First(t => Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity()
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 4));
		city = CityInteractions.BuildCity(site, us, "Home");
	}

	private MapUnit Spawn(string type, Tile tile = null) {
		return gameData.SpawnUnit(us, gameData.unitPrototypes.Find(u => u.name == type), tile ?? city.location);
	}

	private void Grow(int size) {
		CitizenType laborer = gameData.citizenTypes.Find(c => c.IsDefaultCitizen);
		while (city.residents.Count < size) {
			city.AddCitizen(new CityResident { city = city, nationality = us.civilization, citizenType = laborer });
		}
	}

	[Fact]
	public void ASettlerAddsTwoCitizensAndIsUsedUp() {
		MapUnit settler = Spawn("Settler");
		Assert.Contains(UnitAction.JoinCity, settler.GetAvailableActions());

		Assert.Equal(city, settler.JoinCity());

		Assert.Equal(3, city.residents.Count);
		Assert.DoesNotContain(settler, gameData.mapUnits);
	}

	[Fact]
	public void ACapturedWorkerJoinsAsAForeignNational() {
		Player them = gameData.players.First(p => p != us && !p.isBarbarians);
		MapUnit worker = Spawn("Worker");
		worker.nationality = them.civilization;

		worker.JoinCity();

		Assert.Equal(2, city.residents.Count);
		Assert.Equal(them.civilization, city.residents.Last().nationality);
	}

	[Fact]
	public void UnitsCannotJoinPastTheCitysSizeLimit() {
		int limit = city.SizeLimit(gameData);
		Assert.True(limit < 1000, "the test city should have no hospital");
		Grow(limit - 1);
		MapUnit settler = Spawn("Settler");
		MapUnit worker = Spawn("Worker");

		Assert.False(settler.CanJoinCity()); // two would be one too many
		Assert.Null(settler.JoinCity());
		Assert.True(worker.CanJoinCity());
		Assert.NotNull(worker.JoinCity());
		Assert.Equal(limit, city.residents.Count);
	}

	[Fact]
	public void OnlyOurOwnCitiesCanBeJoined() {
		Player them = gameData.players.First(p => p != us && !p.isBarbarians);
		MapUnit settler = Spawn("Settler");
		city.owner = them;

		Assert.False(settler.CanJoinCity());
	}

	[Fact]
	public void UnitsOutsideACityOrWithoutTheOrderCannotJoin() {
		Tile outside = city.location.neighbors.Values.First(t => t.IsLand());
		Assert.False(Spawn("Settler", outside).CanJoinCity());
		Assert.False(Spawn("Warrior").CanJoinCity());
	}

	[Fact]
	public void TheCiv3RulesetLetsSettlersAndWorkersJoin() {
		Assert.Equal(["Settler", "Worker"],
			gameData.unitPrototypes.Where(u => u.actions.Contains(UnitAction.JoinCity)).Select(u => u.name).Order());
	}
}

using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// A new city must stand the rules' minimum distance from every other
// (diagonals counting as one): by default 2, as in Civ3, so only a neighbor
// is too close (user, 2026-11-03). The other tests raise it to 3 (two tiles
// between) to check each place that follows the rule.
public class CitySpacingTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly MapUnit settler;
	private readonly City city;

	public CitySpacingTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		// Founding a city animates; without this a city founded where it
		// shouldn't be would wait for the animation forever.
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		settler = us.units.First(u => u.unitType.isSettler);
		city = CityInteractions.BuildCity(us.units.First(u => !u.unitType.isSettler).location, us, "Home");
	}

	// A tile where a city could stand but for other cities, exactly this
	// far from ours (and farther from all the rest).
	private Tile At(int distance) {
		return city.location.GetTilesWithinTileSquare(distance).First(t => Tile.IsTileValid(t) && t.IsLand() && t.IsAllowCities()
			&& !city.location.GetTilesWithinTileSquare(distance - 1).Contains(t)
			&& !gameData.cities.Any(c => c != city && c.location.GetTilesWithinTileSquare(3).Contains(t)));
	}

	private bool CanBuildAt(int distance) {
		gameData.TeleportUnit(settler, At(distance));
		return settler.canBuildCity();
	}

	[Fact]
	public void NotNextToACity() {
		Assert.Equal(2, gameData.rules.MinimumCityDistance);
		Assert.False(CanBuildAt(1));
		Assert.True(CanBuildAt(2));

		// No Build City button next to a city.
		gameData.TeleportUnit(settler, At(1));
		Assert.DoesNotContain(UnitAction.BuildCity, settler.GetAvailableActions());
	}

	[Fact]
	public void TheRuleSetsTheDistance() {
		us.rules.MinimumCityDistance = 3;
		Assert.False(CanBuildAt(1));
		Assert.False(CanBuildAt(2));
		Assert.True(CanBuildAt(3));
		gameData.TeleportUnit(settler, At(2));
		Assert.DoesNotContain(UnitAction.BuildCity, settler.GetAvailableActions());
	}

	[Fact]
	public void AGoodyHutCityKeepsItsDistanceToo() {
		MapUnit explorer = us.units.First(u => !u.unitType.isSettler);
		Assert.False(GoodyHuts.IsPossible(GoodyHuts.Outcome.City, gameData, explorer, At(1)));
		Assert.True(GoodyHuts.IsPossible(GoodyHuts.Outcome.City, gameData, explorer, At(2)));
		us.rules.MinimumCityDistance = 3;
		Assert.False(GoodyHuts.IsPossible(GoodyHuts.Outcome.City, gameData, explorer, At(2)));
		Assert.True(GoodyHuts.IsPossible(GoodyHuts.Outcome.City, gameData, explorer, At(3)));
	}

	[Fact]
	public void TheEngineRefusesACityTooClose() {
		us.rules.MinimumCityDistance = 3;
		gameData.TeleportUnit(settler, At(2));
		Assert.Null(settler.BuildCity("Crowded").Result);
		Assert.Single(us.cities);
	}

	// An AI settler whose site is now too close to a city (one founded since
	// it set out) looks for another instead of founding there.
	[Fact]
	public void AnAISettlerGivesUpASiteTooClose() {
		us.rules.MinimumCityDistance = 3;
		Tile site = At(2);
		gameData.TeleportUnit(settler, site);
		SettlerAI ai = new(new C7GameData.AIData.SettlerAIData { goal = C7GameData.AIData.SettlerAIData.SettlerGoal.BUILD_CITY, destination = site });
		Assert.Equal(C7GameData.UnitAI.Result.Error, ((C7GameData.UnitAI)ai).PlayTurn(us, settler).Result);
		Assert.Single(us.cities);
	}
}

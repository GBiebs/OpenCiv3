using System.Linq;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's Go to City (Ctrl-Shift-G): which of our cities the list offers a
// unit, and the turns it shows for each.
public class GotoCityTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player human;
	private readonly Player ai;
	private readonly Tile home;

	public GotoCityTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;

		human = gameData.players.First(p => p.isHuman);
		ai = gameData.players.First(p => !p.isBarbarians && !p.isHuman && p.units.Any(u => u.unitType.isSettler));
		home = human.units.First(u => u.unitType.isSettler).location;
		CityInteractions.BuildCity(home, human, "Home");
		human.tileKnowledge.AddTilesToKnown(home);
	}

	private MapUnit Warrior(Tile tile) {
		return gameData.SpawnUnit(human, gameData.unitPrototypes.Single(p => p.name == "Warrior"), tile);
	}

	private static bool IsEmptyLand(Tile t) {
		return t != Tile.NONE && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp
			&& !t.hasGoodyHut && !t.IsImpassable();
	}

	// An empty land tile on home's continent at the given distance from a
	// tile, and not next to a city (whose road would cut the step's cost).
	private Tile LandAt(Tile from, int distance) {
		return gameData.map.tiles.First(t => IsEmptyLand(t) && t.continent == home.continent && t.DistanceTo(from) == distance
			&& !t.neighbors.Values.Any(n => n.HasCity()));
	}

	// Our cities the unit can reach, by name, with the turns a go-to there
	// takes: a warrior takes a turn to step into the city next to it, and
	// more to one farther off. Other civs' cities aren't offered.
	[Fact]
	public void ListsOurCitiesByNameWithTheTurnsToEach() {
		Tile start = LandAt(home, 3);
		City near = CityInteractions.BuildCity(LandAt(start, 1), human, "Zeta");
		City far = CityInteractions.BuildCity(LandAt(start, 5), human, "Alpha");
		CityInteractions.BuildCity(LandAt(start, 2), ai, "Theirs");
		MapUnit warrior = Warrior(start);

		var choices = UnitInteractions.GotoCityChoices(warrior);

		Assert.Equal(["Alpha", "Home", "Zeta"], choices.Select(c => c.city.name));
		Assert.Equal(1, choices.Single(c => c.city == near).turns);
		Assert.True(choices.Single(c => c.city == far).turns > 1);
		Assert.All(choices, c => Assert.Equal(c.city.location, c.path.destination));
	}

	// The city the unit is in isn't one to go to.
	[Fact]
	public void LeavesOutTheCityTheUnitIsIn() {
		CityInteractions.BuildCity(LandAt(home, 4), human, "Other");
		MapUnit warrior = Warrior(home);

		Assert.Equal(["Other"], UnitInteractions.GotoCityChoices(warrior).Select(c => c.city.name));
	}

	// A city the unit can't get to, like one across the sea from a land
	// unit, isn't offered.
	[Fact]
	public void LeavesOutCitiesTheUnitCantReach() {
		Tile overseas = gameData.map.tiles.FirstOrDefault(t => IsEmptyLand(t) && t.continent != home.continent);
		Assert.NotNull(overseas);
		CityInteractions.BuildCity(overseas, human, "Overseas");
		MapUnit warrior = Warrior(home);

		Assert.Empty(UnitInteractions.GotoCityChoices(warrior));
	}
}

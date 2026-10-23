using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI.UnitAI;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI.UnitAI;

// A defender keeps its orders while it walks to the city it was sent to defend.
// If that city is destroyed and another civ founds a city on the same tile, the
// AI used to crash (KeyNotFoundException in DefenderAI.FindAtRiskCityToDefend)
// the next time it chose a city for any of its defenders: it looked the tile's new
// city up among its own cities. Found in all-AI games (start-rating study), where
// it ended 2 of 216 games on turn 173.
public class DefenderResettledCityTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public DefenderResettledCityTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		EngineStorage.animationsEnabled = false;
		// Engine message queue is static; parallelization is disabled repo-wide (XunitSettings.cs)
		EngineStorage.messagesToUI.Clear();
	}

	private static MapUnit Spawn(C7GameData.GameData gameData, Player owner, string unit, Tile tile) {
		gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == unit), tile);
		return owner.units.Last();
	}

	private static bool IsFree(Tile t) => t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity();

	private record Situation(C7GameData.GameData gameData, Player us, City home, MapUnit onTheWay, DefenderAI orders);

	// A Spearman is sent to defend one of our cities; before it arrives, the
	// city is destroyed and another civ settles the same spot.
	private Situation DefenderOnItsWayToAResettledCity() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		// No barbarian camps, so the AI doesn't send the Spearmen after one instead.
		foreach (Tile t in gameData.map.tiles) t.hasBarbarianCamp = false;
		gameData.map.barbarianCamps.Clear();

		Player us = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		Player them = gameData.players.First(p => !p.isHuman && !p.isBarbarians && p != us);

		// Two cities: a defended one at our start, and an undefended one 5 tiles away.
		Tile homeTile = us.units[0].location;
		City home = CityInteractions.BuildCity(homeTile, us, "Home");
		Spawn(gameData, us, "Spearman", homeTile);
		Spawn(gameData, us, "Spearman", homeTile);
		Tile lostTile = gameData.map.tiles.First(t => IsFree(t) && t.DistanceTo(homeTile) == 5);
		City lost = CityInteractions.BuildCity(lostTile, us, "Lost");

		// At war, the AI sends defenders to its cities that need them.
		us.DeclareWarOn(them, gameData.turn);
		Assert.True(PlayerRelationship.IsInAnyWar(us, gameData.players));

		// A Spearman near the undefended city: the AI sends it there.
		Tile nearLost = gameData.map.tiles.First(t => IsFree(t) && t.DistanceTo(lostTile) == 2 && t.DistanceTo(homeTile) >= 5);
		MapUnit onTheWay = Spawn(gameData, us, "Spearman", nearLost);
		onTheWay.currentAI = PlayerAI.GetAIForUnit(onTheWay, us);
		DefenderAI orders = Assert.IsType<DefenderAI>(onTheWay.currentAI);
		Assert.Equal(lostTile, orders.data.destination);

		CityInteractions.DestroyCity(lost);
		CityInteractions.BuildCity(lostTile, them, "Resettled");
		Assert.Equal(them, lostTile.cityAtTile.owner);
		Assert.Equal(lostTile, orders.data.destination); // the Spearman still has its orders

		return new Situation(gameData, us, home, onTheWay, orders);
	}

	[Fact]
	public void ADefenderHeadingToACityThatWasResettledByAnotherCivDoesNotCrashTheAI() {
		(C7GameData.GameData gameData, Player us, City home, _, _) = DefenderOnItsWayToAResettledCity();

		// Another Spearman needs orders: the AI must pick among its own cities and not crash.
		Tile nearHome = home.location.neighbors.Values.First(IsFree);
		MapUnit newDefender = Spawn(gameData, us, "Spearman", nearHome);
		Exception crash = Record.Exception(() => newDefender.currentAI = PlayerAI.GetAIForUnit(newDefender, us));

		Assert.Null(crash);
		DefenderAI newOrders = Assert.IsType<DefenderAI>(newDefender.currentAI);
		Assert.Equal(home.location, newOrders.data.destination);
	}

	[Fact]
	public async Task ADefenderWhoseCityChangedHandsGivesUpItsOrders() {
		(_, Player us, _, MapUnit onTheWay, DefenderAI orders) = DefenderOnItsWayToAResettledCity();
		Tile before = onTheWay.location;

		// Rather than march on (and, at war, attack the city it was sent to
		// defend), it reports an error, so the player AI gives it new orders.
		C7GameData.UnitAI.Result result = await ((C7GameData.UnitAI)orders).PlayTurn(us, onTheWay);

		Assert.Equal(C7GameData.UnitAI.Result.Error, result);
		Assert.Equal(before, onTheWay.location);
	}
}

using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.AI.UnitAI;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI.UnitAI;

// How the AI uses great leaders and armies (see LeaderAI and
// PlayerAI.FillArmies / GetAIForArmy): a leader builds our first army, then
// hurries the build that saves the most shields; armies take the strongest
// attackers in their city, keep its best defender home, and attack at war.
public class LeaderAndArmyAITest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly City home;

	public LeaderAndArmyAITest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		home = CityInteractions.BuildCity(us.units[0].location, us, "Home");
		// Nothing but what each test builds: no barbarian camps to chase.
		foreach (Tile t in gameData.map.tiles) {
			t.hasBarbarianCamp = false;
		}
		gameData.map.barbarianCamps.Clear();
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp
			&& t.baseTerrainType.allowCities && t.overlayTerrainType.allowCities;
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) {
		return gameData.SpawnUnit(owner, Type(type), tile);
	}

	private Building Building(string name) => gameData.Buildings.Find(b => b.name == name);

	// A city of ours on empty land, reachable by land from home, at least
	// four tiles from any other city and at most `maxDistance` from home.
	private City NewCity(int maxDistance = 12) {
		Tile tile = gameData.map.tiles.First(t => IsEmptyLand(t) && t.continent == home.location.continent
			&& t.DistanceTo(home.location) <= maxDistance && gameData.cities.All(c => c.location.DistanceTo(t) >= 4));
		return CityInteractions.BuildCity(tile, us, $"Town {us.cities.Count}");
	}

	// Plays the leader's AI turn by turn until it's used up, at most `turns`.
	private async Task PlayLeader(MapUnit leader, int turns = 10) {
		C7GameData.UnitAI ai = C7Engine.PlayerAI.GetAIForUnit(leader, us);
		Assert.IsType<LeaderAI>(ai);
		for (int turn = 0; turn < turns && us.units.Contains(leader); turn++) {
			leader.OnBeginTurn();
			await ai.PlayTurn(us, leader);
		}
	}

	[Fact]
	public async Task ALeaderBuildsOurFirstArmy() {
		while (us.cities.Count < 4) {
			NewCity(maxDistance: 20);
		}
		home.SetItemBeingProduced(Building("Temple"));
		MapUnit leader = Spawn(us, "Leader", home.location);

		await PlayLeader(leader, turns: 1);

		Assert.DoesNotContain(leader, us.units);
		Assert.Equal(1, us.ArmyCount());
		Assert.Equal(0, home.shieldsStored);
	}

	[Fact]
	public async Task WithoutTheCitiesForAnArmyALeaderHurriesTheBuildThatSavesTheMost() {
		City town = NewCity();
		home.SetItemBeingProduced(Building("Temple"));
		town.SetItemBeingProduced(Building("Library"));
		Assert.True(us.ShieldCost(Building("Library")) > us.ShieldCost(Building("Temple")));
		MapUnit leader = Spawn(us, "Leader", home.location);

		await PlayLeader(leader);

		Assert.DoesNotContain(leader, us.units);
		Assert.Equal(us.ShieldCost(Building("Library")), town.shieldsStored);
		Assert.Equal(0, home.shieldsStored);
		Assert.Equal(0, us.ArmyCount());
	}

	[Fact]
	public async Task ALeaderWithNothingToDoWaitsInOurNearestCity() {
		home.SetItemBeingProduced(Type("Warrior"));
		Tile field = home.location.neighbors.Values.First(IsEmptyLand);
		MapUnit leader = Spawn(us, "Leader", field);

		await PlayLeader(leader, turns: 3);

		Assert.Contains(leader, us.units);
		Assert.Equal(home.location, leader.location);
	}

	[Fact]
	public void ArmiesTakeTheStrongestAttackersAndLeaveTheBestDefenderHome() {
		MapUnit army = Spawn(us, "Army", home.location);
		MapUnit musketman = Spawn(us, "Musketman", home.location);
		MapUnit warrior = Spawn(us, "Warrior", home.location);
		MapUnit archer = Spawn(us, "Archer", home.location);
		MapUnit swordsman = Spawn(us, "Swordsman", home.location);
		MapUnit horseman = Spawn(us, "Horseman", home.location);
		Spawn(us, "Worker", home.location);

		C7Engine.PlayerAI.FillArmies(us);

		MapUnit[] expected = [archer, horseman, swordsman];
		Assert.Equal(expected.OrderBy(u => u.id.ToString()), army.Passengers().OrderBy(u => u.id.ToString()));
		Assert.False(musketman.IsLoaded());
		Assert.False(warrior.IsLoaded());
	}

	// An army in our city with an enemy Warrior in sight, two tiles away.
	private MapUnit ArmyNearAnEnemy() {
		MapUnit army = Spawn(us, "Army", home.location);
		Tile near = home.location.neighbors.Values.First(IsEmptyLand)
			.neighbors.Values.First(t => IsEmptyLand(t) && t.DistanceTo(home.location) == 2);
		Spawn(them, "Warrior", near);
		us.tileKnowledge.AddTilesToKnown(near);
		return army;
	}

	private void Join(MapUnit army, string type) {
		new MsgLoadToTransport(Spawn(us, type, home.location).id, army.id).process();
	}

	[Fact]
	public void AtWarAnArmyAttacksOnceItHasTwoMembers() {
		MapUnit army = ArmyNearAnEnemy();
		us.DeclareWarOn(them, gameData.turn);

		Join(army, "Swordsman");
		Assert.IsType<DefenderAI>(C7Engine.PlayerAI.GetAIForUnit(army, us));

		Join(army, "Swordsman");
		Assert.IsType<CombatAI>(C7Engine.PlayerAI.GetAIForUnit(army, us));
	}

	[Fact]
	public void InPeaceAnArmyWaitsInItsCity() {
		MapUnit army = ArmyNearAnEnemy();
		Join(army, "Swordsman");
		Join(army, "Swordsman");

		Assert.IsType<DefenderAI>(C7Engine.PlayerAI.GetAIForUnit(army, us));
	}
}

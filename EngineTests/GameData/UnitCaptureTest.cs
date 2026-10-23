using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3: a unit that can capture, moving onto enemy units that can't defend
// themselves, captures workers, settlers (who become two workers) and artillery
// without a fight, and destroys anything else. Before, it fought them, and the
// defenseless units always died.
public class UnitCaptureTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile from;
	private readonly Tile to;

	public UnitCaptureTest(SaveGameFixture fixture) {
		// A clone, because some tests change unit types, which the game shares with the save.
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		us.DeclareWarOn(them, gameData.turn);
		// Two empty land tiles side by side, away from everyone's units.
		from = gameData.map.tiles.First(t => IsEmptyLand(t)
			&& t.neighbors.TryGetValue(TileDirection.EAST, out Tile east) && IsEmptyLand(east)
			&& gameData.mapUnits.All(u => u.location.DistanceTo(t) > 3));
		to = from.neighbors[TileDirection.EAST];
	}

	private static bool IsEmptyLand(Tile t) {
		return Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp;
	}

	private MapUnit Spawn(Player owner, string unitType, Tile tile) {
		return gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == unitType), tile);
	}

	private MsgUnitsCaptured Message() {
		return Assert.Single(EngineStorage.messagesToUI.OfType<MsgUnitsCaptured>());
	}

	[Fact]
	public async Task AWarriorCapturesALoneEnemyWorkerWithoutAFight() {
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit worker = Spawn(them, "Worker", to);
		int hitPoints = warrior.hitPointsRemaining;

		Assert.True(await warrior.Move(TileDirection.EAST));

		Assert.Equal(to, warrior.location);
		Assert.Equal(hitPoints, warrior.hitPointsRemaining);
		Assert.Equal(us, worker.owner);
		Assert.Contains(worker, us.units);
		Assert.DoesNotContain(worker, them.units);
		// Captured workers keep their nationality: they're slaves.
		Assert.Equal(them.civilization, worker.nationality);
		Assert.True(worker.IsCaptive());
		Assert.False(worker.movementPoints.canMove);
		MsgUnitsCaptured message = Message();
		Assert.Equal(["Worker"], message.captured);
		Assert.Equal("We captured an enemy Worker!", message.TextFor(us.id));
		Assert.Equal("Our Worker was captured!", message.TextFor(them.id));
	}

	[Fact]
	public async Task ACapturedSettlerBecomesTwoWorkers() {
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit settler = Spawn(them, "Settler", to);

		await warrior.Move(TileDirection.EAST);

		Assert.DoesNotContain(settler, gameData.mapUnits);
		MapUnit[] workers = to.unitsOnTile.Where(u => u.owner == us && u.unitType.name == gameData.rules.SlaveUnitType).ToArray();
		Assert.Equal(2, workers.Length);
		Assert.All(workers, w => Assert.Equal(them.civilization, w.nationality));
		Assert.All(workers, w => Assert.Contains(w, us.units));
		Assert.Equal(["Settler"], Message().captured);
	}

	[Fact]
	public async Task ArtilleryChangesHands() {
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit catapult = Spawn(them, "Catapult", to);

		await warrior.Move(TileDirection.EAST);

		Assert.Equal(us, catapult.owner);
		Assert.Equal("Catapult", catapult.unitType.name);
		Assert.Equal(us.civilization, catapult.nationality);
		Assert.False(catapult.IsCaptive());
	}

	[Fact]
	public async Task AnExplorerCannotBeCapturedAndIsDestroyed() {
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit explorer = Spawn(them, "Explorer", to);

		await warrior.Move(TileDirection.EAST);

		Assert.DoesNotContain(explorer, gameData.mapUnits);
		Assert.DoesNotContain(explorer, us.units);
		MsgUnitsCaptured message = Message();
		Assert.Empty(message.captured);
		Assert.Equal(["Explorer"], message.destroyed);
		Assert.Equal("Our Explorer was destroyed!", message.TextFor(them.id));
	}

	[Fact]
	public async Task AUnitThatCannotCaptureDestroysInstead() {
		MapUnit warrior = Spawn(us, "Warrior", from);
		warrior.unitType.canCapture = false;
		MapUnit worker = Spawn(them, "Worker", to);

		await warrior.Move(TileDirection.EAST);

		Assert.DoesNotContain(worker, gameData.mapUnits);
		Assert.Equal(["Worker"], Message().destroyed);
	}

	[Fact]
	public async Task ADefendedWorkerIsSafeUntilItsDefenderIsBeaten() {
		MapUnit warrior = Spawn(us, "Warrior", from);
		MapUnit defender = Spawn(them, "Spearman", to);
		defender.hitPointsRemaining = 50; // the warrior can't win
		MapUnit worker = Spawn(them, "Worker", to);

		Assert.False(await warrior.Move(TileDirection.EAST));

		Assert.DoesNotContain(warrior, gameData.mapUnits);
		Assert.Equal(them, worker.owner);
		Assert.Empty(EngineStorage.messagesToUI.OfType<MsgUnitsCaptured>());
	}

	[Fact]
	public async Task BarbariansDestroyWhatTheyCatch() {
		Player barbarians = gameData.players.First(p => p.isBarbarians);
		MapUnit raider = Spawn(barbarians, "Warrior", from);
		MapUnit worker = Spawn(them, "Worker", to);

		await raider.Move(TileDirection.EAST);

		Assert.DoesNotContain(worker, gameData.mapUnits);
		Assert.DoesNotContain(barbarians.units, u => u.unitType.name == "Worker");
	}

	[Fact]
	public void TheCiv3RulesetSaysWhoCanCaptureAndWhatCaptivesBecome() {
		Assert.True(gameData.unitPrototypes.Find(u => u.name == "Warrior").canCapture);
		Assert.True(gameData.unitPrototypes.Find(u => u.name == "Tank").canCapture);
		Assert.False(gameData.unitPrototypes.Find(u => u.name == "Catapult").canCapture);
		Assert.False(gameData.unitPrototypes.Find(u => u.name == "Worker").canCapture);
		Assert.Equal("Worker", gameData.rules.SlaveUnitType);
	}
}

using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Civ3's barbarian camps are never left empty: every camp of a new game
// has a guard, and a camp's last unit stays, stepping out only for a
// defenseless unit next to it (Conquests players' reports).
public class BarbarianCampGuardTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private C7GameData.GameData gameData;
	private Player barbarians;
	private Player us;

	public BarbarianCampGuardTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		NewGame();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	// A new game, as the game starts one.
	private void NewGame() {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		gameData.onGameCreation();
		barbarians = gameData.players.Find(p => p.isBarbarians);
		us = gameData.players.First(p => !p.isBarbarians);
		EngineStorage.messagesToUI.Clear();
	}

	private UnitPrototype Type(string name) => gameData.unitPrototypes.Find(u => u.name == name);

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, Type(type), tile);

	// A camp with nothing around it but open land, emptied of its guard, and
	// a free land tile next to it.
	private (Tile camp, Tile next) QuietCamp() {
		foreach (Tile camp in gameData.map.barbarianCamps) {
			if (camp.neighbors.Values.All(n => n.unitsOnTile.Count == 0 && !n.HasCity())
				&& camp.neighbors.Values.FirstOrDefault(n => n.IsLand() && !n.hasBarbarianCamp && !n.hasGoodyHut) is Tile next) {
				foreach (MapUnit u in camp.unitsOnTile.ToList()) {
					gameData.RemoveUnit(u);
				}
				return (camp, next);
			}
		}
		throw new Exception("No quiet camp in the test game");
	}

	private async Task BarbariansPlay(BarbarianActivity activity) {
		gameData.barbarianInfo.barbarianActivity = activity;
		foreach (MapUnit u in barbarians.units) {
			u.OnBeginTurn();
		}
		await BarbarianAI.PlayTurn(barbarians, gameData);
	}

	[Fact]
	public void EveryCampOfANewGameHasAGuard() {
		Assert.NotEmpty(gameData.map.barbarianCamps);
		foreach (Tile camp in gameData.map.barbarianCamps) {
			MapUnit guard = Assert.Single(camp.unitsOnTile);
			Assert.Equal((barbarians, gameData.barbarianInfo.basicBarbarian), (guard.owner, guard.unitType));
		}

		// Loading the game again doesn't add more, nor guard a camp whose
		// guard was killed since.
		gameData.onGameCreation();
		Assert.All(gameData.map.barbarianCamps, camp => Assert.Single(camp.unitsOnTile));
		Tile emptied = gameData.map.barbarianCamps[0];
		gameData.RemoveUnit(emptied.unitsOnTile[0]);
		gameData.onGameCreation();
		Assert.Empty(emptied.unitsOnTile);
	}

	[Fact]
	public void NoGuardsWithoutBarbarians() {
		SaveGame save = fixture.saveGame.Clone();
		gameData = save.ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		gameData.barbarianInfo.barbarianActivity = BarbarianActivity.None;
		BarbarianInteractions.GuardCamps(gameData);
		Assert.All(gameData.map.barbarianCamps, camp => Assert.Empty(camp.unitsOnTile));
	}

	[Fact]
	public async Task TheLastUnitStaysEvenWithAnEnemyNextToIt() {
		(Tile camp, Tile next) = QuietCamp();
		MapUnit guard = Spawn(barbarians, "Warrior", camp);
		Spawn(us, "Warrior", next);
		await BarbariansPlay(BarbarianActivity.Raging);
		Assert.Same(camp, guard.location);
	}

	[Fact]
	public async Task TheLastUnitFallsOnADefenselessUnitThenGoesBack() {
		(Tile camp, Tile next) = QuietCamp();
		MapUnit guard = Spawn(barbarians, "Warrior", camp);
		Spawn(us, "Worker", next);
		await BarbariansPlay(BarbarianActivity.Raging);
		Assert.Same(next, guard.location);
		Assert.DoesNotContain(next.unitsOnTile, u => u.owner == us);

		await BarbariansPlay(BarbarianActivity.Raging);
		Assert.Same(camp, guard.location);
	}

	[Fact]
	public async Task NotWhenBarbariansKeepToTheirCamps() {
		(Tile camp, Tile next) = QuietCamp();
		MapUnit guard = Spawn(barbarians, "Warrior", camp);
		MapUnit worker = Spawn(us, "Worker", next);
		await BarbariansPlay(BarbarianActivity.Sedentary);
		Assert.Same(camp, guard.location);
		Assert.Same(us, worker.owner);
	}

	[Fact]
	public async Task OneOfTwoMayLeaveButOneStays() {
		(Tile camp, Tile next) = QuietCamp();
		Spawn(barbarians, "Warrior", camp);
		Spawn(barbarians, "Warrior", camp);
		Spawn(us, "Warrior", next);
		for (int turn = 0; turn < 4; turn++) {
			await BarbariansPlay(BarbarianActivity.Raging);
			Assert.Contains(camp.unitsOnTile, u => u.owner == barbarians);
		}
	}
}

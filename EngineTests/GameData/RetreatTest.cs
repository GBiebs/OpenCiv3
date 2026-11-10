using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// A fast unit may retreat from a fight it is losing on its last hit
// point, but never from an opponent down to its last hit point too (Civ3).
public class RetreatTest : IClassFixture<SaveGameFixture> {
	private readonly C7GameData.GameData gameData;
	private readonly Player us;
	private readonly Player them;
	private readonly Tile from;
	private readonly Tile to;

	public RetreatTest(SaveGameFixture fixture) {
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		Player[] ais = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(us, them) = (ais[0], ais[1]);
		bool Empty(Tile t) => Tile.IsTileValid(t) && t.IsLand() && t.unitsOnTile.Count == 0 && !t.HasCity() && !t.hasBarbarianCamp && !t.hasGoodyHut;
		from = gameData.map.tiles.First(t => Empty(t) && t.neighbors.Values.Any(Empty));
		to = from.neighbors.Values.First(Empty);
	}

	private MapUnit Spawn(Player owner, string type, Tile tile) => gameData.SpawnUnit(owner, gameData.unitPrototypes.Find(u => u.name == type), tile);

	// How many of `count` fights the attacker retreated from.
	private async Task<int> Retreats(int attackerHp, int defenderHp, int count) {
		int retreats = 0;
		for (int i = 0; i < count; i++) {
			MapUnit attacker = Spawn(us, "Horseman", from);
			MapUnit defender = Spawn(them, "Warrior", to);
			attacker.hitPointsRemaining = attackerHp;
			defender.hitPointsRemaining = defenderHp;
			if (await attacker.Fight(defender) == CombatResult.AttackerRetreated) {
				retreats++;
			}
			foreach (MapUnit unit in new[] { attacker, defender }.Where(u => u.hitPointsRemaining > 0)) {
				unit.RemoveFromPlay();
			}
		}
		return retreats;
	}

	[Fact]
	public async Task NoRetreatFromAnOpponentOnItsLastHitPoint() {
		Assert.Equal(0, await Retreats(attackerHp: 1, defenderHp: 1, count: 300));
	}

	[Fact]
	public async Task RetreatStillHappensAgainstAHealthyOpponent() {
		Assert.True(await Retreats(attackerHp: 1, defenderHp: 3, count: 300) > 0);
	}
}

using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.AI.UnitAI;

// A unit's plan that keeps asking for another step without moving (a king
// sent to its capital with no path did, in the Middle Ages scenario) used to
// spin forever inside UnitAI.PlayTurn, freezing the game. Now the unit gives
// up its turn.
public class StuckPlanTest : IClassFixture<SaveGameFixture> {
	private readonly SaveGameFixture fixture;

	public StuckPlanTest(SaveGameFixture fixture) {
		this.fixture = fixture;
	}

	private class Stuck : C7GameData.UnitAI {
		public int steps;

		C7GameData.UnitAI.MoveResult C7GameData.UnitAI.PlayTurnImpl(Player player, MapUnit unit) {
			steps++;
			return C7GameData.UnitAI.Result.InProgress;
		}

		public string SummarizePlan() => "stuck";

		public void UpdateOnDeath() { }
	}

	[Fact]
	public async Task APlanThatNeverMovesGivesUp() {
		C7GameData.GameData gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		Player player = gameData.players.First(p => !p.isBarbarians);
		MapUnit unit = player.units.First();
		Stuck plan = new();

		Task<C7GameData.UnitAI.Result> turn = ((C7GameData.UnitAI)plan).PlayTurn(player, unit);
		Assert.True(turn.Wait(5000));
		Assert.Equal(C7GameData.UnitAI.Result.Error, turn.Result);
		Assert.Equal(1000, plan.steps);
		await turn;
	}
}

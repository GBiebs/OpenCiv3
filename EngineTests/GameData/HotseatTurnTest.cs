using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Several humans (hotseat): each takes their turn in order and the UI
// follows whoever's turn it is; the AIs move once every human has.
public class HotseatTurnTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;

	public HotseatTurnTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
	}

	// A new game whose first human has the turn, and a second human.
	private async Task<(Player first, Player second)> StartGame() {
		Player first = await CreateGame.createGame(fixture.saveGame, (_) => fixture.behaviors);
		Player second = EngineStorage.gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		second.isHuman = true;
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		return (first, second);
	}

	// Ends the turn of whoever has it, as MsgEndTurn does, and returns
	// whose turn the engine started next.
	private static async Task<ID> EndTurn() {
		EngineStorage.messagesToUI.Clear();
		Player controller = EngineStorage.gameData.GetPlayer(EngineStorage.uiControllerID);
		TurnHandling.OnEndTurn(controller);
		controller.hasPlayedThisTurn = true;

		Task advance = Task.Run(TurnHandling.AdvanceTurn);
		Task finished = await Task.WhenAny(advance, Task.Delay(TimeSpan.FromSeconds(60)));
		Assert.True(finished == advance, "AdvanceTurn kept running turns instead of returning to the UI");
		await advance;
		return Assert.Single(EngineStorage.messagesToUI.OfType<MsgStartTurn>()).playerId;
	}

	[Fact]
	public async Task EachHumanTakesTheirTurnInOrder() {
		(Player first, Player second) = await StartGame();
		int turn = EngineStorage.gameData.turn;
		Assert.Equal(first.id, EngineStorage.uiControllerID);

		// The first human's End Turn hands the same turn to the second.
		Assert.Equal(second.id, await EndTurn());
		Assert.Equal(second.id, EngineStorage.uiControllerID);
		Assert.Equal(turn, EngineStorage.gameData.turn);
		Assert.True(first.hasPlayedThisTurn);

		// The second's ends the turn: the AIs move, and the next one is the
		// first human's again.
		Assert.Equal(first.id, await EndTurn());
		Assert.Equal(turn + 1, EngineStorage.gameData.turn);
		Assert.False(second.hasPlayedThisTurn);
	}

	// A defeated human no longer gets turns while another human plays on.
	[Fact]
	public async Task ADefeatedHumanIsSkipped() {
		(Player first, Player second) = await StartGame();
		first.defeated = true;

		Assert.Equal(second.id, await EndTurn());
		Assert.Equal(second.id, await EndTurn());
		Assert.Equal(second.id, await EndTurn());
	}

	// With a single human nothing changes: the turn comes back to them.
	[Fact]
	public async Task OneHumanKeepsTheTurn() {
		Player human = await CreateGame.createGame(fixture.saveGame, (_) => fixture.behaviors);
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		int turn = EngineStorage.gameData.turn;

		Assert.Equal(human.id, await EndTurn());
		Assert.Equal(turn + 1, EngineStorage.gameData.turn);
	}
}

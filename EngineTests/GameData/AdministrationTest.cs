using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.GameData;

// Hotseat and play by email: whoever knows the admin password chosen at
// setup can hand a human's civ to the AI or a new player, and clear a
// forgotten password.
public class AdministrationTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player first;
	private readonly Player second;
	private readonly Player ai;
	private readonly ID controller = EngineStorage.uiControllerID;

	public AdministrationTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		first = gameData.players.First(p => p.isHuman);
		Player[] others = gameData.players.Where(p => !p.isHuman && !p.isBarbarians).Take(2).ToArray();
		(second, ai) = (others[0], others[1]);
		second.isHuman = true;
		gameData.adminPasswordHash = Passwords.Hash("sid");
	}

	public void Dispose() {
		EngineStorage.messagesToUI.Clear();
		EngineStorage.uiControllerID = controller;
	}

	private static Administration.Change Make(Player player, bool human, bool clearPassword = false) {
		return new Administration.Change { playerId = player.id, human = human, clearPassword = clearPassword };
	}

	[Fact]
	public void AHumanWhoLeftIsHandedToTheAI() {
		gameData.humanDeals.Add(new HumanDeal { from = first.id, to = second.id });
		Assert.True(Administration.Apply(gameData, "sid", [Make(second, human: false)]));
		Assert.False(second.isHuman);
		// Their deals are gone with them.
		Assert.Empty(gameData.humanDeals);
	}

	// And an AI's civ to a new player.
	[Fact]
	public void ANewPlayerTakesOverAnAI() {
		Assert.True(Administration.Apply(gameData, "sid", [Make(ai, human: true)]));
		Assert.True(ai.isHuman);
	}

	[Fact]
	public void AForgottenPasswordIsCleared() {
		second.SetPassword("forgotten");
		Assert.True(Administration.Apply(gameData, "sid", [Make(second, human: true, clearPassword: true)]));
		Assert.False(second.HasPassword());
		Assert.True(second.isHuman);
	}

	[Fact]
	public void OnlyWithTheAdminPassword() {
		Assert.False(Administration.Apply(gameData, "wrong", [Make(second, human: false)]));
		Assert.True(second.isHuman);
		gameData.adminPasswordHash = "";
		Assert.False(gameData.CheckAdminPassword(""));
		Assert.False(Administration.Apply(gameData, "", [Make(second, human: false)]));
		Assert.True(second.isHuman);
	}

	// A game always keeps a human.
	[Fact]
	public void AHumanStays() {
		Assert.False(Administration.Apply(gameData, "sid", [Make(first, human: false), Make(second, human: false)]));
		Assert.True(first.isHuman && second.isHuman);
	}

	[Fact]
	public void TheAdminPasswordCanBeChanged() {
		Assert.True(Administration.Apply(gameData, "sid", [], newAdminPassword: "meier"));
		Assert.False(gameData.CheckAdminPassword("sid"));
		Assert.True(gameData.CheckAdminPassword("meier"));
		// Left empty, it stays.
		Assert.True(Administration.Apply(gameData, "meier", [], newAdminPassword: ""));
		Assert.True(gameData.CheckAdminPassword("meier"));
	}

	[Fact]
	public void TheSaveKeepsIt() {
		C7GameData.GameData reloaded = SaveGame.FromGameData(gameData).Clone().ToGameData(fixture.behaviors);
		Assert.True(reloaded.CheckAdminPassword("sid"));
		Assert.False(reloaded.CheckAdminPassword("Sid"));
	}

	// The human whose turn it is, handed to the AI before their turn shows:
	// the next human's turn begins, and the AI plays theirs with the other
	// AIs'.
	[Fact]
	public async Task TheAIPlaysTheTurnOfAHumanHandedToIt() {
		Player human = await CreateGame.createGame(fixture.saveGame.Clone(), (_) => fixture.behaviors);
		C7GameData.GameData game = EngineStorage.gameData;
		Player other = game.players.First(p => !p.isHuman && !p.isBarbarians);
		other.isHuman = true;
		// By email, so the AIs don't stop to ask a human anything.
		game.playByEmail = true;
		game.adminPasswordHash = Passwords.Hash("sid");
		TurnHandling.OnBeginTurn();
		TurnHandling.InitTurnData();
		await TurnHandling.AdvanceTurn();
		Assert.Equal(human.id, EngineStorage.uiControllerID);
		int turn = game.turn;
		EngineStorage.messagesToUI.Clear();

		new MsgAdministerGame("sid", [Make(human, human: false)]).send();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(System.Threading.SpinWait.SpinUntil(() => EngineStorage.messagesToUI.OfType<MsgStartTurn>().Any(), TimeSpan.FromSeconds(60)));
		Assert.Equal(other.id, EngineStorage.messagesToUI.OfType<MsgStartTurn>().Single().playerId);
		Assert.False(human.isHuman);
		Assert.Equal(turn, game.turn);
		Assert.False(human.hasPlayedThisTurn);

		// The other human's End Turn: the AIs move, the replaced civ too, and
		// the turn comes back to the only human left.
		EngineStorage.messagesToUI.Clear();
		TurnHandling.OnEndTurn(other);
		other.hasPlayedThisTurn = true;
		Task advance = Task.Run(TurnHandling.AdvanceTurn);
		Assert.True(await Task.WhenAny(advance, Task.Delay(TimeSpan.FromSeconds(60))) == advance);
		Assert.Equal(other.id, EngineStorage.messagesToUI.OfType<MsgStartTurn>().Single().playerId);
		Assert.Equal(turn + 1, game.turn);
	}
}

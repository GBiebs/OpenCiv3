using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Online;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Online;

// Online play's promise: every game plays the same orders with the same
// dice, and so plays out the same game, its AIs too.
public class LockstepTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly byte[] save;
	private readonly ID controller = EngineStorage.uiControllerID;

	public LockstepTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		save = fixture.saveGame.ToJson();
	}

	public void Dispose() {
		EngineStorage.ResetSequence();
		EngineStorage.sendOrder = null;
		EngineStorage.onEndTurnDone = null;
		EngineStorage.messagesToUI.Clear();
		EngineStorage.animationsEnabled = true;
		EngineStorage.uiControllerID = controller;
	}

	private C7GameData.GameData Load() {
		C7GameData.GameData gameData = SaveGame.FromJson(save).ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.messagesToUI.Clear();
		EngineStorage.ResetSequence();
		EngineStorage.uiControllerID = gameData.players.First(p => p.isHuman).id;
		return gameData;
	}

	private static void PlayAll() {
		for (int i = 0; i < 100_000 && (EngineStorage.sequencedOrders.Count > 0 || !EngineStorage.OrdersDone()); i++) {
			EngineStorage.ProcessNextMessageToEngine();
			EngineStorage.messagesToUI.Clear();
		}
		Assert.Empty(EngineStorage.sequencedOrders);
		Assert.True(EngineStorage.OrdersDone());
	}

	// The human ends turn after turn: the AIs play each turn the same way.
	private string Play(IEnumerable<int> seeds) {
		C7GameData.GameData gameData = Load();
		Player human = gameData.players.First(p => p.isHuman);
		long seq = 0;
		foreach (int seed in seeds) {
			EngineStorage.EnqueueSequenced(++seq, seed, human.id, new MsgEndTurn());
		}
		PlayAll();
		Assert.Equal(seeds.Count(), gameData.turn);
		return GameChecksum.Of(gameData);
	}

	[Fact]
	public void TheSameOrdersAndDiceMakeTheSameGame() {
		int[] seeds = [11, 22, 33, 44, 55, 66, 77, 88];
		string first = Play(seeds);
		Assert.Equal(first, Play(seeds));
		// (And the dice do matter.)
		Assert.NotEqual(first, Play([11, 22, 33, 44, 55, 66, 77, 89]));
	}

	// Only the player in control's orders are played, in every game.
	[Fact]
	public void AnOrderFromAPlayerNotInControlIsntPlayed() {
		C7GameData.GameData gameData = Load();
		Player rival = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		MapUnit unit = rival.units[0];
		EngineStorage.EnqueueSequenced(1, 5, rival.id, new MsgSetFortification(unit.id, true));
		PlayAll();
		Assert.False(unit.isFortified);
		Assert.Equal(1, EngineStorage.lastSequenced);

		EngineStorage.uiControllerID = rival.id;
		EngineStorage.EnqueueSequenced(2, 5, rival.id, new MsgSetFortification(unit.id, true));
		PlayAll();
		Assert.True(unit.isFortified);
	}

	// Each order gets its dice: the game's random numbers come from its seed.
	[Fact]
	public void AnOrdersDiceComeFromItsSeed() {
		C7GameData.GameData gameData = Load();
		Player human = gameData.players.First(p => p.isHuman);
		EngineStorage.EnqueueSequenced(1, 4242, human.id, new MsgSetFortification(human.units[0].id, true));
		PlayAll();
		Assert.Equal(new Random(4242).Next(), C7GameData.GameData.rng.Next());
	}

	// The next order waits until the last one's work is done, so every
	// game plays it at the same point.
	[Fact]
	public async Task AnOrderWaitsForTheLastOnesWork() {
		C7GameData.GameData gameData = Load();
		Player human = gameData.players.First(p => p.isHuman);
		MapUnit unit = human.units[0];
		TaskCompletionSource<bool> going = new();
		EngineStorage.EnqueueSequenced(1, 1, human.id, new SlowOrder(going.Task));
		EngineStorage.EnqueueSequenced(2, 2, human.id, new MsgSetFortification(unit.id, true));
		for (int i = 0; i < 5; i++) {
			EngineStorage.ProcessNextMessageToEngine();
		}
		Assert.False(unit.isFortified);
		going.SetResult(true);
		await Task.Delay(50);
		EngineStorage.ProcessNextMessageToEngine();
		Assert.True(unit.isFortified);
	}

	// Once an End Turn's work is done (every AI's turn), and only then.
	[Fact]
	public void TheGamesCompareWhenAnEndTurnsWorkIsDone() {
		C7GameData.GameData gameData = Load();
		Player human = gameData.players.First(p => p.isHuman);
		List<long> done = new();
		EngineStorage.onEndTurnDone = seq => done.Add(seq);
		EngineStorage.EnqueueSequenced(1, 1, human.id, new MsgSetFortification(human.units[0].id, true));
		EngineStorage.EnqueueSequenced(2, 2, human.id, new MsgEndTurn());
		EngineStorage.EnqueueSequenced(3, 3, human.id, new MsgEndTurn());
		PlayAll();
		EngineStorage.ProcessNextMessageToEngine();
		Assert.Equal([2, 3], done);
	}

	// Online, an order that changes the game goes to the host; the screens'
	// own stay here.
	[Fact]
	public void OnlineAnOrderGoesToTheHost() {
		C7GameData.GameData gameData = Load();
		List<MessageToEngine> sent = new();
		EngineStorage.sendOrder = sent.Add;
		EngineStorage.pendingMessages.Clear();
		new MsgEndTurn().send();
		new MsgSetAnimationsEnabled(true).send();
		Assert.IsType<MsgEndTurn>(Assert.Single(sent));
		Assert.IsType<MsgSetAnimationsEnabled>(Assert.Single(EngineStorage.pendingMessages));
		EngineStorage.pendingMessages.Clear();
	}

	private class SlowOrder : MessageToEngine {
		private readonly Task until;

		public SlowOrder(Task until) {
			this.until = until;
		}

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() => await until;
	}
}

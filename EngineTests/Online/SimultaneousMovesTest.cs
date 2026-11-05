using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7Engine.Online;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Online;

// Online, Civ3's simultaneous moves: every human plays at once, each order
// as its sender's, then the computers, then the next turn for all.
public class SimultaneousMovesTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly byte[] save;
	private readonly ID controller = EngineStorage.uiControllerID;

	public SimultaneousMovesTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		save = fixture.saveGame.ToJson();
	}

	public void Dispose() {
		EngineStorage.AbandonWork();
		EngineStorage.animationsEnabled = true;
		EngineStorage.uiControllerID = controller;
		EngineStorage.localPlayerID = null;
	}

	// Two humans, playing their first turn at once.
	private (C7GameData.GameData, Player a, Player b) Load() {
		C7GameData.GameData gameData = SaveGame.FromJson(save).ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.AbandonWork();
		Player a = gameData.players.First(p => p.isHuman);
		Player b = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		b.isHuman = true;
		gameData.simultaneousMoves = true;
		gameData.simultaneousTurnStarted = 0;
		EngineStorage.uiControllerID = a.id;
		return (gameData, a, b);
	}

	private static List<MessageToUI> PlayAll() {
		List<MessageToUI> heard = new();
		for (int i = 0; i < 100_000 && (EngineStorage.sequencedOrders.Count > 0 || !EngineStorage.OrdersDone()); i++) {
			EngineStorage.ProcessNextMessageToEngine();
			while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI message)) {
				heard.Add(message);
			}
		}
		Assert.True(EngineStorage.OrdersDone());
		return heard;
	}

	private string PlayARound() {
		(C7GameData.GameData gameData, Player a, Player b) = Load();
		MapUnit hers = b.units[0], his = a.units[0];
		// B's order, though A was the last in control: B's.
		EngineStorage.EnqueueSequenced(1, 11, b.id, new MsgSetFortification(hers.id, true));
		PlayAll();
		Assert.True(hers.isFortified);
		Assert.Equal(b.id, EngineStorage.uiControllerID);
		EngineStorage.EnqueueSequenced(2, 22, a.id, new MsgSetFortification(his.id, true));
		PlayAll();
		Assert.True(his.isFortified);

		// A ends: B plays on; A's orders are no longer played.
		EngineStorage.EnqueueSequenced(3, 33, a.id, new MsgEndTurn());
		EngineStorage.EnqueueSequenced(4, 44, a.id, new MsgSetFortification(his.id, false));
		PlayAll();
		Assert.Equal(0, gameData.turn);
		Assert.True(his.isFortified);
		Assert.False(TurnHandling.PlayingAtOnce(gameData, a.id));
		Assert.True(TurnHandling.PlayingAtOnce(gameData, b.id));

		// B ends: the computers, then the next turn, both at once.
		EngineStorage.EnqueueSequenced(5, 55, b.id, new MsgEndTurn());
		List<MessageToUI> heard = PlayAll();
		Assert.Equal(1, gameData.turn);
		Assert.True(TurnHandling.PlayingAtOnce(gameData, a.id));
		Assert.True(TurnHandling.PlayingAtOnce(gameData, b.id));
		Assert.Equal([a.id, b.id], heard.OfType<MsgStartTurn>().Select(m => m.playerId));
		return GameChecksum.Of(gameData);
	}

	[Fact]
	public void EveryHumanPlaysAtOnceThenTheComputers() {
		Assert.Equal(PlayARound(), PlayARound());
	}

	// The turn timer ran out: every turn still going ends.
	[Fact]
	public void TheTimeRunsOutForEveryone() {
		(C7GameData.GameData gameData, Player a, Player b) = Load();
		EngineStorage.uiControllerID = b.id;
		EngineStorage.EnqueueSequenced(1, 11, a.id, new MsgTurnTimeUp(a.id, 0));
		EngineStorage.EnqueueSequenced(2, 22, a.id, new MsgTurnTimeUp(b.id, 0));
		PlayAll();
		Assert.Equal(1, gameData.turn);
	}

	// The last one playing leaves: the round goes on without them.
	[Fact]
	public void TheLastPlayerLeavingEndsTheRound() {
		(C7GameData.GameData gameData, Player a, Player b) = Load();
		EngineStorage.EnqueueSequenced(1, 11, a.id, new MsgEndTurn());
		EngineStorage.EnqueueSequenced(2, 22, a.id, new MsgPlayerLeftGame(b.id));
		PlayAll();
		Assert.True(b.defeated);
		Assert.Equal(1, gameData.turn);
		Assert.True(TurnHandling.PlayingAtOnce(gameData, a.id));
	}

	// A popup an order brings is for its sender's computer, though another's
	// order may be played by the time it's shown.
	[Fact]
	public void WhatAnOrderBringsIsForItsSender() {
		(C7GameData.GameData gameData, Player a, Player b) = Load();
		EngineStorage.uiControllerID = b.id;
		MessageToUI moved = new MsgShowTemporaryPopup("This unit has already moved.", null);
		moved.send();
		EngineStorage.uiControllerID = a.id;
		Assert.True(MessageRecipients.ForComputerOf(moved, local: b.id, acting: a.id));
		Assert.False(MessageRecipients.ForComputerOf(moved, local: a.id, acting: a.id));
	}

	// Each computer's next unit is its own player's.
	[Fact]
	public void EachComputerMovesItsOwnUnits() {
		(C7GameData.GameData gameData, Player a, Player b) = Load();
		EngineStorage.localPlayerID = b.id;
		Assert.Equal(b, UnitInteractions.getNextSelectedUnit().owner);
		EngineStorage.localPlayerID = a.id;
		Assert.Equal(a, UnitInteractions.getNextSelectedUnit().owner);
	}

	// A third human, and a deal from A on the table for B.
	private (C7GameData.GameData, Player a, Player b, Player c) WithADeal() {
		(C7GameData.GameData gameData, Player a, Player b) = Load();
		Player c = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		c.isHuman = true;
		gameData.humanDeals.Add(new HumanDeal { from = a.id, to = b.id });
		return (gameData, a, b, c);
	}

	// B answering A's deal at the start of their turn doesn't hold up the
	// others: C plays on; B's answer then ends B's diplomacy before any other
	// order, and B's turn is ready.
	private string DealWhileOthersPlay() {
		(C7GameData.GameData gameData, Player a, Player b, Player c) = WithADeal();
		EngineStorage.EnqueueSequenced(1, 11, b.id, new MsgHearFromOthers());
		List<MessageToUI> heard = PlayAll();
		Assert.True(EngineStorage.IsWaitingForAnswerFrom<MsgDiplomacyCompleted>(b.id));
		Assert.Contains(heard, m => m is MsgShowHumanDeal d && d.playerId == b.id);
		Assert.DoesNotContain(heard, m => m is MsgTurnReady r && r.playerId == b.id);

		MapUnit hers = c.units[0];
		EngineStorage.EnqueueSequenced(2, 22, c.id, new MsgSetFortification(hers.id, true));
		PlayAll();
		Assert.True(hers.isFortified);

		// (C closing a talk screen of their own isn't B's answer.)
		EngineStorage.EnqueueSequenced(3, 33, c.id, new MsgDiplomacyCompleted());
		PlayAll();
		Assert.True(EngineStorage.IsWaitingForAnswerFrom<MsgDiplomacyCompleted>(b.id));

		EngineStorage.EnqueueSequenced(4, 44, b.id, new MsgDiplomacyCompleted());
		heard = PlayAll();
		Assert.False(EngineStorage.IsWaitingForAnswerFrom<MsgDiplomacyCompleted>(b.id));
		Assert.Contains(heard, m => m is MsgTurnReady r && r.playerId == b.id);
		return GameChecksum.Of(gameData);
	}

	[Fact]
	public void TwoHumansDealingDontHoldUpTheOthers() {
		Assert.Equal(DealWhileOthersPlay(), DealWhileOthersPlay());
	}

	// B leaves while A's deal is in front of them: it's closed for them,
	// and their civilization goes.
	[Fact]
	public void LeavingInTheMiddleOfADeal() {
		(C7GameData.GameData gameData, Player a, Player b, Player c) = WithADeal();
		EngineStorage.EnqueueSequenced(1, 11, b.id, new MsgHearFromOthers());
		PlayAll();
		EngineStorage.EnqueueSequenced(2, 22, a.id, new MsgPlayerLeftGame(b.id));
		PlayAll();
		Assert.False(EngineStorage.IsWaitingForAnswerFrom<MsgDiplomacyCompleted>(b.id));
		Assert.True(b.defeated);
	}

	// Online, AI diplomacy off: the AIs talk to no human (an old save: on).
	[Fact]
	public void TheAIsTalkToHumansOnlyWithAIDiplomacy() {
		(C7GameData.GameData gameData, Player a, Player b) = Load();
		Player ai = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		Assert.True(gameData.aiDiplomacy);
		Assert.True(TurnHandling.TalksTo(gameData, a, null));
		gameData.aiDiplomacy = false;
		Assert.False(TurnHandling.TalksTo(gameData, a, null));
		Assert.False(TurnHandling.TalksTo(gameData, a, a));
		Assert.True(TurnHandling.TalksTo(gameData, ai, null));
		Assert.False(SaveGame.FromJson(SaveGame.FromGameData(gameData).ToJson()).ToGameData(fixture.behaviors).aiDiplomacy);
		Assert.True(System.Text.Json.JsonSerializer.Deserialize<SaveGame>("{}", new System.Text.Json.JsonSerializerOptions { IncludeFields = true }).AiDiplomacy);
	}

	// A new game with simultaneous moves begins with every human's turn.
	[Fact]
	public void ANewGameBeginsWithEveryonesTurn() {
		C7GameData.GameData gameData = SaveGame.FromJson(save).ToGameData(fixture.behaviors);
		Assert.False(gameData.simultaneousMoves);
		SaveGame saved = SaveGame.FromGameData(gameData);
		saved.SimultaneousMoves = true;
		saved.SimultaneousTurnStarted = 0;
		C7GameData.GameData again = SaveGame.FromJson(saved.ToJson()).ToGameData(fixture.behaviors);
		Assert.True(again.simultaneousMoves);
		Assert.True(TurnHandling.PlayingAtOnce(again, again.players.First(p => p.isHuman).id));
	}
}

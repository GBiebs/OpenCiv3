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

// Online, a player who leaves (or loses their connection): every game
// takes their civilization out at the same point, answers what the
// engine was asking them, and plays on.
public class PlayerLeavingTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly byte[] save;
	private readonly ID controller = EngineStorage.uiControllerID;

	public PlayerLeavingTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		save = fixture.saveGame.ToJson();
	}

	public void Dispose() {
		EngineStorage.AbandonWork();
		EngineStorage.animationsEnabled = true;
		EngineStorage.uiControllerID = controller;
	}

	// Two humans: the host's (first) and another.
	private (C7GameData.GameData, Player host, Player other) Load() {
		C7GameData.GameData gameData = SaveGame.FromJson(save).ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.animationsEnabled = false;
		EngineStorage.AbandonWork();
		Player host = gameData.players.First(p => p.isHuman);
		Player other = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		other.isHuman = true;
		EngineStorage.uiControllerID = host.id;
		return (gameData, host, other);
	}

	private static List<MessageToUI> PlayAll() {
		List<MessageToUI> heard = new();
		for (int i = 0; i < 100_000 && (EngineStorage.sequencedOrders.Count > 0 || !EngineStorage.OrdersDone()); i++) {
			EngineStorage.ProcessNextMessageToEngine();
			while (EngineStorage.TryDequeueNextMessageToUI(out MessageToUI message)) {
				heard.Add(message);
			}
		}
		Assert.Empty(EngineStorage.sequencedOrders);
		Assert.True(EngineStorage.OrdersDone());
		return heard;
	}

	private static void AssertGone(C7GameData.GameData gameData, Player player) {
		Assert.True(player.defeated);
		Assert.Empty(player.cities);
		Assert.Empty(player.units);
		Assert.DoesNotContain(gameData.cities, c => c.owner == player);
		Assert.DoesNotContain(gameData.mapUnits, u => u.owner == player);
	}

	// They leave in their own turn, with a city: it all goes, and the
	// computers' turns and the next turn follow. Two games come out the same.
	private string LeaveInTheirTurn() {
		(C7GameData.GameData gameData, Player host, Player other) = Load();
		EngineStorage.EnqueueSequenced(1, 11, host.id, new MsgEndTurn());
		PlayAll();
		Assert.Equal(other.id, EngineStorage.uiControllerID);
		MapUnit settler = other.units.First(u => u.unitType.actions.Contains(UnitAction.BuildCity));
		EngineStorage.EnqueueSequenced(2, 22, other.id, new MsgBuildCity(settler, "Uruk"));
		PlayAll();
		Assert.Single(other.cities);

		EngineStorage.EnqueueSequenced(3, 33, host.id, new MsgPlayerLeftGame(other.id));
		List<MessageToUI> heard = PlayAll();
		AssertGone(gameData, other);
		Assert.Contains(heard, m => m is MsgCivilizationDestroyed d && d.civilization == other.civilization);
		Assert.Contains(heard, m => m is MsgCityDestroyed);
		Assert.Equal(1, gameData.turn);
		Assert.Equal(host.id, EngineStorage.uiControllerID);
		return GameChecksum.Of(gameData);
	}

	[Fact]
	public void TheirCivilizationGoesAndTheGamePlaysOn() {
		Assert.Equal(LeaveInTheirTurn(), LeaveInTheirTurn());
	}

	// They leave during another's turn: that turn goes on, and theirs is
	// passed over from then on.
	[Fact]
	public void LeavingDuringAnothersTurn() {
		(C7GameData.GameData gameData, Player host, Player other) = Load();
		EngineStorage.EnqueueSequenced(1, 11, host.id, new MsgPlayerLeftGame(other.id));
		PlayAll();
		AssertGone(gameData, other);
		Assert.Equal(host.id, EngineStorage.uiControllerID);
		Assert.Equal(0, gameData.turn);

		EngineStorage.EnqueueSequenced(2, 22, host.id, new MsgEndTurn());
		List<MessageToUI> heard = PlayAll();
		Assert.DoesNotContain(heard, m => m is MsgStartTurn s && s.playerId == other.id);
		Assert.Equal(1, gameData.turn);
		Assert.Equal(host.id, EngineStorage.uiControllerID);
	}

	// The engine was asking them something (an AI's question once they'd
	// played): it's answered as closing the popup would, and the work
	// carries on.
	[Fact]
	public async Task WhatTheEngineAskedThemIsAnswered() {
		(C7GameData.GameData gameData, Player host, Player other) = Load();
		other.hasPlayedThisTurn = true;
		EngineStorage.uiControllerID = other.id;
		Asks<MsgVote> asking = new();
		EngineStorage.EnqueueSequenced(1, 11, other.id, asking);
		for (int i = 0; i < 5; i++) {
			EngineStorage.ProcessNextMessageToEngine();
		}
		Assert.True(EngineStorage.IsWaitingForMessageToEngine<MsgVote>());

		EngineStorage.EnqueueSequenced(2, 22, host.id, new MsgPlayerLeftGame(other.id));
		for (int i = 0; i < 300 && !EngineStorage.OrdersDone(); i++) {
			EngineStorage.ProcessNextMessageToEngine();
			await Task.Delay(10);
		}
		Assert.True(EngineStorage.OrdersDone());
		Assert.Null(Assert.IsType<MsgVote>(asking.answer).candidateId);
		AssertGone(gameData, other);
	}

	// Every question the engine waits on has an answer: if one were
	// missing, the game would wait for the player who left forever.
	[Fact]
	public void EveryQuestionHasItsAnswer() {
		Type[] questions = [typeof(MsgDiplomacyCompleted), typeof(MsgTributeAnswer), typeof(MsgEnvoyAnswer), typeof(MsgStealthTarget),
			typeof(MsgCallElection), typeof(MsgVote), typeof(MsgNewsRead)];
		foreach (Type question in questions) {
			Assert.IsType(question, LeavingPlayers.AnswerFor([question]));
		}
		Assert.False(Assert.IsType<MsgCallElection>(LeavingPlayers.AnswerFor([typeof(MsgCallElection)])).call);
		Assert.Null(Assert.IsType<MsgStealthTarget>(LeavingPlayers.AnswerFor([typeof(MsgStealthTarget)])).targetId);
		Assert.Null(LeavingPlayers.AnswerFor([typeof(MsgEndTurn)]));
	}

	private class Asks<T> : MessageToEngine where T : MessageToEngine {
		public T answer;

		public override void process() => _ = ProcessAsync();

		public override async Task ProcessAsync() => answer = await EngineStorage.WaitForMessageToEngine<T>();
	}
}

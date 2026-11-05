using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;

namespace EngineTests.Online;

// Online, Civ3's turn timer: how long a turn has, and a turn that runs
// out ending in every game.
public class TurnTimerTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly byte[] save;
	private readonly ID controller = EngineStorage.uiControllerID;

	public TurnTimerTest(SaveGameFixture fixture) {
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

	private static void PlayAll() {
		for (int i = 0; i < 100_000 && (EngineStorage.sequencedOrders.Count > 0 || !EngineStorage.OrdersDone()); i++) {
			EngineStorage.ProcessNextMessageToEngine();
			EngineStorage.messagesToUI.Clear();
		}
		Assert.True(EngineStorage.OrdersDone());
	}

	// The rules' base, plus a city's and a unit's seconds, times the speed.
	[Fact]
	public void ATurnsTimeGrowsWithTheEmpire() {
		(C7GameData.GameData gameData, Player host, _) = Load();
		Rules rules = gameData.rules;
		int units = gameData.mapUnits.Count(u => u.owner == host);
		int plain = rules.TurnTimerBaseSeconds + rules.TurnTimerSecondsPerCity * host.cities.Count + rules.TurnTimerSecondsPerUnit * units;
		Assert.True(units > 0);
		gameData.turnTimer = TurnTimer.Fast;
		Assert.Equal(plain * rules.TurnTimerFastPercent / 100, TurnTimes.Seconds(gameData, host));
		gameData.turnTimer = TurnTimer.Slow;
		Assert.Equal(plain * rules.TurnTimerSlowPercent / 100, TurnTimes.Seconds(gameData, host));
		Assert.True(TurnTimes.Seconds(gameData, host) > plain);
		gameData.turnTimer = TurnTimer.None;
		Assert.Equal(0, TurnTimes.Seconds(gameData, host));
	}

	// Time's up: their turn ends, as their End Turn would.
	[Fact]
	public void ATurnThatRunsOutEnds() {
		(C7GameData.GameData gameData, Player host, Player other) = Load();
		EngineStorage.EnqueueSequenced(1, 11, host.id, new MsgTurnTimeUp(host.id, 0));
		PlayAll();
		Assert.True(host.hasPlayedThisTurn);
		Assert.Equal(other.id, EngineStorage.uiControllerID);
	}

	// Their End Turn on its way when the time ran out: the time's up doesn't
	// end the next turn too (nor a turn it wasn't for).
	[Fact]
	public void OnlyTheTurnThatRanOutEnds() {
		(C7GameData.GameData gameData, Player host, Player other) = Load();
		EngineStorage.EnqueueSequenced(1, 11, host.id, new MsgEndTurn());
		EngineStorage.EnqueueSequenced(2, 22, host.id, new MsgTurnTimeUp(host.id, 0));
		EngineStorage.EnqueueSequenced(3, 33, host.id, new MsgTurnTimeUp(other.id, 5));
		PlayAll();
		Assert.Equal(other.id, EngineStorage.uiControllerID);
		Assert.False(other.hasPlayedThisTurn);
		Assert.Equal(0, gameData.turn);
	}

	[Fact]
	public void ASaveKeepsTheTurnTimer() {
		(C7GameData.GameData gameData, _, _) = Load();
		gameData.turnTimer = TurnTimer.Slow;
		C7GameData.GameData again = SaveGame.FromJson(SaveGame.FromGameData(gameData).ToJson()).ToGameData(fixture.behaviors);
		Assert.Equal(TurnTimer.Slow, again.turnTimer);
	}
}

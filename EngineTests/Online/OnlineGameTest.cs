using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Online;
using C7GameData;
using C7GameData.Save;
using EngineTests.Utils;
using Xunit;
using static EngineTests.Online.OnlineTestKit;

namespace EngineTests.Online;

// A game in progress online, over this computer's network: the host puts
// orders in sequence, a player's game sends its orders and plays the
// host's, and the games compare their fingerprints.
public class OnlineGameTest : IClassFixture<SaveGameFixture>, IDisposable {
	private readonly SaveGameFixture fixture;
	private readonly C7GameData.GameData gameData;
	private readonly Player hostPlayer;
	private readonly Player remotePlayer;
	private readonly ID controller = EngineStorage.uiControllerID;
	private readonly List<IDisposable> toDispose = new();
	// (The one HostWithAPlayer starts.)
	private OnlineHost host;

	public OnlineGameTest(SaveGameFixture fixture) {
		this.fixture = fixture;
		gameData = fixture.saveGame.Clone().ToGameData(fixture.behaviors);
		EngineStorage.InitializeGameDataForTests(gameData);
		EngineStorage.AbandonWork();
		hostPlayer = gameData.players.First(p => p.isHuman);
		remotePlayer = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		remotePlayer.isHuman = true;
		EngineStorage.uiControllerID = hostPlayer.id;
	}

	public void Dispose() {
		foreach (IDisposable d in toDispose) {
			d.Dispose();
		}
		EngineStorage.AbandonWork();
		EngineStorage.sendOrder = null;
		EngineStorage.onEndTurnDone = null;
		EngineStorage.uiControllerID = controller;
	}

	private async Task<(HostGame, OnlineClient)> HostWithAPlayer() {
		host = StartHost();
		OnlineClient client = await Join(host);
		await Next<PlayerJoined>(host);
		HostGame game = new(host, hostPlayer.id, new Dictionary<int, ID> { [client.playerId] = remotePlayer.id });
		game.Begin();
		toDispose.Add(client);
		toDispose.Add(game);
		return (game, client);
	}

	private async Task<T> Until<T>(HostGame game, OnlineClient client, Func<T, bool> where = null, int milliseconds = 3000) where T : OnlineMessage {
		for (int waited = 0; waited < milliseconds; waited += 10) {
			game.Poll(gameData);
			while (client.TryNextMessage(out OnlineMessage message)) {
				if (message is T found && (where == null || where(found))) {
					return found;
				}
			}
			await Task.Delay(10);
		}
		return null;
	}

	[Fact]
	public async Task TheHostsOwnOrdersGoInSequenceToEveryGame() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		MapUnit unit = hostPlayer.units[0];
		new MsgSetFortification(unit.id, true).send();
		SequencedOrder sent = await Until<SequencedOrder>(game, client);
		Assert.Equal(1, sent.seq);
		Assert.Equal(hostPlayer.id.ToString(), sent.from);
		Assert.Equal("MsgSetFortification", sent.orderType);
		(long seq, int seed, ID from, MessageToEngine order) = Assert.Single(EngineStorage.sequencedOrders);
		Assert.Equal(sent.seed, seed);
		Assert.Equal(hostPlayer.id, from);
	}

	// A player's order: in sequence when they're in control, else not.
	[Fact]
	public async Task APlayersOrderGoesInSequenceOnlyInTheirTurn() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		MapUnit unit = remotePlayer.units[0];
		OrderRequest request = new() { orderType = "MsgSetFortification", order = OrderCodec.Write(new MsgSetFortification(unit.id, true)) };
		client.Send(request);
		Assert.Null(await Until<SequencedOrder>(game, client, milliseconds: 500));
		Assert.Empty(EngineStorage.sequencedOrders);

		EngineStorage.uiControllerID = remotePlayer.id;
		client.Send(new OrderRequest { orderType = "MsgSetFortification", order = "{\"nonsense\":1}" });
		client.Send(request);
		SequencedOrder sent = await Until<SequencedOrder>(game, client);
		Assert.Equal(remotePlayer.id.ToString(), sent.from);
		Assert.Equal(1, sent.seq);
		Assert.Single(EngineStorage.sequencedOrders);
	}

	// Each game's fingerprint after an End Turn: the host sees a game that
	// went its own way.
	[Fact]
	public async Task TheHostSeesAGameThatWentItsOwnWay() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		bool outOfSync = false;
		game.OutOfSync += () => outOfSync = true;
		string ours = GameChecksum.Of(gameData);
		EngineStorage.onEndTurnDone(5);
		client.Send(new Checksum { seq = 5, hash = ours });
		await Until<ChatLine>(game, client, milliseconds: 300);
		Assert.False(outOfSync);

		EngineStorage.onEndTurnDone(6);
		client.Send(new Checksum { seq = 6, hash = new string('0', 64) });
		await Until<ChatLine>(game, client, milliseconds: 300);
		Assert.True(outOfSync);
	}

	[Fact]
	public async Task EveryoneHearsWhoSaysWhatAndWhoLeaves() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		List<string> notices = new();
		game.Notice += notices.Add;
		client.Send(new Chat { text = "Your move" });
		Assert.Equal("Sargon: Your move", (await Until<ChatLine>(game, client)).text);
		client.Dispose();
		for (int i = 0; i < 300 && !notices.Contains("Sargon has left the game."); i++) {
			game.Poll(gameData);
			await Task.Delay(10);
		}
		Assert.Contains("Sargon has left the game.", notices);
	}

	private async Task UntilAway(HostGame game) {
		for (int i = 0; i < 300 && game.Away.Count == 0; i++) {
			game.Poll(gameData);
			await Task.Delay(10);
		}
	}

	// A player who loses their connection (Civ3's "Waiting on Player"): the
	// game waits for them; once the host drops them, every game takes their
	// civilization out, in sequence, whoever is in control.
	[Fact]
	public async Task APlayerWhoLeavesIsWaitedForUntilTheHostDropsThem() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		int changed = 0;
		game.AwayChanged += () => changed++;
		client.Dispose();
		await UntilAway(game);
		Assert.Equal("Sargon", game.Away[remotePlayer.id]);
		Assert.Equal(1, changed);
		Assert.Empty(EngineStorage.sequencedOrders);

		game.Drop(remotePlayer.id);
		Assert.Empty(game.Away);
		(long seq, int seed, ID from, MessageToEngine order) = Assert.Single(EngineStorage.sequencedOrders);
		Assert.Equal(remotePlayer.id, Assert.IsType<MsgPlayerLeftGame>(order).playerId);
		Assert.Equal(hostPlayer.id, from);
		Assert.Equal(2, changed);
	}

	// They join again with the nickname they played with: their civ is
	// theirs again, and every game begins again from the host's copy (they
	// get it then). Anyone else is turned away.
	[Fact]
	public async Task APlayerWhoLeftRejoins() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		bool rejoined = false;
		game.Rejoined += () => rejoined = true;
		client.Dispose();
		await UntilAway(game);

		using OnlineClient stranger = await Join(host, "Ur-Nammu");
		for (int i = 0; i < 100; i++) {
			game.Poll(gameData);
			await Task.Delay(10);
		}
		Goodbye refused = await Next<Goodbye>(stranger);
		Assert.Equal("The game has already begun. To take your seat again, join with the nickname you played with.", refused.reason);
		Assert.False(rejoined);

		using OnlineClient back = await Join(host, "Sargon");
		for (int i = 0; i < 300 && !rejoined; i++) {
			game.Poll(gameData);
			await Task.Delay(10);
		}
		Assert.True(rejoined);
		Assert.Empty(game.Away);
		Assert.Equal("Rejoining the game...", (await Next<LobbyNotice>(back)).text);
		game.SendGame(OnlineGame.Pack(SaveGame.FromGameData(gameData)));
		GameStart start = await Next<GameStart>(back);
		Assert.Equal(remotePlayer.id.ToString(), start.you);
		Assert.True(start.resumed);
	}

	// While they're away, what the engine asks them is answered for them,
	// so the game doesn't wait on them for it.
	[Fact]
	public async Task WhatTheEngineAsksOneAwayIsAnswered() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		client.Dispose();
		await UntilAway(game);
		EngineStorage.uiControllerID = remotePlayer.id;
		_ = EngineStorage.WaitForMessageToEngine<MsgVote>();
		game.Poll(gameData);
		(long seq, int seed, ID from, MessageToEngine order) = Assert.Single(EngineStorage.sequencedOrders);
		Assert.Null(Assert.IsType<MsgVote>(order).candidateId);
		Assert.Equal(remotePlayer.id, from);
		// (Once.)
		game.Poll(gameData);
		Assert.Single(EngineStorage.sequencedOrders);
	}

	// Civ3's chat balloons: a line goes to the players it's for, and its
	// sayer, and says to whom; every game knows who plays each civ.
	[Fact]
	public async Task ChatGoesToThePlayersItsFor() {
		host = StartHost();
		using OnlineClient sargon = await Join(host, "Sargon");
		using OnlineClient urNammu = await Join(host, "Ur-Nammu");
		await Next<PlayerJoined>(host);
		await Next<PlayerJoined>(host);
		Player third = gameData.players.First(p => !p.isHuman && !p.isBarbarians);
		third.isHuman = true;
		HostGame game = new(host, hostPlayer.id, new Dictionary<int, ID> { [sargon.playerId] = remotePlayer.id, [urNammu.playerId] = third.id });
		toDispose.Add(game);
		List<string> notices = new();
		game.Notice += notices.Add;
		game.Begin();
		PlayersInGame players = await Next<PlayersInGame>(urNammu);
		Assert.Equal(["Hammurabi", "Sargon", "Ur-Nammu"], players.players.Select(p => p.name));
		Assert.Equal("Ur-Nammu", game.Names[third.id]);

		async Task<List<string>> Heard(OnlineClient client) {
			List<string> lines = new();
			for (int i = 0; i < 30; i++) {
				game.Poll(gameData);
				while (client.TryNextMessage(out OnlineMessage message)) {
					if (message is ChatLine line) {
						lines.Add(line.text);
					}
				}
				await Task.Delay(10);
			}
			return lines;
		}
		// Sargon to the host only.
		sargon.Send(new Chat { text = "Psst", to = [hostPlayer.id.ToString()] });
		Assert.Empty(await Heard(urNammu));
		Assert.Equal(["Sargon (to Hammurabi): Psst"], await Heard(sargon));
		Assert.Contains("Sargon (to Hammurabi): Psst", notices);

		// The host to Ur-Nammu only.
		game.Say("Ally?", [third.id]);
		Assert.Equal(["Hammurabi (to Ur-Nammu): Ally?"], await Heard(urNammu));
		Assert.Empty(await Heard(sargon));

		// Everyone.
		sargon.Send(new Chat { text = "Hello all" });
		Assert.Equal(["Sargon: Hello all"], await Heard(urNammu));
		Assert.Contains("Sargon: Hello all", notices);
	}

	// Civ3's live diplomacy over the network: a player's envoy reaches the
	// host, who hears it; both accept the table, and the trade goes in
	// sequence for every game, whoever is in control.
	[Fact]
	public async Task TwoPlayersTradeAtTheTable() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		hostPlayer.EnsureRelationshipExists(remotePlayer);
		hostPlayer.gold = 50;
		List<OnlineMessage> hostHeard = new();
		game.TalkHeard += hostHeard.Add;
		client.Send(new TalkEnvoy { with = hostPlayer.id.ToString() });
		for (int i = 0; i < 300 && hostHeard.Count == 0; i++) {
			game.Poll(gameData);
			await Task.Delay(10);
		}
		Assert.Equal(remotePlayer.id.ToString(), Assert.IsType<TalkEnvoy>(Assert.Single(hostHeard)).with);

		game.Talk(new TalkAnswer { with = remotePlayer.id.ToString(), answer = TalkAnswer.Hear });
		Assert.True((await Until<TalkState>(game, client)).envoyFromUs);
		game.Talk(new TalkTable { with = remotePlayer.id.ToString(), terms = LiveTalks.Write(new TradeOffer { gold = 20 }), accepted = true });
		TalkState table = await Until<TalkState>(game, client, s => s.theyAccepted);
		Assert.Equal(20, LiveTalks.Read(table.theirTerms, gameData).gold);
		client.Send(new TalkTable { with = hostPlayer.id.ToString(), terms = "", accepted = true });
		Assert.Equal(TalkEnd.Done, (await Until<TalkEnd>(game, client)).reason);
		(long seq, int seed, ID from, MessageToEngine order) = Assert.Single(EngineStorage.sequencedOrders);
		MsgHumansTrade trade = Assert.IsType<MsgHumansTrade>(order);
		Assert.Equal(hostPlayer.id, trade.bId);
		Assert.Equal(20, trade.bGives.gold);
		Assert.True(trade.HostOnly);
	}

	// Civ3's turn timer: when a human's turn begins, everyone is told its
	// time; once it's up (and the game is settled) every game ends it, once.
	[Fact]
	public async Task TheTurnTimerEndsATurnThatRunsOut() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		gameData.turnTimer = TurnTimer.Fast;
		int seconds = TurnTimes.Seconds(gameData, hostPlayer);
		List<(ID, int)> started = new();
		game.TimerStarted += (player, s) => started.Add((player, s));
		DateTime start = DateTime.UtcNow;
		game.WatchTheClock(gameData, start);
		TurnTimerStarted told = await Until<TurnTimerStarted>(game, client);
		Assert.Equal(hostPlayer.id.ToString(), told.player);
		Assert.Equal(seconds, told.seconds);
		Assert.Equal([(hostPlayer.id, seconds)], started);

		game.WatchTheClock(gameData, start.AddSeconds(seconds - 1));
		Assert.Empty(EngineStorage.sequencedOrders);
		game.WatchTheClock(gameData, start.AddSeconds(seconds));
		(long seq, int seed, ID from, MessageToEngine order) = Assert.Single(EngineStorage.sequencedOrders);
		MsgTurnTimeUp up = Assert.IsType<MsgTurnTimeUp>(order);
		Assert.Equal(hostPlayer.id, up.playerId);
		Assert.Equal(gameData.turn, up.turn);
		// (Once, even after that order is played.)
		EngineStorage.ResetSequence(EngineStorage.lastSequenced);
		game.WatchTheClock(gameData, start.AddSeconds(seconds + 10));
		Assert.Empty(EngineStorage.sequencedOrders);
	}

	// Simultaneous moves: the timer times the round; once it's up, every
	// turn still going ends.
	[Fact]
	public async Task TheTurnTimerTimesTheRound() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		gameData.turnTimer = TurnTimer.Fast;
		gameData.simultaneousMoves = true;
		gameData.simultaneousTurnStarted = gameData.turn;
		int seconds = Math.Max(TurnTimes.Seconds(gameData, hostPlayer), TurnTimes.Seconds(gameData, remotePlayer));
		DateTime start = DateTime.UtcNow;
		game.WatchTheClock(gameData, start);
		TurnTimerStarted told = await Until<TurnTimerStarted>(game, client);
		Assert.True(told.round);
		Assert.Equal(seconds, told.seconds);
		game.WatchTheClock(gameData, start.AddSeconds(seconds));
		Assert.Equal([hostPlayer.id, remotePlayer.id], EngineStorage.sequencedOrders.Select(o => Assert.IsType<MsgTurnTimeUp>(o.order).playerId));
	}

	// No timer, or a computer playing: nothing is timed.
	[Fact]
	public async Task OnlyAHumansTurnIsTimed() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		DateTime start = DateTime.UtcNow;
		game.WatchTheClock(gameData, start);
		game.WatchTheClock(gameData, start.AddHours(1));
		gameData.turnTimer = TurnTimer.Fast;
		EngineStorage.uiControllerID = gameData.players.First(p => !p.isHuman && !p.isBarbarians).id;
		game.WatchTheClock(gameData, start);
		game.WatchTheClock(gameData, start.AddHours(1));
		Assert.Null(await Until<TurnTimerStarted>(game, client, milliseconds: 300));
		Assert.Empty(EngineStorage.sequencedOrders);
	}

	// Only the host says who has left.
	[Fact]
	public async Task APlayerCantSayAnotherHasLeft() {
		(HostGame game, OnlineClient client) = await HostWithAPlayer();
		EngineStorage.uiControllerID = remotePlayer.id;
		client.Send(new OrderRequest { orderType = "MsgPlayerLeftGame", order = OrderCodec.Write(new MsgPlayerLeftGame(hostPlayer.id)) });
		Assert.Null(await Until<SequencedOrder>(game, client, milliseconds: 500));
		Assert.Empty(EngineStorage.sequencedOrders);
	}

	// A player's game: its orders go to the host, the host's in sequence
	// are played, and a new start comes when the host sends one.
	[Fact]
	public async Task APlayersGameSendsItsOrdersAndPlaysTheHosts() {
		using OnlineHost host = StartHost();
		OnlineClient client = await Join(host);
		RemotePlayer remote = (await Next<PlayerJoined>(host)).player;
		ClientGame game = new(client, remotePlayer.id);
		toDispose.Add(game);
		game.Begin();
		GameStart restarted = null;
		string ended = null;
		game.Restart += start => restarted = start;
		game.Ended += reason => ended = reason;

		MapUnit unit = remotePlayer.units[0];
		new MsgSetFortification(unit.id, true).send();
		OrderRequest request = Assert.IsType<OrderRequest>((await Next<MessageFrom>(host)).message);
		Assert.Equal("MsgSetFortification", request.orderType);
		Assert.Empty(EngineStorage.sequencedOrders);

		host.Send(remote.id, new SequencedOrder { seq = 1, seed = 9, from = remotePlayer.id.ToString(), orderType = request.orderType, order = request.order });
		host.Send(remote.id, new GameStart { save = OnlineGame.Pack(fixture.saveGame), you = remotePlayer.id.ToString(), seq = 1 });
		for (int i = 0; i < 300 && (EngineStorage.sequencedOrders.Count == 0 || restarted == null); i++) {
			game.Poll(gameData);
			await Task.Delay(10);
		}
		(long seq, int seed, ID from, MessageToEngine order) = Assert.Single(EngineStorage.sequencedOrders);
		Assert.Equal((1, 9), (seq, seed));
		Assert.Equal(remotePlayer.id, from);
		Assert.IsType<MsgSetFortification>(order);
		Assert.Equal(remotePlayer.id.ToString(), restarted.you);

		host.Dispose();
		for (int i = 0; i < 300 && ended == null; i++) {
			game.Poll(gameData);
			await Task.Delay(10);
		}
		Assert.Equal("The host has stopped the game.", ended);
	}

	// The game's messages are ones the other side reads.
	[Fact]
	public void TheGamesMessagesAreRead() {
		OnlineMessage[] messages = [
			new SequencedOrder { seq = 1, seed = 9, from = remotePlayer.id.ToString(), orderType = "MsgEndTurn", order = "{}" },
			new OrderRequest { orderType = "MsgEndTurn", order = "{}" },
			new Checksum { seq = 3, hash = new string('A', 64) },
			new GameStart { save = "abc", you = remotePlayer.id.ToString(), seq = 0 },
		];
		foreach (OnlineMessage message in messages) {
			Assert.True(OnlineMessage.Read(OnlineMessage.Write(message)) != null, message.GetType().Name + ": " + System.Text.Encoding.UTF8.GetString(OnlineMessage.Write(message)));
		}
	}

	// The game as it goes over the network: the same save back, without the
	// host's scenario folders; and nothing too big once unpacked.
	[Fact]
	public void TheGameGoesOverTheNetworkWhole() {
		SaveGame save = fixture.saveGame.Clone();
		save.ScenarioSearchPath = "C:/the host's folders";
		SaveGame back = OnlineGame.Unpack(OnlineGame.Pack(save));
		Assert.Null(back.ScenarioSearchPath);
		back.ScenarioSearchPath = save.ScenarioSearchPath;
		Assert.Equal(save.ToJson(), back.ToJson());
		Assert.Throws<InvalidDataException>(() => OnlineGame.Unpack(OnlineGame.Pack(save), limit: 1000));
	}
}

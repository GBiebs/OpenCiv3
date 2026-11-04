using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine.Online;
using C7GameData;
using Xunit;
using static EngineTests.Online.OnlineTestKit;

namespace EngineTests.Online;

// Before an online game: the civs humans play, and who takes which.
public class OnlineLobbyTest : IDisposable {
	private readonly OnlineHost host;
	private readonly OnlineLobby lobby;
	private readonly ID celts = new("Player", 4);

	public OnlineLobbyTest() {
		OnlineLimits limits = Quick();
		limits.wrongPasswordsBeforeLockout = 3;
		host = StartHost(limits);
		lobby = new OnlineLobby(host, new List<OnlineLobby.Seat> {
			new() { number = 0, player = new ID("Player", 2), civ = "Romans", leader = "Caesar" },
			new() { number = 1, player = new ID("Player", 3), civ = "Maya", leader = "Smoke-Jaguar" },
			new() { number = 2, player = celts, civ = "Celts", leader = "Brennus", password = true },
		}, (player, password) => player == celts && password == "carnyx");
		lobby.TakeForHost(0);
	}

	public void Dispose() => host.Dispose();

	// The host's events go to the lobby until `client` hears a T.
	private async Task<T> Until<T>(OnlineClient client, Func<T, bool> where = null) where T : OnlineMessage {
		for (int i = 0; i < 300; i++) {
			while (host.TryNextEvent(out HostEvent hostEvent)) {
				lobby.Handle(hostEvent);
			}
			while (client.TryNextMessage(out OnlineMessage message)) {
				if (message is T found && (where == null || where(found))) {
					return found;
				}
			}
			await Task.Delay(10);
		}
		throw new TimeoutException($"No {typeof(T).Name}");
	}

	private static int? TakenBy(LobbyState state, string civ) => state.seats.Single(s => s.civ == civ).player;

	[Fact]
	public async Task PlayersSeeTheSeatsAndEachOther() {
		using OnlineClient sargon = await Join(host, "Sargon");
		LobbyState state = await Until<LobbyState>(sargon, s => s.players.Count == 2);
		Assert.Equal(["Hammurabi", "Sargon"], state.players.Select(p => p.name));
		Assert.Equal(["Romans", "Maya", "Celts"], state.seats.Select(s => s.civ));
		Assert.Equal(OnlineLobby.Host, TakenBy(state, "Romans"));
		Assert.Null(TakenBy(state, "Maya"));
		Assert.True(state.seats.Single(s => s.civ == "Celts").password);
	}

	[Fact]
	public async Task APlayerTakesAFreeSeat() {
		using OnlineClient sargon = await Join(host, "Sargon");
		using OnlineClient naram = await Join(host, "Naram-Sin");
		sargon.Send(new TakeSeat { seat = 1 });
		LobbyState state = await Until<LobbyState>(naram, s => TakenBy(s, "Maya") != null);
		Assert.Equal(sargon.playerId, TakenBy(state, "Maya"));

		naram.Send(new TakeSeat { seat = 1 });
		Assert.Equal("Someone has already taken the Maya.", (await Until<LobbyNotice>(naram)).text);
		naram.Send(new TakeSeat { seat = 0 });
		Assert.Equal("Someone has already taken the Romans.", (await Until<LobbyNotice>(naram)).text);
		naram.Send(new TakeSeat { seat = 7 });
		Assert.Equal("There's no such seat.", (await Until<LobbyNotice>(naram)).text);
		Assert.False(lobby.Full);
	}

	// A player has one seat at a time, and gives it up by leaving it or
	// the game.
	[Fact]
	public async Task SeatsAreFreedAgain() {
		using OnlineClient sargon = await Join(host, "Sargon");
		sargon.Send(new TakeSeat { seat = 1 });
		await Until<LobbyState>(sargon, s => TakenBy(s, "Maya") == sargon.playerId);
		sargon.Send(new TakeSeat { seat = 2, password = "carnyx" });
		LobbyState moved = await Until<LobbyState>(sargon, s => TakenBy(s, "Celts") == sargon.playerId);
		Assert.Null(TakenBy(moved, "Maya"));
		sargon.Send(new LeaveSeat());
		await Until<LobbyState>(sargon, s => TakenBy(s, "Celts") == null);

		using OnlineClient naram = await Join(host, "Naram-Sin");
		naram.Send(new TakeSeat { seat = 1 });
		await Until<LobbyState>(naram, s => TakenBy(s, "Maya") == naram.playerId);
		naram.Dispose();
		await Until<LobbyState>(sargon, s => TakenBy(s, "Maya") == null && s.players.Count == 2);
	}

	// A loaded game's civ with a password, which can't be guessed either.
	[Fact]
	public async Task ACivsPasswordIsAsked() {
		using OnlineClient sargon = await Join(host, "Sargon");
		sargon.Send(new TakeSeat { seat = 2, password = "lyre" });
		Assert.Equal("That isn't the password for the Celts.", (await Until<LobbyNotice>(sargon)).text);
		sargon.Send(new TakeSeat { seat = 2 });
		await Until<LobbyNotice>(sargon);
		sargon.Send(new TakeSeat { seat = 2, password = "harp" });
		await Until<LobbyNotice>(sargon);
		sargon.Send(new TakeSeat { seat = 2, password = "carnyx" });
		Assert.Equal("Too many wrong passwords. Try again later.", (await Until<LobbyNotice>(sargon)).text);
		Assert.Null(lobby.seats[2].takenBy);
	}

	[Fact]
	public async Task TheLobbyIsFullWhenEverySeatIsTaken() {
		using OnlineClient sargon = await Join(host, "Sargon");
		using OnlineClient naram = await Join(host, "Naram-Sin");
		sargon.Send(new TakeSeat { seat = 1 });
		naram.Send(new TakeSeat { seat = 2, password = "carnyx" });
		await Until<LobbyState>(sargon, s => s.seats.All(seat => seat.player != null));
		Assert.True(lobby.Full);
		Assert.Equal(naram.playerId, lobby.SeatOf(naram.playerId).takenBy);
	}
}

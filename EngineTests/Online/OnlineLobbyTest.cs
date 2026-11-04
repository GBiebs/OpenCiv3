using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine.Online;
using C7GameData;
using Xunit;
using static C7Engine.Online.OnlineLobby;
using static EngineTests.Online.OnlineTestKit;

namespace EngineTests.Online;

// Before a multiplayer game, Civ3's multiplayer setup: slots, civs, who's
// ready, the messages box.
public class OnlineLobbyTest : IDisposable {
	private readonly OnlineHost host;
	private readonly OnlineLobby lobby;
	private static readonly List<string> Civs = ["Babylon", "Rome", "Maya", "Celts"];

	public OnlineLobbyTest() {
		OnlineLimits limits = Quick();
		limits.wrongPasswordsBeforeLockout = 3;
		host = StartHost(limits);
		// The host, two open slots, a computer.
		lobby = new OnlineLobby(host, NewSlots(civCount: 4, humans: 3, online: true), Civs);
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

	private static LobbySlot SlotOf(LobbyState state, int number) => state.slots.Single(s => s.slot == number);

	[Fact]
	public void ANewGameHasTheHostThenHumansThenComputers() {
		List<Slot> slots = NewSlots(civCount: 5, humans: 2, online: true);
		Assert.Equal(LobbyState.MaxSlots, slots.Count);
		Assert.Equal([Kind.Human, Kind.Open, Kind.Computer, Kind.Computer, Kind.Computer, Kind.Closed, Kind.Closed, Kind.Closed], slots.Select(s => s.kind));
		Assert.Equal(Host, slots[0].player);
		Assert.Equal(Kind.Human, NewSlots(civCount: 5, humans: 2, online: false)[1].kind);
	}

	// Civ3's messages box: "has joined the game", and the slots.
	[Fact]
	public async Task PlayersSeeTheSlotsAndWhoJoined() {
		lobby.SetSettings([new LobbySetting { name = "World Size", value = "Standard" }]);
		using OnlineClient sargon = await Join(host, "Sargon");
		Assert.Equal("Sargon has joined the game.", (await Until<ChatLine>(sargon)).text);
		LobbyState state = await Until<LobbyState>(sargon, s => s.players.Count == 2);
		Assert.Equal(["Hammurabi", "Sargon"], state.players.Select(p => p.name));
		Assert.Equal(["human", "open", "open", "computer", "closed", "closed", "closed", "closed"], state.slots.Select(s => s.kind));
		Assert.Equal(Civs, state.civs);
		Assert.Equal("Standard", state.settings.Single().value);
		Assert.Equal(["Sargon has joined the game."], lobby.messages);
	}

	[Fact]
	public async Task APlayerTakesAnOpenSlot() {
		using OnlineClient sargon = await Join(host, "Sargon");
		using OnlineClient naram = await Join(host, "Naram-Sin");
		sargon.Send(new TakeSeat { seat = 1 });
		LobbyState state = await Until<LobbyState>(naram, s => SlotOf(s, 1).kind == "human");
		Assert.Equal(sargon.playerId, SlotOf(state, 1).player);

		naram.Send(new TakeSeat { seat = 1 });
		Assert.Equal("That slot isn't open.", (await Until<LobbyNotice>(naram)).text);
		naram.Send(new TakeSeat { seat = 3 });
		Assert.Equal("That slot isn't open.", (await Until<LobbyNotice>(naram)).text);
		naram.Send(new TakeSeat { seat = 0 });
		Assert.Equal("That slot isn't open.", (await Until<LobbyNotice>(naram)).text);
		naram.Send(new TakeSeat { seat = 7 });
		Assert.Equal("That slot isn't open.", (await Until<LobbyNotice>(naram)).text);

		// One slot at a time.
		sargon.Send(new TakeSeat { seat = 2 });
		LobbyState moved = await Until<LobbyState>(sargon, s => SlotOf(s, 2).player == sargon.playerId);
		Assert.Equal("open", SlotOf(moved, 1).kind);
	}

	// Never two slots with the same civ (Random as often as you like).
	[Fact]
	public async Task EachPicksTheirCivButNotAnothersOne() {
		Assert.Null(lobby.SetCiv(0, "Babylon"));
		using OnlineClient sargon = await Join(host, "Sargon");
		sargon.Send(new ChooseCiv { civ = "Rome" });
		Assert.Equal("Take a slot first.", (await Until<LobbyNotice>(sargon)).text);
		sargon.Send(new TakeSeat { seat = 1 });
		sargon.Send(new ChooseCiv { civ = "Babylon" });
		Assert.Equal("You may not have multiple players of the same race in one game.", (await Until<LobbyNotice>(sargon)).text);
		sargon.Send(new ChooseCiv { civ = "Atlantis" });
		Assert.Equal("There's no such civilization.", (await Until<LobbyNotice>(sargon)).text);
		sargon.Send(new ChooseCiv { civ = "Rome" });
		await Until<LobbyState>(sargon, s => SlotOf(s, 1).civ == "Rome");
		Assert.Equal("You may not have multiple players of the same race in one game.", lobby.SetCiv(3, "Rome"));
		Assert.Null(lobby.SetCiv(3, OnlineLobby.Random));
		Assert.Null(lobby.SetCiv(2, OnlineLobby.Random));
	}

	[Fact]
	public async Task TheGameBeginsOnceEveryoneIsReady() {
		using OnlineClient sargon = await Join(host, "Sargon");
		Assert.Equal("Some slots are still open: wait for players, or make them computers or close them.", lobby.LaunchProblem());
		lobby.SetKind(2, Kind.Closed);
		sargon.Send(new TakeSeat { seat = 1 });
		await Until<LobbyState>(sargon, s => SlotOf(s, 1).player == sargon.playerId);
		Assert.Equal("Not all players are ready for the game to begin.", lobby.LaunchProblem());
		sargon.Send(new SetReady { ready = true });
		await Until<LobbyState>(sargon, s => SlotOf(s, 1).ready);
		Assert.Null(lobby.LaunchProblem());

		// Something changed: they say again.
		lobby.SetSettings([new LobbySetting { name = "Difficulty", value = "Deity" }]);
		await Until<LobbyState>(sargon, s => !SlotOf(s, 1).ready);
		Assert.Equal("Not all players are ready for the game to begin.", lobby.LaunchProblem());
	}

	[Fact]
	public void AGameNeedsTwoCivilizations() {
		OnlineLobby alone = new(null, NewSlots(civCount: 1, humans: 1, online: false), Civs);
		Assert.Equal("A game needs at least two civilizations.", alone.LaunchProblem());
		alone.SetKind(1, Kind.Computer);
		Assert.Null(alone.LaunchProblem());
	}

	// At this computer (hotseat, by email): the same slots, no network.
	[Fact]
	public void AHotseatGameIsSetUpTheSameWay() {
		OnlineLobby local = new(null, NewSlots(civCount: 3, humans: 2, online: false), Civs);
		Assert.Null(local.SetCiv(1, "Maya"));
		Assert.Equal("You may not have multiple players of the same race in one game.", local.SetCiv(2, "Maya"));
		local.SetKind(2, Kind.Human);
		Assert.Null(local.LaunchProblem());
		local.SetKind(0, Kind.Computer);
		Assert.Equal(Kind.Human, local.slots[0].kind);
	}

	// Leaving, or being put out by the host, frees the slot.
	[Fact]
	public async Task SlotsAreFreedAgain() {
		using OnlineClient sargon = await Join(host, "Sargon");
		using OnlineClient naram = await Join(host, "Naram-Sin");
		sargon.Send(new TakeSeat { seat = 1 });
		naram.Send(new TakeSeat { seat = 2 });
		naram.Send(new ChooseCiv { civ = "Maya" });
		await Until<LobbyState>(sargon, s => SlotOf(s, 2).civ == "Maya");
		naram.Dispose();
		Assert.Equal("Naram-Sin has left the game.", (await Until<ChatLine>(sargon, l => l.text.Contains("left"))).text);
		// (The next player picks their own civ.)
		LobbyState freed = await Until<LobbyState>(sargon, s => SlotOf(s, 2).kind == "open");
		Assert.Equal(OnlineLobby.Random, SlotOf(freed, 2).civ);

		lobby.SetKind(1, Kind.Computer);
		await Until<LobbyState>(sargon, s => SlotOf(s, 1).kind == "computer");
		lobby.Kick(sargon.playerId);
		Assert.Equal("The host has removed you from the game.", (await Next<Goodbye>(sargon)).reason);
	}

	[Fact]
	public async Task EveryoneReadsTheMessagesBox() {
		using OnlineClient sargon = await Join(host, "Sargon");
		using OnlineClient naram = await Join(host, "Naram-Sin");
		await Until<ChatLine>(sargon, l => l.text == "Naram-Sin has joined the game.");
		naram.Send(new Chat { text = "  Shall we begin?  " });
		Assert.Equal("Naram-Sin: Shall we begin?", (await Until<ChatLine>(sargon, l => l.text.StartsWith("Naram-Sin:"))).text);
		lobby.Say("In a moment.");
		Assert.Equal("Hammurabi: In a moment.", (await Until<ChatLine>(naram, l => l.text.StartsWith("Hammurabi:"))).text);
		Assert.Contains("Naram-Sin: Shall we begin?", lobby.messages);
	}

	// A loaded game's civ with a password, which can't be guessed either.
	[Fact]
	public async Task ACivsPasswordIsAsked() {
		ID celts = new("Player", 4);
		OnlineLobby loaded = new(host, NewSlots(civCount: 2, humans: 2, online: true), Civs,
			(player, password) => player == celts && password == "carnyx");
		loaded.slots[1].password = true;
		loaded.slots[1].loadedPlayer = celts;
		using OnlineClient sargon = await Join(host, "Sargon");
		async Task<string> Try(string password) {
			sargon.Send(new TakeSeat { seat = 1, password = password });
			for (int i = 0; i < 300; i++) {
				while (host.TryNextEvent(out HostEvent hostEvent)) {
					loaded.Handle(hostEvent);
				}
				while (sargon.TryNextMessage(out OnlineMessage message)) {
					if (message is LobbyNotice notice) {
						return notice.text;
					}
					if (message is LobbyState state && state.slots[1].player == sargon.playerId) {
						return null;
					}
				}
				await Task.Delay(10);
			}
			throw new TimeoutException();
		}
		Assert.Equal("That isn't the civ's password.", await Try("lyre"));
		Assert.Equal("That isn't the civ's password.", await Try(null));
		Assert.Equal("That isn't the civ's password.", await Try("harp"));
		Assert.Equal("Too many wrong passwords. Try again later.", await Try("carnyx"));
		Assert.Equal(Kind.Open, loaded.slots[1].kind);
	}

	// A player's lobby messages out of bounds drop them, as any other.
	[Theory]
	[InlineData("{\"kind\":\"chat\",\"text\":\"\"}")]
	[InlineData("{\"kind\":\"chooseCiv\",\"civ\":\"\"}")]
	[InlineData("{\"kind\":\"takeSeat\",\"seat\":8}")]
	public void BadLobbyMessagesAreNotRead(string json) {
		Assert.Null(OnlineMessage.Read(System.Text.Encoding.UTF8.GetBytes(json)));
		Assert.Null(OnlineMessage.Read(System.Text.Encoding.UTF8.GetBytes("{\"kind\":\"chat\",\"text\":\"" + new string('x', Chat.MaxLength + 1) + "\"}")));
	}
}

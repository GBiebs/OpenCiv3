using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine.Online {
	// Before a multiplayer game, Civ3's multiplayer setup: up to 8 slots,
	// each open (for a player who joins to take), closed, a computer or a
	// human, with its civ (Random or one civ, never two slots with the same
	// one) and, for a player who joined, whether they're ready. The host
	// has the first slot and sets the others and the game's settings; a
	// player takes an open slot, picks its civ, says they're ready and
	// talks in the messages box. Without an OnlineHost the same slots serve
	// a hotseat or play-by-email game set up at this computer.
	public sealed class OnlineLobby {
		// The host, among the players.
		public const int Host = 0;
		public const string Random = "Random";

		public enum Kind { Open, Closed, Computer, Human }

		public sealed class Slot {
			public int number;
			public Kind kind;
			// The player in a human slot: the host, one who joined, or (a
			// hotseat human at this computer) none.
			public int? player;
			public string civ = Random;
			public bool ready;
			// A loaded game's civ with a password, and its player.
			public bool password;
			public ID loadedPlayer;
		}

		private readonly OnlineHost host;
		private readonly Func<ID, string, bool> checkPassword;
		public readonly List<Slot> slots;
		public readonly List<string> civs;
		public List<LobbySetting> settings = new();
		// The messages box, for the host's own screen (players get each line).
		public readonly List<string> messages = new();

		public OnlineLobby(OnlineHost host, List<Slot> slots, List<string> civs, Func<ID, string, bool> checkPassword = null) {
			this.host = host;
			this.slots = slots;
			this.civs = civs;
			this.checkPassword = checkPassword ?? ((_, _) => true);
		}

		// The usual start: the host's slot, then human slots (open online),
		// then computers, the rest closed.
		public static List<Slot> NewSlots(int civCount, int humans, bool online) {
			List<Slot> made = new();
			for (int i = 0; i < LobbyState.MaxSlots; i++) {
				Slot slot = new() { number = i };
				if (i == 0) {
					slot.kind = Kind.Human;
					slot.player = Host;
				} else if (i < humans) {
					slot.kind = online ? Kind.Open : Kind.Human;
				} else {
					slot.kind = i < civCount ? Kind.Computer : Kind.Closed;
				}
				made.Add(slot);
			}
			return made;
		}

		public Slot SeatOf(int player) => slots.FirstOrDefault(s => s.kind == Kind.Human && s.player == player);

		private string NameOf(int player) => player == Host ? host?.hostName ?? "" : host?.Players.FirstOrDefault(p => p.id == player)?.name ?? "?";

		// The host sets a slot (not its own): one with a player in it frees
		// them (they stay, without a slot).
		public void SetKind(int number, Kind kind) {
			Slot slot = slots.First(s => s.number == number);
			if (slot.player == Host || slot.kind == kind) {
				return;
			}
			slot.kind = kind;
			slot.player = null;
			slot.ready = false;
			if (kind == Kind.Closed) {
				slot.civ = Random;
			}
			Changed();
		}

		// The host's civ for its own slot, a computer's or (hotseat) a human's
		// at this computer; null, or why not.
		public string SetCiv(int number, string civ) {
			Slot slot = slots.First(s => s.number == number);
			string problem = CivProblem(slot, civ);
			if (problem == null && slot.civ != civ) {
				slot.civ = civ;
				Changed();
			}
			return problem;
		}

		public void SetSettings(List<LobbySetting> changed) {
			settings = changed;
			Changed();
		}

		// The host's own line in the messages box.
		public void Say(string text) {
			if (!string.IsNullOrWhiteSpace(text)) {
				Message($"{NameOf(Host)}: {text.Trim()}");
			}
		}

		public void Kick(int player) {
			host?.Drop(player, "The host has removed you from the game.");
		}

		// Every change makes the players say again that they're ready (for
		// the game they'll get).
		private void Changed() {
			foreach (Slot slot in slots) {
				slot.ready = false;
			}
			SendToAll();
		}

		private void Message(string line) {
			messages.Add(line);
			host?.SendToAll(new ChatLine { text = line });
		}

		// Handles one of the host's events; false if it's not the lobby's.
		public bool Handle(HostEvent hostEvent) {
			switch (hostEvent) {
				case PlayerJoined joined:
					Message($"{joined.player.name} has joined the game.");
					SendToAll();
					return true;
				case PlayerLeft left:
					Free(left.player.id);
					Message($"{left.player.name} has left the game.");
					SendToAll();
					return true;
				case MessageFrom { message: TakeSeat take } from:
					Answer(from.player, Take(from.player, take));
					return true;
				case MessageFrom { message: LeaveSeat } from:
					Free(from.player.id);
					SendToAll();
					return true;
				case MessageFrom { message: ChooseCiv choose } from: {
						Slot slot = SeatOf(from.player.id);
						string problem = slot == null ? "Take a slot first." : CivProblem(slot, choose.civ);
						if (problem == null) {
							slot.civ = choose.civ;
							slot.ready = false;
						}
						Answer(from.player, problem);
						return true;
					}
				case MessageFrom { message: SetReady ready } from: {
						Slot slot = SeatOf(from.player.id);
						if (slot != null) {
							slot.ready = ready.ready;
							SendToAll();
						}
						return true;
					}
				case MessageFrom { message: Chat chat } from:
					Message($"{from.player.name}: {chat.text.Trim()}");
					return true;
				default:
					return false;
			}
		}

		private void Answer(RemotePlayer player, string problem) {
			if (problem != null) {
				host?.Send(player.id, new LobbyNotice { text = problem });
			} else {
				SendToAll();
			}
		}

		private string CivProblem(Slot slot, string civ) {
			if (civ != Random && !civs.Contains(civ)) {
				return "There's no such civilization.";
			}
			if (civ != Random && slots.Any(s => s != slot && s.kind != Kind.Closed && s.civ == civ)) {
				return "You may not have multiple players of the same race in one game.";
			}
			return null;
		}

		// Why `player` can't have the slot, or null once they have it.
		private string Take(RemotePlayer player, TakeSeat take) {
			Slot slot = slots.FirstOrDefault(s => s.number == take.seat);
			if (slot == null) {
				return "There's no such slot.";
			}
			if (slot.kind == Kind.Human && slot.player == player.id) {
				return null;
			}
			if (slot.kind != Kind.Open) {
				return "That slot isn't open.";
			}
			if (slot.password) {
				if (!host.MayTryPassword(player)) {
					return "Too many wrong passwords. Try again later.";
				}
				if (!checkPassword(slot.loadedPlayer, take.password ?? "")) {
					host.WrongPassword(player);
					return "That isn't the civ's password.";
				}
			}
			Free(player.id);
			slot.kind = Kind.Human;
			slot.player = player.id;
			slot.ready = false;
			return null;
		}

		// (The next player picks their own civ.)
		private void Free(int player) {
			foreach (Slot slot in slots.Where(s => s.kind == Kind.Human && s.player == player)) {
				slot.kind = Kind.Open;
				slot.player = null;
				slot.ready = false;
				slot.civ = Random;
			}
		}

		// Why the game can't begin yet, or null.
		public string LaunchProblem() {
			List<Slot> playing = slots.Where(s => s.kind != Kind.Closed).ToList();
			if (playing.Any(s => s.kind == Kind.Open)) {
				return "Some slots are still open: wait for players, or make them computers or close them.";
			}
			if (playing.Count < 2) {
				return "A game needs at least two civilizations.";
			}
			if (playing.Any(s => s.kind == Kind.Human && s.player != null && s.player != Host && !s.ready)) {
				return "Not all players are ready for the game to begin.";
			}
			List<string> chosen = playing.Select(s => s.civ).Where(c => c != Random).ToList();
			if (chosen.Count != chosen.Distinct().Count()) {
				return "You may not have multiple players of the same race in one game.";
			}
			return null;
		}

		public LobbyState State() {
			return new LobbyState {
				settings = settings,
				slots = slots.Select(s => new LobbySlot {
					slot = s.number,
					kind = LobbySlot.Kinds[(int)s.kind],
					player = s.player,
					civ = s.civ,
					ready = s.ready,
					password = s.password,
				}).ToList(),
				players = new[] { new LobbyPlayer { player = Host, name = NameOf(Host) } }
					.Concat((host?.Players ?? []).Select(p => new LobbyPlayer { player = p.id, name = p.name })).ToList(),
				civs = civs,
			};
		}

		public void SendToAll() => host?.SendToAll(State());
	}
}

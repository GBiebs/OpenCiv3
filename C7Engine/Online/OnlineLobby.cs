using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine.Online {
	// Online play, before the game starts: the host's seats (the civs humans
	// play) and who has taken which. The host keeps its own seat; a player
	// takes a free one, giving the civ's password in a loaded game that has
	// one (guarded like the game's password). A player who leaves frees
	// their seat. Every change goes to everyone.
	public sealed class OnlineLobby {
		// The host, among the players.
		public const int Host = 0;

		public sealed class Seat {
			public int number;
			public ID player;
			public string civ;
			public string leader;
			// A loaded game's civ with a password.
			public bool password;
			// Who has it: the host, a player's number, or no one.
			public int? takenBy;
		}

		private readonly OnlineHost host;
		private readonly Func<ID, string, bool> checkPassword;
		public readonly List<Seat> seats;

		public OnlineLobby(OnlineHost host, List<Seat> seats, Func<ID, string, bool> checkPassword = null) {
			this.host = host;
			this.seats = seats;
			this.checkPassword = checkPassword ?? ((_, _) => true);
		}

		public bool Full => seats.All(s => s.takenBy != null);

		public Seat SeatOf(int player) => seats.FirstOrDefault(s => s.takenBy == player);

		public void TakeForHost(int number) {
			foreach (Seat seat in seats.Where(s => s.takenBy == Host)) {
				seat.takenBy = null;
			}
			seats.First(s => s.number == number).takenBy = Host;
			SendToAll();
		}

		// Handles one of the host's events; false if it's not the lobby's.
		public bool Handle(HostEvent hostEvent) {
			switch (hostEvent) {
				case PlayerJoined:
					SendToAll();
					return true;
				case PlayerLeft left:
					Free(left.player.id);
					SendToAll();
					return true;
				case MessageFrom { message: TakeSeat take } from:
					string problem = Take(from.player, take);
					if (problem != null) {
						host.Send(from.player.id, new LobbyNotice { text = problem });
					} else {
						SendToAll();
					}
					return true;
				case MessageFrom { message: LeaveSeat } from:
					Free(from.player.id);
					SendToAll();
					return true;
				default:
					return false;
			}
		}

		// Why `player` can't have the seat, or null once they have it.
		private string Take(RemotePlayer player, TakeSeat take) {
			Seat seat = seats.FirstOrDefault(s => s.number == take.seat);
			if (seat == null) {
				return "There's no such seat.";
			}
			if (seat.takenBy == player.id) {
				return null;
			}
			if (seat.takenBy != null) {
				return $"Someone has already taken the {seat.civ}.";
			}
			if (seat.password) {
				if (!host.MayTryPassword(player)) {
					return "Too many wrong passwords. Try again later.";
				}
				if (!checkPassword(seat.player, take.password ?? "")) {
					host.WrongPassword(player);
					return $"That isn't the password for the {seat.civ}.";
				}
			}
			Free(player.id);
			seat.takenBy = player.id;
			return null;
		}

		private void Free(int player) {
			foreach (Seat seat in seats.Where(s => s.takenBy == player)) {
				seat.takenBy = null;
			}
		}

		public LobbyState State() {
			return new LobbyState {
				seats = seats.Select(s => new LobbySeat { seat = s.number, civ = s.civ, leader = s.leader, password = s.password, player = s.takenBy }).ToList(),
				players = new[] { new LobbyPlayer { player = Host, name = host.hostName } }
					.Concat(host.Players.Select(p => new LobbyPlayer { player = p.id, name = p.name })).ToList(),
			};
		}

		public void SendToAll() => host.SendToAll(State());
	}
}

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace C7Engine.Online {
	// What the host and the players' games say to each other, as JSON. Only
	// the kinds listed here are read (by their "kind"), with plain data in
	// them; anything else drops the connection. Each checks its own bounds
	// (IsValid), since the other side may not be our game.
	[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
	[JsonDerivedType(typeof(Hello), "hello")]
	[JsonDerivedType(typeof(Welcome), "welcome")]
	[JsonDerivedType(typeof(Refused), "refused")]
	[JsonDerivedType(typeof(Ping), "ping")]
	[JsonDerivedType(typeof(Goodbye), "goodbye")]
	[JsonDerivedType(typeof(LobbyState), "lobby")]
	[JsonDerivedType(typeof(TakeSeat), "takeSeat")]
	[JsonDerivedType(typeof(LeaveSeat), "leaveSeat")]
	[JsonDerivedType(typeof(LobbyNotice), "notice")]
	public abstract class OnlineMessage {
		public const int MaxNameLength = 32;
		public const int MaxTextLength = 500;

		private static readonly JsonSerializerOptions options = new() {
			MaxDepth = 16,
		};

		public virtual bool IsValid() => true;

		public static byte[] Write(OnlineMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, options);

		// The message in `json`, or null if it isn't one we read.
		public static OnlineMessage Read(byte[] json) {
			try {
				OnlineMessage message = JsonSerializer.Deserialize<OnlineMessage>(json, options);
				return message != null && message.IsValid() ? message : null;
			} catch (System.Exception) {
				// (Bad JSON, an unknown kind, a field of the wrong type.)
				return null;
			}
		}

		protected static bool IsName(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxNameLength && !text.Any(char.IsControl);
		protected static bool IsText(string text, int max = MaxTextLength) => text == null || (text.Length <= max && !text.Any(c => char.IsControl(c) && c != '\n'));
	}

	// A player's game, once the connection is encrypted: who they are, and
	// the game's password.
	public sealed class Hello : OnlineMessage {
		public int protocol { get; set; }
		public string game { get; set; }
		public string name { get; set; }
		public string password { get; set; }

		public override bool IsValid() => IsText(game, 100) && IsName(name) && IsText(password, 100);
	}

	// The host lets them in: `you` is their number in this game.
	public sealed class Welcome : OnlineMessage {
		public int you { get; set; }
		public string hostName { get; set; }

		public override bool IsValid() => you > 0 && IsName(hostName);
	}

	// The host doesn't let them in, and why.
	public sealed class Refused : OnlineMessage {
		public string reason { get; set; }

		public override bool IsValid() => IsText(reason);
	}

	// Still here (sent when there's nothing else to say).
	public sealed class Ping : OnlineMessage { }

	// Either side, leaving.
	public sealed class Goodbye : OnlineMessage {
		public string reason { get; set; }

		public override bool IsValid() => IsText(reason);
	}

	// Before the game: the civs humans play, who has taken which, and who's
	// connected (the host is player 0).
	public sealed class LobbyState : OnlineMessage {
		public const int MaxSeats = 32;

		public List<LobbySeat> seats { get; set; } = new();
		public List<LobbyPlayer> players { get; set; } = new();

		public override bool IsValid() => seats != null && players != null && seats.Count <= MaxSeats && players.Count <= 64
			&& seats.All(s => s != null && s.IsValid()) && players.All(p => p != null && p.IsValid());
	}

	public sealed class LobbySeat {
		public int seat { get; set; }
		public string civ { get; set; }
		public string leader { get; set; }
		// A loaded game's civ with a password: whoever takes it gives it.
		public bool password { get; set; }
		// The player in it, if any.
		public int? player { get; set; }

		public bool IsValid() => seat >= 0 && seat < LobbyState.MaxSeats && !string.IsNullOrEmpty(civ) && civ.Length <= 100
			&& (leader == null || leader.Length <= 100) && (player == null || player >= 0);
	}

	public sealed class LobbyPlayer {
		public int player { get; set; }
		public string name { get; set; }

		public bool IsValid() => player >= 0 && !string.IsNullOrWhiteSpace(name) && name.Length <= OnlineMessage.MaxNameLength && !name.Any(char.IsControl);
	}

	// A player asks for a seat (with the civ's password, if it has one).
	public sealed class TakeSeat : OnlineMessage {
		public int seat { get; set; }
		public string password { get; set; }

		public override bool IsValid() => seat >= 0 && seat < LobbyState.MaxSeats && IsText(password, 100);
	}

	// A player gives their seat up.
	public sealed class LeaveSeat : OnlineMessage { }

	// The host tells one player something (a seat they couldn't have).
	public sealed class LobbyNotice : OnlineMessage {
		public string text { get; set; }

		public override bool IsValid() => IsText(text);
	}
}

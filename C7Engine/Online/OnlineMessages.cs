using System;
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
	[JsonDerivedType(typeof(ChooseCiv), "chooseCiv")]
	[JsonDerivedType(typeof(SetReady), "setReady")]
	[JsonDerivedType(typeof(Chat), "chat")]
	[JsonDerivedType(typeof(ChatLine), "chatLine")]
	[JsonDerivedType(typeof(GameStart), "gameStart")]
	[JsonDerivedType(typeof(OrderRequest), "order")]
	[JsonDerivedType(typeof(SequencedOrder), "sequenced")]
	[JsonDerivedType(typeof(Checksum), "checksum")]
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

		public static bool IsCivName(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 60 && !text.Any(char.IsControl);
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

	// Before the game, Civ3's multiplayer setup: the game's settings (to
	// show), its slots and who's connected (the host is player 0). A slot is
	// "open" (for a player to take), "closed", "computer" or "human" (the
	// player in it, if they joined; a hotseat human has none), with its civ
	// ("Random" or one of `civs`) and whether its player is ready.
	public sealed class LobbyState : OnlineMessage {
		public const int MaxSlots = 8;

		public List<LobbySetting> settings { get; set; } = new();
		public List<LobbySlot> slots { get; set; } = new();
		public List<LobbyPlayer> players { get; set; } = new();
		public List<string> civs { get; set; } = new();
		// A saved game resumed: the civs stay as they are.
		public bool loaded { get; set; }

		public override bool IsValid() => settings != null && slots != null && players != null && civs != null
			&& settings.Count <= 32 && slots.Count <= MaxSlots && players.Count <= 64 && civs.Count <= 200
			&& settings.All(s => s != null && s.IsValid()) && slots.All(s => s != null && s.IsValid())
			&& players.All(p => p != null && p.IsValid()) && civs.All(IsCivName);
	}

	public sealed class LobbySetting {
		public string name { get; set; }
		public string value { get; set; }

		public bool IsValid() => !string.IsNullOrEmpty(name) && name.Length <= 60 && value != null && value.Length <= 100;
	}

	public sealed class LobbySlot {
		public static readonly string[] Kinds = { "open", "closed", "computer", "human" };

		public int slot { get; set; }
		public string kind { get; set; }
		public int? player { get; set; }
		public string civ { get; set; }
		public bool ready { get; set; }
		// A loaded game's civ with a password: whoever takes it gives it.
		public bool password { get; set; }

		public bool IsValid() => slot >= 0 && slot < LobbyState.MaxSlots && Kinds.Contains(kind) && (player == null || player >= 0)
			&& OnlineMessage.IsCivName(civ);
	}

	public sealed class LobbyPlayer {
		public int player { get; set; }
		public string name { get; set; }

		public bool IsValid() => player >= 0 && !string.IsNullOrWhiteSpace(name) && name.Length <= OnlineMessage.MaxNameLength && !name.Any(char.IsControl);
	}

	// A player takes an open slot (with the civ's password, if it has one).
	public sealed class TakeSeat : OnlineMessage {
		public int seat { get; set; }
		public string password { get; set; }

		public override bool IsValid() => seat >= 0 && seat < LobbyState.MaxSlots && IsText(password, 100);
	}

	// A player gives their slot up (it's open again).
	public sealed class LeaveSeat : OnlineMessage { }

	// A player picks the civ for their slot.
	public sealed class ChooseCiv : OnlineMessage {
		public string civ { get; set; }

		public override bool IsValid() => IsCivName(civ);
	}

	// A player is ready for the game to begin (Civ3's "Make Ready"), or not.
	public sealed class SetReady : OnlineMessage {
		public bool ready { get; set; }
	}

	// A player says something to everyone (Civ3's messages box).
	public sealed class Chat : OnlineMessage {
		public const int MaxLength = 200;

		public string text { get; set; }

		public override bool IsValid() => !string.IsNullOrWhiteSpace(text) && IsText(text, MaxLength);
	}

	// A line for everyone's messages box: who said what, who joined or left.
	public sealed class ChatLine : OnlineMessage {
		public string text { get; set; }

		public override bool IsValid() => IsText(text);
	}

	// The host tells one player something (a slot they couldn't have).
	public sealed class LobbyNotice : OnlineMessage {
		public string text { get; set; }

		public override bool IsValid() => IsText(text);
	}
	// The game begins, or begins again from the host's copy (it went its
	// own way somewhere): the save (gzip, base64), the player this game
	// plays, and the last order in sequence the save already has.
	public sealed class GameStart : OnlineMessage {
		public string save { get; set; }
		public string you { get; set; }
		public long seq { get; set; }
		// A game going on (loaded, or begun again): no new-game welcome.
		public bool resumed { get; set; }

		public override bool IsValid() => !string.IsNullOrEmpty(save) && save.Length <= 64 * 1024 * 1024 && IsText(you, 60) && seq >= 0;
	}

	// A player's order, for the host to put in sequence.
	public sealed class OrderRequest : OnlineMessage {
		// (Not "kind": that names the message.)
		public string orderType { get; set; }
		public string order { get; set; }

		public override bool IsValid() => IsText(orderType, 60) && !string.IsNullOrEmpty(orderType) && order != null && order.Length <= 60 * 1024;
	}

	// An order in sequence, with its dice: every game plays it.
	public sealed class SequencedOrder : OnlineMessage {
		public long seq { get; set; }
		public int seed { get; set; }
		// Whose order it is.
		public string from { get; set; }
		// (Not "kind": that names the message.)
		public string orderType { get; set; }
		public string order { get; set; }

		public override bool IsValid() => seq > 0 && !string.IsNullOrEmpty(from) && IsText(from, 60) && IsText(orderType, 60) && !string.IsNullOrEmpty(orderType) && order != null && order.Length <= 1024 * 1024;
	}

	// A player's fingerprint of the game once the work of order `seq` (an
	// End Turn) was done.
	public sealed class Checksum : OnlineMessage {
		public long seq { get; set; }
		public string hash { get; set; }

		public override bool IsValid() => seq > 0 && hash != null && hash.Length == 64 && hash.All(Uri.IsHexDigit);
	}
}

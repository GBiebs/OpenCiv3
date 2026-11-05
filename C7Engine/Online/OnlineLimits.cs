using System;

namespace C7Engine.Online {
	public static class OnlineProtocol {
		// Both sides must speak the same version of these messages.
		public const int Version = 5;
		public const int DefaultPort = 47300;
	}

	// Online play: what each side lets the other do. A host's port can be
	// reached by anyone who has its address (by everyone, once it's
	// forwarded on the router), and a joining player's game reads what the
	// host sends, so every message has a size limit and every step a time
	// limit. Not game rules (a mod has no say in them); tests shorten them.
	public class OnlineLimits {
		// Players send orders and answers: small.
		public int maxMessageFromPlayer = 64 * 1024;
		// The host sends the game itself.
		public int maxMessageFromHost = 64 * 1024 * 1024;
		// Connecting to each of an invite's addresses.
		public TimeSpan connectTime = TimeSpan.FromSeconds(5);
		// From connecting to being let in: the encrypted handshake and the
		// password.
		public TimeSpan handshakeTime = TimeSpan.FromSeconds(10);
		// Each side pings while it has nothing to say, and lets go of the
		// other after this much silence.
		public TimeSpan pingInterval = TimeSpan.FromSeconds(15);
		public TimeSpan silenceTime = TimeSpan.FromSeconds(60);
		// Connections at once (players, and anyone still knocking), and from
		// one address.
		public int maxConnections = 16;
		public int maxConnectionsPerAddress = 3;
		// Wrong passwords from one address before it waits; each lockout is
		// twice as long as the last.
		public int wrongPasswordsBeforeLockout = 5;
		public TimeSpan lockout = TimeSpan.FromMinutes(1);
		// Password checks a minute from all addresses together (many
		// addresses guessing at once).
		public int passwordChecksPerMinute = 30;
		// Messages a second a player may send, and in a burst.
		public int messagesPerSecond = 50;
		public int messageBurst = 200;
	}
}

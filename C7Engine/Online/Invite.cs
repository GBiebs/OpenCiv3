using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace C7Engine.Online {
	// What a joining player needs to reach a host: its address and port, and
	// the fingerprint of the host's certificate, so the game knows it has
	// reached that host (and no one in between) before it sends the game's
	// password. Written "address:port/FINGERPRINT", an IPv6 address in
	// brackets; the fingerprint may be in any case, with dashes or spaces.
	public sealed class Invite {
		public readonly string host;
		public readonly int port;
		public readonly string fingerprint;

		public Invite(string host, int port, string fingerprint) {
			this.host = host;
			this.port = port;
			this.fingerprint = fingerprint;
		}

		public override string ToString() {
			string address = IPAddress.TryParse(host, out IPAddress ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{host}]" : host;
			return $"{address}:{port}/{fingerprint}";
		}

		// The invite written in `text`, or null if it isn't one.
		public static Invite Parse(string text) {
			if (string.IsNullOrWhiteSpace(text) || text.Length > 400) {
				return null;
			}
			text = text.Trim();
			int slash = text.LastIndexOf('/');
			if (slash < 0) {
				return null;
			}
			string fingerprint = new string(text[(slash + 1)..].Where(c => c != '-' && c != ' ').ToArray()).ToUpperInvariant();
			if (fingerprint.Length != HostCertificate.FingerprintLength || !fingerprint.All(HostCertificate.IsFingerprintCharacter)) {
				return null;
			}
			string address = text[..slash];
			string host;
			string portText;
			if (address.StartsWith('[')) {
				int close = address.IndexOf(']');
				if (close < 0 || close + 1 >= address.Length || address[close + 1] != ':') {
					return null;
				}
				host = address[1..close];
				portText = address[(close + 2)..];
				if (!IPAddress.TryParse(host, out IPAddress ip) || ip.AddressFamily != AddressFamily.InterNetworkV6) {
					return null;
				}
			} else {
				int colon = address.LastIndexOf(':');
				if (colon < 0) {
					return null;
				}
				host = address[..colon];
				portText = address[(colon + 1)..];
				if (!IsHostName(host)) {
					return null;
				}
			}
			if (!int.TryParse(portText, out int port) || port < 1 || port > 65535) {
				return null;
			}
			return new Invite(host, port, fingerprint);
		}

		// An IPv4 address or a name (a private network tool gives computers
		// names too).
		private static bool IsHostName(string host) {
			if (host.Length == 0 || host.Length > 253) {
				return false;
			}
			foreach (string label in host.Split('.')) {
				if (label.Length == 0 || label.Length > 63 || label.StartsWith('-') || label.EndsWith('-')
					|| !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) {
					return false;
				}
			}
			return true;
		}
	}
}

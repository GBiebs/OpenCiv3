using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace C7Engine.Online {
	// What a joining player needs to reach a host: the addresses its game
	// may find the host at (a private network tool's, the home network's;
	// never its internet address unless the host gives one), the port, and
	// the fingerprint of the host's certificate, so the game knows it has
	// reached that host (and no one in between) before it sends the game's
	// password. Written as one code to copy ("OC3-" and letters and digits
	// in groups, with a checksum against typos), so no one has to read an
	// address. "address:port/FINGERPRINT" is read too (one address).
	public sealed class Invite {
		public const int MaxHosts = 4;
		private const string Prefix = "OC3";
		private const byte Format = 1;

		public readonly IReadOnlyList<string> hosts;
		public readonly int port;
		public readonly string fingerprint;

		public Invite(IEnumerable<string> hosts, int port, string fingerprint) {
			this.hosts = hosts.Distinct().Take(MaxHosts).ToList();
			this.port = port;
			this.fingerprint = fingerprint;
		}

		public Invite(string host, int port, string fingerprint) : this([host], port, fingerprint) { }

		public override string ToString() => Code;

		// Format, port, fingerprint, the addresses, a checksum; in base32.
		public string Code {
			get {
				List<byte> payload = [Format, (byte)(port >> 8), (byte)port, .. Base32.Decode(fingerprint), (byte)hosts.Count];
				foreach (string host in hosts) {
					if (IPAddress.TryParse(host, out IPAddress ip)) {
						payload.Add(ip.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)6 : (byte)4);
						payload.AddRange(ip.GetAddressBytes());
					} else {
						payload.Add(0);
						payload.Add((byte)host.Length);
						payload.AddRange(Encoding.ASCII.GetBytes(host));
					}
				}
				payload.AddRange(SHA256.HashData(payload.ToArray()).Take(2));
				string letters = Base32.Encode(payload.ToArray());
				return Prefix + "-" + string.Join("-", Enumerable.Range(0, (letters.Length + 3) / 4).Select(i => letters.Substring(i * 4, Math.Min(4, letters.Length - i * 4))));
			}
		}

		// The invite written in `text`, or null if it isn't one.
		public static Invite Parse(string text) {
			if (string.IsNullOrWhiteSpace(text) || text.Length > 600) {
				return null;
			}
			text = text.Trim();
			return text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? FromCode(text) : FromAddress(text);
		}

		private static Invite FromCode(string text) {
			string letters = new string(text[Prefix.Length..].Where(c => c != '-' && c != ' ').ToArray()).ToUpperInvariant();
			byte[] payload = Base32.TryDecode(letters);
			return payload == null || payload.Length < 27 ? null : Read(payload);
		}

		private static Invite Read(byte[] payload) {
			byte[] body = payload[..^2];
			if (!SHA256.HashData(body).Take(2).SequenceEqual(payload[^2..]) || body[0] != Format) {
				return null;
			}
			int port = body[1] << 8 | body[2];
			string fingerprint = Base32.Encode(body[3..23]);
			int count = body[23];
			if (port < 1 || count < 1 || count > MaxHosts) {
				return null;
			}
			List<string> hosts = new();
			int at = 24;
			for (int i = 0; i < count; i++) {
				if (at >= body.Length) {
					return null;
				}
				byte kind = body[at++];
				int size = kind switch { 4 => 4, 6 => 16, 0 when at < body.Length => body[at++], _ => -1 };
				if (size <= 0 || at + size > body.Length) {
					return null;
				}
				byte[] bytes = body[at..(at + size)];
				at += size;
				string host = kind == 0 ? Encoding.ASCII.GetString(bytes) : new IPAddress(bytes).ToString();
				if (kind == 0 && !IsHostName(host)) {
					return null;
				}
				hosts.Add(host);
			}
			return at == body.Length ? new Invite(hosts, port, fingerprint) : null;
		}

		// "address:port/FINGERPRINT", the fingerprint in any case, with
		// dashes or spaces; an IPv6 address in brackets.
		private static Invite FromAddress(string text) {
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

	// RFC 4648's base32 (letters and the digits 2 to 7), without padding.
	internal static class Base32 {
		public const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

		public static string Encode(byte[] bytes) {
			StringBuilder text = new();
			int bits = bytes.Length * 8;
			for (int bit = 0; bit < bits; bit += 5) {
				int value = 0;
				for (int i = 0; i < 5; i++) {
					int b = bit + i;
					value = (value << 1) | (b < bits ? (bytes[b / 8] >> (7 - b % 8)) & 1 : 0);
				}
				text.Append(Letters[value]);
			}
			return text.ToString();
		}

		public static byte[] Decode(string text) => TryDecode(text) ?? throw new FormatException("Not base32");

		// The bytes in `text` (whole bytes only), or null if it isn't base32.
		public static byte[] TryDecode(string text) {
			byte[] bytes = new byte[text.Length * 5 / 8];
			for (int i = 0; i < text.Length; i++) {
				int value = Letters.IndexOf(text[i]);
				if (value < 0) {
					return null;
				}
				for (int j = 0; j < 5; j++) {
					int b = i * 5 + j;
					if (b / 8 < bytes.Length && ((value >> (4 - j)) & 1) == 1) {
						bytes[b / 8] |= (byte)(1 << (7 - b % 8));
					}
				}
			}
			return bytes;
		}
	}
}

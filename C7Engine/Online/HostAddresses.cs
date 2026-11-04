using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace C7Engine.Online {
	// The addresses a host's players could reach it at, for its invite: a
	// private network tool's first (what we recommend), then the home
	// network's. The internet address isn't on this computer when a router
	// is in between, so the host types that one in (we don't ask an outside
	// service).
	public static class HostAddresses {
		public enum Kind { Tailscale, ZeroTier, HomeNetwork, Internet }

		public sealed record Address(string address, Kind kind) {
			public string Label => kind switch {
				Kind.Tailscale => $"Tailscale: {address}",
				Kind.ZeroTier => $"ZeroTier: {address}",
				Kind.HomeNetwork => $"Home network: {address}",
				_ => $"Internet: {address}",
			};
		}

		public static List<Address> OfThisComputer() {
			List<Address> found = new();
			try {
				foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces()) {
					if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) {
						continue;
					}
					foreach (UnicastIPAddressInformation unicast in network.GetIPProperties().UnicastAddresses) {
						Address address = Classify(unicast.Address, network.Name + " " + network.Description);
						if (address != null) {
							found.Add(address);
						}
					}
				}
			} catch (NetworkInformationException) {
				// (None to offer: the host types one.)
			}
			return Ordered(found);
		}

		// What goes in the invite: a private network tool's addresses and the
		// home network's, never this computer's internet ones (they'd tell
		// the players where it is); the internet address the host gave, for
		// port forwarding, last.
		public static List<string> ForInvite(IEnumerable<Address> found, string internetAddress = null) {
			// (IPv4 first: Tailscale's IPv6 address only repeats its IPv4 one.)
			List<string> chosen = Ordered(found).Where(a => a.kind != Kind.Internet).OrderBy(a => a.address.Contains(':')).Select(a => a.address).ToList();
			if (!string.IsNullOrWhiteSpace(internetAddress)) {
				chosen = chosen.Take(Invite.MaxHosts - 1).Append(internetAddress.Trim()).ToList();
			}
			return chosen.Take(Invite.MaxHosts).ToList();
		}

		public static List<Address> Ordered(IEnumerable<Address> addresses) {
			// IPv4 first: shorter to send.
			return addresses.DistinctBy(a => a.address).OrderBy(a => a.kind).ThenBy(a => a.address.Contains(':')).ToList();
		}

		// What kind of address `ip` is, on the network named `network`; null
		// if no other computer could reach us there.
		public static Address Classify(IPAddress ip, string network) {
			if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast) {
				return null;
			}
			byte[] bytes = ip.GetAddressBytes();
			string text = ip.ToString();
			if (ip.AddressFamily == AddressFamily.InterNetwork) {
				// Self-assigned (no network answered).
				if (bytes[0] == 169 && bytes[1] == 254) {
					return null;
				}
			} else if (ip.AddressFamily == AddressFamily.InterNetworkV6) {
				// (Without the interface's "%number".)
				text = new IPAddress(bytes).ToString();
			} else {
				return null;
			}
			network ??= "";
			if (network.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) || IsTailscale(bytes)) {
				return new Address(text, Kind.Tailscale);
			}
			if (network.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase)) {
				return new Address(text, Kind.ZeroTier);
			}
			return new Address(text, IsHomeNetwork(bytes) ? Kind.HomeNetwork : Kind.Internet);
		}

		// Tailscale gives computers addresses in 100.64.0.0/10 and
		// fd7a:115c:a1e0::/48.
		private static bool IsTailscale(byte[] b) {
			return (b.Length == 4 && b[0] == 100 && (b[1] & 0xC0) == 64)
				|| (b.Length == 16 && b[0] == 0xfd && b[1] == 0x7a && b[2] == 0x11 && b[3] == 0x5c && b[4] == 0xa1 && b[5] == 0xe0);
		}

		// The private ranges routers hand out at home (and IPv6's unique
		// local addresses).
		private static bool IsHomeNetwork(byte[] b) {
			if (b.Length == 4) {
				return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168);
			}
			return (b[0] & 0xFE) == 0xFC;
		}
	}
}

using System.Linq;
using System.Net;
using System.Threading.Tasks;
using C7Engine.Online;
using Xunit;
using static C7Engine.Online.HostAddresses;
using static EngineTests.Online.OnlineTestKit;

namespace EngineTests.Online;

// The addresses a host offers for its invite, and its password.
public class HostAddressesTest {
	[Theory]
	[InlineData("100.101.102.103", "Ethernet", Kind.Tailscale)]
	[InlineData("100.64.0.1", "", Kind.Tailscale)]
	[InlineData("fd7a:115c:a1e0::1234", "", Kind.Tailscale)]
	[InlineData("10.147.17.5", "ZeroTier One [8056c2e21c000001]", Kind.ZeroTier)]
	[InlineData("192.168.1.20", "Wi-Fi", Kind.HomeNetwork)]
	[InlineData("10.0.0.7", "Ethernet", Kind.HomeNetwork)]
	[InlineData("172.20.1.1", "Ethernet", Kind.HomeNetwork)]
	[InlineData("fd12:3456::1", "Ethernet", Kind.HomeNetwork)]
	[InlineData("203.0.113.9", "Ethernet", Kind.Internet)]
	[InlineData("2001:db8::1", "Ethernet", Kind.Internet)]
	[InlineData("100.128.0.1", "Ethernet", Kind.Internet)]
	[InlineData("172.32.0.1", "Ethernet", Kind.Internet)]
	public void EachAddressIsNamedForItsNetwork(string address, string network, Kind kind) {
		Assert.Equal(kind, Classify(IPAddress.Parse(address), network).kind);
	}

	[Theory]
	[InlineData("127.0.0.1")]
	[InlineData("::1")]
	[InlineData("169.254.10.20")]
	[InlineData("fe80::1%12")]
	public void AddressesNoOneElseCanReachAreLeftOut(string address) {
		Assert.Null(Classify(IPAddress.Parse(address), "Ethernet"));
	}

	[Fact]
	public void TailscaleComesFirstThenTheHomeNetwork() {
		Address[] found = [
			new("203.0.113.9", Kind.Internet), new("192.168.1.20", Kind.HomeNetwork),
			new("fd7a:115c:a1e0::1", Kind.Tailscale), new("100.101.102.103", Kind.Tailscale),
			new("192.168.1.20", Kind.HomeNetwork),
		];
		Assert.Equal(["100.101.102.103", "fd7a:115c:a1e0::1", "192.168.1.20", "203.0.113.9"], Ordered(found).Select(a => a.address));
		Assert.Equal("Tailscale: 100.101.102.103", Ordered(found)[0].Label);
	}

	// (This computer has a network to offer.)
	[Fact]
	public void ThisComputersAddressesAreFound() {
		Assert.All(OfThisComputer(), a => Assert.False(IPAddress.IsLoopback(IPAddress.Parse(a.address))));
	}

	[Fact]
	public void NewPasswordsAreReadableAndDiffer() {
		string password = OnlineHost.NewPassword();
		Assert.Equal(10, password.Length);
		Assert.DoesNotContain(password, c => "il1o0".Contains(c));
		Assert.NotEqual(password, OnlineHost.NewPassword());
	}

	// A new password is for those who join after.
	[Fact]
	public async Task TheHostCanChangeThePassword() {
		using OnlineHost host = StartHost();
		using OnlineClient first = await Join(host, "Sargon");
		host.SetPassword("lamassu");
		await Assert.ThrowsAsync<JoinFailedException>(() => Join(host, "Naram-Sin"));
		using OnlineClient second = await Join(host, "Naram-Sin", password: "lamassu");
		Assert.True(first.Connected);
	}
}

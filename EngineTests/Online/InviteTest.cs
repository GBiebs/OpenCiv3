using System.Linq;
using C7Engine.Online;
using Xunit;
using static C7Engine.Online.HostAddresses;

namespace EngineTests.Online;

// The invite a host gives its players: one code, with the addresses its
// game may find the host at and its certificate's fingerprint.
public class InviteTest {
	private const string Fingerprint = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

	[Fact]
	public void TheCodeIsReadBack() {
		Invite invite = new(["100.101.102.103", "192.168.1.20", "fd7a:115c:a1e0::1", "gunnars-pc.tail1234.ts.net"], 47300, Fingerprint);
		string code = invite.Code;
		Assert.StartsWith("OC3-", code);
		Assert.DoesNotContain("192.168", code);
		Invite read = Invite.Parse(code);
		Assert.Equal(invite.hosts, read.hosts);
		Assert.Equal(47300, read.port);
		Assert.Equal(Fingerprint, read.fingerprint);
	}

	// Pasted with spaces, in small letters, without its dashes.
	[Fact]
	public void TheCodeCanBeWrittenLoosely() {
		string code = new Invite("100.64.1.2", 47300, Fingerprint).Code;
		Assert.Equal(Fingerprint, Invite.Parse("  " + code.ToLowerInvariant() + " ").fingerprint);
		Assert.Equal(Fingerprint, Invite.Parse(code.Replace("-", " ")).fingerprint);
		Assert.Equal(Fingerprint, Invite.Parse("OC3" + code[4..].Replace("-", "")).fingerprint);
	}

	// A letter wrong or missing: the checksum says so.
	[Fact]
	public void AMistypedCodeIsNoInvite() {
		string code = new Invite(["100.64.1.2", "192.168.1.20"], 47300, Fingerprint).Code;
		Assert.Null(Invite.Parse(code[..^1]));
		Assert.Null(Invite.Parse(code[..^5]));
		char[] typo = code.ToCharArray();
		typo[10] = typo[10] == 'A' ? 'B' : 'A';
		Assert.Null(Invite.Parse(new string(typo)));
		Assert.Null(Invite.Parse("OC3-HELLO"));
		Assert.Null(Invite.Parse("OC3-1111-0000"));
	}

	// Typed out by hand, one address.
	[Theory]
	[InlineData("100.64.1.2:47300/" + Fingerprint, "100.64.1.2")]
	[InlineData("[fd7a:115c:a1e0::1]:47300/" + Fingerprint, "fd7a:115c:a1e0::1")]
	[InlineData("gunnars-pc:47300/abcd-efgh-ijkl-mnop qrst-uvwx-yz23-4567", "gunnars-pc")]
	public void AnAddressAndFingerprintAreReadToo(string text, string host) {
		Invite read = Invite.Parse(text);
		Assert.Equal([host], read.hosts);
		Assert.Equal(47300, read.port);
		Assert.Equal(Fingerprint, read.fingerprint);
	}

	[Theory]
	[InlineData("")]
	[InlineData("hello")]
	[InlineData("100.64.1.2:47300")]
	[InlineData("100.64.1.2/" + Fingerprint)]
	[InlineData("100.64.1.2:0/" + Fingerprint)]
	[InlineData("100.64.1.2:70000/" + Fingerprint)]
	[InlineData("bad host!:47300/" + Fingerprint)]
	[InlineData("100.64.1.2:47300/ABCDEFGH")]
	[InlineData("[1.2.3.4]:47300/" + Fingerprint)]
	public void WhatIsNotAnInviteIsRefused(string text) {
		Assert.Null(Invite.Parse(text));
	}

	// The players learn no address that says where the host is: this
	// computer's internet addresses stay out, unless the host gives one.
	[Fact]
	public void TheInviteHoldsNoInternetAddress() {
		Address[] found = [
			new("2600:4040:29f2::1", Kind.Internet), new("192.168.1.20", Kind.HomeNetwork),
			new("100.101.102.103", Kind.Tailscale), new("10.0.0.7", Kind.HomeNetwork), new("172.20.1.1", Kind.HomeNetwork),
			new("fd7a:115c:a1e0::1", Kind.Tailscale),
		];
		Assert.Equal(["100.101.102.103", "192.168.1.20", "10.0.0.7", "172.20.1.1"], ForInvite(found));
		Assert.Equal(["100.101.102.103", "192.168.1.20", "10.0.0.7", "203.0.113.9"], ForInvite(found, " 203.0.113.9 "));
		Assert.Equal(["100.101.102.103", "fd7a:115c:a1e0::1"], ForInvite(found.Where(a => a.kind == Kind.Tailscale)));
		Assert.Empty(ForInvite([new("2600:4040:29f2::1", Kind.Internet)]));
	}

	[Fact]
	public void EachHostHasItsOwnFingerprint() {
		using var one = HostCertificate.Create();
		using var two = HostCertificate.Create();
		string fingerprint = HostCertificate.Fingerprint(one);
		Assert.Equal(HostCertificate.FingerprintLength, fingerprint.Length);
		Assert.True(fingerprint.All(HostCertificate.IsFingerprintCharacter));
		Assert.Equal(fingerprint, HostCertificate.Fingerprint(one));
		Assert.NotEqual(fingerprint, HostCertificate.Fingerprint(two));
		Assert.Equal(fingerprint, Invite.Parse(new Invite("127.0.0.1", 1, fingerprint).Code).fingerprint);
	}
}

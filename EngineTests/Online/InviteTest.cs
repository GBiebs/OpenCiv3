using System.Linq;
using C7Engine.Online;
using Xunit;

namespace EngineTests.Online;

// The invite a host gives its players: where it is and its certificate's
// fingerprint.
public class InviteTest {
	private const string Fingerprint = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

	[Theory]
	[InlineData("100.64.1.2", "100.64.1.2:47300/" + Fingerprint)]
	[InlineData("fd7a:115c:a1e0::1", "[fd7a:115c:a1e0::1]:47300/" + Fingerprint)]
	[InlineData("gunnars-pc.tail1234.ts.net", "gunnars-pc.tail1234.ts.net:47300/" + Fingerprint)]
	public void ItIsWrittenAndReadBack(string host, string written) {
		Invite invite = new(host, 47300, Fingerprint);
		Assert.Equal(written, invite.ToString());
		Invite read = Invite.Parse(written);
		Assert.Equal(host, read.host);
		Assert.Equal(47300, read.port);
		Assert.Equal(Fingerprint, read.fingerprint);
	}

	// Pasted with spaces, dashes or in small letters.
	[Fact]
	public void TheFingerprintCanBeWrittenLoosely() {
		Invite read = Invite.Parse("  100.64.1.2:47300/abcd-efgh-ijkl-mnop qrst-uvwx-yz23-4567 ");
		Assert.Equal(Fingerprint, read.fingerprint);
	}

	[Theory]
	[InlineData("")]
	[InlineData("100.64.1.2:47300")]
	[InlineData("100.64.1.2/" + Fingerprint)]
	[InlineData("100.64.1.2:0/" + Fingerprint)]
	[InlineData("100.64.1.2:70000/" + Fingerprint)]
	[InlineData("100.64.1.2:port/" + Fingerprint)]
	[InlineData("bad host!:47300/" + Fingerprint)]
	[InlineData("-pc:47300/" + Fingerprint)]
	[InlineData("100.64.1.2:47300/ABCDEFGH")]
	[InlineData("100.64.1.2:47300/ABCDEFGHIJKLMNOPQRSTUVWXYZ234561")]
	[InlineData("[fd7a::1:47300/" + Fingerprint)]
	[InlineData("[1.2.3.4]:47300/" + Fingerprint)]
	[InlineData("fd7a::1:47300/" + Fingerprint)]
	public void WhatIsNotAnInviteIsRefused(string text) {
		Assert.Null(Invite.Parse(text));
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
		Assert.NotNull(Invite.Parse(new Invite("127.0.0.1", 1, fingerprint).ToString()));
	}
}

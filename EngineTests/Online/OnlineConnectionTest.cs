using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using C7Engine.Online;
using Xunit;
using static EngineTests.Online.OnlineTestKit;

namespace EngineTests.Online;

// Online play's connection, over this computer's network: a player joins
// a host with its invite and password, and the host turns away whatever
// else knocks.
public class OnlineConnectionTest {
	[Fact]
	public async Task APlayerJoinsWithTheInviteAndPassword() {
		using OnlineHost host = StartHost();
		using OnlineClient client = await Join(host);
		Assert.Equal(1, client.playerId);
		Assert.Equal("Hammurabi", client.hostName);
		PlayerJoined joined = await Next<PlayerJoined>(host);
		Assert.Equal("Sargon", joined.player.name);
		Assert.Equal([1], host.Players.Select(p => p.id));

		// Both ways, in order.
		host.Send(1, new LobbyNotice { text = "one" });
		host.Send(1, new LobbyNotice { text = "two" });
		Assert.Equal("one", (await Next<LobbyNotice>(client)).text);
		Assert.Equal("two", (await Next<LobbyNotice>(client)).text);
		client.Send(new TakeSeat { seat = 2 });
		MessageFrom from = await Next<MessageFrom>(host);
		Assert.Equal(1, from.player.id);
		Assert.Equal(2, Assert.IsType<TakeSeat>(from.message).seat);
	}

	[Fact]
	public async Task AWrongPasswordIsRefused() {
		using OnlineHost host = StartHost();
		JoinFailedException failed = await Assert.ThrowsAsync<JoinFailedException>(() => Join(host, password: "babylon"));
		Assert.Equal("That isn't the game's password.", failed.Message);
		Assert.Empty(host.Players);
		Assert.False(host.TryNextEvent(out _));
	}

	[Fact]
	public async Task TooManyWrongPasswordsLockTheAddressOut() {
		OnlineLimits limits = Quick();
		limits.wrongPasswordsBeforeLockout = 3;
		limits.lockout = TimeSpan.FromMilliseconds(800);
		using OnlineHost host = StartHost(limits);
		for (int i = 0; i < 3; i++) {
			await Assert.ThrowsAsync<JoinFailedException>(() => Join(host, password: "babylon"));
		}
		JoinFailedException failed = await Assert.ThrowsAsync<JoinFailedException>(() => Join(host));
		Assert.Equal("Too many wrong passwords. Try again later.", failed.Message);
		await Task.Delay(1000);
		using OnlineClient client = await Join(host);
	}

	// The password goes only to the host the invite is for: a computer with
	// another certificate never hears it.
	[Fact]
	public async Task TheGameTalksOnlyToTheInvitesHost() {
		using OnlineHost host = StartHost();
		using var other = HostCertificate.Create();
		Invite forged = new("127.0.0.1", host.port, HostCertificate.Fingerprint(other));
		JoinFailedException failed = await Assert.ThrowsAsync<JoinFailedException>(
			() => OnlineClient.Join(forged, "Sargon", Password, GameVersion, Quick()));
		Assert.Contains("isn't the host this invite is for", failed.Message);
		await Task.Delay(200);
		Assert.Equal(0, host.hellos);
	}

	[Fact]
	public async Task BothNeedTheSameVersion() {
		using OnlineHost host = StartHost();
		JoinFailedException failed = await Assert.ThrowsAsync<JoinFailedException>(() => Join(host, version: "test 2"));
		Assert.Equal("The host plays OpenCiv3 test 1; you have test 2. Both need the same version.", failed.Message);
	}

	[Fact]
	public async Task NoHostThereIsSaid() {
		OnlineHost host = StartHost();
		Invite invite = InviteTo(host);
		host.Dispose();
		JoinFailedException failed = await Assert.ThrowsAsync<JoinFailedException>(
			() => OnlineClient.Join(invite, "Sargon", Password, GameVersion, Quick()));
		Assert.Equal($"Couldn't reach a host at 127.0.0.1, port {invite.port}.", failed.Message);
	}

	// Before the Hello (or anything else it can't read), a connection that
	// says something it shouldn't is dropped, and the host goes on.
	[Theory]
	[InlineData("not json at all")]
	[InlineData("{}")]
	[InlineData("[]")]
	[InlineData("{\"kind\":\"unknown\"}")]
	[InlineData("{\"kind\":\"hello\",\"protocol\":1,\"game\":\"test 1\",\"name\":123,\"password\":\"ziggurat\"}")]
	[InlineData("{\"kind\":\"hello\",\"protocol\":1,\"game\":\"test 1\",\"name\":\"\\u0007bell\",\"password\":\"ziggurat\"}")]
	[InlineData("{\"kind\":\"hello\",\"protocol\":1,\"game\":\"test 1\",\"name\":\"" + "Sargon of Akkad, the great king, king of the four quarters" + "\",\"password\":\"ziggurat\"}")]
	[InlineData("{\"kind\":\"takeSeat\",\"seat\":1}")]
	[InlineData("{\"protocol\":1,\"kind\":\"hello\",\"game\":\"test 1\",\"name\":\"Sargon\",\"password\":\"ziggurat\"}")]
	public async Task WhatTheHostDoesntReadDropsTheConnection(string payload) {
		using OnlineHost host = StartHost();
		using SslStream ssl = await Tls(host);
		await WriteFrame(ssl, Encoding.UTF8.GetBytes(payload));
		Assert.True(await ClosedWithin(ssl, 2000));
		Assert.Empty(host.Players);
		using OnlineClient client = await Join(host);
	}

	[Fact]
	public async Task DeepJsonDropsTheConnection() {
		using OnlineHost host = StartHost();
		using SslStream ssl = await Tls(host);
		await WriteFrame(ssl, Encoding.UTF8.GetBytes(new string('[', 200) + new string(']', 200)));
		Assert.True(await ClosedWithin(ssl, 2000));
	}

	// Its length alone gives it away: the host reads none of it.
	[Fact]
	public async Task ATooBigMessageDropsTheConnection() {
		using OnlineHost host = StartHost();
		using SslStream ssl = await Tls(host);
		await WriteFrame(ssl, new byte[16], length: 100_000_000);
		Assert.True(await ClosedWithin(ssl, 2000));
		using SslStream negative = await Tls(host);
		await WriteFrame(negative, new byte[16], length: -5);
		Assert.True(await ClosedWithin(negative, 2000));
	}

	[Fact]
	public async Task ATooBigMessageDropsAPlayer() {
		using OnlineHost host = StartHost();
		using SslStream ssl = await Tls(host);
		await WriteMessage(ssl, Hello());
		await Next<PlayerJoined>(host);
		await WriteMessage(ssl, new LobbyNotice { text = new string('x', 400) });
		try {
			await WriteFrame(ssl, new byte[70_000]);
		} catch (IOException) {
			// (The host stopped reading at the length.)
		}
		PlayerLeft left = await Next<PlayerLeft>(host);
		Assert.Equal("was disconnected", left.reason);
		Assert.Empty(host.Players);
	}

	// Connecting and saying nothing, unencrypted or encrypted.
	[Fact]
	public async Task ASilentConnectionIsDropped() {
		OnlineLimits limits = Quick();
		limits.handshakeTime = TimeSpan.FromMilliseconds(400);
		using OnlineHost host = StartHost(limits);
		using TcpClient tcp = await Tcp(host);
		Assert.True(await ClosedWithin(tcp.GetStream(), 2000));
		using SslStream ssl = await Tls(host);
		Assert.True(await ClosedWithin(ssl, 2000));
	}

	// A player who joined and went quiet (no pings) is let go; one whose
	// game pings stays however long nothing happens.
	[Fact]
	public async Task OnlyAQuietPlayerIsLetGo() {
		OnlineLimits limits = Quick();
		limits.pingInterval = TimeSpan.FromMilliseconds(200);
		limits.silenceTime = TimeSpan.FromMilliseconds(800);
		using OnlineHost host = StartHost(limits);
		using OnlineClient pinging = await Join(host, "Sargon", limits: limits);
		await Next<PlayerJoined>(host);
		using SslStream quiet = await Tls(host);
		await WriteMessage(quiet, Hello("Naram-Sin"));
		await Next<PlayerJoined>(host);

		PlayerLeft left = await Next<PlayerLeft>(host, 3000);
		Assert.Equal("Naram-Sin", left.player.name);
		await Task.Delay(1500);
		Assert.True(pinging.Connected);
		Assert.Equal(["Sargon"], host.Players.Select(p => p.name));
	}

	[Fact]
	public async Task OneAddressCanOnlyOpenSoManyConnections() {
		OnlineLimits limits = Quick();
		limits.maxConnectionsPerAddress = 2;
		using OnlineHost host = StartHost(limits);
		using TcpClient one = await Tcp(host);
		using TcpClient two = await Tcp(host);
		using TcpClient three = await Tcp(host);
		Assert.True(await ClosedWithin(three.GetStream(), 1000));
		Assert.False(await ClosedWithin(one.GetStream(), 300));
		one.Dispose();
		two.Dispose();
		await Task.Delay(300);
		using OnlineClient client = await Join(host);
	}

	[Fact]
	public async Task AFloodDropsThePlayer() {
		OnlineLimits limits = Quick();
		limits.messagesPerSecond = 10;
		limits.messageBurst = 20;
		using OnlineHost host = StartHost(limits);
		using OnlineClient client = await Join(host);
		await Next<PlayerJoined>(host);
		for (int i = 0; i < 100; i++) {
			client.Send(new LeaveSeat());
		}
		Assert.Equal("was disconnected", (await Next<PlayerLeft>(host)).reason);
	}

	[Fact]
	public async Task PlayersHearWhenTheHostStops() {
		OnlineHost host = StartHost();
		using OnlineClient client = await Join(host);
		host.Dispose();
		Goodbye goodbye = await Next<Goodbye>(client);
		Assert.Equal("The host has stopped the game.", goodbye.reason);
		Assert.False(client.Connected);
	}

	[Fact]
	public async Task TheHostHearsWhenAPlayerLeaves() {
		using OnlineHost host = StartHost();
		OnlineClient client = await Join(host);
		await Next<PlayerJoined>(host);
		client.Dispose();
		Assert.Equal("Sargon", (await Next<PlayerLeft>(host)).player.name);
		Assert.Empty(host.Players);
	}

	// Random bytes, as many times as it takes: each connection is dropped,
	// and the host still lets the real player in.
	[Fact]
	public async Task NoiseNeverTakesTheHostDown() {
		OnlineLimits limits = Quick();
		// (Connections close a moment after the noise.)
		limits.maxConnectionsPerAddress = 100;
		limits.maxConnections = 100;
		using OnlineHost host = StartHost(limits);
		Random random = new(1054);
		for (int i = 0; i < 40; i++) {
			using SslStream ssl = await Tls(host);
			byte[] noise = new byte[random.Next(1, 3000)];
			random.NextBytes(noise);
			if (i % 2 == 0) {
				await WriteFrame(ssl, noise);
			} else {
				await ssl.WriteAsync(noise);
				await ssl.FlushAsync();
			}
			ssl.Dispose();
		}
		await Task.Delay(300);
		using OnlineClient client = await Join(host);
		Assert.Equal("Sargon", (await Next<PlayerJoined>(host)).player.name);
	}
}

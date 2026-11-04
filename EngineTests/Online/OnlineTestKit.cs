using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;
using C7Engine.Online;

namespace EngineTests.Online;

// A host on this computer and ways to talk to it, politely or not.
internal static class OnlineTestKit {
	public const string GameVersion = "test 1";
	public const string Password = "ziggurat";

	public static OnlineHost StartHost(OnlineLimits limits = null, string password = Password) {
		OnlineHost host = new("Hammurabi", password, GameVersion, limits ?? Quick());
		host.Start(port: 0);
		return host;
	}

	// Short times, so tests don't wait.
	public static OnlineLimits Quick() => new() {
		handshakeTime = TimeSpan.FromSeconds(3),
		pingInterval = TimeSpan.FromSeconds(1),
		silenceTime = TimeSpan.FromSeconds(4),
	};

	public static Invite InviteTo(OnlineHost host) => host.InviteAt("127.0.0.1");

	public static Task<OnlineClient> Join(OnlineHost host, string name = "Sargon", string password = Password, string version = GameVersion, OnlineLimits limits = null) {
		return OnlineClient.Join(InviteTo(host), name, password, version, limits ?? Quick());
	}

	// The next event of type T (others are skipped).
	public static async Task<T> Next<T>(OnlineHost host, int milliseconds = 5000) where T : HostEvent {
		Stopwatch waited = Stopwatch.StartNew();
		while (waited.ElapsedMilliseconds < milliseconds) {
			if (host.TryNextEvent(out HostEvent hostEvent) && hostEvent is T found) {
				return found;
			}
			await Task.Delay(10);
		}
		throw new TimeoutException($"No {typeof(T).Name}");
	}

	public static async Task<T> Next<T>(OnlineClient client, int milliseconds = 5000) where T : OnlineMessage {
		Stopwatch waited = Stopwatch.StartNew();
		while (waited.ElapsedMilliseconds < milliseconds) {
			if (client.TryNextMessage(out OnlineMessage message) && message is T found) {
				return found;
			}
			await Task.Delay(10);
		}
		throw new TimeoutException($"No {typeof(T).Name}");
	}

	public static async Task<TcpClient> Tcp(OnlineHost host) {
		TcpClient tcp = new();
		await tcp.ConnectAsync(IPAddress.Loopback, host.port);
		return tcp;
	}

	// An encrypted connection that then says whatever the test likes.
	public static async Task<SslStream> Tls(OnlineHost host) {
		TcpClient tcp = await Tcp(host);
		SslStream ssl = new(tcp.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
		await ssl.AuthenticateAsClientAsync("OpenCiv3 host");
		return ssl;
	}

	public static async Task WriteFrame(Stream stream, byte[] payload, int? length = null) {
		byte[] header = new byte[4];
		BinaryPrimitives.WriteInt32BigEndian(header, length ?? payload.Length);
		await stream.WriteAsync(header);
		await stream.WriteAsync(payload);
		await stream.FlushAsync();
	}

	public static Task WriteMessage(Stream stream, OnlineMessage message) => WriteFrame(stream, OnlineMessage.Write(message));

	public static Hello Hello(string name = "Sargon", string password = Password) => new() {
		protocol = OnlineProtocol.Version,
		game = GameVersion,
		name = name,
		password = password,
	};

	// Whether the host closes `stream` within the time (reading and
	// discarding anything it sends first).
	public static async Task<bool> ClosedWithin(Stream stream, int milliseconds) {
		byte[] buffer = new byte[4096];
		Stopwatch waited = Stopwatch.StartNew();
		try {
			while (waited.ElapsedMilliseconds < milliseconds) {
				Task<int> read = stream.ReadAsync(buffer).AsTask();
				if (await Task.WhenAny(read, Task.Delay(milliseconds - (int)waited.ElapsedMilliseconds)) != read) {
					return false;
				}
				if (await read == 0) {
					return true;
				}
			}
			return false;
		} catch (Exception) {
			return true;
		}
	}
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace C7Engine.Online {
	// A player connected to the host (the host itself is player 0).
	public sealed class RemotePlayer {
		public int id { get; internal set; }
		public string name { get; internal set; }
		public IPAddress address { get; internal set; }
		internal Connection connection;
	}

	// What happened on the host's network side, for the game to handle on
	// its own thread (OnlineHost.TryNextEvent).
	public abstract record HostEvent(RemotePlayer player);
	public sealed record PlayerJoined(RemotePlayer player) : HostEvent(player);
	public sealed record PlayerLeft(RemotePlayer player, string reason) : HostEvent(player);
	public sealed record MessageFrom(RemotePlayer player, OnlineMessage message) : HostEvent(player);

	// Online play: the host's side. It listens on one port while hosting,
	// on every address of this computer (a private network tool's too). A
	// connection is encrypted with the host's certificate (the invite has
	// its fingerprint), then must say Hello with this version of the game
	// and the game's password in time, or it's dropped; then its messages
	// come in as events. Network work happens on other threads; the game
	// reads the events on its own.
	public sealed class OnlineHost : IDisposable {
		private static readonly ILogger log = Log.ForContext<OnlineHost>();

		public readonly string fingerprint;
		public readonly string hostName;
		public int port { get; private set; }

		private readonly byte[] passwordHash;
		private readonly string gameVersion;
		private readonly OnlineLimits limits;
		private readonly X509Certificate2 certificate;
		private readonly PasswordGuard guard;
		private readonly CancellationTokenSource stopping = new();
		private readonly ConcurrentQueue<HostEvent> events = new();
		private readonly ConcurrentDictionary<int, RemotePlayer> players = new();
		private readonly Dictionary<IPAddress, int> connectionsFrom = new();
		private int connections;
		private int lastId;
		private TcpListener listener;

		// For tests: Hellos that reached the host.
		internal int hellos;

		public OnlineHost(string hostName, string gamePassword, string gameVersion, OnlineLimits limits = null) {
			this.hostName = hostName;
			passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(gamePassword ?? ""));
			this.gameVersion = gameVersion;
			this.limits = limits ?? new OnlineLimits();
			guard = new PasswordGuard(this.limits);
			certificate = HostCertificate.Create();
			fingerprint = HostCertificate.Fingerprint(certificate);
		}

		// Starts listening (port 0: any free one, for tests).
		public void Start(int port = OnlineProtocol.DefaultPort) {
			listener = new TcpListener(IPAddress.IPv6Any, port);
			listener.Server.DualMode = true;
			listener.Start();
			this.port = ((IPEndPoint)listener.LocalEndpoint).Port;
			log.Information("Hosting on port {Port}", this.port);
			_ = Task.Run(Accept);
			_ = Task.Run(KeepAlive);
		}

		public Invite InviteAt(string address) => new(address, port, fingerprint);

		public IReadOnlyList<RemotePlayer> Players => players.Values.OrderBy(p => p.id).ToList();

		public bool TryNextEvent(out HostEvent hostEvent) => events.TryDequeue(out hostEvent);

		public void Send(int playerId, OnlineMessage message) {
			if (players.TryGetValue(playerId, out RemotePlayer player)) {
				player.connection.Send(message);
			}
		}

		public void SendToAll(OnlineMessage message) {
			foreach (RemotePlayer player in players.Values) {
				player.connection.Send(message);
			}
		}

		public void Drop(int playerId, string reason) {
			if (players.TryGetValue(playerId, out RemotePlayer player)) {
				_ = player.connection.SendAndClose(new Goodbye { reason = reason }, TimeSpan.FromSeconds(2));
			}
		}

		private async Task Accept() {
			while (!stopping.IsCancellationRequested) {
				TcpClient tcp;
				try {
					tcp = await listener.AcceptTcpClientAsync(stopping.Token);
				} catch (OperationCanceledException) {
					break;
				} catch (ObjectDisposedException) {
					break;
				} catch (SocketException e) {
					log.Debug("Accepting a connection failed: {Error}", e.Message);
					continue;
				}
				_ = Task.Run(() => Serve(tcp));
			}
		}

		private bool Admit(IPAddress address) {
			lock (connectionsFrom) {
				int from = connectionsFrom.GetValueOrDefault(address);
				if (connections >= limits.maxConnections || from >= limits.maxConnectionsPerAddress) {
					return false;
				}
				connections++;
				connectionsFrom[address] = from + 1;
				return true;
			}
		}

		private void Release(IPAddress address) {
			lock (connectionsFrom) {
				connections--;
				if (--connectionsFrom[address] == 0) {
					connectionsFrom.Remove(address);
				}
			}
		}

		private async Task Serve(TcpClient tcp) {
			IPAddress address = ((IPEndPoint)tcp.Client.RemoteEndPoint).Address;
			if (address.IsIPv4MappedToIPv6) {
				address = address.MapToIPv4();
			}
			if (!Admit(address)) {
				log.Information("Turned away a connection from {Address}: too many connections", address);
				tcp.Dispose();
				return;
			}
			RemotePlayer player = null;
			SslStream ssl = null;
			Connection connection = null;
			string reason = "left the game";
			try {
				ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
				using (CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token)) {
					handshake.CancelAfter(limits.handshakeTime);
					await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
						ServerCertificate = certificate,
						ClientCertificateRequired = false,
						EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
					}, handshake.Token);
					connection = new Connection(ssl, limits.maxMessageFromPlayer, limits.messagesPerSecond, limits.messageBurst);
					if (await connection.Receive(handshake.Token) is not Hello hello) {
						return;
					}
					Interlocked.Increment(ref hellos);
					string refusal = Refusal(hello, address);
					if (refusal != null) {
						log.Information("Refused {Name} from {Address}: {Reason}", hello.name, address, refusal);
						await connection.SendAndClose(new Refused { reason = refusal }, TimeSpan.FromSeconds(2));
						return;
					}
					player = new RemotePlayer { id = Interlocked.Increment(ref lastId), name = hello.name.Trim(), address = address, connection = connection };
					players[player.id] = player;
					connection.Send(new Welcome { you = player.id, hostName = hostName });
					log.Information("{Name} joined from {Address}", player.name, address);
					events.Enqueue(new PlayerJoined(player));
				}
				while (true) {
					using CancellationTokenSource silence = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
					silence.CancelAfter(limits.silenceTime);
					OnlineMessage message = await connection.Receive(silence.Token);
					if (message == null || message is Goodbye) {
						break;
					}
					if (message is not Ping) {
						events.Enqueue(new MessageFrom(player, message));
					}
				}
			} catch (ProtocolException e) {
				log.Information("Dropped {Who} from {Address}: {Error}", player?.name ?? "a connection", address, e.Message);
				reason = "was disconnected";
			} catch (Exception e) when (e is OperationCanceledException or AuthenticationException or System.IO.IOException or ObjectDisposedException or SocketException) {
				// (Too slow, not our game, or the connection went.)
				reason = stopping.IsCancellationRequested ? "left the game" : "lost the connection";
			} catch (Exception e) {
				log.Warning(e, "Unexpected error serving {Address}", address);
				reason = "was disconnected";
			} finally {
				connection?.Dispose();
				ssl?.Dispose();
				tcp.Dispose();
				Release(address);
				if (player != null && players.TryRemove(player.id, out _)) {
					events.Enqueue(new PlayerLeft(player, reason));
				}
			}
		}

		// Why `hello` can't come in, or null.
		private string Refusal(Hello hello, IPAddress address) {
			if (hello.protocol != OnlineProtocol.Version || hello.game != gameVersion) {
				return $"The host plays OpenCiv3 {gameVersion}; you have {hello.game}. Both need the same version.";
			}
			if (!guard.MayTry(address)) {
				return "Too many wrong passwords. Try again later.";
			}
			if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(hello.password ?? "")), passwordHash)) {
				guard.Wrong(address);
				return "That isn't the game's password.";
			}
			guard.Right(address);
			return null;
		}

		// A player's civ password (TakeSeat), guarded like the game's.
		internal bool MayTryPassword(RemotePlayer player) => guard.MayTry(player.address);
		internal void WrongPassword(RemotePlayer player) => guard.Wrong(player.address);

		private async Task KeepAlive() {
			while (!stopping.IsCancellationRequested) {
				try {
					await Task.Delay(limits.pingInterval / 2, stopping.Token);
				} catch (OperationCanceledException) {
					break;
				}
				foreach (RemotePlayer player in players.Values) {
					if (player.connection.SinceLastSent >= limits.pingInterval) {
						player.connection.Send(new Ping());
					}
				}
			}
		}

		public void Dispose() {
			if (stopping.IsCancellationRequested) {
				return;
			}
			// (A moment for the goodbyes to go out.)
			Task[] goodbyes = players.Values
				.Select(p => p.connection.SendAndClose(new Goodbye { reason = "The host has stopped the game." }, TimeSpan.FromSeconds(1)))
				.ToArray();
			Task.WaitAll(goodbyes, TimeSpan.FromSeconds(1));
			stopping.Cancel();
			listener?.Stop();
			certificate.Dispose();
			log.Information("Stopped hosting");
		}
	}
}

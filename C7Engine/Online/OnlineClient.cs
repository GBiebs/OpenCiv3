using System;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace C7Engine.Online {
	// Joining a host failed, and why (for the player to read).
	public class JoinFailedException : Exception {
		public JoinFailedException(string message) : base(message) { }
	}

	// Online play: a joining player's side. It connects to the invite's
	// address and goes on only if the host's certificate has the invite's
	// fingerprint, so the game's password goes to that host and no one
	// else; then the host's messages come in for the game to read on its
	// own thread (TryNextMessage). When the connection ends, a Goodbye with
	// the reason comes last.
	public sealed class OnlineClient : IDisposable {
		private static readonly ILogger log = Log.ForContext<OnlineClient>();

		public int playerId { get; private set; }
		public string hostName { get; private set; }

		private readonly TcpClient tcp;
		private readonly SslStream ssl;
		private readonly Connection connection;
		private readonly OnlineLimits limits;
		private readonly CancellationTokenSource stopping = new();
		private readonly ConcurrentQueue<OnlineMessage> received = new();

		private OnlineClient(TcpClient tcp, SslStream ssl, Connection connection, OnlineLimits limits) {
			this.tcp = tcp;
			this.ssl = ssl;
			this.connection = connection;
			this.limits = limits;
		}

		public bool Connected => !connection.Closed;

		// Tries the invite's addresses in turn (the first is the likeliest):
		// one where a computer with another certificate answers (another
		// computer with the same home-network address, at the player's
		// end) is passed over, never told the password.
		public static async Task<OnlineClient> Join(Invite invite, string name, string password, string gameVersion,
			OnlineLimits limits = null, CancellationToken cancel = default) {
			limits ??= new OnlineLimits();
			bool anotherComputer = false;
			foreach (string host in invite.hosts) {
				(TcpClient tcp, SslStream ssl, bool wrongCertificate) = await Reach(host, invite, limits, cancel);
				if (ssl != null) {
					return await Greet(tcp, ssl, host, name, password, gameVersion, limits, cancel);
				}
				anotherComputer |= wrongCertificate;
			}
			throw new JoinFailedException(anotherComputer
				? "A computer answered, but it isn't the host this invite is for (its certificate doesn't match). Ask the host for a new invite."
				: "Couldn't reach the host. Check that they're hosting, and that you're both on Tailscale (or the same home network).");
		}

		// An encrypted connection to the host at `host`, or none (and
		// whether a computer with another certificate answered there).
		private static async Task<(TcpClient, SslStream, bool)> Reach(string host, Invite invite, OnlineLimits limits, CancellationToken cancel) {
			TcpClient tcp = new();
			SslStream ssl = null;
			try {
				using (CancellationTokenSource connecting = CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
					connecting.CancelAfter(limits.connectTime);
					await tcp.ConnectAsync(host, invite.port, connecting.Token);
				}
				ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
				using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(cancel);
				handshake.CancelAfter(limits.handshakeTime);
				await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
					TargetHost = "OpenCiv3 host",
					EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
					CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
					RemoteCertificateValidationCallback = (_, certificate, _, _) =>
						certificate != null && HostCertificate.Fingerprint(certificate) == invite.fingerprint,
				}, handshake.Token);
				return (tcp, ssl, false);
			} catch (Exception e) when (e is SocketException or OperationCanceledException or AuthenticationException or System.IO.IOException) {
				ssl?.Dispose();
				tcp.Dispose();
				return (null, null, e is AuthenticationException);
			}
		}

		// Hello, with the password, to the host the connection reached.
		private static async Task<OnlineClient> Greet(TcpClient tcp, SslStream ssl, string host, string name, string password, string gameVersion,
			OnlineLimits limits, CancellationToken cancel) {
			Connection connection = null;
			using CancellationTokenSource joining = CancellationTokenSource.CreateLinkedTokenSource(cancel);
			joining.CancelAfter(limits.handshakeTime);
			try {
				connection = new Connection(ssl, limits.maxMessageFromHost);
				connection.Send(new Hello { protocol = OnlineProtocol.Version, game = gameVersion, name = name, password = password });
				OnlineMessage reply;
				try {
					reply = await connection.Receive(joining.Token);
				} catch (Exception e) when (e is ProtocolException or System.IO.IOException or OperationCanceledException) {
					throw new JoinFailedException("The host didn't answer.");
				}
				if (reply is Refused refused) {
					throw new JoinFailedException(refused.reason ?? "The host turned us away.");
				}
				if (reply is not Welcome welcome) {
					throw new JoinFailedException("The host didn't answer.");
				}
				OnlineClient client = new(tcp, ssl, connection, limits) { playerId = welcome.you, hostName = welcome.hostName };
				log.Information("Joined {Host} at {Address} as player {Id}", welcome.hostName, host, welcome.you);
				_ = Task.Run(client.Read);
				_ = Task.Run(client.KeepAlive);
				return client;
			} catch {
				connection?.Dispose();
				ssl?.Dispose();
				tcp.Dispose();
				throw;
			}
		}

		public bool TryNextMessage(out OnlineMessage message) => received.TryDequeue(out message);

		public void Send(OnlineMessage message) => connection.Send(message);

		private async Task Read() {
			string reason = "The connection to the host was lost.";
			try {
				while (true) {
					using CancellationTokenSource silence = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
					silence.CancelAfter(limits.silenceTime);
					OnlineMessage message = await connection.Receive(silence.Token);
					if (message == null) {
						break;
					}
					if (message is Goodbye goodbye) {
						reason = goodbye.reason ?? "The host has stopped the game.";
						break;
					}
					if (message is not Ping) {
						received.Enqueue(message);
					}
				}
			} catch (ProtocolException e) {
				log.Warning("Left the host: {Error}", e.Message);
				reason = "The host sent something this game doesn't read.";
			} catch (Exception) {
				if (stopping.IsCancellationRequested) {
					reason = "You left the game.";
				}
			}
			connection.Dispose();
			received.Enqueue(new Goodbye { reason = reason });
		}

		private async Task KeepAlive() {
			while (!stopping.IsCancellationRequested && !connection.Closed) {
				try {
					await Task.Delay(limits.pingInterval / 2, stopping.Token);
				} catch (OperationCanceledException) {
					break;
				}
				if (connection.SinceLastSent >= limits.pingInterval) {
					connection.Send(new Ping());
				}
			}
		}

		public void Dispose() {
			if (stopping.IsCancellationRequested) {
				return;
			}
			connection.SendAndClose(new Goodbye { reason = "left the game" }, TimeSpan.FromSeconds(1)).Wait(TimeSpan.FromSeconds(1));
			stopping.Cancel();
			ssl.Dispose();
			tcp.Dispose();
		}
	}
}

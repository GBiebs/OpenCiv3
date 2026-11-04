using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace C7Engine.Online {
	// The other side broke the rules (a message too big, not one we read,
	// too many of them): the connection is dropped.
	public class ProtocolException : Exception {
		public ProtocolException(string message) : base(message) { }
	}

	// One encrypted connection. Each message goes as a 4-byte length, then
	// its JSON; messages go out in the order they're sent, by one writer.
	internal sealed class Connection : IDisposable {
		private readonly Stream stream;
		private readonly int maxIncoming;
		private readonly Channel<OnlineMessage> outgoing = Channel.CreateUnbounded<OnlineMessage>(new UnboundedChannelOptions { SingleReader = true });
		private readonly CancellationTokenSource closing = new();
		private readonly Task writer;
		// When something last came in or went out (for the pings and the
		// silence limit).
		private long lastSent = Stopwatch.GetTimestamp();

		// Messages a second, for a player's connection (0: no limit).
		private readonly int perSecond;
		private readonly int burst;
		private double tokens;
		private long lastRefill = Stopwatch.GetTimestamp();

		public Connection(Stream stream, int maxIncoming, int perSecond = 0, int burst = 0) {
			this.stream = stream;
			this.maxIncoming = maxIncoming;
			this.perSecond = perSecond;
			this.burst = burst;
			tokens = burst;
			writer = Task.Run(Write);
		}

		public bool Closed => closing.IsCancellationRequested;

		public TimeSpan SinceLastSent => Stopwatch.GetElapsedTime(lastSent);

		public void Send(OnlineMessage message) {
			outgoing.Writer.TryWrite(message);
		}

		// Sends what's queued, then closes (a Goodbye or a Refused, last).
		public async Task SendAndClose(OnlineMessage message, TimeSpan wait) {
			Send(message);
			outgoing.Writer.TryComplete();
			await Task.WhenAny(writer, Task.Delay(wait));
			Dispose();
		}

		private async Task Write() {
			try {
				await foreach (OnlineMessage message in outgoing.Reader.ReadAllAsync(closing.Token)) {
					byte[] json = OnlineMessage.Write(message);
					byte[] length = new byte[4];
					BinaryPrimitives.WriteInt32BigEndian(length, json.Length);
					await stream.WriteAsync(length, closing.Token);
					await stream.WriteAsync(json, closing.Token);
					await stream.FlushAsync(closing.Token);
					lastSent = Stopwatch.GetTimestamp();
				}
			} catch (Exception) {
				// (Closed, or the other side went away: the reader sees it too.)
				Dispose();
			}
		}

		// The next message; null once the other side has closed the
		// connection. Throws ProtocolException for anything we don't read,
		// and OperationCanceledException when `cancel` fires first.
		public async Task<OnlineMessage> Receive(CancellationToken cancel) {
			using CancellationTokenSource both = CancellationTokenSource.CreateLinkedTokenSource(cancel, closing.Token);
			byte[] header = new byte[4];
			try {
				await stream.ReadExactlyAsync(header, both.Token);
			} catch (EndOfStreamException) {
				return null;
			}
			int length = BinaryPrimitives.ReadInt32BigEndian(header);
			if (length <= 0 || length > maxIncoming) {
				throw new ProtocolException($"a message of {length} bytes");
			}
			byte[] json = new byte[length];
			await stream.ReadExactlyAsync(json, both.Token);
			if (perSecond > 0 && !TakeToken()) {
				throw new ProtocolException("too many messages");
			}
			return OnlineMessage.Read(json) ?? throw new ProtocolException("a message we don't read");
		}

		private bool TakeToken() {
			long now = Stopwatch.GetTimestamp();
			tokens = Math.Min(burst, tokens + Stopwatch.GetElapsedTime(lastRefill, now).TotalSeconds * perSecond);
			lastRefill = now;
			if (tokens < 1) {
				return false;
			}
			tokens--;
			return true;
		}

		public void Dispose() {
			if (closing.IsCancellationRequested) {
				return;
			}
			closing.Cancel();
			outgoing.Writer.TryComplete();
			try {
				stream.Dispose();
			} catch (Exception) {
			}
		}
	}
}

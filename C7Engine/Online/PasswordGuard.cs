using System;
using System.Collections.Generic;
using System.Net;

namespace C7Engine.Online {
	// Guessing the host's passwords: an address that gets one wrong too
	// often waits (twice as long each time), and all addresses together get
	// only so many tries a minute, so a password can't be found by trying.
	internal sealed class PasswordGuard {
		private sealed class Record {
			public int wrong;
			public int lockouts;
			public DateTime until;
		}

		private readonly OnlineLimits limits;
		private readonly Func<DateTime> now;
		private readonly Dictionary<IPAddress, Record> records = new();
		private readonly Queue<DateTime> recentChecks = new();

		public PasswordGuard(OnlineLimits limits, Func<DateTime> now = null) {
			this.limits = limits;
			this.now = now ?? (() => DateTime.UtcNow);
		}

		// Whether `address` may try a password now (counted as a try).
		public bool MayTry(IPAddress address) {
			lock (records) {
				DateTime time = now();
				if (records.TryGetValue(address, out Record record) && record.until > time) {
					return false;
				}
				while (recentChecks.Count > 0 && time - recentChecks.Peek() >= TimeSpan.FromMinutes(1)) {
					recentChecks.Dequeue();
				}
				if (recentChecks.Count >= limits.passwordChecksPerMinute) {
					return false;
				}
				recentChecks.Enqueue(time);
				return true;
			}
		}

		public void Wrong(IPAddress address) {
			lock (records) {
				if (!records.TryGetValue(address, out Record record)) {
					records[address] = record = new Record();
				}
				record.wrong++;
				if (record.wrong >= limits.wrongPasswordsBeforeLockout) {
					record.until = now() + limits.lockout * Math.Pow(2, Math.Min(record.lockouts, 10));
					record.lockouts++;
					record.wrong = 0;
				}
			}
		}

		public void Right(IPAddress address) {
			lock (records) {
				records.Remove(address);
			}
		}
	}
}

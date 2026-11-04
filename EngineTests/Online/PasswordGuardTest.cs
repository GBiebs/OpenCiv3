using System;
using System.Net;
using C7Engine.Online;
using Xunit;

namespace EngineTests.Online;

// A host's passwords can't be found by trying them.
public class PasswordGuardTest {
	private readonly OnlineLimits limits = new() { wrongPasswordsBeforeLockout = 3, lockout = TimeSpan.FromMinutes(1), passwordChecksPerMinute = 10 };
	private DateTime now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
	private readonly PasswordGuard guard;
	private readonly IPAddress one = IPAddress.Parse("100.64.0.1");
	private readonly IPAddress two = IPAddress.Parse("100.64.0.2");

	public PasswordGuardTest() {
		guard = new PasswordGuard(limits, () => now);
	}

	private void WrongTries(IPAddress address, int times) {
		for (int i = 0; i < times; i++) {
			Assert.True(guard.MayTry(address));
			guard.Wrong(address);
		}
	}

	[Fact]
	public void AnAddressThatKeepsGuessingWaits() {
		WrongTries(one, 3);
		Assert.False(guard.MayTry(one));
		Assert.True(guard.MayTry(two));
		now += TimeSpan.FromSeconds(59);
		Assert.False(guard.MayTry(one));
		now += TimeSpan.FromSeconds(2);
		Assert.True(guard.MayTry(one));
	}

	[Fact]
	public void EachLockoutIsTwiceAsLong() {
		WrongTries(one, 3);
		now += TimeSpan.FromMinutes(1.5);
		WrongTries(one, 3);
		now += TimeSpan.FromMinutes(1.5);
		Assert.False(guard.MayTry(one));
		now += TimeSpan.FromMinutes(0.6);
		Assert.True(guard.MayTry(one));
	}

	[Fact]
	public void TheRightPasswordStartsTheCountAgain() {
		WrongTries(one, 2);
		guard.Right(one);
		WrongTries(one, 2);
		Assert.True(guard.MayTry(one));
	}

	// Many addresses guessing together.
	[Fact]
	public void AllAddressesTogetherGetSoManyTriesAMinute() {
		for (int i = 0; i < 10; i++) {
			Assert.True(guard.MayTry(IPAddress.Parse($"203.0.113.{i}")));
		}
		Assert.False(guard.MayTry(one));
		now += TimeSpan.FromSeconds(61);
		Assert.True(guard.MayTry(one));
	}
}

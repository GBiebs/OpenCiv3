using C7GameData;
using Xunit;

namespace EngineTests.GameData;

// A calendar from a Civ3 file ends on its last turn (540 in Civ3's own), but
// the game can be played on after it ends: time then goes on at the last
// period's pace instead of throwing.
public class CalendarEndTest {
	// Two periods: 2 turns of 50 years, then 3 of 10, so the last turn is 5.
	private static TimeOptions Calendar() => new() {
		startYear = -4000,
		timeScale = new int[,] { { 2, 3 }, { 50, 10 } },
	};

	[Fact]
	public void TheCalendarItself() {
		TimeOptions calendar = Calendar();
		Assert.Equal(-4000, calendar.GetRawNumber(0));
		Assert.Equal(-3900, calendar.GetRawNumber(2));
		Assert.Equal(-3870, calendar.GetRawNumber(5));
	}

	[Fact]
	public void PastTheLastTurnTimeGoesOnAtTheLastPace() {
		TimeOptions calendar = Calendar();
		Assert.Equal(-3860, calendar.GetRawNumber(6));
		Assert.Equal(-3770, calendar.GetRawNumber(15));
		Assert.Equal(-3770, calendar.GetAbsoluteYear(15));
		Assert.Equal(-3770, calendar.currentYear);
	}
}

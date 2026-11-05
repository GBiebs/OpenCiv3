using System.Linq;

namespace C7GameData {
	// Online: Civ3's turn timer (Slow, Normal, Fast or None). A turn's time
	// is the rules' base, plus some for each of the player's cities and
	// units, times the speed's per cent (Civ3's own times aren't documented;
	// players report a base plus a few seconds a city and a unit). When it's
	// up, the host ends the turn (MsgTurnTimeUp).
	public enum TurnTimer { None, Slow, Normal, Fast }

	public static class TurnTimes {
		public static int Seconds(GameData gameData, Player player) {
			Rules rules = gameData.rules;
			int percent = gameData.turnTimer switch {
				TurnTimer.Slow => rules.TurnTimerSlowPercent,
				TurnTimer.Normal => rules.TurnTimerNormalPercent,
				TurnTimer.Fast => rules.TurnTimerFastPercent,
				_ => 0,
			};
			int units = gameData.mapUnits.Count(u => u.owner == player);
			int seconds = rules.TurnTimerBaseSeconds + rules.TurnTimerSecondsPerCity * player.cities.Count + rules.TurnTimerSecondsPerUnit * units;
			return seconds * percent / 100;
		}
	}
}

using System.Diagnostics;

namespace C7GameData {
	// How long a game has been played, in seconds of real time, for Civ3's
	// time limit in minutes (Rules.TimeLimitMinutes; the scenario editor's
	// Scenario tab). Counted at each end of a round from the last (or from
	// when the game was started or loaded), and saved with the game. Only
	// in a game with such a limit: real time differs from one run to the
	// next, and the rest of a game's state doesn't (its checksums, replays
	// and tests rely on that).
	public static class PlayTime {
		private static readonly Stopwatch clock = new();
		private static GameData counting;

		// Adds the time since the last call for this game. The first call for
		// a game only starts the clock.
		public static void Tick(GameData gameData) {
			if ((gameData.rules?.TimeLimitMinutes ?? 0) <= 0) {
				return;
			}
			if (counting == gameData && clock.IsRunning) {
				gameData.playedSeconds += clock.Elapsed.TotalSeconds;
			}
			counting = gameData;
			clock.Restart();
		}

		// Whether the game has reached its time limit in minutes.
		public static bool LimitReached(GameData gameData) {
			int minutes = gameData.rules?.TimeLimitMinutes ?? 0;
			return minutes > 0 && gameData.playedSeconds >= minutes * 60.0;
		}
	}
}

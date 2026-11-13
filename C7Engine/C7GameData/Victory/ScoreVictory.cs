using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine;

public class ScoreVictory : IVictory {

	public string Header() => "Score";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		var history = gameData.history[player.HistoryKey];
		var lastTurn = history.LastOrDefault();
		var totalAccumulatedScore = lastTurn?.Score ?? 0;
		if (gameData.gameOver && player == gameData.winner) {
			totalAccumulatedScore += gameData.victoryBonus;
		}

		VictoryStatus status = new VictoryStatus {
			Player = player,
			Score = totalAccumulatedScore,
		};
		// The turn score is only shown in the victory status screen; the
		// end of turn victory check (which runs right after UpdateHistory
		// computed it) never reads it. So it is computed when first read.
		status.SetTurnScoreSource(() => ComputeTurnScore(player, gameData));
		return status;
	}

	public bool HasVictory(VictoryStatus status) {
		return false;
	}

	public IEnumerable<string[]> GenerateStatusRows(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		yield return TurnScorePrint(status, rivalStatuses);
		yield return ScorePrint(status, rivalStatuses);
	}

	private string[] TurnScorePrint(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		var topRivalByScore = rivalStatuses.OrderByDescending(r => r.TurnScore).FirstOrDefault();

		var topRival = topRivalByScore?.Player?.civilization?.name ?? "";
		var topRivalScore = topRivalByScore == null ? "" : $"{topRivalByScore.TurnScore}";

		return [
			"",
			"",
			"Turn Score:",
			$"{status.TurnScore}",
			topRival,
			topRivalScore
		];
	}

	private string[] ScorePrint(VictoryStatus status, List<VictoryStatus> rivalStatuses) {
		var topRivalByScore = rivalStatuses.OrderByDescending(r => r.Score).FirstOrDefault();

		var topRival = topRivalByScore?.Player?.civilization?.name ?? "";
		var topRivalScore = topRivalByScore == null ? "" : $"{topRivalByScore.Score}";

		return [
			"Tie-breaker at time limit",
			"",
			"Current score:",
			$"{status.Score}",
			topRival,
			topRivalScore
		];
	}

	public static float ComputeTurnScore(Player player, GameData gameData) {
		// A LAN guest isn't sent all of a rival's cities and territory, so
		// the host works it out.
		if (player.hostFacts != null) {
			return player.hostFacts.turnScore;
		}
		int scoredTiles = player.tileKnowledge.CountScoreTiles();

		int happyCitizens = 0;
		int contentCitizens = 0;
		int specialists = 0;
		foreach (City c in player.cities) {
			foreach (CityResident r in c.residents) {
				if (!r.citizenType.IsDefaultCitizen) {
					++specialists;
				} else if (r.mood == CityResident.Mood.Happy) {
					++happyCitizens;
				} else if (r.mood == CityResident.Mood.Content) {
					++contentCitizens;
				}
			}
		}

		// Each future tech counts as much as a content citizen: "1 FUTURE
		// TECH IS WORTH 1 CONTENT CITIZEN OR SPECIALIST", found by giving a
		// civ a future tech with a save editor, which scored (1/21) * 6 = 0.3
		// on Deity after 21 turns
		// (https://forums.civfanatics.com/threads/scoring-of-future-tech.31261/).
		int futureTechs = player.futureTechs;

		// TODO: gameData.gameDifficulty.ScoreMultiplier
		float difficultyFactor = GetDifficultyScoreFactor(gameData);

		float turnScore = (scoredTiles + (2 * happyCitizens) + contentCitizens + specialists + futureTechs);
		turnScore *= difficultyFactor;

		return turnScore;
	}

	// What winning before 2050 AD adds to the winner's score: "For
	// finishing before 2050 AD, the bonus is (2050 - FinishYear) *
	// Difficulty ... If you finish before 10AD, use the year as a negative
	// number in the calculation"
	// (https://civfanatics.com/civ3/strategy/game-mechanics/maximizing-your-score/),
	// with the same difficulty factor as the turn score. The finish year is
	// that of the turn the game was won. UNVERIFIED: a game counted in
	// months, weeks or days rather than years has no year to finish in, so
	// it gets no bonus.
	public static int EarlyWinBonus(GameData gameData) {
		TimeOptions time = gameData.timeOptions;
		if (time == null || time.baseUnit != TimeUnit.Years) {
			return 0;
		}
		int year = time.GetRawNumber(gameData.turn);
		return (int)(Math.Max(0, 2050 - year) * GetDifficultyScoreFactor(gameData));
	}

	// 1 for Chieftain, 2 for Warlord, 3 for Regent, etc.
	public static float GetDifficultyScoreFactor(GameData gameData) {
		int idx = 0;
		for (int i = 0; i < gameData.difficulties.Count; ++i) {
			if (gameData.difficulties[i] == gameData.gameDifficulty) {
				idx = i;
				break;
			}
		}
		return idx + 1;
	}
}

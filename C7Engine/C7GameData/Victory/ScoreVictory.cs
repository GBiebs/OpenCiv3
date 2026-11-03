using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine;

public class ScoreVictory : IVictory {

	public string Header() => "Score";

	public VictoryStatus Evaluate(Player player, GameData gameData) {
		float turnScore =  ComputeTurnScore(player, gameData);

		var history = gameData.history[player.id.ToString()];
		var lastTurn = history.LastOrDefault();
		var totalAccumulatedScore = lastTurn?.Score ?? 0;

		return new VictoryStatus {
			Player = player,
			Score = totalAccumulatedScore,
			TurnScore = turnScore
		};
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

	// A turn's score in its parts, each times the difficulty: happy
	// citizens count twice, content citizens and specialists once, each
	// tile of territory once, and future techs.
	public record struct ScoreParts(float happy, float contentAndSpecialists, float territory, float futureTech) {
		public float Total => happy + contentAndSpecialists + territory + futureTech;
	}

	public static float ComputeTurnScore(Player player, GameData gameData) {
		return ComputeTurnScoreParts(player, gameData).Total;
	}

	public static ScoreParts ComputeTurnScoreParts(Player player, GameData gameData) {
		List<Tile> scoredTiles = player.tileKnowledge.ScoreTiles();

		List<CityResident> citizens = player.cities.SelectMany(c => c.residents.Where(r => r.citizenType.IsDefaultCitizen)).ToList();

		int happyCitizens = citizens.Count(c => c.mood == CityResident.Mood.Happy);
		int contentCitizens = citizens.Count(c => c.mood == CityResident.Mood.Content);
		int specialists = player.cities.Sum(c => c.residents.Count(r => !r.citizenType.IsDefaultCitizen));

		int futureTechs = 0; // TODO: future techs

		// TODO: gameData.gameDifficulty.ScoreMultiplier
		float difficultyFactor = GetDifficultyScoreFactor(gameData);

		return new ScoreParts(2 * happyCitizens * difficultyFactor, (contentCitizens + specialists) * difficultyFactor,
			scoredTiles.Count * difficultyFactor, futureTechs * difficultyFactor);
	}

	// 1 for Chieftain, 2 for Warlord, 3 for Regent, etc.
	private static float GetDifficultyScoreFactor(GameData gameData) {
		var diffs = gameData.difficulties.Select((diff, idx) => new { Diff = diff, Idx = idx});
		var idx = diffs.FirstOrDefault(d => d.Diff == gameData.gameDifficulty)?.Idx ?? 0;
		return idx + 1;
	}
}

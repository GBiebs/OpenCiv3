using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using Serilog;

namespace C7Engine.AI {
	// How the AI picks its government. Civ3's AI weighs how much each
	// government would earn and save it (corruption, the trade bonus and the
	// tile penalty, unit support), its military police and, at war, its war
	// weariness; its favorite and shunned governments matter only a little
	// (CivFanatics, "How the AI cycles through government types"). Here each
	// candidate is tried for real: the empire's useful shields, commerce and
	// food surplus under it, less unit support, less a luxury point for each
	// unhappy citizen beyond the happy ones (what keeping order would cost).
	public static class GovernmentAI {
		private static ILogger log = Log.ForContext(typeof(GovernmentAI));

		// How much better a government has to look to be worth the anarchy,
		// and at least by how much; a religious civ's anarchy is short.
		public const double RevolutionMargin = .15;
		public const double ReligiousRevolutionMargin = .05;
		public const double MinimumGain = 2;

		// How often each AI reviews its government, in turns.
		public const int TurnsBetweenReviews = 10;

		public const double FavoriteGovernmentFactor = 1.1;
		public const double ShunnedGovernmentFactor = .75;

		// A government whose war weariness would overthrow it at once.
		private const double Unthinkable = -1e9;

		// Once a turn: picks a government when anarchy ends, and every few
		// turns starts a revolution if another government looks clearly better.
		public static void ManageGovernment(Player player, GameData gameData) {
			if (player.cities.Count == 0) {
				return;
			}
			if (player.government.transitionType) {
				if (gameData.turn >= player.inAnarchyUntilTurn && BestGovernment(player, gameData, out _) is Government chosen) {
					player.government = chosen;
					player.DoCorruptionCalculations(gameData);
					log.Information($"{player} leaves anarchy for {chosen.name}");
				}
				return;
			}
			if ((gameData.turn + gameData.players.IndexOf(player)) % TurnsBetweenReviews != 0) {
				return;
			}
			Government best = BestGovernment(player, gameData, out double bestScore);
			if (best == null || best == player.government) {
				return;
			}
			double current = Score(player, player.government, gameData);
			if (WorthARevolution(player, current, bestScore)) {
				log.Information($"{player} starts a revolution for {best.name} ({bestScore:F0} against {current:F0} under {player.government.name})");
				player.StartRevolution(gameData);
			}
		}

		// Whether a government scoring `best` beats the current one's `current`
		// by enough to sit through anarchy.
		public static bool WorthARevolution(Player player, double current, double best) {
			double margin = player.civilization.traits.Contains(Civilization.Trait.Religious) ? ReligiousRevolutionMargin : RevolutionMargin;
			return best - current >= Math.Max(margin * Math.Abs(current), MinimumGain);
		}

		// The best government the player can choose, and its score.
		public static Government BestGovernment(Player player, GameData gameData, out double bestScore) {
			Government best = null;
			bestScore = double.MinValue;
			Government actual = player.government;
			try {
				foreach (Government candidate in player.GetAvailableGovernments(gameData)) {
					double score = ScoreAs(player, candidate, gameData);
					if (best == null || score > bestScore) {
						best = candidate;
						bestScore = score;
					}
				}
			} finally {
				Restore(player, actual, gameData);
			}
			return best;
		}

		// How good `government` would be for the player now (see the class).
		public static double Score(Player player, Government government, GameData gameData) {
			Government actual = player.government;
			try {
				return ScoreAs(player, government, gameData);
			} finally {
				Restore(player, actual, gameData);
			}
		}

		private static double ScoreAs(Player player, Government government, GameData gameData) {
			player.government = government;
			if (WarWeariness.WouldOverthrowGovernment(gameData, player)) {
				return Unthinkable;
			}
			player.DoCorruptionCalculations(gameData);
			double score = 0;
			foreach (City city in player.cities) {
				city.RecalculateCitizenMoods(gameData);
				score += city.CurrentProductionYield().useful;
				CommerceBreakdown commerce = city.CurrentCommerceYield(respectCivilDisorder: false);
				score += commerce.taxes + commerce.beakers + commerce.happiness;
				score += Math.Max(0, city.FoodGrowthPerTurn());
				List<CityResident> laborers = city.residents.Where(r => r.citizenType.IsDefaultCitizen && !r.resisting).ToList();
				int unhappy = laborers.Count(r => r.mood == CityResident.Mood.Unhappy);
				int happy = laborers.Count(r => r.mood == CityResident.Mood.Happy);
				score -= Math.Max(0, unhappy - happy);
			}
			score -= player.TotalUnitsAllowedUnitsAndSupportCost().Item3;
			if (score > 0) {
				if (government.name == player.civilization.favoriteGovernment) {
					score *= FavoriteGovernmentFactor;
				} else if (government.name == player.civilization.shunnedGovernment) {
					score *= ShunnedGovernmentFactor;
				}
			}
			return score;
		}

		// Puts the player's real government back, with the corruption and
		// moods that go with it.
		private static void Restore(Player player, Government actual, GameData gameData) {
			player.government = actual;
			player.DoCorruptionCalculations(gameData);
			foreach (City city in player.cities) {
				city.RecalculateCitizenMoods(gameData);
			}
		}
	}
}

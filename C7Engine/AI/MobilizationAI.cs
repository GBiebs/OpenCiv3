using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine.AI {
	// When the AI mobilizes (see Player.mobilized). Civ3's rule isn't
	// documented, and a mobilized civ can't return to normalcy before it signs
	// a peace treaty, so the AI mobilizes only for a war it isn't winning
	// (PeaceAI.StandingIn) that has lasted MobilizeAfterWarTurns, and returns
	// to normalcy as soon as it may once it's at war with no one. AI tuning.
	public static class MobilizationAI {
		internal const int MobilizeAfterWarTurns = 5;

		public static void Manage(Player us, GameData gameData) {
			if (us.CanMobilize() && WantsWarFooting(us, gameData)) {
				us.SetMobilized(true, gameData);
			} else if (us.CanDemobilize() && !Enemies(us, gameData).Any()) {
				us.SetMobilized(false, gameData);
			}
		}

		internal static bool WantsWarFooting(Player us, GameData gameData) {
			return Enemies(us, gameData).Any(them => us.playerRelationships[them.id].warStartTurn is int start
				&& gameData.turn - start >= MobilizeAfterWarTurns
				&& PeaceAI.StandingIn(us, them) != PeaceAI.Standing.Winning);
		}

		// The civs we're at war with (not the barbarians).
		private static IEnumerable<Player> Enemies(Player us, GameData gameData) {
			return gameData.players.Where(p => p != us && !p.isBarbarians && !p.defeated && !us.IsAtPeaceWith(p));
		}
	}
}

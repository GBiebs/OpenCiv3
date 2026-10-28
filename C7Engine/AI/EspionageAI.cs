using System.Linq;
using C7GameData;

namespace C7Engine.AI {
	// The AI's spies (see Espionage). Every EveryTurns turns per civ, against
	// a civ it fights or is Annoyed with, it plants a spy, or else steals an
	// advance it lacks (carefully); and it tries to expose a spy another civ
	// has planted with it. It spends at most a GoldShare'th of its gold on a
	// mission. All AI tuning.
	public static class EspionageAI {
		internal const int EveryTurns = 10;
		internal const int GoldShare = 4;

		public static void Act(Player us, GameData gameData) {
			foreach (Player them in gameData.players.Where(p => p != us && !p.isBarbarians && !p.defeated).ToList()) {
				if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)
					|| relationship.lastEspionageTurn is int last && gameData.turn - last < EveryTurns) {
					continue;
				}
				string mission = Choose(gameData, us, them);
				if (mission == null) {
					continue;
				}
				relationship.lastEspionageTurn = gameData.turn;
				Espionage.Attempt(gameData, us, them, mission, null, Espionage.Risk.Carefully, GameData.rng);
			}
		}

		internal static string Choose(GameData gameData, Player us, Player them) {
			bool Affordable(string key) => Espionage.CanAttempt(us, them, gameData.rules.Mission(key), out _)
				&& Espionage.Cost(gameData, us, them, gameData.rules.Mission(key), null, Espionage.Risk.Carefully) <= us.gold / GoldShare;
			if (Espionage.HasSpy(them, us) && Affordable(EspionageMission.ExposeEnemySpy)) {
				return EspionageMission.ExposeEnemySpy;
			}
			bool hostile = PlayerRelationship.AtWar(us, them) || Attitude.Of(gameData, us, them) >= Attitude.Level.Annoyed;
			if (!hostile) {
				return null;
			}
			if (Affordable(EspionageMission.PlantSpy)) {
				return EspionageMission.PlantSpy;
			}
			bool somethingToSteal = them.knownTechs.Any(t => !us.knownTechs.Contains(t));
			return somethingToSteal && Affordable(EspionageMission.StealTechnology) ? EspionageMission.StealTechnology : null;
		}
	}
}

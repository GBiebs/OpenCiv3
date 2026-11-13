using System.Linq;
using C7Engine;
using C7GameData;

namespace C7Engine.AI {
	// The AI's espionage: it establishes embassies with every civ it has met
	// (again after a war, which closes them; see PlayerRelationship.DeclareWar)
	// and plants spies once it has the Intelligence Agency, and now and then
	// considers a mission as Civ3's AI_ConsiderEspionage does. It always keeps
	// a reserve of gold.
	public static class EspionageAI {
		private const int GoldReserve = 100;
		// The other missions are only tried when the treasury is comfortably
		// above the reserve.
		private const int TheftReserve = 300;

		// Civ3's AI_ConsiderEspionage (Perez's decompile of Civ3 Conquests,
		// doc/perez-findings/Civ3_Findings.md, Round 2d "Espionage";
		// authoritative per the owner's 2026-11-12 ruling): "each turn it
		// rolls Rand(range), range = 200000, 20000, 2000, 200, 20 for
		// relationship level 0..4 (halved if there is an embassy flag or a
		// positive counter). Results 0-1 steal tech, 2 steal map, 3 steal
		// plans, 4-5 propaganda, 6-7 sabotage, 8-9 expose mole; a mission is
		// only tried if enabled in the ESPN mask and the target government is
		// not immune to that mission type."
		private static readonly int[] RollRanges = [200000, 20000, 2000, 200, 20];

		// The decompile doesn't decode which relationship value maps to which
		// level. HEURISTIC: the AI's attitude, best to worst: Gracious 0,
		// Polite 1, Cautious 2, Annoyed 3, Furious 4, so the most hostile
		// civ is spied on most. The "positive counter" isn't known, so only
		// the embassy halves the range.
		public static int RelationshipLevel(AttitudeLevel attitude) => (int)attitude;

		public static int RollRange(AttitudeLevel attitude, bool hasEmbassy) {
			int range = RollRanges[RelationshipLevel(attitude)];
			return hasEmbassy ? range / 2 : range;
		}

		// The mission a roll of AI_ConsiderEspionage picks, or null for none.
		public static EspionageMission? MissionForRoll(int roll) {
			return roll switch {
				0 or 1 => EspionageMission.StealTechnology,
				2 => EspionageMission.StealWorldMap,
				3 => EspionageMission.StealPlans,
				4 or 5 => EspionageMission.Propaganda,
				6 or 7 => EspionageMission.SabotageProduction,
				8 or 9 => EspionageMission.ExposeEnemySpy,
				_ => null,
			};
		}

		public static void PlayTurn(Player player, GameData gameData) {
			if (player.isHuman || player.IsIndependent || player.cities.Count == 0) {
				return;
			}
			foreach (Player other in gameData.players.ToList()) {
				if (other == player || !PlayerRelationship.TryGetRelationship(player, other, out PlayerRelationship pr)) {
					continue;
				}
				if (!pr.hasEmbassy) {
					TryMission(gameData, player, EspionageMission.EstablishEmbassy, other, null, GoldReserve);
				} else if (!pr.hasSpy) {
					TryMission(gameData, player, EspionageMission.PlantSpy, other, null, GoldReserve);
				}
				ConsiderEspionage(gameData, player, other, pr);
			}
		}

		private static void ConsiderEspionage(GameData gameData, Player player, Player other, PlayerRelationship pr) {
			if (other.IsIndependent || other.defeated || Attitudes.Level(gameData, player, other) is not AttitudeLevel attitude) {
				return;
			}
			if (MissionForRoll(GameData.rng.Next(RollRange(attitude, pr.hasEmbassy))) is not EspionageMission mission) {
				return;
			}
			// HEURISTIC: exposing a spy is only worth it when they have one
			// among us.
			if (mission == EspionageMission.ExposeEnemySpy && !Espionage.HasSpy(other, player)) {
				return;
			}
			City city = null;
			if (Espionage.TargetsCity(mission)) {
				city = TargetCity(gameData, player, other, mission);
				if (city == null) {
					return;
				}
			}
			// Espionage.Unavailable leaves out the missions that aren't
			// allowed, including propaganda against an immune government.
			TryMission(gameData, player, mission, other, city, TheftReserve);
		}

		// HEURISTIC, as the decompile doesn't say: the known city with the
		// most shields to sabotage, or the one most likely to defect to
		// propaganda.
		private static City TargetCity(GameData gameData, Player player, Player other, EspionageMission mission) {
			var known = other.cities.Where(c => player.tileKnowledge.isTileKnown(c.location));
			return mission == EspionageMission.SabotageProduction
				? known.OrderByDescending(c => c.shieldsStored).FirstOrDefault()
				: known.OrderByDescending(c => Espionage.PropagandaFlipChance(gameData, player, c)).FirstOrDefault();
		}

		private static void TryMission(GameData gameData, Player player, EspionageMission mission, Player target, City city, int reserve) {
			if (Espionage.Unavailable(gameData, player, mission, target, city) != null) {
				return;
			}
			if (player.gold - Espionage.Cost(gameData, player, mission, target, city) < reserve) {
				return;
			}
			Espionage.Perform(gameData, player, mission, target, city);
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using Serilog;

namespace C7Engine.AI {
	// As in Civ3, an AI that is militarily stronger than a human it is at
	// peace with demands tribute from them: a little gold, or failing that a
	// tech. "They demand when your army is weaker than theirs, and they will
	// usually stop once your army becomes similar in size", and "usually the
	// civs demanding something are only going to demand a little bit of gold,
	// or in the worse case scenario a tech"
	// (https://forums.civfanatics.com/threads/ai-tribute.165970/). Paying
	// pleases the AI, "-1 You pay tribute to the AI", and refusing means war:
	// "On regent and higher difficulty levels, the AI always declares war if
	// you don't meet demands" (UNVERIFIED beyond that thread). Below Regent
	// the AI may back down instead, though it holds it against you: "+1 If
	// you call the AI's bluff when he demands tribute (and he doesn't declare
	// war of course)"
	// (https://civfanatics.com/civ3/strategy/game-mechanics/ai-attitude-study/;
	// see Attitudes).
	//
	// When it asks follows the stock sequence of demand gates, as C3X
	// re-creates it: "This is the same sequence of demand gates used by
	// Leader::impl_begin_turn for demands against human players"
	// (try_ai_demand_from_other_ai in C3X's injected_code.c,
	// https://github.com/maxpetul/C3X/blob/master/injected_code.c; compendium
	// 06, section 6.9). See WouldDemandFrom and RollsToDemand. Stock AIs
	// demand only from humans: "As far as I know AIs won't demand things
	// from each other" (Flintlock, CFC 703785), and C3X's option "Allows AI
	// players to demand tribute from each other" is a mod's addition.
	//
	// It goes alongside Civ3's own war plotting (see StockWarPlotting).
	public static class TributeDemands {
		private static readonly ILogger log = Log.ForContext(typeof(TributeDemands));

		// The fewest turns between demands from one AI to one human. Civ3's
		// interval "varies with the leader, ranging from about 20 turns to
		// around 100 turns" (same thread). Raised from 20 at the owner's
		// request (2026-10-12): the AIs were far too demanding. The stock
		// game's own cooldown is shorter: after a demand the AI sets its
		// diplomacy cooldown for that civ to "0x20" (32) turns (C3X's
		// injected_code.c, above); the owner's 40 is kept.
		public const int MIN_TURNS_BETWEEN_DEMANDS = 40;

		// HEURISTIC (owner's request, 2026-10-12): the fewest turns between
		// demands made of one human by any AI, so that several strong
		// neighbours don't take turns asking.
		public const int MIN_TURNS_BETWEEN_ANY_DEMANDS = 15;

		// HEURISTIC (owner's request, 2026-10-12): the gold demanded is this
		// share of the human's treasury, rounded down to a multiple of 10.
		public const int GOLD_SHARE_PERCENT = 20;

		// HEURISTIC (owner's request, 2026-10-12): a grace period before an
		// AI makes demands, so it doesn't bully a human it has only just
		// met, or anyone in the first turns of the game, before there's
		// much of an army to judge by. No demands before this turn of the
		// game...
		public const int FIRST_DEMAND_TURN = 25;

		// ...nor within this many turns of the two civs first meeting.
		public const int TURNS_AFTER_CONTACT_BEFORE_DEMANDS = 10;
		public const int MIN_GOLD_DEMANDED = 20;

		// Playing by email, the computer players make their demands of a human
		// at the start of the human's turn, onlyWith them (see
		// PlayByEmail.HearFromAIs).
		public static async Task MakeDemands(Player us, GameData gameData, Player onlyWith = null) {
			if (us.isHuman || us.IsIndependent || us.defeated || us.cities.Count == 0) {
				return;
			}
			foreach (Player them in gameData.players.ToList()) {
				if (onlyWith != null && them != onlyWith) {
					continue;
				}
				// A LAN guest who has left can't answer, so isn't asked, nor
				// is a human away playing by email.
				if (!WouldDemandFrom(us, them, gameData) || !EngineStorage.IsPlayerReachable(them.id)) {
					continue;
				}
				// The stock game first finds something to ask for, then rolls.
				TradeOffer tribute = ChooseTribute(us, them, gameData);
				if (tribute == null || !RollsToDemand(us, them, gameData)) {
					continue;
				}
				await DemandFrom(us, them, tribute, gameData);
			}
		}

		// Whether us is in a position to demand tribute from them now.
		public static bool WouldDemandFrom(Player us, Player them, GameData gameData) {
			if (them == us || !them.isHuman || them.defeated || !them.isIncludedInGame) {
				return false;
			}
			if (!us.playerRelationships.TryGetValue(them.id, out PlayerRelationship ourView) || ourView.AtWar()
				|| gameData.AreInLockedPeace(us, them)) {
				return false;
			}
			if (ourView.lastTributeDemandTurn >= 0
				&& gameData.turn - ourView.lastTributeDemandTurn < MIN_TURNS_BETWEEN_DEMANDS) {
				return false;
			}
			if (InGracePeriod(ourView, gameData)) {
				return false;
			}
			if (DemandedOfRecently(them, gameData)) {
				return false;
			}
			return PowerPlaceAllows(gameData, us, them);
		}

		// A civ's place in the power ranking, 1 for the strongest: "32 -
		// this->power_rank", where "power_rank" is 31 for the strongest (see
		// StockWarPlotting.PowerRank).
		public static int PowerPlace(GameData gameData, Player player) {
			return 32 - StockWarPlotting.PowerRank(gameData, player);
		}

		// The stock power gate: "if (32 - this->power_rank > 2 * (32 -
		// recipient->power_rank))" the AI does not demand, so it asks only of
		// a civ whose place is at least half its own. This replaces the
		// HEURISTIC that the AI be stronger by the military advisor's
		// measure and sure to win the war a refusal would bring (which
		// DemandFrom still checks before it declares war).
		public static bool PowerPlaceAllows(GameData gameData, Player us, Player them) {
			return PowerPlace(gameData, us) <= 2 * PowerPlace(gameData, them);
		}

		private static bool OneIn(int n) => n <= 1 || GameData.rng.Next(n) == 0;

		// The stock per-turn rolls once there is something to demand, each
		// of which must pass (C3X's injected_code.c, above). They replace a
		// HEURISTIC 5% chance a turn.
		// - "rand_int (32 - this->power_rank) != 0" stops it: one in our
		//   power place, so the strongest civ always passes.
		// - Attitude: a roll of minus the attitude score (at least 1) must
		//   come up 0. A higher score is worse (see Attitudes), so at
		//   Cautious or worse it always passes, and the friendlier the AI
		//   the rarer the demand.
		// - "rand_int (2 - aggression) != 0" stops it, with the effective
		//   aggression level -2..2 (StockWarPlotting.EffectiveAggressionLevel):
		//   always at +1 or +2, one in 2 at 0, one in 3 at -1 and one in 4
		//   at -2.
		// The stock game also makes no demand when the AI has taken more war
		// damage from the target than it dealt ("war_damage_memory"). The
		// engine doesn't track war damage (see Attitudes), so that gate is
		// left out.
		public static bool RollsToDemand(Player us, Player them, GameData gameData) {
			if (!OneIn(PowerPlace(gameData, us))) {
				return false;
			}
			int attitude = Attitudes.Points(gameData, us, them) ?? 0;
			if (!OneIn(Math.Max(1, -attitude))) {
				return false;
			}
			return OneIn(2 - StockWarPlotting.EffectiveAggressionLevel(gameData, us));
		}

		// Whether it's too early for a demand: early in the game, or soon
		// after the two civs met.
		public static bool InGracePeriod(PlayerRelationship ourView, GameData gameData) {
			if (gameData.turn < FIRST_DEMAND_TURN) {
				return true;
			}
			return ourView.firstContactTurn >= 0
				&& gameData.turn - ourView.firstContactTurn < TURNS_AFTER_CONTACT_BEFORE_DEMANDS;
		}

		// Whether any AI has made a demand of them within the last
		// MIN_TURNS_BETWEEN_ANY_DEMANDS turns.
		public static bool DemandedOfRecently(Player them, GameData gameData) {
			foreach (Player other in gameData.players) {
				if (other != them && other.playerRelationships.TryGetValue(them.id, out PlayerRelationship view)
					&& view.lastTributeDemandTurn >= 0
					&& gameData.turn - view.lastTributeDemandTurn < MIN_TURNS_BETWEEN_ANY_DEMANDS) {
					return true;
				}
			}
			return false;
		}

		// What us demands of them: some of their gold or the cheapest tech
		// they could give us, picked at random when both are possible, or
		// null if neither.
		public static TradeOffer ChooseTribute(Player us, Player them, GameData gameData) {
			return ChooseTribute(us, them, gameData, preferGold: GameData.rng.Next(2) == 0);
		}

		// The same, with the choice of gold or tech made: what's preferred
		// if they have it, otherwise the other.
		public static TradeOffer ChooseTribute(Player us, Player them, GameData gameData, bool preferGold) {
			TradeOffer gold = GoldTribute(them);
			TradeOffer tech = TechTribute(us, them, gameData);
			return preferGold ? gold ?? tech : tech ?? gold;
		}

		private static TradeOffer GoldTribute(Player them) {
			int gold = them.gold * GOLD_SHARE_PERCENT / 100 / 10 * 10;
			return gold >= MIN_GOLD_DEMANDED ? new TradeOffer() { gold = gold } : null;
		}

		private static TradeOffer TechTribute(Player us, Player them, GameData gameData) {
			Tech tech = them.GetTechsTradableTo(us, gameData.techs)
				.OrderBy(t => gameData.TechCostFor(t, us))
				.FirstOrDefault();
			return tech != null ? new TradeOffer() { techs = new List<Tech> { tech } } : null;
		}

		// Demands the tribute, and declares war if it is refused.
		internal static async Task DemandFrom(Player us, Player them, TradeOffer tribute, GameData gameData) {
			us.playerRelationships[them.id].lastTributeDemandTurn = gameData.turn;
			log.Information("{Us} demands {Tribute} in tribute from {Them}", us, tribute.ToString(), them);

			bool pay = await AskForTribute(us, them, tribute);
			// Paying changes nothing if the tribute can no longer be given,
			// such as gold spent since: that counts as refusing.
			PlayerRelationship ourView = us.playerRelationships[them.id];
			if (pay && us.ExecuteDeal(gameData, them, tribute, new TradeOffer(), tribute: true)) {
				log.Information("{Them} paid {Us} tribute", them, us);
				ourView.lastTributePaidTurn = gameData.turn;
				return;
			}
			// Things may have changed since the demand (pacts signed, a war
			// begun elsewhere): no war we wouldn't win. Nor any war while we
			// have a mutual protection pact with them: the stock game rolls
			// to declare after a refused demand only without one
			// ("Relation_Treaties ... & 4", C3X's injected_code.c, above).
			if (BacksDown(us, them, gameData) || !PeaceAI.WouldWinWarWith(gameData, us, them)
				|| Pacts.HasDeal(us, them, DealSubType.MutualProtectionPact)) {
				log.Information("{Them} called {Us}'s bluff and refused to pay tribute", them, us);
				++ourView.tributeBluffsCalled;
				return;
			}
			log.Information("{Them} refused to pay {Us} tribute; {Us} declares war", them, us);
			// They were warned, so it's no sneak attack.
			us.DeclareWarOn(them, gameData.turn, afterWarning: true);
			MsgWarDeclaration.Announce(us, them);
		}

		// Whether an AI whose demand for tribute was refused backs down
		// rather than declare war. HEURISTIC: below Regent (see
		// Attitudes.DifficultyBand), where Civ3's AI doesn't always go to war
		// over it, it backs down unless it is already annoyed or furious
		// with the civ.
		public static bool BacksDown(Player us, Player them, GameData gameData) {
			if (Attitudes.DifficultyBand(gameData) != 0) {
				return false;
			}
			AttitudeLevel? attitude = Attitudes.Level(gameData, us, them);
			return attitude == null || attitude < AttitudeLevel.Annoyed;
		}

		private static async Task<bool> AskForTribute(Player us, Player them, TradeOffer tribute) {
			// The human answering takes the UI. In a hotseat game they may
			// not be the player at the screen.
			if (EngineStorage.uiFollowsActivePlayer) {
				EngineStorage.uiControllerID = them.id;
			}
			EngineStorage.diplomacyPlayerID = them.id;
			EngineStorage.diplomacyAIPlayerID = us.id;
			EngineStorage.tributeDemandAnswer = null;
			new MsgShowTributeDemand(us, them, tribute.gold ?? 0, tribute.techs.FirstOrDefault()).send();
			await EngineStorage.WaitForDiplomacyCompleted(them.id);
			EngineStorage.diplomacyPlayerID = null;
			EngineStorage.diplomacyAIPlayerID = null;

			// Closing the screen without answering is taken as paying, so a
			// stray key press can't start a war.
			bool pay = EngineStorage.tributeDemandAnswer ?? true;
			EngineStorage.tributeDemandAnswer = null;
			return pay;
		}
	}
}

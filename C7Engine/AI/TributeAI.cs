using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using Serilog;

namespace C7Engine.AI {
	// Demands and tribute (Civ3's diplomacy.txt AIDEMANDTRIBUTE: "Give us ...
	// and we will not crush you"). The AI demands an advance it lacks (the
	// costliest), or else half the treasury, from a civ it has met and is at
	// peace with, isn't fond of (Cautious or worse) and has at least
	// OutmatchFactor times the military of, at most every DemandEveryTurns
	// turns. An AI pays when the one demanding has GiveInFactor times its
	// military. A refusal means war if the AI is Annoyed with the civ and no
	// peace treaty with it still runs; otherwise the AI backs down (Civ3's
	// "bluff is called"). Attitude follows CivFanatics' study: demanding
	// costs +2 with the civ asked (+1 if it was Polite or better), paying
	// earns -1 with the one demanding, calling its bluff +1. All AI numbers
	// are AI tuning; the attitude points are rules.
	public static class TributeAI {
		private static ILogger log = Log.ForContext(typeof(TributeAI));

		internal const double OutmatchFactor = 2;
		internal const double GiveInFactor = 3;
		internal const int DemandEveryTurns = 20;
		internal const int MinimumGold = 50;

		// What `us` would demand of `them` now, or null.
		public static TradeOffer Demand(GameData gameData, Player us, Player them) {
			if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship) || relationship.AtWar()
				|| gameData.AreInLockedPeace(us, them) || Attitude.Of(gameData, us, them) <= Attitude.Level.Polite
				|| us.CalculateMilitaryStrength() < OutmatchFactor * them.CalculateMilitaryStrength()) {
				return null;
			}
			Tech tech = gameData.techs.Where(t => them.knownTechs.Contains(t.id) && !us.knownTechs.Contains(t.id))
				.MaxBy(t => gameData.TechCostFor(t, us));
			if (tech != null) {
				return new TradeOffer { techs = [tech] };
			}
			return them.gold / 2 >= MinimumGold ? new TradeOffer { gold = them.gold / 2 } : null;
		}

		// Whether `us` gives in to `demander`.
		public static bool WouldPay(Player us, Player demander) {
			return demander.CalculateMilitaryStrength() >= GiveInFactor * us.CalculateMilitaryStrength();
		}

		// `demander` asks `victim` for `demand`.
		public static void OnDemand(GameData gameData, Player demander, Player victim) {
			AttitudeRules rules = gameData.rules.attitude;
			bool friendly = Attitude.Of(gameData, victim, demander) <= Attitude.Level.Polite;
			Attitude.Remember(victim, demander, friendly ? rules.demandedTributeWhileFriendly : rules.demandedTribute);
		}

		// `victim` pays.
		// (Not a gift: Civ3 counts tribute on its own.)
		public static void Pay(GameData gameData, Player demander, Player victim, TradeOffer demand) {
			if (demand.gold is int gold) {
				victim.gold -= gold;
				demander.gold += gold;
			}
			demander.AcquireTechs(gameData, demand.techs);
			Trading.Give(gameData, victim, demander, demand);
			Attitude.Remember(demander, victim, gameData.rules.attitude.paidUsTribute);
			log.Information($"{victim} pays {demander} tribute: {demand.ToString()}");
		}

		// `victim` refuses: war, or the demander backs down.
		public static void Refuse(GameData gameData, Player demander, Player victim) {
			bool peaceTreaty = demander.playerRelationships[victim.id].multiTurnDeals
				.Any(d => d.dealSubType == DealSubType.Peace && d.TurnsRemaining(gameData.turn) > 0);
			if (!demander.isHuman && Attitude.Of(gameData, demander, victim) >= Attitude.Level.Annoyed && !peaceTreaty) {
				log.Information($"{victim} refuses {demander} tribute: war");
				demander.DeclareWarOn(victim, gameData.turn);
				new MsgWarDeclarationNotification(demander, victim).send();
			} else {
				log.Information($"{victim} calls {demander}'s bluff");
				Attitude.Remember(demander, victim, gameData.rules.attitude.calledOurBluff);
			}
		}

		// On `us`'s turn: demand tribute of civs we can.
		public static async Task MakeDemands(Player us, GameData gameData) {
			foreach (Player them in gameData.players.Where(p => p != us && !p.isBarbarians && !p.defeated).ToList()) {
				if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)
					|| relationship.lastTributeDemandTurn is int last && gameData.turn - last < DemandEveryTurns) {
					continue;
				}
				TradeOffer demand = Demand(gameData, us, them);
				if (demand == null) {
					continue;
				}
				relationship.lastTributeDemandTurn = gameData.turn;
				OnDemand(gameData, us, them);
				log.Information($"{us} demands {demand.ToString()} of {them}");
				if (them.isHuman) {
					new MsgTributeDemanded(us, them, demand).send();
					await EngineStorage.WaitForMessageToEngine<MsgTributeAnswer>();
				} else if (WouldPay(them, us)) {
					Pay(gameData, us, them, demand);
				} else {
					Refuse(gameData, us, them);
				}
			}
		}
	}
}

using System;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using Serilog;

namespace C7Engine.AI {
	// When the AI makes peace. Civ3's rule isn't documented; players report
	// that the AI talks peace once it isn't winning (a first war "after 4-5
	// turns" if you're winning or it's a draw), turns it down while it is
	// ("Not likely! ... is winning!", Civ3's diplomacy.txt), asks to be paid to
	// end a war it leads ("Give us ... and we'll call it a tie"), and that
	// beating its units and taking its cities brings it to the table. So the
	// AI judges each war by what each side has destroyed or taken (WarRecord),
	// and by military strength when the battles haven't decided it:
	// - losing, it wants peace, and pays for it: up to half its gold, and no
	//   more than it has lost;
	// - even, it wants peace once the war has lasted MinimumWarTurns;
	// - winning, it wants what it has gained (at least MinimumTribute), but
	//   settles for peace alone once its people tire of the war (war weariness
	//   level WearyLevel) or the war has lasted LongWarTurns.
	// It offers peace at most every OfferEveryTurns turns, when the other civ
	// will talk to it. All AI tuning.
	public static class PeaceAI {
		private static ILogger log = Log.ForContext(typeof(PeaceAI));

		internal const int MinimumWarTurns = 8;
		internal const int LongWarTurns = 40;
		internal const int OfferEveryTurns = 5;
		internal const int MinimumTribute = 50;
		internal const int WearyLevel = 2;
		// A lead in the battles is at least this many shields, and a fifth of
		// all that both sides have lost.
		internal const int SmallestLead = 20;

		public enum Standing {
			Losing,
			Even,
			Winning,
		}

		// How the war with `them` is going for `us`.
		public static Standing StandingIn(Player us, Player them) {
			PlayerRelationship relationship = us.playerRelationships[them.id];
			int balance = relationship.warGains - relationship.warLosses;
			int lead = Math.Max(SmallestLead, (relationship.warGains + relationship.warLosses) / 5);
			double ratio = us.CalculateMilitaryStrength() / them.CalculateMilitaryStrength();
			if (balance > lead) {
				return ratio < .5 ? Standing.Even : Standing.Winning;
			}
			if (-balance > lead) {
				return ratio > 2 ? Standing.Even : Standing.Losing;
			}
			if (ratio >= 2) {
				return Standing.Winning;
			}
			return ratio <= .5 ? Standing.Losing : Standing.Even;
		}

		// What peace with `them` is worth to `us`, in gold: what we'd pay for it
		// (positive), or want for it (negative); null if we won't make peace
		// now at any price.
		public static int? PeaceValue(GameData gameData, Player us, Player them) {
			if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship) || !relationship.AtWar()
				|| gameData.AreInLockedWar(us, them)) {
				return null;
			}
			int turns = relationship.warStartTurn is int start ? gameData.turn - start : LongWarTurns;
			bool weary = WarWeariness.Level(relationship.warWearinessPoints, WarWeariness.RulesFor(us)) >= WearyLevel;
			switch (StandingIn(us, them)) {
				case Standing.Losing:
					return Math.Min(us.gold / 2, Math.Max(0, relationship.warLosses - relationship.warGains));
				case Standing.Even:
					return turns >= MinimumWarTurns || weary ? 0 : null;
				default:
					if (weary || turns >= LongWarTurns) {
						return 0;
					}
					return -Math.Max(MinimumTribute, relationship.warGains - relationship.warLosses);
			}
		}

		// On `us`'s turn: offer peace to each civ we're at war with that we'd
		// make peace with and that will talk to us. An AI decides at once; a
		// human sees the offer.
		public static async Task OfferPeace(Player us, GameData gameData) {
			foreach (Player them in gameData.players.Where(p => p != us && !p.isBarbarians && !p.defeated).ToList()) {
				if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship) || !relationship.AtWar()) {
					continue;
				}
				if (relationship.lastPeaceOfferTurn is int last && gameData.turn - last < OfferEveryTurns) {
					continue;
				}
				if (!them.WillAcceptCommunicationFrom(us, gameData.turn)) {
					continue;
				}
				int? value = PeaceValue(gameData, us, them);
				// No peace yet, or a price they can't pay.
				if (value is not int price || -price > them.gold) {
					continue;
				}

				TradeOffer weGive = new() { partOfPeaceTreaty = true, gold = price > 0 ? price : null };
				TradeOffer weWant = new() { partOfPeaceTreaty = true, gold = price < 0 ? -price : null };
				relationship.lastPeaceOfferTurn = gameData.turn;
				log.Information($"{us} offers {them} peace ({price} gold)");
				if (them.isHuman) {
					new MsgShowTradeOffer(us, them, weWant, weGive).send();
					await EngineStorage.WaitForMessageToEngine<MsgDiplomacyCompleted>();
				} else if (them.WouldAcceptDealFrom(gameData, us, weGive, weWant)) {
					us.ExecuteDeal(gameData, them, weWant, weGive);
				}
			}
		}
	}
}

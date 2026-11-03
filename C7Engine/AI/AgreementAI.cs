using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using Serilog;

namespace C7Engine.AI {
	// The AI and diplomatic agreements (see Agreements). It signs with civs
	// it doesn't dislike, and won't trust a civ that has broken one before
	// (Civ3's diplomacy.txt: "the last time we had a right of passage treaty
	// with you, you sort of attacked us"; "you have failed to fully honor
	// diplomatic agreements with us in the past"):
	// - right of passage with civs it is at least Cautious about, that have
	//   never attacked anyone under one;
	// - a mutual protection pact with civs it is at least Polite to, that
	//   have never broken an alliance;
	// - an alliance against a civ it is already fighting, or, to join a war,
	//   against a civ it is Annoyed with, if it's no weaker than three
	//   quarters of that civ and has no peace treaty with it still running;
	// - an embargo against a civ it is Annoyed with.
	// On its turn it asks a civ to join its war (unless it's winning it
	// anyway: in the soak, AIs kept drawing more and more civs into wars
	// nobody fought), then a Gracious civ for a
	// pact, then a friend for an embargo against a civ it is Furious with; at
	// most once every ProposeEveryTurns turns per civ. All AI tuning.
	public static class AgreementAI {
		private static ILogger log = Log.ForContext(typeof(AgreementAI));

		internal const int ProposeEveryTurns = 10;
		internal const double JoinWarStrength = .75;

		public static bool WouldSign(GameData gameData, Player us, Player them, Agreement agreement) {
			Attitude.Level towardThem = Attitude.Of(gameData, us, them);
			Player target = agreement.against == null ? null : gameData.GetPlayer(agreement.against);
			switch (agreement.type) {
				case DealSubType.RightOfPassage:
					return towardThem <= Attitude.Level.Cautious && !BrokeRightOfPassage(gameData, them);
				case DealSubType.MutualProtectionPact:
					return towardThem <= Attitude.Level.Polite && !BrokeAlliance(gameData, them);
				case DealSubType.MilitaryAlliance:
					if (towardThem > Attitude.Level.Cautious || BrokeAlliance(gameData, them) || target == null) {
						return false;
					}
					if (PlayerRelationship.AtWar(us, target)) {
						return true;
					}
					return Attitude.Of(gameData, us, target) >= Attitude.Level.Annoyed
						&& !PeaceTreatyRunning(gameData, us, target)
						&& us.CalculateMilitaryStrength() >= JoinWarStrength * target.CalculateMilitaryStrength();
				case DealSubType.TradeEmbargo:
					return towardThem <= Attitude.Level.Cautious && target != null
						&& Attitude.Of(gameData, us, target) >= Attitude.Level.Annoyed;
				default:
					return false;
			}
		}

		private static bool BrokeRightOfPassage(GameData gameData, Player them) {
			return gameData.players.Any(p => p.playerRelationships.TryGetValue(them.id, out PlayerRelationship r) && r.warDeclarationWithRoPActiveCount > 0);
		}

		private static bool BrokeAlliance(GameData gameData, Player them) {
			return gameData.players.Any(p => p.playerRelationships.TryGetValue(them.id, out PlayerRelationship r) && r.brokenAlliances > 0);
		}

		private static bool PeaceTreatyRunning(GameData gameData, Player us, Player them) {
			return PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)
				&& relationship.multiTurnDeals.Any(d => d.dealSubType == DealSubType.Peace && d.TurnsRemaining(gameData.turn) > 0);
		}

		// What `us` would like to sign with `them` now, if anything.
		internal static Agreement Wanted(GameData gameData, Player us, Player them) {
			List<Agreement> available = Agreements.Available(gameData, us, them);
			bool Fits(Agreement a) => WouldSign(gameData, us, them, a) && (them.isHuman || WouldSign(gameData, them, us, a));
			// Help against an enemy of ours they aren't fighting yet.
			Agreement alliance = available.FirstOrDefault(a => a.type == DealSubType.MilitaryAlliance
				&& PlayerRelationship.AtWar(us, gameData.GetPlayer(a.against)) && !PlayerRelationship.AtWar(them, gameData.GetPlayer(a.against))
				&& PeaceAI.StandingIn(us, gameData.GetPlayer(a.against)) != PeaceAI.Standing.Winning && Fits(a));
			if (alliance != null) {
				return alliance;
			}
			if (Attitude.Of(gameData, us, them) == Attitude.Level.Gracious) {
				Agreement pact = available.FirstOrDefault(a => a.type == DealSubType.MutualProtectionPact && Fits(a));
				if (pact != null) {
					return pact;
				}
			}
			return available.FirstOrDefault(a => a.type == DealSubType.TradeEmbargo
				&& Attitude.Of(gameData, us, gameData.GetPlayer(a.against)) == Attitude.Level.Furious && Fits(a));
		}

		// On `us`'s turn: propose an agreement to each civ we have an embassy
		// with, if we want one.
		public static async Task ProposeAgreements(Player us, GameData gameData) {
			foreach (Player them in gameData.players.Where(p => p != us && !p.isBarbarians && !p.defeated).ToList()) {
				if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship) || relationship.AtWar()
					|| !relationship.hasEmbassy) {
					continue;
				}
				if (relationship.lastAgreementOfferTurn is int last && gameData.turn - last < ProposeEveryTurns) {
					continue;
				}
				Agreement wanted = Wanted(gameData, us, them);
				if (wanted == null) {
					continue;
				}
				relationship.lastAgreementOfferTurn = gameData.turn;
				TradeOffer weGive = new() { agreements = [wanted] };
				TradeOffer weWant = new() { agreements = [wanted] };
				log.Information($"{us} proposes {wanted.Describe(gameData)} to {them}");
				if (them.isHuman) {
					TurnHandling.HandControlTo(them);
					new MsgShowTradeOffer(us, them, weWant, weGive).send();
					await EngineStorage.WaitForMessageToEngine<MsgDiplomacyCompleted>();
				} else if (them.WouldAcceptDealFrom(gameData, us, weGive, weWant)) {
					us.ExecuteDeal(gameData, them, weWant, weGive);
				}
			}
		}
	}
}

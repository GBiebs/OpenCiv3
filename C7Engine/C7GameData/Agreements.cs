using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// A diplomatic agreement two civs can sign (the Conquests Civilopedia's
	// "Diplomatic Agreements"): right of passage, a mutual protection pact, a
	// military alliance against a third civ, or a trade embargo against one.
	public class Agreement {
		public DealSubType type;
		// The third civ of an alliance or embargo.
		public ID against;

		public bool SameAs(Agreement other) {
			return type == other.type && against == other.against;
		}

		public string Describe(GameData gameData) {
			string them = against == null ? "" : gameData.GetPlayer(against)?.civilization.noun;
			return type switch {
				DealSubType.RightOfPassage => "Right of Passage",
				DealSubType.MutualProtectionPact => "Mutual Protection Pact",
				DealSubType.MilitaryAlliance => $"Military Alliance vs. the {them}",
				DealSubType.TradeEmbargo => $"Trade Embargo vs. the {them}",
				_ => type.ToString(),
			};
		}
	}

	// Signing and keeping diplomatic agreements. Two civs at peace, with an
	// embassy, can sign them once both know the advance that enables each
	// (Civ3's TECH flags: Map Making right of passage, Writing alliances,
	// Nationalism pacts and embargoes). They run the rules' deal duration (20
	// turns), and:
	// - right of passage lets each one's units through the other's land;
	// - when a civ declares war on one side of a mutual protection pact, the
	//   other declares war on it too;
	// - a military alliance is against a civ one of them is at war with; the
	//   other joins the war, and making peace with that civ before the
	//   alliance ends breaks it;
	// - a trade embargo: neither trades with that civ, and their per-turn
	//   deals with it end.
	public static class Agreements {
		private static ILogger log = Log.ForContext(typeof(Agreements));

		private static readonly Dictionary<DealSubType, SaveTech.Flag> enablingTech = new() {
			{ DealSubType.RightOfPassage, SaveTech.Flag.EnablesRightOfPassage },
			{ DealSubType.MutualProtectionPact, SaveTech.Flag.EnablesMutualProtection },
			{ DealSubType.MilitaryAlliance, SaveTech.Flag.EnablesMilitaryAlliances },
			{ DealSubType.TradeEmbargo, SaveTech.Flag.EnablesTradeEmbargoes },
		};

		public static bool Has(Player a, Player b, DealSubType type, ID against = null) {
			return PlayerRelationship.TryGetRelationship(a, b, out PlayerRelationship relationship)
				&& relationship.multiTurnDeals.Any(d => d.dealSubType == type && (against == null || d.againstPlayer == against));
		}

		// The agreements `a` and `b` could sign now.
		public static List<Agreement> Available(GameData gameData, Player a, Player b) {
			List<Agreement> result = new();
			if (!PlayerRelationship.TryGetRelationship(a, b, out PlayerRelationship relationship) || relationship.AtWar()
				|| !relationship.hasEmbassy) {
				return result;
			}
			bool BothKnow(DealSubType type) => a.HasTechWithFlag(enablingTech[type]) && b.HasTechWithFlag(enablingTech[type]);
			foreach (DealSubType type in new[] { DealSubType.RightOfPassage, DealSubType.MutualProtectionPact }) {
				if (BothKnow(type) && !Has(a, b, type)) {
					result.Add(new Agreement { type = type });
				}
			}
			IEnumerable<Player> thirds = gameData.players.Where(p => p != a && p != b && !p.isBarbarians && !p.defeated
				&& a.playerRelationships.ContainsKey(p.id) && b.playerRelationships.ContainsKey(p.id));
			foreach (Player third in thirds) {
				if (BothKnow(DealSubType.MilitaryAlliance) && !Has(a, b, DealSubType.MilitaryAlliance, third.id)
					&& (PlayerRelationship.AtWar(a, third) || PlayerRelationship.AtWar(b, third))
					&& !gameData.AreInLockedPeace(a, third) && !gameData.AreInLockedPeace(b, third)) {
					result.Add(new Agreement { type = DealSubType.MilitaryAlliance, against = third.id });
				}
				if (BothKnow(DealSubType.TradeEmbargo) && !Has(a, b, DealSubType.TradeEmbargo, third.id)) {
					result.Add(new Agreement { type = DealSubType.TradeEmbargo, against = third.id });
				}
			}
			return result;
		}

		public static void Sign(GameData gameData, Player a, Player b, Agreement agreement) {
			DealType dealType = agreement.type switch {
				DealSubType.MilitaryAlliance => DealType.Alliance,
				DealSubType.TradeEmbargo => DealType.Embargo,
				_ => DealType.DiplomaticAgreement,
			};
			PlayerRelationship.RegisterMultiTurnDeal(a, b, new MultiTurnDeal(dealType, agreement.type, DealDetails.Exchange,
				0, null, gameData.rules.DefaultDealDuration, gameData.turn, agreement.against));
			log.Information($"{a} and {b} sign {agreement.Describe(gameData)}");

			Player third = agreement.against == null ? null : gameData.GetPlayer(agreement.against);
			if (agreement.type == DealSubType.MilitaryAlliance) {
				// The one not yet at war with them joins it.
				foreach (Player ally in new[] { a, b }.Where(p => !PlayerRelationship.AtWar(p, third))) {
					ally.DeclareWarOn(third, gameData.turn);
					new MsgWarDeclarationNotification(ally, third).send();
				}
			} else if (agreement.type == DealSubType.TradeEmbargo) {
				foreach (Player signer in new[] { a, b }) {
					EndPerTurnDeals(signer, third);
				}
			}
			if (agreement.type == DealSubType.RightOfPassage) {
				gameData.InvalidateCachedTradeNetwork();
			}
		}

		private static void EndPerTurnDeals(Player a, Player b) {
			foreach ((Player us, Player them) in new[] { (a, b), (b, a) }) {
				if (PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)) {
					relationship.multiTurnDeals.RemoveAll(d => d.dealSubType is DealSubType.GoldPerTurn
						or DealSubType.ResourcePerTurn or DealSubType.LuxuryPerTurn);
				}
			}
		}

		// Whether `a` and `b` mustn't trade: one of them has signed an
		// embargo against the other.
		public static bool Embargoed(Player a, Player b) {
			bool Against(Player signer, Player target) => signer.playerRelationships.Values
				.Any(r => r.multiTurnDeals.Any(d => d.dealSubType == DealSubType.TradeEmbargo && d.againstPlayer == target.id));
			return Against(a, b) || Against(b, a);
		}

		// After `aggressor` declared war on `defender`: the defender's partners
		// in mutual protection pacts declare war on the aggressor.
		public static void OnWarDeclared(GameData gameData, Player aggressor, Player defender) {
			if (gameData == null) {
				return;
			}
			List<Player> partners = gameData.players
				.Where(p => p != aggressor && p != defender && !p.defeated && Has(p, defender, DealSubType.MutualProtectionPact)
					&& PlayerRelationship.TryGetRelationship(p, aggressor, out PlayerRelationship withAggressor) && !withAggressor.AtWar()
					&& !gameData.AreInLockedPeace(p, aggressor))
				.ToList();
			foreach (Player partner in partners) {
				// It may have been drawn in already by another pact.
				if (PlayerRelationship.AtWar(partner, aggressor)) {
					continue;
				}
				log.Information($"{partner} honors its pact with {defender} and declares war on {aggressor}");
				partner.DeclareWarOn(aggressor, gameData.turn);
				new MsgWarDeclarationNotification(partner, aggressor).send();
			}
		}

		// Before `a` makes peace with `b`: its alliances against `b` are
		// broken (the allies remember), and end.
		public static void OnPeace(GameData gameData, Player a, Player b) {
			foreach ((Player us, Player them) in new[] { (a, b), (b, a) }) {
				foreach (Player ally in gameData.players.Where(p => Has(us, p, DealSubType.MilitaryAlliance, them.id)).ToList()) {
					AttitudeRules rules = gameData.rules.attitude;
					PlayerRelationship withUs = ally.playerRelationships[us.id];
					withUs.attitudePenalty += rules.brokeAlliance;
					withUs.brokenAlliances++;
					foreach (Player other in gameData.players.Where(p => p != us && p != ally && !p.isBarbarians && !p.defeated
						&& PlayerRelationship.TryGetRelationship(p, us, out _)
						&& PlayerRelationship.TryGetRelationship(p, ally, out PlayerRelationship withAlly) && !withAlly.AtWar())) {
						Attitude.Remember(other, us, rules.brokeAllianceWithFriend);
					}
					us.playerRelationships[ally.id].multiTurnDeals.RemoveAll(d => d.dealSubType == DealSubType.MilitaryAlliance && d.againstPlayer == them.id);
					withUs.multiTurnDeals.RemoveAll(d => d.dealSubType == DealSubType.MilitaryAlliance && d.againstPlayer == them.id);
					log.Information($"{us} breaks its alliance with {ally} against {them}");
				}
			}
		}
	}
}

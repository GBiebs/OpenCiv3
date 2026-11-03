using System;
using System.Collections.Generic;
using System.Linq;

namespace C7GameData {
	// What the foreign advisor shows: how the civs we know stand with each
	// other (the lines between their portraits), our treaties and per-turn
	// deals with one of them, and the best of its units we know about.
	public static class ForeignAffairs {
		// The kinds of line between two civs' portraits.
		public enum Relation {
			War,
			Peace,
			MutualProtection,
			Alliance,
			RightOfPassage,
			Embargo,
		}

		// How `a` and `b` stand (nothing if they haven't met): at war or at
		// peace, the agreements between them, and an embargo either has
		// signed (with anyone) against the other.
		public static List<Relation> Between(Player a, Player b) {
			List<Relation> result = new();
			if (!PlayerRelationship.TryGetRelationship(a, b, out _)) {
				return result;
			}
			result.Add(PlayerRelationship.AtWar(a, b) ? Relation.War : Relation.Peace);
			if (Agreements.Has(a, b, DealSubType.MutualProtectionPact)) {
				result.Add(Relation.MutualProtection);
			}
			if (Agreements.Has(a, b, DealSubType.MilitaryAlliance)) {
				result.Add(Relation.Alliance);
			}
			if (Agreements.Has(a, b, DealSubType.RightOfPassage)) {
				result.Add(Relation.RightOfPassage);
			}
			if (Agreements.Embargoed(a, b)) {
				result.Add(Relation.Embargo);
			}
			return result;
		}

		// War or peace with `them`, our agreements with them, then their wars
		// and agreements with the other civs we know.
		public static List<string> Treaties(GameData gameData, Player us, Player them) {
			List<string> lines = new();
			if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)) {
				return lines;
			}
			lines.Add(PlayerRelationship.AtWar(us, them) ? $"We are at war with the {them.civilization.noun}." : $"We are at peace with the {them.civilization.noun}.");
			foreach (MultiTurnDeal deal in relationship.multiTurnDeals.Where(d => d.dealSubType is DealSubType.MutualProtectionPact
				or DealSubType.MilitaryAlliance or DealSubType.RightOfPassage or DealSubType.TradeEmbargo)) {
				string agreement = new Agreement { type = deal.dealSubType, against = deal.againstPlayer }.Describe(gameData);
				lines.Add(deal.turnEndDeal > 0 ? $"{agreement} ({TurnsLeft(deal, gameData.turn)})" : agreement);
			}
			foreach (Player other in gameData.GetKnownRivals(us).Where(p => p != them)) {
				string with = $"the {other.civilization.noun}";
				List<Relation> relations = Between(them, other);
				if (relations.Contains(Relation.War)) {
					lines.Add($"They are at war with {with}.");
				}
				if (relations.Contains(Relation.MutualProtection)) {
					lines.Add($"They have a Mutual Protection Pact with {with}.");
				}
				if (relations.Contains(Relation.Alliance)) {
					lines.Add($"They are allied with {with}.");
				}
				if (relations.Contains(Relation.RightOfPassage)) {
					lines.Add($"They have a Right of Passage with {with}.");
				}
			}
			return lines;
		}

		// Our gold and resource deals with `them` that run for turns.
		public static List<string> Deals(GameData gameData, Player us, Player them) {
			List<string> lines = new();
			if (!PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)) {
				return lines;
			}
			foreach (MultiTurnDeal deal in relationship.multiTurnDeals) {
				string what = deal.dealSubType switch {
					DealSubType.GoldPerTurn => $"{deal.goldPerTurn} gold a turn",
					DealSubType.ResourcePerTurn or DealSubType.LuxuryPerTurn
						=> gameData.Resources.Find(r => r.Key == deal.resourcePerTurn)?.Name ?? deal.resourcePerTurn,
					_ => null,
				};
				if (what == null) {
					continue;
				}
				string who = deal.dealDetails == DealDetails.Outbound ? "We give them" : "They give us";
				lines.Add($"{who} {what} ({TurnsLeft(deal, gameData.turn)})");
			}
			return lines;
		}

		// The strongest of `them`'s units we know about: those we can see, or
		// all of them while our spy is planted there.
		public static UnitPrototype BestKnownUnit(Player us, Player them) {
			bool spy = Espionage.HasSpy(us, them);
			return them.units
				.Where(u => spy || (us.tileKnowledge.isActiveTile(u.location) && Naval.IsVisibleTo(u, us)))
				.Select(u => u.unitType)
				.Where(t => t.attack > 0 || t.defense > 0)
				.OrderByDescending(t => Math.Max(t.attack, t.defense))
				.ThenByDescending(t => t.shieldCost)
				.FirstOrDefault();
		}

		private static string TurnsLeft(MultiTurnDeal deal, int turn) => $"{AdvisorAdvice.Turns(deal.TurnsRemaining(turn))} left";
	}
}

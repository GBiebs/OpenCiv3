using System;
using System.Collections.Generic;
using System.Linq;

namespace C7GameData {
	// The kinds of ties the foreign advisor draws between two civs, as the
	// toggles under Civ3's Foreign Advisor name them (labels.txt,
	// #FOREIGN_ADVISOR: "War", "Peace", "Mutual Protection", "Right of
	// Passage", "Alliance", "Embargo").
	public enum ForeignTieKind {
		War,
		Peace,
		MutualProtection,
		RightOfPassage,
		Alliance,
		Embargo,
	}

	// The three faces of a leader's small portrait, in the order of the rows
	// of Civ3's Art/Advisors/<civ>_all.pcx.
	public enum LeaderFace {
		Pleased,
		Neutral,
		Angry,
	}

	// A leader's small portrait with a given face, for the texture config
	// (leader_heads), which takes the row of the art to show from it.
	public record LeaderPortrait(Player player, LeaderFace face) {
		public int row => (int)face;
	}

	// What two civs that have met have between them.
	public record ForeignTie(Player first, Player second, bool atWar, bool mutualProtection, bool rightOfPassage,
			bool lockedAlliance, List<Player> allianceAgainst, List<Player> embargoAgainst) {
		// The kinds of ties there are between the two, war or peace first.
		public List<ForeignTieKind> Kinds() {
			List<ForeignTieKind> kinds = [atWar ? ForeignTieKind.War : ForeignTieKind.Peace];
			if (mutualProtection) {
				kinds.Add(ForeignTieKind.MutualProtection);
			}
			if (rightOfPassage) {
				kinds.Add(ForeignTieKind.RightOfPassage);
			}
			if (lockedAlliance || allianceAgainst.Count > 0) {
				kinds.Add(ForeignTieKind.Alliance);
			}
			if (embargoAgainst.Count > 0) {
				kinds.Add(ForeignTieKind.Embargo);
			}
			return kinds;
		}

		public Player Other(Player civ) => civ == first ? second : first;
	}

	// What the foreign advisor reports, worked out from the game data. Its
	// wording is Civ3's: the words of labels.txt (#FOREIGN_ADVISOR) and the
	// advisor's lines in script.txt (#FA_...).
	public static class ForeignReport {
		// The civs the advisor shows: the viewer, then the civs it has met.
		public static List<Player> Civs(GameData gameData, Player viewer) =>
			[viewer, .. gameData.GetKnownRivals(viewer)];

		// What two civs have between them, or null if they haven't met.
		// p1.playerRelationships[p2] holds the deals of both sides (see
		// PlayerRelationship), so one side's view is enough.
		public static ForeignTie Tie(GameData gameData, Player first, Player second) {
			if (!PlayerRelationship.TryGetRelationship(first, second, out PlayerRelationship relationship)) {
				return null;
			}
			List<MultiTurnDeal> deals = relationship.multiTurnDeals.Where(d => d != null).ToList();
			List<Player> Against(DealSubType type) => deals
				.Where(d => d.dealSubType == type && d.againstPlayer != null)
				.Select(d => gameData.GetPlayer(d.againstPlayer))
				.Where(p => p != null)
				.Distinct()
				.ToList();
			return new ForeignTie(first, second,
				atWar: relationship.AtWar(),
				mutualProtection: deals.Any(d => d.dealSubType == DealSubType.MutualProtectionPact),
				rightOfPassage: deals.Any(d => d.dealSubType == DealSubType.RightOfPassage),
				lockedAlliance: gameData.AreInLockedPeace(first, second),
				allianceAgainst: Against(DealSubType.MilitaryAlliance),
				embargoAgainst: Against(DealSubType.TradeEmbargo));
		}

		// Whether the viewer knows what the two civs have between them. Its
		// own ties it always knows; those between two rivals need contact
		// with both ("Relationship lines are no longer displayed on the
		// Foreign Advisor unless the player has contact with both
		// civilizations", vanilla README.txt, 1.17f) and an embassy: with an
		// embassy "you can see what civilizations they are at war/peace with
		// on the F4 Foreign Advisor screen"
		// (https://civfanatics.com/civ3/faq/). UNVERIFIED: that an embassy
		// with either of the two is enough.
		public static bool KnowsTie(Player viewer, Player first, Player second) {
			return first == viewer || second == viewer || HasEmbassyWith(viewer, first) || HasEmbassyWith(viewer, second);
		}

		// The ties between every two of the civs the viewer knows that have
		// met each other, and that the viewer knows of (see KnowsTie).
		public static List<ForeignTie> Ties(GameData gameData, Player viewer) {
			List<Player> civs = Civs(gameData, viewer);
			List<ForeignTie> ties = [];
			for (int i = 0; i < civs.Count; i++) {
				for (int j = i + 1; j < civs.Count; j++) {
					if (!KnowsTie(viewer, civs[i], civs[j])) {
						continue;
					}
					ForeignTie tie = Tie(gameData, civs[i], civs[j]);
					if (tie != null) {
						ties.Add(tie);
					}
				}
			}
			return ties;
		}

		// How Civ3's Foreign Advisor names an attitude (labels.txt:
		// "gracious", "polite", "cautious", "annoyed", "furious").
		public static string AttitudeWord(AttitudeLevel level) => level.ToString().ToLowerInvariant();

		// The civ's attitude toward the viewer, or null for the viewer itself
		// and for human players, who have no attitude the game works out.
		public static AttitudeLevel? AttitudeTowardViewer(GameData gameData, Player viewer, Player civ) {
			if (civ == viewer || civ.isHuman) {
				return null;
			}
			return Attitudes.Level(gameData, civ, viewer);
		}

		// The face a leader's portrait on the Foreign Advisor wears: Civ3
		// draws our own leader pleased and every other leader by its
		// attitude toward us, pleased while gracious or polite, neutral while
		// cautious and angry while annoyed or furious (Civ3 FUN_00538830: the
		// switch on FUN_004428b0's attitude, jump table at 0x5390c8, picks
		// the row of the leader's <civ>_all.pcx, whose rows are those three
		// faces). War doesn't change the face, only the attitude does. A
		// civ with no attitude we work out (a human player) looks neutral.
		public static LeaderFace FaceToward(AttitudeLevel? attitude, bool isViewer) {
			if (isViewer) {
				return LeaderFace.Pleased;
			}
			return attitude switch {
				AttitudeLevel.Gracious or AttitudeLevel.Polite => LeaderFace.Pleased,
				AttitudeLevel.Annoyed or AttitudeLevel.Furious => LeaderFace.Angry,
				_ => LeaderFace.Neutral,
			};
		}

		public static LeaderFace FaceToward(GameData gameData, Player viewer, Player civ) =>
			FaceToward(AttitudeTowardViewer(gameData, viewer, civ), civ == viewer);

		// The civ's name as the advisor uses it after "the", as in "the Romans".
		public static string CivNoun(Player civ) => civ?.civilization?.noun ?? civ?.civilization?.name ?? "";

		private static string CivAdjective(Player civ) =>
			civ?.civilization?.adjective is string adjective && adjective.Length > 0 ? adjective : CivNoun(civ);

		// script.txt, #FA_HAPPINESS_1: "The $CIVNOUN0 are $MOOD1 toward us."
		public static string AttitudeLine(GameData gameData, Player viewer, Player civ) {
			AttitudeLevel? level = AttitudeTowardViewer(gameData, viewer, civ);
			return level == null ? null : $"The {CivNoun(civ)} are {AttitudeWord(level.Value)} toward us.";
		}

		// The Treaties tab: the civ's government, then its ties to each civ
		// the viewer knows, in the words of labels.txt (#FOREIGN_ADVISOR:
		// "We", "They", "are", "at peace", "at war", "With", "have a",
		// "against the", "Mutual Protection Pact", "Military Alliance",
		// "Trade embargo", "Right of Passage", "Locked Alliance", "with us").
		// UNVERIFIED: the order Civ3 puts these words in.
		public static List<string> Treaties(GameData gameData, Player viewer, Player civ) {
			List<string> lines = [];
			if (civ.government?.name is string government && government.Length > 0) {
				lines.Add($"Government: {government}");
			}
			string subject = civ == viewer ? "We" : "They";
			foreach (Player other in Civs(gameData, viewer)) {
				if (other == civ || !KnowsTie(viewer, civ, other)) {
					continue;
				}
				ForeignTie tie = Tie(gameData, civ, other);
				if (tie == null) {
					continue;
				}
				string with = other == viewer ? "with us" : $"with the {CivNoun(other)}";
				lines.Add($"{subject} are {(tie.atWar ? "at war" : "at peace")} {with}");
				if (tie.mutualProtection) {
					lines.Add($"{subject} have a Mutual Protection Pact {with}");
				}
				if (tie.rightOfPassage) {
					lines.Add($"{subject} have a Right of Passage {with}");
				}
				if (tie.lockedAlliance) {
					lines.Add($"{subject} have a Locked Alliance {with}");
				}
				foreach (Player target in tie.allianceAgainst) {
					lines.Add($"{subject} have a Military Alliance {with} against the {CivNoun(target)}");
				}
				foreach (Player target in tie.embargoAgainst) {
					lines.Add($"{subject} have a Trade embargo {with} against the {CivNoun(target)}");
				}
			}
			return lines;
		}

		// The Trades tab's potential trades with another civ, in the words of
		// script.txt (#FA_IMPORT_GOOD, #FA_NO_SURPLUS, #FA_EXPORT_GOOD,
		// #FA_WE_NO_SURPLUS) or, when they can't trade, of the trade
		// advisor's labels ("We are at war with the", "We have an embargo
		// against the", "has a trade embargo against us").
		public static List<string> PotentialTrades(GameData gameData, Player viewer, Player civ) {
			TradeStatus status = TradeReport.StatusWith(viewer, civ);
			List<Resource> theirs = [];
			List<Resource> ours = [];
			if (status != TradeStatus.CannotTradeWith) {
				theirs = TradeReport.ExcessResources(gameData, civ, viewer).Select(r => r.resource).ToList();
				ours = TradeReport.ExcessResources(gameData, viewer, civ).Select(r => r.resource).ToList();
			}
			bool atWar = PlayerRelationship.AtWar(viewer, civ);
			return PotentialTrades(civ, atWar, TradeReport.HasEmbargoAgainst(viewer, civ),
				TradeReport.HasEmbargoAgainst(civ, viewer), theirs, ours);
		}

		// The same, given what keeps the two from trading and the resources
		// each has to spare.
		public static List<string> PotentialTrades(Player civ, bool atWar, bool weEmbargoThem, bool theyEmbargoUs,
				List<Resource> theirSurplus, List<Resource> ourSurplus) {
			string noun = CivNoun(civ);
			if (atWar) {
				return [$"We are at war with the {noun}."];
			}
			if (weEmbargoThem) {
				return [$"We have an embargo against the {noun}."];
			}
			if (theyEmbargoUs) {
				return [$"The {noun} has a trade embargo against us."];
			}
			return [
				theirSurplus.Count > 0
					? $"We could trade with the {noun} if you like, sire. They have some fine {Names(theirSurplus)} to trade."
					: $"Unfortunately, the {noun} do not have any surplus resources to trade with us.",
				ourSurplus.Count > 0
					? $"The {noun} might go for some of our {Names(ourSurplus)}."
					: $"We have no surplus goods to trade to the {noun}.",
			];
		}

		private static string Names(List<Resource> resources) {
			List<string> names = resources.Select(r => r.Name).ToList();
			return names.Count == 1 ? names[0] : string.Join(", ", names[..^1]) + " and " + names[^1];
		}

		// The best unit type the viewer knows the civ has: with an embassy,
		// the strongest of all its fighting units; without one, none.
		// UNVERIFIED: that Civ3 needs an embassy for it, and that "best" is
		// the strongest in attack or defence, then in both.
		public static UnitPrototype BestKnownUnit(Player viewer, Player civ) {
			if (!HasEmbassyWith(viewer, civ)) {
				return null;
			}
			return civ.units
				.Select(u => u.unitType)
				.Where(t => t != null && (t.attack > 0 || t.defense > 0))
				.OrderByDescending(t => Math.Max(t.attack, t.defense))
				.ThenByDescending(t => t.attack + t.defense)
				.ThenByDescending(t => t.shieldCost)
				.FirstOrDefault();
		}

		public static bool HasEmbassyWith(Player viewer, Player civ) =>
			viewer.playerRelationships.TryGetValue(civ.id, out PlayerRelationship relationship) && relationship.hasEmbassy;

		// The Details tab: what the viewer knows of the civ's military and
		// culture, in the words of script.txt (#FA_BEST_UNIT,
		// #FA_NO_KNOWLEDGE, #FA_CULTURAL) and labels.txt ("We have:", "An
		// Embassy").
		public static List<string> Details(GameData gameData, Player viewer, Player civ) {
			List<string> lines = [];
			if (civ == viewer) {
				return lines;
			}
			if (HasEmbassyWith(viewer, civ)) {
				lines.Add("We have: An Embassy");
			}
			UnitPrototype best = BestKnownUnit(viewer, civ);
			lines.Add(best != null
				? $"The best unit the {CivNoun(civ)} have -- that we know of -- is the {best.name}."
				: $"Sorry, but we have no knowledge of the {CivAdjective(civ)} military.");
			string opinion = gameData.rules == null ? "" : CultureReport.OpinionOf(gameData.rules,
				CultureReport.TotalCulture(civ), CultureReport.TotalCulture(viewer));
			if (opinion.Length > 0) {
				lines.Add($"The {CivAdjective(civ)} people are {opinion} our culture.");
			}
			return lines;
		}
	}
}

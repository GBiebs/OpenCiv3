using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;

namespace C7GameData {
	// How a civ feels about another: Civ3's five attitudes, from a score where
	// good things are negative (CivFanatics' "AI Attitude Study", measured in
	// Civ3's debug mode): -11 and lower Gracious, -1 to -10 Polite, 0
	// Cautious, 1 to 10 Annoyed, 11 to 100 Furious. The score adds up:
	// - the observer's aggression, higher on harder difficulties;
	// - while they last: a shared culture group, the same government (more
	//   if it's the observer's favorite), the observer's shunned government,
	//   a recent trade, a culture lead, an embassy, right of passage, a mutual
	//   protection pact, a common enemy and an alliance against it, a resource
	//   trade, gifts (a point per 10 gold, up to 10, forgotten at 10 gold a
	//   turn), and a trade embargo against the observer;
	// - for good: war declarations, broken peace treaties and rights of
	//   passage (against the observer, or less against a civ it's at peace
	//   with), razed cities, and flipped cities taken or refused.
	// While the other civ has more military power, the good points count half
	// (right of passage in full). All the numbers are rules
	// (Rules.attitude).
	public static class Attitude {
		public enum Level {
			Gracious,
			Polite,
			Cautious,
			Annoyed,
			Furious,
		}

		public static Level LevelOf(int score, AttitudeRules rules) {
			if (score <= rules.graciousAtMost) {
				return Level.Gracious;
			}
			if (score <= rules.politeAtMost) {
				return Level.Polite;
			}
			if (score <= rules.cautiousAtMost) {
				return Level.Cautious;
			}
			if (score <= rules.annoyedAtMost) {
				return Level.Annoyed;
			}
			return Level.Furious;
		}

		public static Level Of(GameData gameData, Player observer, Player subject) {
			return LevelOf(Score(gameData, observer, subject), gameData.rules.attitude);
		}

		// How `observer` feels about `subject` (0, Cautious, if they haven't
		// met).
		public static int Score(GameData gameData, Player observer, Player subject) {
			if (!PlayerRelationship.TryGetRelationship(observer, subject, out PlayerRelationship relationship)) {
				return 0;
			}
			AttitudeRules rules = gameData.rules.attitude;
			int good = 0;
			int bad = 0;
			void Add(int points) {
				if (points < 0) {
					good += points;
				} else {
					bad += points;
				}
			}

			Add(Math.Clamp(observer.civilization.aggressionLevel + AggressionOffset(gameData), rules.aggressionMinimum, rules.aggressionMaximum));

			CultureGroup ourGroup = observer.civilization.cultureGroup;
			if (ourGroup != null && ourGroup.name == subject.civilization.cultureGroup?.name) {
				Add(rules.sameCultureGroup);
			}

			string ours = observer.government?.name;
			string theirs = subject.government?.name;
			if (ours != null && ours == theirs) {
				Add(rules.sameGovernment);
				if (ours == observer.civilization.favoriteGovernment) {
					Add(rules.favoriteGovernment);
				}
			} else if (theirs != null && theirs == observer.civilization.shunnedGovernment) {
				Add(rules.shunnedGovernment);
			}

			List<MultiTurnDeal> deals = relationship.multiTurnDeals;
			bool recentTrade = relationship.lastTradeTurn is int traded && gameData.turn - traded < rules.recentTradeTurns;
			if (recentTrade || deals.Any(d => d.dealSubType is DealSubType.GoldPerTurn or DealSubType.ResourcePerTurn
				or DealSubType.LuxuryPerTurn or DealSubType.TradeEmbargo)) {
				Add(rules.recentTrade);
			}

			int ourCulture = observer.TotalCulture();
			int theirCulture = subject.TotalCulture();
			if (theirCulture > ourCulture) {
				Add(-rules.cultureLead);
			} else if (ourCulture > theirCulture) {
				Add(rules.cultureLead);
			}

			if (relationship.hasEmbassy) {
				Add(rules.embassy);
			}
			int passage = deals.Any(d => d.dealSubType == DealSubType.RightOfPassage) ? rules.rightOfPassage : 0;
			if (deals.Any(d => d.dealSubType == DealSubType.MutualProtectionPact)) {
				Add(rules.mutualProtection);
			}
			List<Player> commonEnemies = gameData.players
				.Where(p => p != observer && p != subject && !p.isBarbarians
					&& PlayerRelationship.TryGetRelationship(observer, p, out PlayerRelationship r1) && r1.AtWar()
					&& PlayerRelationship.TryGetRelationship(subject, p, out PlayerRelationship r2) && r2.AtWar())
				.ToList();
			if (commonEnemies.Count > 0) {
				Add(rules.commonEnemy);
				if (deals.Any(d => d.dealSubType == DealSubType.MilitaryAlliance && commonEnemies.Any(e => e.id == d.againstPlayer))) {
					Add(rules.alliance);
				}
			}
			if (deals.Any(d => d.dealSubType is DealSubType.ResourcePerTurn or DealSubType.LuxuryPerTurn)) {
				Add(rules.resourceTrade);
			}
			if (subject.playerRelationships.Values.Any(r => r.multiTurnDeals.Any(d => d.dealSubType == DealSubType.TradeEmbargo && d.againstPlayer == observer.id))) {
				Add(rules.tradeEmbargoAgainstUs);
			}
			if (rules.goldPerGiftPoint > 0) {
				Add(-Math.Min(rules.maximumGiftPoints, relationship.giftGold / rules.goldPerGiftPoint));
			}

			if (subject.CalculateMilitaryStrength() > observer.CalculateMilitaryStrength()) {
				good = (int)Math.Floor(good * rules.powerLeadGoodPercent / 100.0);
			}
			return Math.Min(rules.maximum, good + passage + bad + relationship.attitudePenalty + relationship.borderAnger
				+ relationship.spyAnger);
		}

		private static int AggressionOffset(GameData gameData) {
			List<int> offsets = gameData.rules.attitude.aggressionOffsetByDifficulty;
			if (offsets.Count == 0) {
				return 0;
			}
			int difficulty = gameData.difficulties.FindIndex(d => d.Name == gameData.gameDifficulty?.Name);
			return offsets[Math.Clamp(difficulty, 0, offsets.Count - 1)];
		}

		// `observer` holds `points` against `subject` for good.
		public static void Remember(Player observer, Player subject, int points) {
			if (PlayerRelationship.TryGetRelationship(observer, subject, out PlayerRelationship relationship)) {
				relationship.attitudePenalty += points;
			}
		}

		// Civs other than these two that have met both and are at peace with
		// `victim`: they take it badly when `culprit` wrongs their friend.
		private static IEnumerable<Player> FriendsOf(GameData gameData, Player victim, Player culprit) {
			return gameData.players.Where(p => p != victim && p != culprit && !p.isBarbarians && !p.defeated
				&& PlayerRelationship.TryGetRelationship(p, culprit, out _)
				&& PlayerRelationship.TryGetRelationship(p, victim, out PlayerRelationship withVictim) && !withVictim.AtWar());
		}

		// Before `aggressor` declares war on `defender` (and its treaties with
		// it end): it broke any peace treaty still running, and any right of
		// passage (worse with units inside the defender's borders).
		public static void OnWarDeclared(GameData gameData, Player aggressor, Player defender, bool unitsInside) {
			if (gameData == null || !PlayerRelationship.TryGetRelationship(defender, aggressor, out PlayerRelationship relationship)) {
				return;
			}
			AttitudeRules rules = gameData.rules.attitude;
			bool peaceRunning = relationship.multiTurnDeals.Any(d => d.dealSubType == DealSubType.Peace && d.TurnsRemaining(gameData.turn) > 0);
			bool passage = relationship.multiTurnDeals.Any(d => d.dealSubType == DealSubType.RightOfPassage);
			int victim = rules.declaredWarOnUs;
			int friends = rules.declaredWarOnFriend;
			if (peaceRunning) {
				victim += rules.brokePeaceTreaty;
				friends += rules.brokePeaceTreatyWithFriend;
			}
			if (passage) {
				victim += unitsInside ? rules.brokeRightOfPassageWithUnitsInside : rules.brokeRightOfPassage;
				friends += unitsInside ? rules.brokeRightOfPassageWithUnitsInsideWithFriend : rules.brokeRightOfPassageWithFriend;
			}
			relationship.attitudePenalty += victim;
			foreach (Player friend in FriendsOf(gameData, defender, aggressor)) {
				Remember(friend, aggressor, friends);
			}

			// A deal still running is broken: that's the aggressor's reputation.
			if (relationship.multiTurnDeals.Any(d => d.dealSubType != DealSubType.Peace && d.TurnsRemaining(gameData.turn) > 0)) {
				relationship.brokenDeals++;
			}
		}

		// `razer` razed a city it took from `victim`.
		public static void OnCityRazed(GameData gameData, Player razer, Player victim) {
			if (victim == null) {
				return;
			}
			AttitudeRules rules = gameData.rules.attitude;
			List<Player> friends = FriendsOf(gameData, victim, razer).ToList();
			Remember(victim, razer, rules.razedOurCity);
			foreach (Player friend in friends) {
				Remember(friend, razer, rules.razedFriendsCity);
			}
		}

		// One of `from`'s cities flipped to `to`, who took it.
		public static void OnCityFlipped(GameData gameData, Player from, Player to) {
			Remember(from, to, gameData.rules.attitude.tookOurFlippedCity);
		}

		// `refuser` sent back a city that had flipped to it from `from`.
		public static void OnFlipRefused(GameData gameData, Player from, Player refuser) {
			AttitudeRules rules = gameData.rules.attitude;
			Remember(from, refuser, rules.refusedOurFlippedCity - rules.tookOurFlippedCity);
		}

		// After a deal: they've traded, and what one gave for nothing is a
		// gift.
		public static void OnDeal(GameData gameData, Player a, Player b, TradeOffer aGives, TradeOffer bGives) {
			if (!PlayerRelationship.TryGetRelationship(a, b, out PlayerRelationship aToB)
				|| !PlayerRelationship.TryGetRelationship(b, a, out PlayerRelationship bToA)) {
				return;
			}
			aToB.lastTradeTurn = gameData.turn;
			bToA.lastTradeTurn = gameData.turn;
			if (bGives.IsEmpty() && !aGives.IsEmpty()) {
				bToA.giftGold += aGives.GoldEquivalentFor(gameData, b, a);
			} else if (aGives.IsEmpty() && !bGives.IsEmpty()) {
				aToB.giftGold += bGives.GoldEquivalentFor(gameData, a, b);
			}
		}

		// Each turn, gifts are forgotten a little, and every so many turns a
		// point of anger at failed spy missions.
		public static void UpdateForNewTurn(GameData gameData) {
			AttitudeRules rules = gameData.rules.attitude;
			bool spyAngerFades = rules.spyAngerFadeTurns > 0 && gameData.turn % rules.spyAngerFadeTurns == 0;
			foreach (Player player in gameData.players) {
				foreach (PlayerRelationship relationship in player.playerRelationships.Values) {
					relationship.giftGold = Math.Max(0, relationship.giftGold - rules.giftGoldForgottenPerTurn);
					if (spyAngerFades) {
						relationship.spyAnger = Math.Max(0, relationship.spyAnger - 1);
					}
				}
			}
		}
	}
}

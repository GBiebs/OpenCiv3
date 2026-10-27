using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// Civ3's war weariness: every civ keeps a count of points against each
	// other civ (PlayerRelationship.warWearinessPoints). Fighting raises it and
	// peace wears it away. While at war, each 30 points is a level, and under a
	// government with war weariness (Republic and Feudalism low, Democracy
	// high) each level makes a share of every city's citizens unhappy; at
	// level 3 a Democracy falls. A civ another declares war on starts 30
	// points down, and while its count is below zero a quarter of each city's
	// citizens are happy instead, under any government. As CivFanatics
	// measured it:
	// https://civfanatics.com/civ3/strategy/game-mechanics/how-does-war-weariness-work/
	// and as the user's Civ3 saves confirm (a quarter of each city's citizens,
	// specialists included, rounded down, at 31-37 points under a Republic).
	// Civ3 charges the AI for the human's losses, a known bug; here every civ
	// is charged for its own, as the article describes for a human. The
	// numbers are rules (Rules.warWeariness); these are Civ3's.
	public static class WarWeariness {
		private static ILogger log = Log.ForContext(typeof(WarWeariness));

		private static readonly WarWearinessRules Civ3 = new();

		// The war weariness rules `player` plays by (Civ3's for a player made
		// without rules).
		public static WarWearinessRules RulesFor(Player player) {
			return player.rules?.warWeariness ?? Civ3;
		}

		// The level of a count of points: -1 below zero, 0 up to 30, 1 up to
		// 60, 2 up to 90, 3 up to 120, and 4 beyond (with Civ3's rules: at
		// most the last level the unhappiness tables have).
		public static int Level(int points, WarWearinessRules rules = null) {
			rules ??= Civ3;
			if (points < 0) {
				return -1;
			}
			int highest = Math.Max(0, Math.Max(rules.lowUnhappyPercent.Count, rules.highUnhappyPercent.Count) - 1);
			return Math.Min(highest, Math.Max(0, (points - 1) / Math.Max(1, rules.pointsPerLevel)));
		}

		// The percent of a city's citizens made unhappy at a level of war
		// weariness, by the government's kind. A high war weariness
		// government falls at level 3, so it never sees more than 100%.
		public static int UnhappyPercent(Government.WarWearinessType type, int level, WarWearinessRules rules = null) {
			rules ??= Civ3;
			List<int> percents = type switch {
				Government.WarWearinessType.Low => rules.lowUnhappyPercent,
				Government.WarWearinessType.High => rules.highUnhappyPercent,
				_ => null,
			};
			if (percents == null || percents.Count == 0 || level < 0) {
				return 0;
			}
			return percents[Math.Min(level, percents.Count - 1)];
		}

		// `player`'s relationships with the civs it's at war with, not counting
		// barbarians.
		private static IEnumerable<PlayerRelationship> Wars(GameData gameData, Player player) {
			foreach (Player other in gameData.players) {
				if (PlayerRelationship.TryGetRelationship(player, other, out PlayerRelationship relationship) && relationship.AtWar()) {
					yield return relationship;
				}
			}
		}

		// The number of citizens of `city` its owner's wars make unhappy and
		// happy: for each enemy, a share of the city's size by the level of
		// the owner's points against it, rounded down, then added up. Each
		// total is at most the city's size.
		public static (int unhappy, int happy) CitizensAffected(GameData gameData, City city) {
			Player owner = city.owner;
			WarWearinessRules rules = RulesFor(owner);
			int size = city.residents.Count;
			int unhappy = 0;
			int happy = 0;
			foreach (PlayerRelationship relationship in Wars(gameData, owner)) {
				int level = Level(relationship.warWearinessPoints, rules);
				if (level < 0) {
					happy += size * rules.happyPercent / 100;
				} else {
					unhappy += size * UnhappyPercent(owner.government.warWeariness, level, rules) / 100;
				}
			}
			if (unhappy > 0) {
				// A Police Station takes a quarter away (rounding the rest
				// down), Universal Suffrage one citizen in every city.
				if (city.constructed_buildings.Any(cb => cb.building.reducesWarWeariness && !cb.building.IsObsolete(owner))) {
					unhappy = unhappy * (100 - rules.policeStationReductionPercent) / 100;
				}
				if (owner.OwnsBuilding(b => b.HasWonderFlag(SaveBuilding.WonderFlag.ReducesWarWearinessAllCities))) {
					unhappy -= rules.allCitiesReduction;
				}
			}
			return (Math.Clamp(unhappy, 0, size), Math.Min(happy, size));
		}

		// Adds points to `weary`'s count against `enemy`, if they're at war.
		public static void AddPoints(Player weary, Player enemy, int points) {
			if (PlayerRelationship.TryGetRelationship(weary, enemy, out PlayerRelationship relationship) && relationship.AtWar()) {
				relationship.warWearinessPoints += points;
			}
		}

		// After a battle: the defender's people tire of being attacked, win or
		// lose, and the attacker's of losing the attacker.
		public static void OnBattle(MapUnit attacker, MapUnit defender, bool attackerKilled) {
			AddPoints(defender.owner, attacker.owner, RulesFor(defender.owner).unitAttacked);
			if (attackerKilled) {
				AddPoints(attacker.owner, defender.owner, RulesFor(attacker.owner).attackerKilled);
			}
		}

		// After `target` has been bombarded by `bombarder`: if it's down to its
		// last hit point, or dead.
		public static void OnBombarded(MapUnit target, Player bombarder) {
			if (target.hitPointsRemaining <= 1) {
				AddPoints(target.owner, bombarder, RulesFor(target.owner).bombardedDownToOneHitPoint);
			}
		}

		// After `destroyer` has pillaged or bombed `count` improvements on
		// `tile`.
		public static void OnImprovementsDestroyed(Tile tile, Player destroyer, int count) {
			Player landOwner = tile.OwningPlayer();
			if (landOwner != null && landOwner != destroyer) {
				AddPoints(landOwner, destroyer, RulesFor(landOwner).improvementDestroyed * count);
			}
		}

		// After `taker` has captured or destroyed `count` units of `loser`
		// that couldn't defend themselves.
		public static void OnUnitsLost(Player loser, Player taker, int count) {
			AddPoints(loser, taker, RulesFor(loser).unitThatCantDefendLost * count);
		}

		// After `winner` has taken a city of `loser` that had `size` citizens.
		public static void OnCityLost(Player loser, Player winner, int size) {
			WarWearinessRules rules = RulesFor(loser);
			AddPoints(loser, winner, size <= 1 ? rules.townOfSizeOneLost : rules.cityLost);
		}

		// The start of a turn, for every civ against each other, all worked
		// out from the points it starts with: at war, units in the enemy's land
		// add a point; at level 1 or more, a turn with no units in either's
		// land takes one away, at war or at peace; and at peace a twentieth of
		// the points, rounded up, wear away. (So a civ that makes peace at 121
		// points loses 8 the first turn, 7 the next, and is back to level 0
		// after 19 turns and to 0 after 43, as in the article.) Points below
		// zero stay (in the user's Civ3 saves, civs at peace keep exactly -30
		// or -60 for many turns). Then a high war weariness government with
		// level 3 against any enemy falls.
		public static void UpdateForNewTurn(GameData gameData) {
			// Which civs have units in which other civs' land.
			HashSet<(Player, Player)> inLandOf = new();
			foreach (MapUnit unit in gameData.mapUnits) {
				Player landOwner = unit.location.OwningPlayer();
				if (landOwner != null && landOwner != unit.owner) {
					inLandOf.Add((unit.owner, landOwner));
				}
			}

			foreach (Player player in gameData.players) {
				if (player.isBarbarians || player.defeated) {
					continue;
				}
				WarWearinessRules rules = RulesFor(player);
				foreach (Player other in gameData.players) {
					if (!PlayerRelationship.TryGetRelationship(player, other, out PlayerRelationship relationship)) {
						continue;
					}
					int points = relationship.warWearinessPoints;
					bool atWar = relationship.AtWar();
					bool inTheirLand = inLandOf.Contains((player, other));
					int change = 0;
					if (atWar && inTheirLand) {
						change += rules.unitsInEnemyLandPerTurn;
					}
					if (Level(points, rules) >= 1 && !inTheirLand && !inLandOf.Contains((other, player))) {
						change += rules.noFightingPerTurn;
					}
					if (!atWar && points > 0) {
						int divisor = Math.Max(1, rules.peaceDecayDivisor);
						change -= (points + divisor - 1) / divisor;
					}
					relationship.warWearinessPoints = points + change;
				}
				MaybeOverthrowGovernment(gameData, player);
			}
		}

		// Whether war weariness against some enemy is enough to overthrow
		// `player`'s government.
		public static bool WouldOverthrowGovernment(GameData gameData, Player player) {
			WarWearinessRules rules = RulesFor(player);
			return player.government.warWeariness == Government.WarWearinessType.High
				&& Wars(gameData, player).Any(relationship => Level(relationship.warWearinessPoints, rules) >= rules.revoltLevel);
		}

		private static void MaybeOverthrowGovernment(GameData gameData, Player player) {
			if (!WouldOverthrowGovernment(gameData, player)) {
				return;
			}
			Government overthrown = player.government;
			player.StartRevolution(gameData);
			log.Information($"War weariness overthrows the {overthrown.name} of {player}, in anarchy until turn {player.inAnarchyUntilTurn}");
			if (player.isHuman) {
				new MsgDescendIntoAnarchy(player.id, overthrown).send();
			}
		}
	}
}

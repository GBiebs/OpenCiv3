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
	// is charged for its own, as the article describes for a human.
	public static class WarWeariness {
		private static ILogger log = Log.ForContext(typeof(WarWeariness));

		public const int PointsPerLevel = 30;

		// The points each event adds (or, if negative, takes away).
		public const int DeclaredWarOn = -30;
		public const int UnitsInEnemyLandPerTurn = 1;
		public const int NoFightingPerTurn = -1;
		public const int UnitThatCantDefendLost = 1;
		public const int ImprovementDestroyed = 1;
		public const int BombardedDownToOneHitPoint = 1;
		public const int AttackerKilled = 2;
		public const int UnitAttacked = 2;
		public const int TownOfSizeOneLost = 16;
		public const int CityLost = 17;

		// In peace, a twentieth of the points, rounded up, wear away each turn.
		// Points below zero stay (in the user's Civ3 saves, civs at peace keep
		// exactly -30 or -60 for many turns).
		public const int PeaceDecayDivisor = 20;

		// Under a high war weariness government, this level against any enemy
		// overthrows it.
		public const int RevoltLevel = 3;

		// The share of each city's citizens made happy below zero points.
		public const int HappyPercent = 25;

		// A Police Station takes a quarter of the war weariness in its city
		// away (rounding the rest down), and Universal Suffrage one unhappy
		// citizen in every city.
		public const int PoliceStationReductionPercent = 25;
		public const int AllCitiesReduction = 1;

		// The level of a count of points: -1 below zero, 0 up to 30, 1 up to
		// 60, 2 up to 90, 3 up to 120, and 4 beyond.
		public static int Level(int points) {
			if (points < 0) {
				return -1;
			}
			return Math.Min(4, Math.Max(0, (points - 1) / PointsPerLevel));
		}

		// The percent of a city's citizens made unhappy at a level of war
		// weariness, by the government's kind. A high war weariness
		// government falls at level 3, so it never sees more than 100%.
		public static int UnhappyPercent(Government.WarWearinessType type, int level) {
			return type switch {
				Government.WarWearinessType.Low => level switch {
					1 => 25,
					2 or 3 => 50,
					>= 4 => 100,
					_ => 0,
				},
				Government.WarWearinessType.High => level switch {
					1 => 50,
					>= 2 => 100,
					_ => 0,
				},
				_ => 0,
			};
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
			int size = city.residents.Count;
			int unhappy = 0;
			int happy = 0;
			foreach (PlayerRelationship relationship in Wars(gameData, owner)) {
				int level = Level(relationship.warWearinessPoints);
				if (level < 0) {
					happy += size * HappyPercent / 100;
				} else {
					unhappy += size * UnhappyPercent(owner.government.warWeariness, level) / 100;
				}
			}
			if (unhappy > 0) {
				if (city.constructed_buildings.Any(cb => cb.building.reducesWarWeariness && !cb.building.IsObsolete(owner))) {
					unhappy = unhappy * (100 - PoliceStationReductionPercent) / 100;
				}
				if (owner.OwnsBuilding(b => b.HasWonderFlag(SaveBuilding.WonderFlag.ReducesWarWearinessAllCities))) {
					unhappy -= AllCitiesReduction;
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
			AddPoints(defender.owner, attacker.owner, UnitAttacked);
			if (attackerKilled) {
				AddPoints(attacker.owner, defender.owner, AttackerKilled);
			}
		}

		// After `target` has been bombarded by `bombarder`: if it's down to its
		// last hit point, or dead.
		public static void OnBombarded(MapUnit target, Player bombarder) {
			if (target.hitPointsRemaining <= 1) {
				AddPoints(target.owner, bombarder, BombardedDownToOneHitPoint);
			}
		}

		// After `destroyer` has pillaged or bombed `count` improvements on
		// `tile`.
		public static void OnImprovementsDestroyed(Tile tile, Player destroyer, int count) {
			Player landOwner = tile.OwningPlayer();
			if (landOwner != null && landOwner != destroyer) {
				AddPoints(landOwner, destroyer, ImprovementDestroyed * count);
			}
		}

		// After `taker` has captured or destroyed `count` units of `loser`
		// that couldn't defend themselves.
		public static void OnUnitsLost(Player loser, Player taker, int count) {
			AddPoints(loser, taker, UnitThatCantDefendLost * count);
		}

		// After `winner` has taken a city of `loser` that had `size` citizens.
		public static void OnCityLost(Player loser, Player winner, int size) {
			AddPoints(loser, winner, size <= 1 ? TownOfSizeOneLost : CityLost);
		}

		// The start of a turn, for every civ against each other, all worked
		// out from the points it starts with: at war, units in the enemy's land
		// add a point; at level 1 or more, a turn with no units in either's
		// land takes one away, at war or at peace; and at peace a twentieth of
		// the points, rounded up, wear away. (So a civ that makes peace at 121
		// points loses 8 the first turn, 7 the next, and is back to level 0
		// after 19 turns and to 0 after 43, as in the article.) Then a high war
		// weariness government with level 3 against any enemy falls.
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
				foreach (Player other in gameData.players) {
					if (!PlayerRelationship.TryGetRelationship(player, other, out PlayerRelationship relationship)) {
						continue;
					}
					int points = relationship.warWearinessPoints;
					bool atWar = relationship.AtWar();
					bool inTheirLand = inLandOf.Contains((player, other));
					int change = 0;
					if (atWar && inTheirLand) {
						change += UnitsInEnemyLandPerTurn;
					}
					if (Level(points) >= 1 && !inTheirLand && !inLandOf.Contains((other, player))) {
						change += NoFightingPerTurn;
					}
					if (!atWar && points > 0) {
						change -= (points + PeaceDecayDivisor - 1) / PeaceDecayDivisor;
					}
					relationship.warWearinessPoints = points + change;
				}
				MaybeOverthrowGovernment(gameData, player);
			}
		}

		// Whether war weariness against some enemy is enough to overthrow
		// `player`'s government.
		public static bool WouldOverthrowGovernment(GameData gameData, Player player) {
			return player.government.warWeariness == Government.WarWearinessType.High
				&& Wars(gameData, player).Any(relationship => Level(relationship.warWearinessPoints) >= RevoltLevel);
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

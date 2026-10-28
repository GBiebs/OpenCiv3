using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// Civ3's space race (Civilopedia "Victory Conditions", "Space Ship"):
	// once a civ has built the Apollo Program (the small wonder that allows
	// spaceship parts), its cities build the parts of its spaceship, in any
	// order; Civ3's RULE section says how many of each part the ship needs
	// (one of each of ten in Conquests). The parts are kept at the capital:
	// when another civ captures it, the spaceship is lost (script.txt
	// LOSTSPACESHIP; CivFanatics). A complete ship can be launched, which
	// wins the game at once (Civ3 has no flight to Alpha Centauri).
	public static class Spaceship {
		private static ILogger log = Log.ForContext(typeof(Spaceship));

		public static int Needed(Rules rules, int part) {
			return part >= 0 && part < rules.victory.spaceshipParts.Count ? rules.victory.spaceshipParts[part] : 0;
		}

		public static int Built(Player player, int part) {
			return player.spaceshipParts.GetValueOrDefault(part);
		}

		public static int PartsNeeded(Rules rules) => rules.victory.spaceshipParts.Sum();

		public static int PartsBuilt(Player player, Rules rules) {
			return Enumerable.Range(0, rules.victory.spaceshipParts.Count).Sum(i => System.Math.Min(Built(player, i), Needed(rules, i)));
		}

		public static bool IsComplete(Player player, Rules rules) {
			return PartsNeeded(rules) > 0 && PartsBuilt(player, rules) == PartsNeeded(rules);
		}

		// Whether `city` may start this part: the ship needs more of it than
		// we have and our other cities are building.
		public static bool NeedsMore(City city, Building part) {
			Player owner = city.owner;
			int underway = owner.cities.Count(c => c != city && c.itemBeingProduced?.name == part.name);
			return Built(owner, part.spaceshipPart.Value) + underway < Needed(owner.rules, part.spaceshipPart.Value);
		}

		// A city finished a part: it goes into the ship, and a complete ship
		// is launched by the AI or offered to the human.
		public static void AddPart(GameData gameData, City city, Building part) {
			Player owner = city.owner;
			int index = part.spaceshipPart.Value;
			owner.spaceshipParts[index] = Built(owner, index) + 1;
			log.Information($"{owner} adds {part.name} to its spaceship ({PartsBuilt(owner, owner.rules)}/{PartsNeeded(owner.rules)})");
			new MsgSpaceshipPart(owner, part).send();
			if (IsComplete(owner, owner.rules) && !owner.spaceshipLaunched) {
				if (owner.isHuman) {
					new MsgSpaceshipComplete(owner.id).send();
				} else {
					Launch(gameData, owner);
				}
			}
		}

		public static bool CanLaunch(GameData gameData, Player player) {
			return !gameData.gameOver && !player.spaceshipLaunched && IsComplete(player, player.rules)
				&& gameData.victoryConditions?.AllowSpaceRaceVictory == true;
		}

		public static void Launch(GameData gameData, Player player) {
			if (!CanLaunch(gameData, player)) {
				return;
			}
			player.spaceshipLaunched = true;
			log.Information($"{player} launches its spaceship");
			TurnHandling.CheckVictory(gameData);
		}

		// `captor` took `loser`'s capital, and with it the spaceship.
		public static void OnCapitalCaptured(Player loser, Player captor) {
			if (loser.spaceshipParts.Values.Sum() == 0) {
				return;
			}
			loser.spaceshipParts.Clear();
			log.Information($"{captor} destroys {loser}'s spaceship");
			new MsgSpaceshipLost(loser, captor).send();
		}
	}
}

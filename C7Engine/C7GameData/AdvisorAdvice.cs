using System.Collections.Generic;
using System.Linq;

namespace C7GameData {
	// What an advisor tells the player on their screen, and how they look
	// saying it (Civ3's four advisor moods). Each advisor names the most
	// pressing matter first; the wording is ours.
	public static class AdvisorAdvice {
		public enum Mood {
			Happy,
			Sad,
			Angry,
			Surprised,
		}

		public record Advice(Mood mood, string text);

		// How many turns a treasury losing gold may last before the domestic
		// advisor worries.
		public const int TreasuryWarningTurns = 10;

		// The domestic advisor: anarchy, cities in disorder, the treasury
		// running out, starving cities, then cities that can't grow without a
		// building.
		public static Advice Domestic(GameData gameData, Player player) {
			if (player.inAnarchyUntilTurn > gameData.turn) {
				return new(Mood.Sad, $"Anarchy grips the land: {Turns(player.inAnarchyUntilTurn - gameData.turn)} until our new government takes over.");
			}
			List<City> disorder = player.cities.Where(c => c.isInCivilDisorder).ToList();
			if (disorder.Count > 0) {
				return new(Mood.Angry, $"{Names(disorder)} {Are(disorder)} in disorder! More luxuries or entertainers would calm the people.");
			}
			int net = player.CalculateGoldPerTurn();
			if (net < 0 && player.gold + net < 0) {
				return new(Mood.Angry, "The treasury runs dry next turn, and then we must sell buildings to pay our bills!");
			}
			if (net < 0 && player.gold / -net < TreasuryWarningTurns) {
				return new(Mood.Sad, $"We lose {-net} gold a turn: the treasury lasts only {Turns(player.gold / -net)} more.");
			}
			List<City> starving = player.cities.Where(c => c.FoodGrowthPerTurn() < 0).ToList();
			if (starving.Count > 0) {
				return new(Mood.Sad, $"{Names(starving)} {Are(starving)} starving.");
			}
			foreach (City city in player.cities) {
				int limit = city.SizeLimit(gameData);
				if (city.residents.Count >= limit && city.FoodGrowthPerTurn() > 0) {
					bool town = !city.CanGrowIntoCity();
					Building needed = gameData.Buildings.FirstOrDefault(b => town ? b.allowsCitySize2 : b.allowsCitySize3);
					return new(Mood.Surprised, $"{city.name} can grow no larger without {(needed == null ? "help" : WithArticle(needed.name))}.");
				}
			}
			return new(Mood.Happy, net > 0 ? "All is calm at home, and the treasury is growing." : "All is calm at home.");
		}

		// The foreign advisor: no one met yet, our wars, then civs angry with
		// us (AIs only: a human's attitude isn't ours to know).
		public static Advice Foreign(GameData gameData, Player player) {
			List<Player> known = gameData.GetKnownRivals(player);
			if (known.Count == 0) {
				return new(Mood.Surprised, "We haven't met another civilization yet.");
			}
			List<Player> enemies = known.Where(p => PlayerRelationship.AtWar(player, p)).ToList();
			if (enemies.Count > 0) {
				return new(Mood.Angry, $"We are at war with {Nouns(enemies)}!");
			}
			foreach (Attitude.Level level in new[] { Attitude.Level.Furious, Attitude.Level.Annoyed }) {
				List<Player> upset = known.Where(p => !p.isHuman && Attitude.Of(gameData, p, player) == level).ToList();
				if (upset.Count > 0) {
					string who = Nouns(upset);
					return new(Mood.Sad, $"{char.ToUpperInvariant(who[0])}{who[1..]} are {level.ToString().ToLowerInvariant()} with us.");
				}
			}
			return new(Mood.Happy, "We are at peace with every civilization we know.");
		}

		// The military advisor: cities without a defender (worse at war), units
		// we pay for beyond the free ones, then units we could upgrade.
		public static Advice Military(GameData gameData, Player player) {
			bool atWar = gameData.GetKnownRivals(player).Any(p => PlayerRelationship.AtWar(player, p));
			List<City> undefended = MilitaryReport.Undefended(player);
			if (undefended.Count > 0) {
				return new(atWar ? Mood.Angry : Mood.Sad, $"{Names(undefended)} {Are(undefended)} without a defender!");
			}
			(_, _, int cost) = player.TotalUnitsAllowedUnitsAndSupportCost();
			if (cost > 0) {
				return new(Mood.Sad, $"Our units cost us {cost} gold a turn beyond what our cities support for free.");
			}
			int upgradable = MilitaryReport.ByType(gameData, player).Sum(g => g.upgradable);
			if (upgradable > 0) {
				return new(Mood.Surprised, upgradable == 1 ? "One of our units can be upgraded." : $"{upgradable} of our units can be upgraded.");
			}
			return new(Mood.Happy, "Our forces stand ready.");
		}

		// The trade advisor: cities off the capital's network, a luxury a civ
		// we can trade with could give us, a resource we have to spare.
		public static Advice Trade(GameData gameData, Player player) {
			List<City> unconnected = TradeReport.Unconnected(gameData, player);
			if (unconnected.Count > 0) {
				bool one = unconnected.Count == 1;
				return new(Mood.Sad, $"{Names(unconnected)} {(one ? "isn't" : "aren't")} joined to our capital, so {(one ? "it gets" : "they get")} none of our resources.");
			}
			List<TradeReport.Partner> partners = TradeReport.Partners(gameData, player).Where(p => p.standing != TradeReport.Standing.CannotTrade).ToList();
			foreach (TradeReport.Partner partner in partners) {
				Resource wanted = TradeReport.TheyCouldSell(gameData, player, partner.civ).FirstOrDefault(r => r.Category == ResourceCategory.LUXURY);
				if (wanted != null) {
					return new(Mood.Surprised, $"The {partner.civ.civilization.noun} could trade us {wanted.Name}.");
				}
			}
			TradeReport.ResourceRow spare = TradeReport.Resources(gameData, player).FirstOrDefault(r => r.local + r.imported - r.exported >= 2);
			if (spare != null && partners.Count > 0) {
				return new(Mood.Happy, $"We have {spare.resource.Name} to spare for trade.");
			}
			return new(Mood.Happy, partners.Count > 0 ? "Our trade is in good order." : "We have no one to trade with yet.");
		}

		public static string Turns(int turns) => turns == 1 ? "1 turn" : $"{turns} turns";

		public static string WithArticle(string noun) => ("AEIOU".Contains(char.ToUpperInvariant(noun[0])) ? "an " : "a ") + noun;

		private static string Names(List<City> cities) => List(cities.Select(c => c.name).ToList());

		private static string Nouns(List<Player> players) => List(players.Select(p => $"the {p.civilization.noun}").ToList());

		// "Rome", "Rome and Veii", "Rome, Veii and Antium", "Rome, Veii and 3 more".
		private static string List(List<string> names) {
			return names.Count switch {
				1 => names[0],
				2 => $"{names[0]} and {names[1]}",
				3 => $"{names[0]}, {names[1]} and {names[2]}",
				_ => $"{names[0]}, {names[1]} and {names.Count - 2} more",
			};
		}

		private static string Are(List<City> cities) => cities.Count == 1 ? "is" : "are";
	}
}

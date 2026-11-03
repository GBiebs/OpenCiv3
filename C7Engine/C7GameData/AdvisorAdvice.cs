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

		public static string Turns(int turns) => turns == 1 ? "1 turn" : $"{turns} turns";

		public static string WithArticle(string noun) => ("AEIOU".Contains(char.ToUpperInvariant(noun[0])) ? "an " : "a ") + noun;

		// "Rome", "Rome and Veii", "Rome, Veii and 3 more".
		private static string Names(List<City> cities) {
			List<string> names = cities.Select(c => c.name).ToList();
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

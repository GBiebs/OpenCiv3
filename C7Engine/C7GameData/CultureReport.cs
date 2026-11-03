using System;
using System.Collections.Generic;
using System.Linq;

namespace C7GameData {
	// What the cultural advisor shows: each city's culture and what its
	// borders need to grow, our culture a turn from improvements and from
	// wonders, our wonders, and how the civs we know see our culture.
	public static class CultureReport {
		// A city's culture, its culture a turn, and the culture at which its
		// borders next grow.
		public record CityCulture(City city, int culture, int perTurn, int nextExpansion);

		public record WonderCulture(Building wonder, City city, int perTurn);

		// Our cities, most culture first.
		public static List<CityCulture> Cities(Player player) {
			return player.cities
				.Select(c => new CityCulture(c, c.GetCulture(), c.GetCulturePerTurn(), (int)Math.Min(int.MaxValue, Math.Pow(10, c.GetBorderExpansionLevel()))))
				.OrderByDescending(c => c.culture)
				.ToList();
		}

		// Our culture a turn from improvements and from wonders.
		public static (int improvements, int wonders) PerTurn(Player player) {
			int improvements = 0, wonders = 0;
			foreach (City city in player.cities) {
				foreach (CityBuilding cb in city.GetBuildings()) {
					if (cb.building.IsWonder()) {
						wonders += city.CulturePerTurnFrom(cb);
					} else {
						improvements += city.CulturePerTurnFrom(cb);
					}
				}
			}
			return (improvements, wonders);
		}

		// The wonders in our cities, the most culture first.
		public static List<WonderCulture> Wonders(Player player) {
			return player.cities
				.SelectMany(c => c.GetBuildings().Where(cb => cb.building.IsWonder()).Select(cb => new WonderCulture(cb.building, c, c.CulturePerTurnFrom(cb))))
				.OrderByDescending(w => w.perTurn).ThenBy(w => w.wonder.name)
				.ToList();
		}

		// How the people of each civ we know see our culture (Civ3's culture
		// levels, from the best).
		public static List<(Player civ, CultureOpinion opinion)> Opinions(GameData gameData, Player player) {
			return gameData.GetKnownRivals(player)
				.Select(p => (p, Resistance.OpinionOf(gameData, p, player)))
				.Where(o => o.Item2 != null)
				.OrderByDescending(o => o.Item2.cultureRatioPercent)
				.ToList();
		}

		// How many turns until the city's borders grow at its culture a turn,
		// or null if it makes none.
		public static int? TurnsToExpand(CityCulture city) {
			return city.perTurn <= 0 ? null : (city.nextExpansion - city.culture + city.perTurn - 1) / city.perTurn;
		}
	}
}

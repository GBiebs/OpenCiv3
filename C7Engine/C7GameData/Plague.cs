using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// Conquests' plague (Civilopedia "Plague"): it "can wipe out city
	// population and land units (except Kings). The effect of plague is
	// exacerbated in larger cities, walled cities and cities with commerce
	// and a trade network. Once the concept of Sanitation is understood the
	// effect of plague is much reduced." A scenario turns it on and sets its
	// name, earliest start, chance, duration, strength, grace period and
	// most outbreaks (Civ3's GAME settings, Rules.plague); the standard
	// rules have it off, and Civ3 has none in multiplayer. How these combine
	// isn't documented, so this is our reading: from the earliest start
	// year, each turn is a chance percent that an outbreak starts; it lasts
	// the duration in years, and the next can't start until the grace period
	// has passed. During one, each city is struck with the strength as a
	// percent chance, scaled by a weight of 1 to 4 (a city past town size,
	// walls, a trade route to its capital) over 4, halved once its owner
	// knows Sanitation (the advance that "disables diseases"). A strike kills
	// a citizen (never the last) and each land unit in the city with a
	// quarter of that chance. (There are no Kings yet.)
	public static class Plague {
		private static ILogger log = Log.ForContext(typeof(Plague));

		public static bool Active(GameData gameData) {
			return gameData.plague.endsYear != null;
		}

		private static int? Year(GameData gameData) {
			try {
				return gameData.timeOptions.GetAbsoluteYear(gameData.turn);
			} catch (Exception) {
				return null;
			}
		}

		// Whether an outbreak may start in `year`.
		public static bool CanStart(GameData gameData, int year) {
			PlagueRules rules = gameData.rules.plague;
			PlagueState state = gameData.plague;
			return rules.enabled && !Active(gameData) && state.outbreaks < rules.maxOutbreaks && year >= rules.earliestStartYear
				&& (state.endedYear == null || year >= state.endedYear + rules.graceYears);
		}

		// The chance, between 0 and 1, that an outbreak strikes `city` this
		// turn.
		public static double CityChance(GameData gameData, City city) {
			Rules rules = gameData.rules;
			int weight = 1;
			if (city.residents.Count > rules.MaximumLevel1CitySize) {
				++weight;
			}
			if (city.HasWalls()) {
				++weight;
			}
			if (!city.IsCapital() && gameData.GetTradeNetwork().ConnectedToCapital(city.owner, city)) {
				++weight;
			}
			double chance = rules.plague.strength / 100.0 * weight / 4;
			if (city.owner.HasTechWithFlag(SaveTech.Flag.DisablesDiseases)) {
				chance /= 2;
			}
			return Math.Clamp(chance, 0, 1);
		}

		// Once a turn: an outbreak may end or start, and while one lasts it
		// strikes cities.
		public static void UpdateForNewTurn(GameData gameData) {
			PlagueRules rules = gameData.rules.plague;
			PlagueState state = gameData.plague;
			if (!rules.enabled || Year(gameData) is not int year) {
				return;
			}
			if (Active(gameData) && year >= state.endsYear) {
				state.endsYear = null;
				state.endedYear = year;
				log.Information($"The {rules.name} ends in {year}");
			}
			if (CanStart(gameData, year) && GameData.rng.Next(100) < rules.chancePercent) {
				state.endsYear = year + Math.Max(1, rules.durationYears);
				++state.outbreaks;
				log.Information($"The {rules.name} breaks out in {year}, until {state.endsYear}");
			}
			if (!Active(gameData)) {
				return;
			}
			foreach (City city in gameData.cities.Where(c => !c.owner.isBarbarians).ToList()) {
				double chance = CityChance(gameData, city);
				if (chance > 0 && GameData.rng.NextDouble() < chance) {
					Strike(gameData, city, chance);
				}
			}
		}

		public static void Strike(GameData gameData, City city, double chance) {
			if (city.residents.Count > 1) {
				city.RemoveCitizens(1);
			}
			List<string> killed = [];
			foreach (MapUnit unit in city.location.unitsOnTile.Where(u => u.IsLandUnit() && !u.IsInArmy()).ToList()) {
				if (GameData.rng.NextDouble() < chance / 4) {
					killed.Add(unit.unitType.name);
					gameData.RemoveUnit(unit);
				}
			}
			log.Information($"The {gameData.rules.plague.name} strikes {city.name}, now size {city.residents.Count}; killed {string.Join(", ", killed)}");
			new MsgPlague(city.owner.id, city, gameData.rules.plague.name, killed).send();
		}
	}

	// The plague's progress (see Plague), saved with the game.
	public class PlagueState {
		public int outbreaks;
		// The year the outbreak under way ends, if one is.
		public int? endsYear;
		public int? endedYear;
	}
}

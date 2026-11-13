using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;
using Serilog;

namespace C7Engine {
	// Civ3's plague, a scenario feature: off unless the BIQ's GAME section
	// permits it (conquests.biq has PermitPlagues=0), set up on the editor's
	// Disasters page.
	//
	// Sources:
	// - Conquests Civilopedia, GCON_Plague: "Plague, if and when it occurs,
	//   can wipe out city population and land units (except Kings). The
	//   effect of plague is exacerbated in larger cities, walled cities and
	//   cities with commerce and a trade network. Once the concept of
	//   Sanitation is understood the effect of plague is much reduced."
	// - The Conquests editor help (Civ3ConquestsEdit.chm, "Permit Plagues
	//   (Disasters)"): "Earliest Start: Indicates the plague's start year.
	//   Variance: Indicates how close in years to the earliest start the
	//   plague actually arrives, specified as a percentage of the plague
	//   Grace Period. Duration: Indicates the number of years each plague
	//   occurrence lasts before dissipating. Strength: Indicates how powerful
	//   the plague is (set a number from 0 to 100). Grace Period: Indicates
	//   how long in years between each plague. Max Occurrences: Indicates how
	//   many plague periods will occur within the game."
	// - The 1.22 binary (Perez's decompile, doc/perez-findings/
	//   Civ3_Findings.md, the interturn): plague is processed once a round,
	//   globally, before global warming and the volcanoes, "skipped in
	//   network mode; countdown with ±50 % jitter, scenario feature". The
	//   Conquests readme: "Disasters such as plague and volcanic eruption do
	//   not occur in multiplayer games" (see Volcano.IsMultiplayer).
	// - Players (CivFanatics, "The Plague", thread 471693, Middle Ages
	//   scenario): it "affects all players at the same time" and "Plague
	//   affects both units in cities and the population of cities" (joncha,
	//   from memory).
	// - The messages are Civ3's (Conquests/Text/script.txt): SUMMARY_PLAGUE,
	//   "Horrors! $PLAGUE1 strikes $CITY0!", and PLAGUE_KILL, "$UNIT0 has
	//   been killed by $PLAGUE1!".
	//
	// UNVERIFIED (our reading; compendium 07, open questions): no source
	// gives the numbers, so the timing follows the editor help and Perez's
	// note, and the strike follows GBiebs/OpenCiv3's guesses (commit
	// 0f16f257):
	// - The first outbreak comes at the earliest start plus a random part of
	//   the variance (a percentage of the grace period). After one ends, the
	//   next comes a grace period later, jittered by up to ±50% (Perez's
	//   "countdown with ±50 % jitter"). There are at most Max Occurrences.
	// - An outbreak lasts its duration in years, and at least the turn it
	//   starts.
	// - While one lasts, each city (not the barbarians') is struck each turn
	//   with the strength as a percent chance, times a weight of 1 to 4 over
	//   4: one each for being past town size, having walls, and being joined
	//   to its owner's capital by the trade network (commerce and a trade
	//   network). Sanitation (the tech that disables disease) halves it.
	// - A strike costs the city a citizen (never its last) and kills each of
	//   the land units in it but Kings and units in armies with a quarter of
	//   that chance.
	public static class Plague {
		private static readonly ILogger log = Log.ForContext(typeof(Plague));

		// Perez: the countdown between outbreaks is jittered by ±50%.
		public const int GraceJitterPercent = 50;

		// The calendar year of the turn (for a calendar in months, weeks,
		// days or hours, the year it falls in).
		public static int Year(GameData gameData, int turn) {
			TimeOptions time = gameData.timeOptions;
			if (time == null) {
				return turn;
			}
			int perYear = time.baseUnit switch {
				TimeUnit.Months => 12,
				TimeUnit.Weeks => 52,
				TimeUnit.Days => 365,
				TimeUnit.Hours => 365 * 24,
				_ => 1,
			};
			int start = time.GetRawNumber(0);
			int elapsed = time.GetRawNumber(turn) - start;
			if (perYear == 1) {
				return time.GetRawNumber(turn);
			}
			return time.startYear + (int)Math.Floor((double)elapsed / perYear);
		}

		public static bool Enabled(GameData gameData) {
			return gameData.rules != null && gameData.rules.PermitPlagues;
		}

		public static bool Active(GameData gameData) {
			return gameData.plague.endsYear != null;
		}

		// Once a round, after everyone has played: an outbreak may end or
		// begin, and while one lasts it strikes cities. Returns the cities
		// struck.
		public static List<City> ProcessEndOfRound(GameData gameData) {
			List<City> struck = new();
			if (!Enabled(gameData) || Volcano.IsMultiplayer(gameData)) {
				return struck;
			}
			Rules rules = gameData.rules;
			PlagueState state = gameData.plague;
			int year = Year(gameData, gameData.turn);

			if (state.endsYear is int ends && year >= ends && state.startedTurn != gameData.turn) {
				state.endsYear = null;
				int grace = Math.Max(0, rules.PlagueGracePeriod);
				int jitter = grace * GraceJitterPercent / 100;
				state.nextStartYear = year + grace + (jitter > 0 ? GameData.rng.Next(-jitter, jitter + 1) : 0);
				log.Information("{Plague} ends in {Year}; the next may come in {Next}", rules.PlagueName, year, state.nextStartYear);
			}
			if (state.nextStartYear == null && state.outbreaks == 0) {
				int span = Math.Max(0, rules.PlagueGracePeriod) * Math.Max(0, rules.PlagueVariance) / 100;
				state.nextStartYear = rules.PlagueEarliestStart + (span > 0 ? GameData.rng.Next(span + 1) : 0);
			}
			if (!Active(gameData) && state.outbreaks < rules.PlagueMaxOccurrences
				&& state.nextStartYear is int next && year >= next) {
				state.endsYear = year + Math.Max(0, rules.PlagueDuration);
				state.startedTurn = gameData.turn;
				state.nextStartYear = null;
				++state.outbreaks;
				log.Information("{Plague} breaks out in {Year}, until {Ends}", rules.PlagueName, year, state.endsYear);
			}
			if (!Active(gameData)) {
				return struck;
			}

			foreach (City city in gameData.cities.ToList()) {
				if (city.owner == null || city.owner.isBarbarians) {
					continue;
				}
				int chance = ChancePerThousand(gameData, city);
				if (chance > 0 && GameData.rng.Next(1000) < chance) {
					Strike(gameData, city, chance);
					struck.Add(city);
				}
			}
			return struck;
		}

		// The chance, in thousandths, that an outbreak under way strikes the
		// city this turn (see above).
		public static int ChancePerThousand(GameData gameData, City city) {
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
			int chance = Math.Clamp(rules.PlagueStrength, 0, 100) * 10 * weight / 4;
			if (city.owner.HasTechEnabling(SaveTech.Flag.DisablesDiseases)) {
				chance /= 2;
			}
			return chance;
		}

		// The plague strikes the city: it loses a citizen, and each land unit
		// in it but Kings and units in armies dies with a quarter of the
		// chance. Its owner is told.
		public static List<MapUnit> Strike(GameData gameData, City city, int chancePerThousand) {
			string plague = gameData.rules.PlagueName;
			if (city.residents.Count > 1) {
				city.RemoveCitizen(city.residents[city.residents.Count - 1]);
			}
			List<MapUnit> killed = new();
			foreach (MapUnit unit in city.location.unitsOnTile.ToList()) {
				if (!unit.IsLandUnit() || unit.unitType.isKing || unit.IsInArmy()) {
					continue;
				}
				if (GameData.rng.Next(4000) < chancePerThousand) {
					killed.Add(unit);
					unit.RemoveFromPlay();
				}
			}
			log.Information("{Plague} strikes {City}, now size {Size}; {Killed} units die", plague, city.name, city.residents.Count, killed.Count);
			Player owner = city.owner;
			if (owner != null && owner.IsToldNews) {
				new MsgShowDomesticAdvisorPopup(owner, $"Horrors! {plague} strikes {city.name}!").send();
				foreach (MapUnit unit in killed) {
					if (unit.owner == owner) {
						new MsgShowMilitaryAdvisorPopup(owner, $"{unit.unitType.name} has been killed by {plague}!", happy: false).send();
					}
				}
			}
			return killed;
		}
	}

	// The plague's progress (see Plague), saved with the game.
	public class PlagueState {
		// The outbreaks so far.
		public int outbreaks;
		// The year the outbreak under way ends, or null if none is.
		public int? endsYear;
		// The turn the outbreak under way began.
		public int startedTurn = -1;
		// The year the next outbreak may begin, once known.
		public int? nextStartYear;
	}
}

using System.Collections.Generic;
using System.Linq;
using C7GameData;
using Serilog;

namespace C7Engine {
	// Civ3's nuclear meltdowns.
	//
	// - "Nuclear Plants can meltdown, causing all hell to break loose..."
	//   (Conquests Civilopedia, BLDG_Nuclear_Plant). Which buildings can is
	//   the BIQ's "can meltdown" flag (the Nuclear Plant in conquests.biq).
	// - When: "a nuclear plant has a 50% chance of melting down during any
	//   turn in which the host city experiences civil disorder" says the
	//   manual, but "empirical testing indicates that the correct chances
	//   are about 10% – 15%", and "the Nuclear Plant will never melt down on
	//   the turn that the host city enters into disorder – only if a city is
	//   allowed to continue through a full turn in a state of civil disorder
	//   is there a chance of meltdown"
	//   (https://civfanatics.com/civ3/strategy/game-mechanics/everything-you-wanted-to-know-about-power-plants/).
	// - What: "A city that experiences a meltdown will have its eight
	//   surrounding city tiles polluted and will have its population cut in
	//   half"; "neither units nor city improvements (not even the Nuclear
	//   Plant itself) will be damaged or destroyed", and the pollution "will
	//   not destroy the underlying terrain improvements" (the same article);
	//   "the nuclear plant is NOT destroyed in the process, which means that
	//   you can have multiple meltdowns in the same city"
	//   (https://forums.civfanatics.com/threads/nuclear-plant-meltdown.16422/).
	// - The message is Civ3's (Conquests/Text/Script.txt, MELTDOWN, from the
	//   Domestic advisor): "The Nuclear Plant in $CITY0 has malfunctioned!
	//   Thousands are dead, and pollution has crippled production!"
	//
	// - The chance: Rand(100) < 10 for each building that can melt down,
	//   only while the city is in disorder (Civ3 binary,
	//   doc/perez-findings/Civ3_Findings.md, Round 2b; owner's ruling,
	//   2026-11-12). We roll at the turn's disorder check when the city was
	//   in disorder at the previous check and still is, which keeps the
	//   "never on the turn that the host city enters into disorder" above.
	//
	// UNVERIFIED (simplest consistent behaviour):
	// - "Cut in half" rounds in the city's favour: a city of 7 keeps 4. A
	//   size 1 city keeps its citizen.
	// - Only the land tiles of the eight around the city are polluted, as
	//   pollution elsewhere only strikes land (see City.MaybePollute).
	// - Each meltdown adds to world pollution for the rest of the game (see
	//   GlobalWarming).
	public static class Meltdown {
		private static readonly ILogger log = Log.ForContext(typeof(Meltdown));

		// The percent chance of a meltdown, for each building that can.
		public const int ChancePercent = 10;

		// Whether the city has a building that can melt down.
		public static bool CanMeltDown(City city) {
			return PlantCount(city) > 0;
		}

		// The number of the city's buildings that can melt down.
		public static int PlantCount(City city) {
			return city.EffectiveBuildings().Count(cb => cb.building.canMeltdown);
		}

		// Called at the turn's disorder check for a city that was in
		// disorder at the previous check and still is. Returns whether its
		// plant melted down.
		public static bool MaybeMeltDown(GameData gameData, City city) {
			// One roll for each plant; no draw without one, so other cities
			// leave the random numbers alone.
			int plants = PlantCount(city);
			for (int i = 0; i < plants; ++i) {
				if (GameData.rng.Next(100) < ChancePercent) {
					MeltDown(gameData, city);
					return true;
				}
			}
			return false;
		}

		// The city's plant melts down: the land around the city is
		// polluted, half its citizens die, and its owner is told.
		public static List<Tile> MeltDown(GameData gameData, City city) {
			List<Tile> polluted = new();
			foreach (Tile t in city.location.neighbors.Values) {
				if (t == null || t == Tile.NONE || t.IsWater() || t.HasPollution() || polluted.Contains(t)) {
					continue;
				}
				if (Tile.TryAddPollution(t)) {
					polluted.Add(t);
				}
			}

			// The meltdown adds to world pollution (Civ3 FUN_004bb970 counts
			// it in the counter GlobalWarming squares).
			if (gameData != null) {
				++gameData.nuclearEvents;
			}

			int lost = city.residents.Count / 2;
			city.RemoveCitizens(lost);
			if (gameData != null) {
				city.RecalculateCitizenMoods(gameData);
			}
			log.Information("The nuclear plant in {City} melts down, polluting {Tiles} tiles and killing {Lost} citizens",
				city.name, polluted.Count, lost);

			if (city.owner != null && city.owner.IsToldNews) {
				new MsgShowDomesticAdvisorPopup(city.owner,
					$"The Nuclear Plant in {city.name} has malfunctioned! Thousands are dead, and pollution has crippled production!").send();
			}
			return polluted;
		}
	}
}

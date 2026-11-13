using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;
using Serilog;

namespace C7Engine {
	// Civ3's disease from unhealthy terrain.
	//
	// - "Disease kills citizens and units that spend too much time in
	//   unhealthy areas like floodplains, jungles and marsh. Military units
	//   that fortify in these terrains are susceptible to disease." (Conquests
	//   Civilopedia, GCON_Disease). Which terrains cause it, and which a tech
	//   cures, are the BIQ's terrain flags (flood plain, jungle and marsh
	//   cause it; only flood plain is curable).
	// - Which tech cures it: the tech with the BIQ flag "Disables Diseases
	//   From Flood Plains", Sanitation in the standard rules, as intended:
	//   "Disease from Floodplains is eliminated, and the effect of Plague is
	//   reduced." (Conquests Civilopedia, TECH_Sanitation); "Floodplains
	//   continue to be risky until you discover sanitation." (GCON_Disease).
	//   The shipped executable has a bug here: "In the base game, the tech
	//   flag "Disables Diseases From Flood Plains" (applied to Sanitation)
	//   does nothing and instead flood plains disease stops once players
	//   discover Writing. This happens because that flag is encoded as
	//   number 8 and, instead of checking if players have a tech with that
	//   flag, the code mistakenly checks if players have tech #8, which is
	//   Writing under the standard rules."
	//   (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini,
	//   "DISEASE CURING TECH FLAG BUG"; compendium 07 section 7). The
	//   project owner chose the intended behaviour over the bug (owner's
	//   decision, 2026-10-20).
	// - A city is at risk from the mere presence of unhealthy terrain in its
	//   radius, worked or not: "abstaining from working floodplain tiles
	//   does NOT stop the disease" (morchuflex,
	//   https://forums.civfanatics.com/threads/disease-from-flood-plain.102771/);
	//   "just the presence of disease causing terrain within the city radius
	//   will put that city/town at risk" (HellboundHeathcliff,
	//   https://forums.civfanatics.com/threads/cause-of-plague.120157).
	// - The city's illness is the Civ3 binary's City_ProcessIllness (Civ3
	//   binary, doc/perez-findings/Civ3_Findings.md, "Pollution and
	//   illness" and Round 2b), authoritative by the owner's ruling of
	//   2026-11-12, which supersedes the owner's 2026-10-23 ruling (one
	//   citizen, half the time a second, then 20 turns' immunity, at 0.5% a
	//   turn): a healthy city falls ill when
	//   Rand(0x300 - terrainDisease * 512 / 100) < size, and an ill city
	//   loses a citizen every turn and recovers when Rand(128) > size.
	//   Our reading (the decompile gives the rolls, not every detail):
	//   - terrainDisease is the strongest disease strength among the
	//     unhealthy tiles of the city's radius (50 for every disease
	//     terrain in conquests.biq, so the roll is Rand(512)); with none the
	//     city doesn't roll. A size 8 city falls ill 8/512 = 1.6% of its
	//     turns, near CivWiki's "auf ca. 1% geschätzt" (below).
	//   - "vs size" means it falls ill when the roll is below its size, so
	//     big cities fall ill more often, just as they recover less often.
	//   - The turn it falls ill counts as an ill turn and costs a citizen;
	//     each later ill turn costs one and then rolls for recovery against
	//     the new size. So an outbreak almost always costs two citizens on
	//     two turns running, as players observed: a city with at least two
	//     citizens "tötet diese stets zwei Bevölkerungspunkte, soweit
	//     möglich" ("always loses two population points, if possible";
	//     CivWiki, Krankheit (Civ3), https://www.civ-wiki.de/), "you always
	//     get exactly two turns of disease in your city, losing 2 citizens"
	//     (morchuflex, above), and "If you get hit by disease once. You will
	//     get it again the next turn 100% unless you are already at size 1"
	//     (Qitai, https://forums.civfanatics.com/threads/maya-flood-plain-start.74466/).
	//   - A size 1 city doesn't shrink (Qitai; CivWiki's "soweit möglich").
	//   - No building protects a city: neither the decompile nor the
	//     compendium names one.
	// - Units: "fortified land units with a zero PRTO field in jungle
	//   (terrain 8) are lost to disease with chance 1/1000" (Civ3 binary,
	//   Unit_StartOfTurn, Round 2b): only jungle, not flood plains or marsh,
	//   whatever the terrain's disease strength. That matches Brain's test:
	//   50 warriors fortified in jungle lost 20 over 510 turns, and "I also
	//   ran the test with unfortified (but not moving) units and none of the
	//   50 warriors had died after 200 turns."
	//   (https://forums.civfanatics.com/threads/death-by-jungle-fever.113653/,
	//   Conquests). Workers are immune: "Corrected Disease Civilopedia
	//   chapter to reflect workers are immune to disease." (README.txt;
	//   perhaps that PRTO field). We keep the cure: a jungle a tech cures
	//   (none in the standard rules) spares the units of civs that know it.
	// - Plague is off in the standard rules (conquests.biq PermitPlagues=0);
	//   scenarios that permit it get it from Plague.
	// - The messages are Civ3's (Conquests/Text/Script.txt):
	//   SUMMARY_DISEASE, "Disease strikes $CITY0.", and UNITJUNGLEDISEASE,
	//   "Our $UNIT0 has died of disease from the jungle!".
	//
	// UNVERIFIED:
	// - Units fortified in a city aren't struck.
	// - Which citizen dies: the last one working an unhealthy tile, or the
	//   last one if none does.
	public static class Disease {
		private static readonly ILogger log = Log.ForContext(typeof(Disease));

		// The range of the roll for falling ill is IllnessBase less the
		// disease strength times IllnessStrengthScale / 100.
		public const int IllnessBase = 0x300;
		public const int IllnessStrengthScale = 512;

		// An ill city recovers when Rand(RecoveryRange) > size.
		public const int RecoveryRange = 128;

		// A fortified unit in jungle dies with the chance 1 in this a turn.
		public const int UnitDeathRange = 1000;

		// The tech that cures flood plains: the first with the "Disables
		// Diseases From Flood Plains" flag (see above), or null if the rules
		// have none.
		public static Tech CuringTech(GameData gameData) {
			return gameData?.techs?.Find(t => t.HasFlag(SaveTech.Flag.DisablesDiseases));
		}

		// Whether the terrain can give the player's cities and units disease.
		public static bool IsUnhealthy(TerrainType terrain, Player owner) {
			if (terrain == null || !terrain.causesDisease || terrain.diseaseStrength <= 0) {
				return false;
			}
			if (!terrain.diseaseCanBeCured || owner == null) {
				return true;
			}
			return !owner.HasTechEnabling(SaveTech.Flag.DisablesDiseases);
		}

		private static bool IsUnhealthy(Tile tile, Player owner) {
			return tile != null && tile != Tile.NONE && IsUnhealthy(tile.overlayTerrainType, owner);
		}

		// The strongest disease strength among the unhealthy tiles of the
		// city's radius, 0 if none.
		public static int Strength(City city) {
			int strongest = 0;
			int radius = city.owner?.rules?.MaxRankOfWorkableTiles ?? 2;
			foreach (Tile t in city.location.GetTilesWithinRankDistance(radius)) {
				if (IsUnhealthy(t, city.owner)) {
					strongest = System.Math.Max(strongest, t.overlayTerrainType.diseaseStrength);
				}
			}
			return strongest;
		}

		// The range of the city's roll for falling ill,
		// 0x300 - strength * 512 / 100 (at least 1), or 0 if it has no
		// unhealthy terrain and doesn't roll.
		public static int IllnessRange(City city) {
			int strength = Strength(city);
			if (strength <= 0) {
				return 0;
			}
			return System.Math.Max(1, IllnessBase - strength * IllnessStrengthScale / 100);
		}

		// Called once a turn for each city: a healthy city may fall ill; an
		// ill one loses a citizen and may recover (see above). Returns
		// whether it cost a citizen this turn.
		public static bool MaybeStrike(City city, int turn) {
			if (!city.ill) {
				int range = IllnessRange(city);
				// No draw without unhealthy terrain, so healthy cities leave
				// the random numbers alone.
				if (range <= 0 || GameData.rng.Next(range) >= city.residents.Count) {
					return false;
				}
				city.ill = true;
				return StrikeIfBigEnough(city, turn);
			}
			bool struck = StrikeIfBigEnough(city, turn);
			if (GameData.rng.Next(RecoveryRange) > city.residents.Count) {
				city.ill = false;
			}
			return struck;
		}

		private static bool StrikeIfBigEnough(City city, int turn) {
			if (city.residents.Count <= 1) {
				return false;
			}
			Strike(city);
			city.lastDiseaseTurn = turn;
			return true;
		}

		// Disease strikes the city: it loses a citizen, and its owner is
		// told.
		public static void Strike(City city) {
			if (city.residents.Count <= 1) {
				return;
			}
			CityResident victim = city.residents[city.residents.Count - 1];
			for (int i = city.residents.Count - 1; i >= 0; --i) {
				CityResident r = city.residents[i];
				if (r.tileWorked != city.location && IsUnhealthy(r.tileWorked, city.owner)) {
					victim = r;
					break;
				}
			}
			city.RemoveCitizen(victim);
			log.Information("Disease strikes {City}, now size {Size}", city.name, city.residents.Count);
			if (city.owner != null && city.owner.IsToldNews) {
				new MsgShowDomesticAdvisorPopup(city.owner, $"Disease strikes {city.name}.").send();
			}
		}

		// Whether disease can strike the unit where it stands: fortified in
		// jungle, outside a city, and not a worker.
		public static bool CanStrikeUnit(MapUnit unit) {
			if (!unit.isFortified || !unit.IsLandUnit() || unit.unitType.isWorker
					|| unit.location == null || unit.location == Tile.NONE || unit.location.HasCity()) {
				return false;
			}
			TerrainType terrain = unit.location.overlayTerrainType;
			if (terrain == null || !terrain.IsJungle) {
				return false;
			}
			return !terrain.diseaseCanBeCured || !unit.owner.HasTechEnabling(SaveTech.Flag.DisablesDiseases);
		}

		// Called once a turn for each player: each of its units fortified in
		// jungle may die of disease. Returns the units that died.
		public static List<MapUnit> StrikeFortifiedUnits(Player player) {
			List<MapUnit> dead = new();
			foreach (MapUnit unit in player.units.Where(CanStrikeUnit).ToList()) {
				if (GameData.rng.Next(UnitDeathRange) != 0) {
					continue;
				}
				log.Information("{Unit} of {Player} died of disease at {Tile}", unit, player, unit.location);
				dead.Add(unit);
				unit.RemoveFromPlay();
				if (player.IsToldNews) {
					new MsgShowMilitaryAdvisorPopup(player, $"Our {unit.unitType.name} has died of disease from the jungle!", happy: false).send();
				}
			}
			return dead;
		}
	}
}

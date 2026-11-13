using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using Serilog;

namespace C7Engine {
	// The values are sent between LAN machines as numbers, so new missions
	// go at the end. Propaganda took the place of the old Civ2-style
	// "Incite Revolt", which Civ3 doesn't have (see below).
	public enum EspionageMission {
		// Diplomatic missions. An embassy is established with a civ that has
		// been met; investigating a city and stealing technology need an
		// embassy with the target civ.
		EstablishEmbassy,
		InvestigateCity,
		StealTechnology,
		// Spy missions, which need the Intelligence Agency, an embassy with
		// the target and a spy planted there (Plant Spy itself needs only
		// the first two).
		SabotageProduction,
		Propaganda,
		PlantSpy,
		ExposeEnemySpy,
		StealWorldMap,
		StealPlans,
	}

	// How carefully a mission is carried out, the Espionage screen's
	// "operational cost options" (Conquests Civilopedia, GCON_Espionage;
	// script.txt #ESPIONAGE_SAFETY_LEVEL: "[Immediately] ($NUM0 gold - low
	// chance of success)", "[Carefully] ($NUM1 gold - moderate chance of
	// success)", "[Safely] ($NUM2 gold - high chance of success)"). Sent
	// between LAN machines by name.
	public enum EspionageRisk {
		Immediately,
		Carefully,
		Safely,
	}

	// Civ3's diplomatic and espionage missions, launched against another
	// civ, or one of its cities.
	//
	// Sources:
	// - The Conquests Civilopedia (GCON_Espionage, GCON_Espionage_Missions)
	//   lists the missions: Establish Embassy, Plant Spy, Steal Technology,
	//   Steal Military Plans ("reveals the locations of all their military
	//   units"), Initiate Propaganda ("If successful, the people will revolt
	//   and defect to your side"), Expose Enemy Spy ("If you suspect a rival
	//   has planted a spy in one of your cities you can attempt to expose the
	//   intruder"), Investigate City, Steal World Map and Sabotage Production
	//   ("destroy all the shields in the production box of a rival city").
	//   There is no Incite Revolt; neither the Civilopedia nor the study
	//   below lists one, so the old Civ2-style instant revolt is gone.
	// - Which missions need what: "To investigate cities and steal
	//   technology, all you need is an embassy"; the other five "you can
	//   only perform once you've built the Intelligence Agency (Small
	//   Wonder), and only against Civs with whom you've established an
	//   embassy and planted a spy", Steal Plans revealing "all troop
	//   locations of an enemy Civ for one turn"
	//   (https://forums.civfanatics.com/threads/successful-espionage.422226/).
	//   conquests.biq agrees: its ESPN section marks only Build an Embassy,
	//   Investigate City and Steal Technology as diplomat missions.
	// - Costs and chances are from "A Study of Espionage Missions"
	//   (https://civfanatics.com/civ3/strategy/game-mechanics/a-study-of-espionage-missions/,
	//   https://forums.civfanatics.com/threads/a-study-of-espionage-mission.97042/),
	//   with the base costs, propaganda culture factors and government
	//   modifiers it says come from the editor read from conquests.biq (its
	//   ESPN, CULT and GOVT sections), which match the study's tables.
	//
	// Our choices where the sources are silent are marked UNVERIFIED below.
	// A mission is sent immediately, carefully or safely (EspionageRisk), the
	// latter two costing more for better odds (see RiskCost and
	// GetawayPercent). The game has no spy units, so a mission is sent by the
	// embassy (a "diplomat") or by the planted spy, whose experience comes
	// from the sender's government.
	public static class Espionage {
		private static readonly ILogger log = Log.ForContext(typeof(Espionage));

		// The name of the tech Civ3 needs for embassies. The rule itself
		// follows the BIQ's "enables diplomats" flag (Tech.FindEmbassyTech).
		public const string EmbassyTechName = "Writing";

		// The distance used when either civ has no cities to measure from.
		private const int UnknownDistance = 20;

		// Who carries out a mission: the embassy, or a planted spy of some
		// experience (the study's "Embassy", "Regular spy" and "Veteran
		// spy" rows).
		public enum Agent {
			Embassy,
			RegularSpy,
			VeteranSpy,
		}

		public class MissionOption {
			public EspionageMission mission;
			public bool available;
			// Why the mission isn't available, if it isn't.
			public string reason;
			public int cost;
			public int successPercent;
			// The risk the cost and chance are for.
			public EspionageRisk risk;
		}

		// What an investigated city reveals, as it was on the turn it was
		// investigated.
		public class CityReport {
			public int turn;
			public string cityName;
			public int size;
			public string producing;
			public int shieldsStored;
			public List<string> buildings = new();
			public List<string> units = new();
			public int ownerGold;
		}

		public class MissionResult {
			public EspionageMission mission;
			public bool performed;
			public bool success;
			// Whether the agents were caught (only for a failed mission).
			public bool caught;
			// Whether the agent of a successful mission was exposed and lost
			// anyway (see ExposedAfterSuccessPercent).
			public bool exposed;
			public int goldSpent;
			public string message;
			public Tech stolenTech;
			public CityReport report;
			// The capital an embassy was established in, or the investigated
			// city, which the player is shown on the city screen.
			public City city;
			public Player exposedSpyOwner;
			// Propaganda: how many citizens were persuaded.
			public int persuaded;
			// Steal World Map: how many tiles we learnt of. Steal Plans: the
			// tiles their military units are on.
			public int tilesRevealed;
			public List<Tile> unitTiles;
			// The sender's report and the target's warning are one event,
			// told once to a spectator watching every civ.
			public NewsEvent newsEvent = new();
		}

		public static string Describe(EspionageMission mission) {
			return mission switch {
				EspionageMission.EstablishEmbassy => "Establish Embassy",
				EspionageMission.InvestigateCity => "Investigate City",
				EspionageMission.StealTechnology => "Steal Technology",
				EspionageMission.SabotageProduction => "Sabotage Production",
				EspionageMission.Propaganda => "Initiate Propaganda",
				EspionageMission.PlantSpy => "Plant Spy",
				EspionageMission.ExposeEnemySpy => "Expose Enemy Spy",
				EspionageMission.StealWorldMap => "Steal World Map",
				EspionageMission.StealPlans => "Steal Plans",
				_ => mission.ToString(),
			};
		}

		// Whether the mission is aimed at a city rather than a whole civ.
		public static bool TargetsCity(EspionageMission mission) {
			return mission is EspionageMission.InvestigateCity
				or EspionageMission.SabotageProduction or EspionageMission.Propaganda;
		}

		// The missions that need the Intelligence Agency.
		public static bool IsEspionageMission(EspionageMission mission) {
			return mission is EspionageMission.PlantSpy or EspionageMission.ExposeEnemySpy
				or EspionageMission.SabotageProduction or EspionageMission.Propaganda
				or EspionageMission.StealWorldMap or EspionageMission.StealPlans;
		}

		// The missions that need a spy planted in the target civ.
		public static bool NeedsSpy(EspionageMission mission) {
			return IsEspionageMission(mission) && mission != EspionageMission.PlantSpy;
		}

		public static City Capital(Player player) {
			return player.cities.Find(c => c.IsCapital()) ?? player.cities.FirstOrDefault();
		}

		// Whether the player owns a building that allows spy missions (the
		// Intelligence Agency).
		public static bool HasIntelligenceAgency(Player player) {
			foreach (City c in player.cities) {
				foreach (CityBuilding cb in c.constructed_buildings) {
					if (cb.building.allowsSpyMissions && cb.building.TakesEffectFor(player)) {
						return true;
					}
				}
			}
			return false;
		}

		public static bool HasEmbassy(Player actor, Player target) {
			return PlayerRelationship.TryGetRelationship(actor, target, out var pr) && pr.hasEmbassy;
		}

		public static bool HasSpy(Player actor, Player target) {
			return PlayerRelationship.TryGetRelationship(actor, target, out var pr) && pr.hasSpy;
		}

		// The base costs of conquests.biq's ESPN section, which are the
		// study's "Base" column.
		public static int BaseCost(EspionageMission mission) {
			return mission switch {
				EspionageMission.EstablishEmbassy => 20,
				EspionageMission.InvestigateCity => 10,
				EspionageMission.StealTechnology => 10,
				EspionageMission.StealWorldMap => 1,
				EspionageMission.PlantSpy => 60,
				EspionageMission.StealPlans => 10,
				EspionageMission.Propaganda => 100,
				EspionageMission.SabotageProduction => 10,
				EspionageMission.ExposeEnemySpy => 80,
				_ => 0,
			};
		}

		// The experience of a civ's planted spies: conquests.biq's GOVT
		// "spies are" field is veteran for Communism and Fascism and regular
		// for the rest. The ruleset doesn't carry the field, so it goes by
		// the government's name.
		public static Agent SpyLevel(Player player) {
			return player.government?.name is "Communism" or "Fascism" ? Agent.VeteranSpy : Agent.RegularSpy;
		}

		// Who sends the mission: the planted spy if there is one, else the
		// embassy.
		public static Agent AgentFor(Player actor, Player target, EspionageMission mission) {
			if (mission == EspionageMission.PlantSpy || NeedsSpy(mission) || HasSpy(actor, target)) {
				return SpyLevel(actor);
			}
			return Agent.Embassy;
		}

		// The city a mission is measured against: the target city, or the
		// target's capital for a mission against the civ.
		private static City MeasuredCity(Player target, City city) => city ?? Capital(target);

		// The study's "dist": from the city (or capital) to the actor's
		// nearest city. UNVERIFIED: measured as the game measures tile
		// distances (Tile.DistanceTo).
		public static int Distance(Player actor, Player target, City city) {
			City to = MeasuredCity(target, city);
			if (to == null || actor.cities.Count == 0) {
				return UnknownDistance;
			}
			return actor.cities.Min(c => c.location.DistanceTo(to.location));
		}

		// The study's "level": 1 for a town, 2 for a city, 3 for a metropolis.
		public static int SettlementLevel(City city) {
			if (city == null) {
				return 1;
			}
			Rules rules = city.owner.rules;
			if (city.residents.Count <= rules.MaximumLevel1CitySize) {
				return 1;
			}
			return city.residents.Count <= rules.MaximumLevel2CitySize ? 2 : 3;
		}

		private static int TotalCulture(Player player) => Math.Max(0, CultureReport.TotalCulture(player));

		// The study's cost formulas, "dist*level + base ..." (for missions
		// sent immediately):
		//   Build an Embassy      dist*level + base + pop
		//   Investigate City      dist*level + base * pop
		//   Steal Technology      dist*level + base * techs*techrate/100
		//   Steal World Map       dist*level + base * mapsize
		//   Plant Spy             dist*level + base
		//   Steal Plans           dist*level + base * units
		//   Initiate Propaganda   dist*level + base * pop * (1 + opp_cult/your_cult)
		//                         (plus the editor help's gold term, below)
		//   Sabotage Production   dist*level + base * shield
		//   Expose Enemy Spy      dist*level + base
		// pop and level are of the target city, or the capital; techs is how
		// many advances the target knows; techrate is the world size's
		// factor; mapsize is the average of the map's width and height;
		// units is the target's number of units; shield is the shields the
		// city has stored. UNVERIFIED: the rounding (fractions are dropped
		// at the end) and, for a civ with no culture, taking its culture as 1.
		public static int Cost(GameData gameData, Player actor, EspionageMission mission, Player target, City city,
				EspionageRisk risk = EspionageRisk.Immediately) {
			return RiskCost(ImmediateCost(gameData, actor, mission, target, city), RiskFor(mission, risk));
		}

		// "Carefully" costs 3/2 of "immediately" and "safely" twice as much
		// (Oystein, https://forums.civfanatics.com/threads/a-study-of-espionage-mission.97042/
		// page 3: "3/2 for carefully and 2 for" safely; Spoonwood, page 4,
		// tested: "safe steal attempts cost exactly twice of an immediate
		// steal attempt"; compendium 06, section 6.11). UNVERIFIED: the
		// rounding (down).
		public static int RiskCost(int immediateCost, EspionageRisk risk) {
			long cost = risk switch {
				EspionageRisk.Carefully => (long)immediateCost * 3 / 2,
				EspionageRisk.Safely => (long)immediateCost * 2,
				_ => immediateCost,
			};
			return (int)Math.Min(cost, int.MaxValue);
		}

		// Whether how carefully the mission is sent changes its odds: only for
		// the missions whose agents can be caught getting away (the study's
		// escape table). Build an Embassy and Investigate City "always
		// succeed", propaganda is never caught, and the study gives Plant Spy
		// its own chances with no risk column. UNVERIFIED: that Civ3 offers
		// no choice of risk for these; we send them immediately, since more
		// gold would buy nothing.
		public static bool RiskMatters(EspionageMission mission) {
			return mission is EspionageMission.StealTechnology or EspionageMission.StealWorldMap
				or EspionageMission.StealPlans or EspionageMission.SabotageProduction or EspionageMission.ExposeEnemySpy;
		}

		// The risk a mission is actually sent at: the one asked for, where it
		// matters.
		public static EspionageRisk RiskFor(EspionageMission mission, EspionageRisk risk) {
			return RiskMatters(mission) && Enum.IsDefined(risk) ? risk : EspionageRisk.Immediately;
		}

		private static int ImmediateCost(GameData gameData, Player actor, EspionageMission mission, Player target, City city) {
			City measured = MeasuredCity(target, city);
			int pop = measured?.residents.Count ?? 1;
			long cost = (long)Distance(actor, target, city) * SettlementLevel(measured);
			int b = BaseCost(mission);
			switch (mission) {
				case EspionageMission.EstablishEmbassy:
					cost += b + pop;
					break;
				case EspionageMission.InvestigateCity:
					cost += b * pop;
					break;
				case EspionageMission.StealTechnology: {
						// UNVERIFIED: a map without a world size's tech rate
						// counts as 100 (no change).
						int techRate = gameData.map?.techRate > 0 ? gameData.map.techRate : 100;
						cost += (long)b * target.knownTechs.Count * techRate / 100;
						break;
					}
				case EspionageMission.StealWorldMap: {
						int mapSize = gameData.map == null ? 0 : (gameData.map.numTilesWide + gameData.map.numTilesTall) / 2;
						cost += (long)b * mapSize;
						break;
					}
				case EspionageMission.StealPlans:
					cost += (long)b * target.units.Count;
					break;
				case EspionageMission.Propaganda: {
						// The editor help adds a gold term the study lacks:
						// "BaseCost * Target City Size + ( BaseCost * Target
						// City Size * Culture Ratio) + ( ( BaseCost * Target
						// City Size) / 2 * Gold Ratio )" (Conquests editor
						// help, "Base Cost (Diplomat / Spy Mission
						// Properties)"; compendium 06, section 6.11).
						// UNVERIFIED: that the gold ratio is the target's
						// treasury over ours, as the culture ratio is theirs
						// over ours ("opp_cult/your_cult"), with an empty
						// treasury counted as 1 gold.
						int theirs = TotalCulture(target);
						int ours = Math.Max(1, TotalCulture(actor));
						cost += (long)b * pop * (ours + theirs) / ours;
						long theirGold = Math.Max(0, target.gold);
						long ourGold = Math.Max(1, actor.gold);
						cost += (long)b * pop / 2 * theirGold / ourGold;
						break;
					}
				case EspionageMission.SabotageProduction:
					cost += (long)b * Math.Max(0, city?.shieldsStored ?? 0);
					break;
				case EspionageMission.PlantSpy:
				case EspionageMission.ExposeEnemySpy:
					cost += b;
					break;
				default:
					return 0;
			}
			// The final price: "((2*size - nativesOfSpyCiv + 1) * (cost +
			// (sizeClass+1)*DAT_009e5ce4)) / (2*size + 1): a city full of
			// citizens of the spy's nationality is much cheaper to work on"
			// (Perez's decompile of Civ3 Conquests,
			// doc/perez-findings/Civ3_Findings.md, Round 2d "Espionage";
			// authoritative per the owner's 2026-11-12 ruling). The decompile
			// doesn't name DAT_009e5ce4; (sizeClass+1) is the study's
			// "level", and the editor help's "further modified, taking into
			// consideration ... the distance of the target, the target city's
			// population level, the ratio of citizens between the initiating
			// civ and the target civ" (compendium 06, section 6.11) makes it
			// the study's "dist". UNVERIFIED: that it is the distance as
			// Distance measures it.
			int natives = measured?.residents.Count(r => r.nationality == actor.civilization) ?? 0;
			cost = cost * (2L * pop - natives + 1) / (2L * pop + 1);
			return (int)Math.Clamp(cost, 0, int.MaxValue);
		}

		// The study's chance, in per cent, of a planted spy getting away
		// from a theft, sabotage or exposing mission, sent immediately,
		// carefully or safely: embassy 40/55/65, regular spy 60/75/85,
		// veteran spy 70/85/95 (confirmed by Kool Keith and Jazzmail,
		// https://forums.civfanatics.com/threads/steal-technology.232487/post-5709659).
		public static int GetawayPercent(Agent agent, EspionageRisk risk = EspionageRisk.Immediately) {
			int immediate = agent switch {
				Agent.Embassy => 40,
				Agent.RegularSpy => 60,
				Agent.VeteranSpy => 70,
				_ => 40,
			};
			return immediate + risk switch {
				EspionageRisk.Carefully => 15,
				EspionageRisk.Safely => 25,
				_ => 0,
			};
		}

		// "There is an 80% chance of succeeding if not getting caught." Only
		// for Steal Technology and Steal World Map: "after a successful first
		// roll, `Rand(100) < 80` completes the mission; the remaining 20% give
		// "mission failed" without being caught" (Perez's decompile of Civ3
		// Conquests, doc/perez-findings/Civ3_Findings.md, Round 2d
		// "Espionage"; authoritative per the owner's 2026-11-12 ruling).
		// Steal Plans, Sabotage Production and Expose Enemy Spy use the
		// success roll alone, and their agents may be exposed afterwards
		// (ExposedAfterSuccessPercent).
		public const int SuccessIfNotCaughtPercent = 80;

		// Whether a mission has the second, 80% roll (see
		// SuccessIfNotCaughtPercent), so it can fail without its agents being
		// caught.
		public static bool HasSecondRoll(EspionageMission mission) {
			return mission is EspionageMission.StealTechnology or EspionageMission.StealWorldMap;
		}

		// The chance, in per cent, that the agent of a successful mission is
		// exposed and lost anyway: "Exposed afterwards (the spy is lost even
		// though the mission worked): Steal Plans 10%, Sabotage Production
		// 20%, Expose Mole 10%" (Perez's decompile, Round 2d; owner's ruling
		// 2026-11-12). Civ3 has its own messages for it (script.txt,
		// #ESPIONAGE_MISSION_STEAL_PLANS_SUCCESS_EXPOSED,
		// #ESPIONAGE_MISSION_SABOTAGE_PRODUCTION_SUCCESS_EXPOSED,
		// #ESPIONAGE_MISSION_EXPOSE_MOLE_SUCCESS_EXPOSED).
		public static int ExposedAfterSuccessPercent(EspionageMission mission) {
			return mission switch {
				EspionageMission.StealPlans => 10,
				EspionageMission.SabotageProduction => 20,
				EspionageMission.ExposeEnemySpy => 10,
				_ => 0,
			};
		}

		// The study's chances of planting a spy: regular 50%, veteran 60%.
		// (It also gives, in brackets, conscript 20% and elite 75%, which no
		// government's spies are in Conquests.)
		public static int PlantSpyPercent(Agent agent) => agent == Agent.VeteranSpy ? 60 : 50;

		public static int SuccessPercent(GameData gameData, Player actor, EspionageMission mission, Player target, City city,
				EspionageRisk risk = EspionageRisk.Immediately) {
			risk = RiskFor(mission, risk);
			switch (mission) {
				case EspionageMission.EstablishEmbassy:
				case EspionageMission.InvestigateCity:
					// "Build an Embassy and Investigate City always succeed."
					return 100;
				case EspionageMission.PlantSpy:
					return PlantSpyPercent(SpyLevel(actor));
				case EspionageMission.Propaganda:
					return city == null ? 0 : (int)Math.Round(100 * PropagandaFlipChance(gameData, actor, city));
				case EspionageMission.StealTechnology:
				case EspionageMission.StealWorldMap:
				case EspionageMission.StealPlans:
				case EspionageMission.SabotageProduction:
				case EspionageMission.ExposeEnemySpy:
					// Expose Enemy Spy has "the same probabilities ... except
					// you always fail if the enemy does not have a spy".
					if (mission == EspionageMission.ExposeEnemySpy && !HasSpy(target, actor)) {
						return 0;
					}
					int getaway = GetawayPercent(AgentFor(actor, target, mission), risk);
					return HasSecondRoll(mission) ? getaway * SuccessIfNotCaughtPercent / 100 : getaway;
				default:
					return 0;
			}
		}

		// Propaganda's "government" term: conquests.biq's GOVT bribery
		// modifiers (the study's "Government modifier" table), by the
		// sender's government (row) and the target's (column), in the order
		// Anarchy, Despotism, Monarchy, Communism, Republic, Democracy,
		// Fascism, Feudalism. The ruleset doesn't carry them, so they go by
		// name; other governments count 0 (UNVERIFIED).
		private static readonly string[] GovernmentOrder = [
			"Anarchy", "Despotism", "Monarchy", "Communism", "Republic", "Democracy", "Fascism", "Feudalism",
		];
		private static readonly int[,] GovernmentModifiers = {
			{ 0, 0, 0, 0, 0, 0, 0, 0 },
			{ 15, 0, -10, -15, -20, -30, 0, 0 },
			{ 20, 15, 0, -5, -10, -25, 0, 0 },
			{ 25, 20, 5, 0, -10, -20, 0, 0 },
			{ 30, 25, 10, 8, 0, -5, 0, 0 },
			{ 35, 30, 20, 10, 5, 0, 0, 0 },
			{ 0, 0, -5, -20, -10, -10, 0, 0 },
			{ 20, 15, 0, -10, -10, -20, -25, 0 },
		};

		public static int GovernmentModifier(Player actor, Player target) {
			int row = Array.IndexOf(GovernmentOrder, actor.government?.name);
			int column = Array.IndexOf(GovernmentOrder, target.government?.name);
			return row < 0 || column < 0 ? 0 : GovernmentModifiers[row, column];
		}

		// "Democracy is immune against propaganda" (the study; conquests.biq
		// has Democracy immune to the Initiate Propaganda mission).
		public static bool ImmuneToPropaganda(Player target) => target.government?.name == "Democracy";

		// Propaganda's "culture" term: conquests.biq's CULT "chance of
		// successful propaganda" by how the target regards the sender's
		// culture (the study's "Culture factors"): in awe of 30, admirers of
		// 25, impressed with 20, unimpressed by 10, dismissive of 5,
		// disdainful of 3. UNVERIFIED: whose opinion; we take the target
		// civ's of the sender's culture, as the culture advisor reports it.
		private static readonly int[] CultureFactors = [30, 25, 20, 10, 5, 3];

		public static int CultureFactor(GameData gameData, Player actor, Player target) {
			string opinion = CultureReport.OpinionOf(gameData.rules, TotalCulture(target), TotalCulture(actor));
			int index = gameData.rules.CultureOpinions.FindIndex(o => o.Name == opinion);
			return index >= 0 && index < CultureFactors.Length ? CultureFactors[index] : 0;
		}

		// The study's chance, in per cent, of persuading a citizen:
		//   spy + government + culture - 5 * units - improvement - capital + nationality
		// spy is 10 for a veteran spy, 0 for a regular one; units is the
		// garrison; improvement is 20 with a courthouse; capital is 40 for
		// the capital; nationality is 20 for a citizen of the sender's
		// nationality. UNVERIFIED: the garrison counts every unit of the
		// owner in the city, and a courthouse is a building that reduces
		// corruption other than the palace (the ruleset doesn't carry the
		// BIQ's "resistant to bribery" flag).
		public static int PersuasionPercent(GameData gameData, Player actor, City city, CityResident resident) {
			Player target = city.owner;
			int chance = SpyLevel(actor) == Agent.VeteranSpy ? 10 : 0;
			chance += GovernmentModifier(actor, target);
			chance += CultureFactor(gameData, actor, target);
			chance -= 5 * city.location.unitsOnTile.Count(u => u.owner == target);
			if (city.constructed_buildings.Any(cb => cb.building.reducesCorruption && !cb.building.isCenterOfEmpire && cb.building.TakesEffectFor(city.owner))) {
				chance -= 20;
			}
			if (city.IsCapital()) {
				chance -= 40;
			}
			if (resident.nationality == actor.civilization) {
				chance += 20;
			}
			return Math.Clamp(chance, 0, 100);
		}

		// "If at least 3/4 of the citizen are persuading, the city will flip."
		public static bool PropagandaFlips(int persuaded, int size) => size > 0 && 4 * persuaded >= 3 * size;

		// The chance the city flips, from each citizen's chance of being
		// persuaded.
		public static double PropagandaFlipChance(GameData gameData, Player actor, City city) {
			int size = city.residents.Count;
			if (size == 0) {
				return 0;
			}
			// The chances of persuading exactly k citizens.
			double[] exactly = new double[size + 1];
			exactly[0] = 1;
			int n = 0;
			foreach (CityResident resident in city.residents) {
				double p = PersuasionPercent(gameData, actor, city, resident) / 100.0;
				n++;
				for (int k = n; k >= 0; k--) {
					exactly[k] = exactly[k] * (1 - p) + (k > 0 ? exactly[k - 1] * p : 0);
				}
			}
			double flip = 0;
			for (int k = 0; k <= size; k++) {
				if (PropagandaFlips(k, size)) {
					flip += exactly[k];
				}
			}
			return flip;
		}

		// How many cities a player has: a LAN guest isn't sent those it
		// hasn't seen, so the host counts them.
		private static int CityCount(Player player) => player.hostFacts?.cities ?? player.cities.Count;

		// Why the mission can't be performed, or null if it can.
		public static string Unavailable(GameData gameData, Player actor, EspionageMission mission, Player target, City city,
				EspionageRisk risk = EspionageRisk.Immediately) {
			// A LAN client's message can carry any number as the mission.
			if (!Enum.IsDefined(mission)) {
				return "There is no such mission.";
			}
			if (actor == null || target == null || actor == target || target.IsIndependent || target.defeated) {
				return "There is no one to send a mission to.";
			}
			if (!PlayerRelationship.TryGetRelationship(actor, target, out PlayerRelationship pr)) {
				return $"We have not met the {target.civilization.noun}.";
			}
			if (TargetsCity(mission)) {
				if (city == null || city.owner != target || !gameData.cities.Contains(city)) {
					return "That city doesn't belong to them.";
				}
				if (!actor.tileKnowledge.isTileKnown(city.location)) {
					return "We don't know where that city is.";
				}
			}
			if (actor.cities.Count == 0) {
				return "We have no cities to send a mission from.";
			}

			if (mission == EspionageMission.EstablishEmbassy) {
				if (pr.hasEmbassy) return "We already have an embassy with them.";
				if (pr.AtWar()) return "They won't receive an ambassador while we are at war.";
				if (CityCount(target) == 0) return "They have no capital to send an ambassador to.";
				Tech embassyTech = Tech.FindEmbassyTech(gameData.techs);
				if (embassyTech != null && !actor.knownTechs.Contains(embassyTech.id)) {
					return $"We need {embassyTech.Name} to establish embassies.";
				}
			} else {
				// War closes the embassy, so the embassy's missions
				// (Investigate City, Steal Technology, Plant Spy) stop, but
				// "Even if you go to war and your embassy is closed, your spy
				// will continue to function as long as it is not caught"
				// (Civ3.com FAQ, quoted at
				// https://apolyton.net/forum/miscellaneous/archives/civ3-general-archive/41397-espionage-you-can-t-steal-techs-from-civs-you-are-at-war-with).
				// So the spy's own missions need the embassy only to plant
				// it.
				if (!pr.hasEmbassy && !(NeedsSpy(mission) && pr.hasSpy)) {
					return pr.AtWar() ? "Our embassy was closed by the war." : "We need an embassy with them first.";
				}
				if (IsEspionageMission(mission) && !HasIntelligenceAgency(actor)) {
					return "We need an Intelligence Agency for espionage.";
				}
				if (NeedsSpy(mission) && !pr.hasSpy) return "We need a spy in their capital first.";
				switch (mission) {
					case EspionageMission.PlantSpy:
						if (pr.hasSpy) return "We already have a spy in their capital.";
						// "If a spy plant fails you will get a flag saying so.
						// You can't plant a spy when flagged."
						if (pr.spyPlantFailed) return "Our last attempt to plant a spy failed; they are on their guard.";
						if (CityCount(target) == 0) return "They have no capital.";
						break;
					case EspionageMission.StealTechnology:
						if (StealableTechs(gameData, actor, target).Count == 0) return "They know nothing we could steal.";
						break;
					case EspionageMission.Propaganda:
						if (ImmuneToPropaganda(target)) return $"The {target.civilization.noun} government is immune to propaganda.";
						break;
				}
			}

			int cost = Cost(gameData, actor, mission, target, city, risk);
			if (actor.gold < cost) {
				return $"We can't afford the {cost} gold it would cost.";
			}
			return null;
		}

		// The techs the actor could steal from the target.
		public static List<Tech> StealableTechs(GameData gameData, Player actor, Player target) {
			return gameData.techs.Where(t => target.knownTechs.Contains(t.id) && !actor.knownTechs.Contains(t.id)
				&& t.Prerequisites.All(p => actor.knownTechs.Contains(p.id))).ToList();
		}

		public static MissionOption GetOption(GameData gameData, Player actor, EspionageMission mission, Player target, City city,
				EspionageRisk risk = EspionageRisk.Immediately) {
			risk = RiskFor(mission, risk);
			string reason = Unavailable(gameData, actor, mission, target, city, risk);
			bool known = target != null && actor != null && target != actor
				&& PlayerRelationship.TryGetRelationship(actor, target, out _)
				&& (!TargetsCity(mission) || city != null);
			return new MissionOption {
				mission = mission,
				available = reason == null,
				reason = reason,
				cost = known ? Cost(gameData, actor, mission, target, city, risk) : 0,
				successPercent = known ? SuccessPercent(gameData, actor, mission, target, city, risk) : 0,
				risk = risk,
			};
		}

		// The missions that can be listed for a target: the civ missions, and
		// the city missions too when a city is given.
		public static List<MissionOption> GetOptions(GameData gameData, Player actor, Player target, City city) {
			List<MissionOption> result = new();
			foreach (EspionageMission mission in ListedOrder) {
				if (TargetsCity(mission) && city == null) {
					continue;
				}
				result.Add(GetOption(gameData, actor, mission, target, TargetsCity(mission) ? city : null));
			}
			return result;
		}

		// The missions in the order the Espionage screen lists them.
		public static IReadOnlyList<EspionageMission> Missions => ListedOrder;

		// The Civilopedia's order, diplomatic missions first.
		private static readonly EspionageMission[] ListedOrder = [
			EspionageMission.EstablishEmbassy, EspionageMission.InvestigateCity, EspionageMission.StealTechnology,
			EspionageMission.PlantSpy, EspionageMission.StealPlans, EspionageMission.StealWorldMap,
			EspionageMission.SabotageProduction, EspionageMission.Propaganda, EspionageMission.ExposeEnemySpy,
		];

		// Performs the mission if possible. The roll, 0-99, is random if not
		// given. For a mission that can be caught, below the success chance
		// succeeds, below the chance of getting away fails unnoticed (only
		// for the missions with a second roll, see HasSecondRoll), and
		// otherwise the agents are caught. For propaganda it is each
		// citizen's roll against their chance of being persuaded. The
		// exposedRoll, 0-99 and random if not given, decides whether the
		// agent of a successful mission is exposed afterwards (see
		// ExposedAfterSuccessPercent).
		public static MissionResult Perform(GameData gameData, Player actor, EspionageMission mission, Player target, City city, int? roll = null,
				EspionageRisk risk = EspionageRisk.Immediately, int? exposedRoll = null) {
			if (!TargetsCity(mission)) {
				city = null;
			}
			risk = RiskFor(mission, risk);
			MissionResult result = new() { mission = mission };
			string reason = Unavailable(gameData, actor, mission, target, city, risk);
			if (reason != null) {
				result.message = reason;
				return result;
			}

			int cost = Cost(gameData, actor, mission, target, city, risk);
			Agent agent = AgentFor(actor, target, mission);
			actor.gold -= cost;
			result.performed = true;
			result.goldSpent = cost;

			PlayerRelationship ours = actor.playerRelationships[target.id];
			PlayerRelationship theirs = target.playerRelationships[actor.id];

			if (mission == EspionageMission.Propaganda) {
				// "You will never get caught."
				int persuaded = city.residents.Count(r => (roll ?? GameData.rng.Next(100)) < PersuasionPercent(gameData, actor, city, r));
				ApplyPropaganda(gameData, actor, city, persuaded, result);
				return result;
			}

			int chance = SuccessPercent(gameData, actor, mission, target, city, risk);
			// Only the missions with a second roll can fail unnoticed; for the
			// others, failing the success roll is being caught.
			int getaway = mission switch {
				EspionageMission.EstablishEmbassy or EspionageMission.InvestigateCity => 100,
				_ when HasSecondRoll(mission) => GetawayPercent(agent, risk),
				_ => chance,
			};
			int r = roll ?? GameData.rng.Next(100);
			result.success = r < chance;

			if (!result.success) {
				// Expose Enemy Spy "always fail[s] if the enemy does not have
				// a spy". UNVERIFIED: our spy isn't caught then.
				result.caught = r >= getaway && !(mission == EspionageMission.ExposeEnemySpy && !HasSpy(target, actor));
				if (mission == EspionageMission.PlantSpy) {
					ours.spyPlantFailed = true;
				}
				if (!result.caught) {
					result.message = mission == EspionageMission.ExposeEnemySpy && !HasSpy(target, actor)
						? $"Our agents found no {target.civilization.adjective ?? target.civilization.noun} spy among us."
						: $"Our {Describe(mission)} mission against the {target.civilization.noun} failed, but our agents got away.";
					log.Information("{Actor}'s {Mission} against {Target} failed unnoticed", actor, mission, target);
					return result;
				}
				// A caught mission is a diplomatic incident, and a caught spy
				// is lost: "You can plant a spy the same turn your spy has
				// been exposed by a failed mission."
				// UNVERIFIED: a failed plant is an incident too.
				theirs.espionageIncidents++;
				if (agent != Agent.Embassy && mission != EspionageMission.PlantSpy) {
					ours.hasSpy = false;
				}
				result.message = $"Our {Describe(mission)} mission against the {target.civilization.noun} failed, and our agents were caught!";
				NotifyTarget(target, result.newsEvent, $"We caught {actor.civilization.adjective ?? actor.civilization.noun} agents attempting to {Describe(mission).ToLowerInvariant()}"
					+ (city != null ? $" in {city.name}" : "") + "!");
				log.Information("{Actor}'s {Mission} against {Target} was caught", actor, mission, target);
				return result;
			}

			// Whether the agent is exposed although the mission worked (see
			// ExposedAfterSuccessPercent). Civ3's messages for it end
			// "$TITLE $LEADER is not pleased", so it counts as an incident
			// (below). UNVERIFIED: that it is one, as a caught mission is.
			int exposedChance = ExposedAfterSuccessPercent(mission);
			result.exposed = exposedChance > 0 && (exposedRoll ?? GameData.rng.Next(100)) < exposedChance;
			string actorAdjective = actor.civilization.adjective ?? actor.civilization.noun;

			switch (mission) {
				case EspionageMission.EstablishEmbassy: {
						// The embassy reports on the capital as it is now, and
						// shows us the land around it.
						City capital = Capital(target);
						ours.hasEmbassy = true;
						ours.embassyReport = Investigate(capital, gameData.turn);
						RevealCityRadius(actor, capital);
						result.report = ours.embassyReport;
						result.city = capital;
						result.message = $"We have established an embassy with the {target.civilization.noun} in {capital.name}.";
						// The embassy is the actor's alone: the target gets
						// none with the actor, but is told, as agreements
						// with the actor are now open to it too (see
						// Pacts.HasEmbassyEitherWay). Civ3's words
						// (script.txt, #ESPIONAGE_FOREIGN_EMBASSY_ESTABLISHED,
						// "#advisor Foreign Happy"): "$TITLE0, $LEADER1 of the
						// $CIVNOUN2 has established an embassy in our capital
						// city."
						if (target.IsToldNews) {
							string title = target.civilization.leaderTitle;
							string leader = actor.civilization.leader;
							string who = string.IsNullOrEmpty(leader)
								? $"the {actor.civilization.noun} have"
								: $"{leader} of the {actor.civilization.noun} has";
							string message = string.IsNullOrEmpty(title)
								? $"{char.ToUpperInvariant(who[0])}{who[1..]} established an embassy in our capital city."
								: $"{title}, {who} established an embassy in our capital city.";
							new MsgShowForeignAdvisorPopup(target, message, happy: true) { newsEvent = result.newsEvent }.send();
						}
						break;
					}
				case EspionageMission.InvestigateCity:
					// As in Civ3, the city is then shown on the city screen.
					// Civ3's words (script.txt,
					// #ESPIONAGE_MISSION_INVESTIGATE_CITY_SUCCESS): "We have
					// conducted an investigation of the city of $CITY1."
					result.report = Investigate(city, gameData.turn);
					result.city = city;
					result.message = $"We have conducted an investigation of the city of {city.name}.";
					break;
				case EspionageMission.StealTechnology: {
						List<Tech> stealable = StealableTechs(gameData, actor, target);
						Tech tech = stealable[GameData.rng.Next(stealable.Count)];
						actor.AcquireTech(gameData, tech);
						result.stolenTech = tech;
						result.message = $"Our agents among the {target.civilization.noun} have stolen the secrets of {tech.Name}!";
						break;
					}
				case EspionageMission.SabotageProduction:
					city.SetStoredShields(0);
					result.message = $"Our agents have sabotaged production in {city.name}.";
					// Civ3's words when exposed (script.txt,
					// #ESPIONAGE_MISSION_SABOTAGE_PRODUCTION_SUCCESS_EXPOSED):
					// "Unfortunately, our agent (known only as 'Gweeto') was
					// [caught in the act] and removed."; the target's
					// (#ESPIONAGE_CAUGHT_SABOTAGING_SUCCESS): "Production in
					// $CITY3 has been sabotaged! A spy of $TITLE0 $LEADER1 of
					// the $CIVNOUN2 was caught."
					if (result.exposed) {
						result.message += " Unfortunately, our agent was caught in the act and removed.";
					}
					NotifyTarget(target, result.newsEvent, $"Saboteurs have destroyed the production in {city.name}!"
						+ (result.exposed ? $" We caught the {actorAdjective} spy responsible." : ""));
					break;
				case EspionageMission.PlantSpy:
					ours.hasSpy = true;
					result.message = $"We have planted a spy in the {target.civilization.noun} capital.";
					break;
				case EspionageMission.ExposeEnemySpy:
					// Their spy in our capital is uncovered. UNVERIFIED: we
					// remember it as an incident, as they would ours.
					theirs.hasSpy = false;
					ours.espionageIncidents++;
					result.exposedSpyOwner = target;
					result.message = $"We have exposed a {target.civilization.adjective ?? target.civilization.noun} spy in our capital!";
					// Civ3's words when exposed (script.txt,
					// #ESPIONAGE_MISSION_EXPOSE_MOLE_SUCCESS_EXPOSED): "The
					// threat has been neutralized but our agent's affiliation
					// was compromised."
					if (result.exposed) {
						result.message += " The threat has been neutralized, but our agent's affiliation was compromised.";
					}
					NotifyTarget(target, result.newsEvent, $"Our spy among the {actor.civilization.noun} has been exposed!"
						+ (result.exposed ? $" We have caught the {actorAdjective} agent who exposed them." : ""));
					break;
				case EspionageMission.StealWorldMap:
					result.tilesRevealed = StealWorldMap(actor, target);
					result.message = $"Our spy has stolen the {target.civilization.adjective ?? target.civilization.noun} world map, showing us {result.tilesRevealed} new tiles.";
					break;
				case EspionageMission.StealPlans:
					result.unitTiles = StealPlans(actor, target);
					result.message = $"Our spy has stolen the {target.civilization.adjective ?? target.civilization.noun} military plans: their troops are shown on the map until next turn.";
					// Civ3's words when exposed (script.txt,
					// #ESPIONAGE_MISSION_STEAL_PLANS_SUCCESS_EXPOSED):
					// "However, our agent was caught and executed."; the
					// target's (#ESPIONAGE_CAUGHT_STEALING_PLANS_SUCCESS): "Our
					// military plans have been stolen! An agent of $TITLE0
					// $LEADER1 of the $CIVNOUN2 was caught."
					if (result.exposed) {
						result.message += " However, our agent was caught and executed.";
						NotifyTarget(target, result.newsEvent, $"Our military plans have been stolen! We caught the {actorAdjective} agent responsible.");
					}
					break;
			}
			log.Information("{Actor}'s {Mission} against {Target} succeeded: {Message}", actor, mission, target, result.message);

			// The agent of some missions may be exposed and lost although the
			// mission worked. Civ3's messages end "$TITLE $LEADER is not
			// pleased", so it counts as an incident. UNVERIFIED: that it is
			// one, as a caught mission is.
			// An exposed agent is lost, and an incident.
			if (result.exposed) {
				ours.hasSpy = false;
				theirs.espionageIncidents++;
				log.Information("{Actor}'s agent was exposed after its {Mission} against {Target}", actor, mission, target);
			}
			return result;
		}

		// After propaganda persuaded some of a city's citizens: with at least
		// three quarters of them the city defects; otherwise "the persuaded
		// citizen will be confused. Each will give 1 unhappy face. The number
		// of confused citizen are reduced by 1 for each turn. It is not
		// cumulative, if there already are confused citizen in the city, it
		// will be raised to the number of persuaded citizen or stay the
		// same." UNVERIFIED: the city defects as a city won over by culture
		// does (CultureFlip.Flip, whose garrison is lost), as the Civilopedia
		// links propaganda to defection; the spy stays planted.
		public static void ApplyPropaganda(GameData gameData, Player actor, City city, int persuaded, MissionResult result = null) {
			Player target = city.owner;
			result ??= new MissionResult { mission = EspionageMission.Propaganda };
			result.persuaded = persuaded;
			if (PropagandaFlips(persuaded, city.residents.Count)) {
				result.success = true;
				result.message = $"Our propaganda has worked: the people of {city.name} have revolted and joined us!";
				NotifyTarget(target, result.newsEvent, $"Stirred up by {actor.civilization.adjective ?? actor.civilization.noun} propaganda, the people of {city.name} have revolted and defected to them!");
				CultureFlip.Flip(gameData, city, actor, notify: false);
				log.Information("{Actor}'s propaganda made {City} defect from {Target}", actor, city, target);
				return;
			}
			city.propagandaConfusedCitizens = Math.Max(city.propagandaConfusedCitizens, Math.Min(persuaded, city.residents.Count));
			result.message = persuaded == 0
				? $"Our propaganda in {city.name} has fallen on deaf ears."
				: $"Our propaganda has confused {persuaded} of the people of {city.name}, but the city stays loyal.";
			log.Information("{Actor}'s propaganda confused {Persuaded} in {City}", actor, persuaded, city);
		}

		// Copies the target's map of the world into the actor's: the tiles
		// the actor doesn't know, as the target last saw them. Returns how
		// many tiles were new.
		public static int StealWorldMap(Player actor, Player target) {
			int added = 0;
			foreach (Tile t in target.tileKnowledge.AllKnownTiles()) {
				if (actor.tileKnowledge.isTileKnown(t)) {
					continue;
				}
				actor.tileKnowledge.AddTileToKnown(t);
				if (target.tileKnowledge.isActiveTile(t)) {
					actor.tileKnowledge.RememberImprovements(t);
				} else if (target.tileKnowledge.TryGetRememberedImprovements(t, out TerrainImprovement[] improvements)) {
					actor.tileKnowledge.RememberImprovements(t, improvements);
				}
				added++;
			}
			actor.tileKnowledge.RecomputeActiveTiles();
			return added;
		}

		// Shows the actor where the target's military units are, until the
		// actor's next turn ("reveals all troop locations of an enemy Civ
		// for one turn"). UNVERIFIED: the tiles stay known afterwards, as
		// the land around a capital does after an embassy shows it. The view
		// isn't saved, and a LAN guest isn't sent it.
		public static List<Tile> StealPlans(Player actor, Player target) {
			List<Tile> tiles = target.units.Where(u => u.IsCombatUnit() && u.location != null && u.location != Tile.NONE)
				.Select(u => u.location).Distinct().ToList();
			foreach (Tile t in tiles) {
				actor.tileKnowledge.AddTileToKnown(t);
			}
			actor.tileKnowledge.RecomputeActiveTiles();
			actor.tileKnowledge.Peek(tiles);
			return tiles;
		}

		// The espionage bookkeeping at each turn advance, for each player.
		public static void DoPerTurnUpdates(Player player, GameData gameData) {
			// Stolen plans are shown for one turn.
			player.tileKnowledge.EndPeek();
			foreach (City city in player.cities) {
				if (city.propagandaConfusedCitizens > 0) {
					city.propagandaConfusedCitizens--;
				}
			}
			// "There is a chance of 1/3 for the flag to disappear each turn."
			foreach (PlayerRelationship pr in player.playerRelationships.Values) {
				if (pr.spyPlantFailed && GameData.rng.Next(3) == 0) {
					pr.spyPlantFailed = false;
				}
			}
		}

		private static void NotifyTarget(Player target, NewsEvent news, string message) {
			if (target.IsToldNews) {
				new MsgShowMilitaryAdvisorPopup(target, message, happy: false) { newsEvent = news }.send();
			}
		}

		// Shows the player the tiles in the city's radius (its big fat
		// cross), remembering them as they are now.
		public static void RevealCityRadius(Player player, City city) {
			foreach (Tile t in CityRadius(city)) {
				player.tileKnowledge.AddTileToKnown(t);
				player.tileKnowledge.RememberImprovements(t);
			}
			player.tileKnowledge.RecomputeActiveTiles();
		}

		// The tiles in the city's radius, on the map.
		public static List<Tile> CityRadius(City city) {
			return city.location.GetTilesWithinRankDistance(city.owner.rules.MaxRankOfWorkableTiles)
				.Where(t => t != Tile.NONE).ToList();
		}

		public static CityReport Investigate(City city, int turn) {
			return new CityReport {
				turn = turn,
				cityName = city.name,
				size = city.residents.Count,
				producing = city.itemBeingProduced?.name,
				shieldsStored = city.shieldsStored,
				buildings = city.constructed_buildings.Select(cb => cb.building.name).ToList(),
				units = city.location.unitsOnTile.Where(u => u.owner == city.owner).Select(u => u.unitType.name).ToList(),
				ownerGold = city.owner.gold,
			};
		}

		public static string DescribeReport(CityReport report) {
			string buildings = report.buildings.Count == 0 ? "none" : string.Join(", ", report.buildings);
			string units = report.units.Count == 0 ? "none" : string.Join(", ", report.units.GroupBy(u => u).Select(g => g.Count() > 1 ? $"{g.Count()} {g.Key}" : g.Key));
			return $"{report.cityName} (size {report.size}) is building {report.producing ?? "nothing"} ({report.shieldsStored} shields).\n"
				+ $"Buildings: {buildings}\nDefenders: {units}\nTheir treasury: {report.ownerGold} gold";
		}
	}
}

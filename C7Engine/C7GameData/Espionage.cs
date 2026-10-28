using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// Civ3's diplomatic and spy missions (the Conquests Civilopedia,
	// "Espionage"; costs and chances from CivFanatics' "A Study of Espionage
	// Missions"). With Writing a civ works through its embassies: an embassy,
	// investigating a city, stealing technology. With a building that allows
	// spy missions (the Intelligence Agency) it can plant a spy with another
	// civ, and the spy can do more: steal technology, a world map or military
	// plans, start propaganda, sabotage production, and expose the other
	// civ's spy in ours. A government may be immune to a mission (Democracy to
	// propaganda).
	//
	// Cost: distance × level + the mission's term, where distance is from
	// the target city (or the capital) to our nearest city and level its
	// size level; the term is the base cost plus the city's size (embassy),
	// times its size (investigate), times their advances × the map's tech
	// rate / 100 (steal technology), times the map's average side (world
	// map), times their units (plans), times its size × (1 + their culture /
	// ours) (propaganda), times the shields in its box (sabotage); just the
	// base otherwise. The safer ways cost more (rules).
	//
	// Chance: an embassy, an investigation and propaganda are never caught;
	// planting a spy works by the spies' experience (the government's); the
	// rest first gets away, by risk and by the embassy or the spy's
	// experience, then works 80% of the time. A spy that's caught is lost.
	// A failed mission angers its target (+1 for good, +3 for a while).
	public static class Espionage {
		private static ILogger log = Log.ForContext(typeof(Espionage));

		public enum Risk {
			Immediate,
			Carefully,
			Safely,
		}

		public enum Outcome {
			Succeeded,
			Failed,
			Caught,
		}

		public class Result {
			public Outcome outcome;
			public string text;
		}

		public static bool HasIntelligenceAgency(Player player) {
			return player.cities.Any(c => c.constructed_buildings.Any(cb => cb.building.HasWonderFlag(SaveBuilding.WonderFlag.AllowsSpyMissions)
				&& !cb.building.IsObsolete(player)));
		}

		public static bool HasSpy(Player us, Player them) {
			return PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship) && relationship.hasSpy;
		}

		public static bool TargetsCity(string key) {
			return key is EspionageMission.InvestigateCity or EspionageMission.Propaganda or EspionageMission.SabotageProduction;
		}

		public static bool HasRiskChoice(string key) {
			return key is EspionageMission.StealTechnology or EspionageMission.StealWorldMap or EspionageMission.StealPlans
				or EspionageMission.SabotageProduction or EspionageMission.ExposeEnemySpy;
		}

		// Whether `us` can try `mission` against `them` now, and whether by a
		// planted spy (or else through the embassy).
		public static bool CanAttempt(Player us, Player them, EspionageMission mission, out bool bySpy) {
			bySpy = false;
			if (mission == null || !PlayerRelationship.TryGetRelationship(us, them, out PlayerRelationship relationship)
				|| them.government?.immuneTo == mission.key) {
				return false;
			}
			if (mission.key == EspionageMission.Embassy) {
				return Embassies.CanEstablish(us, them);
			}
			bool agency = mission.spy && HasIntelligenceAgency(us);
			if (mission.key == EspionageMission.PlantSpy) {
				return agency && !relationship.hasSpy && !relationship.spyPlantFailed;
			}
			if (mission.key == EspionageMission.ExposeEnemySpy) {
				bySpy = true;
				return agency;
			}
			if (agency && relationship.hasSpy) {
				bySpy = true;
				return true;
			}
			return mission.diplomat && relationship.hasEmbassy && us.HasTechWithFlag(SaveTech.Flag.EnablesDiplomats);
		}

		public static City TargetOf(Player them, City city) {
			return city ?? them.cities.Find(c => c.IsCapital()) ?? them.cities.FirstOrDefault();
		}

		public static int Cost(GameData gameData, Player us, Player them, EspionageMission mission, City city, Risk risk) {
			City target = TargetOf(them, city);
			if (target == null || us.cities.Count == 0) {
				return 0;
			}
			int distance = us.cities.Min(c => c.location.DistanceTo(target.location));
			int size = target.residents.Count;
			int b = mission.baseCost;
			int term = mission.key switch {
				EspionageMission.Embassy => b + size,
				EspionageMission.InvestigateCity => b * size,
				EspionageMission.StealTechnology => b * them.knownTechs.Count * gameData.map.techRate / 100,
				EspionageMission.StealWorldMap => b * (gameData.map.numTilesWide + gameData.map.numTilesTall) / 2,
				EspionageMission.StealPlans => b * them.units.Count,
				EspionageMission.Propaganda => b * size * (us.TotalCulture() + them.TotalCulture()) / Math.Max(1, us.TotalCulture()),
				EspionageMission.SabotageProduction => b * target.shieldsStored,
				_ => b,
			};
			int cost = distance * target.SizeLevel() + term;
			if (HasRiskChoice(mission.key)) {
				cost = cost * gameData.rules.espionage.riskCostPercent[(int)risk] / 100;
			}
			return cost;
		}

		private static int Experience(int level) {
			return Math.Clamp(level, 0, 3);
		}

		// The percent chance that the mission gets away (100: never caught).
		public static int GetAwayPercent(GameData gameData, Player us, string key, bool bySpy, Risk risk) {
			if (!HasRiskChoice(key)) {
				return 100;
			}
			EspionageRules rules = gameData.rules.espionage;
			return bySpy
				? rules.spyGetAwayPercent[Experience(us.government?.spiesAre ?? 1)][(int)risk]
				: rules.embassyGetAwayPercent[(int)risk];
		}

		// The percent chance that the mission works (propaganda's depends on
		// each citizen).
		public static int SuccessPercent(GameData gameData, Player us, string key, bool bySpy, Risk risk) {
			EspionageRules rules = gameData.rules.espionage;
			if (key == EspionageMission.PlantSpy) {
				return rules.plantSpyPercent[Experience(us.government?.spiesAre ?? 1)];
			}
			if (!HasRiskChoice(key)) {
				return 100;
			}
			return GetAwayPercent(gameData, us, key, bySpy, risk) * rules.successPercent / 100;
		}

		// The percent chance that propaganda persuades a citizen of `city`.
		public static int PropagandaPercent(GameData gameData, Player us, City city, CityResident citizen) {
			EspionageRules rules = gameData.rules.espionage;
			Player them = city.owner;
			int chance = Resistance.OpinionOf(gameData, them, us)?.propagandaChance ?? 0;
			chance += us.government?.briberyModifiers.GetValueOrDefault(them.government?.name ?? "", 0) ?? 0;
			chance += rules.propagandaSpyBonus[Experience(us.government?.spiesAre ?? 1)];
			chance -= rules.propagandaPerUnit * city.location.unitsOnTile.Count(u => u.owner == them);
			if (city.constructed_buildings.Any(cb => cb.building.resistantToPropaganda)) {
				chance -= rules.propagandaResistantBuilding;
			}
			if (city.IsCapital()) {
				chance -= rules.propagandaCapital;
			}
			if (citizen.nationality == us.civilization) {
				chance += rules.propagandaOwnNationality;
			}
			return chance;
		}

		// `us` tries `key` against `them` (at `city`, for missions on a city):
		// pays, rolls, and does it. Null if it can't be tried or paid for.
		public static Result Attempt(GameData gameData, Player us, Player them, string key, City city, Risk risk, Random rng) {
			EspionageMission mission = gameData.rules.Mission(key);
			if (!CanAttempt(us, them, mission, out bool bySpy) || (TargetsCity(key) && city?.owner != them)) {
				return null;
			}
			int cost = Cost(gameData, us, them, mission, city, risk);
			if (us.gold < cost) {
				return null;
			}
			us.gold -= cost;
			PlayerRelationship relationship = us.playerRelationships[them.id];
			Result result = Do(gameData, us, them, mission, city, risk, bySpy, relationship, rng);
			if (result.outcome != Outcome.Succeeded) {
				AttitudeRules rules = gameData.rules.attitude;
				PlayerRelationship theirs = them.playerRelationships[us.id];
				theirs.attitudePenalty += rules.failedEspionage;
				theirs.spyAnger += rules.failedEspionageForAWhile;
			}
			if (result.outcome == Outcome.Caught) {
				new MsgSpyCaught(them.id, us, mission.name).send();
			}
			log.Information($"{us} {mission.name} against {them} ({cost} gold, {risk}): {result.outcome}");
			return result;
		}

		private static Result Do(GameData gameData, Player us, Player them, EspionageMission mission, City city, Risk risk, bool bySpy,
			PlayerRelationship relationship, Random rng) {
			string theirs = them.civilization.noun;
			switch (mission.key) {
				case EspionageMission.Embassy:
					Embassies.Open(us, them);
					return new Result { outcome = Outcome.Succeeded, text = $"We have established an embassy with the {theirs}." };
				case EspionageMission.InvestigateCity:
					return new Result { outcome = Outcome.Succeeded, text = Describe(gameData, city) };
				case EspionageMission.PlantSpy:
					if (rng.Next(100) < SuccessPercent(gameData, us, mission.key, bySpy, risk)) {
						relationship.hasSpy = true;
						return new Result { outcome = Outcome.Succeeded, text = $"We have planted a spy with the {theirs}." };
					}
					relationship.spyPlantFailed = true;
					return new Result { outcome = Outcome.Failed, text = $"Our attempt to plant a spy with the {theirs} has failed." };
				case EspionageMission.Propaganda:
					return Propaganda(gameData, us, city, rng);
			}

			if (rng.Next(100) >= GetAwayPercent(gameData, us, mission.key, bySpy, risk)) {
				if (bySpy) {
					relationship.hasSpy = false;
				}
				return new Result { outcome = Outcome.Caught, text = $"Our {(bySpy ? "spy" : "agent")} was caught by the {theirs}!" };
			}
			Result failed = new() { outcome = Outcome.Failed, text = $"Our mission against the {theirs} has failed." };
			if (rng.Next(100) >= gameData.rules.espionage.successPercent) {
				return failed;
			}
			switch (mission.key) {
				case EspionageMission.StealTechnology:
					List<Tech> techs = gameData.techs.Where(t => them.knownTechs.Contains(t.id) && !us.knownTechs.Contains(t.id)).ToList();
					if (techs.Count == 0) {
						return failed;
					}
					Tech stolen = techs[rng.Next(techs.Count)];
					us.AcquireTechs(gameData, [stolen]);
					return new Result { outcome = Outcome.Succeeded, text = $"We have stolen {stolen.Name} from the {theirs}!" };
				case EspionageMission.StealWorldMap:
					us.tileKnowledge.Learn(them.tileKnowledge.knownTiles.ToList());
					return new Result { outcome = Outcome.Succeeded, text = $"We have stolen the {theirs}' world map!" };
				case EspionageMission.StealPlans:
					List<MapUnit> troops = them.units.Where(u => u.IsCombatUnit()).ToList();
					us.tileKnowledge.Learn(troops.Select(u => u.location).Distinct().ToList());
					return new Result { outcome = Outcome.Succeeded, text = $"We have stolen the {theirs}' military plans: we know where their {troops.Count} units are." };
				case EspionageMission.SabotageProduction:
					City target = TargetOf(them, city);
					target.SetStoredShields(0);
					return new Result { outcome = Outcome.Succeeded, text = $"We have sabotaged production in {target.name}!" };
				case EspionageMission.ExposeEnemySpy:
					if (!PlayerRelationship.TryGetRelationship(them, us, out PlayerRelationship theirSpy) || !theirSpy.hasSpy) {
						return new Result { outcome = Outcome.Failed, text = $"We found no {theirs} spy among us." };
					}
					theirSpy.hasSpy = false;
					return new Result { outcome = Outcome.Succeeded, text = $"We have exposed the {theirs} spy among us!" };
			}
			return failed;
		}

		// Each citizen is persuaded or not; if enough are, the city defects,
		// else those persuaded are confused for a while.
		private static Result Propaganda(GameData gameData, Player us, City city, Random rng) {
			int persuaded = city.residents.Count(r => rng.Next(100) < PropagandaPercent(gameData, us, city, r));
			if (city.residents.Count > 0 && persuaded * 100 >= gameData.rules.espionage.propagandaFlipPercent * city.residents.Count) {
				CultureFlip.Flip(gameData, city, us);
				return new Result { outcome = Outcome.Succeeded, text = $"Our propaganda has worked: {city.name} has joined us!" };
			}
			city.confusedCitizens = Math.Max(city.confusedCitizens, persuaded);
			return new Result {
				outcome = Outcome.Failed,
				text = persuaded > 0 ? $"Our propaganda has confused {persuaded} of the people of {city.name}." : $"The people of {city.name} ignore our propaganda.",
			};
		}

		// What an investigation shows.
		public static string Describe(GameData gameData, City city) {
			string producing = city.itemBeingProduced == null ? "nothing" : $"{city.itemBeingProduced.name} ({city.shieldsStored} shields)";
			string buildings = string.Join(", ", city.constructed_buildings.Select(cb => cb.building.name));
			string units = string.Join(", ", city.location.unitsOnTile.Where(u => u.owner == city.owner)
				.GroupBy(u => u.unitType.name).Select(g => $"{g.Count()} {g.Key}"));
			return $"{city.name} (size {city.residents.Count}): building {producing}.\nImprovements: {(buildings == "" ? "none" : buildings)}.\nUnits: {(units == "" ? "none" : units)}.";
		}

		// Each turn a failed plant's flag may clear, and confused citizens
		// calm down.
		public static void UpdateForNewTurn(GameData gameData) {
			int oneIn = Math.Max(1, gameData.rules.espionage.plantFailedClearsOneIn);
			foreach (Player player in gameData.players) {
				foreach (PlayerRelationship relationship in player.playerRelationships.Values.Where(r => r.spyPlantFailed)) {
					if (GameData.rng.Next(oneIn) == 0) {
						relationship.spyPlantFailed = false;
					}
				}
			}
			foreach (City city in gameData.cities.Where(c => c.confusedCitizens > 0)) {
				city.confusedCitizens--;
			}
		}
	}
}

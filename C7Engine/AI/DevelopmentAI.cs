using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;
using Serilog;

namespace C7Engine.AI {
	// How fast the AI develops its empire: what it researches, how its
	// cities work their tiles, which units it hurries with gold, and, still
	// on trial behind an AiExperiments switch, what its cities build. All of
	// them are AI preferences, not rules.
	public static class DevelopmentAI {
		private static readonly ILogger log = Log.ForContext(typeof(DevelopmentAI));

		// Once its units cost upkeep or it has stopped expanding, a civ at
		// peace stops adding soldiers when it has a defender and a half per
		// city (three near a rival's city), and builds no more than one scout,
		// nor walls or barracks. Without this the AI kept some 80 land units
		// for 11 cities by turn 200, and paid for most of them.
		public const string LeanMilitary = "lean-military";

		// Steam Power (railroads) and Philosophy (a free tech for the first
		// to learn it) count for more when choosing research: a flat
		// SwitchedTechBonus each. UNVERIFIED: the size; a guess, about a
		// defender's worth.
		public const string RailroadFirst = "railroad-first";
		public const string PhilosophyFirst = "philosophy-first";
		public const double SwitchedTechBonus = 150;

		// Workers per city: 1.0 while expanding, 1.2 after (see
		// ChooseProducible.EnoughWorkers).
		public const string WorkersOneTwo = "workers-1-2";

		////////////////////////////////////////////////////////////////////
		// lean-military

		internal static bool IsMilitaryLandUnit(UnitPrototype unit) {
			return unit.IsLandUnit() && !unit.isWorker && !unit.actions.Contains(UnitAction.BuildCity)
				&& (unit.attack > 0 || unit.defense > 0) && !unit.isArmy;
		}

		// The soldiers we have, or are building, and how many we want in
		// peacetime: a defender and a half per city (three near a rival's
		// city), an escort for each settler, and a couple to spare (e.g.
		// against barbarians).
		internal static (int have, int want) MilitaryNeed(Player player) {
			int have = 0;
			int settlers = 0;
			foreach (MapUnit u in player.units) {
				if (u.unitType.actions.Contains(UnitAction.BuildCity)) {
					++settlers;
				} else if (IsMilitaryLandUnit(u.unitType)) {
					++have;
				}
			}
			int borderCities = 0;
			foreach (City c in player.cities) {
				if (c.itemBeingProduced is UnitPrototype p && IsMilitaryLandUnit(p)) {
					++have;
				} else if (c.itemBeingProduced is UnitPrototype s && s.actions.Contains(UnitAction.BuildCity)) {
					++settlers;
				}
				// On a crowded continent, the cap without this cost cities to
				// the neighbors.
				if (NearRivalCity(player, c)) {
					++borderCities;
				}
			}
			int want = (int)Math.Ceiling((player.cities.Count + borderCities) * 1.5) + settlers + 2;
			return (have, want);
		}

		private const int BorderDistance = 6;

		internal static bool NearRivalCity(Player player, City city) {
			foreach (Player other in EngineStorage.gameData.players) {
				if (other == player || other.IsIndependent || other.defeated) {
					continue;
				}
				foreach (City theirs in other.cities) {
					if (theirs.location.map == city.location.map && city.location.DistanceTo(theirs.location) <= BorderDistance
							&& AIFogOfWar.KnowsTile(player, theirs.location)) {
						return true;
					}
				}
			}
			return false;
		}

		internal static float LeanMilitaryAdjustment(ChooseProducible.ProducibleStats stats, UnitPrototype unit) {
			if (stats.atWar) {
				return 0;
			}
			// Scouts and explorers: one is enough. (Capping soldiers alone
			// sent the shields to them instead.)
			if (unit.IsLandUnit() && unit.attack == 0 && unit.defense == 0 && !unit.isWorker
					&& !unit.actions.Contains(UnitAction.BuildCity)) {
				return stats.player.units.Any(u => u.unitType.IsLandUnit() && u.unitType.attack == 0 && u.unitType.defense == 0
					&& !u.unitType.isWorker && !u.unitType.actions.Contains(UnitAction.BuildCity)) ? -15 : 0;
			}
			// A city's own guard and a settler's escort are always wanted.
			if (!IsMilitaryLandUnit(unit) || !stats.CityGuarded || stats.HasUnescortedSettler) {
				return 0;
			}
			// Switch prey-war: the attackers a campaign on the plan's prey is
			// short of are wanted too (see PreyWar).
			if (unit.attack > unit.defense && PreyWar.On(stats.player) && WarCampaign.HasPreyCampaign(stats.player)
					&& WarCampaign.WantsAttackers(stats.player)) {
				return 0;
			}
			// Switch horde-war: likewise the horde's (see HordeWar).
			if (HordeWar.On(stats.player) && WarCampaign.HasHordeCampaign(stats.player)
					&& (unit.attack > unit.defense ? WarCampaign.WantsAttackers(stats.player) : WarCampaign.WantsHordeEscort(stats.player))) {
				return 0;
			}
			// While the empire is still expanding and its units cost nothing
			// to support, soldiers are the cheapest use of shields a small
			// city has: the alternatives cost citizens (workers) or upkeep
			// (temples, walls), and capping soldiers then held back the
			// expansion by some 10 turns.
			if (stats.InExpansionPhase && stats.UnitSupportCost <= 0) {
				return 0;
			}
			(int have, int want) = stats.MilitaryNeed;
			if (have < want) {
				return 0;
			}
			return -10 - 5 * (have - want);
		}

		// Nor walls and barracks in peacetime, which the shields also went to.
		internal static float LeanMilitaryAdjustment(ChooseProducible.ProducibleStats stats, Building building) {
			if (stats.atWar) {
				return 0;
			}
			float score = 0;
			if (building.providesVeteranGroundUnits) {
				score -= 10;
			}
			if (building.providesWalls) {
				score -= 8;
			}
			return score;
		}

		////////////////////////////////////////////////////////////////////
		// Research

		// The tech to research next, as Civ3's AI picks it (below), or null
		// with none to research. In AI trials (AiTrialTest, 8 paired games of
		// 200 turns), against heading for Monarchy first and then taking the
		// cheapest tech, civs scored +104 +- 112 on Continents and -108 +-
		// 150 on Pangaea, knew 2-3 more techs halfway through, and spent
		// some 30-45 more turns in Despotism.
		public static Tech ChooseResearch(Player player, List<Tech> techs, IEnumerable<Tech> available) {
			return ChooseResearchAsCiv3(player, available.ToList());
		}

		// Civ3's AI "places a value on each technology it can research
		// next. The values depend on the various things allowed by each
		// technology, as well as on the turns needed by that AI civ", and
		// the values "weight the AI random choice": "techs with higher value
		// were chosen more often" (alexman, who found the values by
		// stripping the techs of their benefits and adding them back one by
		// one: https://forums.civfanatics.com/threads/what-will-the-ai-research-next.45559/).
		// UNVERIFIED: how the values weight the roll; here a tech's chance is
		// in proportion to its value.
		public static Tech ChooseResearchAsCiv3(Player player, List<Tech> available) {
			if (available.Count == 0) {
				return null;
			}
			GameData gameData = EngineStorage.gameData;
			int beakers = player.BeakersPerTurn();
			// The civ's flavors make it favour some (see Flavors).
			List<(Tech tech, double value)> values = available
				.Select(t => (t, TechValue(gameData, player, t, beakers) * Flavors.TechFactor(gameData.rules, player.civilization, t))).ToList();
			double total = values.Sum(v => v.value);
			if (total <= 0) {
				return null;
			}
			double roll = GameData.rng.NextDouble() * total;
			foreach ((Tech tech, double value) in values) {
				roll -= value;
				if (roll < 0) {
					return tech;
				}
			}
			return values[^1].tech;
		}

		// alexman's values (thread above). "These values are cumulative":
		// a tech is worth the sum of what it allows. Those per turn are
		// divided by the turns the civ needs for the tech; the rest are
		// flat. "For example, Iron Working allows swordsmen and iron, so it
		// has a value of 22 more than if it were just an empty tech", and
		// "for the Romans who build Legionaries (attacker and defender), Iron
		// Working is worth an additional 70 points": units are worth what
		// they are to the civ, by the units it can build and their AI
		// strategies in the BIQ.
		private const double EmptyTechPerTurn = 256;
		private const double SpaceshipPartPerTurn = 261;
		private const double GovernmentPerTurn = 259;
		private const double ConscriptionPerTurn = 259;
		private const double MobilizationPerTurn = 259;
		private const double Defender = 198;
		private const double DefenderNeedingResource = 70;
		private const double Attacker = 134;
		private const double AttackerNeedingResource = 6;
		private const double NavalTransport = 34;
		private const double OtherUnit = 2;
		private const double ResourceValue = 16;
		private const double GreatWonder = 6;
		private const double SmallWonder = 2;
		private const double Improvement = 2;
		private const double Specialist = 2;
		private const double WorkerJob = 1;
		// "Optional: divide by 1.5": techs not required to enter the next
		// era.
		private const double OptionalDivisor = 1.5;

		private static readonly (SaveTech.Flag flag, double value)[] FlagValues = [
			(SaveTech.Flag.DoublesWorkerRate, 8),
			(SaveTech.Flag.EnablesDiplomats, 8),
			(SaveTech.Flag.EnablesMutualProtection, 8),
			(SaveTech.Flag.EnablesRightOfPassage, 8),
			(SaveTech.Flag.EnablesMilitaryAlliances, 8),
			(SaveTech.Flag.EnablesTradeEmbargoes, 8),
			(SaveTech.Flag.EnablesTradeOverOcean, 8),
			(SaveTech.Flag.EnablesIrrigationEverywhere, 4),
			(SaveTech.Flag.DisablesDiseases, 4),
			(SaveTech.Flag.EnablesTradeOverSea, 4),
			(SaveTech.Flag.EnablesBridges, 2),
			(SaveTech.Flag.DoublesWealthProduction, 2),
			(SaveTech.Flag.EnablesMapTrading, 2),
			(SaveTech.Flag.EnablesCommunicationTrading, 2),
			(SaveTech.Flag.EnablesRecycling, 1),
			(SaveTech.Flag.EnablesPrecisionBombing, 1),
			// "Bonus Tech: 0; Reveal Map: 0; Sacrifices: 0".
		];

		// The tech's worth to the civ. alexman's later posts in the thread
		// (page 4) explain two more items: "No trade" is the Cannot Be Traded
		// tech flag (149/turns), and "Other tech: 1" is a point for each tech
		// that has this one as a direct prerequisite.
		private const double CannotBeTradedPerTurn = 149;
		private const double LeadsToTech = 1;

		public static double TechValue(GameData gameData, Player player, Tech tech, int beakersPerTurn) {
			double perTurn = EmptyTechPerTurn;
			double flat = 0;

			if (tech.CannotBeTraded) {
				perTurn += CannotBeTradedPerTurn;
			}
			foreach (Tech other in gameData.techs) {
				if (other.Prerequisites.Contains(tech)) {
					flat += LeadsToTech;
				}
			}

			foreach (Government government in gameData.governments) {
				if (!government.transitionType && government.prerequisiteTech == tech.id && gameData.rules.AllowsGovernment(government)) {
					perTurn += GovernmentPerTurn;
				}
			}
			if (tech.HasFlag(SaveTech.Flag.EnablesConscription)) {
				perTurn += ConscriptionPerTurn;
			}
			if (tech.HasFlag(SaveTech.Flag.EnablesMobilization)) {
				perTurn += MobilizationPerTurn;
			}
			foreach ((SaveTech.Flag flag, double worth) in FlagValues) {
				if (tech.HasFlag(flag)) {
					flat += worth;
				}
			}

			foreach (UnitPrototype unit in gameData.unitPrototypes) {
				if (unit.requiredTech != tech || unit.unproducible || !unit.IsProducibleBy(player.civilization)) {
					continue;
				}
				bool needsResource = unit.requiredResources.Count > 0;
				bool offense = unit.flags.Contains(SaveUnitPrototype.Flag.AIOffense);
				bool defense = unit.flags.Contains(SaveUnitPrototype.Flag.AIDefense);
				bool transport = unit.flags.Contains(SaveUnitPrototype.Flag.AINavalTransport);
				if (defense) {
					flat += needsResource ? DefenderNeedingResource : Defender;
				}
				if (offense) {
					flat += needsResource ? AttackerNeedingResource : Attacker;
				}
				// The attacker an archer rush lacks (see ArcherRush).
				if (unit.attack >= ArcherRush.WantedAttack && !needsResource && ArcherRush.NeedsAttacker(gameData, player)) {
					flat += ArcherRush.TechBonus;
				}
				if (transport) {
					flat += NavalTransport;
				}
				if (!offense && !defense && !transport) {
					flat += OtherUnit;
				}
			}

			bool spaceship = false;
			foreach (Building building in gameData.Buildings) {
				if (building.requiredTech != tech) {
					continue;
				}
				if (building.IsSpaceshipPart) {
					spaceship = true;
				} else if (building.IsGreatWonder()) {
					flat += GreatWonder;
				} else if (building.isSmallWonder) {
					flat += SmallWonder;
				} else {
					flat += Improvement;
				}
			}
			if (spaceship) {
				perTurn += SpaceshipPartPerTurn;
			}

			foreach (Resource resource in gameData.Resources) {
				if (resource.Category != ResourceCategory.NONE && resource.Prerequisite == tech.id) {
					flat += ResourceValue;
				}
			}
			foreach (CitizenType citizen in gameData.citizenTypes) {
				if (!citizen.IsDefaultCitizen && citizen.PrerequisiteTech == tech.id) {
					flat += Specialist;
				}
			}
			foreach (Terraform terraform in gameData.Terraforms) {
				if (terraform.RequiredTech == tech.id) {
					flat += WorkerJob;
				}
			}

			if (tech.Name == "Steam Power" && AiExperiments.Has(player, RailroadFirst)) {
				flat += SwitchedTechBonus;
			}
			if (tech.Name == "Philosophy" && AiExperiments.Has(player, PhilosophyFirst)) {
				flat += SwitchedTechBonus;
			}

			double value = perTurn / TurnsNeeded(gameData, player, tech, beakersPerTurn) + flat;
			value = tech.RequiredForEraAdvancement ? value : value / OptionalDivisor;
			// After the divisor: the way out of Despotism is wanted whether
			// or not the era needs it.
			if (GovernmentAI.RepublicRoutePath(player, gameData).Contains(tech)) {
				value += GovernmentAI.RepublicRouteBonus;
			}
			return GovernmentAI.EarlyGovernmentPath(player, gameData).Contains(tech) ? value + GovernmentAI.EarlyGovernmentBonus : value;
		}

		// "The AI needs 24 times the base cost of each technology in
		// beakers" on a standard map, "so an AI producing 4 beakers per turn
		// would need 72/4=18 turns" for Bronze Working (alexman): the tech's
		// cost to the civ over its beakers, within the rules' research
		// times.
		private static int TurnsNeeded(GameData gameData, Player player, Tech tech, int beakersPerTurn) {
			int cost = Math.Max(1, gameData.TechCostFor(tech, player));
			int turns = beakersPerTurn <= 0 ? int.MaxValue : (int)Math.Ceiling((double)cost / beakersPerTurn);
			Rules rules = gameData.rules;
			int max = rules != null && rules.MaximumResearchTime > 0 ? rules.MaximumResearchTime : turns;
			int min = Math.Max(1, rules?.MinimumResearchTime ?? 1);
			return Math.Clamp(turns, min, Math.Max(min, max));
		}

		////////////////////////////////////////////////////////////////////
		// Hurrying production

		// Why a unit is worth hurrying, least urgent first.
		private enum RushReason {
			None,
			Improvement,
			Worker,
			Settler,
			Escort,
			Campaign,
			Defense,
		}

		// Under governments that hurry with gold, buys the units that matter
		// now, most urgent first: defenders for unguarded or threatened
		// cities, attackers and siege engines for a war campaign, escorts for
		// waiting settlers, settlers, and workers while short of them.
		//
		// Improvements are bought too, after every unit, above Civ3's
		// reserve (see IsHurriableImprovement).
		//
		// Not hurried, as AI trials (AiTrialTest) found no gain or a loss:
		// any soldier at war (it mostly bought
		// obsolete warriors and archers); and units bought with citizens
		// under Despotism and the like (fewer units and lower scores).
		// Hurrying as it is, with the taxes at war, civs had some 66 more
		// score after 200 turns over 64 paired games on Continents and
		// Pangaea (+-47; ahead on each map and seed set), most on Pangaea,
		// where they also lost fewer cities.
		public static void HurryProduction(Player player) {
			if (player.isHuman || player.IsIndependent || player.cities.Count == 0
					|| player.government.hurryingType != Government.HurryProductionType.PaidLabor) {
				return;
			}
			City staging = WarCampaign.CampaignStagingCity(player);
			bool atWar = staging != null || PlayerRelationship.IsInAnyWar(player, EngineStorage.gameData.players);
			// The reserve also covers ten turns of any deficit, so that the
			// sliders (PlayerAI.AdjustSliders, which tolerates a deficit of a
			// tenth of the treasury) needn't take beakers for taxes.
			int deficit = Math.Max(0, -player.CalculateGoldPerTurn());
			// Civ3's reserve (see StockGoldReserve), which Civ3's AI keeps
			// when it rushes improvements; this AI keeps it for units too.
			int reserve = Math.Max(StockGoldReserve(player), ReserveDeficitTurns * deficit);
			// At war, or gathering for one, gold kept back is worth less than
			// soldiers in the field.
			int warReserve = atWar ? reserve / 2 : reserve;
			int workers = player.units.Count(u => u.unitType.isWorker);

			List<(City city, RushReason reason, int cost)> candidates = new();
			foreach (City city in player.cities) {
				if (city.hurriedThisTurn || city.itemBeingProduced == null) {
					continue;
				}
				RushReason reason = UnitRushReason(player, city, staging, workers);
				if (reason == RushReason.None && IsHurriableImprovement(city.itemBeingProduced)) {
					reason = RushReason.Improvement;
				}
				if (reason == RushReason.None) {
					continue;
				}
				// From an empty box hurrying costs double; wait a turn unless
				// the city is in danger.
				if (city.shieldsStored == 0 && reason != RushReason.Defense) {
					continue;
				}
				// Done next turn anyway.
				if (city.TurnsUntilProductionFinished() <= 1) {
					continue;
				}
				// A settler or worker the city isn't big enough for would only wait.
				if (city.itemBeingProduced.populationCost >= city.residents.Count) {
					continue;
				}
				City.HurryProductionDetails details = city.GetHurryProductionDetails();
				if (details.errorMessage != null || details.goldCost <= 0) {
					continue;
				}
				candidates.Add((city, reason, details.goldCost));
			}
			// The most urgent first, and among equals the cheapest (the
			// nearest to done), so the gold buys the most.
			foreach ((City city, RushReason reason, int cost) in candidates.OrderByDescending(c => c.reason).ThenBy(c => c.cost)) {
				int keep = reason switch {
					RushReason.Defense => 0,
					RushReason.Campaign => warReserve,
					_ => reserve,
				};
				if (player.gold - cost < keep) {
					continue;
				}
				log.Debug("{Player} hurries {Item} in {City} for {Cost} ({Reason})", player, city.itemBeingProduced, city, cost, reason);
				city.HurryProduction();
				AiExperiments.onHurry?.Invoke(city, cost);
			}
		}

		// Civ3's AI also rushes improvements, never letting its treasury
		// fall below the stock reserve (see StockGoldReserve): "it won't
		// rush an improvement if doing so would leave its treasury below
		// that level" (Flintlock, reading the 1.22 code,
		// https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16787843;
		// compendium 08). Per the owner's decision, 2026-10-21, the AI
		// hurries improvements as well as units. Improvements come after
		// every unit reason, so units bought for defence or a campaign keep
		// first call on the gold. Great and small wonders are not
		// improvements and are never bought. AI trials found it neutral
		// (doc/ai-review-2026-10.md, 21 October): the treasury is seldom
		// above the reserve.
		private static bool IsHurriableImprovement(IProducible item) {
			return item is Building b && !b.IsGreatWonder() && !b.isSmallWonder && !b.isCenterOfEmpire && !b.IsSpaceshipPart;
		}

		// HEURISTIC: a base and so much per city, which sets how long the AI
		// at war raises taxes (see WantedGoldPerTurn); it was once also the
		// gold kept back from hurrying, which is now Civ3's StockGoldReserve.
		// Hurrying also keeps ten turns of any deficit.
		private const int ReserveBase = 10;
		private const int ReservePerCity = 2;
		private const int ReserveDeficitTurns = 10;

		private static int BaseReserve(Player player) {
			return ReserveBase + ReservePerCity * player.cities.Count;
		}

		// Civ3's own gold reserve for the AI: 16 gold per city plus the
		// treasury that earns full interest, for a civ with an interest
		// building. Flintlock, reading the 1.22 code
		// (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16787843),
		// on rush-buying: "Its reserve target is
		// 1000/number_of_interest_generating_wonders + 16 *
		// number_of_cities", and "it won't rush an improvement if doing so
		// would leave its treasury below that level"; the upgrade pass uses
		// the same reserve (post-16937054; see PlayerAI.UpgradeUnits).
		public static int StockGoldReserve(Player player) {
			return 16 * player.cities.Count + player.GoldForFullInterest();
		}

		// The gold per turn the sliders should at least bring in (see
		// PlayerAI.AdjustSliders), or int.MinValue for no more than usual:
		// a gold per city while at war or gathering for one, under
		// governments that hurry with gold, until the treasury holds three
		// times the reserve. The AI otherwise ran its treasury at about
		// nothing a turn, so had little to hurry with when it mattered.
		public static int WantedGoldPerTurn(Player player) {
			if (player.isHuman || player.IsIndependent
					|| player.government.hurryingType != Government.HurryProductionType.PaidLabor
					|| player.gold > 3 * BaseReserve(player)) {
				return int.MinValue;
			}
			if (WarCampaign.CampaignStagingCity(player) == null && !PlayerRelationship.IsInAnyWar(player, EngineStorage.gameData.players)) {
				return int.MinValue;
			}
			return player.cities.Count;
		}

		// HEURISTIC: how many tiles from a campaign's staging city a city may
		// be for the attackers it buys to join the campaign in time.
		private const int CampaignRushDistance = 8;

		private static RushReason UnitRushReason(Player player, City city, City staging, int workers) {
			if (city.itemBeingProduced is not UnitPrototype unit || !unit.IsLandUnit() || unit.isArmy) {
				return RushReason.None;
			}
			if (unit.defense > 0 && (!city.location.unitsOnTile.Any(u => u.owner == player && u.CanDefendOnLand()) || Threatened(player, city))) {
				return RushReason.Defense;
			}
			if (staging != null && city.location.continent == staging.location.continent
					&& city.location.DistanceTo(staging.location) <= CampaignRushDistance) {
				if ((unit.attack > unit.defense && WarCampaign.WantsAttackers(player))
						|| (unit.bombard > 0 && unit.attack == 0 && WarCampaign.WantsBombarders(player))) {
					return RushReason.Campaign;
				}
			}
			if (unit.defense > 0 && unit.defense >= unit.attack
					&& city.location.unitsOnTile.Any(u => u.owner == player && u.currentAI is SettlerAI s && s.data.escort == null)) {
				return RushReason.Escort;
			}
			if (unit.actions.Contains(UnitAction.BuildCity)) {
				return RushReason.Settler;
			}
			if (unit.isWorker && workers < player.cities.Count) {
				return RushReason.Worker;
			}
			return RushReason.None;
		}

		// HEURISTIC: how near an enemy unit must be for its city to be in
		// danger.
		private const int ThreatDistance = 2;

		// A threatened city with fewer real defenders (defense at least
		// attack) than the enemy units near it, or fewer than two, switches
		// to the best defender it can build, unless it's building one, so
		// that hurrying (see HurryProduction's Defense reason) can buy it
		// this turn. Without this a city kept building whatever it had
		// started, say a 20-turn cathedral, with the enemy at its walls.
		// Shields carry over from a building to a unit only partly in Civ3,
		// but a lost city loses them all.
		public static void DefendThreatenedCities(Player player) {
			GameData gameData = EngineStorage.gameData;
			if (gameData == null || player.isHuman || player.IsIndependent) {
				return;
			}
			foreach (City city in player.cities) {
				int enemies = EnemyAttackersNear(player, city);
				if (enemies == 0) {
					continue;
				}
				int defenders = city.location.unitsOnTile.Count(u => u.owner == player && u.CanDefendOnLand()
					&& u.unitType.defense >= u.unitType.attack);
				if (defenders >= Math.Max(2, enemies)) {
					continue;
				}
				if (city.itemBeingProduced is UnitPrototype current && IsDefender(current)) {
					continue;
				}
				UnitPrototype best = city.ListProductionOptions(gameData).OfType<UnitPrototype>().Where(IsDefender)
					.OrderByDescending(p => p.defense).ThenBy(p => p.shieldCost).FirstOrDefault();
				if (best != null) {
					log.Information("{Player} switches {City} to {Unit}, with {Enemies} enemies near and {Defenders} defenders", player, city, best, enemies, defenders);
					city.ChooseProduction(best);
				}
			}
		}

		private static bool IsDefender(UnitPrototype p) {
			return p.IsLandUnit() && !p.isArmy && p.defense > 0 && p.defense >= p.attack && !p.isWorker
				&& !p.actions.Contains(UnitAction.BuildCity);
		}

		// The enemy land units that can attack, which we can see, near the
		// city.
		private static int EnemyAttackersNear(Player player, City city) {
			int count = 0;
			foreach (Tile t in city.location.GetTilesWithinRankDistance(ThreatDistance)) {
				if (!AIFogOfWar.SeesUnitsOn(player, t)) {
					continue;
				}
				foreach (MapUnit u in t.unitsOnTile) {
					if (u.owner != player && u.IsLandUnit() && u.unitType.attack > 0 && !u.owner.IsAtPeaceWith(player)) {
						++count;
					}
				}
			}
			return count;
		}

		// Whether an enemy land unit that can attack stands, as far as we
		// can see, near the city.
		private static bool Threatened(Player player, City city) {
			foreach (Tile t in city.location.GetTilesWithinRankDistance(ThreatDistance)) {
				if (!AIFogOfWar.SeesUnitsOn(player, t)) {
					continue;
				}
				foreach (MapUnit u in t.unitsOnTile) {
					if (u.owner != player && u.IsLandUnit() && u.unitType.attack > 0 && !u.owner.IsAtPeaceWith(player)) {
						return true;
					}
				}
			}
			return false;
		}

		////////////////////////////////////////////////////////////////////
		// Food first

		// The food surplus the citizens of an AI's city aim for, from the
		// usual one: they work their food tiles harder while the city is
		// small, or while it builds a settler or worker. Humans' cities are
		// left as they were.
		public static int DesiredFoodSurplus(City city, int usual) {
			if (city.owner == null || city.owner.isHuman || city.owner.IsIndependent) {
				return usual;
			}
			if (city.itemBeingProduced is UnitPrototype u && u.populationCost > 0) {
				return usual + 2;
			}
			// Without Despotism's tile penalty, food tiles make more, so it
			// pays to work them a little longer.
			if (city.residents.Count < (city.owner.government.hasTilePenalty ? 3 : 4)) {
				return usual + 1;
			}
			return usual;
		}
	}
}

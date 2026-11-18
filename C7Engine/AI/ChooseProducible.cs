using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Events;
using C7Engine.AI;
using C7Engine.AI.Advanced;
using C7Engine.AI.UnitAI;
using C7GameData;
using static C7GameData.PlayerRelationship;

namespace C7Engine {
	public class ChooseProducible {
		private static ILogger log = Log.ForContext<ChooseProducible>();

		// Facts about the city and player that every option is scored
		// against. Nothing changes while the options of one decision are
		// scored, so each is computed at most once per decision, and only if
		// an option needs it.
		internal sealed class ProducibleStats {
			public readonly City city;
			public readonly Player player;

			public float bestAttack;
			public float bestDefense;
			public readonly bool atWar;

			// Finding the open city spots scores every known tile, so only do
			// it if a decision needs it: when scoring a settler, or to tell if
			// a player with 5 or more cities is still expanding.
			private int? numberOfReachableOpenCitySpots;
			public int NumberOfReachableOpenCitySpots => numberOfReachableOpenCitySpots ??= ChooseProducible.NumberOfReachableOpenCitySpots(city);

			// We keep expanding while there's an open city spot on our
			// continent that no settler is being built for yet: the project
			// owner's AI preference is to fill every gap of 3 or more tiles
			// between cities (see SettlerLocationAI), however poor the site.
			// Once settlers are being built for all the spots, the other
			// cities go back to building up.
			private bool? inExpansionPhase;
			public bool InExpansionPhase => inExpansionPhase ??= player.cities.Count < 5 || NumberOfReachableOpenCitySpots > SettlersUnderConstruction;

			public ProducibleStats(City city, Player player) {
				this.city = city;
				this.player = player;
				atWar = IsInAnyWar(player, EngineStorage.gameData.players);
			}

			private bool? cityGuarded;
			public bool CityGuarded => cityGuarded ??= city.location.unitsOnTile.Count(u => u.CanDefendOnLand()) > 0;

			private bool? hasUnescortedSettler;
			public bool HasUnescortedSettler => hasUnescortedSettler ??= ChooseProducible.HasUnescortedSettler(city);

			private int? unitSupportCost;
			public int UnitSupportCost => unitSupportCost ??= player.TotalUnitsAllowedUnitsAndSupportCost().Item3;

			private int? settlersUnderConstruction;
			public int SettlersUnderConstruction => settlersUnderConstruction ??= ChooseProducible.SettlersUnderConstruction(player);

			private int? numWorkers;
			public int NumWorkers => numWorkers ??= player.units.Count(x => x.unitType.isWorker);

			private int? numUnworkedTiles;
			public int NumUnworkedTiles => numUnworkedTiles ??= ChooseProducible.NumUnworkedTiles(city);

			private bool? connectedToCapital;
			public bool ConnectedToCapital => connectedToCapital ??= EngineStorage.gameData.GetTradeNetwork().ConnectedToCapital(player, city);

			// Whether a settler is waiting at this city for a transport that
			// no ship of ours can take on and no city is building yet.
			private bool? needsTransport;
			public bool NeedsTransport => needsTransport ??= ChooseProducible.NeedsTransport(city, player);

			private bool? neighborsOcean;
			public bool NeighborsOcean => neighborsOcean ??= city.location.NeighborsOcean();

			private int? luxuryCount;
			public int LuxuryCount => luxuryCount ??= city.GetLuxuries(EngineStorage.gameData).Keys.Count;

			private int? foodGrowthPerTurn;
			public int FoodGrowthPerTurn => foodGrowthPerTurn ??= city.FoodGrowthPerTurn();

			private CorruptableValue? currentProductionYield;
			public CorruptableValue CurrentProductionYield => currentProductionYield ??= city.CurrentProductionYield();

			private int? culturePerTurn;
			public int CulturePerTurn => culturePerTurn ??= city.GetCulturePerTurn();

			private (int unclaimed, int enemy)? outerRingTiles;
			public (int unclaimed, int enemy) OuterRingTiles => outerRingTiles ??= CountOuterRingTiles(city);

			private (int unhappy, int entertainers)? unhappyAndEntertainers;
			public (int unhappy, int entertainers) UnhappyAndEntertainers => unhappyAndEntertainers ??= CountUnhappyAndEntertainers(city);

			private WonderPlanner.Plan wonderPlan;
			public WonderPlanner.Plan WonderPlan => wonderPlan ??= new(city, player);

			// For the fast-expand trial (see FastExpand): whether it shapes
			// this decision, and the soldiers it wants.
			private bool? fastExpandActive;
			public bool FastExpandActive => fastExpandActive ??= FastExpand.Active(this, city, player);
			private (int have, int want)? fastExpandSoldiers;
			public (int have, int want) FastExpandSoldiers => fastExpandSoldiers ??= FastExpand.Soldiers(player);

			// For the lean-garrison trial (see LeanGarrison).
			private int? leanGarrisonNeed;
			public int LeanGarrisonNeed => leanGarrisonNeed ??= LeanGarrison.Need(player, atWar);

			// For the lean-military trial (see DevelopmentAI).
			private (int have, int want)? militaryNeed;
			public (int have, int want) MilitaryNeed => militaryNeed ??= DevelopmentAI.MilitaryNeed(player);

			// For valuing improvements by their effect (see BuildingEffects).
			private CommerceBreakdown? commerce;
			public CommerceBreakdown Commerce => commerce ??= city.CurrentCommerceYieldRaw();

			private int? goldPerTurn;
			public int GoldPerTurn => goldPerTurn ??= player.CalculateGoldPerTurn();

			internal bool? enemyAircraftNear;
			internal bool? enemyWarshipsNear;

			private int? turnsUntilGrowth;
			public int TurnsUntilGrowth => turnsUntilGrowth ??= city.TurnsUntilGrowth();

			// Whether a naval unit of each type built here would explore.
			private readonly Dictionary<UnitPrototype, bool> wouldExplore = new();
			public bool WouldExplore(UnitPrototype unit) {
				if (!wouldExplore.TryGetValue(unit, out bool result)) {
					// A unit we might build, used only to ask what we would do
					// with it. It isn't put into play, so it doesn't need a
					// real ID.
					MapUnit temp = unit.GetInstance(ID.None(unit.name), unit, player, location: city.location);
					result = PlayerAI.WouldExplore(temp, player, hypothetical: true);
					wouldExplore[unit] = result;
				}
				return result;
			}
		}

		public static IProducible Choose(City city, Player player) {
			IProducible best = null;
			float bestScore = int.MinValue;
			foreach ((IProducible option, float score) in ScoredOptions(city, player)) {
				if (score > bestScore) {
					bestScore = score;
					best = option;
				}
			}
			if (log.IsEnabled(LogEventLevel.Debug)) {
				log.Debug("\t\tchose {Best}", best);
			}
			return best;
		}

		// Every option the city may build, in order, with the score Choose
		// picks the highest of.
		internal static List<(IProducible option, float score)> ScoredOptions(City city, Player player) {
			List<IProducible> options = city.ListProductionOptions(EngineStorage.gameData).ToList();
			// The city governor's production options (see
			// GovernorProduction). If they rule out everything the city can
			// build, it is chosen from everything rather than nothing.
			CityGovernor governor = city.governor;
			List<IProducible> allowed = options.Where(o => !GovernorProduction.Excludes(governor, o)).ToList();
			if (allowed.Count > 0) {
				options = allowed;
			}
			ProducibleStats stats = CalculateStats(city, player, options);
			List<(IProducible option, float score)> scored = new(options.Count);

			bool debugLogging = log.IsEnabled(LogEventLevel.Debug);
			if (debugLogging) {
				log.Debug("{Civilization}: {City}---- {InExpansionPhase} {OpenCitySpots}", player.civilization.name, city, stats.InExpansionPhase, stats.NumberOfReachableOpenCitySpots);
			}
			foreach (IProducible option in options) {
				// Get the item score, with a +/- 10% random adjustment to make
				// things seem appropriately random.
				// An Advanced AI's plan leans the score toward what it wants
				// more of (nothing for the Standard AI; see PlanEffects).
				float score = (GovernorProduction.AdjustScore(governor, player, option, ScoreProducible(stats, city, player, option))
					+ C7Engine.AI.Advanced.PlanEffects.ProductionAdjustment(player, option))
					* (GameData.rng.Next(90, 110) / 100.0f);
				scored.Add((option, score));
				if (debugLogging) {
					log.Debug("\t{Option}: {Score}", option, score);
				}
			}
			return scored;
		}

		private static bool NeedsRoomToGrow(City city, int cap, ProducibleStats stats) {
			int size = city.residents.Count;
			if (size >= cap) {
				return !city.CanGrowPopulationByOne(EngineStorage.gameData);
			}
			return size == cap - 1 && stats.FoodGrowthPerTurn > 0;
		}

		private static float ScoreProducible(ProducibleStats stats, City city, Player player, IProducible option) {
			if (option is UnitPrototype unit) {
				return ScoreUnit(stats, city, player, unit);
			} else if (option is Building building) {
				// The civ's flavors make it favour some (see Flavors).
				float score = ScoreBuilding(stats, city, player, building);
				return score > 0 ? score * (float)Flavors.BuildingFactor(EngineStorage.gameData?.rules, player.civilization, building) : score;
			} else if (option is Inflow inflow) {
				return ScoreInflow(stats, city, player, inflow);
			} else {
				throw new Exception($"Unexpected producible: {option}");
			}
		}

		private static float ScoreInflow(ProducibleStats stats, City city, Player player, Inflow inflow) {
			// The AI never builds Wealth: the owner's choice is that more units
			// are always worth more to it than the gold. (It used to, when its
			// treasury would otherwise run dry within a few turns.)
			return float.NegativeInfinity;
		}

		internal static float ScoreUnit(ProducibleStats stats, City city, Player player, UnitPrototype unit) {
			// Armies are only built on trial (see ArmyAI.ScoreArmy).
			if (unit.isArmy) {
				return C7Engine.AI.UnitAI.ArmyAI.ScoreArmy(city, player, unit, stats.atWar) ?? -1000f;
			}

			// The owner's choice: the AI never builds Explorers.
			if (unit.name == "Explorer") {
				return int.MinValue;
			}

			bool isSettler = unit.actions.Contains(UnitAction.BuildCity);
			bool isWorker = unit.isWorker;
			bool atWar = stats.atWar;

			float attackWeight = atWar ? 15 : 10;
			float defenseWeight = atWar ? 15 : 10;
			float populationWeight = 10;
			float populationCostPenalty = populationWeight * unit.populationCost;
			float noTradeAccessBoost = 10;

			float score = 0;

			////////////////////////////////////////////////////////////////////
			///
			/// Raw score adjustments based on unit stats.
			///

			// Weight the unit's attack and defense score by comparing it to the
			// best stat available. If no option has any attack (or defense),
			// the stat doesn't tell options apart (and 0 / 0 would make every
			// unit's score NaN, so none could be chosen).
			if (stats.bestAttack > 0) {
				score += unit.attack / stats.bestAttack * attackWeight;
			}
			if (stats.bestDefense > 0) {
				score += unit.defense / stats.bestDefense * defenseWeight;
			}

			// Penalize more expensive units.
			score -= city.TurnsToProduce(unit);

			// Penalize units that consume population.
			score -= populationCostPenalty;

			// On trial: the productive cities arm a war campaign that needs it.
			if (AiExperiments.Has(player, "war-production") || TotalWar.On(player) || ((EarlyRush.On(player) || ArcherRush.On(player) || PhasePlan.On(player)) && WarCampaign.HasRush(player))
					|| (PreyWar.On(player) && WarCampaign.HasPreyCampaign(player))) {
				score += WarCampaignUnitBonus(city, player, unit, stats.bestAttack, stats.bestDefense);
			}
			// Switch horde-war: the most productive cities build the horde
			// (see HordeWar).
			if (HordeWar.On(player)) {
				score += HordeWar.ProductionBonus(city, player, unit, stats.CityGuarded);
			}

			////////////////////////////////////////////////////////////////////
			///
			/// Naval units
			///

			if (unit.categories.Contains("Sea")) {
				// A settler waiting here to sail gets its transport first.
				if (stats.NeedsTransport && stats.CityGuarded && SeaRoutes.IsTransport(unit) && unit == SeaRoutes.BuildableTransport(player)) {
					return TransportScore;
				}

				// Don't bother building naval units unless we border the ocean.
				// This does mean that inland seas are excluded.
				if (!stats.NeighborsOcean) {
					return int.MinValue;
				}

				// Don't built a naval unit if we wouldn't explore with it, or
				// fight with it (see NavalAI). Transports are built
				// for settlers waiting for one, above.
				if (!stats.WouldExplore(unit)) {
					if (!NavalAI.IsWarship(unit) || !NavalAI.WantsWarships(player, city)) {
						return int.MinValue;
					}
					score += NavalAI.WarshipBonus;
				}
			}

			////////////////////////////////////////////////////////////////////
			///
			/// Adjustments based on the city situation.
			///

			// Prioritize defending the city if it is unguarded.
			if (!stats.CityGuarded && unit.defense == 0) {
				return int.MinValue;
			}

			// Prioritize building settler escorts if we don't have one.
			if (stats.HasUnescortedSettler && unit.defense == 0) {
				return int.MinValue;
			}

			// Penalize going over the unit support cap unless we're at war or
			// still in the expansion phase, and even then if we can't afford
			// it: units we can't pay for are disbanded anyway.
			int unitSupportCost = stats.UnitSupportCost;
			// Not for a settler, though: it becomes a city, which pays for
			// itself in support.
			if (unitSupportCost > 0 && !isSettler) {
				if (!atWar && !stats.InExpansionPhase) {
					score -= unitSupportCost / 2;
				}
				if (HasWeakEconomy(player)) {
					score -= unitSupportCost * 2;
				}
			}

			////////////////////////////////////////////////////////////////////
			///
			/// Adjustments for settlers and workers.
			///

			// Don't built a worker or settler if we don't have enough population.
			// (fast-expand, Advanced only: a settler may be started a little
			// before the city has grown enough for it; see FastExpand.)
			if (CityIsTooSmall(stats, city, unit)
					&& !(isSettler && FastExpand.On(player) && stats.FastExpandActive && FastExpand.MayPrebuild(stats, city, unit))) {
				return int.MinValue;
			}

			if (AiExperiments.Has(player, DevelopmentAI.LeanMilitary)) {
				score += DevelopmentAI.LeanMilitaryAdjustment(stats, unit);
			}

			score += GranaryPump.UnitAdjustment(stats, city, player, unit);
			// grow-to-settle (Advanced only): a city about to outgrow its
			// cap or its happiness sheds citizens as settlers and workers.
			score += GrowToSettle.UnitAdjustment(stats, city, player, unit);

			// fast-expand (Advanced only): while expanding at peace, one
			// defender a city and settlers first (see FastExpand).
			if (FastExpand.On(player) && stats.FastExpandActive) {
				score += FastExpand.UnitAdjustment(stats, player, unit);
			} else {
				score += StockGarrisonBonus(player, unit, stats);
				if (isSettler && FastExpand.On(player)) {
					score += FastExpand.OverseasSettlerAdjustment(city, player);
				}
			}
			score += LeanGarrison.DefenderAdjustment(stats, city, player, unit);
			// Kings & Nomads: defenders, settlers from captured cities, and
			// attackers for the horde (see KingsAndNomadsAI.UnitBonus).
			score += KingsAndNomadsAI.UnitBonus(city, player, unit);

			// The other cities build rounds of siege engines for a war
			// campaign, to march with the army and soften the cities (owner's
			// design, 2026-10-25; see SiegeBonus).
			if (UsesUnitBuilders(player) && stats.CityGuarded && unit.IsLandUnit() && unit.bombard > 0 && unit.attack == 0
					&& !UnitBuilders(player).Contains(city) && WarCampaign.WantsBombarders(player, SiegeEngines)) {
				score += SiegeBonus;
			}

			// The unit builders raise the army, the other cities everything
			// else (once the city has a defender). See UnitBuilders.
			if (UsesUnitBuilders(player) && stats.CityGuarded && !isSettler && !isWorker
					&& unit.IsLandUnit() && unit.attack + unit.defense > 0) {
				score += UnitBuilders(player).Contains(city) ? UnitBuilderBonus : -OtherCityUnitPenalty;
			}

			if (isSettler) {
				// Don't build settlers if we don't have anywhere to go or if we
				// already have enough settlers under construction to fill all
				// the spots.
				if (stats.NumberOfReachableOpenCitySpots <= stats.SettlersUnderConstruction) {
					return int.MinValue;
				}

				// An open spot is worth more than another unit or building (see
				// InExpansionPhase): negate the population penalty, and add
				// more the more open spots there are for each of our cities.
				float sitesPerCity = stats.NumberOfReachableOpenCitySpots / (float)Math.Max(1, player.cities.Count);
				score += populationCostPenalty + SettlerSiteBonus * Math.Clamp(sitesPerCity, 1f, 3f);

				// Slightly penalize going over the optimal number of cities.
				if (player.cities.Count > EngineStorage.gameData.map.optimalNumberOfCities) {
					score *= 2.0f / 3.0f;
				}

				// Under Rising Tide settlers grow the civ and carry it
				// inland, the more so from a city the sea will soon take
				// (see TideAI).
				if (TideAI.IsOn()) {
					bool doomed = TideAI.CityTurnsLeft(city) <= TideAI.DoomedCityTurns || !TideAI.HasRefuge(player);
					score += doomed ? TideAI.DoomedSettlerBonus : TideAI.SettlerBonus;
				}
			}

			// Under Rising Tide, a unit the city can't finish before the sea
			// takes it would drown with the city. A land unit still comes
			// before a building, as the city may hurry it (see
			// TideAI.HurryEvacuation), a settler first. With no refuge, a
			// settler keeps its worth: hurried, it is the civ's only way on.
			if (TideAI.TooLateToBuild(city, unit) && !(isSettler && !TideAI.HasRefuge(player))) {
				if (unit.IsLandUnit()) {
					return isSettler ? -400f : -500f;
				}
				return -1000f;
			}

			if (isWorker) {
				// The stock AI "assigns a point value of zero to the worker
				// build option when it feels it has enough of them" (Flintlock,
				// reading the code), and was seen to build "a number of Workers
				// equivalent to 80-90% of its number of cities at the peak"
				// (Erlend Philip Wood); doc/civ3-compendium/08, section 3.2.
				// (grow-to-settle lets a city about to outgrow its limits
				// build a few more; see GrowToSettle.)
				if (stats.NumWorkers >= EnoughWorkers(player, stats.InExpansionPhase) + GrowToSettle.ExtraWorkers(stats, city, player)) {
					return int.MinValue;
				}
				// If we have unworked tiles, boost the odds of producing a
				// worker.
				int numUnworkedTiles = stats.NumUnworkedTiles;
				if (numUnworkedTiles > 0) {
					score += populationCostPenalty * Math.Min(3.0f, 1 + numUnworkedTiles);
				}

				// If we have a few cities but don't have trade access to the
				// capital, boost the odds of a worker.
				// Not for a city on another landmass, which workers can't join
				// up by road.
				if (player.cities.Count > 4 && !stats.ConnectedToCapital
						&& city.location.continent == (player.cities.Find(c => c.IsCapital()) ?? player.cities[0]).location.continent) {
					score += noTradeAccessBoost;
				}
			}

			return score;
		}

		// How many workers the AI wants: 85% of its cities, the middle of the
		// 80-90% seen in Civ3 (see ScoreUnit), and at least one.
		internal const float WorkersPerCity = .85f;
		// With the workers-1-2 trial (Advanced only; Suede keeps 1.2-1.4):
		// one per city while expanding, 1.2 once expansion ends.
		internal static int EnoughWorkers(Player player, bool expanding = true) {
			float perCity = WorkersPerCity;
			if (player.IsAdvancedAi && AiExperiments.Has(player, DevelopmentAI.WorkersOneTwo)) {
				perCity = expanding ? 1f : 1.2f;
			}
			// Advanced AI: the workers category weight (50 = as is).
			double workersWeight = C7Engine.AI.Advanced.RuleWeights.Effective(player, C7Engine.AI.Advanced.RuleCategory.Workers);
			if (workersWeight != C7Engine.AI.Advanced.RuleWeights.Neutral) {
				perCity *= (float)(workersWeight / C7Engine.AI.Advanced.RuleWeights.Neutral);
				C7Engine.AI.Advanced.RuleWeights.Used(player, C7Engine.AI.Advanced.RuleCategory.Workers, "production");
			}
			return Math.Max(1, (int)Math.Ceiling(player.cities.Count * perCity));
		}

		internal static float ScoreBuilding(ProducibleStats stats, City city, Player player, Building building) {
			bool atWar = stats.atWar;

			// Under Rising Tide, nothing the city won't keep long enough to
			// use (see TideAI).
			if (TideAI.TooLateToBuild(city, building)) {
				return -1000f;
			}

			float score = 0;

			// Great wonders (see WonderPlanner): the AI builds them in its
			// safe, productive cities. On trial: only at the right time, and
			// valued by their effect instead of the culture and happiness
			// terms below.
			bool valuedWonder = false;
			if (building.IsGreatWonder()) {
				if (AiExperiments.Has(player, WonderPlanner.Timing) && !WonderPlanner.TimingAllows(stats, city, building)) {
					return -1000f;
				}
				if (!player.isHuman) {
					float? site = WonderPlanner.SiteAdjustment(stats, city, player, building);
					if (site == null) {
						return -1000f;
					}
					score += site.Value;
				}
				// Advanced AI: the wonders category weight (50 = as is).
				double wondersWeight = C7Engine.AI.Advanced.RuleWeights.Effective(player, C7Engine.AI.Advanced.RuleCategory.Wonders);
				if (wondersWeight != C7Engine.AI.Advanced.RuleWeights.Neutral) {
					score += (float)((wondersWeight - C7Engine.AI.Advanced.RuleWeights.Neutral) / C7Engine.AI.Advanced.RuleWeights.Neutral * C7Engine.AI.Advanced.PlanEffects.FocusPoints(player));
					C7Engine.AI.Advanced.RuleWeights.Used(player, C7Engine.AI.Advanced.RuleCategory.Wonders, "production");
				}
				if (WonderPlanner.CivStyle(player)) {
					// The standard AI, as Civ3's (see WonderPlanner.CivStyle):
					// a wonder or two at a time, valued by its effect, with
					// Civ3's favourites a little ahead.
					if (!WonderPlanner.RoomForAnother(stats, city, building)) {
						return -1000f;
					}
					valuedWonder = true;
					score += WonderPlanner.EffectScore(stats, city, player, building);
					if (WonderPlanner.IsFavourite(building)) {
						score += WonderPlanner.FavouriteBonus;
					}
				} else if (AiExperiments.Has(player, WonderPlanner.Value)) {
					valuedWonder = true;
					score += WonderPlanner.EffectScore(stats, city, player, building);
				}
			}

			if (building.isSmallWonder) {
				float? smallWonderScore = ScoreSmallWonder(stats, city, player, building);
				if (smallWonderScore == null) {
					return -1000f;
				}
				score += smallWonderScore.Value;
			}

			// Marketplaces should be prioritized in cities with high population
			// that have a decent number of luxuries. Otherwise they don't do
			// much.
			if (building.increasesLuxuryTrade) {
				int luxuryCount = stats.LuxuryCount;
				score += Math.Max(city.residents.Count - luxuryCount, 0) * (luxuryCount / 2);
			}

			// Only build an aqueduct if we need one: the city is held at the
			// size cap (it can't grow by one), or is one short of it and
			// growing. A capped city's citizens work for shields and trade,
			// not the food it can't use (see CityTileAssignmentAI), so its
			// surplus is about zero and can't be what tells it apart.
			if (building.allowsCitySize2 && NeedsRoomToGrow(city, player.rules.GrowthLimitWithoutAqueduct, stats)) {
				score += 50;
			}

			// Ditto with hospitals.
			if (building.allowsCitySize3 && NeedsRoomToGrow(city, player.rules.GrowthLimitWithoutHospital, stats)) {
				score += 70;
			}

			// A town that is at war might consider building walls, otherwise it
			// probably shouldn't: at peace only one near a rival's city,
			// where an attack would come (interior walls are upkeep for
			// nothing).
			if (building.providesWalls) {
				score += atWar ? 20 : DevelopmentAI.NearRivalCity(player, city) ? 8 : -10;
				// Kings & Nomads: walls for the King's city.
				score += KingsAndNomadsAI.BuildingBonus(city, player, building);
			}

			// If a city has a decent number of corrupt shields, prioritize
			// building a courthouse based on how many corrupt shields are in
			// play.
			//
			// Ensure we don't build a courthouse in the capital though!
			if (building.reducesCorruption) {
				if (city.capital) {
					return int.MinValue;
				}

				// Lost trade counts as well as lost shields: a distant trade
				// city can lose half its commerce and few shields.
				CorruptableValue prod = stats.CurrentProductionYield;
				CommerceBreakdown commerce = stats.Commerce;
				int lost = prod.corrupt + commerce.corrupted;
				int kept = prod.useful + commerce.taxes + commerce.beakers + commerce.happiness + commerce.wealth;
				if (kept > 0 && ((float)lost) / kept > .15) {
					score += prod.corrupt * 8 + commerce.corrupted * 4;
				}
			}

			// Boost buildings that help with happiness if we have some unhappy
			// citizens in the city or if we have a decently large value on the
			// luxury slider.
			if (building.contentFacesInCity > 0 && !valuedWonder) {
				(int unhappyCount, int entertainerCount) = stats.UnhappyAndEntertainers;

				if (unhappyCount > 0 || (player.luxuryRate > 3 && city.residents.Count > 4)) {
					score += Math.Min(unhappyCount, building.contentFacesInCity) * 10;
					score += Math.Min(entertainerCount, building.contentFacesInCity) * 10;
				}
			}

			// Boost buildings that provide culture, especially if could claim
			// additional tiles by expanding our borders, or if we are next to
			// an enemy city.
			if (building.culturePerTurn > 0 && !valuedWonder) {
				(int unclaimedTilesInOuterRing, int enemyTilesInOuterRing) = stats.OuterRingTiles;

				if (stats.CulturePerTurn == 0) {
					score += building.culturePerTurn * (unclaimedTilesInOuterRing + enemyTilesInOuterRing * 3);
				} else {
					score += building.culturePerTurn * (unclaimedTilesInOuterRing + enemyTilesInOuterRing * 3) / 2.0f;
				}
			}

			// Prioritize barracks in larger cities, and especially if we are at
			// war.
			if (building.providesVeteranGroundUnits) {
				score += (city.residents.Count / 5) * (atWar ? 3 : 1);
				// On trial: the city an early rush gathers in raises veterans
				// (see EarlyRush).
				if (EarlyRush.On(player) && WarCampaign.RushStagingCity(player) == city) {
					score += EarlyRush.BarracksBonus;
				}
				// And the muster cities of the phase-plan script.
				if (PhasePlan.WantsBarracks(player, city)) {
					score += EarlyRush.BarracksBonus;
				}
				// Switch horde-war: the horde's builders, before it sets out
				// (see HordeWar.BarracksScore).
				if (HordeWar.On(player)) {
					score += HordeWar.BarracksScore(city, player);
				}
				// Barracks in the core cities that raise the army (see
				// CoreBarracksBonus).
				if (!atWar && city.residents.Count >= CoreBarracksMinSize
						&& (!KingsAndNomadsAI.PlaysFor(player) || player.cities.Count >= KingsAndNomadsBarracksCities)
						&& (UsesUnitBuilders(player) ? UnitBuilders(player).Contains(city)
							: player.cities.OrderByDescending(c => c.CurrentProductionYield().useful).ThenBy(c => c.id.ToString())
								.Take(CoreBarracksCities).Contains(city))) {
					score += CoreBarracksBonus;
				}
			}

			// The space race: the Apollo Program opens it, and every
			// spaceship part brings the AI closer to winning. Parts are only
			// offered while the ship still needs them, so favor them strongly,
			// a bit less while at war.
			if (SpaceRace.SpaceRaceAllowed(EngineStorage.gameData)) {
				if (building.IsSpaceshipPart) {
					score += atWar ? 40 : 70;
				} else if (building.buildSpaceshipParts && !SpaceRace.ApolloProgramBuilt(EngineStorage.gameData)) {
					score += atWar ? 20 : 40;
				}
			}

			if (AiExperiments.Has(player, DevelopmentAI.LeanMilitary)) {
				score += DevelopmentAI.LeanMilitaryAdjustment(stats, building);
			}

			score += GranaryPump.BuildingBonus(stats, city, player, building);

			// What the improvement's effect is worth here (see
			// BuildingEffects).
			float? effect = BuildingEffects.Score(stats, city, player, building);
			if (effect == null) {
				return -1000f;
			}
			score += effect.Value;

			// Penalize more expensive buildings. The standard AI doesn't hold
			// a great wonder's build time against it more while expanding:
			// Civ3's AI weighs wonders with everything else all game.
			bool civStyleWonder = building.IsGreatWonder() && WonderPlanner.CivStyle(player);
			score -= city.TurnsToProduce(building) / (stats.InExpansionPhase && !civStyleWonder ? 1 : 2);

			// Penalize buildings with higher maintenance costs, as much of
			// them as a house rule leaves us to pay (see AiHouseRules).
			float maintenance = building.maintenanceCost * AiHouseRules.BuildingMaintenancePercent(player) / 100f;
			score -= maintenance;

			// Boost buildings a bit if we aren't at war and aren't expanding.
			if (!stats.InExpansionPhase && !atWar) {
				score += 20;
			}

			// Penalize buildings if economy is weak
			if (HasWeakEconomy(player)) {
				score -= 10 * maintenance;
			}

			// fast-expand (Advanced only): settlers before buildings while
			// expanding at peace (see FastExpand).
			if (FastExpand.On(player) && stats.FastExpandActive) {
				score += FastExpand.BuildingAdjustment(building);
			}

			return score;
		}

		// The extra score of a small wonder for its effect, or null if it isn't
		// worth building here at all.
		private static float? ScoreSmallWonder(ProducibleStats stats, City city, Player player, Building building) {
			bool atWar = stats.atWar;
			float score = 0;

			// The army wonders are wasted on an AI that doesn't build armies
			// (on trial, see ArmyAI.ScoreArmyWonder).
			if (building.allowsBuildArmy || building.allowsLargerArmies) {
				float? armyScore = C7Engine.AI.UnitAI.ArmyAI.ScoreArmyWonder(player, building, atWar);
				if (armyScore == null) {
					return null;
				}
				score += armyScore.Value;
			}

			// A Forbidden Palace (or Secret Police HQ) pays off in a large
			// empire, in a city that would become the center for several
			// cities now closer to it than to the palace or another one.
			if (building.isForbiddenPalace) {
				List<City> centers = player.citiesWithCorruptionWonders;
				if (player.cities.Count < 6 || centers.Count == 0) {
					return null;
				}
				int served = 0;
				foreach (City c in player.cities) {
					int toCenter = centers.Min(x => c.location.RankDistanceTo(x.location));
					if (c.location.RankDistanceTo(city.location) < toCenter) {
						++served;
					}
				}
				if (served < 3) {
					return null;
				}
				score += served * 6;
			}

			// Wall Street: 5% interest on the treasury, up to 50 gold.
			if (building.treasuryEarnsInterest) {
				score += Math.Min(player.gold / 20, 50) * 2;
			}

			// The Iron Works: a big production boost in an already productive
			// city.
			if (building.productionBonusPercent > 0) {
				score += stats.CurrentProductionYield.useful * building.productionBonusPercent / 100f;
			}

			if (building.increasesLeaderChance) {
				score += atWar ? 15 : 5;
			}
			if (building.allowsEnemyTerritoryHealing) {
				score += atWar ? 20 : 0;
			}
			if (building.decreasesMissileSuccess) {
				score += atWar ? 15 : 0;
			}
			if (building.buildSpaceshipParts) {
				score += 15;
			}
			if (building.allowsSpyMissions) {
				score += 5;
			}

			return score;
		}

		internal static ProducibleStats CalculateStats(City city, Player player, List<IProducible> options) {
			ProducibleStats stats = new(city, player);

			foreach (IProducible option in options) {
				if (option is UnitPrototype unit) {
					stats.bestAttack = Math.Max(stats.bestAttack, unit.attack);
					stats.bestDefense = Math.Max(stats.bestDefense, unit.defense);
				}
			}

			return stats;
		}

		private static (int unclaimed, int enemy) CountOuterRingTiles(City city) {
			int unclaimedTilesInOuterRing = 0;
			int enemyTilesInOuterRing = 0;
			foreach (Tile t in city.location.GetTilesWithinRankDistance(2)) {
				Player owner = t.OwningPlayer();
				if (owner == null) {
					++unclaimedTilesInOuterRing;
				} else if (owner != city.owner) {
					++enemyTilesInOuterRing;
				}
			}
			return (unclaimedTilesInOuterRing, enemyTilesInOuterRing);
		}

		private static (int unhappy, int entertainers) CountUnhappyAndEntertainers(City city) {
			int unhappyCount = 0;
			int entertainerCount = 0;
			foreach (CityResident cr in city.residents) {
				// Specialists' moods are stale and don't count.
				if (cr.citizenType.IsDefaultCitizen && cr.mood == CityResident.Mood.Unhappy) { ++unhappyCount; }
				if (cr.citizenType.Luxuries > 0) { ++entertainerCount; }
			}
			return (unhappyCount, entertainerCount);
		}

		// High enough to come before anything else the city might build: the
		// settler and its escort wait idle in port until the transport is done.
		// (A city without a defender builds one first; see NeedsTransport's
		// caller.)
		private const float TransportScore = 500;

		// HEURISTIC: barracks in the civ's unit builders (see UnitBuilders;
		// in Kings & Nomads, its CoreBarracksCities most productive cities)
		// of at least CoreBarracksMinSize get
		// CoreBarracksBonus, in peacetime, between wars (and in Kings &
		// Nomads once the civ has KingsAndNomadsBarracksCities cities), so
		// that the next army comes out veteran (owner: "+1 health on units is
		// a valuable stat"). Otherwise the AI hardly built any: they add no
		// yield, which is how it values improvements (see BuildingEffects).
		// In AI trials (paired, half the civs) the veteran share of these
		// civs' land units rose from 15% to 28% in Kings & Nomads on
		// Continents, where they lost 4 fewer units and were 15 stronger,
		// and from 6-7% to 8-9% in ordinary games, with no significant
		// change in score or cities; in Kings & Nomads on Pangaea, from 10%
		// to 22%, with perhaps a city fewer (-0.96 +- 0.59). Built at war
		// too, they cost Kings & Nomads civs on Pangaea 23 beakers a turn.
		private const int CoreBarracksCities = 2;
		private const int CoreBarracksMinSize = 4;
		private const float CoreBarracksBonus = 25;
		private const int KingsAndNomadsBarracksCities = 3;

		// The civ's unit builders, the cities that raise its army, as a
		// strong Civ3 player keeps them: its most productive (in shields
		// after waste) of at least UnitBuilderMinSize, losing less than
		// UnitBuilderMaxCorruption of their commerce, two of them, or three
		// with eight cities or more. Their land units score UnitBuilderBonus
		// more, other cities' OtherCityUnitPenalty less, and they get the
		// civ's barracks (see CoreBarracksBonus). Worked out once a turn.
		//
		// In AI trials (paired, 20 games of 6 civs and 150 turns, half the
		// civs) the civs keeping unit builders got 17.7 more beakers a turn
		// on Pangaea (+0.7 cities) and 8 more units on Continents (+0.4
		// cities). Not in Kings & Nomads, where a civ's few small captured
		// cities all raise its hordes: there it cost them a city on Pangaea
		// and 3 more units lost on Continents.
		private static readonly ConditionalWeakTable<Player, Tuple<int, HashSet<City>>> unitBuilders = new();

		internal static bool UsesUnitBuilders(Player player) => !KingsAndNomadsAI.PlaysFor(player);

		internal static HashSet<City> UnitBuilders(Player player) {
			int turn = EngineStorage.gameData?.turn ?? 0;
			if (unitBuilders.TryGetValue(player, out Tuple<int, HashSet<City>> cached) && cached.Item1 == turn) {
				return cached.Item2;
			}
			int count = player.cities.Count >= 8 ? 3 : 2;
			HashSet<City> result = player.cities
				.Where(c => c.residents.Count >= UnitBuilderMinSize && c.corruption < UnitBuilderMaxCorruption)
				.OrderByDescending(c => c.CurrentProductionYield().useful).ThenBy(c => c.id.ToString())
				.Take(count).ToHashSet();
			unitBuilders.AddOrUpdate(player, Tuple.Create(turn, result));
			return result;
		}

		private const int UnitBuilderMinSize = 3;
		// HEURISTIC: a city other than the unit builders scores siege
		// engines (bombard units that don't attack) SiegeBonus more while a
		// war campaign has fewer than SiegeEngines (a campaign takes three,
		// see WarCampaign), counting those being built. Before, the AI
		// hardly built any: they have no attack or defence to score.
		private const int SiegeEngines = 3;
		private const float SiegeBonus = 30;
		private const float UnitBuilderMaxCorruption = 0.25f;
		private const float UnitBuilderBonus = 10;
		private const float OtherCityUnitPenalty = 5;

		private static bool NeedsTransport(City city, Player player) {
			bool waiting = false;
			foreach (MapUnit u in player.units) {
				if (u.currentAI is SettlerAI settlerAi && settlerAi.data.port == city && !FerryAI.HasLiveFerry(settlerAi.data, player)) {
					waiting = true;
					break;
				}
			}
			if (!waiting || SeaRoutes.TransportsReaching(player, city.location).Any()) {
				return false;
			}
			foreach (City c in player.cities) {
				if (c != city && c.itemBeingProduced is UnitPrototype type && SeaRoutes.IsTransport(type)) {
					return false;
				}
			}
			return true;
		}

		// How much more a city wants the unit for a war campaign (see
		// WarCampaign): attackers while the campaign is short of them, in
		// cities producing at least as much as our average, and a couple of
		// siege engines.
		private static float WarCampaignUnitBonus(City city, Player player, UnitPrototype unit, float bestAttack, float bestDefense) {
			if (!unit.IsLandUnit() || unit.isArmy || bestAttack <= 0) {
				return 0;
			}
			if (unit.attack > unit.defense && WarCampaign.WantsAttackers(player)) {
				float average = (float)player.cities.Average(c => c.CurrentProductionYield().useful);
				// The phase-plan script's army comes from its muster cities.
				if (WarCampaign.HasPhaseCampaign(player) ? PhasePlan.IsMusterCity(player, city) : city.CurrentProductionYield().useful >= average) {
					// An archer rush takes the best attack per shield (a
					// little for the best attack, to break ties).
					if (WarCampaign.HasArcherRush(player)) {
						return 20 * ArcherRush.AttackPerShield(unit) / ArcherRush.BestAttackPerShield(player) + 5 * unit.attack / bestAttack;
					}
					return 20 * unit.attack / bestAttack;
				}
			} else if (unit.defense > unit.attack && bestDefense > 0 && WarCampaign.WantsEscort(player)) {
				// The defender an archer rush marches with.
				return 20 * unit.defense / bestDefense;
			} else if (unit.bombard > 0 && unit.attack == 0 && WarCampaign.WantsBombarders(player)) {
				return 30;
			}
			return 0;
		}

		private static bool HasUnescortedSettler(City city) {
			foreach (MapUnit u in city.location.unitsOnTile) {
				if (u.currentAI is SettlerAI settlerAi && settlerAi.data.escort == null) {
					return true;
				}
			}
			return false;
		}

		// HEURISTIC: what each open city spot per city of ours adds to a
		// settler's score, counting at least one and at most three. With
		// less (it used to add nothing once the spots were no more than the
		// cities, and rounded the ratio down) an Archer outscored a settler
		// for the last few spots, and on archipelagos the AI stopped at 4 or
		// 5 cities. In AI trials (8 paired games, 150 turns) this gave civs
		// 3.3 more cities each on Archipelago, reaching 10 cities 43 turns
		// sooner, and changed nothing measurable on Pangaea but a smaller
		// military.
		private const float SettlerSiteBonus = 20;

		// The AI builds up to the garrison Civ3's war plotting looks for,
		// two defensive units per city and one offensive unit per city by
		// the units' BIQ AI strategy (see StockWarPlotting.RollsToPlot).
		// justanick (CFC 703785, #13): "Once AI has 2 defenders and 1
		// attacker per city it is eager to start wars, but not before
		// that"; Ision (CFC 92917): the AI keeps "heavy garrisons in every
		// city" (compendium 08, sections 3.4.1 and 3.5). Without it the AI
		// kept about 1.5 of each per city, so the plotting rolls for the
		// garrison nearly always failed. AI trials (8 games of 200 turns):
		// paired, score -67 +- 54 on Continents and +64 +- 72 on Pangaea,
		// with 9 and 15 more units per civ. With every civ on it, 2.3
		// defensive units per city, and wars declared per civ 0.7 and 0.6
		// (from 0.2 and 0.4), cities captured 1.5 and 1.2 (from 0.7 and
		// 1.2), but scores 77 and 47 lower. Adopted as Civ3's logic (owner's
		// decision, 2026-10-20); the stock-garrison switch is gone.
		//
		// HEURISTIC: what a unit the garrison is short of adds to its
		// score: as much as the best attack or defence on offer.
		private const float GarrisonShortBonus = 10;

		//
		// lean-garrison (Advanced only, see LeanGarrison): the defenders
		// wanted are what each city's place calls for, and those it saves
		// on Civ3's two a city are wanted as attackers instead.
		internal static float StockGarrisonBonus(Player player, UnitPrototype unit, ProducibleStats stats = null) {
			bool defensive = StockWarPlotting.IsDefensive(unit);
			bool offensive = StockWarPlotting.IsOffensive(unit);
			if (!defensive && !offensive) {
				return 0;
			}
			int cities = player.cities.Count;
			int have = 0;
			int defendersInCities = 0, allDefenders = 0, allAttackers = 0;
			foreach (MapUnit u in player.units) {
				bool d = StockWarPlotting.IsDefensive(u);
				if (d) {
					++allDefenders;
					if (u.location?.cityAtTile?.owner == player) {
						++defendersInCities;
					}
				}
				if (StockWarPlotting.IsOffensive(u)) {
					++allAttackers;
				}
				if (defensive ? d : StockWarPlotting.IsOffensive(u)) {
					++have;
				}
			}
			int want = defensive ? 2 * cities : cities;
			if (!LeanGarrison.On(player) && defensive) {
				// Only defenders in the cities count towards their two each,
				// not those escorting or in the field. With the cities held,
				// the AI keeps building defenders as Civ3's does, which
				// "likes" "between a 3:2 and 4:3 ratio of D:A units" and
				// builds "D : O : other ~= 53% : 43% : 4%" in peacetime
				// (Ozymandias, CFC 686973, about 560 AI builds; compendium 08).
				// Here, at least 4 defenders for each 3 attackers.
				bool citiesShort = defendersInCities < want;
				bool ratioShort = 3 * allDefenders < 4 * allAttackers;
				return citiesShort || ratioShort ? GarrisonShortBonus : 0;
			}
			if (LeanGarrison.On(player)) {
				int leanDefenders = stats?.LeanGarrisonNeed
					?? LeanGarrison.Need(player, IsInAnyWar(player, EngineStorage.gameData.players));
				want = defensive ? leanDefenders : cities + System.Math.Max(0, 2 * cities - leanDefenders);
			}
			return have < want ? GarrisonShortBonus : 0;
		}

		internal static int NumberOfReachableOpenCitySpots(City city) {
			int result = 0;

			// Note: GetScoredSettlerCandidates already excludes tiles with
			// settlers moving towards them.
			Dictionary<Tile, float> scoredLocations = SettlerLocationAI.GetScoredSettlerCandidates(city.location, city.owner);
			List<KeyValuePair<Tile, float>> orderedScores = scoredLocations.OrderByDescending(t => t.Value).ToList();
			bool tide = TideAI.IsOn();

			foreach ((Tile tile, float score) in orderedScores) {
				if (scoredLocations.ContainsKey(tile)) {
					++result;

					// Remove all the spots that would become invalid locations
					// if we built this city: like SettlerLocationAI, cities
					// can't be founded within two steps of another city.
					foreach (Tile n in tile.neighbors.Values) {
						if (n == Tile.NONE) {
							continue;
						}
						scoredLocations.Remove(n);
						// Under Rising Tide only next to it (see
						// TideAI.IsNextToCity).
						if (tide) {
							continue;
						}
						foreach (Tile nn in n.neighbors.Values) {
							scoredLocations.Remove(nn);
						}
					}
				}
			}

			return result;
		}

		private static int SettlersUnderConstruction(Player player) {
			int result = 0;
			foreach (City c in player.cities) {
				if (c.itemBeingProduced is UnitPrototype unit && unit.actions.Contains(UnitAction.BuildCity)) {
					++result;
				}
			}
			return result;
		}

		private static bool CityIsTooSmall(ProducibleStats stats, City city, UnitPrototype unit) {
			if (unit.populationCost < city.residents.Count) {
				return false;
			}

			// If we would grow before the city finishes producing the unit, we
			// can build the unit.
			if (unit.populationCost == city.residents.Count && city.TurnsToProduce(unit) >= stats.TurnsUntilGrowth) {
				return false;
			}

			return true;
		}

		private static int NumUnworkedTiles(City city) {
			int result = 0;
			foreach (CityResident cr in city.residents) {
				if (cr.tileWorked != Tile.NONE && !cr.tileWorked.overlays.HasBeenImproved()) {
					++result;
				}
			}
			return result;
		}

		// Whether the treasury is a struggle: it takes more than half the
		// sliders on taxes to keep from losing money. It used to take a science rate of zero,
		// which the AI hardly ever needs (AdjustSliders keeps some science
		// whenever it can), so the AI went on adding upkeep and units until
		// maintenance ate its whole tax income.
		public static bool HasWeakEconomy(Player player) {
			return player.scienceRate < 5 && player.gold < 200;
		}
	}
}

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using C7Engine.Pathing;
using C7GameData;
using C7GameData.AIData;
using C7Engine.AI;
using C7Engine.AI.StrategicAI;
using C7Engine.AI.UnitAI;
using Serilog;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static C7GameData.PlayerRelationship;

namespace C7Engine {
	public class PlayerAI {
		private static ILogger log = Log.ForContext<PlayerAI>();

		public static readonly int MAX_LAND_EXPLORERS = 10;
		public static readonly int MAX_WATER_EXPLORERS = 4;

		public static async Task PlayTurn(Player player, GameData gameData) {
			if (player.isHuman || player.IsIndependent || !player.isIncludedInGame) {
				return;
			}
			List<Tech> techs = gameData.techs;

			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			log.Information("-> Begin " + player.civilization.cityNames[0] + " turn");

			// TODO: Before we call this method to automatically end obsolete deals, we could make this more versatile.
			// For example unless we have a good reason, as a human, receiving luxuries, gpt,
			// or having an active RoP, doesn't hurt us.
			PlayerRelationship.CheckForObsoleteDeals(player, EngineStorage.gameData.players, EngineStorage.gameData.turn);
			// Offer to renew the deals that ran out, if we still want them.
			await RenewExpiredDeals(player, gameData);

			// Tell anyone wandering around our territory to leave. Any war that
			// starts gets planned for in the priorities and unit moves below.
			// (Each step may take a while on a big map, so the window gets to
			// draw between them.)
			await EngineStorage.YieldIfFrameBudgetSpent();
			await TerritoryDemands.MakeDemands(player, gameData);
			// And demand tribute of humans weaker than us.
			await EngineStorage.YieldIfFrameBudgetSpent();
			await TributeDemands.MakeDemands(player, gameData);

			// The Advanced AI sets its plan, which the steps below read.
			C7Engine.AI.Advanced.AdvancedAI.PlayTurn(player, gameData);
			await EngineStorage.YieldIfFrameBudgetSpent();
			MaybeDoPriorityReevaluation(player);
			await EngineStorage.YieldIfFrameBudgetSpent();
			MaybePickTechToResearch(player, techs);
			GovernmentAI.PlayTurn(player, gameData);
			await EngineStorage.YieldIfFrameBudgetSpent();
			ReconsiderWealth(player);
			ReassignCitizensForGrowth(player, gameData);

			// Ask for peace in wars that are going badly.
			await EngineStorage.YieldIfFrameBudgetSpent();
			await PeaceAI.ProposePeace(player, gameData);

			// Roughly every 4 turns, see if there are trades to be made.
			// (With the trade-gpt switch, at once on meeting someone or
			// learning a tech; see TradeGpt.)
			bool tradeNow = TradeGpt.WantsToTradeNow(player);
			bool swapNow = TechSwap.WantsToSwapNow(player);
			if (tradeNow || swapNow || GameData.rng.Next(100) < TradeChance(player)) {
				await AttemptTrading(player, tradeNow, swapNow);
			}

			await EngineStorage.YieldIfFrameBudgetSpent();
			EspionageAI.PlayTurn(player, gameData);
			// Ask for a right of passage where the way to an enemy crosses a
			// third civ's land (see RightOfPassageAI).
			await RightOfPassageAI.PlayTurn(player, gameData);

			await EngineStorage.YieldIfFrameBudgetSpent();
			// Stop paying for obsolete units over the free support (see ObsoleteUnits).
			await ObsoleteUnits.DisbandOverSupport(player);
			// In Kings & Nomads, the horde marches on cities to take (see
			// KingsAndNomadsAI); WarCampaign stands down there.
			await KingsAndNomadsAI.PlayTurn(player, gameData);
			// On trial: march on enemy cities in force (see WarCampaign).
			await WarCampaign.PlayTurn(player, gameData);
			// Mobilize for a major war, and back to Normalcy when the wars
			// are over (see Mobilization.PlayAi).
			Mobilization.PlayAi(player, gameData);
			// Draft defenders in cities about to be attacked (see DraftAI).
			DraftAI.PlayTurn(player, gameData);
			await EngineStorage.YieldIfFrameBudgetSpent();
			// Airlift defenders to threatened cities (see AirliftAI).
			AirliftAI.PlayTurn(player);
			await DoUnitActions(player);
			// Units in enemy land pillage its improvements (see PillageAI).
			await PillageAI.PlayTurn(player, gameData);
			// A city under threat builds a defender, and gold goes first to
			// hurrying what is needed; upgrades come after, with what's
			// left (upgrading first spent the gold a threatened city's
			// defender needed).
			DevelopmentAI.DefendThreatenedCities(player);
			DevelopmentAI.HurryProduction(player);
			// Under Rising Tide, a city about to drown hurries the unit that
			// would carry its people out (see TideAI).
			TideAI.ReconsiderProduction(player);
			TideAI.HurryEvacuation(player);
			UpgradeUnits(player);

			// Before ending the turn, adjust our sliders. We do this after unit
			// moves so that any worker moves that finished during the unit moves
			// will be usable (like if a luxury got hooked up).
			AdjustSliders(player);

			log.Information("-> End " + player.civilization.cityNames[0] + $" turn {stopwatch.ElapsedMilliseconds} milliseconds");
		}

		// Wealth (an Inflow) is never completed, so nothing else ever moves a
		// city off it. The AI no longer chooses Wealth (see
		// ChooseProducible.ScoreInflow), but a city may be on it from an older
		// save or a human's choice before observer mode, so choose again.
		// Wealth stores no shields, so switching away loses nothing.
		private static void ReconsiderWealth(Player player) {
			foreach (City city in player.cities) {
				if (city.itemBeingProduced is Inflow) {
					IProducible choice = ChooseProducible.Choose(city, player);
					if (choice != null && choice != city.itemBeingProduced) {
						city.ChangeProduction(choice);
					}
				}
			}
		}

		// Citizens are placed when they're born, so a city that grew to its
		// size limit still works the food tiles it grew on, and its food is
		// wasted. So a city that can't grow but has food to spare places
		// its citizens again, for shields and trade (see
		// CityTileAssignmentAI.AssignNewCitizenToTile); and once it can
		// grow again (it built an aqueduct), a city with no food to spare
		// places them again for food.
		private static void ReassignCitizensForGrowth(Player player, GameData gameData) {
			foreach (City city in player.cities) {
				int surplus = city.FoodGrowthPerTurn();
				bool canGrow = city.CanGrowPopulationByOne(gameData);
				if ((!canGrow && surplus > 0)
						|| (canGrow && surplus <= 0 && city.residents.Count > 0)) {
					CityInteractions.ReassignAllCitizens(gameData, city);
				}
			}
		}

		// Upgrade units sitting in cities that can upgrade them, keeping
		// Civ3's own reserve of gold: 16 gold per city plus the treasury that
		// earns full interest, and no upgrades at all below it (see
		// doc/ai-review-2026-10.md for the trial). Flintlock, reading the 1.22 code
		// (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16937054):
		// the upgrade pass is "behind a weird little check", "The reserve
		// amount is 16 gold per city plus enough to earn the AI full interest
		// income" "if it has any wonders that grant that", and "the AI will
		// not upgrade anything when it's below the minimum gold reserve even
		// if the upgrade is free."
		private static void UpgradeUnits(Player player) {
			int GOLD_RESERVE = DevelopmentAI.StockGoldReserve(player);
			if (player.gold < GOLD_RESERVE) {
				return;
			}
			Dictionary<City, CityUpgradeInfo> upgradeInfo = new();
			foreach (MapUnit unit in player.units.ToList()) {
				UnitPrototype upgrade = GetAvailableUpgrade(unit, upgradeInfo);
				if (upgrade != null && player.gold - unit.UpgradeCost(upgrade) >= GOLD_RESERVE) {
					// We just worked out the upgrade, so don't have Upgrade()
					// look it up (and the city's resources) again.
					unit.UpgradeTo(upgrade);
				}
			}
		}

		// What a city offers for upgrading units, gathered once per city.
		private sealed class CityUpgradeInfo {
			public bool upgradesLandUnits;
			public bool upgradesSeaUnits;
			public HashSet<Resource> resources;
		}

		// Same result as MapUnit.GetAvailableUpgrade, but shares each city's
		// buildings and resources between all the units in it instead of
		// recomputing them per unit.
		private static UnitPrototype GetAvailableUpgrade(MapUnit unit, Dictionary<City, CityUpgradeInfo> upgradeInfo) {
			if (!unit.unitType.actions.Contains(UnitAction.Upgrade)) {
				return null;
			}

			City city = unit.location?.cityAtTile;
			if (!City.IsValidCity(city) || city.owner != unit.owner) {
				return null;
			}

			if (!upgradeInfo.TryGetValue(city, out CityUpgradeInfo info)) {
				info = new CityUpgradeInfo();
				foreach (CityBuilding cb in city.GetBuildings()) {
					info.upgradesLandUnits |= cb.building.providesVeteranGroundUnits;
					info.upgradesSeaUnits |= cb.building.providesVeteranSeaUnits;
				}
				upgradeInfo[city] = info;
			}

			bool hasUpgradeBuilding = (unit.IsLandUnit() && info.upgradesLandUnits)
				|| (unit.IsWaterUnit() && info.upgradesSeaUnits);
			if (!hasUpgradeBuilding) {
				return null;
			}

			info.resources ??= city.GetAvailableResources(EngineStorage.gameData).Keys.ToHashSet();
			return unit.unitType.GetProducibleUpgrade(city, info.resources);
		}

		private static void MaybeDoPriorityReevaluation(Player player) {
			// Priorities aren't saved, so a loaded game's AIs work theirs out
			// again, quietly: choosing them already happened, and doing it
			// again (declaring war, waking units) would make a reloaded game
			// play differently.
			if (player.strategicPriorityData.Count == 0 && player.turnsUntilPriorityReevaluation > 0) {
				player.strategicPriorityData.AddRange(StrategicPriorityArbitrator.Arbitrate(player));
			}

			if (player.turnsUntilPriorityReevaluation == 0) {
				log.Information("Re-evaluating strategic priorities for " + player);
				List<StrategicPriority> priorities = StrategicPriorityArbitrator.Arbitrate(player);
				player.strategicPriorityData.Clear();
				foreach (StrategicPriority priority in priorities) {
					player.strategicPriorityData.Add(priority);
					priority.OnChosen(player);
				}
				player.turnsUntilPriorityReevaluation = 15 + GameData.rng.Next(10);

				string summary = string.Join(',', player.strategicPriorityData);
				log.Information($"Player {player.civilization.name} now has priorities of: {summary}. {player.turnsUntilPriorityReevaluation} turns until next re-evaluation");

				// Wake up any fortified units so our new strategy (if we have one)
				// takes effect.
				foreach (MapUnit u in player.units) {
					u.Wake();
				}
			} else {
				player.turnsUntilPriorityReevaluation--;
			}
		}

		public static void MaybePickTechToResearch(Player player, List<Tech> techs) {
			while (player.currentlyResearchedTech == null || player.knownTechs.Contains(player.currentlyResearchedTech)) {
				OrderedTechSet available = player.GetAvailableTechsToResearch(techs);
				Tech toResearch = DevelopmentAI.ChooseResearch(player, techs, available) ?? available.FirstOrDefault();
				if (toResearch == null) {
					log.Information($"Player {player.civilization.name} has no techs available to research.");
					player.SetCurrentlyResearchedTech(null);
					break;
				}

				// Drop any queued techs we already know, so we never get stuck
				// re-selecting a known tech.
				while (player.ResearchQueue.Count > 0 && player.knownTechs.Contains(player.ResearchQueue.Peek().id)) {
					player.ResearchQueue.Dequeue();
				}
				if (player.ResearchQueue.Count <= 0) {
					player.AddTechItemToResearchQueue(toResearch);
				}
				player.SetCurrentlyResearchedTech(player.ResearchQueue.Peek().id);
				log.Information($"Player {player.civilization.name} is researching {player.ResearchQueue.Peek().Name}.");
			}
		}

		private static async Task DoUnitActions(Player player) {
			// Reorder the list so that settlers are second to last, giving our escorts a
			// chance to configure themselves without wasting a turn.
			// Finally reorder so that the workers are last, so in case a settler builds a new city,
			// they move to terraform a tile instead of wasting a turn (especially in the very first round in a new game).
			player.units = player.units.OrderBy(x => x.unitType.isSettler).ThenBy(x => x.unitType.isWorker).ToList();

			// Any time we have an unescorted settler, wake up any other units
			// on the same tile to force us to re-evaluate whether they should
			// be an escort. Otherwise, can have perfectly fine escorts sitting
			// there fortified.
			foreach (MapUnit u in player.units) {
				if (u.currentAI is SettlerAI settlerAi && settlerAi.data.escort == null) {
					foreach (MapUnit uu in u.location.unitsOnTile) {
						uu.Wake();
					}
				}
			}

			// Track the units that may be exploring, so counting explorers
			// doesn't need a pass over every unit each time a unit picks a plan.
			HashSet<MapUnit> explorers = new();
			foreach (MapUnit u in player.units) {
				if (u.currentAI is ExplorerAI) {
					explorers.Add(u);
				}
			}
			possibleExplorers[player] = explorers;
			try {
				using (WorkerAI.BeginPlanning(player)) {
					await DoUnitActions(player, explorers);
				}
			} finally {
				possibleExplorers.Remove(player);
			}
		}

		// Sails the ship to the nearest water it can't sink in, or one of
		// our cities, that it can reach. (An AI heuristic.)
		private static async Task MakeForSafeWater(MapUnit ship, Player player) {
			int reach = Math.Max(1, ship.MaxMovementPoints());
			List<Tile> safe = ship.location.GetTilesWithinRankDistance(reach * 2)
				.Where(t => t != ship.location && (t.cityAtTile?.owner == player || (t.IsWater() && !t.HasCity() && !ship.IsUnsafeWater(t))))
				.OrderBy(t => t.DistanceTo(ship.location))
				.ToList();
			if (PathingAlgorithmChooser.GetAlgorithm(ship).FindFirstReachable(ship.location, safe, ship, out TilePath path) < 0) {
				return;
			}
			while (ship.movementPoints.canMove && ship.IsInUnsafeWater() && path.PathLength() > 0) {
				Tile next = path.Next();
				if (!await ship.Move(ship.location.DirectionTo(next)) || ship.location != next) {
					break;
				}
			}
		}

		private static async Task DoUnitActions(Player player, HashSet<MapUnit> explorers) {
			// Do things with units. Copy into an array first to avoid collection-was-modified exception
			foreach (MapUnit unit in player.units.ToArray()) {
				// Let the window draw between units on a long turn.
				await EngineStorage.YieldIfFrameBudgetSpent();

				// A unit may have died or been captured since the list was
				// copied, such as a defender lost to another unit's attack.
				if (!IsStillOurs(unit, player)) {
					continue;
				}

				// Our ships end their turns where they can't sink. The paths
				// they follow keep out of such water, so one is only caught in
				// it when the water turns unsafe under it (as when the Great
				// Lighthouse is lost): it makes for safety first.
				if (unit.IsInUnsafeWater()) {
					await MakeForSafeWater(unit, player);
				}

				// Passengers on a ferry go where it takes them (see FerryAI).
				// Being woken, as all our units are when priorities change,
				// mustn't have them make other plans on the way.
				if (unit.IsLoaded() && unit.Carrier()?.currentAI is FerryAI) {
					continue;
				}

				// Under Rising Tide, a unit the coming wave would drown
				// makes for higher ground before anything else, fortified or
				// on campaign or not (see TideAI).
				if (!unit.IsLockedInArmy() && await TideAI.EvacuateIfNeeded(unit, player)) {
					continue;
				}

				// Units on a war campaign move with it (see WarCampaign).
				if (unit.currentAI is WarCampaign.MemberAI) {
					continue;
				}

				// Members of an army go where the army goes (see ArmyAI).
				if (unit.IsLockedInArmy() && ArmyAI.IsEnabled(player)) {
					continue;
				}

				// A great leader on a landmass with none of our cities is
				// disbanded, as the stock leader AI does: "Disband the unit if
				// it's on a continent with no cities of its civ. This is what
				// the original logic does" (C3X, patch_Unit_ai_move_leader,
				// https://github.com/maxpetul/C3X/blob/master/injected_code.c).
				if (IsStrandedLeader(unit, player)) {
					await unit.Disband();
					continue;
				}

				// A great leader that has reached one of our cities is used
				// up there. Until then it heads for a city like a defender.
				if (UseLeaderInCity(unit, player)) {
					continue;
				}

				// A captive that may be sacrificed in one of our cities is,
				// before anything else, as the stock unit AI does first
				// (Civ3 FUN_00463630, which sacrifices whenever FUN_005d1f20
				// allows it). Unlike the stock AI, the unit type must have
				// the Sacrifice action (see MapUnit_Sacrifice.cs).
				if (unit.CanSacrifice() && unit.Sacrifice()) {
					continue;
				}

				// Nuclear weapons fire at a worthwhile target if there is one,
				// and otherwise wait (they may be loaded, and so fortified,
				// aboard a submarine).
				if (unit.IsNuclearWeapon()) {
					if (unit.hitPointsRemaining > 0) {
						await NuclearAI.TryStrike(player, unit);
					}
					continue;
				}

				// Aircraft don't move like other units; they fly missions from
				// their bases (see AirAI). Those aboard a carrier are
				// fortified, so this comes first.
				if (unit.IsAirUnit()) {
					await AirAI.PlayTurn(player, unit);
					continue;
				}

				// Don't waste time recalculating behaviors for fortified units.
				// This means we'll have to unfortify all our units after
				// interesting events like war declarations, but this seems like
				// a good tradeoff for faster turns. A unit fortified outside our
				// land (with no city it can get to, or left behind after a
				// war) is the exception: it would sit there until our
				// priorities next change, so it thinks again each turn.
				// lean-garrison (Advanced only): so is a defender one too many
				// for its city while another city is short (see LeanGarrison).
				if (unit.isFortified) {
					if (!IsStrandedOutsideHome(unit, player) && !LeanGarrison.ShouldLeave(unit, player)) {
						continue;
					}
					unit.Wake();
					unit.currentAI = null;
				}

				// A hurt attacker stops attacking (or exploring) to heal (see
				// RestAI).
				if ((unit.currentAI is CombatAI || unit.currentAI is ExplorerAI) && RestAI.ShouldRest(unit, player)) {
					unit.currentAI = null;
				}

				// For each unit, if there's already an AI task assigned, it will attempt to complete its goal.
				// It may fail due to conditions having changed since that goal was assigned; in that case it will
				// get a new task to try to complete.
				//
				// Cap our attempts at 2 to avoid getting stuck in bad situations.
				for (int attempt = 0; attempt < 2; ++attempt) {
					if (unit.currentAI == null) {
						unit.currentAI = GetAIForUnit(unit, player);
						if (unit.currentAI is ExplorerAI) {
							explorers.Add(unit);
						}
					}

					// If the unit is still the process of doing its plan, allow
					// it to continue next turn.
					UnitAI.Result result = await unit.currentAI.PlayTurn(player, unit);
					if (result == UnitAI.Result.InProgress) {
						break;
					}

					// A plan that failed, e.g. because its destination got blocked,
					// shouldn't cost the unit the rest of its turn: pick a new one
					// now if it can still move.
					if (result == UnitAI.Result.Error) {
						unit.currentAI = null;
						if (unit.hitPointsRemaining <= 0 || !unit.movementPoints.canMove) {
							break;
						}
						continue;
					}

					if (unit.hitPointsRemaining <= 0 || unit.isFortified) {
						unit.currentAI = null;
						break;
					}

					// Otherwise we need a new plan for next turn. Pick it now
					// to avoid things like new units being preferred for
					// exploration instead of units already far away from home
					// for exploration.
					unit.currentAI = GetAIForUnit(unit, player);
					if (unit.currentAI is ExplorerAI) {
						explorers.Add(unit);
					}
				}

				// Nor does it end its turn where the wave strikes next.
				if (TideAI.IsOn() && IsStillOurs(unit, player)) {
					await TideAI.EvacuateAfterMoving(unit, player);
				}

				// Moving already adds what a unit sees to our knowledge and
				// recomputes the active tiles, as do units dying or being
				// captured, so only recompute them when this revealed new tiles.
				int knownTileCount = player.tileKnowledge.knownTiles.Count;
				player.tileKnowledge.AddTilesToKnown(unit.location, recomputeActiveTiles: false);
				if (player.tileKnowledge.knownTiles.Count != knownTileCount) {
					player.tileKnowledge.RecomputeActiveTiles();
				}
			}
		}

		// Whether the unit stands outside our land and our cities.
		private static bool IsStrandedOutsideHome(MapUnit unit, Player player) {
			return unit.IsLandUnit() && !unit.IsLoaded() && unit.location.cityAtTile?.owner != player
				&& unit.location.OwningPlayer() != player;
		}

		// Whether the unit is still alive and the player's: a unit that dies
		// leaves its owner's list of units.
		internal static bool IsStillOurs(MapUnit unit, Player player) {
			return unit.owner == player && player.units.Contains(unit);
		}

		// Whether a soldier stays near home to escort settlers rather than
		// exploring: we keep one soldier beyond each city's guard for each
		// settler we have or are building, and one more while we have
		// fewer than EARLY_EXPANSION_CITIES cities, so that settlers
		// find an escort close by (see SettlerAI.TryToTakeEscort). Owner's
		// decision, 2026-10-12: "The AI should keep some nearby to
		// escort". Before, every bonus starting unit but the capital's
		// defender went exploring, and settlers waited for escorts.
		internal static bool KeepForEscorts(MapUnit unit, Player player) {
			if (!unit.IsLandUnit() || !DevelopmentAI.IsMilitaryLandUnit(unit.unitType)) {
				return false;
			}
			int wanted = player.cities.Count < EARLY_EXPANSION_CITIES ? 1 : 0;
			int soldiers = 0;
			foreach (MapUnit u in player.units) {
				if (u.unitType.actions.Contains(UnitAction.BuildCity)) {
					++wanted;
				} else if (u != unit && u.IsLandUnit() && DevelopmentAI.IsMilitaryLandUnit(u.unitType)
						&& u.currentAI is not ExplorerAI && u.currentAI is not EscortAI) {
					++soldiers;
				}
			}
			foreach (City c in player.cities) {
				if (c.itemBeingProduced is UnitPrototype p && p.actions.Contains(UnitAction.BuildCity)) {
					++wanted;
				}
			}
			int spare = soldiers - Math.Max(1, player.cities.Count);
			return spare < wanted;
		}

		private const int EARLY_EXPANSION_CITIES = 4;

		// While a player's units are acting, the units that might have an
		// ExplorerAI: every unit that had one when the units started acting
		// or was given one since. Whether each still does is checked when
		// counting.
		private static readonly Dictionary<Player, HashSet<MapUnit>> possibleExplorers = new();

		// The number of the player's units exploring, among land units or
		// among other units.
		private static int CountExplorers(Player player, bool landUnits) {
			int result = 0;
			if (possibleExplorers.TryGetValue(player, out HashSet<MapUnit> explorers)) {
				foreach (MapUnit u in explorers) {
					// A unit that died or changed hands is no longer one of
					// our units.
					if (u.currentAI is ExplorerAI && u.IsLandUnit() == landUnits
						&& u.owner == player && u.hitPointsRemaining > 0) {
						++result;
					}
				}
				return result;
			}

			foreach (MapUnit u in player.units) {
				if (u.currentAI is ExplorerAI && u.IsLandUnit() == landUnits) {
					++result;
				}
			}
			return result;
		}

		// Whether the unit is a great leader standing on land (not aboard a
		// ship) where none of our cities is on the same landmass. UNVERIFIED:
		// whether the stock AI also checks a leader at sea; here it's left
		// alone there.
		internal static bool IsStrandedLeader(MapUnit unit, Player player) {
			if (!(unit.IsLeader() || unit.IsScientificLeader()) || unit.IsLoaded()
					|| !Tile.IsTileValid(unit.location) || !unit.location.IsLand()) {
				return false;
			}
			int continent = unit.location.continent;
			foreach (City city in player.cities) {
				if (city.location.continent == continent) {
					return false;
				}
			}
			return true;
		}

		// A leader in one of our cities finishes the city's improvement or
		// small wonder. A city building a unit is switched to its most
		// expensive improvement first. The AI doesn't use armies yet, so
		// leaders never form them.
		//
		// A city whose production is locked in (it was hurried this turn)
		// keeps the leader until next turn. One building a great wonder, or
		// with nothing a leader can hurry, sends it on to another city.
		// Returns true if the leader was used up.
		internal static bool UseLeaderInCity(MapUnit unit, Player player) {
			if (unit.IsScientificLeader()) {
				return UseScientificLeader(unit, player);
			}
			if (!unit.IsLeader() || !unit.CanOfferHurryProduction()) {
				return false;
			}
			if (UseMilitaryLeaderByChoice(unit, player) is bool used) {
				return used;
			}
			if (ArmyAI.ShouldFormArmy(unit, player)) {
				return ArmyAI.FormArmy(unit, player) != null;
			}
			// On trial: hurry the most valuable improvement the city could
			// build, rather than whichever it happens to be building.
			if (AiExperiments.Has(player, "leader-hurry-best") && unit.location.cityAtTile is City here && !here.hurriedThisTurn
					&& !(here.itemBeingProduced is Building building && building.IsGreatWonder())
					&& BestImprovementForLeader(here) is Building better && better != here.itemBeingProduced) {
				here.ChangeProduction(better);
			}
			if (unit.CanHurryProduction()) {
				return unit.HurryProductionAsLeader();
			}

			City city = unit.location.cityAtTile;
			if (city.hurriedThisTurn) {
				return false;
			}
			if (city.itemBeingProduced is Building current) {
				// An improvement finishing this turn anyway: wait for the
				// next one.
				if (!current.IsGreatWonder()) {
					return false;
				}
			} else if (BestImprovementForLeader(city) is Building improvement) {
				city.ChangeProduction(improvement);
				if (unit.CanHurryProduction()) {
					return unit.HurryProductionAsLeader();
				}
			}

			// Try another city.
			UnitAI ai = MakeLeaderAI(unit, player);
			if (ai is DefenderAI { data.destination: Tile destination } && destination != unit.location) {
				unit.currentAI = ai;
				unit.Wake();
			}
			return false;
		}

		// The rules a military leader follows in C3C: "A Military Leader can
		// be used to either create an army or to hurry production of an
		// Improvement or Small Wonder in a city" (Civilopedia, GCON_Leaders;
		// compendium 01, 1.3), never a great wonder (compendium 04, "Great
		// Leader hurry"). It forms an army in any of our cities, without a
		// Military Academy, which only lets a city build armies.
		//
		// HEURISTIC: a military leader forms an army when we are at war or
		// planning one and good attackers are at hand
		// (ArmyAI.ShouldFormArmyForWar). Otherwise it rushes the most
		// valuable improvement or small wonder in any of our cities
		// (BestBuildForMilitaryLeader), going there if need be. Returns
		// whether the leader was used up, or null with nothing worth its
		// while anywhere, to fall back on the older choice below.
		//
		// In AI trials (AiTrialTest, 8 games of 200 turns on each map) this
		// and the scientific leader's change (see UseScientificLeader) were
		// no weaker, so they were adopted: paired, +3 +- 4 on Continents and
		// +58 +- 53 on Pangaea; all civs, 1562 against 1562 and 1523 against
		// 1506 without. Military leaders hardly ever arose in those games,
		// so what the trials saw is mostly the scientific leader's change.
		private static bool? UseMilitaryLeaderByChoice(MapUnit unit, Player player) {
			if (ArmyAI.ShouldFormArmyForWar(unit, player)) {
				return ArmyAI.FormArmy(unit, player) != null;
			}
			(City target, Building item) = BestBuildForMilitaryLeader(player, unit);
			if (target == null) {
				return null;
			}
			if (unit.location == target.location) {
				if (target.itemBeingProduced != item) {
					target.ChangeProduction(item);
				}
				return unit.CanHurryProduction() && unit.HurryProductionAsLeader();
			}
			if (unit.currentAI is not DefenderAI { data.destination: Tile going } || going != target.location) {
				UnitAI ai = MakeLeaderAI(unit, player);
				if (ai is DefenderAI { data.destination: Tile destination } && destination != unit.location) {
					unit.currentAI = ai;
					unit.Wake();
				}
			}
			return false;
		}

		// HEURISTIC: the city, and improvement
		// or small wonder, a military leader would do most good rushing: of
		// what each city is building and the buildings it wants
		// (ChooseProducible.ScoreBuilding above 0 once the penalty for the
		// turns it takes, which the leader saves, is put back), the one with the most
		// shields left, counted half again for a defensive building (walls
		// or a defence bonus) in a threatened city and a quarter again for
		// one that adds production, and less 3 shields for each tile the
		// leader must walk. A build that's nearly done, with less than half
		// its cost or 3 turns of the city's shields to go, is never chosen:
		// the city will finish it soon anyway. Nor is a non-defensive build
		// in a threatened city, which may just be captured. (null, null) if
		// nothing qualifies.
		internal static (City, Building) BestBuildForMilitaryLeader(Player player, MapUnit leader = null) {
			City bestCity = null;
			Building best = null;
			double bestValue = 0;
			foreach (City city in player.cities) {
				if (city.hurriedThisTurn) {
					continue;
				}
				bool threatened = WonderPlanner.IsThreatened(city, player);
				int perTurn = Math.Max(1, city.CurrentProductionYield().useful);
				void Consider(Building b) {
					if (b.IsGreatWonder()) {
						return;
					}
					int cost = player.ShieldCost(b);
					int remaining = cost - city.shieldsStored;
					if (2 * remaining < cost || remaining <= 3 * perTurn) {
						return;
					}
					bool defensive = b.providesWalls || b.combatDefenseBonus != null;
					if (threatened && !defensive) {
						return;
					}
					double value = remaining;
					if (threatened && defensive) {
						value *= 1.5;
					} else if (b.productionBonusPercent > 0) {
						value *= 1.25;
					}
					if (leader != null && Tile.IsTileValid(leader.location)) {
						value -= 3 * leader.location.DistanceTo(city.location);
					}
					if (value > bestValue) {
						(bestCity, best, bestValue) = (city, b, value);
					}
				}
				if (city.itemBeingProduced is Building current) {
					Consider(current);
				}
				List<IProducible> options = city.ListProductionOptions(EngineStorage.gameData).ToList();
				ChooseProducible.ProducibleStats stats = ChooseProducible.CalculateStats(city, player, options);
				foreach (IProducible p in options) {
					if (p is Building b && b != city.itemBeingProduced && city.CanChangeProduction(b)
							&& ChooseProducible.ScoreBuilding(stats, city, player, b) + city.TurnsToProduce(b) > 0) {
						Consider(b);
					}
				}
			}
			return (bestCity, best);
		}

		// The most expensive improvement or small wonder the city can build
		// that a leader could hurry, or null. On trial ("leader-hurry-best"),
		// the one that would save the most shields among those the city
		// would want to build at all.
		private static Building BestImprovementForLeader(City city) {
			if (AiExperiments.Has(city.owner, "leader-hurry-best") && BestWantedImprovementForLeader(city) is Building wanted) {
				return wanted;
			}
			Building best = null;
			foreach (IProducible p in city.ListProductionOptions(EngineStorage.gameData)) {
				if (p is Building b && !b.IsGreatWonder() && city.owner.ShieldCost(b) > city.shieldsStored
						&& (best == null || city.owner.ShieldCost(b) > city.owner.ShieldCost(best))) {
					best = b;
				}
			}
			return best;
		}

		private static Building BestWantedImprovementForLeader(City city) {
			List<IProducible> options = city.ListProductionOptions(EngineStorage.gameData).ToList();
			ChooseProducible.ProducibleStats stats = ChooseProducible.CalculateStats(city, city.owner, options);
			Building best = null;
			int bestRemaining = 0;
			foreach (IProducible p in options) {
				if (p is not Building b || b.IsGreatWonder()) {
					continue;
				}
				int remaining = city.owner.ShieldCost(b) - city.shieldsStored;
				if (remaining > bestRemaining && ChooseProducible.ScoreBuilding(stats, city, city.owner, b) > 0) {
					best = b;
					bestRemaining = remaining;
				}
			}
			return best;
		}

		// A scientific leader can finish anything a city builds (owner's
		// decision, 2026-10-12; see MapUnit_ScientificLeader.cs), so it goes
		// where it saves the most shields. Wonders, great or small, are the
		// costliest things to build and great ones are beyond a military
		// leader, so it looks for one first. Failing that, it finishes the
		// costliest remaining build in a city not threatened in a war, where
		// what it finishes won't just be captured, and failing that in the
		// capital. It switches the city to the chosen item if need be (Civ3
		// has no penalty for switching between categories). With nothing to
		// finish anywhere it waits in the capital.
		// Returns true if the leader was used up.
		internal static bool UseScientificLeader(MapUnit unit, Player player) {
			(City target, IProducible item) = BestBuildForScientificLeader(player);
			if (target == null || item == null) {
				return false;
			}
			// The empire's best build changes
			// from turn to turn as cities switch production, so a leader
			// chasing it could wander for many turns (seen in AI trials: a
			// scientific leader visited a dozen cities without being used).
			// HEURISTIC: in a safe city of ours where it would save at least
			// half as many shields as at the best target, it is used here.
			if (unit.location.cityAtTile is City here && here.owner == player
					&& here != target && !here.hurriedThisTurn && !WonderPlanner.IsThreatened(here, player)
					&& BestBuildHereForScientificLeader(here) is (IProducible local, int saved)
					&& 2 * saved >= player.ShieldCost(item) - target.shieldsStored) {
				(target, item) = (here, local);
			}
			if (unit.location == target.location && unit.CanOfferHurryProduction()) {
				if (target.itemBeingProduced != item) {
					target.ChangeProduction(item);
				}
				if (unit.CanHurryProduction()) {
					return unit.HurryProductionAsLeader();
				}
				return false;
			}
			if (unit.currentAI is not DefenderAI { data.destination: Tile going } || going != target.location) {
				UnitAI ai = MakeLeaderAI(unit, player);
				if (ai is DefenderAI { data.destination: Tile destination } && destination != unit.location) {
					unit.currentAI = ai;
					unit.Wake();
				}
			}
			return false;
		}

		// The city, and item, a scientific leader should finish (see
		// UseScientificLeader): the wonder that would save the most shields;
		// else the costliest remaining build in a safe city; else the
		// capital's. The item is null if the capital has nothing to finish,
		// and the city is null too with no capital.
		internal static (City, IProducible) BestBuildForScientificLeader(Player player) {
			(City wonderCity, Building wonder) = BestWonderForScientificLeader(player);
			if (wonderCity != null) {
				return (wonderCity, wonder);
			}
			City bestCity = null;
			IProducible best = null;
			int bestRemaining = 0;
			foreach (City city in player.cities) {
				if (WonderPlanner.IsThreatened(city, player)) {
					continue;
				}
				(IProducible item, int remaining) = BestNonWonderForScientificLeader(city);
				if (item != null && remaining > bestRemaining) {
					(bestCity, best, bestRemaining) = (city, item, remaining);
				}
			}
			if (bestCity != null) {
				return (bestCity, best);
			}
			City capital = player.cities.FirstOrDefault(c => c.capital);
			return capital == null ? (null, null) : (capital, BestNonWonderForScientificLeader(capital).Item1);
		}

		// The build in the city a scientific leader would save the most
		// shields on, wonder or not, and the shields, or null with nothing
		// (see UseScientificLeader).
		private static (IProducible, int)? BestBuildHereForScientificLeader(City city) {
			(IProducible best, int bestRemaining) = BestNonWonderForScientificLeader(city);
			void Consider(Building b) {
				int remaining = city.owner.ShieldCost(b) - city.shieldsStored;
				if (remaining > bestRemaining) {
					(best, bestRemaining) = (b, remaining);
				}
			}
			if (city.itemBeingProduced is Building current && City.IsWonder(current)) {
				Consider(current);
			}
			foreach (IProducible p in city.ListProductionOptions(EngineStorage.gameData)) {
				if (p is Building b && City.IsWonder(b) && city.CanChangeProduction(b)) {
					Consider(b);
				}
			}
			return best == null ? null : (best, bestRemaining);
		}

		// The non-wonder build in the city that would save the most shields,
		// and the shields it would save: what the city is building, unit or
		// improvement, or an improvement it would want and could switch to.
		// It doesn't switch to a unit, which the city may not need.
		private static (IProducible, int) BestNonWonderForScientificLeader(City city) {
			IProducible best = null;
			int bestRemaining = 0;
			IProducible current = city.itemBeingProduced;
			if (current is UnitPrototype or Building && !City.IsWonder(current)) {
				int remaining = city.owner.ShieldCost(current) - city.shieldsStored;
				if (remaining > bestRemaining) {
					(best, bestRemaining) = (current, remaining);
				}
			}
			if (!city.hurriedThisTurn && BestWantedImprovementForLeader(city) is Building wanted && city.CanChangeProduction(wanted)) {
				int remaining = city.owner.ShieldCost(wanted) - city.shieldsStored;
				if (remaining > bestRemaining) {
					(best, bestRemaining) = (wanted, remaining);
				}
			}
			return (best, bestRemaining);
		}

		// The city, and wonder, where a scientific leader would save the most
		// shields: a wonder the city is building or could switch to. A city
		// whose production is locked in for the turn only counts with what
		// it's building.
		internal static (City, Building) BestWonderForScientificLeader(Player player) {
			City bestCity = null;
			Building best = null;
			int bestRemaining = 0;
			void Consider(City city, Building b) {
				int remaining = player.ShieldCost(b) - city.shieldsStored;
				if (remaining > bestRemaining) {
					(bestCity, best, bestRemaining) = (city, b, remaining);
				}
			}
			foreach (City city in player.cities) {
				if (city.itemBeingProduced is Building current && City.IsWonder(current)) {
					Consider(city, current);
				}
				if (city.hurriedThisTurn) {
					continue;
				}
				foreach (IProducible p in city.ListProductionOptions(EngineStorage.gameData)) {
					if (p is Building b && City.IsWonder(b) && city.CanChangeProduction(b)) {
						Consider(city, b);
					}
				}
			}
			return (bestCity, best);
		}

		// Whether a leader arriving in the city could be used there: it isn't
		// building a great wonder, and has an improvement to hurry.
		private static bool CityCanUseLeader(City city) {
			if (city.itemBeingProduced is Building b && b.IsGreatWonder()) {
				return false;
			}
			return BestImprovementForLeader(city) != null;
		}

		// Sends a leader straight to the nearest of our cities that can use
		// it, or failing that to the nearest city. A leader can't fight, so it
		// shouldn't explore or escort.
		internal static UnitAI MakeLeaderAI(MapUnit unit, Player player) {
			PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(unit);
			Tile destination = null;
			TilePath path = null;
			// A scientific leader makes for the city where it would save the
			// most, or else the capital (see UseScientificLeader).
			City scientificTarget = null;
			if (unit.IsScientificLeader()) {
				scientificTarget = BestBuildForScientificLeader(player).Item1
					?? player.cities.FirstOrDefault(c => c.capital);
			} else {
				// The city the leader would rush a build in (see
				// UseMilitaryLeaderByChoice), if any.
				scientificTarget = BestBuildForMilitaryLeader(player, unit).Item1;
			}
			foreach (bool needUsable in new[] { true, false }) {
				foreach (City city in player.cities.OrderBy(c => c.location.DistanceTo(unit.location))) {
					if (needUsable && (scientificTarget != null ? city != scientificTarget : !CityCanUseLeader(city))) {
						continue;
					}
					if (city.location == unit.location) {
						destination = city.location;
						break;
					}
					TilePath p = algorithm.PathFrom(unit.location, city.location, unit);
					if (p != null && p.PathLength() > 0) {
						destination = city.location;
						path = p;
						break;
					}
				}
				if (destination != null) {
					break;
				}
			}

			// With no city to go to, the leader stays where it is.
			DefenderAIData data = new() {
				goal = DefenderAIData.DefenderGoal.DEFEND_CITY,
				destination = destination ?? unit.location,
				defender = unit,
				pathToDestination = path,
			};
			log.Information("Sending leader {Unit} to {Destination}", unit, data.destination);
			return new DefenderAI(data);
		}

		// Land units that bombard but can't attack: catapults, cannons,
		// artillery and the like.
		internal static bool IsArtillery(MapUnit unit) {
			return unit.IsLandUnit() && unit.unitType.bombard > 0 && unit.unitType.attack == 0;
		}

		// A King (Regicide) goes to the capital and stays there: "AI civs
		// keep their leader units in the capital city"
		// (https://forums.civfanatics.com/threads/regicide.436212/). Without
		// a capital it stays where it is.
		private static UnitAI MakeKingAI(MapUnit unit, Player player) {
			// In Kings & Nomads the King may start with no capital and
			// never get one (captured cities have no palace), so it retires
			// to the safest of our cities instead (see KingsAndNomadsAI).
			if (KingsAndNomadsAI.PlaysFor(player)) {
				return KingsAndNomadsAI.MakeKingAI(unit, player);
			}
			City capital = player.cities.FirstOrDefault(c => c.capital);
			TilePath path = null;
			if (capital != null && capital.location != unit.location) {
				path = PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, capital.location, unit);
				if (path == null || path.PathLength() == 0) {
					capital = null;
				}
			}
			return new DefenderAI(new DefenderAIData {
				goal = DefenderAIData.DefenderGoal.DEFEND_CITY,
				destination = capital?.location ?? unit.location,
				defender = unit,
				pathToDestination = path,
			});
		}

		public static UnitAI GetAIForUnit(MapUnit unit, Player player) {
			//figure out an AI behavior
			if (unit.IsArmy() && ArmyAI.IsEnabled(player)) {
				return new ArmyAI();
			}
			// In Kings & Nomads a hurt King doesn't go off to heal like an
			// attacker: it keeps to its city (see KingsAndNomadsAI.MakeKingAI).
			if (unit.unitType.isKing && KingsAndNomadsAI.PlaysFor(player)) {
				return MakeKingAI(unit, player);
			}
			if (RestAI.ShouldRest(unit, player) && RestAI.Make(unit, player) is RestAI rest) {
				return rest;
			}
			if (unit.unitType.isKing) {
				return MakeKingAI(unit, player);
			}
			// Kings & Nomads: the King's city keeps its guard.
			if (KingsAndNomadsAI.GuardsTheKing(unit, player)) {
				return new DefenderAI(DefenderAI.MakeAiDataForDefendInPlace(unit, player));
			}
			if (unit.IsLeader()) {
				return MakeLeaderAI(unit, player);
			} else if (unit.unitType.isSettler) {
				return new SettlerAI(SettlerAI.MakeAiData(unit, player));
			} else if (unit.unitType.isWorker) {
				return new WorkerAI(WorkerAI.MakeAiData(unit, player));
			} else if (FerryAI.MaybeMakeAiData(unit, player) is FerryAIData ferryData) {
				return new FerryAI(ferryData);
			} else if (NavalAI.MaybeMake(unit, player) is NavalAI naval) {
				return naval;
			} else if (unit.location.cityAtTile != null && unit.CanDefendOnLand() && unit.location.unitsOnTile.Count(u => u.CanDefendOnLand() && u != unit) == 0) {
				return new DefenderAI(DefenderAI.MakeAiDataForDefendInPlace(unit, player));
			} else if (HuntBarbarians(unit, player) is UnitAI hunt) {
				return hunt;
			} else if (GetCombatAIIfUnitCanAttackNearbyBarbCamp(unit, player) is UnitAI unitAI && unitAI != null) {
				return unitAI;
			} else if (IsArtillery(unit)) {
				//For now tell artillery to sit tight.  It's getting really annoying watching them pointlessly bombard barb camps forever
				// Out in the field, it sits tight in one of our cities.
				if (unit.location.cityAtTile?.owner != player) {
					return new DefenderAI(DefenderAI.MakeAiDataForDefendAtRiskCity(unit, player, minDefenders: int.MaxValue));
				}
				return new DefenderAI(DefenderAI.MakeAiDataForDefendInPlace(unit, player));
			}

			// Special case: we're at war.
			if (IsInAnyWar(player, EngineStorage.gameData.players)) {
				if (AiExperiments.Has(player, SnipeWorkers) && SnipeWorkersPlan(unit, player) is UnitAI snipe) {
					return snipe;
				}
				// Priority 1: ensure we don't have any unguarded cities.
				//
				// If this is an offensive unit only go defend if there are
				// fewer than 3 units in a city, otherwise consider offensive
				// action.
				//
				// With minDefenders at int.MaxValue this never returns null
				// (it falls back to the best city), so the priority 3 fallback
				// below is only reached for offensive units, whose search here
				// used a different threshold and found nothing, returning
				// before any pathfinding. DefenderAI keeps the per-city inputs
				// of that search (defenders, units en route, distances) for the
				// unit, so the fallback reuses them and only re-scores.
				int minDefenders = unit.unitType.attack >= unit.unitType.defense ? 3 : int.MaxValue;
				DefenderAIData maybeDefend = DefenderAI.MakeAiDataForDefendAtRiskCity(unit, player, minDefenders);
				if (maybeDefend != null) {
					return new DefenderAI(maybeDefend);
				}

				// Priority 2: go on the offensive.
				CombatAIData maybeCombat = CombatAI.MakeAiData(unit, player);
				if (maybeCombat != null) {
					return new CombatAI(maybeCombat);
				}

				// Priority 3: defend our cities.
				return new DefenderAI(DefenderAI.MakeAiDataForDefendAtRiskCity(unit, player, minDefenders: int.MaxValue));
			}

			// If there's an unescorted settler, escort it.
			EscortAIData? maybeEscortData = EscortAI.MaybeMakeAiData(unit, player);
			if (maybeEscortData != null) {
				return new EscortAI(maybeEscortData);
			}

			// As long as we don't have too many explorers yet of this unit's
			// type (land vs sea), start a new exploring unit.
			int maxExplorers = unit.IsLandUnit() ? MAX_LAND_EXPLORERS : MAX_WATER_EXPLORERS;
			if (CountExplorers(player, unit.IsLandUnit()) < maxExplorers && !KeepForEscorts(unit, player)) {
				ExplorerAIData? maybeAiData = ExplorerAI.MaybeMakeAiData(unit, player);
				if (maybeAiData != null) {
					return new ExplorerAI(maybeAiData);
				}
			}

			//Nowhere to explore or too many explorers.  What to do now?
			//Priority 1: Adequate defense of cities.
			//Priority 2: Clearing out barbs
			//Priority 3: Defending chokepoints
			//Priority 4: ???
			//Priority 5: Profit!
			//(Realistically, as we evolve there will be a lot of options, such as defending borders from barbs, preparing attackers on other civs, defending
			//resources.  I expect we'll have some sort of arbiter that decides between competing priorities, with each being given a score as to how important
			//they are, including a weight by how far away the task is.  But this will evolve gradually over a long time)

			//As of today (4/7/2022), let's tackle just one of those - adequate defense of cities.  The AI is really good at losing cities to barbs right now,
			//and that's a problem.
			return new DefenderAI(DefenderAI.MakeAiDataForDefendAtRiskCity(unit, player, minDefenders: int.MaxValue));
		}

		private static UnitAI GetCombatAIIfUnitCanAttackNearbyBarbCamp(MapUnit unit, Player player) {
			if (unit.unitType.attack <= 0) {
				return null;
			}

			Tile closestBarbCamp = FindNearbyBarbCamp(unit, player, out TilePath path);
			if (closestBarbCamp != Tile.NONE) {
				CombatAIData caid = new CombatAIData();
				caid.destination = closestBarbCamp;
				caid.path = path;
				log.Information($"Set unit {unit} to take out barb camp at {closestBarbCamp}");
				return new CombatAI(caid);
			}
			return null;
		}

		private const int MAX_BARB_CAMP_DISTANCE = 3;

		// How far out of its way a unit will go to reach a nearby camp, as a
		// path cost (see AStarAlgorithm.PathFrom).
		private const double MAX_BARB_CAMP_PATH_COST = 2 * MAX_BARB_CAMP_DISTANCE;

		// Returns the closest known barbarian camp the unit can reach, if one
		// is within MAX_BARB_CAMP_DISTANCE tiles, or Tile.NONE. Equally close
		// camps are taken in the order GetTilesWithinTileSquare lists them.
		//
		// Camps the unit can't get to (e.g. across water) don't count, or the
		// unit would be sent towards the same unreachable camp every turn.
		// An attacker seeks out the barbarians within HuntRange tiles, the
		// units it can beat (at HuntOdds or better) and their camps, the
		// nearest it can reach first (owner's request, 2026-10-26: "AI should
		// attack barbarians, actively seeking them out"). Before, the AI
		// went only for camps within 3 tiles. In AI trials (paired, 24 games
		// of 6 civs and 200 turns, half the civs hunting) these civs killed
		// twice the barbarians (21 against 10 on Pangaea, 9 against 5 on
		// Continents), and had 6-7 more citizens, with 17 more beakers a turn
		// on Continents; they met the other civs on Pangaea about 5 turns
		// later, their attackers busy hunting rather than exploring.
		private static UnitAI HuntBarbarians(MapUnit unit, Player player) {
			if (unit.unitType.attack <= 0 || !unit.IsLandUnit() || IsArtillery(unit)) {
				return null;
			}
			Tile start = unit.location;
			List<Tile> targets = new();
			foreach (Tile t in start.GetTilesWithinTileSquare(HuntRange)) {
				if (t == Tile.NONE || t == start || !t.IsLand() || !player.tileKnowledge.isTileKnown(t)) {
					continue;
				}
				if (t.hasBarbarianCamp && unit.CanEnter(t)) {
					targets.Add(t);
					continue;
				}
				if (AIFogOfWar.SeesUnitsOn(player, t) && t.unitsOnTile.Count > 0 && t.unitsOnTile.All(u => u.owner.isBarbarians)
						&& t.FindTopDefender(unit) is MapUnit defender && MapUnit.IsMapUnitValid(defender)
						&& CombatAI.EstimateChanceToWin(unit, defender) >= HuntOdds) {
					targets.Add(t);
				}
			}
			if (targets.Count == 0) {
				return null;
			}
			PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(unit);
			foreach (Tile target in targets.OrderBy(t => t.DistanceTo(start))) {
				TilePath p = algorithm is AStarAlgorithm aStar
					? aStar.PathFrom(start, target, unit, 2 * HuntRange)
					: algorithm.PathFrom(start, target, unit);
				if (p != null && p.PathLength() > 0) {
					log.Information("Set unit {Unit} to hunt the barbarians at {Tile}", unit, target);
					return new CombatAI(new CombatAIData { destination = target, path = p });
				}
			}
			return null;
		}

		private const int HuntRange = 6;
		private const double HuntOdds = 0.6;

		// On trial (ZUR-84; CivFanatics "Capturing-Enemy-Settlers",
		// "Plucking-workers"): at war, a spare attacker goes for an enemy
		// worker or settler within SnipeRange tiles that has no defender on
		// its tile; walking onto it captures it (a settler gives two workers).
		// Not while the enemy's fighters could reach the tile next turn and
		// are likely to win (SnipeSurviveOdds).
		internal const string SnipeWorkers = "snipe-workers";
		private const int SnipeRange = 3;
		private const double SnipeSurviveOdds = 0.5;

		internal static UnitAI SnipeWorkersPlan(MapUnit unit, Player player) {
			if (!unit.IsLandUnit() || unit.IsArmy() || unit.IsLeader() || unit.unitType.isSettler || unit.unitType.isWorker
					|| !unit.CanAttack() || unit.unitType.attack <= 0 || unit.unitType.attack < unit.unitType.defense
					|| IsArtillery(unit) || unit.currentAI is WarCampaign.MemberAI) {
				return null;
			}
			Tile start = unit.location;
			// Never empty one of our own cities: another unit that can defend
			// must stay (not one that is itself going out to snipe).
			if (start.cityAtTile != null && start.cityAtTile.owner == player
					&& !start.unitsOnTile.Any(u => u != unit && u.CanDefendOnLand() && u.owner == player && !(u.currentAI is CombatAI))) {
				return null;
			}
			List<Tile> targets = new();
			foreach (Tile t in start.GetTilesWithinTileSquare(SnipeRange)) {
				if (t == Tile.NONE || t == start || !t.IsLand() || !OnlyPreyHere(t, player)
						|| !player.tileKnowledge.isTileKnown(t) || !AIFogOfWar.SeesUnitsOn(player, t)
						|| !unit.CanEnter(t) || t.HasCity() || SnipeTileIsDangerous(unit, player, t)) {
					continue;
				}
				targets.Add(t);
			}
			if (targets.Count == 0) {
				return null;
			}
			PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(unit);
			foreach (Tile target in targets.OrderBy(t => t.DistanceTo(start))) {
				TilePath p = algorithm is AStarAlgorithm aStar
					? aStar.PathFrom(start, target, unit, 2 * SnipeRange)
					: algorithm.PathFrom(start, target, unit);
				if (p != null && p.PathLength() > 0) {
					log.Information("Set unit {Unit} to snipe the workers at {Tile}", unit, target);
					return new CombatAI(new CombatAIData { destination = target, path = p });
				}
			}
			return null;
		}

		// Whether the tile holds only defenceless units of civs we are at war
		// with, at least one a worker or settler; see SnipeTileIsDangerous.
		private static bool OnlyPreyHere(Tile t, Player player) {
			bool prey = false;
			foreach (MapUnit u in t.unitsOnTile) {
				if (u.owner.IsIndependent || u.owner == player || !player.playerRelationships.TryGetValue(u.owner.id, out PlayerRelationship r)
						|| !r.AtWar() || !u.IsSeenBy(player)) {
					return false;
				}
				if (u.CanDefendOnLand() && u.CombatBaseStrength(CombatRole.Defense) > 0 && !u.unitType.isWorker && !u.unitType.isSettler) {
					return false;
				}
				prey |= u.unitType.isWorker || u.unitType.isSettler;
			}
			return prey;
		}

		// An enemy fighter that can reach the tile next turn and is likely to
		// beat the sniper there.
		private static bool SnipeTileIsDangerous(MapUnit unit, Player player, Tile target) {
			foreach (Tile near in target.GetTilesWithinTileSquare(3)) {
				if (near == Tile.NONE) {
					continue;
				}
				foreach (MapUnit foe in near.unitsOnTile) {
					if (foe.owner == player || !foe.IsLandUnit() || !foe.CanAttack()
							|| foe.unitType.attack <= 0 || player.IsAtPeaceWith(foe.owner)
							|| near.DistanceTo(target) > foe.unitType.movement + 1) {
						continue;
					}
					if (CombatAI.EstimateChanceToWin(foe, unit) > 1 - SnipeSurviveOdds) {
						return true;
					}
				}
			}
			return false;
		}

		private static Tile FindNearbyBarbCamp(MapUnit unit, Player player, out TilePath path) {
			path = null;
			Tile start = unit.location;

			// The tiles within a DistanceTo of n are exactly the tile square of
			// rank n, so only those need checking.
			List<Tile> camps = null;
			foreach (Tile t in start.GetTilesWithinTileSquare(MAX_BARB_CAMP_DISTANCE)) {
				if (t == Tile.NONE || !t.hasBarbarianCamp || !player.tileKnowledge.isTileKnown(t) || !unit.CanEnter(t)) {
					continue;
				}
				camps ??= new List<Tile>();
				camps.Add(t);
			}
			if (camps == null) {
				return Tile.NONE;
			}

			// Nearest first; OrderBy is stable, so ties keep their order.
			if (camps.Count > 1) {
				camps = camps.OrderBy(t => t.DistanceTo(start)).ToList();
			}

			PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(unit);
			foreach (Tile camp in camps) {
				TilePath p = algorithm is AStarAlgorithm aStar
					? aStar.PathFrom(start, camp, unit, MAX_BARB_CAMP_PATH_COST)
					: algorithm.PathFrom(start, camp, unit);
				if (camp == start || (p != null && p.PathLength() > 0)) {
					path = p;
					return camp;
				}
			}
			return Tile.NONE;
		}

		// Whether GetAIForUnit would make this unit an explorer, without
		// planning anything or changing any state. The unit doesn't need to be
		// in play, so this can be asked about a unit we're considering
		// building.
		//
		// `hypothetical` says the unit isn't in play (it was made just to ask
		// this), which lets the answer be shared with other such questions
		// this turn (see HasTileToExploreCached).
		internal static bool WouldExplore(MapUnit unit, Player player, bool hypothetical = false) {
			// Mirror the checks GetAIForUnit makes before considering
			// exploration.
			if (unit.IsLeader() || unit.unitType.isSettler || unit.unitType.isWorker) {
				return false;
			}
			// A transport would ferry a settler waiting for one.
			if (FerryAI.SettlerWaitingForTransport(unit, player) != null) {
				return false;
			}
			if (unit.location.cityAtTile != null && unit.CanDefendOnLand() && unit.location.unitsOnTile.Count(u => u.CanDefendOnLand() && u != unit) == 0) {
				return false;
			}
			if (unit.unitType.attack > 0 && FindNearbyBarbCamp(unit, player, out _) != Tile.NONE) {
				return false;
			}
			if (IsArtillery(unit)) {
				return false;
			}
			if (IsInAnyWar(player, EngineStorage.gameData.players)) {
				return false;
			}
			// EscortAI.MaybeMakeAiData would have this unit escort an
			// unescorted settler.
			if (unit.CanDefendOnLand() && unit.location.unitsOnTile.Any(u => u.currentAI is SettlerAI settlerAi && settlerAi.data.escort == null)) {
				return false;
			}
			int maxExplorers = unit.IsLandUnit() ? MAX_LAND_EXPLORERS : MAX_WATER_EXPLORERS;
			if (CountExplorers(player, unit.IsLandUnit()) >= maxExplorers || KeepForEscorts(unit, player)) {
				return false;
			}
			return hypothetical ? HasTileToExploreCached(unit, player) : HasTileToExplore(unit, player);
		}

		// Whether ExplorerAI.MaybeMakeAiData would find a tile for this unit
		// to explore: it takes the best scored candidate it can reach, so this
		// is whether any candidate it would score is reachable. Which one comes
		// first doesn't matter, so this skips the scoring and asks the pathing
		// algorithm once about all of them, nearest first.
		private static bool HasTileToExplore(MapUnit unit, Player player) {
			return HasTileToExplore(unit, player, ExcludedExplorationTargets(player, unit));
		}

		// Exploration targets of explorers that are gone or have other jobs
		// are forgotten before candidates are scored, so the candidates left
		// out are the targets of the other units that are still exploring.
		private static HashSet<Tile> ExcludedExplorationTargets(Player player, MapUnit unit) {
			TileKnowledge knowledge = player.tileKnowledge;
			if (knowledge.aiExplorationTargets.Count == 0) {
				return null;
			}
			HashSet<Tile> excluded = ExplorerAI.ActiveExplorationTargets(player, unit);
			excluded.IntersectWith(knowledge.aiExplorationTargets);
			return excluded;
		}

		private static bool HasTileToExplore(MapUnit unit, Player player, HashSet<Tile> excludedTargets) {
			TileKnowledge knowledge = player.tileKnowledge;

			bool isLandUnit = unit.IsLandUnit();
			List<Tile> candidates = new();
			foreach (Tile t in knowledge.borderTiles) {
				if (t.IsLand() != isLandUnit) {
					continue;
				}
				if (excludedTargets != null && excludedTargets.Contains(t)) {
					continue;
				}
				if (HasUnknownNeighboringTiles(knowledge, t)) {
					candidates.Add(t);
				}
			}
			if (candidates.Count == 0) {
				return false;
			}

			// Searching towards a near candidate first keeps the common case
			// (something nearby is reachable) quick.
			Tile start = unit.location;
			candidates.Sort((a, b) => start.DistanceTo(a).CompareTo(start.DistanceTo(b)));
			PathingAlgorithm algorithm = PathingAlgorithmChooser.GetAlgorithm(unit);
			return algorithm.FindFirstReachableIndex(start, candidates, unit) >= 0;
		}

		// Matches ExplorerAI's test for whether a tile is worth exploring (at
		// least one unknown tile among itself and its neighbors, and no
		// city). Border tiles are the unknown tiles next to known ones, so for
		// them this comes down to having no city; the neighbors only need
		// checking if a tile became known without going through TileKnowledge
		// (which tests do).
		private static bool HasUnknownNeighboringTiles(TileKnowledge knowledge, Tile t) {
			if (t.cityAtTile != null) {
				return false;
			}
			if (!knowledge.isTileKnown(t)) {
				return true;
			}
			foreach (Tile n in t.neighbors.Values) {
				if (n != Tile.NONE && !knowledge.isTileKnown(n)) {
					return true;
				}
			}
			return false;
		}

		// HasTileToExplore for units that aren't in play, such as the units a
		// city is considering building. A city asks this for every kind of
		// unit it could build, every time it picks something to build, and the
		// answer only depends on the unit through where it starts and which
		// tiles it can enter, so answers are shared until the turn or what the
		// player knows changes.
		//
		// What's known about the map, the player's cities (which ships can
		// sail through) and the exploration targets are checked for changes.
		// Other units moving during the turn aren't, which can only change
		// the answer if they block the way to everything left to explore.
		private sealed class ExplorationCheckCache {
			public int turn = int.MinValue;
			public int knownTileCount = -1;
			public int borderTileCount = -1;
			public int cityCount = -1;
			public HashSet<Tile> excludedTargets;
			public readonly Dictionary<ExplorationCheckKey, bool> results = new();
		}

		// Everything about a unit that decides which tiles it can enter, for
		// a unit that isn't in play (so isn't loaded, in an army or carrying
		// anything).
		private readonly record struct ExplorationCheckKey(Tile start, bool land, bool water, bool air, bool combat, bool loadable, bool amphibious);

		private static readonly ConditionalWeakTable<Player, ExplorationCheckCache> explorationChecks = new();

		private static bool HasTileToExploreCached(MapUnit unit, Player player) {
			TileKnowledge knowledge = player.tileKnowledge;
			HashSet<Tile> excluded = ExcludedExplorationTargets(player, unit);
			int turn = EngineStorage.gameData?.turn ?? 0;

			ExplorationCheckCache cache = explorationChecks.GetValue(player, _ => new ExplorationCheckCache());
			bool sameExcluded = (cache.excludedTargets == null || cache.excludedTargets.Count == 0)
				? (excluded == null || excluded.Count == 0)
				: excluded != null && cache.excludedTargets.SetEquals(excluded);
			if (cache.turn != turn || cache.knownTileCount != knowledge.knownTiles.Count
				|| cache.borderTileCount != knowledge.borderTiles.Count || cache.cityCount != player.cities.Count || !sameExcluded) {
				cache.results.Clear();
				cache.turn = turn;
				cache.knownTileCount = knowledge.knownTiles.Count;
				cache.borderTileCount = knowledge.borderTiles.Count;
				cache.cityCount = player.cities.Count;
				cache.excludedTargets = excluded;
			}

			ExplorationCheckKey key = new(unit.location, unit.IsLandUnit(), unit.IsWaterUnit(), unit.IsAirUnit(),
				unit.IsCombatUnit(), unit.IsLoadable(), unit.unitType.isAmphibious);
			if (!cache.results.TryGetValue(key, out bool result)) {
				result = HasTileToExplore(unit, player, excluded);
				cache.results[key] = result;
			}
			return result;
		}

		private static async Task AttemptTrading(Player us, bool tradeNow = false, bool swapNow = false) {
			GameData gD = EngineStorage.gameData;

			log.Information($"{us} is checking for trading opportunities");
			foreach (Player them in EngineStorage.gameData.players) {
				// We can't trade with players we don't know or players we're at
				// war with.
				if (!us.playerRelationships.ContainsKey(them.id) || AtWar(us, them)) {
					continue;
				}

				// Barbarians can't trade.
				if (them.IsIndependent || us.IsIndependent) {
					continue;
				}

				// Nor do we trade with a civ we're about to attack: what we
				// sold would arm it, and the war would break the deal and our
				// name with everyone.
				if (WarCampaign.IsPlannedTarget(us, them)) {
					continue;
				}

				// Trial switch tech-swap: a tech for a tech, at once when there's
				// news (see TechSwap).
				if (TechSwap.On(us, them) && (swapNow || RollsToNegotiate(them))) {
					TechSwap.Swap(gD, us, them);
				}

				// Trial switch trade-gpt: techs for gold per turn with every AI
				// we can, at once when there's news (see TradeGpt).
				if (TradeGpt.On(us, them) && (tradeNow || RollsToNegotiate(them))) {
					TradeGpt.TradeTechs(gD, us, them);
					if (tradeNow) {
						continue;
					}
				} else if (tradeNow) {
					continue;
				}

				if (!RollsToNegotiate(them)) {
					continue;
				}

				// A human is offered one deal by us every
				// HumanOfferCooldownTurns turns at most, rather than the same
				// offer again every few turns.
				if (them.isHuman && us.playerRelationships[them.id].lastDealOfferTurn >= 0
						&& gD.turn - us.playerRelationships[them.id].lastDealOfferTurn < HumanOfferCooldownTurns) {
					continue;
				}

				// At war, now and then, a military alliance against the enemy;
				// otherwise a tech trade if there is one to make, otherwise a
				// resource deal, otherwise a pact or embargo (see
				// DealProposalAI).
				(TradeOffer weGive, TradeOffer weWant)? deal = DealProposalAI.AllianceDeal(gD, us, them)
					?? (TradeGpt.On(us, them) ? null : TechDeal(gD, us, them))
					?? DealProposalAI.ResourceDeal(gD, us, them)
					?? (TriesPacts(us) ? DealProposalAI.PactDeal(gD, us, them) : null)
					?? DealProposalAI.EmbargoDeal(gD, us, them);
				if (deal is not (TradeOffer weGive, TradeOffer weWant)) {
					continue;
				}

				us.playerRelationships[them.id].lastDealOfferTurn = gD.turn;
				await Propose(gD, us, them, weGive, weWant);
			}
		}

		// Whether we talk trade with them this turn: with a human always, and
		// with another AI only on a one in four chance, as in Civ3: "The
		// intercepted call is the base game's 1-in-4 gate before
		// ai_negotiate_with_other_ai" (C3X's injected_code.c,
		// https://github.com/maxpetul/C3X; compendium 08, section 1.2).
		// Paired AI trials (8 games of 200 turns, score per civ against the
		// same civs without it): +25 +- 110 on Continents, -13 +- 169 on
		// Pangaea, so no measurable change, and it is Civ3's rule.
		// Percent chance a turn that an AI looks for trades: 25, and for an
		// Advanced AI that much leaned by the average of its diplomacy and
		// research weights (50 = as is).
		private static int TradeChance(Player us) {
			if (!us.IsAdvancedAi) {
				return 25;
			}
			double w = (C7Engine.AI.Advanced.RuleWeights.Effective(us, C7Engine.AI.Advanced.RuleCategory.Diplomacy) + C7Engine.AI.Advanced.RuleWeights.Effective(us, C7Engine.AI.Advanced.RuleCategory.Research)) / 2;
			if (w == C7Engine.AI.Advanced.RuleWeights.Neutral) {
				return 25;
			}
			C7Engine.AI.Advanced.RuleWeights.Used(us, C7Engine.AI.Advanced.RuleCategory.Diplomacy, "trade");
			return (int)Math.Round(25 * w / C7Engine.AI.Advanced.RuleWeights.Neutral);
		}

		// Whether an Advanced AI tries a pact this time: always at a
		// diplomacy weight of 50 or more (no random draw then), otherwise
		// with a chance of twice the weight.
		private static bool TriesPacts(Player us) {
			if (!us.IsAdvancedAi || C7Engine.AI.Advanced.RuleWeights.Effective(us, C7Engine.AI.Advanced.RuleCategory.Diplomacy) >= C7Engine.AI.Advanced.RuleWeights.Neutral) {
				return true;
			}
			C7Engine.AI.Advanced.RuleWeights.Used(us, C7Engine.AI.Advanced.RuleCategory.Diplomacy, "trade");
			return GameData.rng.Next(100) < 2 * C7Engine.AI.Advanced.RuleWeights.Effective(us, C7Engine.AI.Advanced.RuleCategory.Diplomacy);
		}

		public static bool RollsToNegotiate(Player them) {
			return them.isHuman || GameData.rng.Next(4) == 0;
		}

		// HEURISTIC: how often an AI may offer a human a deal of its own.
		private const int HumanOfferCooldownTurns = 10;

		// Offers them a deal in which we give weGive for weWant: a human is
		// shown it to accept or change, another AI takes it if it would.
		private static async Task Propose(GameData gD, Player us, Player them, TradeOffer weGive, TradeOffer weWant) {
			if (them.isHuman) {
				// A LAN guest who has left can't answer, so isn't asked.
				if (!EngineStorage.IsPlayerReachable(them.id)) {
					return;
				}
				// Only a deal we would take ourselves, as accepting it puts
				// it to us again (see MsgProposeDeal): we price a human's
				// deals with more care (see WouldAcceptDealFrom) than the
				// offers we make.
				if (!us.WouldAcceptDealFrom(gD, them, weWant, weGive)) {
					log.Debug("{Us} doesn't offer {Them} a deal it would turn down itself", us, them);
					return;
				}
				// The human receiving the offer takes the UI to respond.
				// In a hotseat game they may not be the player at the screen.
				if (EngineStorage.uiFollowsActivePlayer) {
					EngineStorage.uiControllerID = them.id;
				}
				EngineStorage.diplomacyPlayerID = them.id;
				EngineStorage.diplomacyAIPlayerID = us.id;
				new MsgShowTradeOffer(us, them, weWant, weGive, DealProposalAI.Greeting(gD, weGive, weWant)).send();
				await EngineStorage.WaitForDiplomacyCompleted(them.id);
				EngineStorage.diplomacyPlayerID = null;
				EngineStorage.diplomacyAIPlayerID = null;
			} else if (them.WouldAcceptDealFrom(gD, us, weGive, weWant)) {
				us.ExecuteDeal(gD, them, weWant, weGive);
			}
		}

		// The deals with us that ran out (see DealRenewal): we offer each
		// again, on the same terms, if we would still accept it; otherwise a
		// human is told we don't wish to extend it.
		internal static async Task RenewExpiredDeals(Player us, GameData gD) {
			foreach ((TradeOffer weGave, Player them, TradeOffer theyGave) in DealRenewal.TakeFor(gD, us)) {
				if (them.defeated || (weGave.IsEmpty() && theyGave.IsEmpty())) {
					continue;
				}
				bool wanted = TradeOffer.ProblemWithDeal(gD, us, them, weGave, theyGave) == null
					&& us.WouldAcceptDealFrom(gD, them, theyGave, weGave);
				if (!wanted) {
					if (them.isHuman) {
						DealRenewal.Tell(them, $"The {us.civilization?.noun} do not wish to extend their deal with us.");
					}
					continue;
				}
				log.Information("{Us} offers to renew a deal with {Them}", us, them);
				await Propose(gD, us, them, weGave, theyGave);
			}
		}

		// The trade of techs (and gold to even it out) we would offer them,
		// or null if there is none worth making.
		private static (TradeOffer weGive, TradeOffer weWant)? TechDeal(GameData gD, Player us, Player them) {
			// Figure out what techs are available for trading.
			List<Tech> techsTheyCanTrade = them.GetTechsTradableTo(us, gD.techs);
			List<Tech> techsWeCanTrade = us.GetTechsTradableTo(them, gD.techs);

			// If we can't trade techs there's no point in continuing - we
			// can't yet trade anything else interesting.
			if (techsWeCanTrade.Count == 0 && techsTheyCanTrade.Count == 0) {
				return null;
			}

			// Tech costs don't change until a deal is made, so look each
			// one up once. Every tech traded here is in one of the two
			// lists, and we need its cost for both players.
			Dictionary<Tech, int> costForUs = new();
			Dictionary<Tech, int> costForThem = new();
			foreach (Tech t in techsTheyCanTrade.Concat(techsWeCanTrade)) {
				costForUs[t] = gD.TechCostFor(t, us);
				costForThem[t] = gD.TechCostFor(t, them);
			}

			techsTheyCanTrade.Sort((a, b) => { return costForUs[b].CompareTo(costForUs[a]); });
			techsWeCanTrade.Sort((a, b) => { return costForThem[b].CompareTo(costForThem[a]); });

			// The value of each offer to each player, kept up to date as
			// techs are added or removed. This matches
			// TradeOffer.GoldEquivalentFor, without the gold.
			int weGiveTechsForThem = 0;
			int weGiveTechsForUs = 0;
			int weWantTechsForThem = 0;
			int weWantTechsForUs = 0;

			// With a human, techs are priced at the human's research cost
			// alone, as in TradeOffer.GoldEquivalentFor. Between AIs, each
			// tech at its buyer's cost (see AiTechPricing).
			bool buyerPrice = AiTechPricing.PricesAtBuyerCost(us, them);
			TradeOffer weGive = new();
			Func<int> CalculateWeGiveValue = () => {
				int gold = weGive.gold ?? 0;
				return them.isHuman || buyerPrice ? gold + weGiveTechsForThem : Math.Max(gold + weGiveTechsForThem, gold + weGiveTechsForUs);
			};
			TradeOffer weWant = new();
			Func<int> CalculateWeWantValue = () => {
				int gold = weWant.gold ?? 0;
				return them.isHuman ? gold + weWantTechsForThem
					: buyerPrice ? gold + weWantTechsForUs
					: Math.Min(gold + weWantTechsForThem, gold + weWantTechsForUs);
			};

			// Figure out the value of what we have available to trade.
			weGive.gold = us.gold;
			foreach (Tech t in techsWeCanTrade) {
				weGive.techs.Add(t);
				weGiveTechsForThem += costForThem[t];
				weGiveTechsForUs += costForUs[t];
			}
			int ourMaxPossibleOffer = CalculateWeGiveValue();

			// Going from the most to the least valuable valuable techs, see
			// if we can afford them. This greedy algorithm should be good
			// enough - we don't need perfect binpacking.
			foreach (Tech t in techsTheyCanTrade) {
				int cost = costForUs[t];
				if (cost < ourMaxPossibleOffer) {
					weWant.techs.Add(t);
					weWantTechsForThem += costForThem[t];
					weWantTechsForUs += costForUs[t];
					ourMaxPossibleOffer -= cost;
				}
			}

			// Between AIs (AiTechPricing): if we can't pay the full value of any
			// of their techs, we offer all we have for the best one worth no
			// more than that at the least share they would take.
			if (buyerPrice && weWant.techs.Count == 0) {
				int share = AiTechPricing.LeastSharePercent(gD);
				Tech best = techsTheyCanTrade.FirstOrDefault(t => (long)costForUs[t] * share <= (long)ourMaxPossibleOffer * 100);
				if (best != null) {
					weWant.techs.Add(best);
					if (weGive.gold == 0) {
						weGive.gold = null;
					}
					return (weGive, weWant);
				}
			}

			// Also ask for any gold we can get.
			weWant.gold = Math.Min(ourMaxPossibleOffer, them.gold);

			// At this point we are getting as much as we possibly can get
			// from the opponent. However, we might be overpaying, possibly
			// by a significant amount. Keep removing techs from our offer
			// as long as it doesn't make our offer worse than theirs.
			int theirOfferValue = CalculateWeWantValue();
			for (int i = 0; i < weGive.techs.Count;) {
				Tech t = weGive.techs[i];
				if (CalculateWeGiveValue() - costForThem[t] >= theirOfferValue) {
					weGive.techs.RemoveAt(i);
					weGiveTechsForThem -= costForThem[t];
					weGiveTechsForUs -= costForUs[t];
				} else {
					++i;
				}
			}

			// Now use any gold to even things out, if possible.
			int remainingDelta = Math.Max(0, CalculateWeGiveValue() - theirOfferValue);
			weGive.gold -= Math.Min(remainingDelta, weGive.gold.Value);

			// A human pays our markup (see TradeMarkup): we ask what we give
			// is worth x (1 + markup), and count what they give at / (1 +
			// markup), so we ask for more of their gold, if they have it.
			int markup = TradeMarkup.Percent(gD, us, them);
			if (markup > 0) {
				int shortfall = TradeMarkup.Asking(TradeMarkup.Asking(CalculateWeGiveValue(), markup), markup) - CalculateWeWantValue();
				if (shortfall > 0) {
					weWant.gold = Math.Max(weWant.gold ?? 0, Math.Min((weWant.gold ?? 0) + shortfall, them.gold));
				}
			}

			// And ensure we minimize the total gold traded, to keep the
			// logs cleaner.
			int redundantGold = Math.Min(weGive.gold.Value, weWant.gold.Value);
			weGive.gold -= redundantGold;
			weWant.gold -= redundantGold;
			if (weGive.gold == 0) {
				weGive.gold = null;
			}
			if (weWant.gold == 0) {
				weWant.gold = null;
			}

			// Finally if the deal is too mismatched or only contains a swap
			// of gold, abandon it. Otherwise we can execute the deal.

			if (them.isHuman) {
				if (TradeMarkup.Asking(CalculateWeGiveValue(), markup) > TradeMarkup.Paying(CalculateWeWantValue(), markup)) {
					return null;
				}
			} else if (CalculateWeGiveValue() > gD.gameDifficulty.AIToAITradeRate / 100.0f * CalculateWeWantValue()) {
				return null;
			}
			if (weGive.techs.Count == 0 && weWant.techs.Count == 0) {
				return null;
			}
			return (weGive, weWant);
		}

		internal const string NoAiLuxury = "no-ai-luxury";

		// Trial switch "lux-slider-high": the AI keeps raising the luxury
		// slider (within its cap and the government's) until no city is
		// unhappy, rather than stopping once 10% or fewer are, before
		// FixRemainingUnhappyCities falls back to entertainers
		// (SuedeCivIII's tips, ZUR-31).
		internal const string LuxSliderHigh = "lux-slider-high";

		// Whether, of this many cities, this many unhappy ones are too many
		// to leave to entertainers.
		internal static bool TooManyUnhappy(Player player, int unhappyCities, int cities) {
			double allowed = AiExperiments.Has(player, LuxSliderHigh) ? 0 : .1;
			return unhappyCities / (double)cities > allowed;
		}

		// Whether the player knows every advance there is to research (not
		// those of no era, which can't be researched; see EraUtils.HIDDEN_CVLPD).
		internal static bool NothingToResearch(Player player) {
			List<Tech> techs = EngineStorage.gameData?.techs;
			return techs != null && techs.Count > 0
				&& techs.All(t => player.knownTechs.Contains(t.id) || t.EraCivilopediaName == EraUtils.HIDDEN_CVLPD);
		}

		// Without reassignCitizens, the citizens stay where they are (for the
		// play recorder's shadow rates, which mustn't change the game; see
		// PlayLog.ShadowDecisions).
		internal static void AdjustSliders(Player player, bool reassignCitizens = true) {
			const int MAX_SLIDER_VALUE = 10;
			const int MAX_AI_LUXURY_SLIDER = 5;

			// Start by zeroing out the sliders.
			player.luxuryRate = 0;
			player.scienceRate = 0;
			player.taxRate = 0;

			// Increase the luxury slider until only a small handful of cities
			// are unhappy (sometimes making all cities happy with the luxury
			// slider is too expensive and it's easier to just use entertainers
			// there).
			//
			// Trial switch "no-ai-luxury": Civ3's AI "will NOT use the luxury
			// slider to compensate for any happiness issues" and uses
			// entertainers instead (Ision, https://forums.civfanatics.com/threads/92917/,
			// compendium 08 section 3.6), which FixRemainingUnhappyCities does.
			// In paired trials (AiTrialTest, 8 games of 200 turns) it scored
			// -39 +- 62 on Continents and -36 +- 87 on Pangaea, with more
			// cities in disorder (+3.2 and +1.7 city-turns per civ) and fewer
			// citizens: weaker on both maps, so the AI keeps its luxury
			// slider.
			while (!AiExperiments.Has(player, NoAiLuxury) && MostCitiesUnhappy(player) && player.luxuryRate < Math.Min(MAX_AI_LUXURY_SLIDER, player.maxLuxuryRate)) {
				++player.luxuryRate;
			}

			// Fix up any remaining unhappy cities.
			if (reassignCitizens) {
				FixRemainingUnhappyCities(player);
			}

			// With every advance known (as in MDJ, see GameSetup.Mdj.cs)
			// research buys nothing, so what science would take goes to
			// taxes, and past the government's cap to luxuries.
			if (NothingToResearch(player)) {
				player.taxRate = Math.Min(player.maxRate, MAX_SLIDER_VALUE - player.luxuryRate);
				player.luxuryRate = MAX_SLIDER_VALUE - player.taxRate;
				player.ApplyGovernmentRateCap();
				// Where gold can't hurry production either (forced labor, as
				// under MDJ's Feudalism), only as much tax as keeps the
				// treasury from shrinking, and the rest to luxuries: happy
				// citizens score twice what content ones do. In AI trials
				// (Rising Tide under MDJ, 32 paired games of 8 civs, twice)
				// civs ended with 1,000 gold rather than 10,000, scored 65
				// and 73 more (+- 25 and 37) and won a little more often.
				if (player.government.hurryingType != Government.HurryProductionType.PaidLabor) {
					int wanted = DevelopmentAI.WantedGoldPerTurn(player);
					int fixedGoldPerTurn = player.CalculateGoldPerTurn() - CityTaxesAndWealth(player);
					for (int tax = 0; tax <= player.maxRate; ++tax) {
						player.taxRate = tax;
						player.luxuryRate = MAX_SLIDER_VALUE - tax;
						player.ApplyGovernmentRateCap();
						if (fixedGoldPerTurn + CityTaxesAndWealth(player) >= Math.Max(0, wanted)) {
							break;
						}
					}
				}
				log.Information($"{player} has nothing to research; slider values: Science: {player.scienceRate}, Luxury: {player.luxuryRate}, Tax: {player.taxRate}");
				return;
			}

			// Now max out the science slider and then decrease it (increasing
			// the tax rate) until we're not losing money.
			player.scienceRate = Math.Min(player.maxScienceRate, MAX_SLIDER_VALUE - player.luxuryRate);
			player.taxRate = MAX_SLIDER_VALUE - player.luxuryRate - player.scienceRate;
			player.ApplyGovernmentRateCap();
			int goldPerTurn;
			// At war it also saves up gold to hurry units with.
			int wantedGoldPerTurn = DevelopmentAI.WantedGoldPerTurn(player);
			if (player.scienceRate > 0 && player.taxRate < player.maxRate
				&& !IsTolerable(player, goldPerTurn = player.CalculateGoldPerTurn(), wantedGoldPerTurn)) {
				// Only the cities' commerce changes as the sliders move; see
				// CityTaxesAndWealth.
				int fixedGoldPerTurn = goldPerTurn - CityTaxesAndWealth(player);
				do {
					player.scienceRate--;
					player.taxRate++;
				} while (player.scienceRate > 0 && player.taxRate < player.maxRate
					&& !IsTolerable(player, fixedGoldPerTurn + CityTaxesAndWealth(player), wantedGoldPerTurn));
				if (player.government.hasTilePenalty) {
					SpendDespotismSurplus(player, fixedGoldPerTurn, wantedGoldPerTurn);
				}
			}

			log.Information($"{player} slider values: Science: {player.scienceRate}, Luxury: {player.luxuryRate}, Tax: {player.taxRate}");
		}

		// Under Despotism (any government with the tile penalty) the AI
		// should almost never run a gold surplus: what isn't needed goes to
		// research (the project owner's decision, 2026-10-20). Taking
		// science down a step at a time to stop a deficit can overshoot into
		// a surplus when the treasury is too small for the tenth-of-the-
		// treasury deficit AdjustSliders otherwise allows. So when the step
		// before, with more science, loses less gold than this one gains,
		// and the treasury can pay that loss this turn, the AI takes it back:
		// over the turns it runs a little either side of nothing, and the
		// treasury never goes below zero. The gold the AI wants at war
		// (wantedGoldPerTurn) is still kept. Before this, AIs in Despotism
		// had a surplus on about 3% of their turns (4 games of 200 turns on
		// Continents), nearly all of them such overshoots. In paired trials
		// (AiTrialTest, 8 games of 200 turns, as the switch
		// "despotism-no-surplus") it scored +167 +- 57 on Continents, with
		// more cities, citizens and techs, and +3 +- 79 on Pangaea.
		private static void SpendDespotismSurplus(Player player, int fixedGoldPerTurn, int wantedGoldPerTurn) {
			int surplus = fixedGoldPerTurn + CityTaxesAndWealth(player);
			if (surplus <= 0 || surplus <= wantedGoldPerTurn || player.taxRate == 0 || player.scienceRate >= player.maxScienceRate) {
				return;
			}
			player.scienceRate++;
			player.taxRate--;
			int deficit = -(fixedGoldPerTurn + CityTaxesAndWealth(player));
			if (deficit >= surplus || player.gold < deficit || -deficit < wantedGoldPerTurn) {
				player.scienceRate--;
				player.taxRate++;
			}
		}

		// Returns true if more than 10% of the player's cities (any, with the
		// switch lux-slider-high) are unhappy with current settings.
		private static bool MostCitiesUnhappy(Player player) {
			int unhappyCities = 0;
			foreach (City city in player.cities) {
				City.Mood cityMood = city.RecalculateCitizenMoods(EngineStorage.gameData);
				if (cityMood == City.Mood.Unhappy) {
					++unhappyCities;
				}
			}
			return TooManyUnhappy(player, unhappyCities, player.cities.Count);
		}

		// In each city, reassign citizens, managing moods, to ensure that we
		// don't have any cities that will riot.
		private static void FixRemainingUnhappyCities(Player player) {
			GameData gameData = EngineStorage.gameData;
			foreach (City city in player.cities) {
				CityInteractions.ReassignAllCitizens(gameData, city);
			}
		}

		public static bool BudgetIsTolerable(Player player) {
			return IsTolerable(player, player.CalculateGoldPerTurn());
		}

		private static bool IsTolerable(Player player, int gpt, int wanted = 0) {
			if (gpt < wanted) {
				return false;
			}
			var tolerableDeficit = gpt > player.gold * -0.1;
			return gpt > 0 || tolerableDeficit;
		}

		// The part of the player's gold per turn that the science and tax
		// sliders affect.
		//
		// Gold per turn (Player.AggregateFlows().Netflows()) is the sum of
		// each city's taxes and wealth from City.CurrentCommerceYield(), plus
		// terms the sliders don't affect: gold-per-turn deals, interest
		// (which depends on the treasury), building maintenance and unit
		// support. The corrupted, beaker and happiness amounts cancel out,
		// and the tax collector split only moves taxes between two terms of
		// the same sum. So once the rest is known, only the cities' commerce
		// needs recomputing as the sliders move.
		internal static int CityTaxesAndWealth(Player player) {
			int result = 0;
			foreach (City city in player.cities) {
				CommerceBreakdown commerce = city.CurrentCommerceYield();
				result += commerce.taxes + commerce.wealth;
			}
			return result;
		}
	}
}

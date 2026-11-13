using System.Collections.Generic;
using System.Linq;
using C7Engine.AI;
using C7Engine.Pathing;
using C7GameData;
using C7GameData.AIData;

namespace C7Engine {
	// The jobs of Civ3's kinds of worker automation (see WorkerAutomation),
	// which a human gives a worker. Each plan is the next job of its kind,
	// or null once there is none, which hands the worker back to the
	// player: with no pollution left, "any Workers on mop-up duty just go to
	// sleep" (tjs282, CFC C3X thread p156 #3108).
	//
	// Adapted from GBiebs/OpenCiv3.
	public partial class WorkerAI {
		private static WorkerAIData WorkerAutomationPlan(MapUnit unit, Player player, WorkerAutomation how) {
			switch (how.kind) {
				case WorkerAutomation.Kind.ThisCityOnly or WorkerAutomation.Kind.ThisCityNoAltering:
					return ThisCityPlan(unit, player, how);
				case WorkerAutomation.Kind.ClearForest or WorkerAutomation.Kind.ClearWetlands or WorkerAutomation.Kind.ClearDamage:
					return ClearingPlan(unit, player, how);
				case WorkerAutomation.Kind.TradeNetwork:
					return TradeNetworkPlan(unit, player);
				case WorkerAutomation.Kind.RoadTo or WorkerAutomation.Kind.RailTo or WorkerAutomation.Kind.IrrigateToCity:
					return RoutePlan(unit, player, how);
				default:
					return null;
			}
		}

		// What a worker of the kind does on a tile it passes on its way.
		private static Terraform WorkerAutomationJobOnTheWay(MapUnit unit, WorkerAutomation how) {
			Tile here = unit.location;
			if (how.CityOnly) {
				City city = how.target?.cityAtTile;
				return city != null && here.owningCity == city ? GetTileImprovement(here, unit) : null;
			}
			if (how.ClearingJob is string job && C7Action.ToTerraform(job) is Terraform clearing
					&& here.OwningPlayer() == unit.owner && unit.CanPerformTerraformAction(clearing, here)) {
				return clearing;
			}
			// The others go straight to the next tile of their road or
			// irrigation, which is the first that needs it.
			return null;
		}

		// Whether building the terraform on the tile would change an
		// improvement there (No Altering): replacing it (a mine with
		// irrigation), or changing the terrain under it (planting a forest
		// on irrigated land). Clearing damage changes neither.
		private static bool AltersImprovement(Terraform terraform, Tile t) {
			if (t.overlays.GetReplacementTarget(terraform) != null) {
				return true;
			}
			return terraform.Improvement == null && terraform.UIAction != C7Action.UnitClearDamage
				&& t.overlays.ImprovementAtLayer(TerrainImprovement.Layer.ResourceDevelopment) != null;
		}

		// The tiles other workers of ours are heading to, which this one
		// leaves to them.
		private static HashSet<Tile> OtherWorkersDestinations(MapUnit unit, Player player) {
			HashSet<Tile> result = new();
			registry.ForEachActive(player, other => {
				if (other.data.worker != unit && other.data.destination != null) {
					result.Add(other.data.destination);
				}
			});
			return result;
		}

		// The tiles left out of a kind's jobs: other workers' and those
		// enemies could catch the worker on (see AIThreat).
		private static System.Func<Tile, bool> Excluded(MapUnit unit, Player player) {
			HashSet<Tile> taken = OtherWorkersDestinations(unit, player);
			AIFogOfWar.Refresh(player);
			AIThreat threat = AIThreat.Assess(player);
			return t => taken.Contains(t) || threat.IsUnsafeForCivilian(t);
		}

		// Shift-I and Ctrl-Shift-I: improving the tiles of one city, those it
		// works and its known luxuries and strategic resources first, as
		// plain automation does for all of them.
		private static WorkerAIData ThisCityPlan(MapUnit unit, Player player, WorkerAutomation how) {
			City city = how.target?.cityAtTile;
			if (city == null || city.owner != player) {
				return null;
			}
			List<Tile> high = new();
			foreach (CityResident resident in city.residents) {
				if (resident.tileWorked != Tile.NONE && !high.Contains(resident.tileWorked)) {
					high.Add(resident.tileWorked);
				}
			}
			List<Tile> low = new();
			foreach (Tile t in city.GetTilesWithinBorders()) {
				if (t.owningCity != city || high.Contains(t)) {
					continue;
				}
				if ((t.Resource.Category == ResourceCategory.LUXURY || t.Resource.Category == ResourceCategory.STRATEGIC)
						&& player.KnowsAboutResource(t.Resource)) {
					high.Add(t);
				} else {
					low.Add(t);
				}
			}
			System.Func<Tile, bool> excluded = Excluded(unit, player);
			HashSet<Tile> reserved = high.Concat(low).Where(excluded).ToHashSet();
			using ReachableTiles reachable = new();
			return GetPlanToImproveNearestUnimproved(unit, player, high, reserved, reachable)
				?? GetPlanToImproveNearestUnimproved(unit, player, low, reserved, reachable);
		}

		// Shift-F, Shift-W and Shift-D: the one clearing job, on the nearest
		// tile that needs it. "Nearby" is read as within our borders, as
		// plain automation works there.
		private static WorkerAIData ClearingPlan(MapUnit unit, Player player, WorkerAutomation how) {
			if (C7Action.ToTerraform(how.ClearingJob) is not Terraform job) {
				return null;
			}
			System.Func<Tile, bool> excluded = Excluded(unit, player);
			HashSet<Tile> ours = new();
			foreach (City city in player.cities) {
				foreach (Tile t in city.GetTilesWithinBorders()) {
					if (t.OwningPlayer() == player) {
						ours.Add(t);
					}
				}
			}
			List<Tile> tiles = ours.Where(t => !excluded(t) && unit.CanPerformTerraformAction(job, t)).ToList();
			return NearestPlan(unit, tiles, _ => job);
		}

		// Ctrl-N: roads along the way from each of our cities not joined to
		// the capital to it, keeping to our land where it can (see
		// ComputeConnectionRoads). Cities on other landmasses are left out:
		// no road reaches them. The hotkey list calls it "Build Road/Rail
		// Net"; whether Civ3 also lays railroad once the roads are done is
		// unsettled (see the compendium's open questions), so it builds
		// roads.
		private static WorkerAIData TradeNetworkPlan(MapUnit unit, Player player) {
			GameData gameData = EngineStorage.gameData;
			City capital = player.cities.Find(c => c.IsCapital());
			if (gameData == null || capital == null || !unit.IsLandUnit()) {
				return null;
			}
			TradeNetwork network = gameData.GetTradeNetwork();
			AStarAlgorithm algorithm = new(unit) {
				StepPenalty = t => t.OwningPlayer() is Player owner && owner != player ? ForeignRoadStepCost : 0,
			};
			System.Func<Tile, bool> excluded = Excluded(unit, player);
			HashSet<Tile> tiles = new();
			foreach (City c in player.cities) {
				if (c == capital || c.location.continent != capital.location.continent || network.ConnectedToCapital(player, c)) {
					continue;
				}
				TilePath path = algorithm.PathFrom(c.location, capital.location, unit);
				if (path?.path == null) {
					continue;
				}
				foreach (Tile t in path.path) {
					if (t.IsLand() && !t.HasRoad() && !t.HasCity() && (t.OwningPlayer() == null || t.OwningPlayer() == player)
							&& !AIThreat.IsHostileTerritory(player, t) && !excluded(t)) {
						tiles.Add(t);
					}
				}
			}
			return NearestPlan(unit, tiles.ToList(), t => RoadTerraform(unit, t));
		}

		// Ctrl-R, Ctrl-Shift-R and Ctrl-I: the work along the way from where
		// the worker stands to its destination (for Ctrl-I, the nearest of
		// our cities when it was given the order), each tile in turn. The
		// way is found again from wherever the worker is.
		private static WorkerAIData RoutePlan(MapUnit unit, Player player, WorkerAutomation how) {
			Tile goal = how.target;
			if (goal == null || goal == Tile.NONE || (how.kind == WorkerAutomation.Kind.IrrigateToCity && goal.cityAtTile?.owner != player)) {
				return null;
			}
			List<Tile> route = new() { unit.location };
			if (goal != unit.location) {
				TilePath path = PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, goal, unit);
				if (path?.path == null || path.PathLength() == 0) {
					return null;
				}
				route.AddRange(path.path);
			}
			AIFogOfWar.Refresh(player);
			AIThreat threat = AIThreat.Assess(player);
			for (int i = 0; i < route.Count; ++i) {
				Tile t = route[i];
				if (RouteJob(unit, how, t) is not Terraform job) {
					continue;
				}
				// A road or irrigation enemies could catch the worker
				// building ends the order, as the player has to decide.
				if (threat.IsUnsafeForCivilian(t)) {
					return null;
				}
				TilePath way = null;
				if (i > 0) {
					way = PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, t, unit);
					if (way == null || way.PathLength() == 0) {
						return null;
					}
				}
				return MakePlan(unit, t, job, way);
			}
			return null;
		}

		// The job a tile on the way needs, or null. Rail to roads a tile
		// before laying the railroad. Irrigation to a city doesn't replace a
		// mine (as No Altering wouldn't), nor is it needed on the city's
		// own tile.
		private static Terraform RouteJob(MapUnit unit, WorkerAutomation how, Tile t) {
			if (!t.IsLand() || t.HasCity()) {
				return null;
			}
			if (how.kind == WorkerAutomation.Kind.IrrigateToCity) {
				if (t.HasIrrigation() || C7Action.ToTerraform(C7Action.UnitIrrigate) is not Terraform irrigate
						|| !unit.CanPerformTerraformAction(irrigate, t) || AltersImprovement(irrigate, t)) {
					return null;
				}
				return irrigate;
			}
			if (!t.HasRoad()) {
				return RoadTerraform(unit, t);
			}
			if (how.kind == WorkerAutomation.Kind.RailTo && !t.HasRailroad()
					&& C7Action.ToTerraform(C7Action.UnitBuildRailroad) is Terraform rail && unit.CanPerformTerraformAction(rail, t)) {
				return rail;
			}
			return null;
		}

		// A plan for the nearest of the tiles the worker can reach that has
		// a job.
		private static WorkerAIData NearestPlan(MapUnit unit, List<Tile> tiles, System.Func<Tile, Terraform> jobOn) {
			List<(Tile tile, Terraform job)> options = new();
			foreach (Tile t in tiles.OrderBy(t => t.DistanceTo(unit.location))) {
				if (jobOn(t) is Terraform job) {
					options.Add((t, job));
				}
			}
			if (options.Count == 0) {
				return null;
			}
			using ReachableTiles reachable = new();
			int index = PathingAlgorithmChooser.GetAlgorithm(unit)
				.FindFirstReachable(unit.location, options.ConvertAll(o => o.tile), unit, reachable, out TilePath path);
			return index < 0 ? null : MakePlan(unit, options[index].tile, options[index].job, path);
		}
	}
}

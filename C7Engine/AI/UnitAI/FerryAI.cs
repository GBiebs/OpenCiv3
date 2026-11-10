using System.Collections.Generic;
using System.Linq;
using C7Engine.Pathing;
using C7GameData;
using C7GameData.AIData;
using Serilog;

namespace C7Engine {
	// A ship carrying land units for the AI (see SeaAI): it takes on the
	// travellers waiting in its port (OverseasAI), waits a little for more
	// on their way while it has room, sails to a landing spot next to free
	// land near their destination and waits there while they go ashore.
	// Empty, it sails to where travellers wait, or waits in port.
	public class FerryAI : UnitAI {
		private static readonly ILogger log = Log.ForContext<FerryAI>();

		private TilePath path;
		private Tile pathTo;
		private int turnsWaited;
		private int lastWaitTurn = -1;
		private int turnsAtLanding;

		public string SummarizePlan() => $"FerryAI: to {pathTo?.ToString() ?? "-"}";

		public void UpdateOnDeath() { }

		UnitAI.MoveResult UnitAI.PlayTurnImpl(Player player, MapUnit ship) {
			Tile here = ship.location;
			foreach (MapUnit traveller in here.unitsOnTile.ToList()) {
				if (traveller.owner == player && !traveller.IsLoaded() && traveller.currentAI is OverseasAI plan && plan.data.port == here
					&& traveller.GetTransportsToBoard(here).Contains(ship)) {
					traveller.BoardTransport(ship);
				}
			}

			List<MapUnit> aboard = ship.Passengers();
			if (aboard.Count > 0) {
				return Carry(player, ship, aboard);
			}
			turnsWaited = 0;
			turnsAtLanding = 0;

			// Empty: to the port where most travellers wait or are headed.
			Tile pickup = SeaAI.Travellers(player)
				.GroupBy(t => t.plan.data.port)
				.OrderByDescending(g => g.Count()).ThenBy(g => g.Key.DistanceTo(here))
				.Select(g => g.Key)
				.FirstOrDefault(port => port == here || CanReach(ship, port));
			if (pickup != null) {
				return pickup == here ? Wait(ship) : SailTo(ship, pickup);
			}

			// Nothing to carry: wait in port.
			City home = SeaAI.NearestReachablePort(ship, player);
			return home == null || home.location == here ? Wait(ship) : SailTo(ship, home.location);
		}

		private UnitAI.MoveResult Carry(Player player, MapUnit ship, List<MapUnit> aboard) {
			GameData gameData = EngineStorage.gameData;
			Tile here = ship.location;
			Tile target = aboard.Select(p => (p.currentAI as OverseasAI)?.data.destination).FirstOrDefault(d => d != null);
			if (target == null) {
				// Passengers with no plan (a loaded game, or nowhere to land):
				// set them down in our nearest port, for new plans.
				City port = SeaAI.NearestReachablePort(ship, player);
				if (port == null) {
					return Wait(ship);
				}
				if (port.location == here) {
					foreach (MapUnit passenger in aboard) {
						passenger.UnboardTransport(ship);
						passenger.currentAI = null;
					}
					return Wait(ship);
				}
				return SailTo(ship, port.location);
			}

			// In port with room, and more on their way: wait a little.
			if (here.cityAtTile != null && ship.FreeCapacity() > 0 && turnsWaited < SeaAI.FerryWaitTurns
				&& SeaAI.Travellers(player).Any(t => t.plan.data.port == here && t.unit.location != here)) {
				if (lastWaitTurn != gameData.turn) {
					lastWaitTurn = gameData.turn;
					++turnsWaited;
				}
				return Wait(ship);
			}

			(Tile water, Tile land)? spot = SeaAI.LandingSpot(ship, target);
			if (spot == null || turnsAtLanding > 2) {
				// Nowhere to land them (or they can't go ashore): their trip
				// is off, they're taken back to port.
				log.Information($"{ship} can't land its passengers bound for {target}: back to port");
				foreach (MapUnit passenger in aboard) {
					passenger.currentAI = new OverseasAI(new OverseasAIData());
				}
				turnsAtLanding = 0;
				return Wait(ship);
			}
			if (spot.Value.water == here) {
				// They go ashore on their own turns (loaded units play after
				// the ships, see PlayerAI): awake, since boarding put them to
				// sleep.
				foreach (MapUnit passenger in aboard) {
					passenger.Wake();
				}
				if (lastWaitTurn != gameData.turn) {
					lastWaitTurn = gameData.turn;
					++turnsAtLanding;
				}
				return Wait(ship);
			}
			return SailTo(ship, spot.Value.water);
		}

		private static bool CanReach(MapUnit ship, Tile to) {
			TilePath route = PathingAlgorithmChooser.GetAlgorithm(ship).PathFrom(ship.location, to, ship);
			return route != null && route.PathLength() > 0;
		}

		private UnitAI.MoveResult SailTo(MapUnit ship, Tile to) {
			if (pathTo != to || path == null) {
				path = PathingAlgorithmChooser.GetAlgorithm(ship).PathFrom(ship.location, to, ship);
				pathTo = to;
			}
			if (path == null || path.PathLength() <= 0) {
				path = null;
				return UnitAI.Result.Error;
			}
			UnitAI.MoveResult result = this.TryToMoveAlongPath(ship, ref path);
			if (result.Result == UnitAI.Result.Error) {
				path = null;
			}
			return result;
		}

		private static UnitAI.MoveResult Wait(MapUnit ship) {
			ship.movementPoints.onConsumeAll();
			return UnitAI.Result.InProgress;
		}
	}
}

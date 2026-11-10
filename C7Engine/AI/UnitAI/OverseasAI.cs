using System.Linq;
using C7Engine.Pathing;
using C7GameData;
using C7GameData.AIData;
using Serilog;

namespace C7Engine {
	public class OverseasAIData : UnitAIData {
		// Where across the water the unit is bound: a settler's site, an enemy
		// city (null: no plan, it stays aboard until the ferry puts in).
		public Tile destination;
		// The port it waits in for a ferry.
		public Tile port;
		public TilePath path;
		public int turnsWaited;
		public int lastWaitTurn = -1;

		public override string ToString() => $"to {destination?.ToString() ?? "-"} from {port?.ToString() ?? "-"}";
	}

	// A land unit bound across the water: it goes to its port and waits
	// there for a ferry (FerryAI), rides it, and goes ashore once the ferry
	// lies next to free land near its destination. Then its usual AI takes
	// over on the new continent.
	public class OverseasAI : UnitAI {
		private static readonly ILogger log = Log.ForContext<OverseasAI>();
		public OverseasAIData data;

		public OverseasAI(OverseasAIData data) {
			this.data = data;
		}

		public string SummarizePlan() => "OverseasAI: " + data;

		public void UpdateOnDeath() { }

		UnitAI.MoveResult UnitAI.PlayTurnImpl(Player player, MapUnit unit) {
			GameData gameData = EngineStorage.gameData;
			if (unit.IsLoaded()) {
				MapUnit ferry = unit.Container();
				if (data.destination != null && ferry != null) {
					Tile land = ferry.location.neighbors.Values
						.Where(n => n.continent == data.destination.continent && n.DistanceTo(data.destination) <= SeaAI.LandingRadius
							&& SeaAI.IsFreeLand(n, player) && unit.CanEnter(n))
						.OrderBy(n => n.DistanceTo(data.destination))
						.FirstOrDefault();
					if (land != null) {
						SeaAI.Landed(unit, data.destination);
						log.Information($"{unit} goes ashore at {land}, bound for {data.destination}");
						return UnitAI.MoveResult.MoveRequested(unit.Move(unit.location.DirectionTo(land)));
					}
				}
				unit.movementPoints.onConsumeAll();
				return UnitAI.Result.InProgress;
			}

			// Across, or no plan: its usual AI takes over.
			if (data.destination == null || unit.location.continent == data.destination.continent) {
				return UnitAI.Result.Done;
			}
			if (data.port == null || data.port.cityAtTile?.owner != player) {
				return UnitAI.Result.Error;
			}
			if (unit.location != data.port) {
				data.path ??= PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, data.port, unit);
				if (data.path == null || data.path.PathLength() <= 0) {
					return UnitAI.Result.Error;
				}
				return this.TryToMoveAlongPath(unit, ref data.path);
			}

			// In port: aboard a ferry with room, else wait (not forever).
			MapUnit ship = unit.GetTransportsToBoard(unit.location).FirstOrDefault(SeaAI.IsFerry);
			if (ship != null) {
				unit.BoardTransport(ship);
			} else {
				if (data.lastWaitTurn != gameData.turn) {
					data.lastWaitTurn = gameData.turn;
					++data.turnsWaited;
				}
				if (data.turnsWaited > SeaAI.PassengerWaitTurns) {
					return UnitAI.Result.Error;
				}
			}
			unit.movementPoints.onConsumeAll();
			return UnitAI.Result.InProgress;
		}
	}
}

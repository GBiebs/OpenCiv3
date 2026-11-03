using System.Linq;
using Serilog;

namespace C7Engine {
	using C7GameData;
	using System.Collections.Generic;

	public class UnitInteractions {

		private static Queue<MapUnit> waitQueue = new Queue<MapUnit>();
		private static ILogger log = Log.ForContext<UnitInteractions>();

		public static MapUnit getNextSelectedUnit() {
			// The units of the human whose turn it is (with several humans,
			// only theirs).
			foreach (Player player in EngineStorage.gameData.players.Where(p => p.isHuman && p.id == EngineStorage.uiControllerID)) {
				foreach (MapUnit unit in player.units.Where(u => u.movementPoints.canMove)) {
					if (unit.isFortified) {
						continue;
					}

					if (unit.IsBusy()) {
						new MsgPerformUnitAction(unit).send();
						continue;
					}

					// Nor is a unit that takes no orders (a princess).
					if (!unit.CanBeActive()) {
						continue;
					}

					if (!waitQueue.Contains(unit)) {
						return unit;
					}
				}
			}
			if (waitQueue.Count > 0) {
				return waitQueue.Dequeue();
			}
			return MapUnit.NONE;
		}

		public static void ClearWaitQueue() {
			waitQueue.Clear();
		}

		public static void waitUnit(ID id) {
			foreach (MapUnit unit in EngineStorage.gameData.mapUnits) {
				if (unit.id == id) {
					log.Verbose("Found matching unit with id " + id + " of type " + unit.GetType().Name + "; adding it to the wait queue");
					waitQueue.Enqueue(unit);
					return;
				}
			}
			log.Warning("Failed to find a matching unit with id " + id);
		}
	}
}

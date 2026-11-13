using System.Linq;
using C7Engine.Pathing;
using Serilog;

namespace C7Engine {
	using C7GameData;
	using System.Collections.Generic;

	public class UnitInteractions {

		// The units waiting to be selected again, by id rather than by
		// reference: a LAN client replaces every unit object with each of the
		// host's snapshots, so the ids are what stay the same. Each id is
		// looked up in the current game when it comes off the queue.
		private static Queue<ID> waitQueue = new Queue<ID>();

		// How many times each unit appears in waitQueue, so that checking
		// whether a unit is waiting doesn't have to search the queue.
		private static readonly Dictionary<ID, int> waitingUnits = new();

		// The turn and the player the waiting units were waited by. Waiting
		// only lasts for the player's turn, so when either changes (in a
		// hotseat game, when the next human takes over) the queue is dropped.
		private static int waitTurn = -1;
		private static ID waitPlayerID;

		// Busy units already told to carry on. On a LAN client the units don't
		// change until the host's next snapshot, so don't ask again until then.
		private static readonly HashSet<ID> busyActionRequested = new();
		private static GameData busyActionGame;
		private static int busyActionTurn = -1;
		private static ILogger log = Log.ForContext<UnitInteractions>();

		// Picks the next unit that needs orders. Given the previously selected
		// unit's stack, the rest of that stack comes first, starting with the
		// units of the same type, so a stack is given its orders back to back.
		public static MapUnit getNextSelectedUnit(Tile previousStack = null, UnitPrototype previousType = null) {
			GameData gameData = EngineStorage.gameData;
			// In observer mode the UI controller is played by the AI, so there
			// are no units for the UI to select.
			Player player = gameData.GetUIControllerPlayer();
			IEnumerable<MapUnit> selectable = player != null && player.isHuman ? player.units : [];
			if (busyActionGame != gameData || busyActionTurn != gameData.turn) {
				busyActionRequested.Clear();
				busyActionGame = gameData;
				busyActionTurn = gameData.turn;
			}
			SyncWaitQueue(gameData);
			MapUnit first = null;
			MapUnit firstInStack = null;
			foreach (MapUnit unit in selectable.Where(u => u.movementPoints.canMove)) {
				// Units in a rigid army go where the army goes.
				if (unit.isFortified || unit.IsLockedInArmy()) {
					continue;
				}

				if (unit.IsBusy()) {
					if (busyActionRequested.Add(unit.id)) {
						new MsgPerformUnitAction(unit).send();
					}
					continue;
				}

				if (unit.id is not null && waitingUnits.ContainsKey(unit.id)) {
					continue;
				}
				// Already given a path that hasn't been handled yet: selecting
				// it would cancel the path once it is.
				if (EngineStorage.HasPendingPathFor(unit.id)) {
					continue;
				}
				if (previousStack == null) {
					return unit;
				}
				// Compared by position and name, as a LAN client's snapshots
				// replace the tile and prototype objects.
				if (unit.location.XCoordinate == previousStack.XCoordinate && unit.location.YCoordinate == previousStack.YCoordinate) {
					if (unit.unitType?.name == previousType?.name) {
						return unit;
					}
					firstInStack ??= unit;
				}
				first ??= unit;
			}
			if (firstInStack != null) {
				return firstInStack;
			}
			if (first != null) {
				return first;
			}
			while (waitQueue.Count > 0) {
				ID id = waitQueue.Dequeue();
				if (waitingUnits[id] <= 1) {
					waitingUnits.Remove(id);
				} else {
					--waitingUnits[id];
				}
				// The unit may have died, or changed hands, since it waited.
				MapUnit next = gameData.GetUnit(id);
				if (next == null || player == null || !player.isHuman || next.owner != player) {
					continue;
				}
				return next;
			}
			return MapUnit.NONE;
		}

		public static void ClearWaitQueue() {
			waitQueue.Clear();
			waitingUnits.Clear();
		}

		// Drops the waiting units if they were waited in another turn, or by
		// another player than the one the UI now shows.
		private static void SyncWaitQueue(GameData gameData) {
			ID playerID = EngineStorage.uiControllerID;
			if (waitTurn != gameData.turn || waitPlayerID != playerID) {
				ClearWaitQueue();
				waitTurn = gameData.turn;
				waitPlayerID = playerID;
			}
		}

		// Forgets everything about the previous game. Called when a game is
		// created or loaded.
		internal static void ResetForNewGame() {
			ClearWaitQueue();
			waitTurn = -1;
			waitPlayerID = null;
			OnGameReplaced();
		}

		// Called when a LAN client replaces its game with the host's snapshot.
		// The waiting units are kept, since they are found by id in whichever
		// game is current (and dropped once the turn or player changes), but
		// nothing here may keep the replaced game alive. Busy units are asked
		// to carry on again, as after any snapshot.
		internal static void OnGameReplaced() {
			busyActionRequested.Clear();
			busyActionGame = null;
			busyActionTurn = -1;
		}

		// The orders for a stack move (the J key): the leader along its own
		// path, and every other unit of ours on its tile that can move, each
		// along its own path to the same destination. The paths are all
		// worked out before anything moves, since moving changes who's on
		// the tile.
		//
		// Every unit with moves left goes, not only those that haven't moved
		// yet this turn: in Civ3 "when I move a group of units with differing
		// MP left, it goes to the maximum distance of the unit with the lowest
		// MP" (ynot56, C3X thread, post-16163674). A stack that had walked
		// along our roads up to a border, say, still goes into the land
		// beyond together.
		public static List<(MapUnit unit, TilePath path)> StackMoveOrders(MapUnit leader, TilePath leaderPath, Tile destination) {
			List<(MapUnit, TilePath)> moves = [(leader, leaderPath)];
			foreach (MapUnit unit in leader.location.unitsOnTile) {
				// Loaded units ride along with their transport. Fortified
				// units stay put, as do units out of moves and workers at work
				// or automated.
				if (unit == leader || unit.owner != leader.owner || unit.IsLoaded()
					|| unit.isFortified || unit.WorkerJob != null || unit.isAutomated
					|| !unit.movementPoints.canMove
					|| !unit.GetAvailableActions().Contains(UnitAction.Goto)) {
					continue;
				}
				TilePath path = PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(unit.location, destination, unit);
				if (path != null && path.PathLength() > 0) {
					moves.Add((unit, path));
				}
			}
			return moves;
		}

		// The aircraft a stack re-base (J with a plane selected, Civ3's "Stack
		// Go to" for aircraft) sends to the destination: the leader and every
		// other aircraft of ours on its tile, aboard a carrier or not, that
		// can re-base there (MapUnit.CanRebaseTo), the leader first.
		public static List<MapUnit> StackRebasers(MapUnit leader, Tile destination) {
			List<MapUnit> planes = [];
			if (leader?.location == null || !leader.CanRebaseTo(destination)) {
				return planes;
			}
			planes.Add(leader);
			foreach (MapUnit unit in leader.location.unitsOnTile) {
				if (unit != leader && unit.owner == leader.owner && unit.IsAirUnit() && unit.CanRebaseTo(destination)) {
					planes.Add(unit);
				}
			}
			return planes;
		}

		// The choices for Civ3's Go to City (Ctrl-Shift-G; script.txt's
		// GOTO_CITY): the owner's cities the unit can reach, other than the
		// one it's in, by name, each with the path a go-to there takes and
		// the turns the go-to cursor shows for it.
		public static List<(City city, TilePath path, int turns)> GotoCityChoices(MapUnit unit) {
			List<(City, TilePath, int)> choices = [];
			if (unit?.location == null || unit.owner == null || !unit.GetAvailableActions().Contains(UnitAction.Goto)) {
				return choices;
			}
			PathingAlgorithm pathing = PathingAlgorithmChooser.GetAlgorithm(unit);
			foreach (City city in unit.owner.cities.OrderBy(c => c.name, System.StringComparer.CurrentCultureIgnoreCase)) {
				if (city.location == unit.location) {
					continue;
				}
				TilePath path = pathing.PathFrom(unit.location, city.location, unit);
				int turns = path?.PathCost(unit.owner, unit.location, unit.MaxMovementPoints(), unit.movementPoints.remaining, unit.unitType) ?? -1;
				if (turns >= 0) {
					choices.Add((city, path, turns));
				}
			}
			return choices;
		}

		public static void waitUnit(ID id) {
			GameData gameData = EngineStorage.gameData;
			MapUnit unit = gameData.GetUnit(id);
			if (unit == null || unit.id is null) {
				log.Warning("Failed to find a matching unit with id {Id}", id);
				return;
			}
			SyncWaitQueue(gameData);
			log.Verbose("Found matching unit with id {Id} of type {Type}; adding it to the wait queue", id, unit.GetType().Name);
			waitQueue.Enqueue(unit.id);
			waitingUnits[unit.id] = waitingUnits.GetValueOrDefault(unit.id) + 1;
		}
	}
}

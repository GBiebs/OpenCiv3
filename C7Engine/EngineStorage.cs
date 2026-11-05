using System.Linq;

namespace C7Engine {
	using System;
	using C7GameData;
	using System.Collections.Generic;
	using System.Threading.Tasks;

	/**
	 * This class stores references to data that the engine needs between calls from the player.
	 * Most obviously this includes a reference to the C7GameData, but it might eventually
	 * also include things like keeping track of which networked players are up to date.
	 *
	 * Note that we should NOT store pointers to pieces of the game data here; that will
	 * all be handled within C7GameData.  We just need a pointer to the main, top level
	 * so we don't forget the state of the game after we create it.
	 **/
	public static class EngineStorage {
		public static GameData gameData { get; set; }

		public static ID uiControllerID;
		// Online: this computer's player, whose view the screens show (the
		// others play on their own computers). Null in a game at one
		// computer, where the screens show the player in control.
		public static ID localPlayerID;
		public static ID viewerID => localPlayerID ?? uiControllerID;
		internal static bool animationsEnabled = true;

		internal static readonly Queue<MessageToEngine> pendingMessages = new();
		internal static readonly Queue<MessageToUI> messagesToUI = new();
		internal static readonly Queue<AnimationMessage> animationMessages = new();

		internal static readonly Dictionary<Guid, TaskCompletionSource<bool>> pendingAnimations = new();
		static readonly Dictionary<Type, TaskCompletionSource<MessageToEngine>> pendingEngineWaiters = new();

		// Orders whose work goes on after they're processed (a move's
		// animation, a turn of AIs), until it's done.
		private static readonly List<Task> working = new();
		private static readonly Serilog.ILogger log = Serilog.Log.ForContext(typeof(EngineStorage));

		// Whether every order's work is done (a civ whose player has left
		// taken out too).
		public static bool OrdersDone() => WorkDone() && leaving.Count == 0;

		private static bool WorkDone() {
			working.RemoveAll(t => t.IsCompleted);
			return working.Count == 0;
		}

		// Online: players who have left (MsgPlayerLeftGame), whose civs go
		// once the work going on is done; until then what the engine asks
		// them is answered for them.
		internal static readonly List<ID> leaving = new();

		// Online play (OnlineGame): where this game's orders go instead of the
		// queue (to the host), and the orders as the host put them in
		// sequence, each with its dice (a seed for GameData.rng). Null and
		// empty in a game at one computer.
		public static Action<MessageToEngine> sendOrder;
		internal static readonly Queue<(long seq, int seed, ID from, MessageToEngine order)> sequencedOrders = new();
		// The last of them played; and called once an End Turn's work (the
		// turn's AIs) is done, with its place in the sequence (the games
		// compare their fingerprints then).
		public static long lastSequenced { get; private set; }
		public static Action<long> onEndTurnDone;
		private static bool endTurnGoing;

		// `from`: the player whose order it is; every game plays it only if
		// they're the one in control then (whose turn it is, or who the
		// engine is asking), so all turn down the same orders.
		public static int OrdersWaiting => sequencedOrders.Count;

		public static void EnqueueSequenced(long seq, int seed, ID from, MessageToEngine order) {
			sequencedOrders.Enqueue((seq, seed, from, order));
		}

		// A game begun again from the host's copy (online): nothing of the
		// last one's work goes on (its tasks are left waiting for good, so no
		// AI carries on in the new copy), and none of its messages are left.
		public static void AbandonWork() {
			working.Clear();
			pendingEngineWaiters.Clear();
			pendingAnimations.Clear();
			pendingMessages.Clear();
			messagesToUI.Clear();
			animationMessages.Clear();
			ResetSequence();
		}

		public static void ResetSequence(long seq = 0) {
			sequencedOrders.Clear();
			leaving.Clear();
			lastSequenced = seq;
			endTurnGoing = false;
		}

		// Whether the next order in sequence may be played: the last one's
		// work done, or the engine waiting for this very answer; no animation
		// going. So every game plays each order at the same point.
		private static bool ReadyFor(MessageToEngine next) {
			return !HasPendingAnimations() && (next.Urgent || OrdersDone() || pendingEngineWaiters.ContainsKey(next.GetType()));
		}

		public static void ProcessNextMessageToEngine() {
			if (endTurnGoing && OrdersDone() && !HasPendingAnimations()) {
				endTurnGoing = false;
				onEndTurnDone?.Invoke(lastSequenced);
			}
			if (leaving.Count > 0 && !HasPendingAnimations()) {
				if (leaving.Contains(uiControllerID) && LeavingPlayers.AnswerFor(pendingEngineWaiters.Keys) is MessageToEngine answer) {
					Process(answer);
					return;
				}
				if (WorkDone()) {
					ID gone = leaving[0];
					leaving.RemoveAt(0);
					Run(LeavingPlayers.TakeOut(gameData, gone), nameof(LeavingPlayers));
					// (The games compare once it's done, as after an End Turn.)
					endTurnGoing = true;
					return;
				}
			}
			if (pendingMessages.Count == 0 && sequencedOrders.Count > 0 && ReadyFor(sequencedOrders.Peek().order)) {
				(long seq, int seed, ID from, MessageToEngine order) = sequencedOrders.Dequeue();
				lastSequenced = seq;
				if (from != uiControllerID && !order.HostOnly) {
					log.Information("Order {Seq} ({Kind}) from {From}, not in control: not played", seq, order.GetType().Name, from);
					return;
				}
				GameData.rng = new Random(seed);
				endTurnGoing |= order is MsgEndTurn or MsgTurnTimeUp;
				Process(order);
				return;
			}
			if (pendingMessages.Count > 0) {
				Process(pendingMessages.Dequeue());
			}
		}

		private static void Process(MessageToEngine msg) {
			Run(msg.ProcessAsync(), msg.GetType().Name);

			if (msg.IsPlayerAction && gameData != null) {
				Player controller = gameData.GetPlayer(uiControllerID);
				controller?.stats.RecordAction(controller, gameData);
			}

			var type = msg.GetType();
			if (pendingEngineWaiters.TryGetValue(type, out var tcs)) {
				tcs.TrySetResult(msg);
				pendingEngineWaiters.Remove(type);
			}
		}

		private static void Run(Task work, string what) {
			if (!work.IsCompleted) {
				working.Add(work);
			}
			work.ContinueWith(t => log.Error(t.Exception, "{Order} failed", what), TaskContinuationOptions.OnlyOnFaulted);
		}

		public static bool HasPendingAnimations() {
			return pendingAnimations.Count > 0;
		}

		public static bool TryDequeueNextMessageToUI(out MessageToUI message) {
			if (messagesToUI.Count > 0) {
				message = messagesToUI.Dequeue();
				return true;
			}
			message = null;
			return false;
		}

		public static bool TryDequeueNextAnimationMessage(out AnimationMessage message) {
			if (animationMessages.Count > 0) {
				message = animationMessages.Dequeue();
				return true;
			}
			message = null;
			return false;
		}

		internal static Task WaitForAnimationFinished(Guid animationId) {
			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			pendingAnimations[animationId] = tcs;
			return tcs.Task;
		}

		public static Task<T> WaitForMessageToEngine<T>() where T : MessageToEngine {
			var tcs = new TaskCompletionSource<MessageToEngine>(TaskCreationOptions.RunContinuationsAsynchronously);
			pendingEngineWaiters[typeof(T)] = tcs;

			return tcs.Task.ContinueWith(t => (T)t.Result);
		}

		// Whether the engine is waiting for a T from the UI (for tests, whose
		// answers must come after the engine starts waiting).
		internal static bool IsWaitingForMessageToEngine<T>() where T : MessageToEngine {
			return pendingEngineWaiters.ContainsKey(typeof(T));
		}

		public static void ReadGameData(Action<GameData> accessor) {
			accessor(gameData);
		}

		public static void InitializeGameDataForTests(GameData gD) {
			gameData = gD;
		}

		/// <summary>
		/// Returns the player that either controls the UI, and if that somehow doesn't exist, it returns the first human player 
		/// </summary>
		/// <returns></returns>
		// The player whose view the screens show.
		public static Player GetViewer() {
			return gameData.players.FirstOrDefault(p => p.id == viewerID) ?? gameData.GetFirstHumanPlayer();
		}

		public static Player GetUIController() {
			return gameData.players.FirstOrDefault(p => p.id == uiControllerID) ?? gameData.GetFirstHumanPlayer();
		}
	}
}

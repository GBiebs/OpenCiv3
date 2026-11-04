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

		// Whether every order's work is done.
		public static bool OrdersDone() {
			working.RemoveAll(t => t.IsCompleted);
			return working.Count == 0;
		}

		public static void ProcessNextMessageToEngine() {
			if (pendingMessages.Count > 0) {
				var msg = pendingMessages.Dequeue();
				Task work = msg.ProcessAsync();
				if (!work.IsCompleted) {
					working.Add(work);
				}
				work.ContinueWith(t => log.Error(t.Exception, "{Order} failed", msg.GetType().Name), TaskContinuationOptions.OnlyOnFaulted);

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
		public static Player GetUIController() {
			return gameData.players.FirstOrDefault(p => p.id == uiControllerID) ?? gameData.GetFirstHumanPlayer();
		}
	}
}

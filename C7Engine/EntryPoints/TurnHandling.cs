using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using Serilog;

namespace C7Engine {

	public class TurnHandling {
		private static ILogger log = Log.ForContext<TurnHandling>();

		public static void OnBeginTurn() {
			GameData gameData = EngineStorage.gameData;
			log.Information("\n*** Beginning turn {Turn} ***", gameData.turn);
		}

		public static void OnEndTurn(Player player) {
			GameData gameData = EngineStorage.gameData;

			// The player's own list of units, rather than every unit in the
			// game. Copied, in case finishing a job changes it.
			foreach (MapUnit busyWorker in player.units.ToArray())
				if (busyWorker.WorkerJob != null)
					busyWorker.PerformEndOfTurnAction();

			// Ships left out in water they aren't built for may sink.
			MapUnit.SinkShipsInUnsafeWater(gameData, player);
		}

		public static void InitTurnData(Player player = null, bool skipTurn = false) {
			GameData gameData = EngineStorage.gameData;
			if (player == null) {
				foreach (MapUnit mapUnit in gameData.mapUnits)
					mapUnit.OnBeginTurn(skipTurn);
			} else {
				foreach (MapUnit mapUnit in player.units)
					mapUnit.OnBeginTurn(skipTurn);
			}
		}

		// The turn loop that is running, if any (see AdvanceTurn).
		private static object runningTurnLoop;

		// Whether the other players are taking their turns, so the turn loop
		// mustn't be started again.
		public static bool TurnInProgress => runningTurnLoop != null;

		// Forgets a turn loop left waiting by the previous game: it never
		// resumes (see EngineStorage.ResetForNewGame).
		internal static void ResetForNewGame() {
			runningTurnLoop = null;
			aiPlayedPausedTurn = null;
		}

		// In observer mode, the player whose turn the game paused on after
		// the AI played it. A turn that began before observing hasn't been
		// played by the AI; see MsgEndTurn.
		internal static ID aiPlayedPausedTurn;

		// Implements the game loop. This method is called when the game is started and when the player signals that they're done moving.
		// Only one loop runs at a time: were a second to start while the
		// first waits (for an animation, say), the AIs would play twice.
		public static async Task AdvanceTurn() {
			if (runningTurnLoop != null) {
				log.Warning("Not advancing the turn: the turn loop is already running");
				return;
			}
			object loop = new();
			runningTurnLoop = loop;
			try {
				await RunTurnLoop();
			} finally {
				// A loop dropped with its game never gets here; if it somehow
				// did, it mustn't release the new game's loop.
				if (runningTurnLoop == loop) {
					runningTurnLoop = null;
				}
			}
		}

		private static async Task RunTurnLoop() {
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			GameData gameData = EngineStorage.gameData;

			// Loop ends with a function return once we reach the UI controller during the movement phase
			while (true) {
				// Watching the AIs play, the game goes on when the civ being
				// followed is destroyed: the view follows another.
				if (gameData.observerMode) {
					FollowAnotherCivIfFollowedOneFell(gameData);
				}

				// The loop hands control back at a human, or at the player
				// being observed. With neither left, the AIs would play
				// against each other forever with nothing for the UI to do.
				if (!gameData.players.Any(p => !p.defeated && (p.isHuman || p.id == EngineStorage.activePlayerID))) {
					log.Information("No human players remain, ending the game");
					// Under Rising Tide the game ends with the last human, and
					// the best score so far wins.
					if (RisingTide.IsOn(gameData) && !gameData.gameOver) {
						EndRisingTide(gameData);
						if (gameData.gameOver) {
							return;
						}
					}
					gameData.gameOver = true;
					new MsgNoHumansRemain().send();
					return;
				}

				bool firstTurn = GetTurnNumber() == 0;

				// Staging comes before anyone moves, and waits for every
				// human to choose where their unit goes (see Staging).
				if (Staging.InProgress(gameData)) {
					Staging.AskHumans(gameData);
					if (Staging.InProgress(gameData)) {
						return;
					}
				}

				// Movement phase
				if (await PlayPlayerTurns(gameData, firstTurn)) {
					stopwatch.Stop();
					log.Debug("Turn time took {Milliseconds} milliseconds", stopwatch.ElapsedMilliseconds);
					return;
				}

				// Clear all wait queue, so if a player ended the turn without handling all waited units, they are selected
				// at the same place in the order. Confirmed this is what Civ3 does.
				UnitInteractions.ClearWaitQueue();

				BarbarianInteractions.DoTurn(gameData);

				// With every player's turn over, Victory Locations score.
				VictoryPoints.ScoreVictoryLocations(gameData);

				// Now and then a historian writes one of the Great Books, as
				// Civ3 does before the turn advances (see GreatBooks).
				if (GreatBooks.MaybeWrite(gameData, GameData.rng) is GreatBooks.Book book) {
					foreach (Player reader in gameData.players.Where(p => p.isHuman && !p.defeated)) {
						new MsgGreatHistoryBook(reader, book.historian, book.title, book.ranking).send();
					}
				}

				gameData.turn++;
				gameData.DestroyDefeatedCivs();
				foreach (Player player in gameData.players) {
					await EngineStorage.YieldIfFrameBudgetSpent();
					player.MaybeSpawnBonusUnits(gameData);
					player.RecordObtainedResources(gameData);
					player.DecrementCityUnhappinessPenalties(gameData);
					Espionage.DoPerTurnUpdates(player, gameData);
					player.UpdateWarWeariness(gameData);
					player.RecalculateCitizenMoods(gameData, goIntoDisorderIfUnhappy: true);
					player.DoCorruptionCalculations(gameData);

					// Finances, growth and production below add to this
					// turn's statistics.
					if (!player.IsIndependent && !player.defeated) {
						player.stats.StartTurn(gameData.turn);
					}

					// A tech paid for by an earlier turn's beakers is learned
					// before this turn's beakers are added, so it arrives the
					// turn after its cost is met (see CompletePaidResearch).
					player.CompletePaidResearch(gameData);

					// Do the financial updates before updating the cities, so
					// that a newly produced unit won't put a player over the
					// unit support cap and cause them to lose gold unexpectedly.
					player.DoPerTurnFinanceUpdates(gameData);
					player.DoPerTurnScienceUpdates(gameData);

					// Free units from wonders like the Statue of Zeus and
					// Knights Templar. Before city production, so
					// a wonder finished this turn starts counting next turn.
					WonderUnits.DoPerTurnUpdates(player, gameData);
					player.DoGreatLibraryUpdates(gameData);

					// Note that we do growth after calculating citizen moods,
					// to ensure that the player has a chance to deal with the
					// unhappiness of a new citizen during their turn.
					log.Information("\n*** City growth/production for turn {Turn}, player {Player} ***", gameData.turn, player);
					player.HandleCityUpdates(gameData);

					player.UpdateHistory(gameData);
					player.AdvanceGoldenAge();

					// Civ3 checks each civ's palace achievements every turn
					// (FUN_0056c5a0 calls FUN_00589ae0).
					PalaceUpgrades.CheckAchievements(player, gameData);
				}

				// The statistics' end-of-turn snapshot, once every player's
				// cities have grown and borders expanded.
				Dictionary<Player, PlayerStats.LandCount> land = PlayerStats.CountLand(gameData.map);
				foreach (Player player in gameData.players) {
					player.stats.EndTurn(player, land.GetValueOrDefault(player));
				}

				// In the interturn, cities may flip to a civ whose culture
				// outweighs their owner's.
				CultureFlip.ProcessEndOfRound(gameData);

				// Strategic resources in use may run out.
				ResourceDepletion.ProcessEndOfRound(gameData);

				// A scenario's plague may break out, end or strike (before
				// the volcanoes, as in Civ3).
				Plague.ProcessEndOfRound(gameData);

				// Volcanoes may erupt.
				Volcano.ProcessEndOfRound(gameData);

				// World pollution may bring global warming (after the
				// volcanoes, as in Civ3).
				GlobalWarming.ProcessEndOfRound(gameData);

				// The United Nations votes once everyone has played the
				// voting turn, and may elect a winner.
				UnitedNations.ProcessEndOfRound(gameData);

				CheckVictory(gameData);

				// Twenty turns before the turn limit, the humans are warned
				// that the game will end.
				TimeLimitVictory.MaybeWarn(gameData);

				// Now that the turn is ending, do all the bookkeeping for the
				// start of the next turn. We don't put the "hasPlayedThisTurn"
				// logic in OnBeginTurn because OnBeginTurn is called when a
				// save game is loaded, and that would erase the saved information
				// about which players have played.
				OnBeginTurn();
				InitTurnData();
				foreach (Player player in gameData.players) {
					player.hasPlayedThisTurn = false;
				}
				RisingTide.BeginTurn(gameData);
				CityStates.BeginTurn(gameData);
				Staging.BeginTurn(gameData);
			}
		}

		/// <summary>
		/// Plays the turns for all the players in the game (including barbarians).
		/// </summary>
		/// <param name="gameData"></param>
		/// <param name="firstTurn"></param>
		/// <returns>true when it is time for the human to take control again</returns>
		private static async Task<bool> PlayPlayerTurns(GameData gameData, bool firstTurn) {
			// With simultaneous turns, the humans all move at once, before
			// the AIs, and the AIs wait until the last of them is done.
			if (gameData.simultaneousTurns) {
				List<Player> toMove = PlayersToMove(gameData);
				if (toMove.Count > 0) {
					EngineStorage.activePlayerID = toMove[0].id;
					foreach (Player human in toMove) {
						BeginPlayersTurn(human, gameData);
						StartHumanTurn(human, gameData);
					}
					return true;
				}
			}

			foreach (Player player in PlayersInTurnOrder(gameData)) {
				if (player.hasPlayedThisTurn || player.defeated) {
					continue;
				}

				if (firstTurn && player.SitsOutFirstTurn()) {
					continue;
				}

				await EngineStorage.YieldIfFrameBudgetSpent();
				BeginPlayersTurn(player, gameData);
				if (player.isBarbarians) {
					await BarbarianAI.PlayTurn(player, gameData);
				} else if (player.isCityStates) {
					await CityStatesAI.PlayTurn(player, gameData);
				} else if (!player.isHuman) {
					await PlayerAI.PlayTurn(player, gameData);
				}

				// Each human player takes the turn in order. In a hotseat game
				// the UI goes with them; in a LAN game it is their machine's turn.
				if (player.isHuman) {
					EngineStorage.activePlayerID = player.id;
					if (EngineStorage.uiFollowsActivePlayer) {
						EngineStorage.uiControllerID = player.id;
					}
				}

				//Human player check. Let the human see what's going on even if they are in observer mode.
				if (player.id == EngineStorage.activePlayerID) {
					aiPlayedPausedTurn = player.isHuman ? null : player.id;
					StartHumanTurn(player, gameData);
					return true;
				}

				OnEndTurn(player);
				player.hasPlayedThisTurn = true;
			}
			return false;
		}

		// In observer mode, follows the first civ still in the game, in turn
		// order, if the one being followed has been destroyed.
		private static void FollowAnotherCivIfFollowedOneFell(GameData gameData) {
			Player followed = gameData.GetPlayer(EngineStorage.activePlayerID);
			if (followed != null && !followed.defeated) {
				return;
			}
			Player next = PlayersInTurnOrder(gameData).FirstOrDefault(p => !p.defeated && !p.IsIndependent);
			if (next == null) {
				return;
			}
			log.Information("{Followed} has been destroyed; now following {Next}", followed, next);
			EngineStorage.activePlayerID = next.id;
			EngineStorage.uiControllerID = next.id;
		}

		// Hands the turn to a human, or to the player being observed.
		// What happens to the player's units as its own turn begins, rather
		// than at the end of the round (InitTurnData): their air missions
		// end (MapUnit.BeginOwnersTurn).
		internal static void BeginPlayersTurn(Player player, GameData gameData) {
			foreach (MapUnit unit in player.units) {
				unit.BeginOwnersTurn(gameData.turn);
			}
		}

		private static void StartHumanTurn(Player player, GameData gameData) {
			if (player.isHuman) {
				// The play log's row of the human's state (see PlayLog.PlayRecorder).
				PlayLog.PlayRecorder.HumanTurnStarted(player, gameData);
				// TODO: Before we call this method to automatically end obsolete deals, we could make this more versatile.
				// For example unless we have a good reason, as a human, receiving luxuries, gpt,
				// or having an active RoP, doesn't hurt us.
				PlayerRelationship.CheckForObsoleteDeals(player, gameData.players, gameData.turn);
			}
			new MsgStartTurn(player).send();
			PalaceUpgrades.AnnounceToHuman(player);
			UnitedNations.AskHumanToVote(gameData, player);
			CultureFlip.AskHuman(gameData, player);
		}

		// The humans who may play now, in turn order. With simultaneous turns
		// that is every human yet to finish this turn; otherwise it is the
		// human whose turn it is, if they haven't finished it.
		public static List<Player> PlayersToMove(GameData gameData) {
			if (!gameData.simultaneousTurns) {
				Player active = gameData.GetPlayer(EngineStorage.activePlayerID);
				return active != null && active.isHuman && !active.defeated && !active.hasPlayedThisTurn ? [active] : [];
			}
			bool firstTurn = gameData.turn == 0;
			return PlayersInTurnOrder(gameData)
				.Where(p => p.isHuman && !p.defeated && !p.hasPlayedThisTurn && !(firstTurn && p.SitsOutFirstTurn()))
				.ToList();
		}

		// Whether it is the player's turn, so they may act. Without
		// simultaneous turns that is the active player (who may be an AI
		// being observed), until they end their turn: they stay the active
		// player while the AIs play, but may no longer act.
		public static bool IsPlayersTurn(GameData gameData, ID playerID) {
			if (playerID == null) {
				return false;
			}
			// No one moves during Staging.
			if (Staging.InProgress(gameData)) {
				return false;
			}
			if (!gameData.simultaneousTurns) {
				return playerID == EngineStorage.activePlayerID && gameData.GetPlayer(playerID)?.hasPlayedThisTurn == false;
			}
			return PlayersToMove(gameData).Any(p => p.id == playerID);
		}

		// Order players: Humans -> AI -> Barbarian AI. The sort is stable, so
		// humans play in the order they appear in the player list.
		internal static List<Player> PlayersInTurnOrder(GameData gameData) {
			return gameData.players.OrderByDescending(p => !p.IsIndependent).ThenByDescending(p => p.isHuman).ToList();
		}

		// Returns the human player who should control the UI when a game is
		// loaded: the first human who has yet to play this turn, or the first
		// human if they all have.
		internal static Player FirstHumanToPlay(GameData gameData) {
			List<Player> humans = PlayersInTurnOrder(gameData).Where(p => p.isHuman).ToList();
			return humans.FirstOrDefault(p => !p.hasPlayedThisTurn && !p.defeated) ?? humans.FirstOrDefault();
		}

		// True when more than one human shares this computer.
		public static bool IsHotseat(GameData gameData) {
			return gameData.players.Count(p => p.isHuman && !p.defeated) > 1;
		}

		///Eventually we'll have a game year or month or whatever, but for now this provides feedback on our progression
		public static int GetTurnNumber() {
			return EngineStorage.gameData.turn;
		}

		internal static void CheckVictory(GameData gameData) {
			if (gameData.gameOver)
				return; // Game is already over

			// Rising Tide ends with one civ standing or none, and the best
			// score wins, fallen or not.
			if (RisingTide.IsOn(gameData) && RisingTide.IsOver(gameData)) {
				EndRisingTide(gameData);
				return;
			}

			List<Tuple<Player, IVictory>> winners = [];

			foreach (Player player in gameData.players) {
				if (player.IsIndependent || player.defeated)
					continue;

				foreach (IVictory victory in gameData.victories) {
					VictoryStatus status = victory.Evaluate(player, gameData);
					if (victory.HasVictory(status)) {
						winners.Add(new Tuple<Player, IVictory>(player, victory));
						break;
					}
				}
			}

			if (winners.Count == 1) {
				DeclareVictory(winners[0].Item1, winners[0].Item2, gameData);
			} else if (winners.Count > 1) {
				// Under Victory Point Scoring, "If the game ends and no one
				// has reached [the limit], then the winner is given to the
				// civilization that has amassed the most victory points"
				// (the Middle Ages conquest's Civilopedia); otherwise the
				// highest score wins.
				bool byVictoryPoints = gameData.victoryConditions?.VictoryLocations == true;
				var topScoring = winners.OrderByDescending(pv => {
					var player = pv.Item1;
					if (byVictoryPoints) {
						return player.victoryPoints;
					}
					HistTurnRecord lastTurn = gameData.history[player.id.ToString()].LastOrDefault();
					return lastTurn?.Score ?? 0;
				}).First();
				DeclareVictory(topScoring.Item1, topScoring.Item2, gameData);
			}
		}

		// Ends a Rising Tide game, the highest score winning.
		internal static void EndRisingTide(GameData gameData) {
			Player winner = RisingTide.Winner(gameData);
			IVictory victory = gameData.victories.OfType<RisingTideVictory>().FirstOrDefault();
			if (winner == null || victory == null) {
				return;
			}
			DeclareVictory(winner, victory, gameData);
		}

		private static void DeclareVictory(Player winner, IVictory victory, GameData gameData) {
			Log.Information("Player {Winner} has wone a {Victory} victory!",
				winner, victory.Header());

			gameData.winner = winner;
			gameData.gameOver = true;
			// The tide sets when the game ends, so there's no winning early.
			gameData.victoryBonus = RisingTide.IsOn(gameData) ? 0 : ScoreVictory.EarlyWinBonus(gameData);

			int finalScore = (int)new ScoreVictory().Evaluate(winner, gameData).Score;
			new MsgVictory(winner, victory, finalScore, gameData.victoryBonus).send();
		}
	}
}

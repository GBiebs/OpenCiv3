using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using C7Engine.AI;
using C7GameData;
using Serilog;

namespace C7Engine {

	public class TurnHandling {
		private static ILogger log = Log.ForContext<TurnHandling>();

		public static void OnBeginTurn() {
			GameData gameData = EngineStorage.gameData;
			log.Information("\n*** Beginning turn " + gameData.turn + " ***");
		}

		// Before the engine asks a human something and waits for the answer:
		// with several humans (hotseat) it may not be their turn, so they're
		// given control first, and MsgHandOver lets the UI change players.
		// Play by email: an AI talks to a human only at the start of that
		// human's turn, on their computer (HearFromOthers), never on its own
		// turn, when no one would be there to answer. `onlyWith` is the human
		// it's talking to then.
		public static bool TalksTo(GameData gameData, Player them, Player onlyWith) {
			return onlyWith != null ? them == onlyWith : !(gameData.playByEmail && them.isHuman);
		}

		// Hotseat and play by email: once the human whose turn it is has given
		// their password, the news they missed while away (by email), the
		// deals other humans have sent them (HumanDiplomacy), then by email,
		// once a turn, what the AIs have to say to them: an election to call
		// or a vote, offers of peace, agreements, demands and trades, as on
		// the AIs' turns (trades as often too). Then their turn begins
		// (MsgTurnReady).
		public static async Task HearFromOthers(Player human) {
			GameData gameData = EngineStorage.gameData;
			if (human.newsWhileAway.Count > 0) {
				new MsgNewsWhileAway(human.id, new(human.newsWhileAway)).send();
				human.newsWhileAway.Clear();
				await EngineStorage.WaitForMessageToEngine<MsgNewsRead>();
			}
			await HumanDiplomacy.HearFromHumans(gameData, human);
			if (gameData.playByEmail && human.heardFromAIsOnTurn != gameData.turn) {
				human.heardFromAIsOnTurn = gameData.turn;
				await UnitedNations.MaybeCallElection(gameData, human);
				await UnitedNations.CastAwaitedVote(gameData, human);
				foreach (Player ai in gameData.players.Where(p => !p.isHuman && !p.isBarbarians && !p.defeated && p.isIncludedInGame).ToList()) {
					if (human.defeated || gameData.gameOver) {
						break;
					}
					await PeaceAI.OfferPeace(ai, gameData, human);
					await AgreementAI.ProposeAgreements(ai, gameData, human);
					await TributeAI.MakeDemands(ai, gameData, human);
					if (GameData.rng.Next(100) < 25) {
						await PlayerAI.AttemptTrading(ai, human);
						await TradeAI.SwapGoods(ai, gameData, human);
					}
				}
			}
			new MsgTurnReady(human.id).send();
		}

		public static void HandControlTo(Player human) {
			if (human.id == EngineStorage.uiControllerID) {
				return;
			}
			EngineStorage.uiControllerID = human.id;
			new MsgHandOver(human.id).send();
		}

		public static void OnEndTurn(Player player) {
			GameData gameData = EngineStorage.gameData;

			// Ships out where they may sink.
			Naval.SinkShips(gameData, player);

			// (A list: a worker that finishes a structure is used up.)
			List<MapUnit> busyWorkers = gameData.mapUnits.Where(u => u.owner.id == player.id && u.WorkerJob != null).ToList();
			foreach (MapUnit busyWorker in busyWorkers)
				if (busyWorker.WorkerJob != null)
					_ = busyWorker.PerformEndOfTurnAction();
		}

		public static void InitTurnData(Player player = null, bool skipTurn = false) {
			GameData gameData = EngineStorage.gameData;
			if (player == null) {
				foreach (MapUnit mapUnit in gameData.mapUnits)
					mapUnit.OnBeginTurn(skipTurn);
			} else {
				foreach (MapUnit mapUnit in gameData.mapUnits.Where(u => u.owner == player))
					mapUnit.OnBeginTurn(skipTurn);
			}
			// What recon missions saw is fogged again.
			foreach (Player p in player == null ? gameData.players : [player]) {
				p.tileKnowledge.EndScouting();
			}
		}

		// Implements the game loop. This method is called when the game is started and when the player signals that they're done moving.
		public static async Task AdvanceTurn() {
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			GameData gameData = EngineStorage.gameData;

			// Loop ends with a function return once we reach the UI controller during the movement phase
			while (true) {
				bool firstTurn = GetTurnNumber() == 0;

				// Movement phase
				if (await PlayPlayerTurns(gameData, firstTurn)) {
					stopwatch.Stop();
					log.Debug("Turn time took " + stopwatch.ElapsedMilliseconds + " milliseconds");
					return;
				}

				// Clear all wait queue, so if a player ended the turn without handling all waited units, they are selected
				// at the same place in the order. Confirmed this is what Civ3 does.
				UnitInteractions.ClearWaitQueue();

				BarbarianInteractions.SpawnBarbarians(gameData);

				gameData.turn++;
				// Ships have moved and tiles have been explored since the trade
				// network was worked out.
				gameData.InvalidateCachedTradeNetwork();
				WarWeariness.UpdateForNewTurn(gameData);
				Attitude.UpdateForNewTurn(gameData);
				Espionage.UpdateForNewTurn(gameData);
				VictoryPoints.UpdateForNewTurn(gameData);
				Volcanoes.UpdateForNewTurn(gameData);
				GlobalWarming.UpdateForNewTurn(gameData);
				Plague.UpdateForNewTurn(gameData);
				foreach (Player player in gameData.players) {
					player.MaybeSpawnBonusUnits(gameData);
					player.DecrementCityUnhappinessPenalties(gameData);
					player.LetGovernorsManageMoods(gameData);
					player.RecalculateCitizenMoods(gameData, goIntoDisorderIfUnhappy: true);
					Pollution.CheckMeltdowns(gameData, player);
					player.UpdateCelebrations(gameData);
					CultureFlip.CheckFlips(gameData, player);
					player.DoCorruptionCalculations(gameData);

					// Finances, growth and production below add to this turn's statistics.
					if (!player.isBarbarians && !player.defeated)
						player.stats.StartTurn(gameData.turn);

					// Do the financial updates before updating the cities, so
					// that a newly produced unit won't put a player over the
					// unit support cap and cause them to lose gold unexpectedly.
					player.DoPerTurnFinanceUpdates(gameData);
					player.DoPerTurnScienceUpdates(gameData);
					player.CollectGreatLibraryTechs(gameData);

					// Note that we do growth after calculating citizen moods,
					// to ensure that the player has a chance to deal with the
					// unhappiness of a new citizen during their turn.
					log.Information($"\n*** City growth/production for turn {gameData.turn}, player {player} ***");
					player.HandleCityUpdates(gameData);
					Pollution.UpdateForNewTurn(gameData, player);
					Disease.UpdateForNewTurn(gameData, player);
					player.CountDownGoldenAge();
					player.CountDownAgeOfScience();

					player.UpdateHistory(gameData);
				}

				// Snapshot the statistics once every player's cities have grown and borders expanded.
				Dictionary<Player, PlayerStats.LandCount> land = PlayerStats.CountLand(gameData.map);
				foreach (Player player in gameData.players) {
					player.stats.EndTurn(player, land.GetValueOrDefault(player), PlayerStats.LastHistory(player, gameData));
				}

				CheckVictory(gameData);

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
			}
		}

		/// <summary>
		/// Plays the turns for all the players in the game (including barbarians).
		/// </summary>
		/// <param name="gameData"></param>
		/// <param name="firstTurn"></param>
		/// <returns>true when it is time for the human to take control again</returns>
		private static async Task<bool> PlayPlayerTurns(GameData gameData, bool firstTurn) {
			// Order players: Humans -> AI -> Barbarian AI. Each human in the
			// game takes their turn in order (hotseat), and the UI follows
			// whoever's turn it is.
			var orderedPlayers = gameData.players.OrderByDescending(p => !p.isBarbarians).ThenByDescending(p => p.isHuman).ToList();
			// With no human left (all defeated, or everyone handed to the AI in
			// observer mode), the UI controller still gets its turn, so control
			// returns to the UI and the loop stops.
			bool anyHumanLeft = gameData.players.Any(p => p.isHuman && !p.defeated);
			foreach (Player player in orderedPlayers) {
				bool takesControl = (player.isHuman && !player.defeated) || (!anyHumanLeft && player.id == EngineStorage.uiControllerID);
				if (player.hasPlayedThisTurn || (player.defeated && !takesControl)) {
					continue;
				}

				if (firstTurn && player.SitsOutFirstTurn()) {
					continue;
				}

				// The United Nations' owner may call an election before its turn
				// (a human playing by email, once at their computer: HearFromOthers).
				if (!player.isBarbarians && !player.defeated && !(gameData.playByEmail && player.isHuman)) {
					await UnitedNations.MaybeCallElection(gameData, player);
				}

				if (player.isBarbarians) {
					await BarbarianAI.PlayTurn(player, gameData);
				} else if (!player.isHuman && !player.defeated) {
					await PlayerAI.PlayTurn(player, gameData);
				}

				// Control passes to this player; their End Turn (MsgEndTurn)
				// finishes their turn and carries on with the next player. The
				// human sees what's going on even in observer mode.
				if (takesControl) {
					// Units the last human told to wait aren't this one's.
					if (player.id != EngineStorage.uiControllerID) {
						UnitInteractions.ClearWaitQueue();
					}
					EngineStorage.uiControllerID = player.id;
					new MsgStartTurn(player.id).send();
					return true;
				}

				OnEndTurn(player);
				player.hasPlayedThisTurn = true;
			}
			return false;
		}

		///Eventually we'll have a game year or month or whatever, but for now this provides feedback on our progression
		public static int GetTurnNumber() {
			return EngineStorage.gameData.turn;
		}

		internal static void CheckVictory(GameData gameData) {
			if (gameData.gameOver)
				return; // Game is already over

			List<Tuple<Player, IVictory>> winners = [];

			foreach (Player player in gameData.players) {
				if (player.isBarbarians || player.defeated)
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
				// The best score wins, or the most victory points in a game
				// that scores them.
				var topScoring = winners.OrderByDescending(pv => {
					var player = pv.Item1;
					if (VictoryPoints.Scoring(gameData)) {
						return player.victoryPoints;
					}
					HistTurnRecord lastTurn = gameData.history[player.id.ToString()].LastOrDefault();
					return lastTurn?.Score ?? 0;
				}).First();
				DeclareVictory(topScoring.Item1, topScoring.Item2, gameData);
			}
		}

		private static void DeclareVictory(Player winner, IVictory victory, GameData gameData) {
			Log.Information("Player {Winner} has wone a {Victory} victory!",
				winner, victory.Header());

			gameData.winner = winner;
			gameData.gameOver = true;

			new MsgVictory(winner, victory).send();
		}
	}
}

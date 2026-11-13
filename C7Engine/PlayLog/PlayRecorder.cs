using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using C7Engine.AI.Advanced;
using C7GameData;
using C7GameData.AIData;
using Serilog;

namespace C7Engine.PlayLog {
	// The play log (Settings, Developer, "Record play log"): a JSON-lines
	// file of a game a human plays, to learn from their play and compare it
	// with the Advanced AI's (see doc/play-log.md). It holds:
	// - a "header" row when the game starts or loads;
	// - every human's state at the start of each of their turns, in the rows
	//   of AiTrialTest's state log (PlayerStateRow, with "ai": "Human"), so
	//   the training tools read human games next to the AI's;
	// - every message a human sends the engine ("action" rows: what they did
	//   and to what), with, at their decisions, what the Advanced AI would
	//   have chosen instead ("shadow", see ShadowDecisions);
	// - every fight a human's unit is in ("combat" rows: the odds and the
	//   result).
	// Only rows other than the state rows have a "type".
	//
	// It records in the engine that plays the game: a single machine's, for
	// every human of a hotseat game, and a LAN host's, for every human,
	// at whichever machine they play. A LAN guest's engine plays nothing, so
	// it records nothing.
	//
	// Recording must not change the game: it reads the game, and what it asks
	// the AI is asked through ShadowAdvanced, which leaves the game's state
	// and random numbers as they were (see PlayRecorderTest).
	public static class PlayRecorder {
		private static readonly ILogger log = Log.ForContext(typeof(PlayRecorder));

		// Set by the UI from the setting before a game starts or loads.
		// C7_PLAY_LOG_DIR records too, into that folder (for headless games).
		public static bool enabled;
		public static string directory;
		// Set by the UI: a game with the setting on keeps its rows until the
		// UI says which file is the game's log (UseLogFile, once the player
		// has named the game, see PlayLogFolders) or that it isn't recorded
		// (Close). Without it, each game writes a new file in `directory`.
		public static bool waitForLogFile;

		// The game being recorded, and its log file (opened with the first row
		// written, so a game nothing is recorded in leaves no file).
		private static GameData game;
		private static string recordingDirectory;
		private static StreamWriter writer;
		public static string CurrentPath { get; private set; }
		// The rows written while waiting for the game's log file.
		private static List<string> pending;
		// Rows are written from the engine's thread, the log file is given
		// from the UI's.
		private static readonly object writeLock = new();

		private static readonly Dictionary<ID, ShadowAdvanced.State> shadowStates = new();
		private static readonly HashSet<(int turn, ID player)> stateRowsWritten = new();

		private static readonly JsonSerializerOptions jsonOptions = new() {
			DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
		};

		// Whether this engine records the game now.
		public static bool Active => game != null && ReferenceEquals(game, EngineStorage.gameData) && EngineStorage.remoteEngine == null;

		// Called when a game has been started or loaded.
		public static void OnGameCreated(GameData gameData) {
			Close();
			string envDirectory = Environment.GetEnvironmentVariable("C7_PLAY_LOG_DIR");
			lock (writeLock) {
				if (!string.IsNullOrEmpty(envDirectory)) {
					recordingDirectory = envDirectory;
				} else if (enabled && waitForLogFile) {
					recordingDirectory = null;
					pending = new List<string>();
				} else if (enabled && !string.IsNullOrEmpty(directory)) {
					recordingDirectory = directory;
				} else {
					return;
				}
				game = gameData;
			}
		}

		// Whether the game is recorded once the UI says where (UseLogFile).
		public static bool WaitingForLogFile {
			get {
				lock (writeLock) {
					return game != null && pending != null;
				}
			}
		}

		// The game's log is the file at path: a new one starts with a
		// "header" row, an existing one (the game's log from before it was
		// saved) carries on after a "session" row. Then come the rows written
		// while waiting.
		public static void UseLogFile(string path, string saveLoaded = null) {
			lock (writeLock) {
				if (game == null || pending == null) {
					return;
				}
				List<string> rows = pending;
				pending = null;
				try {
					Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
					bool carriesOn = File.Exists(path) && new FileInfo(path).Length > 0;
					writer = new StreamWriter(path, append: true) { NewLine = "\n" };
					CurrentPath = path;
					log.Information(carriesOn ? "Carrying on the play log {Path}" : "Recording the play log to {Path}", path);
					writer.WriteLine(JsonSerializer.Serialize(Header(game, carriesOn ? "session" : "header", saveLoaded), jsonOptions));
					foreach (string row in rows) {
						writer.WriteLine(row);
					}
					writer.Flush();
				} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
					log.Warning("Can't write the play log {Path}, so it stops: {Error}", path, e.Message);
					game = null;
				}
			}
		}

		public static void Close() {
			lock (writeLock) {
				try {
					writer?.Dispose();
				} catch (Exception e) when (e is IOException or ObjectDisposedException) {
				}
				writer = null;
				pending = null;
				game = null;
				CurrentPath = null;
				shadowStates.Clear();
				stateRowsWritten.Clear();
			}
		}

		// ---- what is recorded ----

		// The state row of a human whose turn starts (once a turn).
		public static void HumanTurnStarted(Player player, GameData gameData) {
			if (!Active || player == null || !player.isHuman || !stateRowsWritten.Add((gameData.turn, player.id))) {
				return;
			}
			Guard("turn start", () => {
				List<Player> civs = gameData.players.Where(p => !p.IsIndependent).ToList();
				Write(PlayerStateRow.Build(gameData, player, civs, gameData.seed, gameData.seed, "main", gameData.turn, trial: false, ai: "Human"));
			});
		}

		// A message the engine is about to handle, which it has allowed. A
		// row is written for each a human sends; it is written now, or once
		// the message is handled (End) for the decisions whose outcome it notes.
		public sealed class Entry {
			internal MessageToEngine msg;
			internal Player player;
			internal Dictionary<string, object> row;
			internal Action<Dictionary<string, object>> after;
		}

		public static Entry Begin(MessageToEngine msg) {
			if (!Active || msg.IsLocal || Skipped(msg)) {
				return null;
			}
			GameData gameData = EngineStorage.gameData;
			Player player = gameData.GetPlayer(msg.playerID);
			if (player == null || !player.isHuman) {
				return null;
			}
			Entry entry = null;
			Guard(msg.GetType().Name, () => {
				Dictionary<string, object> row = new() {
					["type"] = "action",
					["game"] = gameData.seed,
					["turn"] = gameData.turn,
					["civ"] = player.civilization.name,
					["msg"] = msg.GetType().Name,
				};
				entry = new Entry { msg = msg, player = player, row = row };
				Describe(gameData, player, msg, row);
				entry.after = ShadowDecisions.Shadow(gameData, player, ShadowState(player), msg, row);
				if (entry.after == null) {
					Write(row);
					entry = null;
				}
			});
			return entry;
		}

		// The message has been handled (if it has finished).
		public static void End(Entry entry, bool finished) {
			if (entry == null) {
				return;
			}
			Guard(entry.msg.GetType().Name, () => {
				if (finished && Active) {
					entry.after(entry.row);
				}
				Write(entry.row);
			});
		}

		// A fight a human's unit is in: noted as it starts (the odds) and
		// written with its result.
		public sealed class FightEntry {
			internal Dictionary<string, object> row;
			internal MapUnit attacker, defender;
		}

		public static FightEntry BeginFight(MapUnit attacker, MapUnit defender, double roundOdds) {
			if (!Active || attacker?.owner == null || defender?.owner == null || !(attacker.owner.isHuman || defender.owner.isHuman)) {
				return null;
			}
			FightEntry entry = null;
			Guard("fight", () => {
				GameData gameData = EngineStorage.gameData;
				Player human = attacker.owner.isHuman ? attacker.owner : defender.owner;
				entry = new FightEntry {
					attacker = attacker,
					defender = defender,
					row = new Dictionary<string, object> {
						["type"] = "combat",
						["game"] = gameData.seed,
						["turn"] = gameData.turn,
						["civ"] = human.civilization.name,
						["humanAttacks"] = attacker.owner.isHuman,
						["attacker"] = UnitSide(attacker),
						["defender"] = UnitSide(defender),
						["at"] = TileText(defender.location),
						// The chance of winning each round, and the AI's estimate of
						// the chance of winning the fight (CombatAI).
						["roundOdds"] = Math.Round(roundOdds, 3),
						["winChance"] = Math.Round(AI.UnitAI.CombatAI.EstimateChanceToWin(attacker, defender), 3),
					},
				};
			});
			return entry;
		}

		public static void EndFight(FightEntry entry, CombatResult result) {
			if (entry == null) {
				return;
			}
			Guard("fight", () => {
				entry.row["result"] = result.ToString();
				entry.row["attackerHpAfter"] = entry.attacker.hitPointsRemaining;
				entry.row["defenderHpAfter"] = entry.defender.hitPointsRemaining;
				Write(entry.row);
			});
		}

		// Messages that only say what the UI shows.
		// A password isn't a move, and mustn't be written down.
		private static bool Skipped(MessageToEngine msg) => msg is MsgSelectUnit or MsgAskWhatToResearch or MsgSetPassword;

		private static ShadowAdvanced.State ShadowState(Player player) {
			if (!shadowStates.TryGetValue(player.id, out ShadowAdvanced.State state)) {
				shadowStates[player.id] = state = new ShadowAdvanced.State();
			}
			return state;
		}

		// ---- the message's fields ----

		// The message's own fields, readably, and for some what they act on.
		private static void Describe(GameData gameData, Player player, MessageToEngine msg, Dictionary<string, object> row) {
			foreach (FieldInfo f in msg.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) {
				if (f.Name == nameof(MessageToEngine.playerID)) {
					continue;
				}
				object value = f.GetValue(msg);
				// A unit's ID: the unit.
				if (value is ID id && gameData.GetUnit(id) is MapUnit unit) {
					row[f.Name] = Text(unit);
					continue;
				}
				row[f.Name] = Text(value);
			}
			switch (msg) {
				case MsgMoveUnit m when gameData.GetUnit(m.unitID) is MapUnit u:
					row["from"] = TileText(u.location);
					row["to"] = u.location != null && u.location.neighbors.TryGetValue(m.dir, out Tile to) ? TileText(to) : null;
					break;
				case MsgSetUnitPath m when gameData.GetUnit(m.unitID) is MapUnit u:
					row["from"] = TileText(u.location);
					row["steps"] = m.path?.path?.Count;
					break;
				case MsgStartWorkerJob m when gameData.GetUnit(m.unitID) is MapUnit u:
					row["at"] = TileText(u.location);
					break;
				case MsgBuildCity m when m.unit != null:
					row["at"] = TileText(m.unit.location);
					break;
				case MsgChooseProduction m when gameData.GetCity(m.cityID) is City c:
					row["city"] = c.name;
					row["previous"] = Text(c.itemBeingProduced);
					break;
				case MsgEnqueueProduction m when gameData.GetCity(m.cityID) is City c:
					row["city"] = c.name;
					break;
				case MsgClearProductionQueue m when gameData.GetCity(m.cityID) is City c:
					row["city"] = c.name;
					break;
				case MsgSaveProductionQueue m when gameData.GetCity(m.cityID) is City c:
					row["city"] = c.name;
					break;
				case MsgLoadProductionQueue m when gameData.GetCity(m.cityID) is City c:
					row["city"] = c.name;
					break;
				case MsgChangeSliders:
					row["before"] = Rates(player);
					break;
			}
		}

		internal static Dictionary<string, object> Rates(Player player) => new() {
			["tax"] = player.taxRate,
			["science"] = player.scienceRate,
			["luxury"] = player.luxuryRate,
		};

		private static Dictionary<string, object> UnitSide(MapUnit unit) => new() {
			["civ"] = unit.owner.civilization.name,
			["unit"] = unit.unitType.name,
			["hp"] = unit.hitPointsRemaining,
		};

		internal static string TileText(Tile t) => t == null || t == Tile.NONE ? null : $"{t.XCoordinate},{t.YCoordinate}";

		internal static object Text(object value) {
			switch (value) {
				case null:
					return null;
				case string or bool or int or long or float or double:
					return value;
				case Enum e:
					return e.ToString();
				case Player p:
					return p.civilization?.name;
				case City c:
					return c.name;
				case MapUnit u:
					return u == MapUnit.NONE ? null : $"{u.unitType.name}@{TileText(u.location)}";
				case Tile t:
					return TileText(t);
				case Tech t:
					return t.Name;
				case Government g:
					return g.name;
				case IProducible p:
					return p.name;
				case Terraform t:
					return t.Name;
				case TilePath path:
					return TileText(path.destination);
				default:
					return value.ToString();
			}
		}

		// ---- writing ----

		private static void Write(Dictionary<string, object> row) {
			lock (writeLock) {
				if (!Active) {
					return;
				}
				try {
					if (writer == null && pending != null) {
						pending.Add(JsonSerializer.Serialize(row, jsonOptions));
						return;
					}
					if (writer == null) {
						Open();
					}
					writer.WriteLine(JsonSerializer.Serialize(row, jsonOptions));
					writer.Flush();
				} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
					log.Warning("Can't write the play log {Path}, so it stops: {Error}", CurrentPath, e.Message);
					game = null;
				}
			}
		}

		private static void Open() {
			GameData gameData = game;
			Directory.CreateDirectory(recordingDirectory);
			Player human = gameData.players.FirstOrDefault(p => p.isHuman);
			string civ = new((human?.civilization?.name ?? "game").Where(char.IsLetterOrDigit).ToArray());
			string stem = $"{DateTime.Now:yyyyMMdd-HHmmss}-{civ}-{gameData.seed}";
			string path = Path.Combine(recordingDirectory, stem + ".jsonl");
			for (int i = 2; File.Exists(path); ++i) {
				path = Path.Combine(recordingDirectory, $"{stem}-{i}.jsonl");
			}
			writer = new StreamWriter(path, append: false) { NewLine = "\n" };
			CurrentPath = path;
			log.Information("Recording the play log to {Path}", path);
			writer.WriteLine(JsonSerializer.Serialize(Header(gameData, "header", null), jsonOptions));
		}

		// The row a log starts with ("header"), or that marks a session
		// carrying on a log ("session"): the game as it stands when it
		// starts or loads.
		private static Dictionary<string, object> Header(GameData gameData, string type, string saveLoaded) {
			Dictionary<string, object> row = new() {
			["type"] = type,
			["version"] = 1,
			["game"] = gameData.seed,
			["seed"] = gameData.seed,
			["name"] = gameData.playLog?.name,
			["recorded"] = DateTime.Now.ToString("s"),
			["turn"] = gameData.turn,
			["loaded"] = saveLoaded,
			["difficulty"] = gameData.gameDifficulty?.Name,
			["map"] = $"{gameData.map?.numTilesWide}x{gameData.map?.numTilesTall}",
			["simultaneousTurns"] = gameData.simultaneousTurns,
			["civs"] = gameData.players.Where(p => !p.IsIndependent).Select(p => new Dictionary<string, object> {
				["civ"] = p.civilization.name,
				["ai"] = p.isHuman ? "Human" : p.aiType.ToString(),
			}).ToList(),
			};
			// What isn't known (the name of a game without one, the save of a
			// new game) is left out.
			foreach (string key in row.Where(e => e.Value == null).Select(e => e.Key).ToList()) {
				row.Remove(key);
			}
			return row;
		}

		// Recording never stops the game: a failure is logged and the row
		// left out.
		private static void Guard(string what, Action action) {
			try {
				action();
			} catch (Exception e) {
				log.Warning(e, "The play log couldn't record {What}", what);
			}
		}
	}
}

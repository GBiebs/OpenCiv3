using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace C7GameData {
	// A finished game in the Hall of Fame: who played it, how it ended and
	// the score they had.
	public record HallOfFameEntry {
		// The player's leader and civilization (Civ3's "Name" and
		// "Civilization" columns).
		public string Leader { get; init; } = "";
		public string Civilization { get; init; } = "";
		public string Difficulty { get; init; } = "";
		public int Score { get; init; }
		// How the game ended, in the words of Civ3's Hall of Fame
		// (labels.txt, #HALL_OF_FAME_WIN): "Domination", "Military",
		// "Cultural", "Diplomatic", "Space Race", "None" or "Retired".
		public string VictoryType { get; init; } = "None";
		// Whether the player won, for Civ3's "(Won)" and "(Lost)". False for
		// a game the player retired from.
		public bool Won { get; init; }
		// The civilization that won, if any.
		public string Winner { get; init; } = "";
		// When the game ended: its turn and date in the game, and the day it
		// was played.
		public int Turn { get; init; }
		public string GameDate { get; init; } = "";
		public DateTime RecordedAt { get; init; }

		// The Victory Type column: the type, then "(Won)" or "(Lost)" unless
		// the player retired.
		public string Outcome => VictoryType == HallOfFame.Retired ? VictoryType : $"{VictoryType} {(Won ? "(Won)" : "(Lost)")}";
	}

	// Civ3's Hall of Fame: the games played to the end on this computer,
	// best score first, kept in a file of their own.
	public static class HallOfFame {
		private static readonly ILogger log = Log.ForContext(typeof(HallOfFame));

		public const string FileName = "hall_of_fame.json";
		public const string Retired = "Retired";
		public const string Everyone = "Everyone";

		private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

		// The Hall of Fame's name for a kind of victory. Its list (labels.txt,
		// #HALL_OF_FAME_WIN) has no "Conquest": UNVERIFIED that a conquest
		// victory is its "Military", and that a game won on score at the end
		// of time is "None".
		public static string VictoryTypeName(IVictory victory) => victory?.Header() switch {
			"Conquest" => "Military",
			"Domination" => "Domination",
			"Cultural" => "Cultural",
			"Diplomatic" => "Diplomatic",
			"Space Race" => "Space Race",
			_ => "None",
		};

		// The player's score: the last score in the game's history of them.
		public static int ScoreOf(GameData gameData, Player player) {
			if (player == null || gameData.history == null
				|| !gameData.history.TryGetValue(player.id.ToString(), out List<HistTurnRecord> history)) {
				return 0;
			}
			return history.LastOrDefault()?.Score ?? 0;
		}

		// The entry for a game someone won, for the given (human) player.
		public static HallOfFameEntry ForVictory(GameData gameData, Player player, Player winner, IVictory victory,
				string gameDate, DateTime now) =>
			NewEntry(gameData, player, gameDate, now) with {
				VictoryType = VictoryTypeName(victory),
				// A team wins together (see GameSetup.Teams.cs).
				Won = winner == player || (winner != null && gameData.AreTeammates(player, winner)),
				Winner = winner?.civilization?.noun ?? "",
			};

		// The entry for a game the player lost without anyone winning it, as
		// when every human player was defeated.
		public static HallOfFameEntry ForDefeat(GameData gameData, Player player, string gameDate, DateTime now) =>
			NewEntry(gameData, player, gameDate, now);

		// The entry for a game the player retired from.
		public static HallOfFameEntry ForRetirement(GameData gameData, Player player, string gameDate, DateTime now) =>
			NewEntry(gameData, player, gameDate, now) with { VictoryType = Retired };

		private static HallOfFameEntry NewEntry(GameData gameData, Player player, string gameDate, DateTime now) => new() {
			Leader = player?.civilization?.leader ?? "",
			Civilization = player?.civilization?.noun ?? player?.civilization?.name ?? "",
			Difficulty = gameData.gameDifficulty?.Name ?? "",
			Score = ScoreOf(gameData, player),
			Turn = gameData.turn,
			GameDate = gameDate ?? "",
			RecordedAt = now,
		};

		// The entries in the file, or none if there's no file or it can't be
		// read.
		public static List<HallOfFameEntry> Load(string path) {
			try {
				if (!File.Exists(path)) {
					return [];
				}
				return JsonSerializer.Deserialize<List<HallOfFameEntry>>(File.ReadAllText(path), jsonOptions)?
					.Where(e => e != null).ToList() ?? [];
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) {
				log.Warning(e, "Couldn't read the Hall of Fame at {Path}", path);
				return [];
			}
		}

		// Adds an entry to the file, making it if there's none. Returns
		// whether it was saved. A game is in it once: the same save played
		// to the same end again (as the owner's Cultural win, loaded and won
		// three times, 2026-11-12) is the entry already there.
		public static bool Add(string path, HallOfFameEntry entry) {
			List<HallOfFameEntry> entries = Load(path);
			if (entries.Any(e => IsSameGame(e, entry))) {
				log.Information("The Hall of Fame already has this game");
				return true;
			}
			entries.Add(entry);
			try {
				string directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory)) {
					Directory.CreateDirectory(directory);
				}
				// Written to the side and moved over, so a crash midway
				// doesn't lose the games already there.
				string temp = path + ".tmp";
				File.WriteAllText(temp, JsonSerializer.Serialize(entries, jsonOptions));
				File.Move(temp, path, overwrite: true);
				return true;
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				log.Warning(e, "Couldn't save the Hall of Fame at {Path}", path);
				return false;
			}
		}

		// Whether two entries are the same game ending the same way: everything
		// but when it was recorded is the same.
		public static bool IsSameGame(HallOfFameEntry a, HallOfFameEntry b) =>
			a with { RecordedAt = default } == b with { RecordedAt = default };

		// The leaders with games in the Hall of Fame, for its "List high
		// scores for" choice, after "Everyone".
		public static List<string> Leaders(IEnumerable<HallOfFameEntry> entries) =>
			entries.Select(e => e.Leader).Where(l => l.Length > 0).Distinct().OrderBy(l => l).ToList();

		// The entries ranked best score first (earlier games first among
		// equal scores), only those of the given leader unless it's null or
		// "Everyone".
		public static List<(int rank, HallOfFameEntry entry)> Ranked(IEnumerable<HallOfFameEntry> entries, string leader = null) =>
			entries
				.Where(e => leader == null || leader == Everyone || e.Leader == leader)
				.OrderByDescending(e => e.Score)
				.ThenBy(e => e.RecordedAt)
				.Select((e, i) => (i + 1, e))
				.ToList();
	}
}

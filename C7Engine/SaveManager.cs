namespace C7Engine {
	using System;
	using System.IO;
	using System.Threading.Tasks;
	using C7GameData;
	using C7GameData.Save;

	enum SaveFileFormat {
		Sav,
		Biq,
		C7,
		Invalid,
	}

	// The engine performs all save file creating, reading, and updating
	// via the SaveManager
	public static class SaveManager {
		private static SaveFileFormat getFileFormat(string path) {
			return Path.GetExtension(path).ToUpper() switch {
				".SAV" => SaveFileFormat.Sav,
				".BIQ" or ".BIX" or ".BIC" => SaveFileFormat.Biq,
				".JSON" => SaveFileFormat.C7,
				_ => SaveFileFormat.Invalid,
			};
		}

		// Load and initialize a save
		public static SaveGame LoadSave(string path, string bicPath, Func<string, string> getPediaIconsPath) {
			// The file may be one still being written.
			WaitForPendingSaves();
			SaveGame save = getFileFormat(path) switch {
				SaveFileFormat.Sav => ImportCiv3.ImportSav(path, bicPath, getPediaIconsPath),
				SaveFileFormat.Biq => ImportCiv3.ImportBiq(path, bicPath, getPediaIconsPath),
				SaveFileFormat.C7 => SaveGame.Load(path, getPediaIconsPath),
				_ => throw new FileLoadException($"{Path.GetFileName(path)} is not a save C7 can load (.sav, .biq or .json)"),
			};
			// A scenario remembers which it was, for the Campaign Record.
			if (getFileFormat(path) == SaveFileFormat.Biq && string.IsNullOrEmpty(save.ScenarioName)) {
				save.ScenarioName = Path.GetFileNameWithoutExtension(path);
			}
			return save;
		}

		public static void Save(string path) {
			GameData gameData = EngineStorage.gameData;
			SaveGame save = SaveGame.FromGameData(gameData);
			save.Save(path);
		}

		// Saves the game as it stands, and writes it on a worker thread, which
		// for a large game takes a noticeable while. The task fails if the
		// file can't be written. Saves are written one at a time, in order.
		public static Task SaveInBackground(GameData gameData, string path) {
			// Taken from the game here, with nothing the game goes on to
			// change, so it can be written while the game carries on.
			SaveGame save = SaveGame.FromGameData(gameData);
			Network.SnapshotDetacher.Detach(save);
			lock (savesLock) {
				Task writing = pendingSaves.ContinueWith(_ => save.Save(path), TaskScheduler.Default);
				pendingSaves = writing;
				return writing;
			}
		}

		private static readonly object savesLock = new();
		private static Task pendingSaves = Task.CompletedTask;

		// Waits for the saves being written, as before quitting or loading
		// one of them. Their failures are for whoever saved them to report.
		public static void WaitForPendingSaves() {
			Task saves;
			lock (savesLock) {
				saves = pendingSaves;
			}
			try {
				saves.Wait();
			} catch (AggregateException) {
			}
		}

	}
}

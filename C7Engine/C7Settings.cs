using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IniParser.Model;
using IniParser.Exceptions;
using Serilog;

namespace C7Engine {
	public class C7Settings {
		private const string SETTINGS_FILE_NAME = "C7.ini";
		public static IniData settings;

		public static class LastGame {
			public const string SectionName = nameof(LastGame);
			public const string WorldSize = nameof(WorldSize);
			public const string BarbarianActivity = nameof(BarbarianActivity);
			public const string Landform = nameof(Landform);
			public const string OceanCoverage = nameof(OceanCoverage);
			public const string Climate = nameof(Climate);
			public const string Temperature = nameof(Temperature);
			public const string Age = nameof(Age);
			public const string Civilization = nameof(Civilization);
			public const string Difficulty = nameof(Difficulty);
			public const string Opponents = nameof(Opponents);
			public const string CoreCitiesFreeOfCorruption = nameof(CoreCitiesFreeOfCorruption);
		}

		// Where the game keeps the files it writes, like its settings and log:
		// the current directory when it's writable, as when playing from an
		// extracted zip or the editor, otherwise a per-user folder, as when
		// installed under Program Files.
		private static string writableDirectory;
		public static string WritableDirectory {
			get {
				if (writableDirectory == null) {
					string current = Directory.GetCurrentDirectory();
					writableDirectory = IsWritable(current)
						? current
						: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCiv3");
					try {
						Directory.CreateDirectory(writableDirectory);
					} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
						// Saving will fail and say so; the game still runs.
					}
				}
				return writableDirectory;
			}
		}

		private static bool IsWritable(string directory) {
			try {
				string probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
				File.WriteAllText(probe, "");
				File.Delete(probe);
				return true;
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				return false;
			}
		}

		private static string SettingsPath => Path.Combine(WritableDirectory, SETTINGS_FILE_NAME);

		public static void LoadSettings() {
			// Settings left next to a read-only install are still read, and
			// saved to the per-user folder from then on.
			string path = File.Exists(SettingsPath) ? SettingsPath
				: File.Exists(SETTINGS_FILE_NAME) ? SETTINGS_FILE_NAME
				: null;
			if (path == null) {
				// First run: no settings yet, so start from the defaults.
				settings = new IniData();
				SaveSettings();
				return;
			}

			try {
				settings = Util.GetFileIniDataParser().ReadFile(path);
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException || e is ParsingException { InnerException: IOException or UnauthorizedAccessException }) {
				// The file is there but can't be read (ini-parser wraps I/O
				// errors in a ParsingException). Run on the defaults without
				// overwriting it.
				Log.ForContext<C7Settings>().Warning(e, "Could not read settings from {Path}; using defaults", path);
				settings = new IniData();
			} catch (ParsingException e) {
				// The file is malformed. Keep it as .bak so nothing the player
				// set is silently lost, and start again from the defaults.
				string backup = path + ".bak";
				Log.ForContext<C7Settings>().Warning(e, "Could not parse settings in {Path}; moving it to {Backup} and using defaults", path, backup);
				try {
					File.Move(path, backup, overwrite: true);
				} catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) {
					Log.ForContext<C7Settings>().Warning(moveError, "Could not move {Path} to {Backup}", path, backup);
				}
				settings = new IniData();
				SaveSettings();
			}
		}

		public static void SaveSettings() {
			try {
				Util.GetFileIniDataParser().WriteFile(SettingsPath, settings);
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				// Losing a setting shouldn't stop the game.
				Log.ForContext<C7Settings>().Warning(e, "Could not save settings to {Path}", SettingsPath);
			}
		}

		public static void SetValue(string section, string key, string value) {
			if (settings == null) {
				LoadSettings();
			}
			settings[section][key] = value;
		}

		// Forgets a saved value, so whoever reads it gets their default again.
		public static void RemoveValue(string section, string key) {
			if (settings == null) {
				LoadSettings();
			}
			settings[section]?.RemoveKey(key);
		}

		public static string GetSettingValue(string section, string key) {
			if (settings == null) {
				LoadSettings();
			}
			return settings[section][key];
		}

		public static string GetSettingsValueOrDefault(string section, string key, string defaultValue) {
			if (settings == null) {
				LoadSettings();
			}
			if (settings[section] == null) {
				return defaultValue;
			}
			if (settings[section][key] == null) {
				return defaultValue;
			}
			return settings[section][key];
		}

		public static T GetTypedSettingOrDefault<T>(string section, string key, T defaultValue) where T : struct, Enum {
			string value = GetSettingValue(section, key);
			return Enum.TryParse(value, true, out T result) ? result : defaultValue;
		}

		// Whether to play with the game's own art and rules rather than a Civ3
		// install's: the mode of the game being played (a Civ3 scenario or
		// save plays in Civ3's), else the player's choice.
		public static bool UseStandaloneMode() {
			return sessionStandaloneMode ?? PrefersStandaloneMode();
		}

		// The player's choice (the Settings page): standalone is the default
		// until the player chooses. A game in the other mode doesn't change
		// it (the user, 2026-11-18: "that should be true unless it's changed
		// in a setting").
		public static bool PrefersStandaloneMode() {
			return GetSettingsValueOrDefault("locations", "useStandaloneMode", "true") == "true";
		}

		// The mode of the game being played when it isn't the player's
		// choice; null otherwise.
		private static bool? sessionStandaloneMode;

		// Plays in a mode: the player's choice from now on (remember), or
		// only until SetMode is called again.
		public static void SetMode(bool standalone, bool remember) {
			if (remember) {
				SetValue("locations", "useStandaloneMode", standalone ? "true" : "false");
				SaveSettings();
				sessionStandaloneMode = null;
			} else {
				sessionStandaloneMode = standalone == PrefersStandaloneMode() ? null : standalone;
			}
		}
	}
}

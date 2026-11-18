using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// The scenario editor's files. A scenario is a SaveGame at turn 0 with its
// ScenarioInfo: the rules, and, if it has its own map, the map with its
// players, cities and units. The editor edits it as GameData, which the map
// view draws, and saves it back with ToScenario.
public static class ScenarioFiles {
	// Civ3's editor starts on a 100 by 100 map of ocean.
	public const int DefaultWidth = 100;
	public const int DefaultHeight = 100;
	public const string DefaultTerrain = "ocean";

	// A new scenario: the given rules (a ruleset, as GameMode.GetSave makes,
	// or any save, whose map and players are dropped) on a blank map.
	public static SaveGame NewScenario(SaveGame rules, int width = DefaultWidth, int height = DefaultHeight,
		string terrainKey = DefaultTerrain) {
		SaveGame save = rules.Clone();
		save.Players.Clear();
		save.Cities.Clear();
		save.Units.Clear();
		save.Map = BlankMap(save, width, height, terrainKey);
		save.Scenario = new ScenarioInfo();
		Clean(save);
		return save;
	}

	// A map of width by height of one terrain. Civ3's maps are counted in
	// half tiles across: each row holds width / 2 tiles, every other row
	// shifted by one (see ImportCiv3.GetMapCoordinates), so the width must
	// be even.
	public static SaveMap BlankMap(SaveGame rules, int width, int height, string terrainKey) {
		if (width <= 0 || height <= 0 || width % 2 != 0) {
			throw new ArgumentException($"A map must be at least 1 tile each way and an even number of tiles wide, not {width} by {height}");
		}
		if (!rules.TerrainTypes.Any(t => t.Key == terrainKey)) {
			throw new ArgumentException($"The rules have no terrain \"{terrainKey}\"");
		}
		WorldSize size = NearestWorldSize(rules, width, height);
		SaveMap map = new() {
			tilesWide = width,
			tilesTall = height,
			// Civ3's maps wrap east to west unless told otherwise.
			wrapHorizontally = true,
			wrapVertically = false,
			techRate = size?.techRate ?? 0,
			optimalNumberOfCities = size?.optimalNumberOfCities ?? 0,
		};
		ID.Factory ids = new();
		int perRow = width / 2;
		map.tiles = new List<SaveTile>(perRow * height);
		for (int i = 0; i < perRow * height; ++i) {
			int y = i / perRow;
			int x = i % perRow * 2 + y % 2;
			map.tiles.Add(new SaveTile {
				id = ids.CreateID("tile"),
				X = x,
				Y = y,
				baseTerrain = terrainKey,
				overlayTerrain = terrainKey,
			});
		}
		return map;
	}

	// The rules' world size closest to the map's, for its research rate and
	// optimal number of cities; null if the rules have none.
	private static WorldSize NearestWorldSize(SaveGame rules, int width, int height) {
		return rules.WorldSizes
			.OrderBy(w => Math.Abs(w.width * w.height - width * height))
			.FirstOrDefault();
	}

	// Opens a scenario or a Civ3 scenario file (.biq) to edit, or a saved
	// game (.json, .sav) to start a scenario from.
	public static SaveGame Open(string path, string defaultBiqPath, Func<string, string> getPediaIconsPath) {
		return Open(path, defaultBiqPath, getPediaIconsPath, out _);
	}

	// Also tells whether the file was a scenario made in the editor, which
	// is saved back to the same file, or something to start a new one from.
	public static SaveGame Open(string path, string defaultBiqPath, Func<string, string> getPediaIconsPath, out bool isScenario) {
		SaveGame save = SaveManager.LoadSave(path, defaultBiqPath, getPediaIconsPath);
		isScenario = save.Scenario != null;
		return ForEditing(save, Path.GetFileNameWithoutExtension(path));
	}

	// Readies a save for the editor: one without a map (a rules mod, such
	// as many Civ3 scenarios) gets a blank one to show, which isn't saved
	// unless the scenario is given a map of its own; one opened from
	// anything but a scenario gets its ScenarioInfo.
	public static SaveGame ForEditing(SaveGame save, string name = null) {
		if (save.Scenario == null) {
			save.Scenario = new ScenarioInfo {
				title = name ?? save.ScenarioName ?? "",
				customMap = save.Map.tiles.Count > 0,
				customPlayerData = save.Players.Count > 0,
			};
			FromRules(save.Scenario, save.Rules);
			if (save.VictoryConditions is C7GameData.VictoryConditions vc) {
				save.Scenario.autoPlaceCaptureUnits = vc.AutoPlaceCaptureUnits;
				save.Scenario.anyoneCapturesAnyFlag = vc.AnyoneCapturesAnyFlag;
				save.Scenario.respawnFlagUnits = vc.RespawnFlagUnits;
			}
		}
		if (save.Map.tiles.Count == 0) {
			save.Map = BlankMap(save, DefaultWidth, DefaultHeight, DefaultTerrain);
		}
		Clean(save);
		return save;
	}

	// The Scenario tab's switches the game keeps in its rules (a Civ3
	// file's GAME section, imported).
	private static void FromRules(ScenarioInfo info, Rules rules) {
		if (rules == null) {
			return;
		}
		info.civSpecificAbilities = rules.CivSpecificAbilities;
		info.culturallyLinkedStart = rules.CulturallyLinkedStart;
		info.respawnAiPlayers = rules.RespawnAiPlayers;
		info.preserveRandomSeed = rules.PreserveRandomSeed;
		info.retainCulture = rules.RetainCultureOnCapture;
		info.timeLimitMinutes = rules.TimeLimitMinutes;
	}

	// The scenario being edited, as a save to write to its file.
	public static SaveGame ToScenario(GameData gameData, ScenarioInfo info) {
		SaveGame save = SaveGame.FromGameData(gameData);
		save.Scenario = info;
		Clean(save);
		if (!info.customMap) {
			// A rules mod: the game makes the map and the players.
			save.Map = new SaveMap();
			save.Players.Clear();
			save.Cities.Clear();
			save.Units.Clear();
		}
		return save;
	}

	public static void Save(SaveGame scenario, string path) {
		scenario.Save(path);
	}

	// A scenario starts a game, so it carries nothing a game played so far
	// would: no history, no replay, no record of how it went, no plans the
	// computer players made, and fresh random numbers. Nor a difficulty: as
	// in Civ3, the player chooses one when starting it.
	private static void Clean(SaveGame save) {
		save.GameDifficulty = new Difficulty();
		save.GameDifficultyIndex = null;
		save.TurnNumber = 0;
		save.RngState = null;
		save.PlayedSeconds = 0;
		save.History = new();
		save.Replay = new();
		save.PlayLog = null;
		save.HostFacts = null;
		save.Tutorial = null;
		save.GameOver = false;
		save.Winner = null;
		save.VictoryBonus = 0;
		save.NuclearEvents = 0;
		foreach (SavePlayer player in save.Players) {
			player.ai = null;
		}
	}
}

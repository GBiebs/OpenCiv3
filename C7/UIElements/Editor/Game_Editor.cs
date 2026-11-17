using System;
using System.IO;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using Godot;

// The game scene as the scenario editor. The scenario is the game's data,
// so the map view, its art and the minimap draw it as they draw a game;
// nobody plays it, no turn starts, and the editor (ScenarioEditor) takes
// the place of the game's controls. The game scene opens as the editor when
// ScenarioEditorSession has a scenario waiting (see InitializeGame).
public partial class Game {
	// Whether this game scene is the scenario editor.
	private bool editing = false;
	private ScenarioEditor editor;

	// What the scenario says about itself, and the file it's saved in (null
	// until it's saved).
	internal ScenarioInfo EditorInfo { get; private set; }
	internal string EditorPath { get; private set; }
	// Whether the scenario came with changes not saved yet (reopened with a
	// new map).
	internal bool EditorStartsModified { get; private set; }

	internal MapView EditorMapView => mapView;

	private void InitializeEditor() {
		(SaveGame scenario, string path) = ScenarioEditorSession.Take();
		EditorStartsModified = ScenarioEditorSession.TakeModified();
		editing = true;
		EditorInfo = scenario.Scenario ?? new ScenarioInfo();
		EditorPath = path;

		// As for a game, the scenario's art folders. But it's shown in the
		// mode the game is in (with or without Civ3), not the one it was
		// made in: switching modes, as loading a game does, would change the
		// player's setting. The scenario keeps its own mode when saved.
		Util.setModPath(scenario.ScenarioSearchPath);
		Util.ClearCaches();
		ScenarioEditing.Open(scenario, Global.GameMode.behaviors);
		controller = null;

		InitializeMapView();
	}

	private void StartEditor() {
		// The game's own controls have nothing to do here, nor anything to
		// keep up to date: they follow the player at the screen, and there's
		// none.
		foreach (string path in new[] { "CanvasLayer/Control/UnitButtons", "CanvasLayer/Control/ToolBar", "CanvasLayer/Control/GameStatus" }) {
			if (GetNodeOrNull<CanvasItem>(path) is CanvasItem item) {
				item.Hide();
				item.ProcessMode = ProcessModeEnum.Disabled;
			}
		}
		editor = new ScenarioEditor(this);
		GetNode<CanvasLayer>("CanvasLayer").AddChild(editor);
		CenterEditorCamera();
		log.Information("Scenario editor: {Title} ({Path})", EditorInfo.title, EditorPath ?? "not saved");
	}

	private void CenterEditorCamera() {
		GameMap map = EngineStorage.gameData.map;
		Tile middle = map.tileAt(map.numTilesWide / 2 / 2 * 2, map.numTilesTall / 2 / 2 * 2);
		if (Tile.IsTileValid(middle)) {
			mapView.centerCameraOnTile(middle);
		}
	}

	// The tile under a point of the screen, or null.
	internal Tile EditorTileAt(Vector2 position) {
		Tile tile = PositionToTile(position);
		return Tile.IsTileValid(tile) ? tile : null;
	}

	// The map's input while editing, which the editor gets before the game's
	// own handling would (see _UnhandledInput).
	private void EditorUnhandledInput(InputEvent @event) {
		editor?.HandleMapInput(@event);
	}

	// Saves the scenario to the file. Returns why it couldn't, or null.
	internal string SaveScenario(string path) {
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			SaveGame scenario = null;
			EngineStorage.ReadGameData(gameData => scenario = ScenarioFiles.ToScenario(gameData, EditorInfo));
			ScenarioFiles.Save(scenario, path);
			EditorPath = path;
			log.Information("Saved the scenario to {Path}", path);
			return null;
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) {
			log.Warning(e, "Couldn't save the scenario to {Path}", path);
			return e.Message;
		}
	}

	// Opens another scenario (or a Civ3 scenario, or a saved game to start
	// one from) in a new editor. Returns why it couldn't, or null.
	internal string OpenScenario(string path) {
		SaveGame scenario;
		bool isScenario;
		try {
			scenario = ScenarioEditorSession.OpenFile(path, out isScenario);
		} catch (Exception e) {
			log.Warning(e, "Couldn't open {Path} in the scenario editor", path);
			return e.Message;
		}
		// A Civ3 scenario or a saved game is saved as a new scenario.
		ScenarioEditorSession.Open(scenario, isScenario ? path : null);
		GetTree().ChangeSceneToFile("res://C7Game.tscn");
		return null;
	}

	// A new scenario on the current rules, in a new editor.
	internal void NewScenario() {
		ScenarioEditorSession.Open(ScenarioFiles.NewScenario(Global.GameMode.GetSave()), null);
		GetTree().ChangeSceneToFile("res://C7Game.tscn");
	}

	// The scenario as it is now, as it would be saved.
	internal SaveGame CurrentScenario() {
		SaveGame scenario = null;
		EngineStorage.ReadGameData(gameData => scenario = ScenarioFiles.ToScenario(gameData, EditorInfo));
		return scenario;
	}

	// Opens the scenario again in a new editor, with its changes not saved
	// yet (after Generate Map, Clear Map or a change to the map's
	// switches, which replace the map).
	internal void ReopenScenario(SaveGame scenario) {
		ScenarioEditorSession.Open(ScenarioFiles.ForEditing(scenario), EditorPath, modified: true);
		GetTree().ChangeSceneToFile("res://C7Game.tscn");
	}

	internal void ExitEditor() {
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}
}

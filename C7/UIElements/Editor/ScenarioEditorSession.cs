using System.IO;
using Godot;
using C7Engine;
using C7GameData.Save;

// Hands the scenario to edit from the main menu (or from the editor itself,
// opening another) to the game scene, which opens the scenario editor
// instead of a game while one is waiting (see Game_Editor.cs).
public static class ScenarioEditorSession {
	private static SaveGame pending;
	private static string pendingPath;
	private static bool pendingModified;

	// The active player to choose when the editor opens again (by name: the
	// players are made anew), or null.
	public static string ActivePlayer { get; set; }

	// Whether the next game scene is the scenario editor.
	public static bool Opening => pending != null;

	// Where scenarios made in the editor are kept, beside the saves.
	public static string ScenariosDirectory => Path.Combine(C7Settings.WritableDirectory, "Scenarios");

	// The scenario for the next game scene to edit, and the file it came
	// from (null for a new one, and for a Civ3 scenario, which is saved as
	// a new file).
	// modified: it has changes not yet saved to the file (a scenario
	// reopened with a new map).
	public static void Open(SaveGame scenario, string path, bool modified = false) {
		pending = scenario;
		pendingPath = path;
		pendingModified = modified;
	}

	// Opens a scenario, a Civ3 scenario or a saved game to edit, finding a
	// Civ3 scenario's own PediaIcons file in its art folders as a game does
	// (see Game.CreateGameParams).
	public static SaveGame OpenFile(string path, out bool isScenario) {
		return C7Engine.Editor.ScenarioFiles.Open(path, GamePaths.DefaultBicPath, PediaIconsPath, out isScenario);
	}

	// The scenario's own PediaIcons file if its art folders have one, else
	// Civ3's, else the game's own (standalone mode's Civilopedia). Playing
	// without Civ3, the game looks only in the scenario's folders and its own
	// art, which has no PediaIcons file: the game itself gets round that by
	// switching to Civ3's mode to load a Civ3 scenario, which the editor
	// doesn't do (see Game_Editor.InitializeEditor).
	private static string PediaIconsPath(string searchPath) => Util.PediaIconsPathFor(searchPath);

	public static (SaveGame scenario, string path) Take() {
		(SaveGame, string) taken = (pending, pendingPath);
		pending = null;
		pendingPath = null;
		return taken;
	}

	// Whether the scenario just taken has unsaved changes (read once).
	public static bool TakeModified() {
		bool modified = pendingModified;
		pendingModified = false;
		return modified;
	}
}

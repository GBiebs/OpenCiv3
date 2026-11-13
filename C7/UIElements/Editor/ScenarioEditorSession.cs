using System.IO;
using C7Engine;
using C7GameData.Save;

// Hands the scenario to edit from the main menu (or from the editor itself,
// opening another) to the game scene, which opens the scenario editor
// instead of a game while one is waiting (see Game_Editor.cs).
public static class ScenarioEditorSession {
	private static SaveGame pending;
	private static string pendingPath;

	// Whether the next game scene is the scenario editor.
	public static bool Opening => pending != null;

	// Where scenarios made in the editor are kept, beside the saves.
	public static string ScenariosDirectory => Path.Combine(C7Settings.WritableDirectory, "Scenarios");

	// The scenario for the next game scene to edit, and the file it came
	// from (null for a new one, and for a Civ3 scenario, which is saved as
	// a new file).
	public static void Open(SaveGame scenario, string path) {
		pending = scenario;
		pendingPath = path;
	}

	// Opens a scenario, a Civ3 scenario or a saved game to edit, finding a
	// Civ3 scenario's own PediaIcons file in its art folders as a game does
	// (see Game.CreateGameParams).
	public static SaveGame OpenFile(string path, out bool isScenario) {
		return C7Engine.Editor.ScenarioFiles.Open(path, GamePaths.DefaultBicPath, searchPath => {
			Util.setModPath(searchPath);
			return Util.Civ3MediaPath("Text/PediaIcons.txt");
		}, out isScenario);
	}

	public static (SaveGame scenario, string path) Take() {
		(SaveGame, string) taken = (pending, pendingPath);
		pending = null;
		pendingPath = null;
		return taken;
	}
}

using System.IO;
using C7GameData.Save;
using Godot;

// Playing a scenario that has only rules (a rules mod: one made in the
// editor without a map of its own, or a Civ3 one such as the QC mods). Load
// Scenario has no players to offer for it, so its game is set up as a new
// one is, on the world and player setup screens, under its rules, as Civ3
// does. The rules wait here from Load Scenario until the world setup screen
// takes them (see WorldSetup.LoadRules), and are let go on the main menu.
public static class ScenarioPlay {
	// The rules of the scenario to set a new game up on, or null for the
	// game's own.
	public static SaveGame RulesForNewGame { get; private set; }

	// The scenario's name, for the world setup screen.
	public static string Title { get; private set; }

	// Goes to the new game screens if the scenario has no players to start
	// with. Returns whether it did.
	public static bool SetUpAsNewGame(SaveGame scenario, string path, SceneTree tree) {
		if (scenario.Players.Count > 0) {
			return false;
		}
		RulesForNewGame = scenario;
		Title = scenario.Scenario?.title is { Length: > 0 } title ? title
			: !string.IsNullOrWhiteSpace(scenario.ScenarioName) ? scenario.ScenarioName
			: Path.GetFileNameWithoutExtension(path);
		// Not at once: called while a scene is being made ready, changing
		// scenes then hangs the game.
		Callable.From(() => tree.ChangeSceneToFile("res://UIElements/NewGame/world_setup.tscn")).CallDeferred();
		return true;
	}

	// For a scenario with its own map but no players (one painted in the
	// editor), how many civs its starting locations have room for; null
	// for any other game. Civ3 sets a custom map's number of players in
	// Scenario Properties, which the editor hasn't got yet.
	public static int? StartsOnMap(SaveGame save) {
		return save?.Map?.tiles.Count > 0 && save.Players.Count == 0 ? save.Map.startingLocations.Count : null;
	}

	public static void Clear() {
		RulesForNewGame = null;
		Title = null;
	}
}

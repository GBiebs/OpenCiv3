using System.Collections.Generic;
using C7GameData.Save;

namespace C7Engine.Editor;

// What a scenario made in the scenario editor says about itself, beyond the
// game it sets up: Civ3's editor keeps the same in a scenario file (its
// Scenario Properties, and the Scenario menu's Custom Map, Custom Rules and
// Custom Player Data switches). Saved in the scenario as SaveGame.Scenario;
// null in an ordinary saved game.
public class ScenarioInfo {
	public string title = "";
	public string description = "";

	// Whether the scenario carries its own map. Without one it's a rules
	// mod: the game makes a map when it starts, as for a new game, and the
	// map the editor shows isn't saved.
	public bool customMap = true;

	// Whether the scenario carries its own rules. Always so for now: the
	// game reads a scenario's rules from the scenario itself.
	public bool customRules = true;

	// Whether the scenario sets up its players itself (Civ3's Player
	// Properties).
	public bool customPlayerData = false;

	// The cities whose name is Civ3's "Auto": the game names them from their
	// owner's list when the scenario starts (ScenarioStart). By city ID.
	public List<string> autoNamedCities = [];

	// Civ3's player starting locations: a player's own start, where it gets
	// its start units when the scenario starts (ScenarioStart). The map's
	// starting locations are Civ3's "Any" ones.
	public List<PlayerStart> playerStarts = [];

	// Civ3's Fog of War tool: the tiles every player knows when the game
	// starts (besides what their cities and units see).
	public List<TileLocation> revealedTiles = [];
}

public sealed class PlayerStart {
	// The player's ID.
	public string player;
	public int x, y;
}

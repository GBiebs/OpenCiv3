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

	// Civ3's Scenario Properties, Scenario tab, beyond what the game data
	// keeps (victory conditions, rules, calendar, barbarians):
	// the number of players the scenario is made for (0: not set), and the
	// civilizations a player may pick (empty: all).
	public int numberOfPlayers;
	public List<string> playableCivs = [];
	// The whole map known to every player when the game starts
	// (ScenarioStart).
	public bool revealMap;
	// Kept for scenarios but not used by the game yet: Preserve Random
	// Seed, Civ-Specific Abilities, Culturally-Linked Start, Respawn AI
	// Players, Auto Place Capture Units, Retain Culture on Capture, the
	// time limit in minutes, Conquests' campaign number.
	public bool preserveRandomSeed, civSpecificAbilities = true, culturallyLinkedStart, respawnAiPlayers, autoPlaceCaptureUnits = true, retainCulture;
	public int timeLimitMinutes;
	public int campaign;
	// Victory Point Limits' capture-the-flag options, kept but not used by
	// the game yet: anyone may capture any flag; the flag unit comes back
	// when captured.
	public bool anyoneCapturesAnyFlag, respawnFlagUnits;
}

public sealed class PlayerStart {
	// The player's ID.
	public string player;
	public int x, y;
}

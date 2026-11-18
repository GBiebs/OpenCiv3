using System.Collections.Generic;
using System.Linq;
using C7GameData;
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
	// For a scenario without its own map, the world the new game's setup
	// screen starts on or is limited to (ours, see ScenarioWorldSetup).
	public ScenarioWorldSetup world = new();
	// The whole map known to every player when the game starts
	// (ScenarioStart).
	public bool revealMap;
	// The Scenario tab's switches, which the game keeps in its rules
	// (Rules.CivSpecificAbilities and the rest) and victory conditions
	// (Auto Place Capture Units): copied there by ScenarioSettings and
	// ScenarioStart. Conquests' campaign number is kept only (Civ3 itself
	// does nothing with it).
	public bool preserveRandomSeed = true, civSpecificAbilities = true, culturallyLinkedStart, respawnAiPlayers, autoPlaceCaptureUnits = true, retainCulture;
	public int timeLimitMinutes;
	public int campaign;
	// Victory Point Limits' capture-the-flag options (the game's
	// VictoryConditions; see CaptureTheFlag): anyone may capture any flag;
	// the flag unit comes back when captured.
	public bool anyoneCapturesAnyFlag, respawnFlagUnits = true;

	// Civ3's Player Properties beyond what the players keep themselves
	// (government, era, treasury, advances, colour, Skip 1st Turn): one
	// for each player that has any, by player ID. A player without one is
	// its civilization, with the scenario's own data.
	public List<PlayerSlot> players = [];
}

// What a player's civilization is: the one it has, or Civ3's "Any" (the
// human picks one at setup, or a random one for a computer) or "Random"
// (fixed at setup).
public enum CivChoice { Fixed, Any, Random }

public sealed class PlayerSlot {
	public string player;
	public CivChoice civ = CivChoice.Fixed;
	// Civ3's "Civilization Defaults": the leader name, gender, team colour
	// and free advances are the civilization's (set again at the start, as
	// an Any or Random player's civilization is known only then).
	public bool civDefaults;
	// Overrides of the civilization's leader name and gender, or null.
	public string leaderName;
	public Gender? leaderGender;
	// The difficulty the player plays (null: Civ3's "Any"). The human's
	// is the only one the game honours: the game has one difficulty.
	public string difficulty;
	// Civ3's Starting Units, given at the player's own starting location
	// instead of the rules' two (empty: the rules').
	public List<StartUnit> startUnits = [];
	// Civ3's Start with Embassies: an embassy with every other player that
	// has it (so they've met).
	public bool startWithEmbassies;

	public PlayerSlot Copy() => new() {
		player = player,
		civ = civ,
		civDefaults = civDefaults,
		leaderName = leaderName,
		leaderGender = leaderGender,
		difficulty = difficulty,
		startUnits = startUnits.Select(u => new StartUnit { type = u.type, count = u.count }).ToList(),
		startWithEmbassies = startWithEmbassies,
	};
}

public sealed class StartUnit {
	public string type;
	public int count = 1;
}

public sealed class PlayerStart {
	// The player's ID.
	public string player;
	public int x, y;
}

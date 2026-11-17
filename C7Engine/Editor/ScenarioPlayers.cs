using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// The scenario's players, as Civ3's editor has them: each a civilization,
// with the barbarians as player 0. Everything placed on the map (cities,
// units, starting locations) belongs to the player chosen with Set Active
// Player. A player's own settings (government, treasury, advances...) are
// Player Properties' (Phase 4).
public static class ScenarioPlayers {
	// The civilizations a player can still be added for: those of the rules
	// no player has yet (barbarians aside).
	public static List<Civilization> Available(SaveGame scenario) {
		HashSet<string> taken = scenario.Players.Select(p => p.civilization).ToHashSet();
		return scenario.Civilizations.Where(c => !c.isBarbarian && !taken.Contains(c.name)).ToList();
	}

	// Adds a player for the civilization, set up as a new game sets one up
	// (its starting advances, treasury, government and colours) but with no
	// units: the scenario places its own. The barbarians come with the first
	// player. The scenario now carries its players (Custom Player Data).
	// Returns the new player.
	public static SavePlayer Add(SaveGame scenario, Civilization civ, ScenarioInfo info = null) {
		HashSet<string> used = scenario.Players.Where(p => p.id != null).Select(p => p.id.ToString()).ToHashSet();
		int next = 1;
		ID NextId() {
			while (used.Contains($"Player-{next}")) {
				++next;
			}
			used.Add($"Player-{next}");
			return ID.FromString($"Player-{next}");
		}
		// By civilization: a loaded save doesn't keep SavePlayer.isBarbarian.
		HashSet<string> barbarianCivs = scenario.Civilizations.Where(c => c.isBarbarian).Select(c => c.name).ToHashSet();
		if (!scenario.Players.Any(p => p.isBarbarian || barbarianCivs.Contains(p.civilization))
				&& scenario.Civilizations.Find(c => c.isBarbarian) is Civilization barbarians) {
			scenario.Players.Insert(0, NewPlayer(scenario, barbarians, NextId()));
		}
		SavePlayer player = NewPlayer(scenario, civ, NextId());
		scenario.Players.Add(player);
		if (info != null) {
			info.customPlayerData = true;
		}
		return player;
	}

	private static SavePlayer NewPlayer(SaveGame scenario, Civilization civ, ID id) {
		ID government = scenario.Rules.OnlyGovernment is string only && scenario.Governments.Find(g => g.name == only) is Government named
			? named.id
			: scenario.Governments.Find(g => g.defaultType)?.id;
		return new SavePlayer {
			isBarbarian = civ.isBarbarian,
			isCityStates = civ.isCityStates,
			human = false,
			id = id,
			primaryColorIndex = civ.primaryColorIndex,
			secondaryColorIndex = civ.secondaryColorIndex,
			civilization = civ.name,
			knownTechs = new HashSet<ID>(civ.startingTechs),
			eraCivilopediaName = scenario.Rules.FirstEraCivilopediaName,
			gold = scenario.Rules.StartingTreasury,
			governmentId = government,
			canBePicked = !civ.isBarbarian && !civ.IsIndependent,
		};
	}

	// Player Properties' Civilization: the player becomes the civilization,
	// its units and citizens of the old one too (and back, to undo).
	internal static void ChangeCivilization(GameData gameData, Player player, Civilization civ) {
		Civilization old = player.civilization;
		if (old == civ) {
			return;
		}
		player.civilization = civ;
		foreach (MapUnit unit in gameData.mapUnits.Where(u => u.owner == player && u.nationality == old)) {
			unit.nationality = civ;
		}
		foreach (City city in gameData.cities.Where(c => c.owner == player)) {
			foreach (CityResident resident in city.residents.Where(r => r.nationality == old)) {
				resident.nationality = civ;
			}
		}
	}

	// What the editor calls a player: its civilization's name.
	public static string NameOf(Player player) {
		if (player == null) {
			return "Nobody";
		}
		return player.isBarbarians ? "Barbarians" : player.civilization?.name ?? player.id.ToString();
	}
}

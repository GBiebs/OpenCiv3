using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// Civ3's File > Import: "Imports BIC information from another file". Its
// help says no more; we offer the parts a scenario's own switches name
// (Custom Rules, Custom Map, Custom Player Data):
// - Rules: every rules page as the other file has them, as Restore Defaults
//   does with the standard rules (what named something gone is cleared:
//   units of a type the rules haven't got leave the map, and so on);
// - Map: the other file's map; without its player data the scenario's
//   cities and units go (as Generate Map);
// - Player Data (with the map): its players, cities, units and Player
//   Properties.
public static class ScenarioImport {
	// The scenario with the parts imported, or null and why not.
	public static SaveGame Import(SaveGame target, SaveGame source, bool rules, bool map, bool playerData, out string problem) {
		problem = null;
		if (!rules && !map && !playerData) {
			problem = "Choose something to import.";
			return null;
		}
		if (playerData && !map) {
			problem = "Player data comes with its map: import the map too.";
			return null;
		}
		if (map && source.Map.tiles.Count == 0) {
			problem = "The file has no map.";
			return null;
		}
		SaveGame result = target.Clone();
		SaveGame from = source.Clone();
		if (rules) {
			RuleEditing.RestoreDefaults(result, from);
		}
		if (map) {
			result.Map = from.Map;
			result.Seed = from.Seed;
			if (playerData) {
				result.Players = from.Players;
				result.Cities = from.Cities;
				result.Units = from.Units;
				result.Alliances = from.Alliances;
				if (result.Scenario != null) {
					result.Scenario.players = from.Scenario?.players ?? [];
				}
			} else {
				result.Cities.Clear();
				result.Units.Clear();
			}
			if (Mismatch(result) is string why) {
				problem = why;
				return null;
			}
			FitToRules(result);
		}
		return result;
	}

	// Why the map and players can't go with the rules, or null.
	private static string Mismatch(SaveGame save) {
		HashSet<string> civs = save.Civilizations.Select(c => c.name).ToHashSet();
		foreach (SavePlayer player in save.Players.Where(p => !p.isBarbarian)) {
			if (player.civilization != null && !civs.Contains(player.civilization)) {
				return $"The players include the {player.civilization}, a civilization these rules haven't got: import the rules too.";
			}
		}
		HashSet<string> terrains = save.TerrainTypes.Select(t => t.Key).ToHashSet();
		if (save.Map.tiles.FirstOrDefault(t => !terrains.Contains(t.baseTerrain) || !terrains.Contains(t.overlayTerrain)) is SaveTile odd) {
			return $"The map has terrain ({odd.overlayTerrain}) these rules haven't got.";
		}
		return null;
	}

	// What the rules haven't got leaves the map: resources, units of types
	// the rules lack, buildings in cities.
	private static void FitToRules(SaveGame save) {
		HashSet<string> resources = save.Resources.Select(r => r.Key).ToHashSet();
		foreach (SaveTile tile in save.Map.tiles.Where(t => !string.IsNullOrEmpty(t.resource) && !resources.Contains(t.resource))) {
			tile.resource = null;
		}
		HashSet<string> units = save.UnitPrototypes.Select(u => u.name).ToHashSet();
		save.Units.RemoveAll(u => !units.Contains(u.prototype));
		HashSet<string> buildings = save.Buildings.Select(b => b.name).ToHashSet();
		foreach (SaveCity city in save.Cities) {
			city.buildings?.RemoveAll(b => !buildings.Contains(b.building));
		}
	}
}

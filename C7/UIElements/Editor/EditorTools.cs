using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// The scenario editor's look, while one is being chosen (the plan's decision
// 4): three to compare in the program, switched from View > Look. Once one is
// chosen, the others go.
public enum EditorLook {
	// The game's own: buttons over the map where the game has its own, and
	// dialogs framed in stone and parchment like its setup screens.
	Game,
	// Civ3's editor's layout (a menu bar and a toolbar across the top,
	// tabbed dialogs) drawn in the game's parchment.
	Civ3Editor,
	// A desktop tool's: Godot's own dark controls.
	Plain,
}

// What the scenario editor paints with: Civ3's editor's brushes (its
// toolbar and Map menu), their keys, and the game art each is shown with,
// which is there with Civ3 and without.
public enum EditorTool { Terrain, Resources, Overlays, Cities, Units, FogOfWar }

public static class EditorTools {
	public record Info(EditorTool Tool, string Name, string Tooltip, Key Hotkey);

	public static readonly Info[] All = [
		new(EditorTool.Terrain, "Terrain", "Paint terrain (T)", Key.T),
		new(EditorTool.Resources, "Resources", "Place natural resources (R)", Key.R),
		new(EditorTool.Overlays, "Overlays", "Rivers, roads, irrigation, mines, huts, camps, starting locations... (O)", Key.O),
		new(EditorTool.Cities, "Cities", "Place cities", Key.None),
		new(EditorTool.Units, "Units", "Place units", Key.None),
		new(EditorTool.FogOfWar, "Fog of War", "What each player has seen when the game starts", Key.None),
	];

	// Civ3's brush sizes: a square this many tiles across.
	public static readonly int[] Sizes = [1, 3, 5, 7];

	public static Info Of(EditorTool tool) => All.First(t => t.Tool == tool);

	// The game's button for a worker's job, or a command, that goes with the
	// tool (a tree for terrain, a road for overlays...).
	public static string ButtonKey(EditorTool tool) {
		string action = tool switch {
			EditorTool.Terrain => "unit_plant_forest",
			EditorTool.Resources => "unit_irrigate",
			EditorTool.Overlays => "unit_build_road",
			EditorTool.Cities => "unit_build_city",
			EditorTool.Units => "unit_fortify",
			_ => "unit_explore",
		};
		return "ui.unit_control." + action;
	}

	// The tool's picture: its button's, or for resources a resource's own
	// icon.
	public static Texture2D Icon(EditorTool tool) {
		if (tool == EditorTool.Resources) {
			C7GameData.Resource resource = EngineStorage.gameData?.Resources
				.FirstOrDefault(r => r.Category == ResourceCategory.BONUS && r.Name is "Wheat" or "Cattle")
				?? EngineStorage.gameData?.Resources.FirstOrDefault(r => r.Category != ResourceCategory.NONE);
			if (resource != null) {
				try {
					return TextureLoader.Load("resources.icon", resource, useCache: true);
				} catch (Exception) {
					// The button's picture below.
				}
			}
		}
		return TextureLoader.TryLoad(ButtonKey(tool) + ".normal");
	}
}

// What's on a tile, as Civ3's Tile Properties dialog lists it.
public static class TileFacts {
	public record Flag(string Name, bool On);

	// In the dialog's two columns, row by row, as Civ3's.
	public static List<Flag> Flags(Tile tile, GameMap map) {
		bool has(string key) => tile.overlays.GetImprovements().Any(i => i.key == key);
		return [
			new("Irrigation", tile.HasIrrigation()), new("Mine", has(Tile.TileOverlays.MINE)),
			new("Road", tile.HasRoad()), new("Railroad", tile.HasRailroad()),
			new("Fortress", has(Tile.TileOverlays.FORTRESS)), new("Pollution", tile.HasPollution()),
			new("Goody Hut", tile.hasGoodyHut), new("Barbarian Camp", tile.hasBarbarianCamp),
			new("Starting Location", map.startingLocations.Contains(tile)), new("Colony", has(Tile.TileOverlays.COLONY)),
			new("Airfield", has(Tile.TileOverlays.AIRFIELD)), new("Radar Tower", has(Tile.TileOverlays.RADAR_TOWER)),
			new("Outpost", has(Tile.TileOverlays.OUTPOST)), new("Victory Location", tile.isVictoryLocation),
			new("Ruins", tile.HasRuins()),
		];
	}

	public static string Terrain(Tile tile) {
		string terrain = tile.overlayTerrainType?.DisplayName ?? "";
		if (tile.baseTerrainType != null && tile.baseTerrainType != tile.overlayTerrainType) {
			terrain += $" on {tile.baseTerrainType.DisplayName}";
		}
		return terrain;
	}

	public static string Owner(Tile tile) => tile.OwningPlayer()?.civilization?.name ?? "Nobody";

	// "None" and the rules' resources, and which of them the tile has.
	public static (List<string> names, int selected) Resources(Tile tile, GameData gameData) {
		List<C7GameData.Resource> resources = gameData.Resources.Where(r => r.Category != ResourceCategory.NONE).ToList();
		List<string> names = ["None", .. resources.Select(r => r.Name)];
		int selected = resources.IndexOf(tile.Resource) + 1;
		return (names, selected);
	}

	public static string City(Tile tile) {
		return tile.HasCity(out City city)
			? $"{city.name}, size {city.residents.Count}, {city.owner?.civilization?.name ?? "nobody's"}"
			: null;
	}

	public static List<string> Units(Tile tile) {
		return tile.unitsOnTile.Select(u => $"{u.unitType.name} ({u.owner?.civilization?.name ?? "nobody's"})").ToList();
	}
}

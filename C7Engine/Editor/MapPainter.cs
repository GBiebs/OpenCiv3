using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine.Editor;

// One of the terrain brush's choices: a terrain of the rules, or one of
// Civ3's three variants of one (its Terrain brush lists "Bonus Grassland",
// "Snow-Capped Mountains" and "Pine Forest" after the terrains).
public sealed record TerrainBrush(TerrainType Terrain, TerrainBrush.Kind Variant, string Name) {
	public enum Kind { Plain, BonusGrassland, SnowCapped, PineForest }
}

// One of the overlay brush's choices (Civ3's list, rivers aside): an
// improvement of the rules (a road, a mine, a colony...), or a tile's flag
// (a goody hut, a barbarian camp, a starting or victory point location).
public sealed record OverlayBrush(OverlayBrush.Kind What, string Name, TerrainImprovement Improvement) {
	public enum Kind { River, GoodyHut, BarbarianCamp, Improvement, StartingLocation, VictoryLocation }
}

// Something on a tile that the right-click menu's Delete submenu can take
// off: the resource, an overlay (an improvement or a flag), or the river
// along one edge.
public sealed record Erasable(string Name, Resource Resource = null, OverlayBrush Overlay = null, TileDirection? RiverEdge = null,
	City City = null, MapUnit Unit = null);

// Paints the map: the brushes' rules and their undo (EditHistory). A stroke
// is one press of the mouse, however far it's dragged: BeginStroke, Paint
// at each tile the brush passes over, EndStroke.
public sealed partial class MapPainter {
	private readonly GameData gameData;
	private readonly Random random;
	public EditHistory History { get; }

	// Civ3's View > Options "randomize bonus grassland": painted grassland
	// is bonus grassland a third of the time (the map generator's share),
	// rather than never.
	public bool RandomizeBonusGrassland { get; set; }

	// Brush sizes, as Civ3's editor's: 1x1, 3x3, 5x5 and 7x7.
	public static readonly int[] Sizes = [1, 3, 5, 7];

	public MapPainter(GameData gameData) {
		this.gameData = gameData;
		History = new EditHistory(gameData.map);
		random = new Random(gameData.seed);
	}

	private GameMap Map => gameData.map;

	// The terrain brushes, in the rules' order of terrains (Civ3's: desert to
	// ocean), then the variants the rules have the terrain for.
	public List<TerrainBrush> TerrainBrushes() {
		List<TerrainBrush> brushes = gameData.terrainTypes
			.Where(t => t != TerrainType.NONE && !string.IsNullOrEmpty(t.Key))
			.Select(t => new TerrainBrush(t, TerrainBrush.Kind.Plain, NameOf(t)))
			.ToList();
		void AddVariant(Func<TerrainType, bool> which, TerrainBrush.Kind kind, string name) {
			if (gameData.terrainTypes.Find(t => which(t)) is TerrainType terrain) {
				brushes.Add(new TerrainBrush(terrain, kind, name));
			}
		}
		AddVariant(t => t.IsGrassland, TerrainBrush.Kind.BonusGrassland, "Bonus Grassland");
		AddVariant(t => t.IsMountains, TerrainBrush.Kind.SnowCapped, "Snow-Capped Mountains");
		AddVariant(t => t.IsForest, TerrainBrush.Kind.PineForest, "Pine Forest");
		return brushes;
	}

	private static string NameOf(TerrainType terrain) {
		return string.IsNullOrEmpty(terrain.DisplayName) ? terrain.Key : terrain.DisplayName;
	}

	// The tiles a brush of a size covers around a tile: a square of size by
	// size tiles, as the map's diamonds go (the tile alone, or it and the
	// 1, 2 or 3 rings around it). Off a map's edge that doesn't wrap, less.
	public static List<Tile> TilesUnder(Tile center, int size) {
		List<Tile> tiles = [];
		if (center == null || center == Tile.NONE || center.map == null) {
			return tiles;
		}
		int rings = Math.Max(0, (size - 1) / 2);
		for (int along = -rings; along <= rings; ++along) {
			for (int across = -rings; across <= rings; ++across) {
				// One step along a diamond's rows is (+1, +1), across (-1, +1).
				Tile tile = center.map.tileAt(center.XCoordinate + along - across, center.YCoordinate + along + across);
				if (tile != Tile.NONE && !tiles.Contains(tile)) {
					tiles.Add(tile);
				}
			}
		}
		return tiles;
	}

	// Why the terrain can't be painted on the tile, or null if it can. A
	// city keeps land under it, and one only on terrain that allows cities
	// (Civ3 1.33's "whether or not cities can be built on a particular
	// terrain type"); units keep terrain they can stand on.
	public static string CantPaint(Tile tile, TerrainType terrain) {
		if (tile.HasCity()) {
			if (terrain.IsWater) {
				return "A city stands here.";
			}
			if (!terrain.allowCities) {
				return $"A city stands here, and cities can't be on {NameOf(terrain)}.";
			}
		}
		foreach (MapUnit unit in tile.unitsOnTile) {
			if (terrain.IsWater && unit.unitType.IsLandUnit() && !tile.unitsOnTile.Any(u => u.unitType.IsSeaUnit())) {
				return "A land unit stands here.";
			}
			if (!terrain.IsWater && unit.unitType.IsSeaUnit() && !tile.HasCity()) {
				return "A ship is here.";
			}
		}
		return null;
	}


	// The resource brush's choices: the rules' resources, in their order
	// (Civ3's: strategic, luxury, then bonus).
	public List<Resource> ResourceBrushes() {
		return gameData.Resources.Where(r => r != null && r.Category != ResourceCategory.NONE).ToList();
	}

	// Why the resource can't be placed on the tile, or null if it can: only
	// on terrain the rules allow it on (Civ3's help: "resource placement is
	// also limited by which terrain types can support the given resource").
	public static string CantPlace(Tile tile, Resource resource) {
		TerrainType terrain = tile.overlayTerrainType;
		if (terrain == null || !terrain.allowedResources.Contains(resource.Key)) {
			return $"{resource.Name} can't be on {(terrain == null ? "this terrain" : NameOf(terrain))}.";
		}
		return null;
	}

	// Places the resource on the tile, in place of any other (Civ3's brush
	// sizes are the terrain brush's only, so one tile). Returns the tiles to
	// redraw, or none.
	public List<Tile> PlaceResource(Tile tile, Resource resource) {
		if (tile == null || tile == Tile.NONE || CantPlace(tile, resource) != null || tile.Resource == resource) {
			return [];
		}
		History.Touch(tile);
		tile.Resource = resource;
		tile.ResourceKey = resource.Key;
		Journal([tile]);
		return [tile];
	}

	// The overlay brushes in the order of Civ3's help ("Brush Selector
	// (Overlay)"): each improvement only if the rules have it.
	public List<OverlayBrush> OverlayBrushes() {
		List<OverlayBrush> brushes = [new OverlayBrush(OverlayBrush.Kind.River, "River", null)];
		void Improvement(string key, string name) {
			if (gameData.terrainImprovements.Find(i => i.key == key) is TerrainImprovement improvement) {
				brushes.Add(new OverlayBrush(OverlayBrush.Kind.Improvement, name, improvement));
			}
		}
		brushes.Add(new OverlayBrush(OverlayBrush.Kind.GoodyHut, "Goody Hut", null));
		brushes.Add(new OverlayBrush(OverlayBrush.Kind.BarbarianCamp, "Barbarian Camp", null));
		Improvement(Tile.TileOverlays.FORTRESS, "Fortress");
		Improvement(Tile.TileOverlays.COLONY, "Colony");
		Improvement(Tile.TileOverlays.IRRIGATION, "Irrigation");
		Improvement(Tile.TileOverlays.MINE, "Mine");
		Improvement(Tile.TileOverlays.RAILROAD, "Railroad");
		Improvement(Tile.TileOverlays.ROAD, "Road");
		Improvement(Tile.TileOverlays.POLLUTION, "Pollution");
		brushes.Add(new OverlayBrush(OverlayBrush.Kind.StartingLocation, "Starting Location", null));
		Improvement(Tile.TileOverlays.AIRFIELD, "Airfield");
		Improvement(Tile.TileOverlays.RADAR_TOWER, "Radar Tower");
		Improvement(Tile.TileOverlays.OUTPOST, "Outpost");
		brushes.Add(new OverlayBrush(OverlayBrush.Kind.VictoryLocation, "Victory Point Location", null));
		Improvement(Tile.TileOverlays.RUINS, "Ruins");
		return brushes;
	}

	// Why the overlay can't go on the tile, or null if it can. Overlays are
	// land's. A city's tile takes only a road, a railroad or a victory point
	// location (the game keeps improvements off city tiles). The terrain must
	// allow the improvement: forts, colonies, airfields, radar towers and
	// outposts by its flags, roads where they add commerce, irrigation food
	// and mines shields, as the game's workers. A goody hut, a barbarian camp
	// and a starting location exclude one another (Civ3's Tile Properties).
	public static string CantPlace(Tile tile, OverlayBrush brush) {
		if (brush.What == OverlayBrush.Kind.River) {
			return null; // Its edge decides (CantPlaceRiver).
		}
		TerrainType terrain = tile.overlayTerrainType;
		if (terrain == null || terrain.IsWater) {
			return $"A {brush.Name.ToLowerInvariant()} goes on land only.";
		}
		bool roadLike = brush.Improvement?.layer == TerrainImprovement.Layer.Roads;
		if (tile.HasCity() && !roadLike && brush.What != OverlayBrush.Kind.VictoryLocation) {
			return "A city stands here.";
		}
		if (brush.Improvement is TerrainImprovement improvement) {
			bool allowed = improvement.key switch {
				Tile.TileOverlays.IRRIGATION => improvement.GetYieldBonus(terrain, Tile.YieldType.Food) > 0,
				Tile.TileOverlays.MINE => improvement.GetYieldBonus(terrain, Tile.YieldType.Production) > 0,
				_ => Tile.TileOverlays.TerrainAllows(terrain, improvement),
			};
			return allowed ? null : $"A {brush.Name.ToLowerInvariant()} can't be on {NameOf(terrain)}.";
		}
		bool hut = tile.hasGoodyHut, camp = tile.hasBarbarianCamp, start = tile.map?.startingLocations.Contains(tile) ?? false;
		return brush.What switch {
			OverlayBrush.Kind.GoodyHut when camp => "A barbarian camp is here.",
			OverlayBrush.Kind.GoodyHut when start => "A starting location is here.",
			OverlayBrush.Kind.BarbarianCamp when hut => "A goody hut is here.",
			OverlayBrush.Kind.BarbarianCamp when start => "A starting location is here.",
			OverlayBrush.Kind.StartingLocation when hut => "A goody hut is here.",
			OverlayBrush.Kind.StartingLocation when camp => "A barbarian camp is here.",
			_ => null,
		};
	}

	// Whether the tile already has the overlay (a railroad is a road too).
	private static bool Has(Tile tile, OverlayBrush brush) {
		if (brush.Improvement?.key == Tile.TileOverlays.ROAD && tile.HasRoad()) {
			return true;
		}
		return brush.What switch {
			OverlayBrush.Kind.GoodyHut => tile.hasGoodyHut,
			OverlayBrush.Kind.BarbarianCamp => tile.hasBarbarianCamp,
			OverlayBrush.Kind.StartingLocation => tile.map?.startingLocations.Contains(tile) ?? false,
			OverlayBrush.Kind.VictoryLocation => tile.isVictoryLocation,
			_ => tile.overlays.HasImprovement(brush.Improvement),
		};
	}

	// Puts the overlay on the tile (one tile: Civ3's brush sizes are the
	// terrain brush's). An improvement takes the place of the one on its
	// layer: irrigation a mine's, a railroad a road's, an outpost a
	// fortress's... A colony, outpost, airfield or radar tower placed here
	// belongs to no one until the editor has players to give it to.
	// Returns the tiles to redraw, or none.
	public List<Tile> PlaceOverlay(Tile tile, OverlayBrush brush) {
		if (tile == null || tile == Tile.NONE || CantPlace(tile, brush) != null || Has(tile, brush)) {
			return [];
		}
		History.Touch(tile);
		switch (brush.What) {
			case OverlayBrush.Kind.GoodyHut:
				tile.hasGoodyHut = true;
				break;
			case OverlayBrush.Kind.BarbarianCamp:
				tile.hasBarbarianCamp = true;
				break;
			case OverlayBrush.Kind.StartingLocation:
				Map.startingLocations.Add(tile);
				break;
			case OverlayBrush.Kind.VictoryLocation:
				tile.isVictoryLocation = true;
				break;
			default:
				tile.overlays.Add(brush.Improvement);
				break;
		}
		Journal([tile]);
		return [tile];
	}

	// The tile's edges, which rivers run along.
	public static readonly TileDirection[] Edges = [TileDirection.NORTHEAST, TileDirection.SOUTHEAST, TileDirection.SOUTHWEST, TileDirection.NORTHWEST];

	// Why a river can't run along the tile's edge, or null if it can. On
	// Civ3's maps a river's edges always have land on both sides (every
	// river edge in 12 of the user's saves); it reaches the sea at a corner.
	public static string CantPlaceRiver(Tile tile, TileDirection edge) {
		if (!tile.neighbors.TryGetValue(edge, out Tile other) || other == null || other == Tile.NONE) {
			return "The map ends here.";
		}
		if (tile.overlayTerrainType.IsWater || other.overlayTerrainType.IsWater) {
			return "A river runs between land tiles.";
		}
		return null;
	}

	// Lays a river along the tile's edge (both tiles' flags), and works out
	// the corner flags around it. Returns the tiles to redraw, or none.
	public List<Tile> PlaceRiver(Tile tile, TileDirection edge) {
		if (tile == null || tile == Tile.NONE || CantPlaceRiver(tile, edge) != null || Rivers.Has(tile, edge)) {
			return [];
		}
		Tile other = tile.neighbors[edge];
		List<Tile> around = Rivers.CornersAround([tile, other]);
		foreach (Tile t in around) {
			History.Touch(t);
		}
		Rivers.Set(tile, edge, true);
		Rivers.Set(other, edge.Reversed(), true);
		Rivers.SetCorners(around);
		Journal(around);
		return around;
	}

	// What the Delete submenu lists for the tile, in the overlay pop-up's
	// order after the resource; rivers one entry per edge.
	public List<Erasable> ErasablesOn(Tile tile) {
		List<Erasable> things = [];
		if (tile == null || tile == Tile.NONE) {
			return things;
		}
		if (tile.cityAtTile is City city) {
			things.Add(new Erasable($"City: {city.name}", City: city));
		}
		foreach (MapUnit unit in tile.unitsOnTile) {
			things.Add(new Erasable($"{unit.unitType.name} ({ScenarioPlayers.NameOf(unit.owner)})", Unit: unit));
		}
		if (tile.Resource != null && tile.Resource != Resource.NONE && tile.Resource.Category != ResourceCategory.NONE) {
			things.Add(new Erasable(tile.Resource.Name, Resource: tile.Resource));
		}
		foreach (TileDirection edge in Edges) {
			if (Rivers.Has(tile, edge)) {
				things.Add(new Erasable($"River ({EdgeName(edge)} edge)", RiverEdge: edge));
			}
		}
		foreach (OverlayBrush brush in OverlayBrushes()) {
			bool on = brush.What switch {
				OverlayBrush.Kind.River => false,
				OverlayBrush.Kind.Improvement => tile.overlays.HasImprovement(brush.Improvement),
				_ => Has(tile, brush),
			};
			if (on) {
				things.Add(new Erasable(brush.Name, Overlay: brush));
			}
		}
		// Improvements the pop-up hasn't got (craters, fallout, a barricade).
		foreach (TerrainImprovement improvement in tile.overlays.GetImprovements()) {
			if (!things.Any(t => t.Overlay?.Improvement == improvement)) {
				things.Add(new Erasable(Capitalized(improvement.key), Overlay: new OverlayBrush(OverlayBrush.Kind.Improvement, improvement.key, improvement)));
			}
		}
		return things;
	}

	private static string EdgeName(TileDirection edge) => edge switch {
		TileDirection.NORTHEAST => "northeast",
		TileDirection.SOUTHEAST => "southeast",
		TileDirection.SOUTHWEST => "southwest",
		_ => "northwest",
	};

	private static string Capitalized(string key) => string.IsNullOrEmpty(key) ? key : char.ToUpperInvariant(key[0]) + key[1..];

	// Takes the things off the tile, as one action ("Delete Mine", or
	// "Delete All" for several). Returns the tiles to redraw, or none.
	public List<Tile> Erase(Tile tile, IReadOnlyList<Erasable> things) {
		if (tile == null || tile == Tile.NONE || things.Count == 0) {
			return [];
		}
		History.Begin(things.Count == 1 ? $"Delete {things[0].Name}" : "Delete All");
		HashSet<Tile> redraw = [tile];
		History.Touch(tile);
		foreach (Erasable thing in things) {
			if (thing.City is City city && tile.cityAtTile == city) {
				TakeCity(city);
				History.Record(() => AddCity(city), () => TakeCity(city), changesCities: true);
			} else if (thing.Unit is MapUnit unit && tile.unitsOnTile.Contains(unit)) {
				TakeUnit(unit);
				History.Record(() => AddUnit(unit), () => TakeUnit(unit));
			} else if (thing.Resource != null && tile.Resource == thing.Resource) {
				tile.Resource = Resource.NONE;
				tile.ResourceKey = Resource.NONE.Key;
			} else if (thing.RiverEdge is TileDirection edge && Rivers.Has(tile, edge)) {
				List<Tile> around = Rivers.CornersAround([tile, tile.neighbors[edge]]);
				foreach (Tile t in around) {
					History.Touch(t);
				}
				Rivers.Set(tile, edge, false);
				if (tile.neighbors.TryGetValue(edge, out Tile other) && other != null && other != Tile.NONE) {
					Rivers.Set(other, edge.Reversed(), false);
				}
				Rivers.SetCorners(around);
				redraw.UnionWith(around);
			} else if (thing.Overlay is OverlayBrush overlay) {
				switch (overlay.What) {
					case OverlayBrush.Kind.GoodyHut:
						tile.hasGoodyHut = false;
						break;
					case OverlayBrush.Kind.BarbarianCamp:
						tile.hasBarbarianCamp = false;
						break;
					case OverlayBrush.Kind.StartingLocation:
						Map.startingLocations.Remove(tile);
						break;
					case OverlayBrush.Kind.VictoryLocation:
						tile.isVictoryLocation = false;
						break;
					case OverlayBrush.Kind.Improvement when tile.overlays.HasImprovement(overlay.Improvement):
						tile.overlays.Remove(overlay.Improvement);
						break;
				}
			}
		}
		Journal(redraw);
		// No terrain changes: the continents stay as they are.
		return History.End() == null ? [] : [.. redraw];
	}

	// Civ3's Map > Redistribute: the starting locations, the resources,
	// the goody huts or the bonus grassland placed afresh on the map as it
	// is, by the map generator's own step (MapGenerator.Redistribute) with
	// the seed. One undo step; only the tiles that change are recorded.
	public EditHistory.Step Redistribute(MapGenerator.Redistribution what, int seed) {
		GameMap map = Map;
		WorldCharacteristics wc = WorldMaker.CharacteristicsOf(gameData);
		(Resource[] resources, bool[] huts, bool[] bonus, List<Tile> starts) = MapGenerator.Redistribute(map, wc, what, seed);
		History.Begin(what switch {
			MapGenerator.Redistribution.StartingLocations => "Redistribute Starting Locations",
			MapGenerator.Redistribution.Resources => "Redistribute Natural Resources",
			MapGenerator.Redistribution.GoodyHuts => "Redistribute Goody Huts",
			_ => "Redistribute Bonus Grassland",
		});
		List<Tile> changed = [];
		for (int i = 0; i < map.tiles.Count; ++i) {
			Tile tile = map.tiles[i];
			switch (what) {
				case MapGenerator.Redistribution.Resources: {
						Resource now = resources[i] == null ? Resource.NONE
						: gameData.Resources.Find(r => r.Key == resources[i].Key) ?? Resource.NONE;
						if (tile.Resource != now) {
							History.Touch(tile);
							tile.Resource = now;
							tile.ResourceKey = now.Key;
							changed.Add(tile);
						}
						break;
					}
				case MapGenerator.Redistribution.GoodyHuts when tile.hasGoodyHut != huts[i]:
					History.Touch(tile);
					tile.hasGoodyHut = huts[i];
					changed.Add(tile);
					break;
				case MapGenerator.Redistribution.BonusGrassland when tile.isBonusShield != bonus[i]:
					History.Touch(tile);
					tile.isBonusShield = bonus[i];
					changed.Add(tile);
					break;
			}
		}
		if (what == MapGenerator.Redistribution.StartingLocations) {
			foreach (Tile tile in map.startingLocations.Concat(starts).Distinct().ToList()) {
				History.Touch(tile);
				changed.Add(tile);
			}
			map.startingLocations.Clear();
			map.startingLocations.AddRange(starts);
		}
		Journal(changed);
		return History.End();
	}

	public void BeginStroke(string name) {
		History.Begin(name);
	}

	// Paints the brush on the tiles a brush of this size covers around the
	// tile, skipping any it can't be painted on. Returns the tiles whose
	// look may have changed (to redraw), or none.
	public List<Tile> PaintTerrain(Tile center, int size, TerrainBrush brush) {
		List<Tile> painted = [];
		foreach (Tile tile in TilesUnder(center, size)) {
			if (CantPaint(tile, brush.Terrain) == null && !Shows(tile, brush)) {
				History.Touch(tile);
				SetTerrain(tile, brush);
				painted.Add(tile);
			}
		}
		if (painted.Count == 0) {
			return [];
		}
		HashSet<Tile> changed = [.. painted];
		changed.UnionWith(MakeCoasts(painted));
		List<Tile> redraw = ChooseArt(changed);
		Journal(redraw);
		return redraw;
	}

	// Tells the map views (the minimap follows the engine's tile journal)
	// what to draw again.
	private static void Journal(IEnumerable<Tile> tiles) {
		foreach (Tile tile in tiles) {
			TileChangeJournal.Record(tile);
		}
	}

	// Ends the stroke: the continents and fresh water are worked out again
	// if it changed land into water or back (a whole-map pass: 34 ms on a
	// 362 x 362 map, measured 2026-11-17). Returns the action for the undo
	// list, or null if nothing changed.
	public EditHistory.Step EndStroke() {
		EditHistory.Step step = History.End();
		if (step != null && step.ChangesWater) {
			Settle();
		}
		return step;
	}

	public EditHistory.Step Undo() {
		EditHistory.Step step = History.Undo();
		if (step != null) {
			if (step.ChangesWater) {
				Settle();
			}
			Journal(LookChanged(step));
		}
		return step;
	}

	public EditHistory.Step Redo() {
		EditHistory.Step step = History.Redo();
		if (step != null) {
			if (step.ChangesWater) {
				Settle();
			}
			Journal(LookChanged(step));
		}
		return step;
	}

	// The tiles whose look an action changed: its own, and those whose art
	// depends on them (TerrainTextureFiles: a tile's art is the corner it
	// shares with its north, northwest and northeast neighbors).
	public static List<Tile> LookChanged(EditHistory.Step step) {
		HashSet<Tile> tiles = [.. step.Tiles];
		foreach (Tile tile in step.Tiles) {
			tiles.UnionWith(ArtDependents(tile));
		}
		return [.. tiles];
	}

	// Whether the tile already is what the brush paints.
	// (With bonus grassland randomized, grassland is grassland, bonus or
	// not, so a drag over it doesn't roll again.)
	private bool Shows(Tile tile, TerrainBrush brush) {
		bool anyBonus = RandomizeBonusGrassland && brush.Variant == TerrainBrush.Kind.Plain && brush.Terrain.IsGrassland;
		return tile.overlayTerrainType == brush.Terrain
			&& (anyBonus || tile.isBonusShield == (brush.Variant == TerrainBrush.Kind.BonusGrassland))
			&& tile.isSnowCapped == (brush.Variant == TerrainBrush.Kind.SnowCapped)
			&& tile.isPineForest == (brush.Variant == TerrainBrush.Kind.PineForest);
	}

	// Desert, plains, grassland or tundra: the land a tile's base can be.
	private static bool IsBasicLand(TerrainType t) {
		return t.IsDesert || t.IsPlains || t.IsGrassland || t.IsTundra;
	}

	// The terrain under a hill, mountain, forest and so on (the tile's base
	// terrain, which its art is drawn from): the land already there, or
	// grassland on what was water; a flood plain is a river's desert.
	private TerrainType BaseFor(Tile tile, TerrainType terrain) {
		if (terrain.IsWater || IsBasicLand(terrain)) {
			return terrain;
		}
		if (terrain.IsFloodPlain) {
			return gameData.terrainTypes.Find(t => t.IsDesert) ?? terrain;
		}
		if (IsBasicLand(tile.baseTerrainType)) {
			return tile.baseTerrainType;
		}
		return gameData.terrainTypes.Find(t => t.IsGrassland) ?? terrain;
	}

	private void SetTerrain(Tile tile, TerrainBrush brush) {
		TerrainType terrain = brush.Terrain;
		bool wasWater = tile.baseTerrainType.IsWater;
		tile.baseTerrainType = BaseFor(tile, terrain);
		tile.overlayTerrainType = terrain;
		tile.isBonusShield = brush.Variant == TerrainBrush.Kind.BonusGrassland
			|| (RandomizeBonusGrassland && brush.Variant == TerrainBrush.Kind.Plain && terrain.IsGrassland && random.Next(3) == 0);
		tile.isSnowCapped = brush.Variant == TerrainBrush.Kind.SnowCapped;
		tile.isPineForest = brush.Variant == TerrainBrush.Kind.PineForest;
		if (terrain.IsWater && !wasWater) {
			ClearLandThings(tile);
		}
		Tidy(tile);
	}

	// What only land has: rivers (on both sides of each edge), huts, camps,
	// victory locations and starting locations.
	private void ClearLandThings(Tile tile) {
		if (Edges.Any(e => Rivers.Has(tile, e))) {
			List<Tile> around = Rivers.CornersAround([tile]);
			foreach (Tile t in around) {
				History.Touch(t);
			}
			foreach (TileDirection edge in Edges) {
				Rivers.Set(tile, edge, false);
				if (tile.neighbors.TryGetValue(edge, out Tile other) && other != null && other != Tile.NONE) {
					Rivers.Set(other, edge.Reversed(), false);
				}
			}
			Rivers.SetCorners(around);
		}
		tile.hasGoodyHut = false;
		tile.hasBarbarianCamp = false;
		tile.isVictoryLocation = false;
		Map.startingLocations.Remove(tile);
	}

	// Takes off what the tile's terrain no longer allows: a resource it
	// can't have, improvements it can't have (all of them on water).
	private static void Tidy(Tile tile) {
		TerrainType terrain = tile.overlayTerrainType;
		if (tile.Resource != null && tile.Resource != Resource.NONE && tile.Resource.Category != ResourceCategory.NONE
			&& !terrain.allowedResources.Contains(tile.Resource.Key)) {
			tile.Resource = Resource.NONE;
			tile.ResourceKey = Resource.NONE.Key;
		}
		foreach (TerrainImprovement improvement in tile.overlays.GetImprovements().ToList()) {
			if (terrain.IsWater || !Tile.TileOverlays.TerrainAllows(terrain, improvement)) {
				tile.overlays.Remove(improvement);
			}
		}
	}

	// Sea and ocean next to land become coast, as on Civ3's maps, where
	// only coast touches land. Returns the tiles that changed.
	private List<Tile> MakeCoasts(List<Tile> painted) {
		TerrainType coast = gameData.terrainTypes.Find(t => t.IsCoast);
		if (coast == null) {
			return [];
		}
		HashSet<Tile> near = [];
		foreach (Tile tile in painted) {
			near.Add(tile);
			foreach (Tile n in tile.neighbors.Values) {
				if (n != null && n != Tile.NONE) {
					near.Add(n);
				}
			}
		}
		List<Tile> changed = [];
		foreach (Tile tile in near) {
			if ((tile.overlayTerrainType.IsSea || tile.overlayTerrainType.IsOcean)
				&& tile.neighbors.Values.Any(n => n != null && n != Tile.NONE && !n.overlayTerrainType.IsWater)) {
				History.Touch(tile);
				tile.baseTerrainType = coast;
				tile.overlayTerrainType = coast;
				Tidy(tile);
				changed.Add(tile);
			}
		}
		return changed;
	}

	private static IEnumerable<Tile> ArtDependents(Tile tile) {
		foreach (TileDirection direction in new[] { TileDirection.SOUTH, TileDirection.SOUTHWEST, TileDirection.SOUTHEAST }) {
			if (tile.neighbors.TryGetValue(direction, out Tile n) && n != null && n != Tile.NONE) {
				yield return n;
			}
		}
	}

	// Chooses the art again for the changed tiles and those drawn from them.
	// Returns them all.
	private List<Tile> ChooseArt(IEnumerable<Tile> changed) {
		HashSet<Tile> tiles = [];
		foreach (Tile tile in changed) {
			tiles.Add(tile);
			tiles.UnionWith(ArtDependents(tile));
		}
		foreach (Tile tile in tiles) {
			History.Touch(tile);
		}
		TerrainTextureFiles.ReassignTextureDetails(random, gameData.terrainTypes, tiles);
		return [.. tiles];
	}

	// Works out the continents and fresh water lakes again, as a new map's.
	private void Settle() {
		foreach (Tile tile in Map.tiles) {
			if (tile.IsWater()) {
				tile.isFreshWater = false;
			}
		}
		Map.recomputeContinents();
	}
}

// A tile's river flags by direction. Civ3 keeps rivers on a tile's four
// edges (northeast, southeast, southwest, northwest), on both tiles of an
// edge, and a flag for each corner (north, east, south, west) that is set
// exactly when the river edges meeting there cut the tile off from the tile
// across the corner: measured on 12 of the user's Civ3 saves (every corner
// flag agreed; one edge alone, or two bending round a side tile, leave it
// off), and how the game reads it (Tile.HasRiverCrossing).
public static class Rivers {
	private static readonly (TileDirection corner, TileDirection left, TileDirection right)[] Corners = [
		(TileDirection.NORTH, TileDirection.NORTHWEST, TileDirection.NORTHEAST),
		(TileDirection.EAST, TileDirection.NORTHEAST, TileDirection.SOUTHEAST),
		(TileDirection.SOUTH, TileDirection.SOUTHEAST, TileDirection.SOUTHWEST),
		(TileDirection.WEST, TileDirection.SOUTHWEST, TileDirection.NORTHWEST),
	];

	// The tiles whose corner flags the edges of these tiles can change: the
	// tiles and all their neighbours.
	public static List<Tile> CornersAround(IEnumerable<Tile> tiles) {
		HashSet<Tile> around = [];
		foreach (Tile tile in tiles) {
			around.Add(tile);
			foreach (Tile n in tile.neighbors.Values) {
				if (n != null && n != Tile.NONE) {
					around.Add(n);
				}
			}
		}
		return [.. around];
	}

	// Works out the tiles' corner flags from the edges.
	public static void SetCorners(IEnumerable<Tile> tiles) {
		foreach (Tile tile in tiles) {
			foreach ((TileDirection corner, TileDirection left, TileDirection right) in Corners) {
				Set(tile, corner, CornerCut(tile, corner, left, right));
			}
		}
	}

	// Whether the river edges at the corner cut the tile off from the one
	// across it: neither way round the corner (past the left tile or the
	// right one) is free of river.
	private static bool CornerCut(Tile tile, TileDirection corner, TileDirection left, TileDirection right) {
		if (!tile.neighbors.TryGetValue(corner, out Tile across) || across == null || across == Tile.NONE) {
			return false;
		}
		bool viaLeft = !Has(tile, left) && !Has(across, right.Reversed());
		bool viaRight = !Has(tile, right) && !Has(across, left.Reversed());
		return !viaLeft && !viaRight;
	}

	public static bool Has(Tile tile, TileDirection direction) {
		return direction switch {
			TileDirection.NORTH => tile.riverNorth,
			TileDirection.NORTHEAST => tile.riverNortheast,
			TileDirection.EAST => tile.riverEast,
			TileDirection.SOUTHEAST => tile.riverSoutheast,
			TileDirection.SOUTH => tile.riverSouth,
			TileDirection.SOUTHWEST => tile.riverSouthwest,
			TileDirection.WEST => tile.riverWest,
			TileDirection.NORTHWEST => tile.riverNorthwest,
			_ => false,
		};
	}

	public static void Set(Tile tile, TileDirection direction, bool on) {
		switch (direction) {
			case TileDirection.NORTH: tile.riverNorth = on; break;
			case TileDirection.NORTHEAST: tile.riverNortheast = on; break;
			case TileDirection.EAST: tile.riverEast = on; break;
			case TileDirection.SOUTHEAST: tile.riverSoutheast = on; break;
			case TileDirection.SOUTH: tile.riverSouth = on; break;
			case TileDirection.SOUTHWEST: tile.riverSouthwest = on; break;
			case TileDirection.WEST: tile.riverWest = on; break;
			case TileDirection.NORTHWEST: tile.riverNorthwest = on; break;
		}
	}
}

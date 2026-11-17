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

// Paints the map: the brushes' rules and their undo (EditHistory). A stroke
// is one press of the mouse, however far it's dragged: BeginStroke, Paint
// at each tile the brush passes over, EndStroke.
public sealed class MapPainter {
	private readonly GameData gameData;
	private readonly Random random;
	public EditHistory History { get; }

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

	// Ends the stroke: the continents and fresh water are worked out again.
	// Returns the action for the undo list, or null if nothing changed.
	public EditHistory.Step EndStroke() {
		EditHistory.Step step = History.End();
		if (step != null) {
			Settle();
		}
		return step;
	}

	public EditHistory.Step Undo() {
		EditHistory.Step step = History.Undo();
		if (step != null) {
			Settle();
			Journal(LookChanged(step));
		}
		return step;
	}

	public EditHistory.Step Redo() {
		EditHistory.Step step = History.Redo();
		if (step != null) {
			Settle();
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
	private static bool Shows(Tile tile, TerrainBrush brush) {
		return tile.overlayTerrainType == brush.Terrain
			&& tile.isBonusShield == (brush.Variant == TerrainBrush.Kind.BonusGrassland)
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
		tile.isBonusShield = brush.Variant == TerrainBrush.Kind.BonusGrassland;
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
		foreach (TileDirection direction in Enum.GetValues<TileDirection>()) {
			if (Rivers.Has(tile, direction)) {
				Rivers.Set(tile, direction, false);
				if (tile.neighbors.TryGetValue(direction, out Tile other) && other != null && other != Tile.NONE) {
					History.Touch(other);
					Rivers.Set(other, direction.Reversed(), false);
				}
			}
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
			tile.ResourceKey = null;
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

// A tile's river flags by direction.
public static class Rivers {
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

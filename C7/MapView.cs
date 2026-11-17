using System.Collections.Generic;
using System;
using C7.Map;
using Godot;
using C7GameData;
using C7Engine;
using Serilog;
using System.Diagnostics;
using System.Runtime.CompilerServices;

// Loose layers are for drawing things on the map on a per-tile basis. (Historical aside: There used to be another kind of layer called a TileLayer
// that was intended to draw regularly tiled objects like terrain sprites but using LooseLayers for everything was found to be a prefereable
// approach.) LooseLayer is effectively the standard map layer. The MapView contains a list of loose layers, inside a LooseView object. Right now to
// add a new layer you must modify the MapView constructor to add it to the list, but (TODO) eventually that will be made moddable.
public abstract class LooseLayer {
	// drawObject draws the things this layer is supposed to draw that are associated with the given tile. Its parameters are:
	//   looseView: The Node2D to actually draw to, e.g., use looseView.DrawCircle(...) to draw a circle. This object also contains a reference to
	//     the MapView in case you need it.
	//   gameData: A reference to the game data so each layer doesn't have to redundantly request access.
	//   tile: The game tile whose contents are to be drawn. This function gets called for each tile in view of the camera and none out of
	//     view. The same tile may be drawn multiple times at different locations due to edge wrapping.
	//   tileCenter: The location to draw to. You should draw around this location without adjusting for the camera location or zoom since the
	//     MapView already transforms the looseView node to account for those things.
	public abstract void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter);

	public virtual void onBeginDraw(LooseView looseView, GameData gameData) { }
	public virtual void onEndDraw(LooseView looseView, GameData gameData) { }

	// Whether the layer has nothing to draw at the moment, like the tile assignments while no city is shown. A view whose layers are all
	// blank isn't redrawn again once it's been drawn blank.
	public virtual bool IsBlank => false;

	// A layer that only draws on a few of the tiles in the drawn region, like the units, returns them here, in the order the region's tiles
	// are drawn in, so the others needn't be looked at; null to be given every tile. Called after onBeginDraw.
	public virtual List<MapView.VisibleTile> TilesToDraw(LooseView looseView, GameData gameData) => null;

	// Called when a LAN client swaps in the host's snapshot. Layers must forget what they remember about the old game's tiles, cities and
	// units, since the new game data is made of new objects.
	public virtual void onGameDataReplaced(GameData gameData) { }

	// The layer will be skipped during map drawing if visible is false
	public bool visible {
		get => isVisible;
		set {
			if (isVisible != value) {
				isVisible = value;
				RequestRedraw();
			}
		}
	}
	private bool isVisible = true;

	// Most views are only redrawn when the map changes. A layer whose contents change for another reason (a setting, the city being shown)
	// asks for its view to be redrawn with this.
	protected void RequestRedraw() {
		redrawRequested = true;
	}
	internal bool redrawRequested = false;
}

// Terrain types classified once, so the terrain layers don't compare terrain keys for every tile they draw.
[Flags]
public enum TerrainKind {
	None = 0,
	Hilly = 1 << 0,
	Mountains = 1 << 1,
	Hills = 1 << 2,
	Volcano = 1 << 3,
	Forest = 1 << 4,
	Jungle = 1 << 5,
	Marsh = 1 << 6,
	Grassland = 1 << 7,
	Plains = 1 << 8,
	Water = 1 << 9,
}

public static class TerrainKinds {
	private static readonly Dictionary<TerrainType, TerrainKind> kinds = new(ReferenceEqualityComparer.Instance);

	public static TerrainKind Of(TerrainType type) {
		if (kinds.TryGetValue(type, out TerrainKind kind)) {
			return kind;
		}

		kind = TerrainKind.None;
		if (type.isHilly()) kind |= TerrainKind.Hilly;
		if (type.isWater()) kind |= TerrainKind.Water;
		kind |= type.Key switch {
			"mountains" => TerrainKind.Mountains,
			"hills" => TerrainKind.Hills,
			"volcano" => TerrainKind.Volcano,
			"forest" => TerrainKind.Forest,
			"jungle" => TerrainKind.Jungle,
			"marsh" => TerrainKind.Marsh,
			"grassland" => TerrainKind.Grassland,
			"plains" => TerrainKind.Plains,
			_ => TerrainKind.None,
		};
		kinds[type] = kind;
		return kind;
	}

	public static bool Is(TerrainType type, TerrainKind kind) {
		return (Of(type) & kind) != 0;
	}

	// Whether any of the tiles sharing an edge with this one is water.
	public static bool HasWaterOnEdge(Tile tile) {
		return Is(tile.neighbors[TileDirection.NORTHEAST].baseTerrainType, TerrainKind.Water)
			|| Is(tile.neighbors[TileDirection.NORTHWEST].baseTerrainType, TerrainKind.Water)
			|| Is(tile.neighbors[TileDirection.SOUTHEAST].baseTerrainType, TerrainKind.Water)
			|| Is(tile.neighbors[TileDirection.SOUTHWEST].baseTerrainType, TerrainKind.Water);
	}

	// Terrain types belong to a game, so forget them when the game is replaced.
	public static void Clear() {
		kinds.Clear();
	}
}

public partial class TerrainLayer : LooseLayer {

	public static readonly Vector2 terrainSpriteSize = new Vector2(128, 64);

	// TileToDraw stores the arguments passed to drawObject so the draws can be grouped by texture file before being submitted. This
	// significantly reduces the number of draw calls Godot must generate (1483 to 312 when fully zoomed out on our test map) and modestly
	// improves framerate (by about 14% on my system).
	private struct TileToDraw {
		public Tile tile;
		public Vector2 tileCenter;
	}

	// The tiles to draw, grouped by BaseTerrainFileID and drawn in increasing file ID order. Grouping keeps the order in which tiles were
	// added within a group. Tiles without terrain info or with out of range file IDs are kept aside and sorted.
	private List<TileToDraw>[] tilesByFileID = [];
	private readonly List<TileToDraw> tilesWithOddFileID = new();

	// The base terrain sprite depends only on the file and image IDs, so look it up by those rather than by tile.
	private readonly Dictionary<(int fileID, int imageID), ImageTexture> terrainTextures = new();

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		// Each sprite overlaps the tiles to its south, so those are drawn along with it in case they aren't drawn on their own. Known
		// neighbors inside the drawn region draw themselves, so they're skipped here to avoid drawing the same sprite twice.
		Enqueue(tile, tileCenter);
		int X = (int)(tileCenter.X / MapView.cellSize.X) - 1;
		int Y = (int)(tileCenter.Y / MapView.cellSize.Y) - 1;
		EnqueueNeighbor(looseView, tile.neighbors[TileDirection.SOUTH], X, Y + 2, tileCenter + new Vector2(0, 64));
		EnqueueNeighbor(looseView, tile.neighbors[TileDirection.SOUTHWEST], X - 1, Y + 1, tileCenter + new Vector2(-64, 32));
		EnqueueNeighbor(looseView, tile.neighbors[TileDirection.SOUTHEAST], X + 1, Y + 1, tileCenter + new Vector2(64, 32));
	}

	private void EnqueueNeighbor(LooseView looseView, Tile neighbor, int X, int Y, Vector2 tileCenter) {
		if (looseView.drawRegion.Contains(X, Y) && looseView.IsTileKnown(neighbor)) {
			return;
		}
		// A tile that doesn't draw itself is the neighbor of up to three tiles that do, so only draw it once at each place.
		if (!enqueuedNeighbors.Add(((long)X << 32) | (uint)Y)) {
			return;
		}
		Enqueue(neighbor, tileCenter);
	}

	// The virtual coordinates of the neighbors enqueued in this draw.
	private readonly HashSet<long> enqueuedNeighbors = new();

	public override void onBeginDraw(LooseView looseView, GameData gameData) {
		enqueuedNeighbors.Clear();
	}

	private void Enqueue(Tile tile, Vector2 tileCenter) {
		if (tile == Tile.NONE) {
			return;
		}

		TileToDraw tTD = new() { tile = tile, tileCenter = tileCenter };
		int fileID = tile?.ExtraInfo?.BaseTerrainFileID ?? int.MaxValue;
		if (fileID < 0 || fileID >= 1024) {
			tilesWithOddFileID.Add(tTD);
			return;
		}

		if (fileID >= tilesByFileID.Length) {
			int oldLength = tilesByFileID.Length;
			Array.Resize(ref tilesByFileID, fileID + 1);
			for (int i = oldLength; i < tilesByFileID.Length; i++) {
				tilesByFileID[i] = new List<TileToDraw>();
			}
		}
		tilesByFileID[fileID].Add(tTD);
	}

	// Null for a tile without terrain art info: the blank stand-ins a LAN
	// host sends a guest for the tiles it knows nothing about (see
	// SnapshotFilter), which the fog hides. The terrain config would fail on
	// them (owner's report, 2026-10-28: a guest's map had no hills,
	// forests, rivers, borders or resources, as that stopped the drawing).
	private ImageTexture GetTexture(Tile tile) {
		if (tile.ExtraInfo == null) {
			return null;
		}

		var key = (tile.ExtraInfo.BaseTerrainFileID, tile.ExtraInfo.BaseTerrainImageID);
		if (!terrainTextures.TryGetValue(key, out ImageTexture texture)) {
			texture = TextureLoader.Load("terrain.base", tile);
			terrainTextures[key] = texture;
		}
		return texture;
	}

	public override void onEndDraw(LooseView looseView, GameData gameData) {
		// Tiles with unusual file IDs (which shouldn't exist) are drawn in file ID order around the usual ones.
		if (tilesWithOddFileID.Count > 0) {
			tilesWithOddFileID.Sort((a, b) => FileID(a).CompareTo(FileID(b)));
		}
		DrawTiles(looseView, tilesWithOddFileID, drawNegativeFileIDs: true);
		foreach (List<TileToDraw> tiles in tilesByFileID) {
			DrawTiles(looseView, tiles);
		}
		DrawTiles(looseView, tilesWithOddFileID, drawNegativeFileIDs: false);
		tilesWithOddFileID.Clear();
		enqueuedNeighbors.Clear();
	}

	private static int FileID(TileToDraw tTD) {
		return tTD.tile?.ExtraInfo?.BaseTerrainFileID ?? int.MaxValue;
	}

	private void DrawTiles(LooseView looseView, List<TileToDraw> tiles) {
		foreach (TileToDraw tTD in tiles) {
			DrawTile(looseView, tTD);
		}
		tiles.Clear();
	}

	private void DrawTiles(LooseView looseView, List<TileToDraw> tiles, bool drawNegativeFileIDs) {
		foreach (TileToDraw tTD in tiles) {
			if ((FileID(tTD) < 0) == drawNegativeFileIDs) {
				DrawTile(looseView, tTD);
			}
		}
	}

	private void DrawTile(LooseView looseView, TileToDraw tTD) {
		ImageTexture texture = GetTexture(tTD.tile);
		if (texture == null) {
			return;
		}

		Vector2 terrainOffset = new Vector2(0, -1 * MapView.cellSize.Y);
		Vector2 position = tTD.tileCenter - (float)0.5 * terrainSpriteSize + terrainOffset;

		// Multiply size by 100.1% so avoid "seams" in the map.  See issue #106.
		// Jim's option of a whole-map texture is less hacky, but this is quicker and seems to be working well.
		Rect2 screenRect = new Rect2(position, terrainSpriteSize * 1.001f);

		looseView.DrawTextureRect(texture, screenRect, tile: false);
	}
}

public partial class HillsLayer : LooseLayer {
	public static readonly Vector2 mountainSize = new Vector2(128, 88);
	public static readonly Vector2 volcanoSize = new Vector2(128, 88);  //same as mountain
	public static readonly Vector2 hillsSize = new Vector2(128, 72);
	private ImageTexture mountainTexture;
	private ImageTexture snowMountainTexture;
	private ImageTexture forestMountainTexture;
	private ImageTexture jungleMountainTexture;
	private ImageTexture hillsTexture;
	private ImageTexture forestHillsTexture;
	private ImageTexture jungleHillsTexture;
	private ImageTexture volcanosTexture;
	private ImageTexture forestVolcanoTexture;
	private ImageTexture jungleVolcanoTexture;

	public HillsLayer() {
		mountainTexture = TextureLoader.Load("terrain.mountain.base");
		snowMountainTexture = TextureLoader.Load("terrain.mountain.snow");
		forestMountainTexture = TextureLoader.Load("terrain.mountain.forest");
		jungleMountainTexture = TextureLoader.Load("terrain.mountain.jungle");
		hillsTexture = TextureLoader.Load("terrain.hill.base");
		forestHillsTexture = TextureLoader.Load("terrain.hill.forest");
		jungleHillsTexture = TextureLoader.Load("terrain.hill.jungle");
		volcanosTexture = TextureLoader.Load("terrain.volcano.base");
		forestVolcanoTexture = TextureLoader.Load("terrain.volcano.forest");
		jungleVolcanoTexture = TextureLoader.Load("terrain.volcano.jungle");
	}

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		TerrainKind kind = TerrainKinds.Of(tile.overlayTerrainType);
		if ((kind & TerrainKind.Hilly) != 0) {
			int pcxIndex = getMountainIndex(tile);
			int row = pcxIndex / 4;
			int column = pcxIndex % 4;
			if ((kind & TerrainKind.Mountains) != 0) {
				Rect2 mountainRectangle = new Rect2(column * mountainSize.X, row * mountainSize.Y, mountainSize);
				Rect2 screenTarget = new Rect2(tileCenter - (float)0.5 * mountainSize + new Vector2(0, -12), mountainSize);
				ImageTexture mountainGraphics;
				if (tile.isSnowCapped) {
					mountainGraphics = snowMountainTexture;
				} else {
					TerrainKind dominantVegetation = getDominantVegetationNearHillyTile(tile);
					if (dominantVegetation == TerrainKind.Forest) {
						mountainGraphics = forestMountainTexture;
					} else if (dominantVegetation == TerrainKind.Jungle) {
						mountainGraphics = jungleMountainTexture;
					} else {
						mountainGraphics = mountainTexture;
					}
				}
				looseView.DrawTextureRectRegion(mountainGraphics, screenTarget, mountainRectangle);
			} else if ((kind & TerrainKind.Hills) != 0) {
				Rect2 hillsRectangle = new Rect2(column * hillsSize.X, row * hillsSize.Y, hillsSize);
				Rect2 screenTarget = new Rect2(tileCenter - (float)0.5 * hillsSize + new Vector2(0, -4), hillsSize);
				ImageTexture hillGraphics;
				TerrainKind dominantVegetation = getDominantVegetationNearHillyTile(tile);
				if (dominantVegetation == TerrainKind.Forest) {
					hillGraphics = forestHillsTexture;
				} else if (dominantVegetation == TerrainKind.Jungle) {
					hillGraphics = jungleHillsTexture;
				} else {
					hillGraphics = hillsTexture;
				}
				looseView.DrawTextureRectRegion(hillGraphics, screenTarget, hillsRectangle);
			} else if ((kind & TerrainKind.Volcano) != 0) {
				Rect2 volcanoRectangle = new Rect2(column * volcanoSize.X, row * volcanoSize.Y, volcanoSize);
				Rect2 screenTarget = new Rect2(tileCenter - (float)0.5 * volcanoSize + new Vector2(0, -12), volcanoSize);
				ImageTexture volcanoGraphics;
				TerrainKind dominantVegetation = getDominantVegetationNearHillyTile(tile);
				if (dominantVegetation == TerrainKind.Forest) {
					volcanoGraphics = forestVolcanoTexture;
				} else if (dominantVegetation == TerrainKind.Jungle) {
					volcanoGraphics = jungleVolcanoTexture;
				} else {
					volcanoGraphics = volcanosTexture;
				}
				looseView.DrawTextureRectRegion(volcanoGraphics, screenTarget, volcanoRectangle);
			}
		}
	}

	// Returns TerrainKind.Forest, TerrainKind.Jungle, or TerrainKind.None if neither should be drawn on the hilly tile.
	private static TerrainKind getDominantVegetationNearHillyTile(Tile center) {
		int hills = 0;
		int forests = 0;
		int jungles = 0;
		CountVegetation(center.neighbors[TileDirection.NORTHEAST].overlayTerrainType, ref hills, ref forests, ref jungles);
		CountVegetation(center.neighbors[TileDirection.NORTHWEST].overlayTerrainType, ref hills, ref forests, ref jungles);
		CountVegetation(center.neighbors[TileDirection.SOUTHEAST].overlayTerrainType, ref hills, ref forests, ref jungles);
		CountVegetation(center.neighbors[TileDirection.SOUTHWEST].overlayTerrainType, ref hills, ref forests, ref jungles);

		if (hills + forests + jungles < 4) {    //some surrounding tiles are neither forested nor hilly
			return TerrainKind.None;
		}
		if (forests == 0 && jungles == 0) {
			return TerrainKind.None;    //all hills
		}
		if (forests > jungles) {
			return TerrainKind.Forest;
		}
		if (jungles > forests) {
			return TerrainKind.Jungle;
		}

		//If we get here, it's a tie between forest and jungle.  Deterministically choose one so it doesn't change on every render
		if (center.XCoordinate % 2 == 0) {
			return TerrainKind.Forest;
		}
		return TerrainKind.Jungle;
	}

	private static void CountVegetation(TerrainType type, ref int hills, ref int forests, ref int jungles) {
		TerrainKind kind = TerrainKinds.Of(type);
		if ((kind & TerrainKind.Hilly) != 0) {
			hills++;
		} else if ((kind & TerrainKind.Forest) != 0) {
			forests++;
		} else if ((kind & TerrainKind.Jungle) != 0) {
			jungles++;
		}
	}

	private static int getMountainIndex(Tile tile) {
		int index = 0;
		if (TerrainKinds.Is(tile.neighbors[TileDirection.NORTHWEST].overlayTerrainType, TerrainKind.Hilly)) {
			index++;
		}
		if (TerrainKinds.Is(tile.neighbors[TileDirection.NORTHEAST].overlayTerrainType, TerrainKind.Hilly)) {
			index += 2;
		}
		if (TerrainKinds.Is(tile.neighbors[TileDirection.SOUTHWEST].overlayTerrainType, TerrainKind.Hilly)) {
			index += 4;
		}
		if (TerrainKinds.Is(tile.neighbors[TileDirection.SOUTHEAST].overlayTerrainType, TerrainKind.Hilly)) {
			index += 8;
		}
		return index;
	}
}

public partial class ForestLayer : LooseLayer {
	public static readonly Vector2 forestJungleSize = new Vector2(128, 88);

	private ImageTexture largeJungleTexture;
	private ImageTexture smallJungleTexture;
	private ImageTexture largeForestTexture;
	private ImageTexture largePlainsForestTexture;
	private ImageTexture largeTundraForestTexture;
	private ImageTexture smallForestTexture;
	private ImageTexture smallPlainsForestTexture;
	private ImageTexture smallTundraForestTexture;
	private ImageTexture pineForestTexture;
	private ImageTexture pinePlainsTexture;
	private ImageTexture pineTundraTexture;

	public ForestLayer() {
		largeJungleTexture = TextureLoader.Load("terrain.jungle.large");
		smallJungleTexture = TextureLoader.Load("terrain.jungle.small");
		largeForestTexture = TextureLoader.Load("terrain.forest.large");
		largePlainsForestTexture = TextureLoader.Load("terrain.forest.plains.large");
		largeTundraForestTexture = TextureLoader.Load("terrain.forest.tundra.large");
		smallForestTexture = TextureLoader.Load("terrain.forest.small");
		smallPlainsForestTexture = TextureLoader.Load("terrain.forest.plains.small");
		smallTundraForestTexture = TextureLoader.Load("terrain.forest.tundra.small");
		pineForestTexture = TextureLoader.Load("terrain.pine.forest");
		pinePlainsTexture = TextureLoader.Load("terrain.pine.plains");
		pineTundraTexture = TextureLoader.Load("terrain.pine.tundra");
	}

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		TerrainKind kind = TerrainKinds.Of(tile.overlayTerrainType);
		if ((kind & TerrainKind.Jungle) != 0) {
			//Randomly, but predictably, choose a large jungle graphic
			//More research is needed on when to use large vs small jungles.  Probably, small is used when neighboring fewer jungles.
			//For the first pass, we're just always using large jungles.
			int randomJungleRow = tile.YCoordinate % 2;
			int randomJungleColumn;
			ImageTexture jungleTexture;
			if (TerrainKinds.HasWaterOnEdge(tile)) {
				randomJungleColumn = tile.XCoordinate % 6;
				jungleTexture = smallJungleTexture;
			} else {
				randomJungleColumn = tile.XCoordinate % 4;
				jungleTexture = largeJungleTexture;
			}
			Rect2 jungleRectangle = new Rect2(randomJungleColumn * forestJungleSize.X, randomJungleRow * forestJungleSize.Y, forestJungleSize);
			Rect2 screenTarget = new Rect2(tileCenter - (float)0.5 * forestJungleSize + new Vector2(0, -12), forestJungleSize);
			looseView.DrawTextureRectRegion(jungleTexture, screenTarget, jungleRectangle);
		}
		if ((kind & TerrainKind.Forest) != 0) {
			TerrainKind baseKind = TerrainKinds.Of(tile.baseTerrainType);
			bool grassland = (baseKind & TerrainKind.Grassland) != 0;
			bool plains = (baseKind & TerrainKind.Plains) != 0;
			int forestRow = 0;
			int forestColumn = 0;
			ImageTexture forestTexture;
			if (tile.isPineForest) {
				forestRow = tile.YCoordinate % 2;
				forestColumn = tile.XCoordinate % 6;
				if (grassland) {
					forestTexture = pineForestTexture;
				} else if (plains) {
					forestTexture = pinePlainsTexture;
				} else { //Tundra
					forestTexture = pineTundraTexture;
				}
			} else {
				forestRow = tile.YCoordinate % 2;
				if (TerrainKinds.HasWaterOnEdge(tile)) {
					forestColumn = tile.XCoordinate % 5;
					if (grassland) {
						forestTexture = smallForestTexture;
					} else if (plains) {
						forestTexture = smallPlainsForestTexture;
					} else {    //tundra
						forestTexture = smallTundraForestTexture;
					}
				} else {
					forestColumn = tile.XCoordinate % 4;
					if (grassland) {
						forestTexture = largeForestTexture;
					} else if (plains) {
						forestTexture = largePlainsForestTexture;
					} else {    //tundra
						forestTexture = largeTundraForestTexture;
					}
				}
			}
			Rect2 forestRectangle = new Rect2(forestColumn * forestJungleSize.X, forestRow * forestJungleSize.Y, forestJungleSize);
			Rect2 screenTarget = new Rect2(tileCenter - (float)0.5 * forestJungleSize + new Vector2(0, -12), forestJungleSize);
			looseView.DrawTextureRectRegion(forestTexture, screenTarget, forestRectangle);
		}
	}
}
public partial class MarshLayer : LooseLayer {
	public static readonly Vector2 marshSize = new Vector2(128, 88);
	//Because the marsh graphics are 88 pixels tall instead of the 64 of a tile, we also need an addition 12 pixel offset to the top
	//88 - 64 = 24; 24/2 = 12.  This keeps the marsh centered with half the extra 24 pixels above the tile and half below.
	readonly Vector2 MARSH_OFFSET = (float)0.5 * marshSize + new Vector2(0, -12);

	private ImageTexture largeMarshTexture;
	private ImageTexture smallMarshTexture;

	public MarshLayer() {
		largeMarshTexture = TextureLoader.Load("terrain.marsh.large");
		smallMarshTexture = TextureLoader.Load("terrain.marsh.small");
	}

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		if (TerrainKinds.Is(tile.overlayTerrainType, TerrainKind.Marsh)) {
			int randomJungleRow = tile.YCoordinate % 2;
			int randomMarshColumn;
			ImageTexture marshTexture;
			if (TerrainKinds.HasWaterOnEdge(tile)) {
				randomMarshColumn = tile.XCoordinate % 5;
				marshTexture = smallMarshTexture;
			} else {
				randomMarshColumn = tile.XCoordinate % 4;
				marshTexture = largeMarshTexture;
			}
			Rect2 jungleRectangle = new Rect2(randomMarshColumn * marshSize.X, randomJungleRow * marshSize.Y, marshSize);
			Rect2 screenTarget = new Rect2(tileCenter - MARSH_OFFSET, marshSize);
			looseView.DrawTextureRectRegion(marshTexture, screenTarget, jungleRectangle);
		}
	}
}

public partial class FloodPlainLayer : LooseLayer {
	public static readonly Vector2 floodPlainSize = new Vector2(128, 64);
	private ImageTexture floodPlainTexture;

	public FloodPlainLayer() {
		// A 4x4 grid indexed by which edges of the tile have a river, with
		// NW = 1, NE = 2, SW = 4 and SE = 8. Each river edge adds a patch of
		// flood plain along it.
		floodPlainTexture = TextureLoader.Load("terrain.flood_plain");
	}

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		if (!tile.overlayTerrainType.IsFloodPlain) {
			return;
		}

		int index = (tile.riverNorthwest ? 1 : 0)
			| (tile.riverNortheast ? 2 : 0)
			| (tile.riverSouthwest ? 4 : 0)
			| (tile.riverSoutheast ? 8 : 0);
		if (index == 0) {
			return;
		}

		Rect2 floodPlainRectangle = new Rect2(index % 4 * floodPlainSize.X, index / 4 * floodPlainSize.Y, floodPlainSize);
		Rect2 screenTarget = new Rect2(tileCenter - 0.5f * floodPlainSize, floodPlainSize);
		looseView.DrawTextureRectRegion(floodPlainTexture, screenTarget, floodPlainRectangle);
	}
}

public partial class RiverLayer : LooseLayer {
	public static readonly Vector2 riverSize = new Vector2(128, 64);
	public static readonly Vector2 riverCenterOffset = new Vector2(riverSize.X / 2, 0);
	private ImageTexture narrowTexture;
	private ImageTexture broadTexture;

	public RiverLayer() {
		// Both textures are 4x4 grids of equal dimensions
		// The narrow texture has four river deltas and 12 narrow river segments
		// The broad texture has 16 broad river segments with more meandering
		narrowTexture = TextureLoader.Load("terrain.river_delta");
		broadTexture = TextureLoader.Load("terrain.river");
	}

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		// The point where four terrain tiles meet and where we want to center our river texture.
		// It is the easternmost point of the current tile.
		Vector2 thePoint = tileCenter + riverCenterOffset;

		// We draw the texture centered on the point by starting the draw from half of its width away
		Vector2 drawOffset = -0.5f * riverSize;

		// The right river texture is calculated by evaluating the tiles around the point
		Tile northOfPoint = tile.neighbors[TileDirection.NORTHEAST];
		Tile eastOfPoint = tile.neighbors[TileDirection.EAST];
		Tile southOfPoint = tile.neighbors[TileDirection.SOUTHEAST];
		Tile westOfPoint = tile;

		var (row, col, idx) = DeriveTextureIndex(northOfPoint, eastOfPoint, southOfPoint, westOfPoint);
		if (row < 0 || col < 0 || idx < 0)
			return;

		Rect2 riverRectangle = new Rect2(col * riverSize.X, row * riverSize.Y, riverSize);
		Rect2 screenTarget = new Rect2(thePoint + drawOffset, riverSize);

		// Draw a river texture from one of the textures, depending on terrain.
		// The placement calculation is the same for both river textures.

		if (idx is 1 or 2 or 4 or 8) {
			// Narrow set has deltas at these indexes
			if (HasWater(northOfPoint, eastOfPoint, southOfPoint, westOfPoint))
				looseView.DrawTextureRectRegion(narrowTexture, screenTarget, riverRectangle);
			else
				looseView.DrawTextureRectRegion(broadTexture, screenTarget, riverRectangle);
		} else {
			// TODO: When to draw from the broad texture? Is it semi-random? Is it based on elevation?

			// Default: narrow rivers, less meandering
			looseView.DrawTextureRectRegion(narrowTexture, screenTarget, riverRectangle);
		}
	}

	private static (int, int, int) DeriveTextureIndex(Tile north, Tile east, Tile south, Tile west) {
		var textureIndex = 0;

		if (north.riverSouthwest || west.riverNortheast) {
			textureIndex += 1;
		}
		if (east.riverNorthwest || north.riverSoutheast) {
			textureIndex += 2;
		}
		if (west.riverSoutheast || south.riverNorthwest) {
			textureIndex += 4;
		}
		if (south.riverNortheast || east.riverSouthwest) {
			textureIndex += 8;
		}

		// Passes "The Point" only: an oasis
		if (textureIndex == 0 && (north.riverSouth || east.riverWest || west.riverEast || south.riverNorth))
			return (0, 0, 0);

		if (textureIndex == 0)
			return (-1, -1, -1);

		// We will index into a 4x4 texture matrix
		return (textureIndex / 4, textureIndex % 4, textureIndex);
	}

	private static bool HasWater(Tile north, Tile east, Tile south, Tile west) {
		return TerrainKinds.Is(north.baseTerrainType, TerrainKind.Water)
			|| TerrainKinds.Is(east.baseTerrainType, TerrainKind.Water)
			|| TerrainKinds.Is(south.baseTerrainType, TerrainKind.Water)
			|| TerrainKinds.Is(west.baseTerrainType, TerrainKind.Water);
	}
}

public partial class GridLayer : LooseLayer {
	public Color color = Color.Color8(50, 50, 50, 150);
	public float lineWidth = (float)1.0;

	public GridLayer() { }

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		Vector2 cS = MapView.cellSize;
		Vector2 left = tileCenter + new Vector2(-cS.X, 0);
		Vector2 top = tileCenter + new Vector2(0, -cS.Y);
		Vector2 right = tileCenter + new Vector2(cS.X, 0);
		looseView.DrawLine(left, top, color, lineWidth);
		looseView.DrawLine(top, right, color, lineWidth);

		if (gameData.observerMode && gameData.showGridCoordinates) {
			looseView.DrawString(ThemeDB.FallbackFont,
								 tileCenter + new Vector2(-16, 0), tile.XCoordinate + "," + tile.YCoordinate,
								 HorizontalAlignment.Center, -1, 24, Color.Color8(255, 0, 0));
		}
	}
}

public partial class BuildingLayer : LooseLayer {
	private ImageTexture barbCamp;
	private ImageTexture goodyHut;
	// Null when the art has none, as standalone mode's may not.
	private ImageTexture victoryLocation;

	public BuildingLayer() {
		barbCamp = TextureLoader.Load("terrain.barbarian_camp");
		goodyHut = TextureLoader.Load("terrain.goody_hut");
		victoryLocation = TextureLoader.TryLoad("terrain.victory_location");
	}

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		if (tile.isVictoryLocation && victoryLocation != null) {
			Rect2 screenRect = new(tileCenter - 0.5f * victoryLocation.GetSize(), victoryLocation.GetSize());
			looseView.DrawTextureRect(victoryLocation, screenRect, tile: false);
		}
		if (tile.hasBarbarianCamp) {
			Rect2 screenRect = new(tileCenter - 0.5f * barbCamp.GetSize(), barbCamp.GetSize());
			looseView.DrawTextureRect(barbCamp, screenRect, tile: false);
		}
		if (tile.hasGoodyHut) {
			Rect2 screenRect = new(tileCenter - 0.5f * goodyHut.GetSize(), goodyHut.GetSize());
			looseView.DrawTextureRect(goodyHut, screenRect, tile: false);
		}
	}
}

public partial class LooseView : Node2D {
	// How often a view is redrawn.
	public enum RedrawPolicy {
		// Only when what's on the map or the drawn region changes. The view draws a region somewhat larger than the screen so that the
		// camera can move a little without a redraw.
		WhenMapChanges,
		// Like WhenMapChanges, and also whenever MapView.contentVersion changes, for views that keep things worked out against it up to date
		// as they're drawn, like the city labels.
		WhenContentChanges,
		// Every frame while the player is choosing a goto destination or a bombard target.
		WhileTargeting,
		// Like WhenMapChanges, and also every frame while an animation is playing, whenever what the units are drawn for (the selected unit,
		// the UI's player, the zoom) changes, and a couple of times a second in case something else changed. Looping animations, like a
		// worker at work, are kept going in between without drawing the units again; see UnitLayer.UpdateLoopingAnimations.
		WhenUnitsChange,
		// Every frame.
		EveryFrame,
	}

	public MapView mapView;
	public List<LooseLayer> layers = new List<LooseLayer>();
	public readonly RedrawPolicy redrawPolicy;
	// The MapView.contentVersion the view was last queued for redrawing at; see RedrawPolicy.WhenContentChanges.
	internal int queuedContentVersion = -1;
	// Whether the view was last queued for redrawing with nothing to draw; see LooseLayer.IsBlank.
	private bool queuedBlank = false;

	// Queues the view to be redrawn, unless it has nothing to draw and has already been drawn that way.
	public void QueueRedrawUnlessBlank() {
		bool blank = true;
		foreach (LooseLayer layer in layers) {
			if (layer.visible && !layer.IsBlank) {
				blank = false;
				break;
			}
		}
		if (!blank || !queuedBlank) {
			QueueRedraw();
		}
		queuedBlank = blank;
	}
	private static ILogger log = Log.ForContext<LooseView>();

	// Valid while the view is being drawn: the region of tiles being drawn, and the player whose view of the map is shown (null in
	// observer mode, where everything is known). The tile knowledge is also null while the whole map is revealed.
	public MapView.VisibleRegion drawRegion { get; private set; }
	public Player uiPlayer { get; private set; }
	public TileKnowledge tileKnowledge { get; private set; }
	private bool observerMode;

	public LooseView(MapView mapView, RedrawPolicy redrawPolicy = RedrawPolicy.EveryFrame) {
		this.mapView = mapView;
		this.redrawPolicy = redrawPolicy;
	}

	// Whether one of this view's layers asked to be redrawn. Clears the requests.
	public bool TakeRedrawRequest() {
		bool requested = false;
		foreach (LooseLayer layer in layers) {
			requested |= layer.redrawRequested;
			layer.redrawRequested = false;
		}
		return requested;
	}

	public override void _Draw() {
		base._Draw();

		// EngineStorage.ReadGameData only hands over the game data, so it's read directly, sparing a closure every draw.
		DrawLayers(EngineStorage.gameData);
	}

	// The layers whose drawing has failed, each logged once.
	private static readonly HashSet<Type> failedLayers = new();

	private void DrawLayers(GameData gD) {
		// Iterating over visible tiles is unfortunately pretty expensive, so the MapView collects them once and shares them with every
		// view. Views drawn every frame only draw what's on screen; the others draw everything the MapView collected.
		bool onlyOnScreen = redrawPolicy == RedrawPolicy.WhileTargeting || redrawPolicy == RedrawPolicy.EveryFrame;
		MapView.VisibleRegion visRegion = onlyOnScreen ? mapView.getVisibleRegion() : default;
		List<MapView.VisibleTile> tiles = mapView.GetVisibleTiles(gD, onlyOnScreen ? visRegion : null);
		drawRegion = onlyOnScreen ? visRegion : mapView.drawnRegion;

		observerMode = mapView.ShowsWholeMap(gD);
		uiPlayer = gD.observerMode ? null : gD.GetUIControllerPlayer();
		tileKnowledge = observerMode ? null : uiPlayer?.tileKnowledge;

		long start = Stopwatch.GetTimestamp();
		foreach (LooseLayer layer in layers) {
			if (!layer.visible || layer is FogOfWarLayer || layer is GridLayer) {
				continue;
			}
			// A layer that fails is left out, and the rest still drawn.
			try {
				layer.onBeginDraw(this, gD);
				foreach (MapView.VisibleTile vT in layer.TilesToDraw(this, gD) ?? tiles) {
					if (vT.known && (!onlyOnScreen || visRegion.Contains(vT.x, vT.y))) {
						layer.drawObject(this, gD, vT.tile, vT.tileCenter);
					}
				}
				layer.onEndDraw(this, gD);
			} catch (Exception e) {
				if (failedLayers.Add(layer.GetType())) {
					Serilog.Log.ForContext<LooseView>().Error(e, "The map's {Layer} failed to draw", layer.GetType().Name);
				}
			}
		}

		foreach (LooseLayer layer in layers) {
			if (!layer.visible || layer is not GridLayer) {
				continue;
			}
			layer.onBeginDraw(this, gD);
			// Known tiles first, then the unknown ones.
			for (int pass = 0; pass < 2; pass++) {
				bool known = pass == 0;
				foreach (MapView.VisibleTile vT in tiles) {
					if (vT.known == known && (!onlyOnScreen || visRegion.Contains(vT.x, vT.y))) {
						layer.drawObject(this, gD, vT.tile, vT.tileCenter);
					}
				}
			}
			layer.onEndDraw(this, gD);
		}

		long elapsedMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency;
		if (elapsedMilliseconds > 100) {
			log.Warning("-> End draw of the {View} view: {Milliseconds} milliseconds", layers.Count > 0 ? layers[0].GetType().Name : "empty", elapsedMilliseconds);
		}

		if (!observerMode) {
			foreach (LooseLayer layer in layers) {
				if (layer is not FogOfWarLayer) {
					continue;
				}
				layer.onBeginDraw(this, gD);
				foreach (MapView.VisibleTile vT in tiles) {
					if (vT.tile != Tile.NONE && (!onlyOnScreen || visRegion.Contains(vT.x, vT.y))) {
						layer.drawObject(this, gD, vT.tile, vT.tileCenter);
					}
				}
				layer.onEndDraw(this, gD);
			}
		}

		uiPlayer = null;
		tileKnowledge = null;
	}

	// Whether the UI's player knows the tile. Only valid while the view is being drawn.
	public bool IsTileKnown(Tile tile) {
		// Off the map there's nothing to know, even when the whole map is shown.
		if (!Tile.IsTileValid(tile)) {
			return false;
		}
		return observerMode || tileKnowledge.isTileKnown(tile);
	}

	public bool IsTileCoveredByTileInfo(Tile tile) {
		var target = mapView.game.tileInfo;
		if (target == null) return false;

		return target.IsCovered(tile);
	}

	public bool HasCityLabelToHideFromTileInfo(Tile tile) {
		if (!tile.HasCity()) return false;

		var target = mapView.game.tileInfo;
		if (target == null) return false;

		return target.HasCityLabelToCover(tile);
	}
}

public partial class MapView : Node2D {
	// cellSize is half the size of the tile sprites, or the amount of space each tile takes up when they are packed on the grid (note tiles are
	// staggered and half overlap).
	public static readonly Vector2 cellSize = new Vector2(64, 32);
	public Vector2 scaledCellSize {
		get { return cellSize * new Vector2(cameraZoom, cameraZoom); }
	}

	public Game game;

	public MapView() { }

	public int mapWidth { get; private set; }
	public int mapHeight { get; private set; }
	public bool wrapHorizontally { get; private set; }
	public bool wrapVertically { get; private set; }

	private Vector2 internalCameraLocation = new Vector2(0, 0);
	public Vector2 cameraLocation {
		get {
			return internalCameraLocation;
		}
		set {
			setCameraLocation(value);
		}
	}
	public float internalCameraZoom = 1;
	public float cameraZoom {
		get { return internalCameraZoom; }
		set { setCameraZoomFromMiddle(value); }
	}

	private List<LooseView> looseViews = new();

	// Specifies a rectangular block of tiles that are currently potentially on screen. Accessible through getVisibleRegion(). Tile coordinates
	// are "virtual", i.e. "unwrapped", so there isn't necessarily a tile at each location. The region is intended to include the upper left
	// coordinates but not the lower right ones. When iterating over all tiles in the region you must account for the fact that map rows are
	// staggered, see MapView.CollectVisibleTiles for an example.
	public struct VisibleRegion {
		public int upperLeftX, upperLeftY;
		public int lowerRightX, lowerRightY;

		public int getRowStartX(int y) {
			return upperLeftX + (y - upperLeftY) % 2;
		}

		public readonly bool Contains(int X, int Y) {
			return X >= upperLeftX && X < lowerRightX && Y >= upperLeftY && Y < lowerRightY;
		}

		// Whether every tile of the other region is also in this one. The regions' rows must also be staggered the same way.
		public readonly bool Covers(VisibleRegion other) {
			return upperLeftX <= other.upperLeftX && upperLeftY <= other.upperLeftY
				&& lowerRightX >= other.lowerRightX && lowerRightY >= other.lowerRightY
				&& ((upperLeftX - upperLeftY) - (other.upperLeftX - other.upperLeftY)) % 2 == 0;
		}
	}

	// A tile in the drawn region and where to draw it. X and Y are the tile's virtual coordinates.
	public struct VisibleTile {
		public Tile tile;
		public Vector2 tileCenter;
		public int x, y;
		public bool known;
	}

	public GridLayer gridLayer { get; private set; }
	public CityLayer cityLayer { get; private set; }
	private UnitLayer unitLayer;
	private LooseView unitLayerView;
	public TileAssignmentLayer tileAssignmentLayer { get; private set; }
	public BorderLayer borderLayer { get; private set; }
	private LooseView terrainLooseView;

	// Adds a layer drawn over the terrain view's (the scenario editor's).
	public void AddTerrainLayer(LooseLayer layer) {
		terrainLooseView.layers.Add(layer);
		InvalidateMap();
	}

	// The game's minimap (the scenario editor shows its own instead).
	public MiniMap GameMiniMap => miniMap;

	// What bare map mode hides: whole views (cities and units are child nodes, so skipping their layers wouldn't hide them) and the layers
	// of the terrain view drawn on top of the terrain and resources.
	private readonly List<LooseView> viewsHiddenOnBareMap = new();
	private readonly List<LooseLayer> layersHiddenOnBareMap = new();

	// Shows only the terrain and resources, without cities, units, roads, improvements, borders and the like (Ctrl+Shift+M).
	public bool bareMap {
		get => isBareMap;
		set {
			if (isBareMap == value) {
				return;
			}
			isBareMap = value;
			foreach (LooseView view in viewsHiddenOnBareMap) {
				view.Visible = !value;
			}
			foreach (LooseLayer layer in layersHiddenOnBareMap) {
				layer.visible = !value;
			}
			InvalidateMap();
		}
	}
	private bool isBareMap = false;

	// Shows the whole map without fog of war, as observers see it, but without handing the game over to the AI. Only changes what this
	// client draws, to look over the generated terrain; the players' knowledge of the map is untouched, so turning it off restores the fog.
	public bool revealWholeMap {
		get => isWholeMapRevealed;
		set {
			if (isWholeMapRevealed == value) {
				return;
			}
			isWholeMapRevealed = value;
			InvalidateMap();
		}
	}
	private bool isWholeMapRevealed = false;

	// Whether every tile is drawn as known: in observer mode, or while the whole map is revealed.
	public bool ShowsWholeMap(GameData gD) {
		return gD.observerMode || revealWholeMap;
	}

	const float MIN_SCALE = 0.1f;
	const float MAX_SCALE = 4.0f;

	private TransportInfoBox transportInfoBox;
	private LowerRightInfoBox lowerRightInfoBox;
	private MiniMap miniMap;

	// The region drawn by the views that are only redrawn when the map changes, and the tiles in it in drawing order.
	public VisibleRegion drawnRegion { get; private set; }
	private bool hasDrawnRegion = false;
	private readonly List<VisibleTile> visibleTiles = new();
	private bool visibleTilesStale = true;

	// Bumped whenever something on the map may have changed. Layers use it to know when to recompute what they remember, such as city
	// labels.
	public int contentVersion { get; private set; } = 0;
	// Whether every view is to be redrawn, and whether the units are.
	private bool mapChanged = true;
	private bool unitsChanged = true;

	// While the engine is busy (the AI is playing, animations are running) the game changes without telling the UI. The units are redrawn
	// every frame until a little after it is done, and once more when it's done; during the computer's turn, only several times a second.
	// The other views, which are costly to redraw, are only redrawn when mapChangeTracker notices something they show may have changed, as
	// often as the units at most, and in case it missed something every BusyFullRedrawSeconds and once more when the engine is done.
	// Things remembered against contentVersion are recomputed a few times a second meanwhile.
	private const int BusyGraceFrames = 10;
	private const double BusyRedrawSeconds = 0.1;
	private const double BusyRefreshSeconds = 0.25;
	private const double BusyFullRedrawSeconds = 1.0;
	private int busyFramesLeft = 0;
	private double busyRedrawTimer = 0;
	private double busyRefreshTimer = 0;
	private double busyFullRedrawTimer = 0;
	private readonly MapChangeTracker mapChangeTracker = new();
	private long lastProcessedMessageCount = -1;
	private bool mapWasHidden = false;
	private bool targetingViewDrawn = false;

	// What the units were last drawn for, and how long ago; see RedrawPolicy.WhenUnitsChange.
	private readonly record struct UnitsDrawnFor(VisibleRegion region, float indicatorZoom, MapUnit selected, Player controller,
		TileInfo tileInfo, bool unitsVisible, bool animating);
	private UnitsDrawnFor unitsDrawnFor;
	private double unitsDrawnSeconds = 0;
	private const double UnitsRefreshSeconds = 0.5;

	public override void _Ready() {
		lowerRightInfoBox = GetNode<LowerRightInfoBox>("/root/C7Game/CanvasLayer/Control/GameStatus/LowerRightInfoBox");
		lowerRightInfoBox.game = game;
		lowerRightInfoBox.CenterCameraOnActiveUnit += OnCenterCameraOnUnit;

		var canvasControl = GetNode<Control>("/root/C7Game/CanvasLayer/Control");

		miniMap = new MiniMap(this);
		canvasControl.AddChild(miniMap);

		transportInfoBox = new TransportInfoBox(game);
		canvasControl.AddChild(transportInfoBox);
	}

	public override void _ExitTree() {
		lowerRightInfoBox.CenterCameraOnActiveUnit -= OnCenterCameraOnUnit;
		// Take our pieces of the HUD with us.
		miniMap.QueueFree();
		transportInfoBox.QueueFree();
	}

	public MapView(Game game, int mapWidth, int mapHeight, bool wrapHorizontally, bool wrapVertically) {
		this.game = game;
		this.mapWidth = mapWidth;
		this.mapHeight = mapHeight;
		this.wrapHorizontally = wrapHorizontally;
		this.wrapVertically = wrapVertically;

		TerrainKinds.Clear();

		// Set up our set of views, and the layers within each view.
		//
		// The drawing order within a view matches the order of `layers`, so
		// here borders will be drawn on top of the terrain.
		//
		// However, because cities and units use child nodes, we need separate
		// LooseView objects to get the ordering correct between textures and
		// nodes. Without this unit health bars (which are textures) would
		// be drawn behind cities (which are child nodes).
		LooseView terrainView = new(this, LooseView.RedrawPolicy.WhenMapChanges);
		terrainView.layers.Add(new TerrainLayer());
		terrainView.layers.Add(new FloodPlainLayer());
		terrainView.layers.Add(new RiverLayer());
		terrainView.layers.Add(new ForestLayer());
		terrainView.layers.Add(new MarshLayer());
		terrainView.layers.Add(new HillsLayer());
		terrainView.layers.Add(new TntLayer());
		TileOverlayLayer tileOverlayLayer = new();
		terrainView.layers.Add(tileOverlayLayer);
		terrainView.layers.Add(new ResourceLayer());
		this.gridLayer = new GridLayer();
		terrainView.layers.Add(this.gridLayer);
		BuildingLayer buildingLayer = new();
		terrainView.layers.Add(buildingLayer);
		borderLayer = new();
		terrainView.layers.Add(borderLayer);
		terrainLooseView = terrainView;
		layersHiddenOnBareMap.AddRange([tileOverlayLayer, buildingLayer, borderLayer]);

		LooseView cityView = new(this, LooseView.RedrawPolicy.WhenContentChanges);
		this.cityLayer = new();
		cityView.layers.Add(this.cityLayer);

		LooseView tileAssignmentView = new(this, LooseView.RedrawPolicy.WhenContentChanges);
		this.tileAssignmentLayer = new();
		tileAssignmentView.layers.Add(this.tileAssignmentLayer);

		LooseView fogOfWarView = new(this, LooseView.RedrawPolicy.WhenMapChanges);
		fogOfWarView.layers.Add(new FogOfWarLayer());

		LooseView unitView = new(this, LooseView.RedrawPolicy.WhileTargeting);
		unitView.layers.Add(new GotoLayer());
		unitView.layers.Add(new BombardLayer());
		LooseView otherView = new(this, LooseView.RedrawPolicy.WhenUnitsChange);
		unitLayerView = otherView;
		unitLayer = new UnitLayer();
		otherView.layers.Add(unitLayer);

		AddChild(terrainView);
		looseViews.Add(terrainView);

		AddChild(cityView);
		looseViews.Add(cityView);

		AddChild(tileAssignmentView);
		looseViews.Add(tileAssignmentView);

		AddChild(fogOfWarView);
		looseViews.Add(fogOfWarView);

		AddChild(unitView);
		looseViews.Add(unitView);
		AddChild(otherView);
		looseViews.Add(otherView);

		viewsHiddenOnBareMap.AddRange([cityView, tileAssignmentView, unitView, otherView]);
	}

	// Tells the map that what's on it may have changed, e.g. after the engine sent the UI a message.
	public void InvalidateMap() {
		++contentVersion;
		mapChanged = true;
	}

	// Whether this map view can show the given map, as when a LAN client receives a snapshot of the same game.
	public bool CanShow(GameMap map) {
		return map.numTilesWide == mapWidth && map.numTilesTall == mapHeight
			&& map.wrapHorizontally == wrapHorizontally && map.wrapVertically == wrapVertically;
	}

	// Points the map at new game data with the same map, e.g. a LAN snapshot, keeping the camera where it is.
	public void RebindToGameData(GameData gameData) {
		TerrainKinds.Clear();
		foreach (LooseView looseView in looseViews) {
			foreach (LooseLayer layer in looseView.layers) {
				layer.onGameDataReplaced(gameData);
			}
		}
		visibleTilesStale = true;
		InvalidateMap();
	}

	public override void _Process(double delta) {
		if (game == null) {
			return;
		}

		// Brings the animations up to date before the units are drawn. This happens here rather than when the units are drawn because the
		// unit view isn't drawn in bare map mode, and the engine waits for the animations it starts to finish.
		game.animationController?.updateAnimations();
		if (game.animationController != null && EngineStorage.gameData != null) {
			unitLayer.PruneAnimations(game.animationController.animTracker, EngineStorage.gameData);
		}

		// Nothing to draw while the map is hidden; catch up once it's back.
		bool hidden = game.IsMapHidden;
		if (hidden) {
			mapWasHidden = true;
		} else if (mapWasHidden) {
			mapWasHidden = false;
			InvalidateMap();
		}

		// Any message the engine processed may have changed the map.
		long processedMessageCount = EngineStorage.processedMessageCount;
		if (processedMessageCount != lastProcessedMessageCount) {
			lastProcessedMessageCount = processedMessageCount;
			InvalidateMap();
		}

		if (game.MapMayChangeWithoutNotice) {
			if (busyFramesLeft == 0) {
				// Just got busy: redraw right away.
				busyRedrawTimer = BusyRedrawSeconds;
			}
			busyFramesLeft = BusyGraceFrames;
		}
		// Whether to look for changes the engine didn't tell us about.
		bool checkForChanges = true;
		if (busyFramesLeft > 0) {
			--busyFramesLeft;
			// The last busy frame shows and remembers the game as the engine left it.
			bool done = busyFramesLeft == 0;
			busyRedrawTimer += delta;
			bool throttled = game.CurrentState == Game.GameState.ComputerTurn;
			checkForChanges = !throttled || busyRedrawTimer >= BusyRedrawSeconds || done;
			if (checkForChanges) {
				busyRedrawTimer = 0;
				unitsChanged = true;
			}
			busyFullRedrawTimer += delta;
			if (busyFullRedrawTimer >= BusyFullRedrawSeconds || done) {
				busyFullRedrawTimer = 0;
				mapChanged = true;
			}
			busyRefreshTimer += delta;
			if (busyRefreshTimer >= BusyRefreshSeconds || done) {
				busyRefreshTimer = 0;
				++contentVersion;
			}
		} else {
			busyRedrawTimer = 0;
			busyRefreshTimer = 0;
			busyFullRedrawTimer = 0;
		}

		UpdateDrawnRegion();
		// Also catches the screen changing size.
		cityLayer.PlaceScenes(this);

		// Done after the drawn region is settled, since only changes in it matter. While the map is hidden the changes are kept for later.
		if (checkForChanges && !hidden) {
			GameData gD = EngineStorage.gameData;
			if (gD != null && mapChangeTracker.Check(gD, this, drawnRegion)) {
				mapChanged = true;
			}
		}

		if (mapChanged) {
			visibleTilesStale = true;
		}

		foreach (LooseView looseView in looseViews) {
			switch (looseView.redrawPolicy) {
				case LooseView.RedrawPolicy.WhenMapChanges:
				case LooseView.RedrawPolicy.WhenContentChanges:
					if (hidden) {
						break;
					}
					bool contentChanged = looseView.redrawPolicy == LooseView.RedrawPolicy.WhenContentChanges
						&& looseView.queuedContentVersion != contentVersion;
					if (looseView.TakeRedrawRequest() || mapChanged || contentChanged) {
						looseView.queuedContentVersion = contentVersion;
						looseView.QueueRedrawUnlessBlank();
					}
					break;
				case LooseView.RedrawPolicy.WhenUnitsChange:
					if (hidden) {
						break;
					}
					unitsDrawnSeconds += delta;
					UnitsDrawnFor drawnFor = new(drawnRegion, UnitLayer.IndicatorZoom(cameraZoom), game.CurrentlySelectedUnit, game.controller,
						game.tileInfo, unitLayer.visible, game.animationController.animTracker.hasPlayingAnimations());
					if (looseView.TakeRedrawRequest() || mapChanged || unitsChanged || drawnFor.animating || drawnFor != unitsDrawnFor
						|| unitsDrawnSeconds >= UnitsRefreshSeconds || !unitLayer.UpdateLoopingAnimations(looseView)) {
						unitsDrawnFor = drawnFor;
						unitsDrawnSeconds = 0;
						looseView.QueueRedraw();
					}
					break;
				case LooseView.RedrawPolicy.WhileTargeting:
					// Redraw once more after targeting ends to clear what was drawn.
					bool targeting = !hidden && (game.gotoInfo != null || game.bombardInfo != null);
					if (targeting || targetingViewDrawn) {
						looseView.QueueRedraw();
					}
					targetingViewDrawn = targeting;
					break;
				default:
					// Units animate, so they're drawn every frame.
					looseView.QueueRedraw();
					break;
			}
		}

		if (!hidden) {
			mapChanged = false;
			unitsChanged = false;
		}
	}

	// Makes sure the drawn region covers the screen, moving it and queueing redraws if not. The drawn region is bigger than the screen so
	// that small camera moves don't require redrawing, but not so much bigger that it's wasteful.
	private void UpdateDrawnRegion() {
		VisibleRegion visible = getVisibleRegion();
		int padX = Math.Max(4, (visible.lowerRightX - visible.upperLeftX) / 16 * 2);
		int padY = Math.Max(4, (visible.lowerRightY - visible.upperLeftY) / 16 * 2);
		// Along a wrapping edge, the camera is moved back by whole copies of the map to stay in the drawn region (see
		// WrapCameraIntoDrawnRegion), so padding by half the map is enough for the camera to go anywhere without a redraw. Zoomed far out,
		// more would draw the same tiles again just off screen. The padding stays even, so the tiles' rows stay staggered the same way.
		if (wrapHorizontally && mapWidth > 0) {
			padX = Math.Min(padX, Math.Max(4, (mapWidth / 2 + 1) & ~1));
		}
		if (wrapVertically && mapHeight > 0) {
			padY = Math.Min(padY, Math.Max(4, (mapHeight / 2 + 1) & ~1));
		}

		if (hasDrawnRegion) {
			VisibleRegion drawn = drawnRegion;
			bool tooBig = (drawn.lowerRightX - drawn.upperLeftX) > (visible.lowerRightX - visible.upperLeftX) + 4 * padX
				|| (drawn.lowerRightY - drawn.upperLeftY) > (visible.lowerRightY - visible.upperLeftY) + 4 * padY;
			if (!tooBig && (drawn.Covers(visible) || WrapCameraIntoDrawnRegion(drawn, visible))) {
				return;
			}
		}

		drawnRegion = new VisibleRegion {
			upperLeftX = visible.upperLeftX - padX,
			upperLeftY = visible.upperLeftY - padY,
			lowerRightX = visible.lowerRightX + padX,
			lowerRightY = visible.lowerRightY + padY,
		};
		hasDrawnRegion = true;
		visibleTilesStale = true;

		foreach (LooseView looseView in looseViews) {
			if (looseView.redrawPolicy == LooseView.RedrawPolicy.WhenMapChanges
				|| looseView.redrawPolicy == LooseView.RedrawPolicy.WhenContentChanges) {
				looseView.QueueRedrawUnlessBlank();
			}
		}
	}

	// On a map that wraps, moves the camera by whole copies of the map, which shows exactly the same thing, if that puts the screen back in
	// the drawn region, so the map needn't be redrawn. Returns whether it did.
	private bool WrapCameraIntoDrawnRegion(VisibleRegion drawn, VisibleRegion visible) {
		int copiesX = 0, copiesY = 0;
		if (wrapHorizontally && mapWidth > 0) {
			float offset = (drawn.upperLeftX + drawn.lowerRightX - visible.upperLeftX - visible.lowerRightX) / 2f;
			copiesX = (int)Math.Round(offset / mapWidth);
		}
		if (wrapVertically && mapHeight > 0) {
			float offset = (drawn.upperLeftY + drawn.lowerRightY - visible.upperLeftY - visible.lowerRightY) / 2f;
			copiesY = (int)Math.Round(offset / mapHeight);
		}
		if (copiesX == 0 && copiesY == 0) {
			return false;
		}
		VisibleRegion moved = new() {
			upperLeftX = visible.upperLeftX + copiesX * mapWidth,
			upperLeftY = visible.upperLeftY + copiesY * mapHeight,
			lowerRightX = visible.lowerRightX + copiesX * mapWidth,
			lowerRightY = visible.lowerRightY + copiesY * mapHeight,
		};
		if (!drawn.Covers(moved)) {
			return false;
		}
		internalCameraLocation += cellSize * new Vector2(copiesX * mapWidth, copiesY * mapHeight) * cameraZoom;
		foreach (LooseView looseView in looseViews) {
			looseView.Position = -internalCameraLocation;
		}
		// The selection cursor is at the copy of the selected unit nearest the middle of the screen, which may now be another one.
		unitsChanged = true;
		// The rounding of the camera's new location could have moved the screen by a tile.
		return drawn.Covers(getVisibleRegion());
	}

	// Returns the tiles in the drawn region, collecting them if needed. If onScreen is given, the drawn region is first made to cover it.
	public List<VisibleTile> GetVisibleTiles(GameData gameData, VisibleRegion? onScreen) {
		if (!hasDrawnRegion || (onScreen.HasValue && !drawnRegion.Covers(onScreen.Value))) {
			UpdateDrawnRegion();
		}
		if (visibleTilesStale) {
			CollectVisibleTiles(gameData);
			visibleTilesStale = false;
		}
		return visibleTiles;
	}

	private void CollectVisibleTiles(GameData gD) {
		visibleTiles.Clear();

		TileKnowledge knowledge = ShowsWholeMap(gD) ? null : gD.GetUIControllerPlayer().tileKnowledge;
		VisibleRegion region = drawnRegion;
		for (int Y = region.upperLeftY; Y < region.lowerRightY; Y++) {
			if (gD.map.isRowAt(Y)) {
				for (int X = region.getRowStartX(Y); X < region.lowerRightX; X += 2) {
					Tile tile = gD.map.tileAt(X, Y);
					visibleTiles.Add(new VisibleTile {
						tile = tile,
						tileCenter = cellSize * new Vector2(X + 1, Y + 1),
						x = X,
						y = Y,
						known = tile != Tile.NONE && (knowledge == null || knowledge.isTileKnown(tile)),
					});
				}
			}
		}
	}

	// Returns the center of the tile, in map coordinates, at whichever of its wrapped positions is closest to the reference point.
	public Vector2 NearestTileCenter(Tile tile, Vector2 reference) {
		Vector2 center = cellSize * new Vector2(tile.XCoordinate + 1, tile.YCoordinate + 1);
		if (wrapHorizontally && mapWidth > 0) {
			float period = mapWidth * cellSize.X;
			center.X += Mathf.Round((reference.X - center.X) / period) * period;
		}
		if (wrapVertically && mapHeight > 0) {
			float period = mapHeight * cellSize.Y;
			center.Y += Mathf.Round((reference.Y - center.Y) / period) * period;
		}
		return center;
	}

	// The point at the middle of the screen, in map coordinates.
	public Vector2 CameraCenterInMap() {
		return (cameraLocation + getVisibleAreaSize() / 2) / cameraZoom;
	}

	// Returns the size in pixels of the area in which the map will be drawn. This is the viewport size or, if that's null, the window size.
	public Vector2 getVisibleAreaSize() {
		return GetViewport() != null ? GetViewportRect().Size : DisplayServer.WindowGetSize();
	}

	public VisibleRegion getVisibleRegion() {
		(int X0, int Y0) = tileCoordsInMainCamera(new Vector2(0, 0));
		Vector2 mapViewSize = new Vector2(2, 4) + getVisibleAreaSize() / scaledCellSize;
		return new VisibleRegion {
			upperLeftX = X0 - 2,
			upperLeftY = Y0 - 2,
			lowerRightX = X0 + (int)mapViewSize.X,
			lowerRightY = Y0 + (int)mapViewSize.Y
		};
	}

	// "center" is the screen location around which the zoom is centered, e.g., if center is (0, 0) the tile in the top left corner will be the
	// same after the zoom level is changed, and if center is screenSize/2, the tile in the center of the window won't change.
	public void setCameraZoom(float newScale, Vector2 center) {
		newScale = Math.Clamp(newScale, MIN_SCALE, MAX_SCALE);

		Vector2 v2NewZoom = new Vector2(newScale, newScale);
		Vector2 v2OldZoom = new Vector2(cameraZoom, cameraZoom);
		if (v2NewZoom != v2OldZoom) {
			internalCameraZoom = newScale;
			foreach (LooseView looseView in looseViews) {
				looseView.Scale = v2NewZoom;
			}
			setCameraLocation((v2NewZoom / v2OldZoom) * (cameraLocation + center) - center);
		}
	}

	// Zooms in or out centered on the middle of the screen
	public void setCameraZoomFromMiddle(float newScale) {
		setCameraZoom(newScale, getVisibleAreaSize() / 2);
	}

	public void moveCamera(Vector2 offset) {
		setCameraLocation(cameraLocation + offset);
	}

	public void setCameraLocation(Vector2 location) {
		// Prevent the camera from moving beyond an unwrapped edge of the map. One complication here is that the viewport might actually be
		// larger than the map (if we're zoomed far out) so in that case we must apply the constraint the other way around, i.e. constrain the
		// map to the viewport rather than the viewport to the map.
		Vector2 visAreaSize = getVisibleAreaSize();
		Vector2 mapPixelSize = new Vector2(cameraZoom, cameraZoom) * (new Vector2(cellSize.X * (mapWidth + 1), cellSize.Y * (mapHeight + 1)));
		if (!wrapHorizontally) {
			float leftLim, rightLim;
			{
				if (mapPixelSize.X >= visAreaSize.X) {
					leftLim = 0;
					rightLim = mapPixelSize.X - visAreaSize.X;
				} else {
					leftLim = mapPixelSize.X - visAreaSize.X;
					rightLim = 0;
				}
			}
			if (location.X < leftLim)
				location.X = leftLim;
			else if (location.X > rightLim)
				location.X = rightLim;
		}
		if (!wrapVertically) {
			// These margins allow the player to move the camera that far off those map edges so that the UI controls don't cover up the
			// map. TODO: These values should be read from the sizes of the UI elements instead of hardcoded.
			float topMargin = 70, bottomMargin = 140;
			float topLim, bottomLim;
			{
				if (mapPixelSize.Y >= visAreaSize.Y) {
					topLim = -topMargin;
					bottomLim = mapPixelSize.Y - visAreaSize.Y + bottomMargin;
				} else {
					topLim = mapPixelSize.Y - visAreaSize.Y;
					bottomLim = 0;
				}
			}
			if (location.Y < topLim)
				location.Y = topLim;
			else if (location.Y > bottomLim)
				location.Y = bottomLim;
		}

		internalCameraLocation = location;
		foreach (LooseView looseView in looseViews) {
			looseView.Position = -location;
		}

		// Draw any newly uncovered part of the map this frame, and keep the cities at their copies on screen.
		UpdateDrawnRegion();
		cityLayer?.PlaceScenes(this);
	}

	public Vector2 screenLocationOfTileCoords(int X, int Y, bool center = true) {
		// Add one to X & Y to get the tile center b/c in Civ 3 the tile at (X, Y) is a diamond centered on (x+1, y+1).
		Vector2 centeringOffset = center ? new Vector2(1, 1) : new Vector2(0, 0);

		var mapLoc = (new Vector2(X, Y) + centeringOffset) * cellSize;
		return mapLoc * cameraZoom - cameraLocation;
	}

	// Returns the location of the tile on the screen, if "center" is true returns the location of the tile center and otherwise returns the
	// upper left. Works even if the tile is off screen. On a wrapping map, it's the location of the copy of the tile nearest the middle of
	// the screen.
	public Vector2 screenLocationOfTile(Tile tile, bool center = true) {
		Vector2 mapLoc = NearestTileCenter(tile, CameraCenterInMap());
		if (!center) {
			mapLoc -= cellSize;
		}
		return mapLoc * cameraZoom - cameraLocation;
	}

	// Returns the virtual tile coordinates on screen at the given location. "Virtual" meaning the coordinates are unwrapped and there isn't
	// necessarily a tile there at all.
	public (int, int) tileCoordsInMainCamera(Vector2 screenLocation) {
		Vector2 mapLoc = (screenLocation + cameraLocation) / scaledCellSize;
		return tileCoordsForMapLocation(mapLoc);
	}

	public (int, int) tileCoordsForMapLocation(Vector2 mapLocation) {
		Vector2 intMapLoc = mapLocation.Floor();
		Vector2 fracMapLoc = mapLocation - intMapLoc;
		int X = (int)intMapLoc.X, Y = (int)intMapLoc.Y;
		bool evenColumn = X % 2 == 0, evenRow = Y % 2 == 0;
		if (evenColumn ^ evenRow) {
			if (fracMapLoc.Y > fracMapLoc.X)
				X -= 1;
			else
				Y -= 1;
		} else {
			if (fracMapLoc.Y < 1 - fracMapLoc.X) {
				X -= 1;
				Y -= 1;
			}
		}
		return (X, Y);
	}

	// The unit the map draws on top of the tile's stack, or null if none.
	public MapUnit DrawnUnitOn(GameData gameData, Tile tile) {
		return unitLayer == null ? null : unitLayer.DrawnUnitOn(unitLayerView, gameData, tile);
	}

	public Tile tileOnScreenAt(GameMap map, Vector2 screenLocation) {
		(int X, int Y) = tileCoordsInMainCamera(screenLocation);
		return map.tileAt(X, Y);
	}

	// Centers the camera on the tile. On a wrapping map, on the copy of the tile nearest the middle of the screen, so the camera doesn't jump
	// across the map (and the whole map isn't redrawn) to get to a copy that looks the same.
	public void centerCameraOnTile(Tile t) {
		var tileCenter = NearestTileCenter(t, CameraCenterInMap()) * cameraZoom;
		setCameraLocation(tileCenter - (float)0.5 * getVisibleAreaSize());
	}

	public void OnCenterCameraOnUnit() {
		MapUnit currentlySelectedUnit = game.CurrentlySelectedUnit;
		if (!MapUnit.IsMapUnitValid(currentlySelectedUnit))
			return;
		centerCameraOnTile(currentlySelectedUnit.location);
	}

	// Whether the tile, at any of its wrapped positions, is within margin tiles of the region.
	public bool IsTileNear(Tile tile, VisibleRegion region, int margin) {
		return IsNear(tile.XCoordinate, region.upperLeftX - margin, region.lowerRightX + margin, wrapHorizontally ? mapWidth : 0)
			&& IsNear(tile.YCoordinate, region.upperLeftY - margin, region.lowerRightY + margin, wrapVertically ? mapHeight : 0);
	}

	// Whether the coordinate, or one of its copies a multiple of period away (if period isn't zero), is in [low, high).
	private static bool IsNear(int coordinate, int low, int high, int period) {
		if (period <= 0) {
			return coordinate >= low && coordinate < high;
		}
		if (high - low >= period) {
			return true;
		}
		int offset = (coordinate - low) % period;
		if (offset < 0) {
			offset += period;
		}
		return offset < high - low;
	}

	// Notices changes the engine doesn't tell the UI about, as while the AI plays, to what the terrain, fog of war and city views show in
	// the drawn region, so that they're only redrawn when they may have changed. It watches what the UI's player knows of the map and of
	// technology (for resources), the turn, the number of cities, and the tiles recorded in the engine's TileChangeJournal (see
	// TileChanges): any change to a tile's terrain or its units' owners, to who owns it, and so on, records the tile. Most of the recorded
	// changes are units moving, which the terrain and fog don't show, so each tile's signature (see Signature) is kept and only a tile whose
	// signature changed counts. Changes that aren't recorded, like a road being finished, are left to the MapView's regular full redraws
	// while the engine is busy, and to the messages the engine sends.
	private sealed class MapChangeTracker {
		private GameData gameData;
		private Player player;
		private bool wholeMap;
		private int turn, knownTiles, knownTechs, cityCount;
		private long journalEpoch, journalPosition;

		// Each tile's signature when it was last recorded in the journal, by tile index; zero for tiles not seen yet.
		private long[] signatures = [];

		// Returns whether something the views show in the region may have changed since the last call.
		public bool Check(GameData gD, MapView mapView, VisibleRegion region) {
			bool wholeMap = mapView.ShowsWholeMap(gD);
			Player player = gD.observerMode ? null : gD.GetUIControllerPlayer();
			TileKnowledge knowledge = player?.tileKnowledge;
			int knownTiles = knowledge?.knownTiles.Count ?? 0;
			int knownTechs = player?.knownTechs.Count ?? 0;
			bool changed = gD != gameData || player != this.player || wholeMap != this.wholeMap || gD.turn != turn
				|| knownTiles != this.knownTiles || knownTechs != this.knownTechs || gD.cities.Count != cityCount;
			if (gD != gameData || signatures.Length != gD.map.tiles.Count) {
				signatures = new long[gD.map.tiles.Count];
			}
			gameData = gD;
			this.player = player;
			this.wholeMap = wholeMap;
			turn = gD.turn;
			this.knownTiles = knownTiles;
			this.knownTechs = knownTechs;
			cityCount = gD.cities.Count;

			long head = TileChanges.Head;
			if (TileChanges.Epoch != journalEpoch || head - journalPosition > TileChanges.Capacity || head < journalPosition) {
				// What changed was forgotten.
				journalEpoch = TileChanges.Epoch;
				journalPosition = head;
				return true;
			}

			// A unit sees up to two tiles away, or further with radar, so a unit just outside the region can change the fog inside it. The
			// terrain's art depends on the neighboring tiles too.
			int margin = 2 * Math.Max(2, gD.rules?.RadarTileVisibility ?? 0) + 2;
			for (long i = journalPosition; i < head; ++i) {
				Tile tile = TileChanges.At(i);
				if (tile == null || tile == Tile.NONE || tile.XCoordinate < 0 || tile.YCoordinate < 0
					|| tile.XCoordinate >= gD.map.numTilesWide || tile.YCoordinate >= gD.map.numTilesTall) {
					continue;
				}
				int index = gD.map.tileCoordsToIndex(tile.XCoordinate, tile.YCoordinate);
				if (index < 0 || index >= signatures.Length || gD.map.tiles[index] != tile) {
					// Not a tile of this game.
					continue;
				}
				long signature = Signature(tile, player);
				if (signature != signatures[index]) {
					signatures[index] = signature;
					changed = changed || mapView.IsTileNear(tile, region, margin);
				}
			}
			journalPosition = head;
			return changed;
		}

		// A value that changes whenever anything the terrain, fog of war and city views show of the tile changes in a way the engine records
		// in the journal: its terrain, resource, improvements, goody hut or barbarian camp, owner and city, and whether one of the UI's
		// player's units is on top there (the fog of war around a tile with a unit depends on whose unit it is, and on radar). Never zero.
		private static long Signature(Tile tile, Player player) {
			long signature = Mix(17, RuntimeHelpers.GetHashCode(tile.baseTerrainType));
			signature = Mix(signature, RuntimeHelpers.GetHashCode(tile.overlayTerrainType));
			signature = Mix(signature, RuntimeHelpers.GetHashCode(tile.Resource));
			signature = Mix(signature, RuntimeHelpers.GetHashCode(tile.OwningPlayer()));
			signature = Mix(signature, RuntimeHelpers.GetHashCode(tile.cityAtTile));
			// In any order, since the improvements may be listed in another order without having changed.
			long improvements = 0;
			foreach (TerrainImprovement improvement in tile.overlays.terrainImprovementByLayer.Values) {
				improvements += Mix(23, RuntimeHelpers.GetHashCode(improvement));
			}
			signature = Mix(signature, improvements);
			List<MapUnit> units = tile.unitsOnTile;
			bool ours = player != null && units.Count > 0 && units[0].owner == player;
			bool radar = false;
			if (ours) {
				foreach (MapUnit unit in units) {
					radar |= unit.unitType.hasRadar;
				}
			}
			signature = Mix(signature, (tile.hasGoodyHut ? 1 : 0) | (tile.hasBarbarianCamp ? 2 : 0) | (ours ? 4 : 0) | (radar ? 8 : 0)
				| (tile.isVictoryLocation ? 16 : 0));
			return signature == 0 ? 1 : signature;
		}

		private static long Mix(long hash, long value) {
			unchecked {
				hash ^= value * -7046029254386353131L; // 0x9E3779B97F4A7C15
				hash = (hash ^ (hash >> 29)) * -4658895280553007687L; // 0xBF58476D1CE4E5B9
				return hash ^ (hash >> 32);
			}
		}
	}
}

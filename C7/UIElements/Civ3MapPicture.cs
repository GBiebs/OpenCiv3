using System;
using System.Collections.Generic;
using C7GameData;
using Godot;

// Civ3's small picture of the world, as on its minimap and its replay: each
// tile a diamond (8x4 pixels) in its terrain's color, softened; a civ's land
// a crisp diamond of the terrain mixed with the civ's color; cities as
// white squares; land not yet explored black. The colors and the mix were
// measured on the user's screenshot of Civ3's replay against the same
// game's save (2026-11-11: 99.8% of tiles matched); the minimap's water and
// land colors in another Civ3 screenshot are the same.
public class Civ3MapPicture {
	public static readonly Dictionary<string, Color> TerrainColors = new() {
		{ "desert", Color.Color8(148, 148, 115) },
		{ "plains", Color.Color8(132, 107, 66) },
		{ "grassland", Color.Color8(90, 99, 49) },
		{ "tundra", Color.Color8(132, 140, 140) },
		{ "flood plain", Color.Color8(74, 49, 33) },
		{ "hills", Color.Color8(49, 74, 49) },
		{ "mountains", Color.Color8(123, 123, 123) },
		{ "forest", Color.Color8(57, 82, 49) },
		{ "jungle", Color.Color8(33, 66, 24) },
		{ "marsh", Color.Color8(74, 90, 66) },
		{ "volcano", Color.Color8(99, 66, 49) },
		{ "coast", Color.Color8(41, 107, 107) },
		{ "sea", Color.Color8(33, 90, 90) },
		{ "ocean", Color.Color8(24, 57, 66) },
	};
	private static readonly Color Ocean = TerrainColors["ocean"];
	// A civ's land: half the terrain under it plus 60% of the civ's color.
	private const float OwnedTerrain = 0.5f, OwnedCiv = 0.6f;

	public readonly int width, height;
	public readonly byte[] pixels;
	private readonly GameMap map;
	private readonly Image image;
	public readonly ImageTexture texture;

	// The softened terrain, kept until the terrain or what's explored changes.
	private byte[] terrain;
	private long terrainKey = long.MinValue;

	public Civ3MapPicture(GameMap map) {
		this.map = map;
		width = Math.Max(4, map.numTilesWide * 4);
		height = Math.Max(2, map.numTilesTall * 2);
		pixels = new byte[width * height * 4];
		image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
		texture = ImageTexture.CreateFromImage(image);
	}

	// A tile's middle in the picture (Civ3's places, a row lower than the
	// tile's own y).
	public static (int x, int y) Center(int x, int y) => (x * 4, y * 2 + 2);

	// Starts a picture from the softened terrain; `known` (null: all) leaves
	// the rest black, with crisp edges.
	public void Begin(Func<Tile, bool> known = null) {
		long key = 17;
		foreach (Tile t in map.tiles) {
			key = key * 31 + t.overlayTerrainType.GetHashCode() + (known == null || known(t) ? 1 : 0);
		}
		if (terrain == null || key != terrainKey) {
			terrain = Terrain(known);
			terrainKey = key;
		}
		Array.Copy(terrain, pixels, pixels.Length);
		if (known != null) {
			foreach (Tile t in map.tiles) {
				if (!known(t)) {
					(int cx, int cy) = Center(t.XCoordinate, t.YCoordinate);
					Diamond(cx, cy, (x, y) => Put(x, y, Colors.Black));
				}
			}
		}
	}

	// A civ's land on a tile.
	public void Own(Tile tile, Color civ) {
		(int cx, int cy) = Center(tile.XCoordinate, tile.YCoordinate);
		Diamond(cx, cy, (x, y) => {
			int at = (y * width + x) * 4;
			pixels[at] = Mix(terrain[at], civ.R8);
			pixels[at + 1] = Mix(terrain[at + 1], civ.G8);
			pixels[at + 2] = Mix(terrain[at + 2], civ.B8);
		});
	}

	// A city: a white square `size` pixels across.
	public void City(int tileX, int tileY, int size) {
		(int cx, int cy) = Center(tileX, tileY);
		for (int dy = -size / 2; dy < size - size / 2; dy++) {
			for (int dx = -size / 2; dx < size - size / 2; dx++) {
				Put(cx + dx, cy + dy, Colors.White);
			}
		}
	}

	public void Finish() {
		image.SetData(width, height, false, Image.Format.Rgba8, pixels);
		texture.Update(image);
	}

	private static byte Mix(byte terrain, int civ) => (byte)Math.Min(255, (int)Math.Round(OwnedTerrain * terrain + OwnedCiv * civ));

	// Each tile's color (unexplored ones black), then softened as Civ3's is.
	private byte[] Terrain(Func<Tile, bool> known) {
		Array.Clear(pixels);
		foreach (Tile t in map.tiles) {
			TerrainType type = t.baseTerrainType.isWater() ? t.baseTerrainType : t.overlayTerrainType;
			Color c = known != null && !known(t) ? Colors.Black
				: TerrainColors.GetValueOrDefault(type.Key, TerrainColors.GetValueOrDefault(t.baseTerrainType.Key, Ocean));
			(int cx, int cy) = Center(t.XCoordinate, t.YCoordinate);
			Diamond(cx, cy, (x, y) => Put(x, y, c));
		}
		// Whatever no tile covers (the edges) is like its neighbors' sea.
		Color edge = known == null ? Ocean : Colors.Black;
		for (int at = 0; at < pixels.Length; at += 4) {
			if (pixels[at + 3] == 0) {
				Put(at / 4 % width, at / 4 / width, edge);
			}
		}
		return Blur(pixels);
	}

	// A box blur 5 pixels wide and 3 high (a tile is 8x4).
	private byte[] Blur(byte[] source) {
		bool wrap = map.wrapHorizontally;
		byte[] across = new byte[source.Length];
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				for (int ch = 0; ch < 3; ch++) {
					int sum = 0;
					for (int d = -2; d <= 2; d++) {
						int sx = wrap ? ((x + d) % width + width) % width : Math.Clamp(x + d, 0, width - 1);
						sum += source[(y * width + sx) * 4 + ch];
					}
					across[(y * width + x) * 4 + ch] = (byte)(sum / 5);
				}
				across[(y * width + x) * 4 + 3] = 255;
			}
		}
		byte[] result = new byte[source.Length];
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				for (int ch = 0; ch < 3; ch++) {
					int sum = 0;
					for (int d = -1; d <= 1; d++) {
						sum += across[(Math.Clamp(y + d, 0, height - 1) * width + x) * 4 + ch];
					}
					result[(y * width + x) * 4 + ch] = (byte)(sum / 3);
				}
				result[(y * width + x) * 4 + 3] = 255;
			}
		}
		return result;
	}

	// The pixels of a tile's diamond, rows 2, 6, 6 and 2 wide, inside the picture.
	private void Diamond(int cx, int cy, Action<int, int> pixel) {
		for (int dy = -2; dy <= 1; dy++) {
			int half = dy is -2 or 1 ? 1 : 3;
			for (int dx = -half; dx < half; dx++) {
				int x = cx + dx, y = cy + dy;
				if (map.wrapHorizontally) {
					x = ((x % width) + width) % width;
				}
				if (x >= 0 && x < width && y >= 0 && y < height) {
					pixel(x, y);
				}
			}
		}
	}

	private void Put(int x, int y, Color c) {
		if (map.wrapHorizontally) {
			x = ((x % width) + width) % width;
		}
		if (x < 0 || x >= width || y < 0 || y >= height) {
			return;
		}
		int at = (y * width + x) * 4;
		pixels[at] = (byte)c.R8;
		pixels[at + 1] = (byte)c.G8;
		pixels[at + 2] = (byte)c.B8;
		pixels[at + 3] = 255;
	}
}

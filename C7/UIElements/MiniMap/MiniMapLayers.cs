using System.Collections.Generic;
using C7.Textures;
using C7GameData;
using Godot;

/// Works out each tile's minimap colour into a managed RGBA8 buffer. Each
/// tile owns the 4 bytes (r, g, b, a) at `offset`. Layers read back what the
/// previous layers wrote, the same way Image.GetPixel would.
public abstract class MiniMapLayer {
	public virtual void Configure(GameData gD) { }
	public virtual void DrawTile(byte[] pixels, int offset, Tile tile) { }

	public bool visible = true;

	// Stores a Color the way Godot's Image.SetPixel does for an RGBA8 image.
	protected static void SetPixel(byte[] pixels, int offset, Color color) {
		pixels[offset] = ToByte(color.R);
		pixels[offset + 1] = ToByte(color.G);
		pixels[offset + 2] = ToByte(color.B);
		pixels[offset + 3] = ToByte(color.A);
	}

	// Reads a Color the way Godot's Image.GetPixel does for an RGBA8 image.
	protected static Color GetPixel(byte[] pixels, int offset) {
		return new Color(pixels[offset] / 255f, pixels[offset + 1] / 255f, pixels[offset + 2] / 255f, pixels[offset + 3] / 255f);
	}

	private static byte ToByte(float channel) {
		return (byte)Mathf.Clamp(channel * 255.0f, 0, 255);
	}
}

public class BaseLandMiniLayer : MiniMapLayer {
	private static readonly Color LandColor = TerrainMiniLayer.TerrainColorMap["grassland"];

	public override void DrawTile(byte[] pixels, int offset, Tile tile) {
		if (visible && tile.IsLand())
			SetPixel(pixels, offset, LandColor);
	}
}

// Muted, earthy colours in the style of Civ3's minimap, so unclaimed land
// reads as a calm backdrop for the civ colours.
public class TerrainMiniLayer : MiniMapLayer {
	public static readonly Dictionary<string, Color> TerrainColorMap = new()
	{
		{ "desert",      Color.Color8(186, 166, 118) },
		{ "plains",      Color.Color8(138, 114, 70) },
		{ "grassland",   Color.Color8(92, 104, 52) },
		{ "tundra",      Color.Color8(150, 150, 134) },
		{ "flood plain", Color.Color8(112, 112, 56) },
		{ "hills",       Color.Color8(118, 104, 70) },
		{ "mountains",   Color.Color8(128, 128, 124) },
		{ "forest",      Color.Color8(60, 84, 48) },
		{ "jungle",      Color.Color8(52, 88, 56) },
		{ "marsh",       Color.Color8(72, 80, 56) },
		{ "volcano",     Color.Color8(94, 84, 80) },
		{ "coast",       Color.Color8(41, 107, 107) },
		{ "sea",         Color.Color8(33, 88, 94) },
		{ "ocean",       Color.Color8(25, 64, 74) },
	};

	// The colour of each terrain type, if it has one, so tiles don't look theirs up by name.
	private readonly Dictionary<TerrainType, (bool hasColor, Color color)> colors = new(ReferenceEqualityComparer.Instance);

	public override void Configure(GameData gD) {
		// Terrain types belong to a game.
		if (!ReferenceEquals(gD, configuredFor)) {
			configuredFor = gD;
			colors.Clear();
		}
	}
	private GameData configuredFor;

	public override void DrawTile(byte[] pixels, int offset, Tile tile) {
		if (!visible)
			return;
		TerrainType type = tile.overlayTerrainType;
		if (!colors.TryGetValue(type, out (bool hasColor, Color color) entry)) {
			entry.hasColor = TerrainColorMap.TryGetValue(type.Key, out entry.color);
			colors[type] = entry;
		}
		if (entry.hasColor)
			SetPixel(pixels, offset, entry.color);
	}
}

// Owned land is shown in its owner's colour, shaded a little by the terrain
// so the land keeps some texture without the colour getting muddy.
public class PlayerColorMiniLayer : MiniMapLayer {
	private static readonly Dictionary<string, float> TerrainShade = new()
	{
		{ "desert",      1.08f },
		{ "tundra",      1.08f },
		{ "plains",      1.0f },
		{ "flood plain", 1.0f },
		{ "grassland",   0.96f },
		{ "marsh",       0.92f },
		{ "hills",       0.9f },
		{ "forest",      0.87f },
		{ "jungle",      0.87f },
		{ "mountains",   0.84f },
		{ "volcano",     0.8f },
	};

	// Each player's colour, looked up once per redraw instead of once per
	// owned tile.
	private readonly Dictionary<Player, Color> civColors = new();

	// Each terrain type's shade, so tiles don't look theirs up by name.
	private readonly Dictionary<TerrainType, float> shades = new(ReferenceEqualityComparer.Instance);
	private GameData configuredFor;

	public override void Configure(GameData gD) {
		civColors.Clear();
		// Terrain types belong to a game.
		if (!ReferenceEquals(gD, configuredFor)) {
			configuredFor = gD;
			shades.Clear();
		}
	}

	public override void DrawTile(byte[] pixels, int offset, Tile tile) {
		if (!visible || !tile.IsLand())
			return;
		Player owner = tile.OwningPlayer();
		if (owner == null)
			return;
		if (!civColors.TryGetValue(owner, out Color civColor)) {
			civColor = TextureLoader.LoadColor(owner.GetPlayerColor());
			civColors[owner] = civColor;
		}
		TerrainType type = tile.overlayTerrainType;
		if (!shades.TryGetValue(type, out float shade)) {
			shade = TerrainShade.GetValueOrDefault(type.Key, 1f);
			shades[type] = shade;
		}
		SetPixel(pixels, offset, new Color(civColor.R * shade, civColor.G * shade, civColor.B * shade));
	}
}

public class FogOfWarMiniLayer : MiniMapLayer {
	private bool _observerMode;
	private TileKnowledge _tileKnowledge;

	public override void Configure(GameData gD) {
		_observerMode = gD.observerMode;
		// The scenario editor has no player at all, and sees the whole map.
		_tileKnowledge = gD.GetUIControllerPlayer()?.tileKnowledge;
	}

	public override void DrawTile(byte[] pixels, int offset, Tile tile) {
		if (visible && !_observerMode) {
			if (_tileKnowledge.borderTiles.Contains(tile)) {
				var currentColor = GetPixel(pixels, offset);
				var newColor = Colors.Black.Lerp(currentColor, 0.70f); // soften the edge of the known world
				SetPixel(pixels, offset, newColor);
			} else if (!_tileKnowledge.knownTiles.Contains(tile)) {
				SetPixel(pixels, offset, Colors.Black);
			}
		}
	}
}

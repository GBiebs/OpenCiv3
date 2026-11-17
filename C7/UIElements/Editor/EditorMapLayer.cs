using System.Collections.Generic;
using C7.Textures;
using C7GameData;
using Godot;

// What the scenario editor draws over the map, as Civ3's editor does: the
// starting locations (always; Civ3's ring with "Any Start"), and from the
// View menu the food and shield icons on the tiles, the tiles' owners and
// the number of units on each tile.
public class EditorMapLayer : LooseLayer {
	public bool showYields { get => yields; set => Set(ref yields, value); }
	public bool showOwners { get => owners; set => Set(ref owners, value); }
	public bool showUnitCounts { get => unitCounts; set => Set(ref unitCounts, value); }
	// The Fog of War tool's veil over the tiles still hidden at the start.
	public bool showFog { get => fog; set => Set(ref fog, value); }
	private bool yields, owners, unitCounts, fog;

	// The players' own starting locations and the fog painting.
	public C7Engine.Editor.MapPainter painter;
	private readonly Dictionary<Tile, Player> playerStarts = [];
	private static readonly Color Veil = new(0, 0, 0, 0.55f);

	private void Set(ref bool field, bool value) {
		if (field != value) {
			field = value;
			RequestRedraw();
		}
	}

	private readonly ImageTexture food = TextureLoader.TryLoad("icons.map_food");
	private readonly ImageTexture shield = TextureLoader.TryLoad("icons.map_shield");

	// The starting locations by tile, in the map's order, worked out once a
	// drawing.
	private readonly Dictionary<Tile, int> starts = [];

	private static readonly Color StartRing = new(0.1f, 0.15f, 0.85f);
	private static readonly Color StartLabel = new(0f, 0.5f, 0f);
	private static readonly Vector2[] Diamond = new Vector2[4];

	public override void onBeginDraw(LooseView looseView, GameData gameData) {
		starts.Clear();
		List<Tile> locations = gameData.map.startingLocations;
		for (int i = 0; i < locations.Count; ++i) {
			starts.TryAdd(locations[i], i);
		}
		playerStarts.Clear();
		if (painter != null) {
			foreach ((Tile tile, Player player) in painter.PlayerStarts()) {
				playerStarts[tile] = player;
			}
		}
	}

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		if (owners && Owner(tile) is Player owner) {
			Vector2 c = MapView.cellSize;
			Diamond[0] = tileCenter + new Vector2(0, -c.Y);
			Diamond[1] = tileCenter + new Vector2(c.X, 0);
			Diamond[2] = tileCenter + new Vector2(0, c.Y);
			Diamond[3] = tileCenter + new Vector2(-c.X, 0);
			looseView.DrawColoredPolygon(Diamond, new Color(TextureLoader.LoadColor(owner.GetPlayerColor()), 0.45f));
		}
		if (fog && painter != null && !painter.IsRevealed(tile)) {
			Vector2 c = MapView.cellSize;
			Diamond[0] = tileCenter + new Vector2(0, -c.Y);
			Diamond[1] = tileCenter + new Vector2(c.X, 0);
			Diamond[2] = tileCenter + new Vector2(0, c.Y);
			Diamond[3] = tileCenter + new Vector2(-c.X, 0);
			looseView.DrawColoredPolygon(Diamond, Veil);
		}
		if (yields) {
			DrawYields(looseView, tile, tileCenter);
		}
		if (playerStarts.TryGetValue(tile, out Player starter) && !looseView.mapView.bareMap) {
			Color color = TextureLoader.LoadColor(starter.GetPlayerColor());
			DrawStart(looseView, tileCenter, color, $"{C7Engine.Editor.ScenarioPlayers.NameOf(starter)} Start", color);
		}
		// Clean map hides starting locations with the other man-made things
		// (Civ3's help for View > Clean Map).
		if (starts.ContainsKey(tile) && !looseView.mapView.bareMap) {
			DrawStart(looseView, tileCenter, StartRing, "Any Start", StartLabel);
		}
		if (unitCounts && tile.unitsOnTile.Count > 0) {
			Label(looseView, tileCenter + new Vector2(28, -14), tile.unitsOnTile.Count.ToString(), new Color(0, 0, 0, 0.7f), Colors.White);
		}
	}

	// Who owns the tile: its territory's player, else what stands on it (a
	// city, a colony or outpost, units).
	private static Player Owner(Tile tile) {
		return tile.OwningPlayer() ?? tile.cityAtTile?.owner ?? tile.holdingOwner
			?? (tile.unitsOnTile.Count > 0 ? tile.unitsOnTile[0].owner : null);
	}

	// The tile's food then shields, a row of icons under its middle, as the
	// game shows a city's tiles (no player: every resource counts).
	private void DrawYields(LooseView looseView, Tile tile, Vector2 tileCenter) {
		if (food == null || shield == null) {
			return;
		}
		int foodCount = tile.FoodYield((Player)null).yield, shieldCount = tile.ProductionYield((Player)null).yield;
		int count = foodCount + shieldCount;
		if (count == 0) {
			return;
		}
		// Smaller than the city screen's, so the tile shows through.
		Vector2 size = food.GetSize() * 0.7f;
		float step = Mathf.Min(size.X + 1, (MapView.cellSize.X * 1.4f - size.X) / Mathf.Max(1, count - 1));
		float width = step * (count - 1) + size.X;
		Vector2 at = tileCenter + new Vector2(-width / 2, -size.Y / 2);
		for (int i = 0; i < count; ++i) {
			looseView.DrawTextureRect(i < foodCount ? food : shield, new Rect2(at + new Vector2(step * i, 0), size), tile: false);
		}
	}

	// Civ3's mark for a starting location no player was given: a blue ring
	// with "Any Start" under it.
	private static void DrawStart(LooseView looseView, Vector2 tileCenter, Color ringColor, string text, Color labelColor) {
		Vector2[] ring = new Vector2[33];
		for (int i = 0; i < ring.Length; ++i) {
			float a = Mathf.Tau * i / (ring.Length - 1);
			ring[i] = tileCenter + new Vector2(Mathf.Cos(a) * 42, Mathf.Sin(a) * 21);
		}
		looseView.DrawPolyline(ring, ringColor, 3, true);
		looseView.DrawLine(tileCenter + new Vector2(0, -21), tileCenter + new Vector2(0, -13), ringColor, 2);
		looseView.DrawLine(tileCenter + new Vector2(0, 13), tileCenter + new Vector2(0, 21), ringColor, 2);
		Label(looseView, tileCenter + new Vector2(0, 32), text, labelColor, labelColor.Luminance > 0.5f ? Colors.Black : Colors.White);
	}

	private static void Label(LooseView looseView, Vector2 center, string text, Color back, Color ink) {
		Font font = ThemeDB.FallbackFont;
		const int size = 14;
		Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
		Rect2 box = new(center - new Vector2(extent.X / 2 + 3, extent.Y / 2), extent + new Vector2(6, 0));
		looseView.DrawRect(box, back);
		looseView.DrawString(font, box.Position + new Vector2(3, font.GetAscent(size)), text, HorizontalAlignment.Left, -1, size, ink);
	}
}

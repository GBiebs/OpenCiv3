using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7.Textures;
using C7GameData;
using Godot;

// Civ3's editor's minimap: a small window at the top right, "Minimap -
// Terrain", whose View menu shows the terrain, the territory or the tiles'
// owners, with the starting locations marked by a small x. A click or drag
// on it moves the view (Civ3's help). Built from the game's minimap layers;
// the game's own minimap, in its HUD frame, is hidden while editing.
public partial class EditorMinimap : PanelContainer {
	public enum View { Terrain, Territory, Owners }

	private readonly MapView mapView;
	private readonly Action<PopupMenu> styleMenu;
	private View view = View.Terrain;

	private Label title;
	private TextureRect image;
	private MiniMapBoundsOverlay bounds;
	private ImageTexture texture;
	private StyleBoxFlat frame;

	// The image's size: Civ3's minimap is about this big, whatever the map.
	private static readonly Vector2I ImageSize = new(240, 120);

	private readonly BaseLandMiniLayer land = new();
	private readonly TerrainMiniLayer terrain = new();
	private readonly PlayerColorMiniLayer territory = new();
	private readonly OwnerMiniLayer owners = new();

	private byte[] tileColors = [], pixels = [];
	private int[] pixelTiles = [];
	private GameMap pixelTilesMap;
	private long drawnAt = -1;
	private bool dirty = true;
	private double sinceCheck;
	private bool dragging;

	public bool Focused { get; private set; }

	// The players' own starting locations, marked as the map's are.
	public Func<IEnumerable<Tile>> playerStarts;

	public EditorMinimap(MapView mapView, Action<PopupMenu> styleMenu) {
		this.mapView = mapView;
		this.styleMenu = styleMenu;
		Name = "Minimap";
	}

	public View ShownView {
		get => view;
		set {
			view = value;
			title.Text = $"Minimap - {value}";
			dirty = true;
		}
	}

	public override void _Ready() {
		MouseFilter = MouseFilterEnum.Stop;
		frame = SetupStyle.Box(new Color(0.99f, 0.96f, 0.88f), SetupStyle.Brown, 1, 3);
		AddThemeStyleboxOverride("panel", frame);
		SetAnchorsPreset(LayoutPreset.TopRight);

		VBoxContainer box = new();
		box.AddThemeConstantOverride("separation", 2);
		AddChild(box);

		HBoxContainer bar = new();
		title = new Label { Text = "Minimap - Terrain", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		title.AddThemeColorOverride("font_color", SetupStyle.Ink);
		bar.AddChild(title);
		Button close = new() { Text = "x", Flat = true, TooltipText = "Hide the minimap (M)" };
		close.AddThemeColorOverride("font_color", SetupStyle.Ink);
		close.Pressed += () => Visible = false;
		bar.AddChild(close);
		box.AddChild(bar);

		Godot.MenuButton viewMenu = new() { Text = "View", Flat = true };
		viewMenu.AddThemeColorOverride("font_color", SetupStyle.Ink);
		PopupMenu popup = viewMenu.GetPopup();
		foreach (View v in Enum.GetValues<View>()) {
			popup.AddRadioCheckItem(v.ToString(), (int)v);
		}
		styleMenu(popup);
		popup.AboutToPopup += () => {
			foreach (View v in Enum.GetValues<View>()) {
				popup.SetItemChecked(popup.GetItemIndex((int)v), v == view);
			}
		};
		popup.IdPressed += id => ShownView = (View)(int)id;
		HBoxContainer menuRow = new();
		menuRow.AddChild(viewMenu);
		box.AddChild(menuRow);

		image = new TextureRect {
			CustomMinimumSize = ImageSize,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Stop,
		};
		image.GuiInput += OnImageInput;
		bounds = new MiniMapBoundsOverlay();
		bounds.SetAnchorsPreset(LayoutPreset.FullRect);
		image.AddChild(bounds);
		box.AddChild(image);
	}

	// Tab moves the focus between the minimap and the map (Civ3's); the
	// focused minimap has a darker frame.
	public void SetFocused(bool focused) {
		Focused = focused;
		frame.BorderColor = focused ? SetupStyle.Ink : SetupStyle.Brown;
		frame.SetBorderWidthAll(focused ? 2 : 1);
	}

	public override void _Process(double delta) {
		// Keep to the top right, under the toolbars.
		Position = new Vector2(GetParentAreaSize().X - Size.X - 8, 96);
		GameData gameData = EngineStorage.gameData;
		if (gameData?.map == null || !Visible) {
			return;
		}
		bounds.SetBounds(gameData.map, mapView.getVisibleRegion());
		// What the minimap shows changes only through the tile journal (the
		// brushes record every tile they change); looked at a few times a
		// second, which is plenty for a picture this small.
		sinceCheck += delta;
		if (dirty || (sinceCheck >= 0.2 && TileChanges.Head != drawnAt)) {
			sinceCheck = 0;
			Redraw(gameData);
		}
	}

	private void Redraw(GameData gameData) {
		GameMap map = gameData.map;
		dirty = false;
		drawnAt = TileChanges.Head;
		int width = ImageSize.X, height = ImageSize.Y;
		if (pixelTilesMap != map || pixelTiles.Length != width * height) {
			pixelTilesMap = map;
			pixelTiles = new int[width * height];
			for (int py = 0; py < height; ++py) {
				for (int px = 0; px < width; ++px) {
					Tile tile = MiniMap.TileShownAt(map, (px + 0.5f) / width * map.numTilesWide, (py + 0.5f) / height * map.numTilesTall);
					pixelTiles[py * width + px] = tile == Tile.NONE ? -1 : map.tileCoordsToIndex(tile.XCoordinate, tile.YCoordinate);
				}
			}
		}
		territory.visible = view == View.Territory;
		owners.visible = view == View.Owners;
		MiniMapLayer[] layers = [land, terrain, territory, owners];
		foreach (MiniMapLayer layer in layers) {
			layer.Configure(gameData);
		}
		if (tileColors.Length != map.tiles.Count * 4) {
			tileColors = new byte[map.tiles.Count * 4];
		}
		Array.Clear(tileColors);
		for (int i = 0; i < map.tiles.Count; ++i) {
			foreach (MiniMapLayer layer in layers) {
				layer.DrawTile(tileColors, i * 4, map.tiles[i]);
			}
		}
		if (pixels.Length != width * height * 4) {
			pixels = new byte[width * height * 4];
		}
		for (int p = 0; p < pixelTiles.Length; ++p) {
			int t = pixelTiles[p];
			if (t < 0) {
				pixels[p * 4] = pixels[p * 4 + 1] = pixels[p * 4 + 2] = 0;
				pixels[p * 4 + 3] = 255;
			} else {
				Buffer.BlockCopy(tileColors, t * 4, pixels, p * 4, 4);
			}
		}
		foreach (Tile start in map.startingLocations.Concat(playerStarts?.Invoke() ?? [])) {
			MarkX(start, map, width, height);
		}
		foreach (City city in gameData.cities) {
			MarkCity(city.location, map, width, height);
		}
		Image picture = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
		if (texture == null) {
			texture = ImageTexture.CreateFromImage(picture);
			image.Texture = texture;
		} else {
			texture.Update(picture);
		}
	}

	private static (int, int) PixelOf(Tile tile, GameMap map, int width, int height) {
		return (Mathf.RoundToInt((tile.XCoordinate + 1) * (float)width / map.numTilesWide),
			Mathf.RoundToInt((tile.YCoordinate + 1) * (float)height / map.numTilesTall));
	}

	private void Plot(int x, int y, int width, int height, byte value) {
		if (x >= 0 && x < width && y >= 0 && y < height) {
			int o = (y * width + x) * 4;
			pixels[o] = pixels[o + 1] = pixels[o + 2] = value;
			pixels[o + 3] = 255;
		}
	}

	// A starting location: a small black x (Civ3 1.31's "display of starting
	// locations on Mini-Map").
	private void MarkX(Tile tile, GameMap map, int width, int height) {
		(int x, int y) = PixelOf(tile, map, width, height);
		for (int d = -2; d <= 2; ++d) {
			Plot(x + d, y + d, width, height, 0);
			Plot(x + d, y - d, width, height, 0);
		}
	}

	private void MarkCity(Tile tile, GameMap map, int width, int height) {
		(int x, int y) = PixelOf(tile, map, width, height);
		for (int dy = -1; dy <= 1; ++dy) {
			for (int dx = -1; dx <= 1; ++dx) {
				Plot(x + dx, y + dy, width, height, 255);
			}
		}
	}

	// A click, or a drag, centres the view on the tile under the mouse.
	private void OnImageInput(InputEvent @event) {
		switch (@event) {
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
				dragging = button.Pressed;
				if (button.Pressed) {
					CenterAt(button.Position);
				}
				image.AcceptEvent();
				break;
			case InputEventMouseMotion motion when dragging:
				CenterAt(motion.Position);
				image.AcceptEvent();
				break;
		}
	}

	private void CenterAt(Vector2 position) {
		GameMap map = EngineStorage.gameData?.map;
		if (map == null) {
			return;
		}
		Vector2 at = (position / image.Size).Clamp(Vector2.Zero, Vector2.One) * new Vector2(map.numTilesWide, map.numTilesTall);
		Tile tile = MiniMap.TileShownAt(map, at.X, at.Y);
		if (tile != Tile.NONE) {
			mapView.centerCameraOnTile(tile);
		}
	}
}

// The Owners view: each tile in the colour of whoever owns it or what's on
// it (a city, a colony or outpost, units), shaded by nothing.
public class OwnerMiniLayer : MiniMapLayer {
	private readonly Dictionary<Player, Color> colors = [];

	public override void Configure(GameData gD) {
		colors.Clear();
	}

	public override void DrawTile(byte[] pixels, int offset, Tile tile) {
		if (!visible) {
			return;
		}
		Player owner = tile.OwningPlayer() ?? tile.cityAtTile?.owner ?? tile.holdingOwner
			?? (tile.unitsOnTile.Count > 0 ? tile.unitsOnTile[0].owner : null);
		if (owner == null) {
			if (tile.IsLand()) {
				SetPixel(pixels, offset, new Color(0.55f, 0.55f, 0.5f));
			}
			return;
		}
		if (!colors.TryGetValue(owner, out Color color)) {
			color = TextureLoader.LoadColor(owner.GetPlayerColor());
			colors[owner] = color;
		}
		SetPixel(pixels, offset, color);
	}
}

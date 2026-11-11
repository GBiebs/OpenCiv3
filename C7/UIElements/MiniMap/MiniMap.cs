using System;
using System.Linq;
using C7.Textures;
using C7Engine;
using C7GameData;
using Godot;
using Serilog;

// The minimap: Civ3's picture of the world (Civ3MapPicture, as on Civ3's
// minimap and replay: terrain colors softened, each civ's land in its
// color, cities as white squares, unexplored land black), and the view's
// bounds in white.
[GlobalClass]
[Tool]
public partial class MiniMap : Control {
	private ILogger log = LogManager.ForContext<MiniMap>();

	// How often the picture is redrawn (the bounds follow the view every frame).
	private const double RefreshSeconds = 0.5;
	// The bounds' pixels per tile, for a line a pixel thin.
	private const int BoundsScale = 2;
	// A city's square on the screen, in pixels.
	private const float CityPixels = 3.5f;

	private MapView mapView;

	private MiniMapFrame frame;
	private MiniMapControls controls;

	private Civ3MapPicture picture;
	private GameMap pictureMap;
	private double untilRefresh;
	private Image bounds;
	private ImageTexture boundsTexture;

	public MiniMap(MapView mapView) {
		this.mapView = mapView;
	}

	public override void _Ready() {
		frame = new MiniMapFrame(mapView);
		AddChild(frame);

		controls = new MiniMapControls();
		AddChild(controls);

		MouseFilter = MouseFilterEnum.Pass;
	}

	public override void _Process(double delta) {
		frame.SetViewportPosition();
		untilRefresh -= delta;

		EngineStorage.ReadGameData((GameData gD) => {
			GameMap map = gD.map;
			if (picture == null || pictureMap != map) {
				picture = new Civ3MapPicture(map);
				pictureMap = map;
				frame.ShowPicture(picture.texture);
				bounds = Image.CreateEmpty(map.numTilesWide * BoundsScale, Math.Max(1, map.numTilesTall / 2) * BoundsScale, false, Image.Format.Rgba8);
				boundsTexture = ImageTexture.CreateFromImage(bounds);
				frame.ShowBounds(boundsTexture);
				untilRefresh = 0;
			}
			if (untilRefresh <= 0) {
				untilRefresh = RefreshSeconds;
				Draw(gD);
			}

			// The view's bounds, on their own layer.
			if (mapView != null) {
				bounds.Fill(Colors.Transparent);
				controls.DrawBounds(bounds, map, mapView.getVisibleRegion(), BoundsScale);
				boundsTexture.Update(bounds);
			}
		});
	}

	private void Draw(GameData gD) {
		TileKnowledge knowledge = gD.observerMode ? null : EngineStorage.GetViewer()?.tileKnowledge;
		Func<Tile, bool> known = knowledge == null ? null : knowledge.knownTiles.Contains;
		picture.Begin(known);
		Color[] colors = gD.players.Select(p => TextureLoader.LoadColor(p.GetPlayerColor())).ToArray();
		float scale = frame.PictureSize.X / picture.width;
		int citySize = Math.Max(3, (int)Math.Round(CityPixels / scale));
		foreach (Tile t in gD.map.tiles) {
			if (known != null && !known(t)) {
				continue;
			}
			if (!t.baseTerrainType.isWater() && t.OwningPlayer() is Player owner) {
				int index = gD.players.IndexOf(owner);
				if (index >= 0 && index < colors.Length) {
					picture.Own(t, colors[index]);
				}
			}
		}
		foreach (City city in gD.cities) {
			if (known == null || known(city.location)) {
				picture.City(city.location.XCoordinate, city.location.YCoordinate, citySize);
			}
		}
		picture.Finish();
	}
}

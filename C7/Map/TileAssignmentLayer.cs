using System;
using System.Collections.Generic;
using C7GameData;
using Godot;

namespace C7.Map {
	public partial class TileAssignmentLayer : LooseLayer {
		ImageTexture foodTexture = TextureLoader.Load("icons.map_food");
		ImageTexture shieldTexture = TextureLoader.Load("icons.map_shield");
		ImageTexture wastedShieldTexture = TextureLoader.Load("icons.wasted_shield");
		ImageTexture goldTexture = TextureLoader.Load("icons.map_commerce");
		// What a government's tile penalty (despotism's) takes, in red as
		// Civ3 draws it: its red shield and coins, and, as its art has no
		// red food, a red copy of the food.
		ImageTexture lostShieldTexture = TextureLoader.Load("icons.penalty_shield");
		ImageTexture lostGoldTexture = TextureLoader.Load("icons.penalty_commerce");
		private ImageTexture lostFoodTexture;
		private ImageTexture LostFoodTexture => lostFoodTexture ??= Reddened(foodTexture);

		private const int tileWidth = 128;
		private const int tileHeight = 64;

		// The widest a tile's row of yield icons gets before they overlap.
		private const float MaxYieldRowWidth = tileWidth;
		// Like Civ3, icons sit a pixel apart so each one can be counted.
		private const float YieldIconGap = 1;
		// Tiles outside the city's radius are darkened, as in Civ3.
		private static readonly Color OutsideRadiusDim = new(0, 0, 0, 0.4f);

		// When non-null, the city whose tile assignments should be shown.
		public City city {
			get => shownCity;
			set {
				if (shownCity != value) {
					shownCity = value;
					RequestRedraw();
				}
			}
		}
		private City shownCity = null;

		public override bool IsBlank => shownCity == null;

		// Reused for every tile, rather than made anew for each.
		private readonly List<ImageTexture> rowIcons = new();
		private readonly Vector2[] dimPolygon = new Vector2[4];

		// What we know about the shown city's tiles, worked out again when the city or the map changes.
		private readonly HashSet<Tile> workableTiles = new();
		private readonly Dictionary<Tile, TileYields> yields = new();
		private City cachedCity = null;
		private int cachedContentVersion;

		private readonly struct TileYields {
			public readonly bool hasPollution;
			public readonly int food, foodPenalty, shields, shieldPenalty, gold, goldPenalty;

			public TileYields(Tile tile, City city) {
				hasPollution = tile.HasPollution();
				if (hasPollution) {
					food = foodPenalty = shields = shieldPenalty = gold = goldPenalty = 0;
					return;
				}
				Tile.Yield foodYield = tile.FoodYield(city);
				Tile.Yield shieldYield = tile.ProductionYield(city);
				Tile.Yield goldYield = tile.CommerceYield(city);
				food = foodYield.yield;
				foodPenalty = foodYield.penalty;
				shields = shieldYield.yield;
				shieldPenalty = shieldYield.penalty;
				gold = goldYield.yield;
				goldPenalty = goldYield.penalty;
			}
		}

		public override void onBeginDraw(LooseView looseView, GameData gameData) {
			if (city == null) {
				return;
			}

			int contentVersion = looseView.mapView.contentVersion;
			if (city == cachedCity && contentVersion == cachedContentVersion) {
				return;
			}
			cachedCity = city;
			cachedContentVersion = contentVersion;
			yields.Clear();

			workableTiles.Clear();
			foreach (Tile t in city.GetWorkableTiles()) {
				workableTiles.Add(t);
			}

			// Include the city center in the "workable" tiles to avoid having
			// a border drawn there.
			workableTiles.Add(city.location);
		}

		public override void onGameDataReplaced(GameData gameData) {
			cachedCity = null;
			workableTiles.Clear();
			yields.Clear();
		}

		public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
			City city = this.city;
			if (city == null) {
				return;
			}

			// Draw a nice border around our city's workable tiles.
			//
			// TODO: use the fog of war layer to highlight the BFC.
			if (workableTiles.Contains(tile)) {
				DrawWorkableTileBorder(looseView, tile, tileCenter);

				// If one of our workable tiles is worked by another city, draw
				// a rectangle around it.
				if (tile.personWorkingTile != null && tile.personWorkingTile.city != city) {
					DrawOccupiedTileSquare(looseView, tileCenter);
				}
			} else {
				DimTile(looseView, tileCenter);
				return;
			}

			// Only draw yields for our citizens.
			if (tile.personWorkingTile?.city != city && tile.cityAtTile != city) {
				return;
			}

			if (!yields.TryGetValue(tile, out TileYields tileYields)) {
				tileYields = new TileYields(tile, city);
				yields[tile] = tileYields;
			}

			if (tileYields.hasPollution) {
				looseView.DrawTexture(wastedShieldTexture,
					tileCenter + new Vector2(-wastedShieldTexture.GetSize().X / 2, -15));
				return;
			}

			// The yield in one row: food, then shields, then commerce, each
			// followed by what a penalty (e.g. despotism's) takes of it, in
			// red. Like Civ3, a long row squeezes its icons together to stay
			// within the tile.
			List<ImageTexture> icons = rowIcons;
			icons.Clear();
			AddIcons(icons, foodTexture, tileYields.food);
			AddIcons(icons, LostFoodTexture, tileYields.foodPenalty);
			AddIcons(icons, shieldTexture, tileYields.shields);
			AddIcons(icons, lostShieldTexture, tileYields.shieldPenalty);
			AddIcons(icons, goldTexture, tileYields.gold);
			AddIcons(icons, lostGoldTexture, tileYields.goldPenalty);
			DrawYieldRow(looseView, icons, tileCenter, Colors.White);
		}

		// A red copy of an icon, as Civ3's red shield and coins are of its
		// shield and coins.
		private static ImageTexture Reddened(ImageTexture texture) {
			Image image = texture.GetImage();
			image.Convert(Image.Format.Rgba8);
			for (int y = 0; y < image.GetHeight(); ++y) {
				for (int x = 0; x < image.GetWidth(); ++x) {
					Color color = image.GetPixel(x, y);
					if (color.A == 0) {
						continue;
					}
					float light = color.Luminance;
					float highlight = 0.1f + 0.6f * Math.Max(0, light - 0.55f);
					image.SetPixel(x, y, new Color(Math.Min(1, 0.27f + 0.9f * light), highlight, highlight, color.A));
				}
			}
			ImageTexture red = ImageTexture.CreateFromImage(image);
			// (A remade icon has more pixels than it's drawn with.)
			red.SetSizeOverride((Vector2I)texture.GetSize());
			return red;
		}

		private static void AddIcons(List<ImageTexture> icons, ImageTexture texture, int count) {
			for (int i = 0; i < count; i++) {
				icons.Add(texture);
			}
		}

		// Draws a row of icons centered on rowCenter and returns its width.
		private static float DrawYieldRow(LooseView looseView, List<ImageTexture> icons, Vector2 rowCenter, Color tint) {
			if (icons.Count == 0) {
				return 0;
			}
			// The distance from the first icon's left edge to the last one's.
			float naturalSpan = 0;
			for (int i = 0; i < icons.Count - 1; i++) {
				naturalSpan += icons[i].GetWidth() + YieldIconGap;
			}
			float lastWidth = icons[^1].GetWidth();
			float squeeze = naturalSpan + lastWidth > MaxYieldRowWidth && naturalSpan > 0
				? Math.Max(0, MaxYieldRowWidth - lastWidth) / naturalSpan : 1f;
			float rowWidth = naturalSpan * squeeze + lastWidth;

			float x = -rowWidth / 2;
			foreach (ImageTexture texture in icons) {
				looseView.DrawTexture(texture, (rowCenter + new Vector2(x, -texture.GetHeight() / 2f)).Round(), tint);
				x += (texture.GetWidth() + YieldIconGap) * squeeze;
			}
			return rowWidth;
		}

		private void DimTile(LooseView looseView, Vector2 tileCenter) {
			// Godot copies the points, so the array can be reused.
			dimPolygon[0] = tileCenter + new Vector2(-tileWidth / 2, 0);
			dimPolygon[1] = tileCenter + new Vector2(0, -tileHeight / 2);
			dimPolygon[2] = tileCenter + new Vector2(tileWidth / 2, 0);
			dimPolygon[3] = tileCenter + new Vector2(0, tileHeight / 2);
			looseView.DrawColoredPolygon(dimPolygon, OutsideRadiusDim);
		}

		private void DrawWorkableTileBorder(LooseView looseView, Tile tile, Vector2 tileCenter) {
			if (!workableTiles.Contains(tile.neighbors[TileDirection.NORTHWEST])) {
				looseView.DrawLine(tileCenter + new Vector2(-tileWidth / 2, 0),
									tileCenter + new Vector2(0, -tileHeight / 2),
									Colors.White, width: 2);
			}

			if (!workableTiles.Contains(tile.neighbors[TileDirection.NORTHEAST])) {
				looseView.DrawLine(tileCenter + new Vector2(tileWidth / 2, 0),
									tileCenter + new Vector2(0, -tileHeight / 2),
									Colors.White, width: 2);
			}

			if (!workableTiles.Contains(tile.neighbors[TileDirection.SOUTHWEST])) {
				looseView.DrawLine(tileCenter + new Vector2(-tileWidth / 2, 0),
									tileCenter + new Vector2(0, tileHeight / 2),
									Colors.White, width: 2);
			}

			if (!workableTiles.Contains(tile.neighbors[TileDirection.SOUTHEAST])) {
				looseView.DrawLine(tileCenter + new Vector2(tileWidth / 2, 0),
									tileCenter + new Vector2(0, tileHeight / 2),
									Colors.White, width: 2);
			}
		}

		private void DrawOccupiedTileSquare(LooseView looseView, Vector2 tileCenter) {
			// Thick brown outline.
			{
				int lineWidth = 4;
				int width = tileWidth - lineWidth * 4;
				int height = tileHeight - lineWidth * 2;

				Color brown = new Color(166.0f/256, 116.0f/256, 87.0f/256);
				DrawSquare(looseView, tileCenter, brown, lineWidth, width, height);
			}

			// Black borders of the brown outline.
			{
				int lineWidth = 1;
				int width = tileWidth - 4;
				int height = tileHeight - 2;

				DrawSquare(looseView, tileCenter, Colors.Black, lineWidth, width, height);
			}
			{
				int lineWidth = 1;
				int width = tileWidth - 20;
				int height = tileHeight - 10;

				DrawSquare(looseView, tileCenter, Colors.Black, lineWidth, width, height);
			}
		}

		private void DrawSquare(LooseView looseView, Vector2 tileCenter, Color color, int lineWidth, int tileWidth, int tileHeight) {
			looseView.DrawLine(tileCenter + new Vector2(-tileWidth / 2, 0),
								tileCenter + new Vector2(0, -tileHeight / 2),
								color, lineWidth);
			looseView.DrawLine(tileCenter + new Vector2(tileWidth / 2, 0),
								tileCenter + new Vector2(0, -tileHeight / 2),
								color, lineWidth);
			looseView.DrawLine(tileCenter + new Vector2(-tileWidth / 2, 0),
								tileCenter + new Vector2(0, tileHeight / 2),
								color, lineWidth);
			looseView.DrawLine(tileCenter + new Vector2(tileWidth / 2, 0),
								tileCenter + new Vector2(0, tileHeight / 2),
								color, lineWidth);
		}
	}
}

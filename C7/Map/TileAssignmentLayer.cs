using System;
using System.Linq;
using System.Collections.Generic;
using C7GameData;
using Godot;

namespace C7.Map {
	public partial class TileAssignmentLayer : LooseLayer {
		// Civ3's small yield icons, and in red what the despotism penalty
		// takes (Civ3's small red coins; its art has no small red shield or
		// food, so those are made red here). Pollution shows a red shield.
		ImageTexture foodTexture = TextureLoader.Load("icons.tile_food");
		ImageTexture shieldTexture = TextureLoader.Load("icons.tile_shield");
		ImageTexture goldTexture = TextureLoader.Load("icons.tile_commerce");
		ImageTexture lostGoldTexture = TextureLoader.Load("icons.tile_lost_commerce");
		private ImageTexture lostFoodTexture;
		private ImageTexture LostFoodTexture => lostFoodTexture ??= Reddened(foodTexture);
		private ImageTexture lostShieldTexture;
		private ImageTexture LostShieldTexture => lostShieldTexture ??= Reddened(shieldTexture);

		private const int tileWidth = 128;
		private const int tileHeight = 64;
		// The width a tile's row of yields may take (map pixels).
		private const int yieldRowWidth = tileWidth - 16;

		// When non-null, the city whose tile assignments should be shown.

		public City city = null;
		private HashSet<Tile> workableTiles;

		public override void onBeginDraw(LooseView looseView, GameData gameData) {
			if (city == null) {
				return;
			}
			workableTiles = city.GetWorkableTiles().ToHashSet();

			// Include the city center in the "workable" tiles to avoid having
			// a border drawn there.
			workableTiles.Add(city.location);
		}

		public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
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
			}

			// Only draw yields for our citizens.
			if (tile.personWorkingTile?.city != city && tile.cityAtTile != city) {
				return;
			}

			if (tile.HasPollution()) {
				DrawYieldRow(looseView, tileCenter, [LostShieldTexture]);
				return;
			}

			Tile.Yield food = tile.FoodYield(city);
			Tile.Yield shields = tile.ProductionYield(city);
			Tile.Yield gold = tile.CommerceYield(city);

			List<ImageTexture> icons = [
				.. Enumerable.Repeat(foodTexture, food.yield), .. Enumerable.Repeat(LostFoodTexture, food.penalty),
				.. Enumerable.Repeat(shieldTexture, shields.yield), .. Enumerable.Repeat(LostShieldTexture, shields.penalty),
				.. Enumerable.Repeat(goldTexture, gold.yield), .. Enumerable.Repeat(lostGoldTexture, gold.penalty),
			];
			DrawYieldRow(looseView, tileCenter, icons);
		}

		// One row of icons across the tile, squeezed together when they
		// wouldn't fit it. (The city screen always shows the map at its own
		// zoom, so they're the city info's size.)
		private static void DrawYieldRow(LooseView looseView, Vector2 tileCenter, List<ImageTexture> icons) {
			if (icons.Count == 0) {
				return;
			}
			float width = icons.Sum(icon => icon.GetWidth());
			float squeeze = icons.Count > 1 && width > yieldRowWidth ? (width - yieldRowWidth) / (icons.Count - 1) : 0;
			float x = -Math.Min(width, yieldRowWidth) / 2;
			foreach (ImageTexture icon in icons) {
				looseView.DrawTexture(icon, tileCenter + new Vector2(x, -icon.GetHeight() / 2));
				x += icon.GetWidth() - squeeze;
			}
		}

		// A red copy of an icon, as Civ3's red coins are of its coins.
		private static ImageTexture Reddened(ImageTexture texture) {
			Image image = texture.GetImage();
			for (int x = 0; x < image.GetWidth(); ++x) {
				for (int y = 0; y < image.GetHeight(); ++y) {
					Color color = image.GetPixel(x, y);
					if (color.A == 0) {
						continue;
					}
					float light = color.Luminance;
					float highlight = 0.1f + 0.6f * Math.Max(0, light - 0.55f);
					image.SetPixel(x, y, new Color(Math.Min(1, 0.27f + 0.9f * light), highlight, highlight, color.A));
				}
			}
			return ImageTexture.CreateFromImage(image);
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

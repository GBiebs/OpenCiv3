using System.Collections.Generic;
using C7GameData;
using Godot;

// The layer responsible for drawing the cursor and tile effects relating to bombardment.
public partial class BombardLayer : LooseLayer {
	private readonly ImageTexture bombardCursorTexture;
	private readonly ImageTexture bombardDenyCursorTexture;

	private TextureRect bombardCursorRect = null;
	private TextureRect bombardDenyCursorRect = null;

	private Color bombardRed = Color.Color8(200, 0, 0, 225);
	private float bombardGridLineWidth = (float)1.0;

	private Dictionary<string, List<Tile>> tileSquareCache = new();

	public BombardLayer() {
		bombardCursorTexture = TextureLoader.Load("ui.cursor.bombard");
		bombardDenyCursorTexture = TextureLoader.Load("ui.cursor.bombard_deny");
	}

	public void DrawBombardCursor() {
		Input.SetCustomMouseCursor(bombardCursorTexture, hotspot: bombardCursorTexture.Center());
	}
	public void DrawBombardDenyCursor() {
		Input.SetCustomMouseCursor(bombardDenyCursorTexture, hotspot: bombardDenyCursorTexture.Center());
	}

	public override void onBeginDraw(LooseView looseView, GameData gameData) {
		bombardCursorRect?.Hide();
		bombardDenyCursorRect?.Hide();
	}

	// The unit picking a target, its reach in tiles, the tile under the
	// mouse and whether it can be targeted: a bombard, or an air mission,
	// airdrop or airlift, which pick their targets the same way.
	private static (MapUnit unit, int range, Tile mouseTile, bool valid)? Targeting(Game game, GameData gameData) {
		if (game.airTargetInfo is AirTargetInfo air) {
			// CanTarget checks the range; re-basing and airlifts reach too far to draw.
			return (air.unit, air.GridRange(), air.mouseTile, air.CanTarget(gameData, air.mouseTile));
		}
		if (game.bombardInfo is BombardInfo bombard) {
			// CanBombardTile checks the range; an ICBM reaches anywhere.
			MapUnit unit = bombard.bombardingUnit;
			return (unit, unit.unitType.bombardRange, bombard.mouseTile, bombard.mouseTile != null && unit.CanBombardTile(bombard.mouseTile));
		}
		return null;
	}

	public override void drawObject(LooseView looseView, GameData gameData, Tile tile, Vector2 tileCenter) {
		if (Targeting(looseView.mapView.game, gameData) is not { } targeting || targeting.unit.location != tile)
			return;

		// Draw the grid of tiles in reach
		if (targeting.range > 0 || looseView.mapView.game.bombardInfo != null) {
			foreach (Tile bt in GetTileSquare(tile, targeting.range)) {
				drawBombardTile(looseView, TileCenter(bt));
			}
		}
	}

	// Choose one of two cursors depending on mouse tile hover, wherever the
	// unit is (planes reach beyond the screen).
	public override void onEndDraw(LooseView looseView, GameData gameData) {
		if (Targeting(looseView.mapView.game, gameData) is not { } targeting || targeting.mouseTile == null)
			return;

		if (targeting.valid) {
			DrawBombardCursor();
			drawTargetBombardTile(looseView, TileCenter(targeting.mouseTile));
		} else
			DrawBombardDenyCursor();
	}

	private List<Tile> GetTileSquare(Tile tile, int range) {
		var key = $"{tile.Id}_{range}";
		if (tileSquareCache.TryGetValue(key, out var square))
			return square;
		tileSquareCache[key] = tile.GetTilesWithinTileSquare(range);
		return tileSquareCache[key];
	}

	private static Vector2 TileCenter(Tile bt) {
		return MapView.cellSize * new Vector2(bt.XCoordinate + 1, bt.YCoordinate + 1);
	}

	private void drawBombardTile(LooseView looseView, Vector2 tileCenter) {
		var cS = MapView.cellSize;
		var left = tileCenter + new Vector2(-cS.X, 0);
		var top = tileCenter + new Vector2(0, -cS.Y);
		var right = tileCenter + new Vector2(cS.X, 0);
		var bottom = tileCenter + new Vector2(0, cS.Y);
		DrawSquare(looseView, left, top, right, bottom);
	}

	private void drawTargetBombardTile(LooseView looseView, Vector2 tileCenter) {
		var cS = MapView.cellSize;
		var inset = 10;
		var left = tileCenter + new Vector2(-cS.X + inset, 0);
		var top = tileCenter + new Vector2(0, -cS.Y + (inset/2f));
		var right = tileCenter + new Vector2(cS.X - inset, 0);
		var bottom = tileCenter + new Vector2(0, cS.Y - (inset/2f));
		DrawSquare(looseView, left, top, right, bottom);
	}

	private void DrawSquare(LooseView looseView, Vector2 left, Vector2 top, Vector2 right, Vector2 bottom) {
		looseView.DrawLine(left, top, bombardRed, bombardGridLineWidth);
		looseView.DrawLine(top, right, bombardRed, bombardGridLineWidth);
		looseView.DrawLine(left, bottom, bombardRed, bombardGridLineWidth);
		looseView.DrawLine(bottom, right, bombardRed, bombardGridLineWidth);
	}
}

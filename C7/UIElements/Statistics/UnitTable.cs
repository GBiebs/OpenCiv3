using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A table for the parchment screens: how many of each unit type each civ built.
/// One row per unit type, one column per civ (names slanted above), and a total
/// row. The highest count in each row is bold; the emphasized civ's column is
/// shaded. Scrolls with the mouse wheel when the rows don't fit.
///
/// Adapted from GBiebs/OpenCiv3.
/// </summary>
public partial class UnitTable : Control {
	public class Column {
		public string name;
		public Color color;
		public bool emphasized;
		public Dictionary<string, int> counts;
	}

	public List<string> rowNames = new();
	public List<Column> columns = new();
	public string totalLabel = "All units";
	public string emptyText = "No units built yet.";
	public int fontSize = 13;

	private static readonly Color Ink = LineChart.Ink;
	private static readonly Color Faint = new(0.24f, 0.16f, 0.08f, 0.40f);
	private static readonly Color Stripe = new(0.36f, 0.25f, 0.12f, 0.07f);
	private static readonly Color Shade = new(0.36f, 0.25f, 0.12f, 0.12f);
	private static readonly Color Rule = new(0.36f, 0.25f, 0.12f, 0.45f);

	private const float NameWidth = 150;
	private const float RowHeight = 21;
	private const float ScrollbarWidth = 6;
	// The slant of the civ names above the columns.
	private const float HeaderAngle = -50 * Mathf.Pi / 180;

	private int firstRow = 0;
	private int hoverRow = -1;

	public UnitTable() {
		// Stop, for the mouse wheel and the cell tooltips (_GetTooltip decides the
		// text; TooltipText only needs to be non-empty for Godot to ask).
		MouseFilter = MouseFilterEnum.Stop;
		TooltipText = " ";
		ClipContents = true;
		MouseExited += () => { hoverRow = -1; QueueRedraw(); };
	}

	private int Count(Column column, int row) => column.counts.GetValueOrDefault(rowNames[row]);

	private int Total(Column column) => column.counts.Values.Sum();

	// The slanted names need height, and the last one reaches past its column to the right.
	private float HeaderHeight => columns.Count == 0 ? 0 : Mathf.Sin(-HeaderAngle) * MaxNameWidth + 18;

	private float MaxNameWidth => columns.Count == 0 ? 0 : columns.Max(c => LineChart.BoldFont.GetStringSize(c.name, fontSize: fontSize - 1).X);

	private float ColumnWidth {
		get {
			float overhang = Mathf.Cos(HeaderAngle) * MaxNameWidth;
			return Math.Clamp((Size.X - NameWidth - ScrollbarWidth - overhang) / Math.Max(columns.Count, 1), 30, 72);
		}
	}

	private float BodyTop => HeaderHeight;

	private int VisibleRows => Math.Max(1, (int)((Size.Y - BodyTop - RowHeight - 6) / RowHeight));

	private int MaxFirstRow => Math.Max(0, rowNames.Count - VisibleRows);

	private float ColumnLeft(int c) => NameWidth + c * ColumnWidth;

	public override void _Draw() {
		Font regular = LineChart.RegularFont, bold = LineChart.BoldFont;
		if (rowNames.Count == 0 || columns.Count == 0) {
			DrawString(regular, new Vector2(0, Size.Y / 2), emptyText, HorizontalAlignment.Center, Size.X, fontSize + 1, Ink);
			return;
		}
		float columnWidth = ColumnWidth;
		float tableRight = ColumnLeft(columns.Count);
		int visible = Math.Min(VisibleRows, rowNames.Count - firstRow);
		float bodyBottom = BodyTop + visible * RowHeight;

		// Civ names, slanted up to the right from each column's middle, over a color swatch.
		for (int c = 0; c < columns.Count; c++) {
			Column column = columns[c];
			float middle = ColumnLeft(c) + columnWidth / 2;
			if (column.emphasized) {
				DrawRect(new Rect2(ColumnLeft(c), BodyTop - 14, columnWidth, bodyBottom + RowHeight + 6 - (BodyTop - 14)), Shade);
			}
			DrawRect(new Rect2(middle - 8, BodyTop - 11, 16, 8), column.color);
			DrawSetTransform(new Vector2(middle - 2, BodyTop - 16), HeaderAngle, Vector2.One);
			DrawString(column.emphasized ? bold : regular, Vector2.Zero, column.name, HorizontalAlignment.Left, -1, fontSize - 1, Ink);
			DrawSetTransform(Vector2.Zero, 0, Vector2.One);
		}
		DrawLine(new Vector2(0, BodyTop - 1), new Vector2(tableRight, BodyTop - 1), Rule, 1);

		for (int i = 0; i < visible; i++) {
			int row = firstRow + i;
			float top = BodyTop + i * RowHeight;
			if (row == hoverRow) {
				DrawRect(new Rect2(0, top, tableRight, RowHeight), Shade);
			} else if (row % 2 == 1) {
				DrawRect(new Rect2(0, top, tableRight, RowHeight), Stripe);
			}
			DrawRowText(rowNames[row], top, c => Count(columns[c], row));
		}

		// The total row stays put under the scrolling rows.
		float totalTop = bodyBottom + 4;
		DrawLine(new Vector2(0, totalTop - 2), new Vector2(tableRight, totalTop - 2), Rule, 1);
		DrawRowText(totalLabel, totalTop, c => Total(columns[c]), boldLabel: true);

		if (MaxFirstRow > 0) {
			float trackHeight = bodyBottom - BodyTop;
			float thumbHeight = Math.Max(16, trackHeight * VisibleRows / rowNames.Count);
			float thumbTop = BodyTop + (trackHeight - thumbHeight) * firstRow / MaxFirstRow;
			float x = tableRight + 4;
			DrawRect(new Rect2(x, BodyTop, ScrollbarWidth - 2, trackHeight), Stripe);
			DrawRect(new Rect2(x, thumbTop, ScrollbarWidth - 2, thumbHeight), Rule);
		}
	}

	// A row's label and its counts; the highest count is bold, zeros are a faint dash.
	private void DrawRowText(string label, float top, Func<int, int> count, bool boldLabel = false) {
		Font regular = LineChart.RegularFont, bold = LineChart.BoldFont;
		float baseline = top + RowHeight / 2 + fontSize * 0.35f;
		DrawString(boldLabel ? bold : regular, new Vector2(4, baseline), label, HorizontalAlignment.Left, NameWidth - 10, fontSize, Ink);
		int max = Enumerable.Range(0, columns.Count).Max(count);
		for (int c = 0; c < columns.Count; c++) {
			int n = count(c);
			bool best = n > 0 && n == max && columns.Count > 1;
			DrawString(best ? bold : regular, new Vector2(ColumnLeft(c), baseline), n > 0 ? n.ToString("N0") : "–",
				HorizontalAlignment.Center, ColumnWidth, fontSize, n > 0 ? Ink : Faint);
		}
	}

	// The unit row under a point, or -1 (the header and the total row aren't unit rows).
	private int RowAt(Vector2 at) {
		int i = (int)Math.Floor((at.Y - BodyTop) / RowHeight);
		return at.Y >= BodyTop && i < Math.Min(VisibleRows, rowNames.Count - firstRow) ? firstRow + i : -1;
	}

	private int ColumnAt(Vector2 at) {
		int c = (int)Math.Floor((at.X - NameWidth) / ColumnWidth);
		return at.X >= NameWidth && c < columns.Count ? c : -1;
	}

	public override void _GuiInput(InputEvent @event) {
		if (@event is InputEventMouseButton { Pressed: true } wheel && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) {
			int step = wheel.ButtonIndex == MouseButton.WheelUp ? -2 : 2;
			int scrolled = Math.Clamp(firstRow + step, 0, MaxFirstRow);
			if (scrolled != firstRow) {
				firstRow = scrolled;
				hoverRow = RowAt(wheel.Position);
				QueueRedraw();
			}
			AcceptEvent();
		} else if (@event is InputEventMouseMotion motion) {
			int row = RowAt(motion.Position);
			if (row != hoverRow) {
				hoverRow = row;
				QueueRedraw();
			}
		}
	}

	public override string _GetTooltip(Vector2 atPosition) {
		int row = RowAt(atPosition), c = ColumnAt(atPosition);
		if (row < 0 || c < 0) return "";
		int n = Count(columns[c], row);
		return $"{columns[c].name}: {n:N0} {rowNames[row]} built";
	}
}

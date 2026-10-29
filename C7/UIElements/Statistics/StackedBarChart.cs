using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Horizontal stacked bars for the parchment screens: one bar per row, split
/// into named parts, with an optional reference line (e.g. the average) and a
/// tooltip per row.
/// </summary>
public partial class StackedBarChart : Control {
	public class Row {
		public string label;
		public Color color;
		public bool emphasized;
		public List<double> parts;
		public string tooltip;
	}

	public List<string> partNames = new();
	public List<Color> partColors = new();
	public List<Row> rows = new();
	public double? referenceValue;
	public string referenceLabel = "";
	public Func<double, string> formatValue = v => v.ToString("0");
	public int fontSize = 13;

	private static readonly Color Ink = LineChart.Ink;
	private static readonly Color Faint = new(0.24f, 0.16f, 0.08f, 0.55f);

	private const float LegendHeight = 26;
	private const float LabelWidth = 112;
	private const float ValueWidth = 44;

	public StackedBarChart() {
		// Stop, so that rows can show tooltips (_GetTooltip decides the text;
		// TooltipText only needs to be non-empty for Godot to ask).
		MouseFilter = MouseFilterEnum.Stop;
		TooltipText = " ";
	}

	private float RowHeight => rows.Count == 0 ? 0 : Math.Min(36, (Size.Y - LegendHeight) / rows.Count);

	public override void _Draw() {
		Font regular = LineChart.RegularFont, bold = LineChart.BoldFont;

		// The parts' legend.
		float x = 0;
		for (int i = 0; i < partNames.Count; i++) {
			DrawRect(new Rect2(x, 6, 16, 10), partColors[i]);
			DrawString(regular, new Vector2(x + 22, 16), partNames[i], HorizontalAlignment.Left, -1, fontSize, Ink);
			x += 22 + regular.GetStringSize(partNames[i], fontSize: fontSize).X + 24;
		}
		if (rows.Count == 0) return;

		double max = Math.Max(rows.Max(r => r.parts.Sum()), referenceValue ?? 0) * 1.08;
		float barLeft = LabelWidth, barWidth = Size.X - LabelWidth - ValueWidth;
		float X(double value) => barLeft + barWidth * (float)(value / max);
		float rowHeight = RowHeight;

		for (int r = 0; r < rows.Count; r++) {
			Row row = rows[r];
			float top = LegendHeight + r * rowHeight;
			float barTop = top + rowHeight * 0.18f, barHeight = rowHeight * 0.64f;
			float middle = top + rowHeight / 2 + fontSize * 0.35f;

			DrawRect(new Rect2(0, top + rowHeight / 2 - 5, 14, 10), row.color);
			DrawString(row.emphasized ? bold : regular, new Vector2(20, middle), row.label, HorizontalAlignment.Left, LabelWidth - 26, fontSize, Ink);

			double start = 0;
			for (int p = 0; p < row.parts.Count; p++) {
				Rect2 part = new(X(start), barTop, X(start + row.parts[p]) - X(start), barHeight);
				DrawRect(part, row.emphasized ? partColors[p] : partColors[p].Lerp(new Color(1, 1, 1), 0.25f));
				start += row.parts[p];
			}
			if (row.emphasized) {
				DrawRect(new Rect2(X(0), barTop, X(start) - X(0), barHeight), Ink, filled: false, width: 1.5f);
			}
			DrawString(row.emphasized ? bold : regular, new Vector2(X(start) + 6, middle), formatValue(start), HorizontalAlignment.Left, ValueWidth, fontSize, Ink);
		}

		if (referenceValue is double reference) {
			float rx = X(reference);
			float bottom = LegendHeight + rows.Count * rowHeight;
			DrawDashedLine(new Vector2(rx, LegendHeight - 2), new Vector2(rx, bottom), Faint, 1.5f, 5);
			DrawString(regular, new Vector2(rx - 60, bottom + fontSize + 2), referenceLabel, HorizontalAlignment.Center, 120, fontSize - 1, Faint);
		}
	}

	public override string _GetTooltip(Vector2 atPosition) {
		int r = (int)((atPosition.Y - LegendHeight) / RowHeight);
		return atPosition.Y >= LegendHeight && r >= 0 && r < rows.Count ? rows[r].tooltip ?? "" : "";
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A line chart for the parchment screens: one line per series over game turns,
/// with gridlines and labels. Set <see cref="series"/> and call QueueRedraw.
/// </summary>
public partial class LineChart : Control {
	public class Series {
		public string name;
		public Color color;
		public List<int> turns;
		public List<double> values;
		// The player's own line is drawn thicker and on top.
		public bool emphasized;
	}

	public string title = "";
	public List<Series> series = new();
	public Func<double, string> formatValue = v => v.ToString("0");
	// Values in the hover box, if more precise than the axis labels.
	public Func<double, string> formatHoverValue = null;
	// Draw a line at zero, for series that go negative (e.g. luck).
	public bool showZeroLine = false;
	// Start the value axis at zero (off for values that stay in a narrow band, like efficiencies).
	public bool includeZero = true;
	public int fontSize = 13;

	public static readonly Color Ink = new(0.24f, 0.16f, 0.08f);
	private static readonly Color GridInk = new(0.24f, 0.16f, 0.08f, 0.16f);
	private static readonly Color ZeroInk = new(0.24f, 0.16f, 0.08f, 0.5f);

	private static Font regularFont;
	private static Font boldFont;

	public static Font RegularFont => regularFont ??= ResourceLoader.Load<FontFile>("res://Fonts/NotoSans-Regular.ttf");
	public static Font BoldFont => boldFont ??= ResourceLoader.Load<FontFile>("res://Fonts/NotoSans-Bold.ttf");

	// Show every line's value at the turn under the mouse.
	public bool hoverEnabled {
		get => MouseFilter == MouseFilterEnum.Stop;
		set => MouseFilter = value ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
	}

	private Vector2? hoverPosition;

	public LineChart() {
		MouseFilter = MouseFilterEnum.Ignore;
		MouseExited += () => SetHover(null);
	}

	/// Where the mouse is over the chart, or null (also used for developer screenshots).
	public void SetHover(Vector2? position) {
		hoverPosition = position;
		QueueRedraw();
	}

	public override void _GuiInput(InputEvent @event) {
		if (@event is InputEventMouseMotion motion) {
			SetHover(motion.Position);
		}
	}

	public override void _Draw() {
		float titleHeight = string.IsNullOrEmpty(title) ? 0 : fontSize + 8;
		if (titleHeight > 0) {
			DrawString(BoldFont, new Vector2(0, fontSize + 1), title, HorizontalAlignment.Left, Size.X, fontSize + 1, Ink);
		}

		List<Series> drawn = series.Where(s => s.values.Count > 0).ToList();
		if (drawn.Count == 0) {
			DrawString(RegularFont, new Vector2(0, titleHeight + Size.Y / 2), "No data yet", HorizontalAlignment.Center, Size.X, fontSize, Ink);
			return;
		}

		int firstTurn = drawn.Min(s => s.turns[0]);
		int lastTurn = Math.Max(drawn.Max(s => s.turns[^1]), firstTurn + 1);
		double low = drawn.Min(s => s.values.Min());
		if (includeZero) low = Math.Min(0, low);
		double high = drawn.Max(s => s.values.Max());
		(double step, double axisLow, double axisHigh) = NiceAxis(low, high, 4);

		// Room for the value labels on the left and the turn labels below.
		float labelWidth = Enumerable.Range(0, (int)Math.Round((axisHigh - axisLow) / step) + 1)
			.Max(i => RegularFont.GetStringSize(formatValue(axisLow + i * step), fontSize: fontSize).X) + 6;
		Rect2 plot = new(labelWidth, titleHeight + fontSize / 2f, Size.X - labelWidth - 4, Size.Y - titleHeight - fontSize / 2f - fontSize - 8);

		float X(int turn) => plot.Position.X + plot.Size.X * (turn - firstTurn) / (lastTurn - firstTurn);
		float Y(double value) => plot.End.Y - plot.Size.Y * (float)((value - axisLow) / (axisHigh - axisLow));

		for (double v = axisLow; v <= axisHigh + step / 2; v += step) {
			float y = Y(v);
			DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), GridInk, 1);
			string label = formatValue(v);
			DrawString(RegularFont, new Vector2(0, y + fontSize / 2.5f), label, HorizontalAlignment.Right, labelWidth - 6, fontSize, Ink);
		}
		if (showZeroLine && axisLow < 0) {
			DrawLine(new Vector2(plot.Position.X, Y(0)), new Vector2(plot.End.X, Y(0)), ZeroInk, 1.5f);
		}

		(double turnStep, _, _) = NiceAxis(firstTurn, lastTurn, 5);
		for (double t = Math.Ceiling(firstTurn / turnStep) * turnStep; t <= lastTurn; t += turnStep) {
			float x = X((int)t);
			DrawLine(new Vector2(x, plot.End.Y), new Vector2(x, plot.End.Y + 4), Ink, 1);
			DrawString(RegularFont, new Vector2(x - 30, plot.End.Y + fontSize + 4), ((int)t).ToString(), HorizontalAlignment.Center, 60, fontSize, Ink);
		}
		DrawLine(new Vector2(plot.Position.X, plot.End.Y), new Vector2(plot.End.X, plot.End.Y), Ink, 1);

		foreach (Series s in drawn.OrderBy(s => s.emphasized)) {
			Vector2[] points = s.turns.Zip(s.values, (t, v) => new Vector2(X(t), Y(v))).ToArray();
			if (points.Length == 1) {
				DrawCircle(points[0], s.emphasized ? 3 : 2, s.color);
			} else {
				DrawPolyline(points, s.color, s.emphasized ? 3f : 1.6f, antialiased: true);
			}
		}

		if (hoverPosition is Vector2 hover && plot.HasPoint(hover)) {
			int turn = Math.Clamp((int)Math.Round(firstTurn + (hover.X - plot.Position.X) / plot.Size.X * (lastTurn - firstTurn)), firstTurn, lastTurn);
			DrawHover(drawn, turn, X(turn), Y, hover, plot);
		}
	}

	private static readonly Color TooltipBackground = new(0.99f, 0.96f, 0.88f, 0.97f);

	// A guide line at the turn, a dot on each line, and a box listing every
	// line's value at that turn, highest first.
	private void DrawHover(List<Series> drawn, int turn, float x, Func<double, float> Y, Vector2 mouse, Rect2 plot) {
		List<(Series series, double value)> rows = drawn
			.Select(s => (series: s, index: s.turns.IndexOf(turn)))
			.Where(r => r.index >= 0)
			.Select(r => (r.series, r.series.values[r.index]))
			.OrderByDescending(r => r.Item2)
			.ToList();
		if (rows.Count == 0) return;

		DrawLine(new Vector2(x, plot.Position.Y), new Vector2(x, plot.End.Y), ZeroInk, 1);
		foreach ((Series series, double value) in rows) {
			DrawCircle(new Vector2(x, Y(value)), series.emphasized ? 4.5f : 3.5f, series.color);
		}

		int size = fontSize;
		float rowHeight = size + 6;
		float nameWidth = rows.Max(r => (r.series.emphasized ? BoldFont : RegularFont).GetStringSize(r.series.name, fontSize: size).X);
		Func<double, string> format = formatHoverValue ?? formatValue;
		float valueWidth = rows.Max(r => RegularFont.GetStringSize(format(r.value), fontSize: size).X);
		Vector2 boxSize = new(18 + nameWidth + 16 + valueWidth + 10, rowHeight * (rows.Count + 1) + 6);

		// Beside the mouse, flipped to stay inside the chart.
		Vector2 at = mouse + new Vector2(16, -boxSize.Y / 2);
		if (at.X + boxSize.X > Size.X) at.X = mouse.X - 16 - boxSize.X;
		at.Y = Math.Clamp(at.Y, 0, Math.Max(Size.Y - boxSize.Y, 0));

		DrawRect(new Rect2(at, boxSize), TooltipBackground);
		DrawRect(new Rect2(at, boxSize), ZeroInk, filled: false, width: 1);
		DrawString(BoldFont, at + new Vector2(8, rowHeight), $"Turn {turn}", HorizontalAlignment.Left, boxSize.X - 16, size, Ink);
		for (int i = 0; i < rows.Count; i++) {
			(Series series, double value) = rows[i];
			float baseline = at.Y + rowHeight * (i + 2);
			DrawRect(new Rect2(at.X + 8, baseline - size * 0.7f, 10, size * 0.7f), series.color);
			DrawString(series.emphasized ? BoldFont : RegularFont, new Vector2(at.X + 24, baseline), series.name, HorizontalAlignment.Left, nameWidth + 4, size, Ink);
			DrawString(RegularFont, new Vector2(at.X + boxSize.X - 8 - valueWidth, baseline), format(value), HorizontalAlignment.Right, valueWidth, size, Ink);
		}
	}

	/// Round gridline steps (1, 2 or 5 times a power of ten) covering [low, high].
	public static (double step, double low, double high) NiceAxis(double low, double high, int targetSteps) {
		if (high <= low) high = low + 1;
		double rough = (high - low) / targetSteps;
		double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
		double step = new[] { 1, 2, 5, 10 }.Select(m => m * magnitude).First(s => s >= rough);
		return (step, Math.Floor(low / step) * step, Math.Ceiling(high / step) * step);
	}
}

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

	public LineChart() {
		MouseFilter = MouseFilterEnum.Ignore;
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

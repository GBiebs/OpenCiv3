using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Civ3's histograph: time runs down the chart, and on each turn every civ's
/// band is as wide as its share of that turn's total (score, power, culture).
/// The bands are lined up by their time keys (turns or calendar dates), so a
/// civ met late or destroyed early is simply missing on the other turns.
/// </summary>
public partial class HistographChart : Control {
	public record Band(string name, Color color, List<int> times, List<double> values);

	public List<Band> bands = new();
	// The text for a time key: a date or "Turn N".
	public Func<int, string> timeLabel = time => $"Turn {time}";
	// Civ names in their colors above the bands (Civ3), and the dates down the
	// left, outside the chart (the histograph screen's date bar).
	public bool showNames = true;
	public bool showDates = false;
	public string title = "";
	public int fontSize = 12;
	public Func<double, string> formatValue = v => v.ToString("N0");

	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	private const int DateLabels = 10;

	public HistographChart() {
		MouseFilter = MouseFilterEnum.Pass;
		// A tooltip shows every civ's value on the turn under the mouse.
		TooltipText = " ";
	}

	private float TitleHeight => title == "" ? 0 : fontSize + 6;
	private float NamesHeight => showNames ? fontSize + 6 : 0;
	private Rect2 Plot => new(0, TitleHeight + NamesHeight, Size.X, Math.Max(1, Size.Y - TitleHeight - NamesHeight));

	private List<int> Times() => bands.SelectMany(b => b.times).Distinct().Order().ToList();

	private static double ValueAt(Band band, int time) {
		int i = band.times.IndexOf(time);
		return i >= 0 ? Math.Max(0, band.values[i]) : 0;
	}

	// Each band's left and right edge on the turn, as shares of the width.
	private List<(float left, float right)> Shares(int time) {
		List<double> values = bands.Select(b => ValueAt(b, time)).ToList();
		double total = values.Sum();
		List<(float, float)> shares = new();
		double at = 0;
		for (int i = 0; i < bands.Count; i++) {
			// With nothing to share, everyone present gets an equal part.
			double share = total > 0 ? values[i] / total : 1.0 / bands.Count;
			shares.Add(((float)at, (float)(at + share)));
			at += share;
		}
		return shares;
	}

	public override void _Draw() {
		if (title != "") {
			DrawString(LineChart.BoldFont, new Vector2(0, fontSize + 1), title, HorizontalAlignment.Left, Size.X, fontSize + 1, Ink);
		}
		List<int> times = Times();
		Rect2 plot = Plot;
		if (times.Count == 0 || bands.Count == 0) {
			DrawString(LineChart.RegularFont, new Vector2(0, plot.Position.Y + plot.Size.Y / 2), "No data yet", HorizontalAlignment.Center, Size.X, fontSize, Ink);
			return;
		}

		float RowY(int index) => plot.Position.Y + (times.Count == 1 ? 0 : plot.Size.Y * index / (times.Count - 1));
		List<List<(float left, float right)>> rows = times.Select(Shares).ToList();
		if (times.Count == 1) {
			rows.Add(rows[0]);
		}
		for (int r = 0; r + 1 < rows.Count; r++) {
			float y0 = RowY(r), y1 = times.Count == 1 ? plot.End.Y : RowY(r + 1);
			for (int b = 0; b < bands.Count; b++) {
				(float l0, float r0) = rows[r][b];
				(float l1, float r1) = rows[r + 1][b];
				if (r0 - l0 <= 0 && r1 - l1 <= 0) {
					continue;
				}
				Color color = bands[b].color;
				// A quad needs no triangulation, so a band that narrows to
				// nothing still draws.
				DrawPrimitive([
					new Vector2(plot.Position.X + l0 * plot.Size.X, y0), new Vector2(plot.Position.X + r0 * plot.Size.X, y0),
					new Vector2(plot.Position.X + r1 * plot.Size.X, y1), new Vector2(plot.Position.X + l1 * plot.Size.X, y1),
				], [color, color, color, color], null);
			}
		}

		if (showNames) {
			DrawNames(plot);
		}
		if (showDates) {
			DrawDates(times, RowY, plot);
		}
	}

	// The names spread evenly across the top, each on its civ's color.
	private void DrawNames(Rect2 plot) {
		float y = TitleHeight;
		for (int i = 0; i < bands.Count; i++) {
			string name = bands[i].name;
			float width = LineChart.RegularFont.GetStringSize(name, fontSize: fontSize).X + 6;
			float x = Math.Min(plot.Position.X + plot.Size.X * i / bands.Count, plot.End.X - width);
			Color color = bands[i].color;
			DrawRect(new Rect2(x, y, width, NamesHeight - 2), color);
			Color text = color.Luminance > 0.55f ? Ink : Colors.White;
			DrawString(LineChart.RegularFont, new Vector2(x + 3, y + fontSize), name, HorizontalAlignment.Left, -1, fontSize, text);
		}
	}

	private void DrawDates(List<int> times, Func<int, float> rowY, Rect2 plot) {
		int labels = Math.Min(DateLabels, times.Count);
		for (int k = 0; k < labels; k++) {
			int index = labels == 1 ? 0 : (int)Math.Round(k * (times.Count - 1) / (double)(labels - 1));
			// Centered on its row, but inside the chart's height.
			float y = Math.Clamp(rowY(index) + fontSize / 2f - 2, plot.Position.Y + fontSize, plot.End.Y);
			DrawString(LineChart.RegularFont, new Vector2(-62, y), timeLabel(times[index]), HorizontalAlignment.Right, 58, fontSize - 1, Ink);
		}
	}

	public override string _GetTooltip(Vector2 atPosition) {
		List<int> times = Times();
		Rect2 plot = Plot;
		if (times.Count == 0 || !plot.HasPoint(atPosition)) {
			return "";
		}
		int index = times.Count == 1 ? 0 : (int)Math.Round((atPosition.Y - plot.Position.Y) / plot.Size.Y * (times.Count - 1));
		int time = times[Math.Clamp(index, 0, times.Count - 1)];
		IEnumerable<string> lines = bands.Select(b => (b.name, value: ValueAt(b, time)))
			.OrderByDescending(x => x.value)
			.Select(x => $"{x.name}: {formatValue(x.value)}");
		return timeLabel(time) + "\n" + string.Join("\n", lines);
	}
}

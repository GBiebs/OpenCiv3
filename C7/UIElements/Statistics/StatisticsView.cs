using System;
using System.Collections.Generic;
using System.Linq;
using C7.Textures;
using C7Engine;
using C7GameData;
using Godot;

/// <summary>
/// The statistics screen (F12, and at the end of the game): production,
/// efficiencies, population, commerce, improved tiles, game actions and luck.
/// It opens on a dashboard of headline numbers and small charts; clicking one
/// shows it as a big chart, with the other statistics listed beside it. At the
/// end of the game it compares every civ; before then it shows only the
/// player's own statistics.
/// </summary>
public partial class StatisticsView : CenterContainer {
	public enum Layout { Dashboard, ChartBrowser }

	/// One civ's statistics as shown on the screen.
	public record CivStats(string name, Color color, PlayerStats stats, bool isPlayer);

	private TextureRect background;
	private List<CivStats> civs = new();
	private Layout layout = Layout.Dashboard;
	private int selectedStat = 0;
	// The end of the game: every civ is compared, and the start ratings are shown.
	private bool endOfGame = false;
	// The civ whose numbers the headlines show and whose line is drawn thick;
	// at the end of the game, clicking a civ in the legend switches to it.
	private string focusName;
	// What the exit button does: hide the screen, or (at the end of the game) leave it.
	private Action onClose;

	// The chart in the big chart view (for developer screenshots of the hover box).
	public LineChart bigChart { get; private set; }

	private static readonly Color Ink = LineChart.Ink;
	private static readonly Color Shade = new(0.36f, 0.25f, 0.12f, 0.10f);
	private static readonly Color Highlight = new(0.36f, 0.25f, 0.12f, 0.22f);
	private static readonly Color Border = new(0.36f, 0.25f, 0.12f, 0.45f);

	public StatisticsView() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		background = new TextureRect();
		AddChild(background);
	}

	/// Shows the statistics in game: every civ once the game is over, otherwise only the player's own.
	public void ShowView(Action onClose = null) {
		List<CivStats> shown = new();
		EngineStorage.ReadGameData((GameData gameData) => {
			Player controller = gameData.GetPlayer(EngineStorage.uiControllerID);
			IEnumerable<Player> players = gameData.gameOver
				? gameData.players.Where(p => !p.isBarbarians && p.stats.TurnCount > 0)
				: [controller];
			shown = players.Select(p => ForPlayer(p, p == controller)).ToList();
		});
		ShowView(shown, Layout.Dashboard, onClose: onClose, endOfGame: EngineStorage.gameData.gameOver);
	}

	public static CivStats ForPlayer(Player player, bool isPlayer) =>
		new(player.civilization.name, TextureLoader.LoadColor(player.GetPlayerColor()), player.stats, isPlayer);

	public void ShowView(List<CivStats> shown, Layout layout, string statName = null, Action onClose = null, string focus = null, bool endOfGame = false) {
		this.endOfGame = endOfGame;
		civs = shown.OrderByDescending(c => c.isPlayer).ToList();
		this.layout = layout;
		selectedStat = Math.Max(VisibleStats.FindIndex(s => s.name == statName), 0);
		focusName = focus ?? Player?.name;
		this.onClose = onClose ?? (() => GetParentOrNull<GameViews>()?.Hide());
		Build();
		Show();
	}

	// ---------------------------------------------------------------- what is shown

	private record Stat(string name, string unit, string chartTitle, string headlineLabel,
		Func<PlayerStats, (List<int> turns, List<double> values)> perTurn, Func<PlayerStats, double?> headline,
		Func<double, string> format, Func<double, string> axisFormat = null,
		bool includeZero = true, bool zeroLine = false, bool actionsOnly = false,
		// The short label under a dashboard card's number (default: headlineLabel).
		string cardLabel = null,
		// A one-off rating shown as bars rather than a line over turns.
		bool startPosition = false);

	private static string Whole(double v) => v.ToString("N0");
	private static string Percent(double v) => v.ToString("P1");
	private static string WholePercent(double v) => v.ToString("P0");
	private static string Signed(double v) => v.ToString("+0.0;-0.0;0.0");
	private static string SignedWhole(double v) => v.ToString("+0;-0;0");

	private static (List<int>, List<double>) PerTurn(PlayerStats s, List<int> series) => (s.turns, series.Select(v => (double)v).ToList());

	// A rating over turns 0..i for each turn i, skipping turns where it has no value yet.
	private static (List<int>, List<double>) Running(PlayerStats s, Func<int, double?> ratingUpTo) {
		List<int> turns = new();
		List<double> values = new();
		for (int i = 0; i < s.TurnCount; i++) {
			if (ratingUpTo(i) is double v) {
				turns.Add(s.turns[i]);
				values.Add(v);
			}
		}
		return (turns, values);
	}

	private static (List<int>, List<double>) RunningLuck(PlayerStats s) {
		List<double> values = new();
		double sum = 0;
		for (int i = 0; i < s.TurnCount; i++) {
			sum += s.battlesWon[i] - s.battleExpectedWins10k[i] / 1e4;
			values.Add(sum);
		}
		return (s.turns, values);
	}

	private static readonly List<Stat> Stats = [
		new("Start position", "start rating, against the game's average start (100)", "Start position", "start rating (100 = average)",
			s => ([], []), s => s.startRating?.rating, v => v.ToString("0"), cardLabel: "100 = average", startPosition: true),
		new("Production", "shields per turn", "Production (shields per turn)", "shields in total",
			s => PerTurn(s, s.shieldsProduced), s => PlayerStats.Sum(s.shieldsProduced), Whole),
		new("Production efficiency", "share of usable shields that went into builds", "Production efficiency", "of usable shields used",
			s => Running(s, i => s.ProductionEfficiency(0, i)), s => s.ProductionEfficiency(), Percent, WholePercent, includeZero: false, cardLabel: "of usable shields"),
		new("Growth efficiency", "share of surplus food that went into growth", "Growth efficiency", "of surplus food used",
			s => Running(s, i => s.GrowthEfficiency(0, i)), s => s.GrowthEfficiency(), Percent, WholePercent, includeZero: false, cardLabel: "of surplus food"),
		new("Population", "citizens", "Population (citizens)", "citizens now",
			s => PerTurn(s, s.population), s => s.population.LastOrDefault(), Whole),
		new("Commerce", "commerce per turn, before corruption", "Commerce (per turn)", "commerce in total",
			s => PerTurn(s, s.commerce), s => PlayerStats.Sum(s.commerce), Whole),
		new("Improved tiles", "improved tiles inside the borders", "Improved tiles", "tiles now",
			s => PerTurn(s, s.tilesImproved), s => s.tilesImproved.LastOrDefault(), Whole),
		new("Game actions", "commands given per turn", "Game actions (per turn)", "actions in total",
			s => PerTurn(s, s.actions), s => PlayerStats.Sum(s.actions), Whole, actionsOnly: true, cardLabel: "in total"),
		new("Luck", "battles won minus battles expected, over the game", "Luck (wins above expected)", "wins above expected",
			RunningLuck, s => s.Luck()?.WinsAboveExpected, Signed, SignedWhole, zeroLine: true),
	];

	private static Stat StatNamed(string name) => Stats.First(s => s.name == name);

	// The start ratings are a judge's view of the map: only shown once the game is over.
	private List<Stat> VisibleStats => Stats.Where(s => endOfGame || !s.startPosition).ToList();

	// Game actions only exist for human players.
	private IEnumerable<CivStats> CivsFor(Stat stat) =>
		stat.actionsOnly ? civs.Where(c => c.isPlayer || PlayerStats.Sum(c.stats.actions) > 0) : civs;

	private static string Headline(Stat stat, CivStats civ) {
		// Computer players give no commands.
		if (stat.actionsOnly && !civ.isPlayer && PlayerStats.Sum(civ.stats.actions) == 0) return "n/a";
		return stat.headline(civ.stats) is double v ? stat.format(v) : "n/a";
	}

	// "2nd of 8" among the civs shown, higher being better.
	private string Rank(Stat stat, CivStats civ) {
		List<CivStats> ranked = CivsFor(stat).Where(c => stat.headline(c.stats) != null).OrderByDescending(c => stat.headline(c.stats)).ToList();
		int index = ranked.IndexOf(civ);
		if (ranked.Count < 2 || index < 0) return "";
		int n = index + 1;
		string suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
		return $"{n}{suffix} of {ranked.Count}";
	}

	private CivStats Player => civs.FirstOrDefault(c => c.isPlayer) ?? civs.FirstOrDefault();

	private CivStats Focus => civs.FirstOrDefault(c => c.name == focusName) ?? Player;

	// " · SPAIN" when looking at another civ's numbers.
	private string FocusSuffix(bool upper) => Focus == Player ? "" : $"  ·  {(upper ? Focus.name.ToUpper() : Focus.name)}";

	// The breakdown shown under a statistic's headline in the big chart view.
	private static string Details(Stat stat, PlayerStats s) {
		static long Total(List<int> series) => PlayerStats.Sum(series);
		switch (stat.name) {
			case "Production efficiency": {
					long wasted = Total(s.shieldsOverflowed) + Total(s.shieldsCapped) + Total(s.shieldsLostToDisorder);
					return $"Wasted {wasted:N0} shields: {Total(s.shieldsOverflowed):N0} overflow beyond a finished build, " +
						$"{Total(s.shieldsCapped):N0} into a full box, {Total(s.shieldsLostToDisorder):N0} to civil disorder.\n" +
						$"Not counted as waste: {Total(s.shieldsCorrupted):N0} lost to corruption, {Total(s.shieldsLostToAnarchy):N0} to anarchy.";
				}
			case "Growth efficiency":
				return $"Wasted {Total(s.foodWasted):N0} of {Total(s.foodSurplus):N0} surplus food: overflow when a city grew, " +
					"and the whole surplus while a city couldn't grow.";
			case "Commerce":
				return $"{Total(s.commerceCorrupted):N0} lost to corruption; {Total(s.goldIncome):N0} gold income and {Total(s.beakers):N0} beakers in total.";
			case "Improved tiles":
				return s.tilesOwned.LastOrDefault() is int owned and > 0 ? $"{s.tilesImproved.Last():N0} of the {owned:N0} tiles inside its borders." : "";
			case "Luck": {
					if (s.Luck() is not LuckRating luck) return "No battles yet.";
					string percentile = luck.Percentile is double p ? $", luckier than {p:P0} of possible outcomes" : "";
					string Describe(NotableBattle b) => $"{b.unit} {(b.attacking ? "attacking" : "defending against")} {b.enemyCivilization} {b.enemyUnit} ({b.winChance:P0} to win, turn {b.turn})";
					return $"Won {luck.won} of {luck.battles} battles, {luck.expectedWins:0.0} expected{percentile}." +
						(s.luckiestWin != null ? $"\nLuckiest win: {Describe(s.luckiestWin)}" : "") +
						(s.unluckiestLoss != null ? $"\nUnluckiest loss: {Describe(s.unluckiestLoss)}" : "");
				}
			case "Start position": {
					if (s.startRating is not StartRating r) return "No start rating: this game began before start ratings were recorded.";
					string camps = r.barbarianCamps switch { 0 => "no barbarian camps", 1 => "1 barbarian camp", int n => $"{n} barbarian camps" };
					string rival = r.nearestRival > 0 ? $", nearest rival start {r.nearestRival} tiles away" : "";
					return $"Capital site: {r.sitePoints:N0} points from the tiles a capital there could work (index {r.siteIndex:0}).\n" +
						$"Expansion room: {r.landShare} land tiles nearer to it than to any rival within {StartPositionRating.RoomRadius} tiles, " +
						$"{camps} nearby{rival} (index {r.roomIndex:0}).\n" +
						$"Resources: {r.luxuries} luxury and {r.strategics} strategic types within {StartPositionRating.ResourceRadius} tiles (index {r.resourceIndex:0}).";
				}
			default:
				return "";
		}
	}

	// ---------------------------------------------------------------- building the screen

	private void Build() {
		foreach (Node child in background.GetChildren()) {
			background.RemoveChild(child);
			child.QueueFree();
		}
		background.Texture = TextureLoader.Load(layout == Layout.Dashboard ? "screens.wonders.background" : "screens.standing.histogram.background");

		TextureButton close = AdvisorUtils.CreateExitButton(background);
		close.Pressed += () => onClose();

		// Nothing to show before the first turn, unless the start has been rated.
		if (civs.Count == 0 || (Player.stats.TurnCount == 0 && Player.stats.startRating == null)) {
			AddLabel("STATISTICS", new Rect2(172, 14, 692, 36), 20, bold: true, align: HorizontalAlignment.Center);
			AddLabel("No statistics yet: they are recorded from the next turn on.", new Rect2(100, 340, 824, 40), 18, align: HorizontalAlignment.Center);
			return;
		}

		if (layout == Layout.Dashboard) {
			BuildDashboard();
		} else {
			BuildChartBrowser();
		}
	}

	private void Open(Layout layout, string statName = null) {
		this.layout = layout;
		if (statName != null) selectedStat = VisibleStats.FindIndex(s => s.name == statName);
		Build();
	}

	private string TurnsText => $"{Player.stats.TurnCount} TURNS";

	// Headline numbers on top and a grid of small charts; each opens as a big chart.
	private void BuildDashboard() {
		AddLabel($"STATISTICS  ·  {TurnsText}{FocusSuffix(upper: true)}", new Rect2(172, 14, 692, 36), 20, bold: true, align: HorizontalAlignment.Center);
		CivStats player = Focus;

		List<string> cards = new[] { "Start position", "Production efficiency", "Growth efficiency", "Luck", "Game actions" }
			.Where(name => VisibleStats.Any(s => s.name == name)).ToList();
		float cardWidth = (876 - 10 * (cards.Count - 1)) / (float)cards.Count;
		for (int i = 0; i < cards.Count; i++) {
			Stat stat = StatNamed(cards[i]);
			Rect2 rect = new(76 + i * (cardWidth + 10), 76, cardWidth, 86);
			Vector2 inner = new(rect.Size.X - 16, 20);
			AddBox(rect, () => Open(Layout.ChartBrowser, stat.name));
			AddLabel(stat.name, new Rect2(rect.Position + new Vector2(8, 6), inner), 12, bold: true);
			AddLabel(Headline(stat, player), new Rect2(rect.Position + new Vector2(8, 24), new Vector2(inner.X, 34)), 24, bold: true);
			string sub = stat.name == "Luck" && player.stats.Luck()?.Percentile is double p ? $"luckier than {p:P0}"
				: stat.actionsOnly && Headline(stat, player) == "n/a" ? "computer player"
				: stat.cardLabel ?? stat.headlineLabel;
			string rank = Rank(stat, player);
			AddLabel(rank != "" ? $"{sub}  ·  {rank}" : sub, new Rect2(rect.Position + new Vector2(8, 60), inner), 10);
		}

		AddLegend(new Vector2(78, 172), vertical: false, width: 880);

		string[] charts = ["Production", "Population", "Commerce", "Improved tiles", "Production efficiency", "Luck"];
		for (int i = 0; i < charts.Length; i++) {
			Stat stat = StatNamed(charts[i]);
			Rect2 rect = new(76 + (i % 3) * 296, 202 + (i / 3) * 252, 284, 240);
			AddBox(rect, () => Open(Layout.ChartBrowser, stat.name));
			Chart(stat, new Rect2(rect.Position + new Vector2(8, 6), rect.Size - new Vector2(16, 12)), stat.chartTitle, 12);
		}
	}

	// One big chart of the selected statistic, the statistics listed on the right.
	private void BuildChartBrowser() {
		AddLabel("STATISTICS", new Rect2(142, 14, 200, 32), 18, bold: true, align: HorizontalAlignment.Center);
		AddButton("◀  OVERVIEW", new Rect2(800, 14, 156, 32), () => Open(Layout.Dashboard));

		Stat stat = VisibleStats[selectedStat];
		CivStats player = Focus;
		string rank = Rank(stat, player);
		AddLabel($"{stat.name}{FocusSuffix(upper: false)}", new Rect2(166, 76, 560, 28), 20, bold: true);
		AddLabel($"{Headline(stat, player)} {stat.headlineLabel}{(rank != "" ? $"  ·  {rank}" : "")}", new Rect2(166, 104, 560, 22), 14);

		float chartTop = 134;
		string details = Details(stat, player.stats);
		if (details != "") {
			// Lines after wrapping at the label's width.
			int lines = details.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(LineChart.RegularFont.GetStringSize(line, fontSize: 12).X / 550)));
			AddLabel(details, new Rect2(166, 128, 560, 18 * lines + 4), 12, wrap: true);
			chartTop = 136 + 18 * lines + 8;
		}
		Rect2 chartRect = new(160, chartTop, 566, 676 - chartTop);
		if (stat.startPosition) {
			bigChart = null;
			StartPositionBars(chartRect);
		} else {
			bigChart = Chart(stat, chartRect, stat.unit);
			bigChart.hoverEnabled = true;
		}

		List<Stat> listed = VisibleStats;
		for (int i = 0; i < listed.Count; i++) {
			string name = listed[i].name;
			StatTile tile = new() {
				Position = new Vector2(804, 74 + i * 44),
				Size = new Vector2(150, 40),
				label = name,
				value = Headline(listed[i], player),
				selected = i == selectedStat,
			};
			tile.Clicked += () => Open(Layout.ChartBrowser, name);
			background.AddChild(tile);
		}

		float legendTop = 74 + listed.Count * 44 + 10;
		AddLegend(new Vector2(806, legendTop), vertical: true, width: 146);
		if (civs.Count > 1) {
			AddLabel("Click a civ to see its numbers.", new Rect2(806, legendTop + civs.Count * 22 + 4, 146, 18), 10);
		}
	}

	// ---------------------------------------------------------------- pieces

	private LineChart Chart(Stat stat, Rect2 rect, string title = "", int fontSize = 13) {
		LineChart chart = new() {
			Position = rect.Position,
			Size = rect.Size,
			title = title,
			formatValue = stat.axisFormat ?? stat.format,
			formatHoverValue = stat.format,
			showZeroLine = stat.zeroLine,
			includeZero = stat.includeZero,
			fontSize = fontSize,
		};
		foreach (CivStats civ in CivsFor(stat)) {
			(List<int> turns, List<double> values) = stat.perTurn(civ.stats);
			chart.series.Add(new LineChart.Series { name = civ.name, color = civ.color, turns = turns, values = values, emphasized = civ == Focus && civs.Count > 1 });
		}
		background.AddChild(chart);
		return chart;
	}

	private static readonly List<(string name, Color color)> StartParts = [
		("Capital site", new Color(0.55f, 0.34f, 0.16f)),
		("Expansion room", new Color(0.33f, 0.50f, 0.24f)),
		("Luxuries and strategics", new Color(0.82f, 0.60f, 0.16f)),
	];

	// One bar per civ, best start first, split into each factor's share of the rating.
	private void StartPositionBars(Rect2 rect) {
		StackedBarChart chart = new() {
			Position = rect.Position,
			Size = rect.Size,
			partNames = StartParts.Select(p => p.name).ToList(),
			partColors = StartParts.Select(p => p.color).ToList(),
			referenceValue = 100,
			referenceLabel = "average start",
		};
		foreach (CivStats civ in civs.Where(c => c.stats.startRating != null).OrderByDescending(c => c.stats.startRating.rating)) {
			StartRating r = civ.stats.startRating;
			double site = StartPositionRating.SiteWeight * r.siteIndex;
			double room = StartPositionRating.RoomWeight * r.roomIndex;
			double resources = StartPositionRating.ResourceWeight * r.resourceIndex;
			chart.rows.Add(new StackedBarChart.Row {
				label = civ.name,
				color = civ.color,
				emphasized = civ == Focus && civs.Count > 1,
				parts = [site, room, resources],
				tooltip = $"{civ.name}: {r.rating:0} = capital site {site:0} + expansion room {room:0} + resources {resources:0}",
			});
		}
		background.AddChild(chart);
	}

	private Label AddLabel(string text, Rect2 rect, int fontSize, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Left, Color? color = null, bool wrap = false) {
		Label label = new() {
			// Wrapping is set before the size: otherwise the label first grows to fit the whole line.
			AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
			Text = text,
			Position = rect.Position,
			Size = rect.Size,
			HorizontalAlignment = align,
			VerticalAlignment = VerticalAlignment.Center,
			ClipText = !text.Contains('\n'),
			MouseFilter = MouseFilterEnum.Ignore,
			LabelSettings = new LabelSettings {
				Font = bold ? LineChart.BoldFont : LineChart.RegularFont,
				FontSize = fontSize,
				FontColor = color ?? Ink,
			},
		};
		background.AddChild(label);
		return label;
	}

	// A shaded box; clicking it runs onClick.
	private void AddBox(Rect2 rect, Action onClick) {
		Box box = new() { Position = rect.Position, Size = rect.Size };
		box.Clicked += onClick;
		background.AddChild(box);
	}

	private void AddButton(string text, Rect2 rect, Action onClick) {
		AddBox(rect, onClick);
		AddLabel(text, rect, 14, bold: true, align: HorizontalAlignment.Center);
	}

	// The civs' colors; with more than one civ, clicking a civ shows its numbers.
	private void AddLegend(Vector2 position, bool vertical, float width) {
		Vector2 at = position;
		foreach (CivStats civ in civs) {
			bool focused = civ == Focus;
			float textWidth = (focused ? LineChart.BoldFont : LineChart.RegularFont).GetStringSize(civ.name, fontSize: 13).X;
			if (!vertical && at.X + textWidth + 30 > position.X + width) {
				at = new Vector2(position.X, at.Y + 20);
			}
			Vector2 size = new(vertical ? width : textWidth + 28, 20);
			if (civs.Count > 1) {
				LegendEntry entry = new() {
					Position = at - new Vector2(2, 0),
					Size = size + new Vector2(4, 0),
					focused = focused,
					TooltipText = civ.isPlayer ? "Your numbers" : $"Show the {civ.name} numbers",
				};
				entry.Clicked += () => {
					focusName = civ.name;
					Build();
				};
				background.AddChild(entry);
			}
			background.AddChild(new Swatch { Position = at + new Vector2(0, 5), Size = new Vector2(16, 10), color = civ.color });
			AddLabel(civ.name, new Rect2(at + new Vector2(22, 0), new Vector2(size.X - 22, 20)), 13, bold: focused);
			at = vertical ? at + new Vector2(0, 22) : at + new Vector2(textWidth + 40, 0);
		}
	}

	// Something on the screen that reacts to the mouse.
	private abstract partial class Clickable : Control {
		public event Action Clicked;
		protected bool hovered;

		protected Clickable() {
			MouseFilter = MouseFilterEnum.Stop;
			MouseDefaultCursorShape = CursorShape.PointingHand;
			MouseEntered += () => { hovered = true; QueueRedraw(); };
			MouseExited += () => { hovered = false; QueueRedraw(); };
		}

		public override void _GuiInput(InputEvent @event) {
			if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) {
				Clicked?.Invoke();
				AcceptEvent();
			}
		}
	}

	private partial class Box : Clickable {
		public override void _Draw() {
			DrawRect(new Rect2(Vector2.Zero, Size), hovered ? Highlight : Shade);
			DrawRect(new Rect2(Vector2.Zero, Size), Border, filled: false, width: 1);
		}
	}

	// A statistic in the big chart view's list.
	private partial class StatTile : Clickable {
		public string label;
		public string value;
		public bool selected;

		public override void _Draw() {
			DrawRect(new Rect2(Vector2.Zero, Size), selected || hovered ? Highlight : Shade);
			DrawRect(new Rect2(Vector2.Zero, Size), Border, filled: false, width: selected ? 2 : 1);
			DrawString(LineChart.BoldFont, new Vector2(6, 16), label, HorizontalAlignment.Left, Size.X - 12, 12, Ink);
			DrawString(LineChart.RegularFont, new Vector2(6, 33), value, HorizontalAlignment.Left, Size.X - 12, 12, Ink);
		}
	}

	// A civ in the legend: highlighted when it's the one shown or under the mouse.
	private partial class LegendEntry : Clickable {
		public bool focused;

		public override void _Draw() {
			if (focused || hovered) {
				DrawRect(new Rect2(Vector2.Zero, Size), focused ? Highlight : Shade);
			}
		}
	}

	private partial class Swatch : Control {
		public Color color;

		public Swatch() {
			MouseFilter = MouseFilterEnum.Ignore;
		}

		public override void _Draw() {
			DrawRect(new Rect2(Vector2.Zero, Size), color);
			DrawRect(new Rect2(Vector2.Zero, Size), Border, filled: false, width: 1);
		}
	}
}

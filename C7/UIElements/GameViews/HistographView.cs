using System;
using System.Collections.Generic;
using System.Linq;
using C7.Textures;
using C7Engine;
using C7GameData;
using Godot;

/// <summary>
/// Civ3's histograph: every civ we've met (all of them once the game is
/// over) as a band down the years, as wide as its share of the score, power,
/// culture or victory points; the world ranking by score and our score's
/// parts on the right. Reached from the victory status screen's "Show" list,
/// which also leads to the statistics.
/// </summary>
public partial class HistographView : CenterContainer {
	public enum Mode { Score, Power, Culture, VictoryPoints }

	private static readonly Color Ink = new(0.13f, 0.10f, 0.05f);
	private const int MaxRanked = 16;

	// The "Show" list's entries besides the histograph's modes (whose ids are
	// their numbers).
	public const int VictoryStatusEntry = 10, StatisticsEntry = 11;

	private TextureRect background;
	private Mode mode;

	public HistographView() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		background = new TextureRect();
		AddChild(background);
	}

	public void ShowView(Mode mode) {
		this.mode = mode;
		EngineStorage.ReadGameData(Build);
		Show();
	}

	private static int Value(HistTurnRecord record, Mode mode) => mode switch {
		Mode.Score => record.Score,
		Mode.Power => record.Power,
		Mode.Culture => record.Culture,
		_ => record.VP,
	};

	private static string Description(Mode mode) => mode switch {
		Mode.Score => "The score decides the game when time runs out. It is the average of your score on every turn so far.",
		Mode.Power => "Power weighs cities, gold, culture, advances, resources, military strength, nuclear weapons and wonders together.",
		Mode.Culture => "The culture of all of a civilization's cities, added up.",
		_ => "Victory points come from battles, wonders, advances, conquests, victory locations and princesses.",
	};

	private void Build(GameData gameData) {
		foreach (Node child in background.GetChildren()) {
			background.RemoveChild(child);
			child.QueueFree();
		}
		background.Texture = TextureLoader.Load("screens.standing.histogram.background");
		TextureButton close = AdvisorUtils.CreateExitButton(background);
		close.Pressed += () => GetParent<GameViews>().Hide();

		AddLabel("HISTOGRAPH", new Rect2(140, 12, 605, 36), 24, align: HorizontalAlignment.Center);
		AddLabel("SCORE", new Rect2(795, 12, 160, 36), 24, align: HorizontalAlignment.Center);

		Player player = gameData.GetPlayer(EngineStorage.viewerID) ?? EngineStorage.GetViewer();
		List<Player> shown = Shown(gameData, player);
		Dictionary<Player, List<HistTurnRecord>> history = shown.ToDictionary(p => p, p => gameData.history.GetValueOrDefault(p.id.ToString()) ?? new());

		HistographChart chart = new() {
			Position = new Vector2(140, 74),
			Size = new Vector2(605, 588),
			showDates = true,
			timeLabel = raw => gameData.timeOptions.GetDisplayTimeFromRaw(raw),
		};
		foreach (Player p in shown) {
			chart.bands.Add(new HistographChart.Band(p.civilization.name, TextureLoader.LoadColor(p.GetPlayerColor()),
				history[p].Select(r => r.Date).ToList(), history[p].Select(r => (double)Value(r, mode)).ToList()));
		}
		background.AddChild(chart);

		AddShowList(this, background, HasVictoryPoints(gameData, player), (int)mode, new Vector2(192, 666));
		AddScorePanel(shown, history, player);
	}

	// Ourselves and the civs we've met, in the game's order; everyone once the
	// game is over.
	public static List<Player> Shown(GameData gameData, Player player) {
		HashSet<Player> met = gameData.gameOver ? gameData.players.ToHashSet() : gameData.GetKnownRivals(player).Append(player).ToHashSet();
		return gameData.players.Where(p => met.Contains(p) && !p.isBarbarians).ToList();
	}

	// Whether a civ we see has victory points, which puts them on the list.
	public static bool HasVictoryPoints(GameData gameData, Player player) => Shown(gameData, player)
		.Any(p => (gameData.history.GetValueOrDefault(p.id.ToString())?.LastOrDefault()?.VP ?? 0) > 0);

	// The "Show" list of the victory status screen, the histograph and the
	// statistics, on parchment like Civ3's, with its label to the left.
	public static OptionButton AddShowList(Control view, Control parent, bool victoryPoints, int selected, Vector2 position) {
		Label label = new() {
			Text = "Show",
			Position = position - new Vector2(44, -2),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		label.AddThemeFontOverride("font", LineChart.RegularFont);
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AddThemeColorOverride("font_color", Ink);
		parent.AddChild(label);

		OptionButton list = new() {
			Position = position,
			Size = new Vector2(150, 22),
			FocusMode = FocusModeEnum.None,
		};
		StyleBoxFlat box = new() {
			BgColor = new Color(0.94f, 0.89f, 0.76f),
			BorderColor = new Color(0.36f, 0.25f, 0.12f),
			ContentMarginLeft = 8,
		};
		box.SetBorderWidthAll(1);
		StyleBoxFlat hover = (StyleBoxFlat)box.Duplicate();
		hover.BgColor = new Color(0.98f, 0.94f, 0.83f);
		list.AddThemeStyleboxOverride("normal", box);
		list.AddThemeStyleboxOverride("hover", hover);
		list.AddThemeStyleboxOverride("pressed", hover);
		list.AddThemeFontOverride("font", LineChart.RegularFont);
		list.AddThemeFontSizeOverride("font_size", 13);
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) {
			list.AddThemeColorOverride(color, Ink);
		}
		list.AddItem("Victory Status", VictoryStatusEntry);
		list.AddItem("Score", (int)Mode.Score);
		list.AddItem("Power", (int)Mode.Power);
		list.AddItem("Culture", (int)Mode.Culture);
		if (victoryPoints) {
			list.AddItem("Victory Points", (int)Mode.VictoryPoints);
		}
		list.AddItem("Statistics", StatisticsEntry);
		list.Selected = list.GetItemIndex(selected);
		list.ItemSelected += index => {
			GameViews views = view.GetParent<GameViews>();
			switch (list.GetItemId((int)index)) {
				case VictoryStatusEntry:
					views.ShowVictoryStatus();
					break;
				case StatisticsEntry:
					views.ShowStatistics();
					break;
				case int mode:
					views.ShowHistograph((Mode)mode);
					break;
			}
		};
		parent.AddChild(list);
		return list;
	}

	private void AddScorePanel(List<Player> shown, Dictionary<Player, List<HistTurnRecord>> history, Player player) {
		AddLabel("World Ranking:", new Rect2(808, 86, 150, 26), 19);
		float y = 114;
		foreach (Player p in shown.OrderByDescending(p => history[p].LastOrDefault()?.Score ?? 0).Take(MaxRanked)) {
			string name = p.civilization.name;
			if (name.Length > 11) {
				name = name[..8] + "...";
			}
			AddLabel(name + ":", new Rect2(823, y, 90, 16), 13);
			AddLabel($"{history[p].LastOrDefault()?.Score ?? 0}", new Rect2(895, y, 45, 16), 13, align: HorizontalAlignment.Right);
			y += 15;
		}

		HistTurnRecord ours = history.GetValueOrDefault(player)?.LastOrDefault();
		AddLabel("Your Score:", new Rect2(808, 366, 150, 26), 19);
		AddLabel($"{ours?.Score ?? 0}", new Rect2(808, 390, 120, 26), 19, align: HorizontalAlignment.Center);
		AddLabel("Summary:", new Rect2(808, 416, 150, 20), 14);
		float at = 434;
		foreach ((string label, float value) in new[] {
			("Happy Citizens:", ours?.ScoreHappy ?? 0),
			("Content and Specialist Citizens:", ours?.ScoreContent ?? 0),
			("Territory:", ours?.ScoreTerritory ?? 0),
			("Future Tech.:", ours?.ScoreFutureTech ?? 0),
		}) {
			int lines = label.Length > 20 ? 2 : 1;
			AddLabel(label, new Rect2(823, at, 132, 16 * lines), 13, wrap: true);
			AddLabel(value.ToString("0.0"), new Rect2(823, at + 16 * lines, 80, 16), 13, align: HorizontalAlignment.Center);
			at += 16 * lines + 18;
		}
		AddLabel(Description(mode), new Rect2(808, 584, 145, 90), 13, wrap: true);
	}

	private void AddLabel(string text, Rect2 rect, int fontSize, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false) {
		Label label = new() {
			Text = text,
			HorizontalAlignment = align,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		label.AddThemeFontOverride("font", LineChart.RegularFont);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", Ink);
		if (wrap) {
			// Before the size, so the label wraps at it rather than growing.
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}
		label.Position = rect.Position;
		label.Size = rect.Size;
		background.AddChild(label);
	}
}

using System.Collections.Generic;
using C7.Textures;
using C7Engine;
using C7GameData;
using Godot;

// Civ3's Histograph: each known civ's score or power over the game, a line
// in the civ's color, scaled by turns or by years (see HistographReport).
// It is reached from the Victory Status screen, as in Conquests, where the
// Histograph's old button became the Victory Status one
// (Conquests/Text/labels.txt: "V = Victory (used to be Histograph)").
public partial class HistographView : Control {

	private TextureRect background;
	private HistographGraph graph;
	private VBoxContainer legend;
	private readonly Dictionary<HistographReport.Measure, Button> measureButtons = new();
	private Button turnsButton, yearsButton;

	private HistographReport.Measure measure = HistographReport.Measure.Score;
	private bool byYears;

	// The background's two sheets of paper: the graph's and the legend's.
	private static readonly Rect2 GraphArea = new(137, 64, 610, 633);
	private static readonly Rect2 SideArea = new(795, 64, 175, 633);

	public HistographView() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		AdvisorUtils.CreateUI(this, CreateUI);
	}

	private void CreateUI() {
		background = new TextureRect();
		AddChild(background);
		background.Texture = TextureLoader.Load("screens.standing.histogram.background");
		Vector2 size = background.Texture.GetSize();
		CustomMinimumSize = size;

		TextureButton close = AdvisorUtils.CreateExitButton(background);
		close.Pressed += () => { this.GetParent<GameViews>().Hide(); };

		AdvisorUtils.CreateAdvisorTitle(background, size.X, "HISTOGRAPH");

		graph = new HistographGraph {
			Position = GraphArea.Position + new Vector2(10, 40),
			Size = GraphArea.Size - new Vector2(20, 50),
		};
		background.AddChild(graph);

		VBoxContainer side = new() { Position = SideArea.Position + new Vector2(10, 40), Size = SideArea.Size - new Vector2(20, 50) };
		side.AddThemeConstantOverride("separation", 4);
		background.AddChild(side);

		side.AddChild(AdvisorUtils.MakeLabel("Show", 15));
		foreach ((HistographReport.Measure m, string label) in new[] {
			(HistographReport.Measure.Score, "Score"), (HistographReport.Measure.Power, "Power") }) {
			Button button = MakeToggle(label);
			button.Pressed += () => { measure = m; Redraw(); };
			measureButtons[m] = button;
			side.AddChild(button);
		}
		// Not Civ3's: the statistics kept each turn (see StatisticsView).
		Button statistics = MakeToggle("Statistics");
		statistics.ToggleMode = false;
		statistics.Pressed += () => this.GetParent<GameViews>().ShowStatistics();
		side.AddChild(statistics);

		side.AddChild(AdvisorUtils.MakeSpacer(8));
		side.AddChild(AdvisorUtils.MakeLabel("Scale by", 15));
		turnsButton = MakeToggle("Turns");
		turnsButton.Pressed += () => { byYears = false; Redraw(); };
		side.AddChild(turnsButton);
		yearsButton = MakeToggle("Years");
		yearsButton.Pressed += () => { byYears = true; Redraw(); };
		side.AddChild(yearsButton);

		side.AddChild(AdvisorUtils.MakeSpacer(12));
		legend = new VBoxContainer();
		legend.AddThemeConstantOverride("separation", 2);
		side.AddChild(legend);

		Button victoryStatus = MakeToggle("Victory Status");
		victoryStatus.ToggleMode = false;
		victoryStatus.Position = new Vector2(SideArea.Position.X + 10, SideArea.End.Y - 44);
		victoryStatus.Size = new Vector2(SideArea.Size.X - 20, 32);
		victoryStatus.Pressed += () => this.GetParent<GameViews>().ShowVictoryStatus();
		background.AddChild(victoryStatus);
	}

	// A date of the time axis, as the game shows it; a year between turns
	// has no display of its own, so it is written out.
	private static string DateText(TimeOptions time, int date) {
		try {
			return time.GetDisplayTime(time.GetTurnFromRaw(date));
		} catch (System.Exception) {
			if (time.baseUnit != TimeUnit.Years) {
				return date.ToString();
			}
			return date < 0 ? $"{-date} {time.negativeLabel}" : $"{date} {time.positiveLabel}";
		}
	}

	private static Button MakeToggle(string text) {
		Button button = new() { Text = text, ToggleMode = true, Alignment = HorizontalAlignment.Left };
		button.AddThemeFontSizeOverride("font_size", 14);
		return button;
	}

	public void ShowView() {
		Show();
		Redraw();
	}

	private void Redraw() {
		if (graph == null) {
			return;
		}
		foreach ((HistographReport.Measure m, Button button) in measureButtons) {
			button.SetPressedNoSignal(m == measure);
		}
		turnsButton.SetPressedNoSignal(!byYears);
		yearsButton.SetPressedNoSignal(byYears);

		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			if (player == null) {
				return;
			}
			List<HistographSeries> series = HistographReport.SeriesFor(gameData, player, measure);
			List<(Color color, List<HistographPoint> points)> lines = new();
			AdvisorUtils.ClearList(legend);
			foreach (HistographSeries s in series) {
				Color color = TextureLoader.LoadColor(s.player.GetPlayerColor());
				lines.Add((color, s.points));

				ColorRect swatch = new() { Color = color, CustomMinimumSize = new Vector2(16, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
				legend.AddChild(AdvisorUtils.MakeRow(22, swatch, AdvisorUtils.MakeLabel(AdvisorUtils.CivTitle(s.player), 13, 125)));
			}
			TimeOptions time = gameData.timeOptions;
			graph.SetLines(lines, HistographReport.MaxValue(series), byYears, date => DateText(time, date));
		});
	}
}

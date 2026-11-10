using System;
using System.Collections.Generic;
using System.Linq;
using C7.Textures;
using C7Engine;
using C7GameData;
using Godot;

// Civ3's replay at the end of the game (or on retiring): the world turn by
// turn, each civ's land in its color and the cities as dots, with the year,
// each civ's score that turn and the latest events; then Continue.
public partial class ReplayScreen : Control {
	private const double SecondsPerTurn = 0.15;
	private static readonly Color Water = new(0.16f, 0.27f, 0.45f);
	private static readonly Color Land = new(0.45f, 0.52f, 0.36f);
	private static readonly Color Ink = new(0.93f, 0.9f, 0.82f);

	private readonly GameData gameData;
	private readonly Action onDone;
	private readonly ReplayHistory history;
	private readonly int tileCount;

	private TextureRect mapRect;
	private Label dateLabel;
	private VBoxContainer legend;
	private Label eventsLabel;
	private HSlider slider;
	private Button playButton;

	private int frame = -1;
	private int[] owners;
	private bool playing = true;
	private double untilNext;

	public ReplayScreen(GameData gameData, Action onDone) {
		this.gameData = gameData;
		this.onDone = onDone;
		history = gameData.replay;
		tileCount = gameData.map.tiles.Count;
	}

	public override void _Ready() {
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		AddChild(new ColorRect { Color = new Color(0.07f, 0.07f, 0.09f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });

		Label title = new() { Text = "Replay", Position = new Vector2(24, 12) };
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", Ink);
		AddChild(title);
		dateLabel = new() { Position = new Vector2(200, 22) };
		dateLabel.AddThemeFontSizeOverride("font_size", 18);
		dateLabel.AddThemeColorOverride("font_color", Ink);
		AddChild(dateLabel);

		mapRect = new TextureRect {
			Position = new Vector2(24, 60),
			Size = new Vector2(900, 560),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = TextureFilterEnum.Nearest,
		};
		AddChild(mapRect);

		legend = new VBoxContainer { Position = new Vector2(944, 60), Size = new Vector2(310, 560) };
		AddChild(legend);

		eventsLabel = new Label { Position = new Vector2(24, 630), Size = new Vector2(900, 120), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		eventsLabel.AddThemeColorOverride("font_color", Ink);
		eventsLabel.AddThemeFontSizeOverride("font_size", 14);
		AddChild(eventsLabel);

		slider = new HSlider { Position = new Vector2(944, 640), Size = new Vector2(310, 24), MinValue = 0, MaxValue = Math.Max(0, history.frames.Count - 1), Step = 1 };
		slider.ValueChanged += value => {
			playing = false;
			playButton.Text = "Play";
			ShowFrame((int)value);
		};
		AddChild(slider);
		playButton = Styled(new Button { Text = "Pause", Position = new Vector2(944, 680), Size = new Vector2(100, 32) });
		playButton.Pressed += () => {
			playing = !playing;
			if (playing && frame >= history.frames.Count - 1) {
				ShowFrame(0);
			}
			playButton.Text = playing ? "Pause" : "Play";
		};
		AddChild(playButton);
		Button done = Styled(new Button { Text = "Continue", Position = new Vector2(1154, 680), Size = new Vector2(100, 32) });
		done.Pressed += () => {
			QueueFree();
			onDone?.Invoke();
		};
		AddChild(done);

		if (history.frames.Count == 0) {
			dateLabel.Text = "Nothing was recorded for this game.";
			playing = false;
			return;
		}
		ShowFrame(0);
	}

	// Light text on a framed dark button (the default theme's text is dark).
	private static Button Styled(Button button) {
		foreach (string state in new[] { "normal", "hover", "pressed", "focus" }) {
			button.AddThemeStyleboxOverride(state, new StyleBoxFlat {
				BgColor = state == "hover" ? new Color(0.22f, 0.22f, 0.26f) : new Color(0.15f, 0.15f, 0.18f),
				BorderColor = Ink,
				BorderWidthBottom = 1,
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
			});
		}
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) {
			button.AddThemeColorOverride(color, Ink);
		}
		return button;
	}

	public override void _Process(double delta) {
		if (!playing || history.frames.Count == 0) {
			return;
		}
		untilNext -= delta;
		if (untilNext > 0) {
			return;
		}
		untilNext = SecondsPerTurn;
		if (frame >= history.frames.Count - 1) {
			playing = false;
			playButton.Text = "Play";
			return;
		}
		ShowFrame(frame + 1);
		slider.SetValueNoSignal(frame);
	}

	private void ShowFrame(int target) {
		// Forward a turn at a time; backward from the start.
		if (owners == null || target < frame) {
			frame = -1;
		}
		foreach ((int turn, int[] o) in history.Frames(tileCount).Skip(frame + 1).Take(target - frame)) {
			owners = o;
		}
		frame = target;
		int replayTurn = history.turns[frame];
		Draw(replayTurn);
		dateLabel.Text = $"{gameData.timeOptions.GetDisplayTime(replayTurn)} (turn {replayTurn})";
		ShowLegend(replayTurn);
		eventsLabel.Text = string.Join("\n", history.events.Where(e => e.turn <= replayTurn && e.text != "").TakeLast(6)
			.Select(e => $"{gameData.timeOptions.GetDisplayTime(e.turn)}: {e.text}"));
	}

	private void Draw(int turn) {
		GameMap map = gameData.map;
		Image image = Image.CreateEmpty(map.numTilesWide, Math.Max(1, map.numTilesTall / 2), false, Image.Format.Rgba8);
		Color[] colors = gameData.players.Select(p => p.isBarbarians ? Colors.Black : TextureLoader.LoadColor(p.GetPlayerColor())).ToArray();
		for (int i = 0; i < map.tiles.Count; i++) {
			Tile t = map.tiles[i];
			Color c = t.IsLand() ? Land : Water;
			int owner = owners[i];
			if (owner > 0 && owner <= colors.Length) {
				c = Color.FromHsv(colors[owner - 1].H, 1, colors[owner - 1].V).Lerp(c, 0.3f);
			}
			image.SetPixel(t.XCoordinate, t.YCoordinate / 2, c);
		}
		// The cities standing that turn.
		Dictionary<string, (int x, int y)> cities = new();
		foreach (ReplayHistory.Event e in history.events.Where(e => e.turn <= turn && e.city != null)) {
			if (e.kind == ReplayHistory.Kind.CityGone) {
				cities.Remove(e.city);
			} else if (e.x >= 0) {
				cities[e.city] = (e.x, e.y);
			}
		}
		foreach ((int x, int y) in cities.Values) {
			image.SetPixel(x, y / 2, Colors.White);
		}
		mapRect.Texture = ImageTexture.CreateFromImage(image);
	}

	private void ShowLegend(int turn) {
		foreach (Node child in legend.GetChildren()) {
			child.QueueFree();
		}
		var rows = gameData.players.Where(p => !p.isBarbarians)
			.Select(p => (player: p, score: Score(p, turn)))
			.OrderByDescending(r => r.score);
		foreach ((Player player, int score) in rows) {
			HBoxContainer row = new();
			row.AddChild(new ColorRect { Color = TextureLoader.LoadColor(player.GetPlayerColor()), CustomMinimumSize = new Vector2(16, 16) });
			Label name = new() { Text = $"{player.civilization.noun}{(player.isHuman ? " (you)" : "")}", CustomMinimumSize = new Vector2(220, 0) };
			name.AddThemeColorOverride("font_color", Ink);
			row.AddChild(name);
			Label points = new() { Text = score.ToString() };
			points.AddThemeColorOverride("font_color", Ink);
			row.AddChild(points);
			legend.AddChild(row);
		}
	}

	// The player's score that turn, from the histograph's records.
	private int Score(Player player, int turn) {
		if (!gameData.history.TryGetValue(player.id.ToString(), out List<HistTurnRecord> records) || records.Count == 0) {
			return 0;
		}
		return records[Math.Clamp(turn, 0, records.Count - 1)].Score;
	}
}

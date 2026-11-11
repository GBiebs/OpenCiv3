using System;
using System.Collections.Generic;
using System.Linq;
using C7.Textures;
using C7Engine;
using C7GameData;
using Godot;

// Civ3's Summary Replay, at the end of the game or on retiring (user,
// 2026-11-11: the same as Civ3's, from its screenshot), on Civ3's own art:
// the world turn by turn with each civ's land in its color and the cities
// as dots, the progress bar and its five buttons, the histograph up to the
// date shown, and the list of cities founded, captured and destroyed and
// wonders created ("Show event location" rings them on the map). The X goes
// on to the statistics.
public partial class ReplayScreen : Control {
	private enum Stat { Score, Power, Culture, VictoryPoints }

	private const double SecondsPerTurn = 0.15;
	// Turns a "Show event location" ring stays on the map, fading.
	private const int RingTurns = 4;
	private const int LineHeight = 20;
	// Where things are on Civ3's 1024x768 background (replay_background.pcx).
	private static readonly Vector2 BackgroundSize = new(1024, 768);
	private static readonly Rect2 MapSlot = new(295, 143, 426, 201);
	private static readonly Vector2 ProgressAt = new(299, 364);
	private static readonly Color Ink = new(0.08f, 0.07f, 0.05f);
	// The map: Civ3's dark sea, lighter by the shore, and land no one holds.
	private static readonly Color Ocean = Color.Color8(24, 57, 64);
	private static readonly Color Sea = Color.Color8(29, 70, 76);
	private static readonly Color Coast = Color.Color8(38, 90, 92);
	private static readonly Color Land = Color.Color8(82, 92, 62);

	private readonly GameData gameData;
	private readonly Action onDone;
	private readonly ReplayHistory history;
	private readonly int tileCount;
	private readonly List<(ReplayHistory.Event e, ReplayHistory.Line line, string name)> lines;

	private Control frame;
	private TextureRect mapRect;
	private Control rings;
	private Label dateLabel;
	private Control progress;
	private TextureButton playButton;
	private HistographChart chart;
	private Stat stat = Stat.Power;
	private Civ3Checkbox showLocations;
	private ScrollContainer eventScroll;
	private VBoxContainer eventList;

	private int shownFrame = -1;
	private int[] owners;
	private bool playing = true;
	private double untilNext;
	private Image image;
	private ImageTexture texture;
	private byte[] pixels;
	private int width, height;

	public ReplayScreen(GameData gameData, Action onDone) {
		this.gameData = gameData;
		this.onDone = onDone;
		history = gameData.replay;
		tileCount = gameData.map.tiles.Count;
		lines = history.Lines().ToList();
	}

	public override void _Ready() {
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		AddChild(new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });

		frame = new Control { Size = BackgroundSize, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(frame);
		frame.AddChild(new TextureRect { Texture = TextureLoader.Load("screens.replay.background"), MouseFilter = MouseFilterEnum.Ignore });

		AddLabel("SUMMARY REPLAY", new Rect2(112, 14, 800, 40), 24, HorizontalAlignment.Center, spacing: 12);
		dateLabel = AddLabel("", new Rect2(330, 104, 354, 30), 18, HorizontalAlignment.Center);

		width = gameData.map.numTilesWide * 4;
		height = Math.Max(2, gameData.map.numTilesTall * 2);
		pixels = new byte[width * height * 4];
		image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
		texture = ImageTexture.CreateFromImage(image);
		mapRect = new TextureRect {
			Texture = texture,
			Position = MapSlot.Position,
			Size = MapSlot.Size,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		frame.AddChild(mapRect);
		rings = new Control { Position = MapSlot.Position, Size = MapSlot.Size, MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
		rings.Draw += DrawRings;
		frame.AddChild(rings);

		progress = new Control { Position = ProgressAt, Size = new Vector2(0, 8), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
		progress.AddChild(new TextureRect { Texture = TextureLoader.Load("screens.replay.progress"), MouseFilter = MouseFilterEnum.Ignore });
		frame.AddChild(progress);

		// Civ3's buttons, where its screen has them.
		AddButton("start", 295, "Back to the start", () => { Pause(); ShowFrame(0); });
		AddButton("back", 384, "Back a turn", () => { Pause(); ShowFrame(Math.Max(0, shownFrame - 1)); });
		AddButton("stop", 462, "Stop", Pause);
		playButton = AddButton("play", 518, "Play", Play);
		playButton.ToggleMode = true;
		AddButton("forward", 604, "Forward a turn", () => { Pause(); ShowFrame(Math.Min(history.frames.Count - 1, shownFrame + 1)); });

		AddHistograph();
		AddEventList();

		TextureButton close = AdvisorUtils.CreateExitButton(frame);
		close.Pressed += () => {
			QueueFree();
			onDone?.Invoke();
		};

		GetViewport().SizeChanged += FitToWindow;
		FitToWindow();

		if (history.frames.Count == 0) {
			dateLabel.Text = "Nothing was recorded for this game.";
			playing = false;
			return;
		}
		ShowFrame(0);
		playButton.SetPressedNoSignal(true);
	}

	// The 1024x768 screen in the middle, smaller on a smaller window.
	private void FitToWindow() {
		Vector2 window = GetViewportRect().Size;
		float scale = Math.Min(1, Math.Min(window.X / BackgroundSize.X, window.Y / BackgroundSize.Y));
		frame.Scale = new Vector2(scale, scale);
		frame.Position = ((window - BackgroundSize * scale) / 2).Floor();
	}

	public override void _ExitTree() {
		GetViewport().SizeChanged -= FitToWindow;
	}

	private Label AddLabel(string text, Rect2 rect, int fontSize, HorizontalAlignment align = HorizontalAlignment.Left, int spacing = 0) {
		Label label = new() {
			Text = text,
			Position = rect.Position,
			Size = rect.Size,
			HorizontalAlignment = align,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		Font font = LineChart.RegularFont;
		if (spacing > 0) {
			font = new FontVariation { BaseFont = LineChart.RegularFont, SpacingGlyph = spacing };
		}
		label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", Ink);
		frame.AddChild(label);
		return label;
	}

	private TextureButton AddButton(string name, float x, string tooltip, Action action) {
		TextureButton button = new() { Position = new Vector2(x, 392), TooltipText = tooltip, FocusMode = FocusModeEnum.None };
		TextureLoader.SetButtonTextures(button, $"screens.replay.{name}");
		button.Pressed += action;
		frame.AddChild(button);
		return button;
	}

	private void Play() {
		if (history.frames.Count == 0) {
			playButton.SetPressedNoSignal(false);
			return;
		}
		playing = true;
		playButton.SetPressedNoSignal(true);
		if (shownFrame >= history.frames.Count - 1) {
			ShowFrame(0);
		}
		untilNext = SecondsPerTurn;
	}

	private void Pause() {
		playing = false;
		playButton.SetPressedNoSignal(false);
	}

	// "View Histograph by", and the histograph up to the date shown.
	private void AddHistograph() {
		AddLabel("View Histograph by", new Rect2(200, 437, 176, 22), 14, HorizontalAlignment.Right);
		OptionButton by = new() { Position = new Vector2(386, 438), Size = new Vector2(97, 21), FocusMode = FocusModeEnum.None };
		StyleBoxFlat box = new() { BgColor = Colors.White, BorderColor = new Color(0.35f, 0.35f, 0.35f), ContentMarginLeft = 6 };
		box.SetBorderWidthAll(1);
		foreach (string state in new[] { "normal", "hover", "pressed", "focus" }) {
			by.AddThemeStyleboxOverride(state, box);
		}
		by.AddThemeFontOverride("font", LineChart.RegularFont);
		by.AddThemeFontSizeOverride("font_size", 13);
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) {
			by.AddThemeColorOverride(color, Ink);
		}
		by.AddThemeIconOverride("arrow", DropdownArrow());
		by.AddItem("Score", (int)Stat.Score);
		by.AddItem("Power", (int)Stat.Power);
		by.AddItem("Culture", (int)Stat.Culture);
		if (gameData.history.Values.Any(records => records.Any(r => r.VP > 0))) {
			by.AddItem("Victory Points", (int)Stat.VictoryPoints);
		}
		by.Select(by.GetItemIndex((int)stat));
		by.ItemSelected += index => {
			stat = (Stat)by.GetItemId((int)index);
			ShowHistograph();
		};
		frame.AddChild(by);

		chart = new HistographChart { Position = new Vector2(166, 468), Size = new Vector2(320, 208), showNames = false };
		frame.AddChild(chart);
	}

	// A drop-down's arrow as on Civ3's screen: a black triangle on a grey key.
	private static ImageTexture DropdownArrow() {
		Image arrow = Image.CreateEmpty(17, 19, false, Image.Format.Rgba8);
		arrow.Fill(new Color(0.55f, 0.55f, 0.55f));
		for (int x = 0; x < 17; x++) {
			arrow.SetPixel(x, 0, new Color(0.85f, 0.85f, 0.85f));
			arrow.SetPixel(x, 18, new Color(0.25f, 0.25f, 0.25f));
		}
		for (int y = 0; y < 19; y++) {
			arrow.SetPixel(0, y, new Color(0.85f, 0.85f, 0.85f));
			arrow.SetPixel(16, y, new Color(0.25f, 0.25f, 0.25f));
		}
		for (int row = 0; row < 4; row++) {
			for (int x = 5 + row; x <= 11 - row; x++) {
				arrow.SetPixel(x, 8 + row, Colors.Black);
			}
		}
		return ImageTexture.CreateFromImage(arrow);
	}

	private static int Value(HistTurnRecord record, Stat stat) => stat switch {
		Stat.Score => record.Score,
		Stat.Power => record.Power,
		Stat.Culture => record.Culture,
		_ => record.VP,
	};

	private void ShowHistograph() {
		int turn = shownFrame >= 0 ? history.turns[shownFrame] : gameData.turn;
		int now;
		try {
			now = gameData.timeOptions.GetRawNumber(turn);
		} catch (Exception) {
			now = int.MaxValue;
		}
		chart.bands.Clear();
		foreach (Player p in gameData.players.Where(p => !p.isBarbarians)) {
			List<HistTurnRecord> records = (gameData.history.GetValueOrDefault(p.id.ToString()) ?? new()).Where(r => r.Date <= now).ToList();
			chart.bands.Add(new HistographChart.Band(p.civilization.name, TextureLoader.LoadColor(p.GetPlayerColor()),
				records.Select(r => r.Date).ToList(), records.Select(r => (double)Value(r, stat)).ToList()));
		}
		chart.QueueRedraw();
	}

	// "Show event location", and the events up to the date shown, the
	// latest at the bottom.
	private void AddEventList() {
		// (Civ3Checkbox takes its text colors from the theme's Button.)
		Theme ink = new() { DefaultFont = LineChart.RegularFont };
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color" }) {
			ink.SetColor(color, "Button", Ink);
		}
		showLocations = new Civ3Checkbox { Text = "Show event location", Position = new Vector2(534, 438), ButtonPressed = true, Theme = ink };
		showLocations.Toggled += _ => rings.QueueRedraw();
		frame.AddChild(showLocations);

		eventScroll = new ScrollContainer {
			Position = new Vector2(531, 469),
			// Ten whole lines, as Civ3's.
			Size = new Vector2(296, 10 * LineHeight),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		eventList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		eventList.AddThemeConstantOverride("separation", 0);
		eventScroll.AddChild(eventList);
		frame.AddChild(eventScroll);
		frame.AddChild(new Civ3ScrollBar(eventScroll, LineHeight) { Position = new Vector2(830, 468), Size = new Vector2(20, 208) });
	}

	private void ShowEvents(int turn) {
		List<string> shown = lines.Where(l => l.e.turn <= turn)
			.Select(l => ReplayHistory.Text(l.line, l.name, gameData.timeOptions.GetDisplayTime(l.e.turn))).ToList();
		if (shown.Count == eventList.GetChildCount()) {
			return;
		}
		foreach (Node child in eventList.GetChildren()) {
			eventList.RemoveChild(child);
			child.QueueFree();
		}
		foreach (string text in shown) {
			Label label = new() {
				Text = text,
				CustomMinimumSize = new Vector2(0, LineHeight),
				VerticalAlignment = VerticalAlignment.Center,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			label.AddThemeFontOverride("font", LineChart.RegularFont);
			label.AddThemeFontSizeOverride("font_size", 13);
			label.AddThemeColorOverride("font_color", Ink);
			eventList.AddChild(label);
		}
		// To the latest, once the list has its new height (containers lay
		// out later in the frame).
		scrollToEnd = 2;
	}

	private int scrollToEnd;

	public override void _Process(double delta) {
		if (scrollToEnd > 0) {
			--scrollToEnd;
			eventScroll.ScrollVertical = (int)eventScroll.GetVScrollBar().MaxValue;
		}
		if (!playing || history.frames.Count == 0) {
			return;
		}
		untilNext -= delta;
		if (untilNext > 0) {
			return;
		}
		untilNext = SecondsPerTurn;
		if (shownFrame >= history.frames.Count - 1) {
			Pause();
			return;
		}
		ShowFrame(shownFrame + 1);
	}

	private void ShowFrame(int target) {
		// Forward a turn at a time; backward from the start.
		if (owners == null || target < shownFrame) {
			shownFrame = -1;
		}
		foreach ((int turn, int[] o) in history.Frames(tileCount).Skip(shownFrame + 1).Take(target - shownFrame)) {
			owners = o;
		}
		shownFrame = target;
		int replayTurn = history.turns[shownFrame];
		DrawMap(replayTurn);
		dateLabel.Text = $"Current Date: {gameData.timeOptions.GetDisplayTime(replayTurn)}";
		progress.Size = new Vector2(419f * (history.frames.Count > 1 ? (float)shownFrame / (history.frames.Count - 1) : 1), 8);
		ShowHistograph();
		ShowEvents(replayTurn);
		rings.QueueRedraw();
	}

	// Each tile a diamond (8x4 pixels, Civ3's map shape), stretched over the
	// slot as Civ3 does; the cities standing that turn as white dots.
	private void DrawMap(int turn) {
		GameMap map = gameData.map;
		Color[] colors = gameData.players.Select(p => p.isBarbarians ? Land : TextureLoader.LoadColor(p.GetPlayerColor())).ToArray();
		Array.Clear(pixels);
		for (int i = 0; i < map.tiles.Count; i++) {
			Tile t = map.tiles[i];
			Color c = !t.baseTerrainType.isWater() ? Land
				: t.baseTerrainType.Key == "ocean" ? Ocean
				: t.baseTerrainType.Key == "sea" ? Sea
				: Coast;
			int owner = owners[i];
			if (owner > 0 && owner <= colors.Length && !t.baseTerrainType.isWater()) {
				c = colors[owner - 1];
			}
			Diamond(t.XCoordinate * 4, t.YCoordinate * 2, c, map.wrapHorizontally);
		}
		Dictionary<string, (int x, int y)> cities = new();
		foreach (ReplayHistory.Event e in history.events.Where(e => e.turn <= turn && e.city != null)) {
			if (e.kind == ReplayHistory.Kind.CityGone) {
				cities.Remove(e.city);
			} else if (e.x >= 0) {
				cities[e.city] = (e.x, e.y);
			}
		}
		foreach ((int x, int y) in cities.Values) {
			for (int dy = -1; dy <= 0; dy++) {
				for (int dx = -1; dx <= 0; dx++) {
					Put(x * 4 + dx, y * 2 + dy, Colors.White, map.wrapHorizontally);
				}
			}
		}
		image.SetData(width, height, false, Image.Format.Rgba8, pixels);
		texture.Update(image);
	}

	// The rows of a tile's diamond: 2, 6, 6 and 2 pixels wide.
	private void Diamond(int cx, int cy, Color c, bool wrap) {
		for (int dy = -2; dy <= 1; dy++) {
			int half = dy is -2 or 1 ? 1 : 3;
			for (int dx = -half; dx < half; dx++) {
				Put(cx + dx, cy + dy, c, wrap);
			}
		}
	}

	private void Put(int x, int y, Color c, bool wrap) {
		if (wrap) {
			x = ((x % width) + width) % width;
		}
		if (x < 0 || x >= width || y < 0 || y >= height) {
			return;
		}
		int at = (y * width + x) * 4;
		pixels[at] = (byte)c.R8;
		pixels[at + 1] = (byte)c.G8;
		pixels[at + 2] = (byte)c.B8;
		pixels[at + 3] = 255;
	}

	// "Show event location": a ring where each event of the last few turns
	// happened, fading with age.
	private void DrawRings() {
		if (showLocations == null || !showLocations.ButtonPressed || shownFrame < 0) {
			return;
		}
		int turn = history.turns[shownFrame];
		Vector2 scale = new(MapSlot.Size.X / gameData.map.numTilesWide, MapSlot.Size.Y / Math.Max(1, gameData.map.numTilesTall));
		foreach (var l in lines.Where(l => l.e.x >= 0 && l.e.turn <= turn && l.e.turn > turn - RingTurns)) {
			float fade = 1f - (turn - l.e.turn) / (float)RingTurns;
			Vector2 at = new Vector2(l.e.x, l.e.y) * scale;
			rings.DrawArc(at, 7, 0, Mathf.Tau, 24, new Color(0, 0, 0, 0.6f * fade), 3.5f);
			rings.DrawArc(at, 7, 0, Mathf.Tau, 24, new Color(1, 1, 1, fade), 2f);
		}
	}
}

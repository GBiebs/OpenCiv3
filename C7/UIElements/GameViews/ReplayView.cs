using System;
using System.Collections.Generic;
using System.Linq;
using C7.Textures;
using C7Engine;
using C7GameData;
using Godot;

// Civ3's replay ("SUMMARY REPLAY", Conquests/Text/labels.txt), shown at the
// end of the game (Civ3 FUN_004f9fb0 opens it after the end of game
// announcement): the game played back turn by turn on a map of the world,
// with each turn's events listed and the Histograph growing beside them.
//
// From Civ3 1.22 (module remarks_replay): a 1024x768 screen
// (FUN_00599d00, art\Replay\replay_background.pcx) with the map at
// (295, 142), 426 by 202; the buttons Rewind, Back One Turn, Stop, Play and
// Forward One Turn (art\Replay\replay_nav.pcx); a bar of the turns played;
// "View Histograph by" Power, Score, Culture and (with victory points)
// Victory Points over the graph; the list of events; and "Show event
// location", which shows each event as it is played in a box beside the map
// with a line to its place, for a second (FUN_0059ba30, FUN_0059d030).
// Playing goes on a turn every half second (the screen's idle handler at
// 0x59aff0 waits 500 ms), and stops at the last turn.
public partial class ReplayView : Control {
	private static readonly Rect2 MapArea = new(295, 142, 426, 202);
	private const float BarLeft = 299, BarWidth = 419, BarTop = 364, BarBottom = 372;
	private const double TurnSeconds = 0.5, EventSeconds = 1.0;

	private ReplayPlayback playback;
	private ReplayPlayback.State state;
	// The last turn whose events have been played; one before the first
	// turn when nothing has.
	private int shownTurn;
	private bool playing;
	private double sinceStep;
	// What remains of the event box's second, during which playing waits.
	private double eventBoxLeft;
	private readonly Queue<ReplayEvent> eventsToShow = new();

	private Action onExit;

	// At the end of the game the replay is one of its pages (see
	// EndOfGame): a click on it (off its controls), Enter, Space or its Next
	// button goes on to the next one.
	private bool EndOfGamePage => onExit != null;
	private Button next;
	private ulong shownAt;
	private const ulong InputGraceMsec = 300;

	// What is read from the game when the replay opens.
	private readonly Dictionary<string, Color> civColors = new();
	private readonly Dictionary<string, Player> civs = new();
	private int mapWidth, mapHeight;
	private bool[,] water;
	private Func<int, string> dateOfTurn = turn => turn.ToString();
	private Func<int, int> rawDateOfTurn = turn => turn;
	private bool victoryPoints;

	private TextureRect background;
	private TextureRect map;
	private Label dateLabel;
	private Control bar;
	private HistographGraph graph;
	private OptionButton measures;
	private CheckBox showLocation;
	private VBoxContainer eventList;
	private ScrollContainer eventScroll;
	private PanelContainer eventBox;
	private Label eventBoxText;
	private Line2D eventLine;

	public ReplayView() {
		MouseFilter = MouseFilterEnum.Stop;
		CustomMinimumSize = new Vector2(1024, 768);
	}

	public override void _Ready() {
		AdvisorUtils.CreateUI(this, CreateUI);
		VisibilityChanged += () => {
			if (!IsVisibleInTree()) {
				playing = false;
				Action exit = onExit;
				onExit = null;
				exit?.Invoke();
			}
		};
	}

	private void CreateUI() {
		const string backgroundPath = "Art/Replay/replay_background.pcx";
		if (Util.HasCiv3Media(backgroundPath)) {
			background = new TextureRect { Texture = TextureLoader.LoadByPath(backgroundPath) };
			AddChild(background);
		} else {
			// Without Civ3's art (standalone mode), a parchment in a frame,
			// as the Hall of Fame has, for the dark ink of the labels and
			// the Histograph.
			AddChild(new ColorRect { Color = Color.Color8(196, 180, 140), Size = new Vector2(1024, 768), MouseFilter = MouseFilterEnum.Pass });
			AddChild(new ColorRect { Color = Color.Color8(240, 226, 190), Position = new Vector2(140, 12), Size = new Vector2(744, 700), MouseFilter = MouseFilterEnum.Pass });
		}

		Label title = AdvisorUtils.CreateLabel(this, "SUMMARY REPLAY", new Rect2(247, 21, 530, 26), 20);
		title.HorizontalAlignment = HorizontalAlignment.Center;

		// "Current Date:" (labels.txt) and the date.
		dateLabel = AdvisorUtils.CreateLabel(this, "", new Rect2(402, 104, 300, 20), 14);

		AddChild(new ColorRect { Position = MapArea.Position, Size = MapArea.Size, Color = new Color(0, 0, 0), MouseFilter = MouseFilterEnum.Pass });
		map = new TextureRect {
			Position = MapArea.Position,
			Size = MapArea.Size,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = TextureFilterEnum.Nearest,
		};
		AddChild(map);

		bar = new Control { Position = new Vector2(BarLeft, BarTop - 4), Size = new Vector2(BarWidth, BarBottom - BarTop + 8) };
		bar.Draw += DrawBar;
		AddChild(bar);

		// Civ3's buttons, left to right, by their place in replay_nav.pcx.
		AddNavButton("Rewind", 295, 61, 0xc6, Rewind);
		AddNavButton("Back One Turn", 384, 52, 1, BackOneTurn);
		AddNavButton("Stop", 460, 43, 99, () => playing = false);
		AddNavButton("Play", 516, 44, 0x36, Play);
		AddNavButton("Forward One Turn", 602, 54, 0x8f, () => { playing = false; StepForward(); });

		AdvisorUtils.CreateLabel(this, "View Histograph by", new Rect2(241, 442, 145, 20), 13);
		measures = new OptionButton { Position = new Vector2(386, 436), Size = new Vector2(100, 22) };
		measures.AddThemeFontSizeOverride("font_size", 12);
		measures.ItemSelected += _ => RedrawGraph();
		AddChild(measures);

		showLocation = new CheckBox { Position = new Vector2(532, 436), Text = "Show event location" };
		showLocation.AddThemeFontSizeOverride("font_size", 13);
		AddChild(showLocation);

		graph = new HistographGraph { Position = new Vector2(166, 468), Size = new Vector2(320, 208), MouseFilter = MouseFilterEnum.Pass };
		AddChild(graph);

		eventScroll = new ScrollContainer { Position = new Vector2(530, 468), Size = new Vector2(320, 208), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
		eventList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		eventScroll.AddChild(eventList);
		AddChild(eventScroll);

		eventLine = new Line2D { Width = 2, DefaultColor = new Color(1, 1, 1), Visible = false };
		AddChild(eventLine);
		eventBox = new PanelContainer { Visible = false, Size = new Vector2(340, 0) };
		eventBoxText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) };
		eventBox.AddChild(eventBoxText);
		AddChild(eventBox);

		TextureButton exit = AdvisorUtils.CreateExitButton(this);
		exit.Pressed += Leave;

		// labels.txt's "Next", beside the close button.
		next = PlainButton("Next  ▸", 12);
		next.Position = new Vector2(820, 724);
		next.Size = new Vector2(118, 30);
		next.FocusMode = FocusModeEnum.None;
		next.Visible = EndOfGamePage;
		next.Pressed += Leave;
		AddChild(next);
	}

	private void Leave() => GetParent<GameViews>().Hide();

	public override void _GuiInput(InputEvent @event) {
		if (EndOfGamePage && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
				&& Time.GetTicksMsec() - shownAt >= InputGraceMsec) {
			AcceptEvent();
			Leave();
		}
	}

	public override void _Input(InputEvent @event) {
		if (EndOfGamePage && IsVisibleInTree() && @event is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Space }) {
			GetViewport().SetInputAsHandled();
			if (Time.GetTicksMsec() - shownAt >= InputGraceMsec) {
				Leave();
			}
		}
	}

	// A dark button edged in gold, with light text, as the replay's buttons
	// are without Civ3's art.
	private static Button PlainButton(string label, int fontSize) {
		Button plain = new() { Text = label, ClipText = true, TooltipText = label };
		plain.AddThemeFontSizeOverride("font_size", fontSize);
		StyleBoxFlat Box(Color fill) => new() {
			BgColor = fill,
			BorderColor = Color.Color8(170, 140, 70),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3,
		};
		plain.AddThemeStyleboxOverride("normal", Box(Color.Color8(70, 55, 35)));
		plain.AddThemeStyleboxOverride("hover", Box(Color.Color8(95, 75, 45)));
		plain.AddThemeStyleboxOverride("pressed", Box(Color.Color8(50, 40, 25)));
		plain.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) {
			plain.AddThemeColorOverride(color, Color.Color8(245, 232, 200));
		}
		return plain;
	}

	// A button of replay_nav.pcx, whose rows are its looks (UNVERIFIED which
	// row is which; Civ3 FUN_00599d00 crops rows 1, 31 and 61), or a plain
	// button with its labels.txt name without Civ3's art.
	private void AddNavButton(string label, int x, int width, int artX, Action pressed) {
		const string navPath = "Art/Replay/replay_nav.pcx";
		BaseButton button;
		if (Util.HasCiv3Media(navPath)) {
			Texture2D art = TextureLoader.LoadByPath(navPath);
			AtlasTexture Crop(int y) => new() { Atlas = art, Region = new Rect2(artX, y, width, 29) };
			button = new TextureButton {
				TextureNormal = Crop(1),
				TextureHover = Crop(31),
				TexturePressed = Crop(61),
				TooltipText = label,
			};
		} else {
			// Dark buttons edged in gold, with light text, on the parchment.
			button = PlainButton(label, 10);
		}
		button.Position = new Vector2(x, 392);
		button.Size = new Vector2(width, 29);
		button.Pressed += pressed;
		AddChild(button);
	}

	// Opens the replay of the game; onExit runs once it is closed.
	public void ShowView(Action onExit) {
		this.onExit = onExit;
		shownAt = Time.GetTicksMsec();
		if (next != null) {
			next.Visible = EndOfGamePage;
		}
		Show();
		if (map == null) {
			return;
		}
		EngineStorage.ReadGameData(ReadGame);
		Rewind();
	}

	private void ReadGame(GameData gameData) {
		playback = new ReplayPlayback(gameData.gameHistory, gameData.turn);
		civColors.Clear();
		civs.Clear();
		foreach (Player p in gameData.players) {
			string key = GameHistory.KeyOf(p);
			civs[key] = p;
			civColors[key] = TextureLoader.LoadColor(p.GetPlayerColor());
		}
		mapWidth = gameData.map.numTilesWide;
		mapHeight = gameData.map.numTilesTall;
		water = new bool[mapWidth, mapHeight];
		foreach (Tile t in gameData.map.tiles) {
			if (t.XCoordinate >= 0 && t.XCoordinate < mapWidth && t.YCoordinate >= 0 && t.YCoordinate < mapHeight) {
				water[t.XCoordinate, t.YCoordinate] = t.baseTerrainType?.IsWater ?? false;
			}
		}
		TimeOptions time = gameData.timeOptions;
		Dictionary<int, string> dates = new();
		for (int turn = playback.firstTurn; turn <= playback.lastTurn; ++turn) {
			dates[turn] = time.GetDisplayTime(turn);
		}
		dateOfTurn = turn => dates.TryGetValue(turn, out string d) ? d : turn.ToString();
		Dictionary<int, int> raw = new();
		for (int turn = playback.firstTurn; turn <= playback.lastTurn; ++turn) {
			raw[turn] = time.GetRawNumber(turn);
		}
		rawDateOfTurn = turn => raw.TryGetValue(turn, out int r) ? r : int.MaxValue;
		victoryPoints = gameData.victoryConditions?.VictoryLocations == true;

		measures.Clear();
		measures.AddItem("Power", (int)HistographReport.Measure.Power);
		measures.AddItem("Score", (int)HistographReport.Measure.Score);
		measures.AddItem("Culture", (int)HistographReport.Measure.Culture);
		if (victoryPoints) {
			measures.AddItem("Victory Points", (int)HistographReport.Measure.VictoryPoints);
		}
		measures.Select(0);
	}

	private void Rewind() {
		playing = false;
		if (playback == null) {
			return;
		}
		GoTo(playback.firstTurn - 1);
	}

	private void Play() {
		if (playback == null) {
			return;
		}
		if (shownTurn >= playback.lastTurn) {
			GoTo(playback.firstTurn - 1);
		}
		playing = true;
		sinceStep = TurnSeconds;
	}

	private void BackOneTurn() {
		playing = false;
		if (playback != null && shownTurn >= playback.firstTurn) {
			GoTo(shownTurn - 1);
		}
	}

	// Plays the next turn's events.
	private void StepForward() {
		if (playback == null || shownTurn >= playback.lastTurn) {
			playing = false;
			return;
		}
		int turn = shownTurn + 1;
		List<ReplayEvent> events = playback.EventsOf(turn).ToList();
		foreach (ReplayEvent e in events) {
			if (ReplayPlayback.IsListed(e.Type)) {
				AddEventLine(e);
				if (showLocation.ButtonPressed) {
					eventsToShow.Enqueue(e);
				}
			}
		}
		shownTurn = turn;
		state = playback.StateAt(turn);
		Redraw();
	}

	// Shows the world after the given turn, without playing it.
	private void GoTo(int turn) {
		eventsToShow.Clear();
		HideEventBox();
		shownTurn = turn;
		state = playback.StateAt(turn);
		AdvisorUtils.ClearList(eventList);
		foreach (ReplayEvent e in state.listed) {
			AddEventLine(e);
		}
		Redraw();
	}

	public override void _Process(double delta) {
		if (!IsVisibleInTree() || playback == null) {
			return;
		}
		if (eventBoxLeft > 0) {
			eventBoxLeft -= delta;
			if (eventBoxLeft <= 0) {
				HideEventBox();
			}
			return;
		}
		if (eventsToShow.Count > 0) {
			ShowEventBox(eventsToShow.Dequeue());
			return;
		}
		if (!playing) {
			return;
		}
		sinceStep += delta;
		if (sinceStep >= TurnSeconds) {
			sinceStep = 0;
			StepForward();
		}
	}

	private void Redraw() {
		int turn = Math.Max(shownTurn, playback.firstTurn);
		dateLabel.Text = $"Current Date: {dateOfTurn(turn)}";
		map.Texture = ImageTexture.CreateFromImage(DrawMap());
		bar.QueueRedraw();
		RedrawGraph();
	}

	private void DrawBar() {
		int span = Math.Max(1, playback?.lastTurn - playback?.firstTurn ?? 1);
		int done = playback == null ? 0 : Math.Clamp(shownTurn - playback.firstTurn, 0, span);
		bar.DrawRect(new Rect2(0, 4, BarWidth, BarBottom - BarTop), new Color(0, 0, 0, 0.4f));
		bar.DrawRect(new Rect2(0, 4, BarWidth * done / span, BarBottom - BarTop), new Color(0.85f, 0.75f, 0.35f));
	}

	// The world at the shown turn: water and land, each tile its owner's
	// color, and the cities. Civ3's map coordinates are a diamond grid, in
	// which a row holds every other column, so each tile fills two pixels.
	private Image DrawMap() {
		int w = Math.Max(1, mapWidth), h = Math.Max(1, mapHeight);
		Image image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		Color sea = new(0.1f, 0.18f, 0.35f), land = new(0.35f, 0.3f, 0.2f);
		for (int y = 0; y < mapHeight; ++y) {
			for (int x = 0; x < mapWidth; ++x) {
				int tileX = (x + y) % 2 == 0 ? x : x - 1;
				if (tileX < 0) {
					tileX = x + 1 < mapWidth ? x + 1 : x;
				}
				bool isWater = water[Math.Clamp(tileX, 0, mapWidth - 1), y];
				Color color = isWater ? sea : land;
				if (state != null && state.owners.TryGetValue((tileX, y), out string owner) && civColors.TryGetValue(owner, out Color civ)) {
					color = isWater ? civ.Darkened(0.5f) : civ;
				}
				image.SetPixel(x, y, color);
			}
		}
		if (state != null) {
			foreach ((int x, int y) in state.cities.Keys) {
				for (int dx = 0; dx < 2; ++dx) {
					if (x + dx >= 0 && x + dx < w && y >= 0 && y < h) {
						image.SetPixel(x + dx, y, new Color(1, 1, 1));
					}
				}
			}
		}
		// A row of the diamond grid is half a tile tall and a column half a
		// tile wide, a tile being twice as wide as it is tall: twice as wide
		// keeps the map's shape.
		image.Resize(w * 2, h, Image.Interpolation.Nearest);
		return image;
	}

	private void RedrawGraph() {
		if (playback == null || measures.ItemCount == 0) {
			return;
		}
		HistographReport.Measure measure = (HistographReport.Measure)measures.GetSelectedId();
		int upTo = shownTurn < playback.firstTurn ? int.MinValue : rawDateOfTurn(shownTurn);
		EngineStorage.ReadGameData(gameData => {
			List<HistographSeries> all = HistographReport.AllSeries(gameData, measure, int.MaxValue);
			List<HistographSeries> sofar = HistographReport.AllSeries(gameData, measure, upTo);
			int lastIndex = Math.Max(1, all.Select(s => s.points.Count).DefaultIfEmpty(0).Max() - 1);
			graph.fixedTurns = (0, lastIndex);
			List<(Color, List<HistographPoint>)> lines = sofar
				.Select(s => (civColors.GetValueOrDefault(GameHistory.KeyOf(s.player), Colors.White), s.points))
				.ToList();
			graph.SetLines(lines, HistographReport.MaxValue(all), false, null);
		});
	}

	private void AddEventLine(ReplayEvent e) {
		Label line = AdvisorUtils.MakeWrappedLabel(Describe(e), 12, 300);
		eventList.AddChild(line);
		// Newest at the bottom, kept in view.
		Callable.From(() => eventScroll.ScrollVertical = (int)eventScroll.GetVScrollBar().MaxValue).CallDeferred();
	}

	private string CivNoun(string key) => key != null && civs.TryGetValue(key, out Player p) ? AdvisorUtils.CivName(p) : "";
	private string CivName(string key) => key != null && civs.TryGetValue(key, out Player p) ? p.civilization?.name ?? AdvisorUtils.CivName(p) : "";

	// The event's line of the list, in the wording of Civ3's labels.txt
	// that Civ3 FUN_0059be00 picks for it.
	private string Describe(ReplayEvent e) {
		string date = dateOfTurn(e.Turn);
		string text = e.Text ?? "";
		return e.Type switch {
			ReplayEventType.CityFounded => $"{text} founded in {date}",
			ReplayEventType.CityDestroyed or ReplayEventType.CityDestroyedOwnerKeepsTile => $"{text} destroyed in {date}",
			ReplayEventType.CityCaptured => $"{text} captured in {date}",
			ReplayEventType.WonderBuilt => $"{text} created in {date}",
			ReplayEventType.KingKilled => $"{text} of the {CivNoun(e.Civ)} killed in {date}",
			ReplayEventType.PrincessCaptured => $"{CivNoun(e.Civ)} capture a princess in {date}",
			ReplayEventType.PrincessPickedUp => $"{CivNoun(e.Civ)} pick up a princess in {date}",
			ReplayEventType.PrincessRescued => $"Princess of {CivName(e.Civ)} rescued in {date}",
			ReplayEventType.VictoryLocationSecured => $"Victory point location secured by {CivName(e.Civ)} in {date}",
			ReplayEventType.VictoryLocationLost => $"Victory point location lost by {CivName(e.Civ)} in {date}",
			ReplayEventType.GreatLeader => $"A great event for {CivName(e.Civ)} has produced the Great Leader, {text}, in {date}",
			ReplayEventType.GoldenAge => $"{CivName(e.Civ)} enters a Golden Age in {date}",
			_ => text,
		};
	}

	// The map pixel, in this screen, of a tile.
	private Vector2 PointOf(int x, int y) {
		if (map?.Texture == null) {
			return MapArea.GetCenter();
		}
		Vector2 tex = map.Texture.GetSize();
		float scale = Math.Min(MapArea.Size.X / tex.X, MapArea.Size.Y / tex.Y);
		Vector2 offset = MapArea.Position + (MapArea.Size - tex * scale) / 2;
		return offset + new Vector2((x + 1) * 2, y + 0.5f) * scale;
	}

	// The event's box (script.txt's REPLAY_WIN, "$String0."), placed as
	// Civ3 FUN_0059ba30 places it: at the left for an event in the left part
	// of the map (less than 176 pixels in), otherwise at the right, with a
	// line from the box to the event's place.
	private void ShowEventBox(ReplayEvent e) {
		eventBoxText.Text = Describe(e) + ".";
		Vector2 at = PointOf(e.X, e.Y);
		bool left = at.X - MapArea.Position.X < 176;
		eventBox.Position = left ? new Vector2(10, 30) : new Vector2(665, 30);
		eventLine.Points = [left ? new Vector2(80, 80) : new Vector2(735, 80), at];
		eventBox.Visible = true;
		eventLine.Visible = true;
		eventBoxLeft = EventSeconds;
	}

	private void HideEventBox() {
		eventBoxLeft = 0;
		if (eventBox != null) {
			eventBox.Visible = false;
			eventLine.Visible = false;
		}
	}
}

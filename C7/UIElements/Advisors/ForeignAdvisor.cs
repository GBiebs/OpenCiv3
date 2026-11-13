using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;
using Vector2 = Godot.Vector2;

// Civ3's Foreign Advisor (F4): the leaders of the civs we know in a circle,
// with lines for the ties between them (war, peace, pacts, alliances and
// embargoes, each of which can be turned off), and the Treaties, Trades and
// Details tabs about the leader picked. Its wording is Civ3's, from
// labels.txt (#FOREIGN_ADVISOR) and script.txt (#FA_...), worked out in
// ForeignReport.
[GlobalClass]
[Tool]
public partial class ForeignAdvisor : Control {

	[Export] public TextureRect background;

	private enum Tab {
		Treaties,
		Trades,
		Details,
	}

	private readonly TextureRect[] _tabPanels = new TextureRect[3];
	private readonly Button[] _tabButtons = new Button[3];
	private Tab _tab = Tab.Treaties;

	private TextureButton _close;
	private TextureRect _advisorHead;
	private TextureButton _dialogBox;
	private Label _dialogBoxLabel;

	private ForeignWeb _web;
	private Label _tabTitle;
	private VBoxContainer _tabList;

	// The ties shown, by kind, which the checkboxes turn on and off.
	private readonly HashSet<ForeignTieKind> _shownKinds = Enum.GetValues<ForeignTieKind>().ToHashSet();
	private readonly Dictionary<ForeignTieKind, CheckBox> _kindBoxes = [];

	// The leader picked, whose tabs are shown, by player id.
	private ID _selected;

	// Where the tabs are on the background: the panel of Civ3's art
	// (foreignTAB.pcx) with its three tabs along the top.
	private static readonly Vector2 TabsPosition = new(760, 254);
	private static readonly Vector2 TabSize = new(74, 18);
	private static readonly Vector2 PanelSize = new(223, 236);

	// The colors of the lines for each kind of tie. UNVERIFIED: Civ3's own
	// colors for them.
	public static readonly Dictionary<ForeignTieKind, Color> KindColors = new() {
		[ForeignTieKind.War] = Color.Color8(200, 30, 30),
		[ForeignTieKind.Peace] = Color.Color8(40, 140, 60),
		[ForeignTieKind.MutualProtection] = Color.Color8(40, 80, 200),
		[ForeignTieKind.RightOfPassage] = Color.Color8(220, 150, 20),
		[ForeignTieKind.Alliance] = Color.Color8(140, 40, 160),
		[ForeignTieKind.Embargo] = Color.Color8(60, 60, 60),
	};

	// The toggles' names, from labels.txt (#FOREIGN_ADVISOR).
	private static readonly Dictionary<ForeignTieKind, string> KindNames = new() {
		[ForeignTieKind.War] = "War",
		[ForeignTieKind.Peace] = "Peace",
		[ForeignTieKind.MutualProtection] = "Mutual Protection",
		[ForeignTieKind.RightOfPassage] = "Right of Passage",
		[ForeignTieKind.Alliance] = "Alliance",
		[ForeignTieKind.Embargo] = "Embargo",
	};

	public ForeignAdvisor() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		AdvisorUtils.CreateUI(this, CreateUI);
	}

	private void CreateUI() {
		background.Texture = TextureLoader.Load("advisors.foreign.background");

		_advisorHead = AdvisorUtils.CreateAdvisorHead(background, AdvisorHead.Advisor.Foreign);
		_close = AdvisorUtils.CreateExitButton(background);
		_close.Pressed += () => { this.GetParent<Advisors>().Hide(); };
		(_dialogBox, _dialogBoxLabel) = AdvisorUtils.CreateAdvisorDialogBox(background);
		AdvisorUtils.SpeakOnClickAndInTime(this, _dialogBox, Speak);

		// The leaders and their ties, on the left.
		_web = new ForeignWeb { Position = new Vector2(67, 65), Size = new Vector2(683, 652) };
		_web.LeaderPressed += id => {
			_selected = id;
			ShowAdvisor();
		};
		background.AddChild(_web);

		CreateTabs();
		CreateToggles(new Vector2(TabsPosition.X + 4, TabsPosition.Y + PanelSize.Y + 8));

		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "FOREIGN ADVISOR");
		AdvisorUtils.CreateAdvisorSidebar(background, AdvisorHead.Advisor.Foreign);
	}

	private void CreateTabs() {
		string[] keys = ["treaties", "trades", "details"];
		string[] names = ["Treaties", "Trades", "Details"];
		for (int i = 0; i < 3; i++) {
			TextureRect panel = new() { Position = TabsPosition, Size = PanelSize };
			ImageTexture art = TextureLoader.TryLoad("advisors.foreign.navigation." + keys[i]);
			if (art != null) {
				panel.Texture = art;
			} else {
				// Without the art, a plain panel of the parchment's color.
				ColorRect plain = new() {
					Color = Color.Color8(232, 214, 168),
					Position = new Vector2(0, TabSize.Y),
					Size = PanelSize - new Vector2(0, TabSize.Y),
					MouseFilter = MouseFilterEnum.Ignore,
				};
				panel.AddChild(plain);
			}
			background.AddChild(panel);
			_tabPanels[i] = panel;
		}

		for (int i = 0; i < 3; i++) {
			Tab tab = (Tab)i;
			Button button = new() {
				Text = names[i],
				Flat = true,
				Position = TabsPosition + new Vector2(i * (TabSize.X + 1), 0),
				Size = TabSize,
				CustomMinimumSize = TabSize,
			};
			button.AddThemeFontSizeOverride("font_size", 11);
			button.AddThemeColorOverride("font_color", Colors.Black);
			button.AddThemeColorOverride("font_hover_color", new Color(0.45f, 0, 0.45f));
			button.AddThemeColorOverride("font_pressed_color", Colors.Black);
			button.AddThemeConstantOverride("h_separation", 0);
			button.Pressed += () => {
				_tab = tab;
				ShowAdvisor();
			};
			background.AddChild(button);
			_tabButtons[i] = button;
		}

		_tabTitle = AdvisorUtils.CreateLabel(background, "",
			new Rect2(TabsPosition + new Vector2(8, TabSize.Y + 4), new Vector2(PanelSize.X - 16, 20)), 13, HorizontalAlignment.Center);
		_tabList = AdvisorUtils.CreateList(background,
			new Rect2(TabsPosition + new Vector2(8, TabSize.Y + 26), new Vector2(PanelSize.X - 16, PanelSize.Y - TabSize.Y - 32)));
		_tabList.AddThemeConstantOverride("separation", 3);
	}

	// The checkboxes that turn each kind of line on and off, with Civ3's
	// "Select All" and "All Off".
	private void CreateToggles(Vector2 position) {
		VBoxContainer column = new() { Position = position };
		column.AddThemeConstantOverride("separation", 0);
		foreach (ForeignTieKind kind in Enum.GetValues<ForeignTieKind>()) {
			CheckBox box = new() { Text = KindNames[kind], ButtonPressed = true, CustomMinimumSize = new Vector2(0, 22) };
			box.AddThemeFontSizeOverride("font_size", 12);
			foreach (string state in new[] { "font_color", "font_pressed_color", "font_hover_color", "font_hover_pressed_color", "font_focus_color" }) {
				box.AddThemeColorOverride(state, KindColors[kind]);
			}
			box.Toggled += on => {
				if (on) {
					_shownKinds.Add(kind);
				} else {
					_shownKinds.Remove(kind);
				}
				_web.SetShownKinds(_shownKinds);
			};
			column.AddChild(box);
			_kindBoxes[kind] = box;
		}
		HBoxContainer buttons = new();
		buttons.AddThemeConstantOverride("separation", 6);
		buttons.AddChild(MakeToggleAllButton("Select All", true));
		buttons.AddChild(MakeToggleAllButton("All Off", false));
		column.AddChild(buttons);
		background.AddChild(column);
	}

	private Button MakeToggleAllButton(string text, bool on) {
		Button button = new() { Text = text };
		button.AddThemeFontSizeOverride("font_size", 12);
		button.Pressed += () => {
			foreach (CheckBox box in _kindBoxes.Values) {
				box.ButtonPressed = on;
			}
		};
		return button;
	}

	public void ShowAdvisor() {
		Show();

		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			List<Player> civs = ForeignReport.Civs(gameData, player);
			Player selected = civs.FirstOrDefault(c => c.id == _selected) ?? civs.Skip(1).FirstOrDefault() ?? player;
			_selected = selected.id;

			_web.Fill(gameData, player, civs, ForeignReport.Ties(gameData, player), selected);
			_web.SetShownKinds(_shownKinds);
			FillTab(gameData, player, selected);

			if (!SpeakWith(gameData, player, selected)) {
				_dialogBoxLabel.Text = Advice(gameData, player, civs, selected);
				_advisorHead.Texture = AdvisorHead.GetPopupImage(AdvisorHead.Advisor.Foreign,
					Mood(gameData, player, civs, selected), player.EraIndex());
			}
		});
	}

	// The advisor's next words, in Civ3's script.txt: about the leader
	// picked, if any, and about trade while the Trades tab is open.
	// UNVERIFIED: that the Trades tab is what Civ3's trade view of the
	// advisor's words (FUN_0053f1e0) goes with.
	private void Speak() {
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			SpeakWith(gameData, player, ForeignReport.Civs(gameData, player).FirstOrDefault(c => c.id == _selected));
		});
	}

	private bool SpeakWith(GameData gameData, Player player, Player selected) =>
		AdvisorUtils.Speak(_dialogBoxLabel, _advisorHead, AdvisorHead.Advisor.Foreign, gameData, player,
			selectedForeignCiv: selected == player ? null : selected, foreignTradeView: _tab == Tab.Trades);

	private void FillTab(GameData gameData, Player player, Player selected) {
		for (int i = 0; i < 3; i++) {
			_tabPanels[i].Visible = (int)_tab == i;
		}
		string leader = selected.civilization?.leader;
		string noun = ForeignReport.CivNoun(selected);
		_tabTitle.Text = string.IsNullOrEmpty(leader) ? $"The {noun}" : $"{leader} of the {noun}";

		AdvisorUtils.ClearList(_tabList);
		switch (_tab) {
			case Tab.Treaties:
				AddLines(ForeignReport.Treaties(gameData, player, selected));
				break;
			case Tab.Trades:
				AddHeader("Active");
				if (selected == player) {
					AddLines(["You must select an opponent for which to view active deals."]);
				} else {
					ActiveDeals deals = ActiveDeals.Between(gameData, player, selected);
					if (deals.IsEmpty) {
						AddLines(["You have no active deals with this opponent."]);
					}
					AddLines(deals.agreements);
					AddLines(deals.theyGive.Select(d => $"From the {noun}: {d}"));
					AddLines(deals.weGive.Select(d => $"To the {noun}: {d}"));
					AddHeader("Potential");
					AddLines(ForeignReport.PotentialTrades(gameData, player, selected));
				}
				break;
			case Tab.Details:
				AddLines(ForeignReport.Details(gameData, player, selected));
				break;
		}
	}

	private void AddHeader(string text) {
		Label label = AdvisorUtils.MakeLabel(text, 13);
		label.AddThemeColorOverride("font_color", new Color(0.35f, 0.1f, 0.05f));
		_tabList.AddChild(label);
	}

	private void AddLines(IEnumerable<string> lines) {
		foreach (string line in lines) {
			_tabList.AddChild(AdvisorUtils.MakeWrappedLabel(line, 11, PanelSize.X - 28));
		}
	}

	private static string Advice(GameData gameData, Player player, List<Player> civs, Player selected) {
		if (civs.Count <= 1) {
			return "We know of no other civilizations.";
		}
		List<Player> enemies = civs.Where(c => c != player && PlayerRelationship.AtWar(player, c)).ToList();
		if (selected != player && enemies.Contains(selected)) {
			return $"We are at war with the {ForeignReport.CivNoun(selected)}!";
		}
		if (selected != player && ForeignReport.AttitudeLine(gameData, player, selected) is string attitude) {
			return attitude;
		}
		if (enemies.Count > 0) {
			return $"We are at war with the {ForeignReport.CivNoun(enemies[0])}!";
		}
		return "We are at peace with every civilization we know.";
	}

	// HEURISTIC: the advisor is angry while we're at war, sad when the
	// leader picked is annoyed or furious with us, and happy otherwise.
	private static AdvisorHead.Mood Mood(GameData gameData, Player player, List<Player> civs, Player selected) {
		if (civs.Any(c => c != player && PlayerRelationship.AtWar(player, c))) {
			return AdvisorHead.Mood.Angry;
		}
		AttitudeLevel? attitude = ForeignReport.AttitudeTowardViewer(gameData, player, selected);
		if (attitude is AttitudeLevel.Annoyed or AttitudeLevel.Furious) {
			return AdvisorHead.Mood.Sad;
		}
		return AdvisorHead.Mood.Happy;
	}
}

// The leaders of the civs we know, in a circle, with lines for the ties
// between them. Clicking a leader picks it.
public partial class ForeignWeb : Control {
	public event Action<ID> LeaderPressed;

	private readonly List<(Player civ, Vector2 center)> _leaders = [];
	private List<ForeignTie> _ties = [];
	private HashSet<ForeignTieKind> _shown = [];
	private Player _selected;
	private Player _viewer;
	private float _headSize;
	private readonly HashSet<Player> _atWarWithViewer = [];

	// The rings round the leaders: Civ3's yellow for the one picked
	// (leader_highlight.pcx), red for those at war with us, and a plain one
	// for the rest.
	private static readonly Color SelectedRing = Color.Color8(255, 204, 0);
	private static readonly Color WarRing = Color.Color8(194, 64, 58);
	private static readonly Color PlainRing = Color.Color8(150, 120, 80);
	private static readonly Color HeadBackdrop = Color.Color8(222, 200, 150);

	public ForeignWeb() {
		MouseFilter = MouseFilterEnum.Pass;
	}

	public void SetShownKinds(HashSet<ForeignTieKind> kinds) {
		_shown = [.. kinds];
		QueueRedraw();
	}

	public void Fill(GameData gameData, Player viewer, List<Player> civs, List<ForeignTie> ties, Player selected) {
		foreach (Node child in GetChildren()) {
			RemoveChild(child);
			child.QueueFree();
		}
		_leaders.Clear();
		_atWarWithViewer.Clear();
		_ties = ties;
		_selected = selected;
		_viewer = viewer;

		// The viewer at the bottom, the others round the circle from there.
		// The more civs, the smaller their heads, so they fit.
		// A little above the middle, to leave room for the names under the
		// heads at the bottom.
		Vector2 center = Size / 2 - new Vector2(0, 22);
		int count = civs.Count;
		float radius = count <= 1 ? 0 : Mathf.Min(Size.X, Size.Y) / 2 - 80;
		float spacing = count <= 1 ? float.MaxValue : Mathf.Tau * radius / count;
		_headSize = Mathf.Clamp(spacing * 0.7f, 44, 90);
		float labelWidth = Mathf.Clamp(spacing, 90, 150);

		for (int i = 0; i < count; i++) {
			Player civ = civs[i];
			float angle = Mathf.Pi / 2 + Mathf.Tau * i / count;
			Vector2 position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
			_leaders.Add((civ, position));
			if (civ != viewer && PlayerRelationship.AtWar(viewer, civ)) {
				_atWarWithViewer.Add(civ);
			}

			ID id = civ.id;
			// The head, cut to a circle as Civ3 shows them, some of whose
			// leader heads have a square background.
			Vector2 headBox = new(_headSize, _headSize);
			Panel frame = new() {
				Position = position - headBox / 2,
				Size = headBox,
				ClipChildren = ClipChildrenMode.Only,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			frame.AddThemeStyleboxOverride("panel", CircleStyle(_headSize));
			AddChild(frame);
			// Its face as it feels toward us (ForeignReport.FaceToward).
			Texture2D art = LeaderHead(civ, ForeignReport.FaceToward(gameData, viewer, civ));
			if (art != null) {
				frame.AddChild(new TextureRect {
					Texture = art,
					ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
					Size = headBox,
					MouseFilter = MouseFilterEnum.Ignore,
				});
			} else {
				// Without the art, the civ's initial.
				string noun = ForeignReport.CivNoun(civ);
				Label initial = AdvisorUtils.MakeLabel(noun.Length > 0 ? noun[..1] : "?",
					(int)(_headSize / 2), _headSize, HorizontalAlignment.Center);
				initial.Size = headBox;
				initial.MouseFilter = MouseFilterEnum.Ignore;
				frame.AddChild(initial);
			}
			TextureButton head = new() {
				Position = frame.Position,
				Size = headBox,
				TooltipText = civ.civilization?.leader ?? "",
			};
			head.Pressed += () => LeaderPressed?.Invoke(id);
			AddChild(head);

			// The leader's name, and the civ's with its attitude toward us.
			string attitude = ForeignReport.AttitudeTowardViewer(gameData, viewer, civ) is AttitudeLevel level
				? $" ({ForeignReport.AttitudeWord(level)})" : "";
			Label name = AdvisorUtils.MakeLabel($"{civ.civilization?.leader}\n{AdvisorUtils.CivTitle(civ)}{attitude}",
				11, labelWidth, HorizontalAlignment.Center);
			name.ClipText = false;
			name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			// Clear of the rings drawn round the head (see _Draw: ours is
			// ringed twice, out to RingRadius + 4), which the names touched.
			name.Position = position + new Vector2(-labelWidth / 2, RingRadius(_headSize) + 8);
			name.Size = new Vector2(labelWidth, 30);
			name.MouseFilter = MouseFilterEnum.Ignore;
			// Outlined in the parchment's color, to read over the lines.
			name.AddThemeColorOverride("font_outline_color", HeadBackdrop);
			name.AddThemeConstantOverride("outline_size", 4);
			AddChild(name);
		}
		QueueRedraw();
	}

	// The radius of the ring round a head of the given size.
	private static float RingRadius(float headSize) => headSize / 2 + 4;

	// A circle of the parchment's color, filling a square of the given side.
	private static StyleBoxFlat CircleStyle(float side) {
		StyleBoxFlat style = new() { BgColor = HeadBackdrop, AntiAliasing = true };
		style.SetCornerRadiusAll((int)Mathf.Ceil(side / 2));
		style.CornerDetail = 16;
		return style;
	}

	private static Texture2D LeaderHead(Player civ, LeaderFace face) {
		try {
			return LeaderHeadTextures.Get(civ, face);
		} catch (Exception) {
			// Art that isn't there (TextureLoader logs it) gets the drawn
			// initial instead.
			return null;
		}
	}

	public override void _Draw() {
		Dictionary<Player, Vector2> centers = _leaders.ToDictionary(l => l.civ, l => l.center);
		const float lineWidth = 3;
		const float lineGap = 4;
		foreach (ForeignTie tie in _ties) {
			if (!centers.TryGetValue(tie.first, out Vector2 from) || !centers.TryGetValue(tie.second, out Vector2 to)) {
				continue;
			}
			List<ForeignTieKind> kinds = tie.Kinds().Where(_shown.Contains).ToList();
			if (kinds.Count == 0 || from == to) {
				continue;
			}
			// Each kind a line of its own, side by side.
			Vector2 across = (to - from).Normalized().Orthogonal();
			for (int k = 0; k < kinds.Count; k++) {
				Vector2 offset = across * ((k - (kinds.Count - 1) / 2f) * lineGap);
				DrawLine(from + offset, to + offset, ForeignAdvisor.KindColors[kinds[k]], lineWidth, true);
			}
		}

		float radius = RingRadius(_headSize);
		foreach ((Player civ, Vector2 center) in _leaders) {
			Color ring = civ == _selected ? SelectedRing : _atWarWithViewer.Contains(civ) ? WarRing : PlainRing;
			DrawCircle(center, radius, ring);
			if (civ == _viewer) {
				// Our own leader is ringed twice.
				DrawArc(center, radius + 3, 0, Mathf.Tau, 48, PlainRing, 2, true);
			}
		}
	}
}

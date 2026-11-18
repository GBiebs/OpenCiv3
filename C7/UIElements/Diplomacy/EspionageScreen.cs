using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// Civ3's Espionage screen, opened by the "E" button (Play the World's art,
// Art/Espionage). As the Conquests Civilopedia describes it (GCON_Espionage,
// GCON_Espionage_Missions): "The Espionage screen is comprised of: current
// opponents, available missions, your current asset information, city
// selection, and operational cost options." A mission is sent by picking
// the target civilization in the opponents list ("followed by icons that
// indicate your Espionage status within that civilization": embassy and
// spy), the mission type (Diplomatic or Spy) and the mission, the target
// city "(if appropriate)" and the operational cost, then Execute; Cancel
// or leaving the screen sends nothing.
//
// The places of the parts are the artist's notes on X_ESPIONAGE PARTS.PCX:
// the opponents' buttons from (50, 92), 10 pixels apart, with the embassy
// and spy icons at (308, 106) and (340, 106) on the first; "BOXES START AT
// 402, 117, 11 PIXELS SPACING IN BETWEEN"; the bars of the list at
// (718, 106); and the scroll bars at (14, 95), 590 long, and (977, 106), 340
// long. UNVERIFIED: which list goes where (we put the missions in the
// boxes in the middle and the target cities on the bars at the right, with
// the operational costs on the three bars below them, the mission type on
// the bar under the middle panel and the assets below it). The words are
// Civ3's where labels.txt has them, in its block after "ESPIONAGE" (the
// title): "Select Mission", "Unmet", the assets' "Diplomatic Advance:",
// "Espionage Advance:", "Intelligence Center:", "Spy Experience:",
// "Immediately", "Carefully", "Safely" and "Execute".
public partial class EspionageScreen : Control {
	private static readonly Vector2 WindowSize = new(1024, 768);
	private static readonly Color Parchment = Color.Color8(232, 214, 168);
	private static readonly Color Ink = Color.Color8(40, 28, 16);
	private static readonly Color Faded = Color.Color8(110, 100, 90);
	private static readonly Color Highlight = Color.Color8(230, 140, 30);
	private static readonly Color PickedInk = Color.Color8(150, 60, 0);
	private static readonly Color UnavailableInk = Color.Color8(78, 70, 62);

	private static readonly Vector2 CivListPosition = new(50, 92);
	private static readonly Vector2 CivButtonSize = new(321, 48);
	private const int CivButtonGap = 10;
	// The width of the square tab at the left end of an opponent's button.
	private const float CivButtonTab = 46;
	private static readonly Rect2 CivScrollBar = new(14, 95, 26, 590);

	private static readonly Vector2 FirstBox = new(402, 117);
	private const int BoxPitch = 23 + 11;

	private static readonly Vector2 CityListPosition = new(718, 106);
	private static readonly Vector2 BarSize = new(256, 24);
	private const int BarGap = 6;
	private static readonly Rect2 CityScrollBar = new(977, 106, 20, 340);

	private static readonly Rect2[] RiskBars = [new(717, 523, 276, 30), new(717, 555, 276, 30), new(717, 587, 276, 30)];
	private static readonly Rect2 MissionTypeBar = new(403, 545, 210, 30);
	private static readonly Rect2 AssetsArea = new(400, 584, 272, 92);
	private static readonly Rect2 InfoArea = new(398, 452, 280, 86);

	private enum MissionType {
		Diplomatic,
		Spy,
	}

	// What is picked.
	private ID selectedCiv;
	private MissionType missionType = MissionType.Diplomatic;
	private EspionageMission? selectedMission;
	private City selectedCity;
	private EspionageRisk risk = EspionageRisk.Immediately;

	private VBoxContainer civList;
	private ScrollContainer civScroll;
	private VBoxContainer cityList;
	private ScrollContainer cityScroll;
	private Control missionBoxes;
	private readonly Button[] riskButtons = new Button[3];
	private readonly Panel[] riskFrames = new Panel[3];
	private Button diplomaticButton;
	private Button spyButton;
	private Label assets;
	private Label info;
	private TextureButton execute;
	private TextureButton cancel;
	private Label title;

	public EspionageScreen() {
		MouseFilter = MouseFilterEnum.Stop;
		CustomMinimumSize = WindowSize;
		Size = WindowSize;
	}

	public override void _Ready() {
		AdvisorUtils.CreateUI(this, CreateUI);
	}

	private static ImageTexture Art(string key) => TextureLoader.TryLoad("espionage." + key);

	private void CreateUI() {
		ImageTexture background = Art("background");
		if (background != null) {
			AddChild(new TextureRect { Texture = background, Size = WindowSize, MouseFilter = MouseFilterEnum.Ignore });
		} else {
			// Without Play the World's art, a plain page with the panels
			// drawn in.
			AddChild(new ColorRect { Color = Parchment, Size = WindowSize, MouseFilter = MouseFilterEnum.Ignore });
			Color panel = Color.Color8(150, 135, 105);
			foreach (Rect2 r in new[] { new Rect2(388, 94, 294, 352), new Rect2(703, 94, 304, 588), new Rect2(388, 540, 294, 142) }) {
				AddChild(new ColorRect { Color = panel, Position = r.Position, Size = r.Size, MouseFilter = MouseFilterEnum.Ignore });
			}
		}

		title = AdvisorUtils.CreateLabel(this, "ESPIONAGE", new Rect2(170, 14, 690, 36), 20, HorizontalAlignment.Center);
		title.AddThemeColorOverride("font_color", Ink);

		// The opponents.
		(civScroll, civList) = MakeScrollList(new Rect2(CivListPosition, new Vector2(CivButtonSize.X, CivScrollBar.Size.Y)), CivButtonGap, CivScrollBar);

		// The missions, in the boxes of the middle panel.
		missionBoxes = new Control { Position = Vector2.Zero, Size = WindowSize, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(missionBoxes);

		// The target cities.
		(cityScroll, cityList) = MakeScrollList(new Rect2(CityListPosition, new Vector2(BarSize.X, CityScrollBar.Size.Y)), BarGap, CityScrollBar);

		// The mission type, on the bar under the middle panel.
		diplomaticButton = MakeFlatButton("Diplomatic", new Rect2(MissionTypeBar.Position, new Vector2(MissionTypeBar.Size.X / 2, MissionTypeBar.Size.Y)));
		diplomaticButton.Pressed += () => PickMissionType(MissionType.Diplomatic);
		spyButton = MakeFlatButton("Spy", new Rect2(MissionTypeBar.Position + new Vector2(MissionTypeBar.Size.X / 2, 0),
			new Vector2(MissionTypeBar.Size.X / 2, MissionTypeBar.Size.Y)));
		spyButton.Pressed += () => PickMissionType(MissionType.Spy);

		// The operational costs, on the three bars at the bottom right, in
		// script.txt's order (#ESPIONAGE_SAFETY_LEVEL).
		for (int i = 0; i < 3; i++) {
			EspionageRisk r = (EspionageRisk)i;
			riskButtons[i] = MakeFlatButton("", RiskBars[i]);
			riskButtons[i].Pressed += () => {
				risk = r;
				Refresh();
			};
			riskFrames[i] = MakeFrame(RiskBars[i].Position, RiskBars[i].Size);
			AddChild(riskFrames[i]);
		}

		// Execute and Cancel, under the operational costs.
		execute = MakeBarButton("Execute", new Rect2(718, 636, 132, 26));
		execute.Pressed += Execute;
		cancel = MakeBarButton("Cancel", new Rect2(860, 636, 132, 26));
		cancel.Pressed += () => {
			selectedMission = null;
			Refresh();
		};

		info = AdvisorUtils.CreateLabel(this, "", InfoArea, 12);
		info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		info.VerticalAlignment = VerticalAlignment.Top;
		info.AddThemeColorOverride("font_color", Ink);

		// Five lines, in a font small enough for all of them to fit.
		assets = AdvisorUtils.CreateLabel(this, "", AssetsArea, 11);
		assets.ClipText = false;
		assets.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
		assets.AddThemeConstantOverride("line_spacing", 0);
		assets.VerticalAlignment = VerticalAlignment.Top;
		assets.AddThemeColorOverride("font_color", Parchment);

		TextureButton close = AdvisorUtils.CreateExitButton(this);
		close.Pressed += () => { this.GetParent<Advisors>().Hide(); };
	}

	// An orange frame around something picked.
	private static Panel MakeFrame(Vector2 position, Vector2 size) {
		StyleBoxFlat frame = new() { DrawCenter = false, BorderColor = Highlight };
		frame.SetBorderWidthAll(2);
		Panel panel = new() { Position = position, Size = size, MouseFilter = MouseFilterEnum.Ignore };
		panel.AddThemeStyleboxOverride("panel", frame);
		return panel;
	}

	// A list that scrolls, with its own scroll bar in the background's slot
	// for it.
	private (ScrollContainer, VBoxContainer) MakeScrollList(Rect2 area, int gap, Rect2 barArea) {
		ScrollContainer scroll = new() {
			Position = area.Position,
			Size = area.Size,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
		};
		VBoxContainer list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", gap);
		scroll.AddChild(list);
		AddChild(scroll);

		VScrollBar bar = new() { Position = barArea.Position, Size = barArea.Size, Visible = false };
		AddChild(bar);
		VScrollBar inner = scroll.GetVScrollBar();
		void Sync() {
			bar.MaxValue = inner.MaxValue;
			bar.Page = inner.Page;
			bar.SetValueNoSignal(inner.Value);
			bar.Visible = inner.MaxValue > inner.Page + 1;
		}
		inner.Changed += Sync;
		inner.ValueChanged += _ => Sync();
		bar.ValueChanged += v => scroll.ScrollVertical = (int)v;
		return (scroll, list);
	}

	private Button MakeFlatButton(string text, Rect2 box) {
		Button button = new() { Text = text, Flat = true, Position = box.Position, Size = box.Size, CustomMinimumSize = box.Size };
		button.AddThemeFontSizeOverride("font_size", 12);
		foreach (string state in new[] { "font_color", "font_pressed_color", "font_focus_color" }) {
			button.AddThemeColorOverride(state, Ink);
		}
		button.AddThemeColorOverride("font_hover_color", new Color(0.45f, 0, 0.45f));
		button.AddThemeColorOverride("font_disabled_color", Faded);
		AddChild(button);
		return button;
	}

	// A button drawn with the list's bar, stretched to its size.
	private TextureButton MakeBarButton(string text, Rect2 box) {
		TextureButton button = new() {
			Position = box.Position,
			Size = box.Size,
			CustomMinimumSize = box.Size,
			IgnoreTextureSize = true,
			StretchMode = TextureButton.StretchModeEnum.Scale,
			TextureNormal = Art("bar.normal"),
			TextureHover = Art("bar.selected"),
			TexturePressed = Art("bar.selected"),
			TextureDisabled = Art("bar.disabled"),
		};
		AddFallbackFace(button, box.Size);
		Label label = AdvisorUtils.MakeLabel(text, 13, box.Size.X, HorizontalAlignment.Center);
		label.Size = box.Size;
		label.AddThemeColorOverride("font_color", Ink);
		label.MouseFilter = MouseFilterEnum.Ignore;
		button.AddChild(label);
		AddChild(button);
		return button;
	}

	// Without the art, a button is a plain box.
	private static void AddFallbackFace(TextureButton button, Vector2 size) {
		if (button.TextureNormal == null) {
			button.AddChild(new ColorRect { Color = Color.Color8(200, 184, 140), Size = size, MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true });
		}
	}

	// Shows the screen, with the civ (and city) picked if given, as from a
	// foreign city's menu.
	public void ShowScreen(ID civ = null, City city = null) {
		if (civ != null) {
			selectedCiv = civ;
			selectedCity = city;
			selectedMission = null;
		}
		Show();
		Refresh();
	}

	// After a mission was sent, or the game changed: draws the screen again.
	public void Refresh() {
		if (!IsInsideTree() || civList == null) {
			return;
		}
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			if (player == null) {
				return;
			}
			List<Player> civs = Opponents(gameData, player);
			List<Player> met = civs.Where(c => Met(player, c)).ToList();
			Player target = met.FirstOrDefault(c => c.id == selectedCiv)
				?? met.FirstOrDefault(c => Espionage.HasEmbassy(player, c)) ?? met.FirstOrDefault();
			if (target?.id != selectedCiv) {
				selectedCity = null;
				selectedMission = null;
			}
			selectedCiv = target?.id;

			FillCivs(gameData, player, civs, target);
			FillMissions(gameData, player, target);
			FillCities(gameData, player, target);
			FillRisks(gameData, player, target);
			FillAssets(gameData, player);
		});
	}

	// The other civs in the game, met or not, in the game's order. Those
	// we haven't met are listed but not named (labels.txt has "Unmet" among
	// the Espionage screen's words).
	private static List<Player> Opponents(GameData gameData, Player player) {
		return gameData.players.Where(p => p != player && !p.defeated && !p.IsIndependent).ToList();
	}

	private static bool Met(Player player, Player civ) => PlayerRelationship.TryGetRelationship(player, civ, out _);

	private const string UnmetName = "Unmet Player";

	private void FillCivs(GameData gameData, Player player, List<Player> civs, Player target) {
		AdvisorUtils.ClearList(civList);
		foreach (Player civ in civs) {
			if (!Met(player, civ)) {
				civList.AddChild(MakeUnmetButton());
				continue;
			}
			bool selected = civ == target;
			TextureButton button = new() {
				CustomMinimumSize = CivButtonSize,
				TextureNormal = Art(selected ? "civ.selected" : "civ.normal"),
				TextureHover = Art(selected ? "civ.selected" : "civ.hover"),
				TexturePressed = Art("civ.selected"),
				TooltipText = EmbassyReport(gameData, player, civ),
			};
			AddFallbackFace(button, CivButtonSize);
			AddLeaderHead(button, civ);
			// The name goes on the bar, right of the square tab at the
			// button's left end, and stops short of the icons.
			float nameLeft = CivButtonTab + 10;
			Label name = AdvisorUtils.MakeLabel(AdvisorUtils.CivTitle(civ), 15, 252 - nameLeft);
			name.Position = new Vector2(nameLeft, 3);
			name.Size = new Vector2(252 - nameLeft, CivButtonSize.Y - 6);
			name.AddThemeColorOverride("font_color", selected ? PickedInk : Ink);
			name.MouseFilter = MouseFilterEnum.Ignore;
			button.AddChild(name);
			// The civ picked is framed, as the operational cost picked is,
			// since the art's own highlight is faint.
			if (selected) {
				button.AddChild(MakeFrame(new Vector2(CivButtonTab - 2, 2), CivButtonSize - new Vector2(CivButtonTab, 6)));
			}
			// "If the Embassy Icon is displayed, you currently have an
			// embassy within that civilization's capitol city. If the Spy
			// icon is present, you have a spy planted within that
			// civilization."
			AddIcon(button, "embassy", Espionage.HasEmbassy(player, civ), new Vector2(258, 14), "An Embassy");
			AddIcon(button, "spy", Espionage.HasSpy(player, civ), new Vector2(290, 14), "A Spy");
			ID id = civ.id;
			button.Pressed += () => {
				selectedCiv = id;
				Refresh();
			};
			civList.AddChild(button);
		}
	}

	// The inside of the square tab at the left end of an opponent's
	// button, within its bevel.
	private static readonly Rect2 HeadBox = new(5, 5, CivButtonTab - 10, 48 - 10);

	// The civ's leader head (for its era, as on the diplomacy screens) in
	// the square tab, filling it.
	private static void AddLeaderHead(Control button, Player civ) {
		Texture2D head;
		try {
			head = LeaderHeadTextures.Get(civ);
		} catch (Exception) {
			// Art that isn't there (TextureLoader logs it): an empty tab.
			return;
		}
		if (head == null) {
			return;
		}
		Control frame = new() {
			Position = HeadBox.Position,
			Size = HeadBox.Size,
			ClipContents = true,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		frame.AddChild(new TextureRect {
			Texture = head,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			Size = HeadBox.Size,
			MouseFilter = MouseFilterEnum.Ignore,
		});
		button.AddChild(frame);
	}

	// An opponent we haven't met: its button, greyed, that can't be picked.
	private static TextureButton MakeUnmetButton() {
		TextureButton button = new() {
			CustomMinimumSize = CivButtonSize,
			TextureNormal = Art("civ.normal"),
			Disabled = true,
			MouseDefaultCursorShape = CursorShape.Arrow,
		};
		AddFallbackFace(button, CivButtonSize);
		float nameLeft = CivButtonTab + 10;
		Label name = AdvisorUtils.MakeLabel(UnmetName, 15, 252 - nameLeft);
		name.Position = new Vector2(nameLeft, 3);
		name.Size = new Vector2(252 - nameLeft, CivButtonSize.Y - 6);
		name.AddThemeColorOverride("font_color", UnavailableInk);
		name.MouseFilter = MouseFilterEnum.Ignore;
		button.AddChild(name);
		return button;
	}

	private static void AddIcon(Control parent, string key, bool shown, Vector2 position, string fallback) {
		if (!shown) {
			return;
		}
		ImageTexture art = Art(key);
		if (art != null) {
			parent.AddChild(new TextureRect { Texture = art, Position = position, MouseFilter = MouseFilterEnum.Ignore });
		} else {
			Label label = AdvisorUtils.MakeLabel(key == "embassy" ? "E" : "S", 13);
			label.Position = position;
			label.MouseFilter = MouseFilterEnum.Ignore;
			parent.AddChild(label);
		}
	}

	// What our embassy reported of their capital when it was established.
	private static string EmbassyReport(GameData gameData, Player player, Player civ) {
		if (!PlayerRelationship.TryGetRelationship(player, civ, out PlayerRelationship pr) || !pr.hasEmbassy) {
			return $"We have no embassy with the {civ.civilization.noun}.";
		}
		Espionage.CityReport report = pr.embassyReport;
		if (report == null) {
			return $"We have an embassy with the {civ.civilization.noun}.";
		}
		return $"Our embassy in {report.cityName} reported in {gameData.timeOptions.GetDisplayTime(report.turn)}:\n"
			+ Espionage.DescribeReport(report);
	}

	private static IEnumerable<EspionageMission> MissionsOf(MissionType type) {
		return Espionage.Missions.Where(m => Espionage.IsEspionageMission(m) == (type == MissionType.Spy));
	}

	// The mission's option for the target, at the risk picked: for a city
	// mission, against the city picked (their capital until one is).
	private Espionage.MissionOption Option(GameData gameData, Player player, Player target, EspionageMission mission, EspionageRisk r) {
		City city = Espionage.TargetsCity(mission) ? selectedCity ?? Espionage.Capital(target) : null;
		return Espionage.GetOption(gameData, player, mission, target, city, r);
	}

	private void FillMissions(GameData gameData, Player player, Player target) {
		foreach (Node child in missionBoxes.GetChildren()) {
			missionBoxes.RemoveChild(child);
			child.QueueFree();
		}
		diplomaticButton.ButtonPressed = missionType == MissionType.Diplomatic;
		spyButton.ButtonPressed = missionType == MissionType.Spy;
		diplomaticButton.AddThemeColorOverride("font_color", missionType == MissionType.Diplomatic ? Ink : Faded);
		spyButton.AddThemeColorOverride("font_color", missionType == MissionType.Spy ? Ink : Faded);

		int row = 0;
		foreach (EspionageMission mission in MissionsOf(missionType)) {
			Vector2 position = FirstBox + new Vector2(0, row++ * BoxPitch);
			bool picked = selectedMission == mission;
			Espionage.MissionOption option = target == null ? null : Option(gameData, player, target, mission, EspionageRisk.Immediately);
			bool available = option?.available ?? false;
			TextureButton box = new() {
				Position = position,
				CustomMinimumSize = new Vector2(24, 23),
				TextureNormal = Art(picked ? "box.checked" : "box.normal"),
				TextureHover = Art(picked ? "box.checked_hover" : "box.hover"),
				TooltipText = option?.reason ?? "",
			};
			AddFallbackFace(box, new Vector2(24, 23));
			Button label = new() {
				Text = Espionage.Describe(mission),
				Flat = true,
				Alignment = HorizontalAlignment.Left,
				Position = position + new Vector2(30, -2),
				Size = new Vector2(240, 27),
				TooltipText = option?.reason ?? "",
			};
			label.AddThemeFontSizeOverride("font_size", 14);
			// Dark on the stone of the panel: black if it can be sent, grey if
			// not, and dark orange when picked.
			Color color = picked ? PickedInk : available ? Colors.Black : UnavailableInk;
			foreach (string state in new[] { "font_color", "font_pressed_color", "font_focus_color" }) {
				label.AddThemeColorOverride(state, color);
			}
			label.AddThemeColorOverride("font_hover_color", PickedInk);
			void Pick() {
				selectedMission = picked ? null : mission;
				Refresh();
			}
			box.Pressed += Pick;
			label.Pressed += Pick;
			missionBoxes.AddChild(box);
			missionBoxes.AddChild(label);
		}

		if (target == null) {
			info.Text = "";
		} else if (selectedMission is EspionageMission chosen) {
			Espionage.MissionOption option = Option(gameData, player, target, chosen, risk);
			info.Text = option.available
				? $"{Espionage.Describe(chosen)} against the {target.civilization.noun}"
					+ (Espionage.TargetsCity(chosen) ? $" in {(selectedCity ?? Espionage.Capital(target))?.name}" : "") + "."
				: option.reason;
		} else {
			info.Text = "Select Mission";
		}
	}

	// The cities of the target that a city mission may be sent to: those we
	// know of. Only for city missions.
	private void FillCities(GameData gameData, Player player, Player target) {
		AdvisorUtils.ClearList(cityList);
		if (target == null || selectedMission is not EspionageMission mission || !Espionage.TargetsCity(mission)) {
			return;
		}
		City picked = selectedCity ?? Espionage.Capital(target);
		foreach (City city in target.cities.Where(c => player.tileKnowledge.isTileKnown(c.location))
				.OrderByDescending(c => c.IsCapital()).ThenBy(c => c.name)) {
			bool selected = city == picked;
			TextureButton bar = new() {
				CustomMinimumSize = BarSize,
				TextureNormal = Art(selected ? "bar.selected" : "bar.normal"),
				TextureHover = Art(selected ? "bar.selected" : "bar.hover"),
			};
			AddFallbackFace(bar, BarSize);
			Label name = AdvisorUtils.MakeLabel(city.name, 13, BarSize.X - 16);
			name.Position = new Vector2(8, 0);
			name.Size = new Vector2(BarSize.X - 16, BarSize.Y);
			name.AddThemeColorOverride("font_color", Ink);
			name.MouseFilter = MouseFilterEnum.Ignore;
			bar.AddChild(name);
			City c = city;
			bar.Pressed += () => {
				selectedCity = c;
				Refresh();
			};
			cityList.AddChild(bar);
		}
	}

	// The operational costs of the mission picked: "[Immediately] ($NUM0
	// gold - low chance of success)", "[Carefully] ($NUM1 gold - moderate
	// chance of success)", "[Safely] ($NUM2 gold - high chance of success)"
	// (script.txt, #ESPIONAGE_SAFETY_LEVEL), with the chance in numbers.
	// Only Immediately where the risk makes no difference (see
	// Espionage.RiskMatters).
	private void FillRisks(GameData gameData, Player player, Player target) {
		EspionageMission? mission = selectedMission;
		bool choice = mission is EspionageMission m && Espionage.RiskMatters(m);
		if (!choice) {
			risk = EspionageRisk.Immediately;
		}
		Espionage.MissionOption chosen = null;
		for (int i = 0; i < 3; i++) {
			EspionageRisk r = (EspionageRisk)i;
			string name = r.ToString();
			Button button = riskButtons[i];
			bool enabled = target != null && mission != null && (choice || r == EspionageRisk.Immediately);
			button.Disabled = !enabled;
			riskFrames[i].Visible = enabled && r == risk;
			if (target == null || mission == null) {
				button.Text = name;
				continue;
			}
			Espionage.MissionOption option = Option(gameData, player, target, mission.Value, r);
			if (r == risk) {
				chosen = option;
			}
			string chance = option.successPercent >= 100 ? "" : $", {option.successPercent}% chance";
			// A mission that can't be sent has no price to show (its
			// reason is above the mission type).
			button.Text = enabled && option.available ? $"{name} ({option.cost} gold{chance})" : name;
		}
		execute.Disabled = chosen == null || !chosen.available;
		execute.Modulate = execute.Disabled ? new Color(1, 1, 1, 0.5f) : Colors.White;
		cancel.Disabled = mission == null;
	}

	// "The assets section provides a summary of your current monetary and
	// technological assets, including the amount of gold in your treasury,
	// your technological achievements (those that apply to espionage), and
	// your level of spy experience."
	// In Civ3's words (labels.txt, after "ESPIONAGE"): "Diplomatic
	// Advance:", "Espionage Advance:", "Intelligence Center:" and "Spy
	// Experience:", answered "Yes" or "No". The advances are the one that
	// enables embassies (Writing) and the one the spy missions' building
	// needs (Espionage), as the BIQ has them. UNVERIFIED: the spy
	// experience's wording (the experience level's name).
	private void FillAssets(GameData gameData, Player player) {
		static string YesNo(bool yes) => yes ? "Yes" : "No";
		bool Knows(Tech tech) => tech != null && player.knownTechs.Contains(tech.id);
		Tech diplomatic = Tech.FindEmbassyTech(gameData.techs);
		Tech espionage = gameData.Buildings.FirstOrDefault(b => b.allowsSpyMissions)?.requiredTech;
		List<string> lines = [
			$"Treasury: {player.gold} gold",
			$"Diplomatic Advance: {YesNo(Knows(diplomatic))}",
			$"Espionage Advance: {YesNo(Knows(espionage))}",
			$"Intelligence Center: {YesNo(Espionage.HasIntelligenceAgency(player))}",
			$"Spy Experience: {Espionage.SpyLevelName(Espionage.SpyLevel(player))}",
		];
		assets.Text = string.Join("\n", lines);
	}

	private void PickMissionType(MissionType type) {
		if (type != missionType) {
			missionType = type;
			selectedMission = null;
		}
		Refresh();
	}

	private void Execute() {
		if (selectedMission is not EspionageMission mission) {
			return;
		}
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			Player target = gameData.GetPlayer(selectedCiv);
			if (player == null || target == null) {
				return;
			}
			City city = Espionage.TargetsCity(mission) ? selectedCity ?? Espionage.Capital(target) : null;
			if (!Option(gameData, player, target, mission, risk).available) {
				return;
			}
			new MsgPerformEspionage(mission, target, city, risk).send();
		});
		selectedMission = null;
	}

	// Lets go of the game's objects after the game was replaced (as by a LAN
	// snapshot); the civ picked is kept by its id.
	public void ForgetGameObjects() {
		selectedCity = null;
	}
}

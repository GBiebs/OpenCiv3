using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

[GlobalClass]
[Tool]
public partial class MilitaryAdvisor : Control {

	[Export] public TextureRect background;

	private TextureButton _close;
	private TextureRect _advisorHead;
	private TextureButton _dialogBox;
	private Label _dialogBoxLabel;

	private Label _totalUnitsLabel;
	private Label _allowedUnitsLabel;
	private Label _unitSupportCostLabel;

	private Label _armyHeader;
	private VBoxContainer _intelligenceList;
	private VBoxContainer _armyList;
	private VBoxContainer _productionList;

	private Button _viewByCity;
	private Button _viewByUnit;
	private bool _viewingByCity = false;

	// The civ picked in the Military Intelligence box, whose army is listed
	// and which the advisor compares ours with, as in Civ3 (its military
	// advisor keeps the civ picked, FUN_00581a80, which its speech,
	// FUN_0042b3d0, compares with). Null for our own.
	private ID _selectedCiv;

	// The height of a list row, so rows sit on the background's ruled lines.
	private const float RowHeight = 45;

	public MilitaryAdvisor() {
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _Ready() {
		AdvisorUtils.CreateUI(this, CreateUI);
	}

	private void CreateUI() {
		background.Texture = TextureLoader.Load("advisors.military.background");

		_advisorHead = AdvisorUtils.CreateAdvisorHead(background, AdvisorHead.Advisor.Military);
		_close = AdvisorUtils.CreateExitButton(background);
		_close.Pressed += () => { this.GetParent<Advisors>().Hide(); };
		(_dialogBox, _dialogBoxLabel) = AdvisorUtils.CreateAdvisorDialogBox(background);
		AdvisorUtils.SpeakOnClickAndInTime(this, _dialogBox, Speak);

		AdvisorUtils.CreateAdvisorTitle(background, background.Texture.GetWidth(), "MILITARY ADVISOR");
		AdvisorUtils.CreateAdvisorSidebar(background, AdvisorHead.Advisor.Military);

		// What we know of the other civs' militaries, top left.
		AdvisorUtils.CreateLabel(background, "Military Intelligence", new Rect2(88, 94, 280, 20), 14, HorizontalAlignment.Center);
		_intelligenceList = AdvisorUtils.CreateList(background, new Rect2(86, 116, 284, 114));

		// The three coloured boxes in the middle.
		_totalUnitsLabel = AdvisorUtils.CreateLabel(background, "", new Rect2(388, 89, 150, 44), 12, HorizontalAlignment.Center);
		_allowedUnitsLabel = AdvisorUtils.CreateLabel(background, "", new Rect2(388, 137, 150, 46), 12, HorizontalAlignment.Center);
		_unitSupportCostLabel = AdvisorUtils.CreateLabel(background, "", new Rect2(388, 186, 150, 46), 12, HorizontalAlignment.Center);

		// The yellow box on the right picks how the army is listed.
		AdvisorUtils.CreateLabel(background, "View by", new Rect2(586, 118, 166, 20), 13, HorizontalAlignment.Center);
		_viewByCity = MakeToggle("City", new Vector2(600, 148), true);
		_viewByUnit = MakeToggle("Unit", new Vector2(672, 148), false);

		_armyHeader = AdvisorUtils.CreateLabel(background, "", new Rect2(101, 250, 418, 39), 14, HorizontalAlignment.Center);
		_armyList = AdvisorUtils.CreateList(background, new Rect2(105, 292, 414, 360));

		AdvisorUtils.CreateLabel(background, "Unit in production", new Rect2(560, 250, 396, 39), 14, HorizontalAlignment.Center);
		_productionList = AdvisorUtils.CreateList(background, new Rect2(560, 292, 396, 360));
	}

	private Button MakeToggle(string text, Vector2 position, bool byCity) {
		Button button = new() {
			Text = text,
			ToggleMode = true,
			Position = position,
			CustomMinimumSize = new Vector2(64, 28),
		};
		button.Pressed += () => {
			_viewingByCity = byCity;
			ShowAdvisor();
		};
		background.AddChild(button);
		return button;
	}

	public void ShowAdvisor() {
		// The advisor speaks as the screen opens, not when it's redrawn.
		bool opening = !IsVisibleInTree();
		Show();

		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			var (totalUnits, allowedUnits, unitSupportCost) = player.TotalUnitsAllowedUnitsAndSupportCost();

			_totalUnitsLabel.Text = $"Total Units\n{totalUnits}";
			_allowedUnitsLabel.Text = $"Allowed Units\n{allowedUnits}";
			_unitSupportCostLabel.Text = $"Army Support Cost\n{unitSupportCost} gold/turn";

			// A civ picked that we no longer know (or that's gone) is let go.
			Player rival = SelectedRival(gameData, player);
			_selectedCiv = rival?.id;

			// A rival's army is listed by unit: we don't know where it is.
			_viewByCity.Disabled = rival != null;
			_viewByUnit.Disabled = rival != null;
			_viewByCity.SetPressedNoSignal(_viewingByCity && rival == null);
			_viewByUnit.SetPressedNoSignal(!_viewingByCity || rival != null);
			Player shown = rival ?? player;
			string intelligence = MilitaryReport.Intelligence(player, shown);
			_armyHeader.Text = $"The Army of the {AdvisorUtils.CivName(shown)}" + (intelligence.Length > 0 ? $"\n{intelligence}" : "");

			FillIntelligence(gameData, player, rival);
			if (rival != null) {
				FillArmyByUnit(gameData, rival, MilitaryReport.KnownUnits(player, rival), ours: false);
				AdvisorUtils.ClearList(_productionList);
			} else {
				if (_viewingByCity) {
					FillArmyByCity(player);
				} else {
					FillArmyByUnit(gameData, player, player.units);
				}
				FillProduction(player);
			}

			if (opening ? !AdvisorUtils.Speak(_dialogBoxLabel, _advisorHead, AdvisorHead.Advisor.Military, gameData, player,
						militaryRival: rival)
					: !AdvisorUtils.HasScript()) {
				_dialogBoxLabel.Text = Advice(gameData, player, totalUnits, allowedUnits, unitSupportCost);
				_advisorHead.Texture =
					AdvisorHead.GetPopupImage(AdvisorHead.Advisor.Military, AdvisorHead.Mood.Happy, player.EraIndex());
			}
		});
	}

	// The advisor's next words, in Civ3's script.txt, comparing our army
	// with that of the civ picked, if any.
	private void Speak() {
		EngineStorage.ReadGameData((GameData gameData) => {
			Player player = gameData.GetUIControllerPlayer();
			AdvisorUtils.Speak(_dialogBoxLabel, _advisorHead, AdvisorHead.Advisor.Military, gameData, player,
				militaryRival: SelectedRival(gameData, player));
		});
	}

	private Player SelectedRival(GameData gameData, Player player) =>
		_selectedCiv == null ? null : gameData.GetKnownRivals(player).FirstOrDefault(r => r.id == _selectedCiv && !r.defeated);

	// Picks a civ to show the army of, or ours with null.
	private void Pick(ID civ) {
		_selectedCiv = civ;
		ShowAdvisor();
		// The advisor weighs the armies up as soon as a civ is picked.
		Speak();
	}

	private void FillIntelligence(GameData gameData, Player player, Player selected) {
		AdvisorUtils.ClearList(_intelligenceList);
		List<Player> rivals = gameData.GetKnownRivals(player);
		if (rivals.Count == 0) {
			_intelligenceList.AddChild(AdvisorUtils.MakeLabel("We know of no other civilizations", 12));
			return;
		}
		// Our own army first, to go back to it.
		_intelligenceList.AddChild(AdvisorUtils.MakeRow(22,
			AdvisorUtils.MakeSpacer(11),
			MakeCivButton(AdvisorUtils.CivTitle(player), selected == null, () => Pick(null))));
		foreach (Player rival in rivals) {
			bool atWar = PlayerRelationship.AtWar(player, rival);
			string strength = player.CompareMilitaryStrengthTo(rival) switch {
				Player.MilitaryStrength.WeakTo => "They are stronger",
				Player.MilitaryStrength.StrongTo => "They are weaker",
				_ => "We are even",
			};
			ID id = rival.id;
			_intelligenceList.AddChild(AdvisorUtils.MakeRow(22,
				AdvisorUtils.MakeIcon(StatusIcon(atWar), 11),
				MakeCivButton(AdvisorUtils.CivTitle(rival), rival == selected, () => Pick(id)),
				AdvisorUtils.MakeLabel(atWar ? "At War" : "At Peace", 12, 60),
				AdvisorUtils.MakeLabel(strength, 12)));
		}
	}

	// A civ's name in the Military Intelligence box, which picks it; the
	// one picked is in blue. UNVERIFIED: how Civ3 marks the civ picked.
	private static Button MakeCivButton(string name, bool picked, System.Action pick) {
		Button button = new() {
			Text = name,
			Flat = true,
			Alignment = HorizontalAlignment.Left,
			ClipText = true,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			CustomMinimumSize = new Vector2(92, 0),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
		};
		Color color = picked ? new Color(0.1f, 0.15f, 0.6f) : Colors.Black;
		button.AddThemeColorOverride("font_color", color);
		button.AddThemeColorOverride("font_hover_color", new Color(0.45f, 0, 0.45f));
		button.AddThemeColorOverride("font_pressed_color", color);
		button.AddThemeColorOverride("font_focus_color", color);
		button.AddThemeFontSizeOverride("font_size", 12);
		foreach (string state in new[] { "normal", "hover", "pressed", "focus" }) {
			button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
		}
		button.Pressed += pick;
		return button;
	}

	// The icons drawn when the art of the war and peace icons is missing (as
	// in a standalone release that shipped without it, owner's report,
	// 2026-10-12), by whether at war.
	private static readonly Dictionary<bool, ImageTexture> drawnStatusIcons = [];

	// The icon of whether we're at war or peace with a civ: Civ3's red dot
	// for war and red ring for peace.
	private static Texture2D StatusIcon(bool atWar) {
		ImageTexture art = TextureLoader.TryLoad(atWar ? "advisors.military.status.war" : "advisors.military.status.peace");
		if (art != null) {
			return art;
		}
		if (!drawnStatusIcons.TryGetValue(atWar, out ImageTexture drawn)) {
			drawn = DrawStatusIcon(atWar);
			drawnStatusIcons[atWar] = drawn;
		}
		return drawn;
	}

	private static ImageTexture DrawStatusIcon(bool atWar) {
		const int size = 11;
		Color red = Color.Color8(205, 30, 30);
		Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		Vector2 center = new Vector2(size, size) / 2;
		for (int y = 0; y < size; y++) {
			for (int x = 0; x < size; x++) {
				float distance = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(center);
				if (distance <= size / 2f && (atWar || distance > size / 2f - 1.5f)) {
					image.SetPixel(x, y, red);
				} else if (distance <= size / 2f) {
					image.SetPixel(x, y, Colors.White);
				}
			}
		}
		return ImageTexture.CreateFromImage(image);
	}

	private void FillArmyByUnit(GameData gameData, Player player, IEnumerable<MapUnit> known, bool ours = true) {
		AdvisorUtils.ClearList(_armyList);
		List<(UnitPrototype type, bool slaves, List<MapUnit> units)> groups = MilitaryReport.UnitsByType(gameData, known);
		foreach ((UnitPrototype type, bool slaves, List<MapUnit> units) in groups) {
			_armyList.AddChild(AdvisorUtils.MakeRow(RowHeight,
				AdvisorUtils.MakeSpacer(4),
				AdvisorUtils.MakeIcon(UnitIcon(type, player), 32),
				AdvisorUtils.MakeLabel(MilitaryReport.GroupName(type, slaves), 13, 170),
				AdvisorUtils.MakeLabel($"{type.attack}.{type.defense}.{type.movement}", 13, 70, HorizontalAlignment.Center),
				AdvisorUtils.MakeLabel($"{units.Count} {(units.Count == 1 ? "unit" : "units")}", 13, 90, HorizontalAlignment.Right)));
		}
		// For a rival, the header says what we know, if nothing.
		if (groups.Count == 0 && ours) {
			_armyList.AddChild(AdvisorUtils.MakeRow(RowHeight, AdvisorUtils.MakeSpacer(4), AdvisorUtils.MakeLabel("We have no units.")));
		}
	}

	private void FillArmyByCity(Player player) {
		AdvisorUtils.ClearList(_armyList);
		foreach ((City city, List<MapUnit> units) in MilitaryReport.UnitsByCity(player)) {
			Control name = city != null
				? AdvisorUtils.MakeCityButton(this, city, 130)
				: AdvisorUtils.MakeLabel("In the field", 13, 130);
			string summary = string.Join(", ", units
				.GroupBy(u => (type: u.unitType, slaves: MilitaryReport.IsSlave(u)))
				.Select(g => {
					string groupName = MilitaryReport.GroupName(g.Key.type, g.Key.slaves);
					return g.Count() == 1 ? groupName : $"{g.Count()} {groupName}";
				}));
			_armyList.AddChild(AdvisorUtils.MakeRow(RowHeight,
				AdvisorUtils.MakeSpacer(4),
				name,
				AdvisorUtils.MakeLabel(summary, 12, 262)));
		}
		if (player.units.Count == 0) {
			_armyList.AddChild(AdvisorUtils.MakeRow(RowHeight, AdvisorUtils.MakeSpacer(4), AdvisorUtils.MakeLabel("We have no units.")));
		}
	}

	private void FillProduction(Player player) {
		AdvisorUtils.ClearList(_productionList);
		foreach ((City city, UnitPrototype unit) in MilitaryReport.UnitsInProduction(player)) {
			int turns = city.TurnsUntilProductionFinished();
			_productionList.AddChild(AdvisorUtils.MakeRow(RowHeight,
				AdvisorUtils.MakeSpacer(4),
				AdvisorUtils.MakeCityButton(this, city, 130),
				AdvisorUtils.MakeIcon(UnitIcon(unit, player), 32),
				AdvisorUtils.MakeLabel(unit.name, 13, 130),
				AdvisorUtils.MakeLabel($"{turns} {(turns == 1 ? "turn" : "turns")}", 13, 60, HorizontalAlignment.Right)));
		}
	}

	private static ImageTexture UnitIcon(UnitPrototype unit, Player player) =>
		TextureLoader.Load("unit_icons", new ItemContext(unit, player), useCache: true);

	private static string Advice(GameData gameData, Player player, int totalUnits, int allowedUnits, int supportCost) {
		List<Player> enemies = gameData.GetKnownRivals(player).Where(r => PlayerRelationship.AtWar(player, r)).ToList();
		if (enemies.Count > 0) {
			return $"We are at war with the {AdvisorUtils.CivName(enemies[0])}!";
		}
		if (supportCost > 0) {
			return $"Our army costs {supportCost} gold a turn.";
		}
		if (totalUnits < allowedUnits) {
			return "We can support more units for free.";
		}
		return "Our army is at full strength.";
	}
}

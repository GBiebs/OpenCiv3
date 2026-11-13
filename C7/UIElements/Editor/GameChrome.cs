using System;
using System.Collections.Generic;
using C7GameData;
using Godot;

// The game's look: the editor's controls where the game has its own. The
// game's menu, Civilopedia and advisor buttons at the top left (the menu,
// the rules, the scenario's properties), the brushes as a row of the game's
// round command buttons at the bottom, the game's info box at the bottom
// right telling the brush and the tile under the mouse, and dialogs framed
// in stone and parchment like the game's setup screens.
public partial class GameChrome : EditorChrome {
	private Label titleLabel, tileLabel, toolLabel, messageLabel;
	private readonly Dictionary<EditorTool, BaseButton> toolButtons = [];
	private readonly Dictionary<int, Civ3MenuButton> sizeButtons = [];
	private Control menuPopup;

	public GameChrome(ScenarioEditor editor) : base(editor) { }

	public override void Build(List<PopupMenu> menus) {
		// The menus are offered on the game's menu popup instead (see
		// ShowMenu), but stay here so their keys work.
		foreach (PopupMenu menu in menus) {
			StyleMenu(menu);
			AddChild(menu);
		}

		HBoxContainer upperLeft = new() { Position = new Vector2(8, 6) };
		upperLeft.AddThemeConstantOverride("separation", 4);
		upperLeft.AddChild(NavButton("upper_left_navigation.menu", "Menu: new, open, save, the grid, the look", ShowMenu));
		upperLeft.AddChild(NavButton("upper_left_navigation.civilopedia", "The rules (Phase 5)",
			() => ShowMessage("The rules editor comes in Phase 5.", false)));
		upperLeft.AddChild(NavButton("upper_left_navigation.advisor.normal", "Scenario properties (Phase 4)",
			() => ShowMessage("Scenario properties come in Phase 4.", false)));
		AddChild(upperLeft);

		// The brushes, where the game has its unit commands.
		HBoxContainer brushes = new() { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
		brushes.AddThemeConstantOverride("separation", 4);
		brushes.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
		brushes.GrowHorizontal = GrowDirection.Both;
		brushes.GrowVertical = GrowDirection.Begin;
		brushes.OffsetBottom = -8;
		foreach (EditorTools.Info tool in EditorTools.All) {
			BaseButton button = BrushButton(tool);
			button.Pressed += () => Run(ScenarioEditor.ToolItem(tool.Tool));
			toolButtons[tool.Tool] = button;
			brushes.AddChild(button);
		}
		AddChild(brushes);

		// The game's info box, bottom right.
		TextureRect box = new() { Texture = TextureLoader.Load("lower_right_infobox.box"), MouseFilter = MouseFilterEnum.Stop };
		box.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
		box.GrowHorizontal = GrowDirection.Begin;
		box.GrowVertical = GrowDirection.Begin;
		VBoxContainer lines = new() { Position = new Vector2(44, 16), Size = new Vector2(220, 108) };
		lines.AddThemeConstantOverride("separation", 0);
		titleLabel = BoxLabel();
		tileLabel = BoxLabel();
		tileLabel.ClipText = false;
		tileLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		tileLabel.MaxLinesVisible = 2;
		toolLabel = BoxLabel();
		toolLabel.CustomMinimumSize = Vector2.Zero;
		toolLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		messageLabel = BoxLabel();
		lines.AddChild(titleLabel);
		lines.AddChild(tileLabel);
		// The brush, and its sizes beside it.
		HBoxContainer toolRow = new();
		toolRow.AddThemeConstantOverride("separation", 0);
		toolRow.AddChild(toolLabel);
		ButtonGroup group = new();
		foreach (int size in EditorTools.Sizes) {
			Civ3MenuButton button = new() {
				Text = size.ToString(),
				FontSize = 12,
				ToggleMode = true,
				ButtonGroup = group,
				TooltipText = $"A brush {size} tiles across",
				textPosition = Civ3MenuButton.TextPosition.TextRightOfIcon,
			};
			button.Pressed += () => Run(ScenarioEditor.SizeItem(size));
			sizeButtons[size] = button;
			toolRow.AddChild(button);
		}
		lines.AddChild(toolRow);
		lines.AddChild(messageLabel);
		box.AddChild(lines);
		AddChild(box);
	}

	private static TextureButton NavButton(string key, string tooltip, Action pressed) {
		TextureButton button = new() { TextureNormal = TextureLoader.Load(key), TooltipText = tooltip, FocusMode = FocusModeEnum.None };
		button.Pressed += pressed;
		return button;
	}

	// A brush as the game's round command buttons; resources, which have no
	// such button, as a resource's own picture.
	private static BaseButton BrushButton(EditorTools.Info tool) {
		TextureButton button = new() {
			ToggleMode = true,
			TooltipText = tool.Tooltip,
			FocusMode = FocusModeEnum.None,
		};
		if (tool.Tool == EditorTool.Resources) {
			button.TextureNormal = EditorTools.Icon(tool.Tool);
			button.IgnoreTextureSize = true;
			button.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
			button.CustomMinimumSize = new Vector2(32, 32);
			button.Toggled += on => button.SelfModulate = on ? Colors.White : new Color(0.75f, 0.75f, 0.75f);
			button.SelfModulate = new Color(0.75f, 0.75f, 0.75f);
		} else {
			TextureLoader.SetButtonTextures(button, EditorTools.ButtonKey(tool.Tool));
		}
		return button;
	}

	private static Label BoxLabel() {
		Label label = new() { HorizontalAlignment = HorizontalAlignment.Right, ClipText = true, CustomMinimumSize = new Vector2(220, 0) };
		label.AddThemeColorOverride("font_color", Colors.Black);
		label.AddThemeFontSizeOverride("font_size", 12);
		return label;
	}

	// The scenario's name alone: the box is small.
	public override void ShowTitle(string title) {
		string name = title.Replace("Scenario Editor: ", "");
		int file = name.IndexOf(" (");
		titleLabel.Text = file > 0 ? name[..file] : name;
		titleLabel.TooltipText = title;
	}

	public override void ShowTile(string text) => tileLabel.Text = text;

	public override void ShowTool(EditorTool tool, int size, bool grid) {
		foreach ((EditorTool t, BaseButton button) in toolButtons) {
			button.SetPressedNoSignal(t == tool);
			if (t == EditorTool.Resources) {
				button.SelfModulate = t == tool ? Colors.White : new Color(0.75f, 0.75f, 0.75f);
			}
		}
		foreach ((int s, Civ3MenuButton button) in sizeButtons) {
			button.SetPressedNoSignal(s == size);
		}
		toolLabel.Text = EditorTools.Of(tool).Name + (tool == EditorTool.FogOfWar ? "" : " brush");
	}

	public override void ShowMessage(string text, bool problem) {
		messageLabel.Text = text;
		messageLabel.AddThemeColorOverride("font_color", problem ? SetupStyle.ProblemColor : Colors.Black);
	}

	public override void StyleMenu(PopupMenu menu) {
		menu.AddThemeStyleboxOverride("panel", SetupStyle.Box(new Color(0.99f, 0.96f, 0.88f), SetupStyle.Brown, 2, 4));
		menu.AddThemeStyleboxOverride("hover", SetupStyle.Box(new Color(0.85f, 0.78f, 0.6f), SetupStyle.Brown, 0, 2));
		foreach (string color in new[] { "font_color", "font_hover_color" }) {
			menu.AddThemeColorOverride(color, SetupStyle.Ink);
		}
		menu.AddThemeColorOverride("font_disabled_color", new Color(SetupStyle.Faded, 0.6f));
		menu.AddThemeFontSizeOverride("font_size", 14);
	}

	// The game's menu popup: parchment, a heading, and Civ3's menu buttons.
	private void ShowMenu() {
		menuPopup?.QueueFree();
		List<(string, Action)> items = [
			("New Scenario (Ctrl-N)", () => Run(ScenarioEditor.MenuItem.New)),
			("Open Scenario... (Ctrl-O)", () => Run(ScenarioEditor.MenuItem.Open)),
			("Save Scenario (Ctrl-S)", () => Run(ScenarioEditor.MenuItem.Save)),
			("Save Scenario As...", () => Run(ScenarioEditor.MenuItem.SaveAs)),
			(editor.GridShown ? "Hide the Grid" : "Show the Grid", () => Run(ScenarioEditor.MenuItem.Grid)),
		];
		foreach ((ScenarioEditor.MenuItem item, EditorLook look, string name) in ScenarioEditor.Looks) {
			if (look != editor.Look) {
				items.Add(($"Look: {name}", () => Run(item)));
			}
		}
		items.Add(("Exit to Main Menu", () => Run(ScenarioEditor.MenuItem.Exit)));
		items.Add(("Back to the Map", () => { }));

		const int width = 360;
		int height = 70 + items.Count * 27;
		TextureRect background = Popup.CreateBackground(width, height);
		background.CustomMinimumSize = new Vector2(width, height);
		background.MouseFilter = MouseFilterEnum.Stop;
		Label heading = new() { Text = "Scenario Editor", HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0, 12), Size = new Vector2(width, 30) };
		heading.AddThemeFontSizeOverride("font_size", 22);
		heading.AddThemeColorOverride("font_color", Colors.Black);
		background.AddChild(heading);
		VBoxContainer buttons = new() { Position = new Vector2(30, 56) };
		buttons.AddThemeConstantOverride("separation", 2);
		foreach ((string label, Action action) in items) {
			Civ3MenuButton button = new() { Text = label, FontSize = 14 };
			button.Pressed += () => {
				CloseMenu();
				action();
			};
			buttons.AddChild(button);
		}
		background.AddChild(buttons);
		menuPopup = Backdrop(background, 0.2f);
		menuPopup.GuiInput += e => {
			if (e is InputEventMouseButton { Pressed: true }) {
				CloseMenu();
			}
		};
		AddChild(menuPopup);
	}

	private void CloseMenu() {
		menuPopup?.QueueFree();
		menuPopup = null;
	}

	// Tile Properties as a screen of the game's: a stone frame, a parchment
	// banner, parchment panels, and the game's confirm and cancel buttons.
	public override Control TileProperties(Tile tile, Action close) {
		Panel frame = SetupStyle.MakeFrame();
		frame.CustomMinimumSize = new Vector2(720, 560);
		SetupStyle.AddTitle(frame, this, "TILE PROPERTIES");
		HBoxContainer body = SetupStyle.MakeBody(frame);

		VBoxContainer left = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		left.AddThemeConstantOverride("separation", 10);
		body.AddChild(left);
		(PanelContainer land, VBoxContainer landBox) = SetupStyle.MakePanel(this, "THE LAND");
		GridContainer facts = new() { Columns = 2 };
		facts.AddThemeConstantOverride("h_separation", 12);
		facts.AddThemeConstantOverride("v_separation", 6);
		facts.AddChild(Ink("Terrain"));
		facts.AddChild(SetupStyle.MakeValue(TileFacts.Terrain(tile)));
		facts.AddChild(Ink("Owner"));
		facts.AddChild(SetupStyle.MakeValue(TileFacts.Owner(tile), "Who owns what's on the tile"));
		facts.AddChild(Ink("Resource"));
		OptionButton resource = SetupStyle.MakeDropdown();
		(List<string> names, int selected) = TileFacts.Resources(tile, Data);
		SetupStyle.AddItems(resource, names, selected);
		facts.AddChild(resource);
		landBox.AddChild(facts);
		left.AddChild(land);

		(PanelContainer here, VBoxContainer hereBox) = SetupStyle.MakePanel(this, "WHAT'S HERE");
		here.SizeFlagsVertical = SizeFlags.ExpandFill;
		GridContainer flags = new() { Columns = 2 };
		flags.AddThemeConstantOverride("h_separation", 8);
		flags.AddThemeConstantOverride("v_separation", 2);
		foreach (TileFacts.Flag flag in TileFacts.Flags(tile, Data.map)) {
			flags.AddChild(new Civ3Checkbox {
				Text = flag.Name,
				FontSize = 14,
				ButtonPressed = flag.On,
				CustomMinimumSize = new Vector2(180, 23),
			});
		}
		hereBox.AddChild(flags);
		left.AddChild(here);

		VBoxContainer right = new() { CustomMinimumSize = new Vector2(220, 0) };
		right.AddThemeConstantOverride("separation", 10);
		body.AddChild(right);
		(PanelContainer cityPanel, VBoxContainer cityBox) = SetupStyle.MakePanel(this, "CITY");
		cityBox.AddChild(Ink(TileFacts.City(tile) ?? "None", wrap: true));
		right.AddChild(cityPanel);
		(PanelContainer unitsPanel, VBoxContainer unitsBox) = SetupStyle.MakePanel(this, "UNITS");
		unitsPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		List<string> units = TileFacts.Units(tile);
		foreach (string unit in units.Count > 0 ? units : ["None"]) {
			unitsBox.AddChild(Ink(unit, wrap: true));
		}
		right.AddChild(unitsPanel);

		// The game's confirm and cancel buttons, on the stone at the bottom
		// right as the setup screens have them.
		HBoxContainer buttons = new();
		buttons.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
		buttons.GrowHorizontal = GrowDirection.Begin;
		buttons.GrowVertical = GrowDirection.Begin;
		buttons.OffsetRight = -SetupStyle.BorderSide - 4;
		buttons.OffsetBottom = -(SetupStyle.BorderBottom - 34) / 2 - 4;
		foreach ((string key, string tooltip) in new[] { ("ui.confirm", "OK"), ("ui.cancel", "Cancel") }) {
			TextureButton button = new() { TooltipText = tooltip };
			TextureLoader.SetButtonTextures(button, key);
			button.Pressed += close;
			buttons.AddChild(button);
		}
		frame.AddChild(buttons);
		return Backdrop(frame, 0.3f);
	}

	private static Label Ink(string text, bool wrap = false) {
		Label label = new() { Text = text };
		if (wrap) {
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			label.CustomMinimumSize = new Vector2(200, 0);
		}
		label.AddThemeColorOverride("font_color", SetupStyle.Ink);
		label.AddThemeFontSizeOverride("font_size", 14);
		return label;
	}
}

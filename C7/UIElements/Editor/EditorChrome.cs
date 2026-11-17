using System;
using System.Collections.Generic;
using C7Engine;
using C7GameData;
using Godot;

// The scenario editor's controls: Civ3's editor's layout in the game's
// parchment (the user's choice of three, 2026-11-16). A menu bar and a
// toolbar across the top and a status bar along the bottom, on parchment
// strips; the tools in the game's own button art; tabbed dialogs on the
// game's popup parchment, with Civ3's checkboxes. The ScenarioEditor keeps
// what they show and does what they ask.
public partial class EditorChrome : Control {
	private readonly ScenarioEditor editor;
	private Label titleLabel, tileLabel, toolLabel, messageLabel, zoomLabel;
	private readonly Dictionary<EditorTool, Button> toolButtons = [];
	private readonly Dictionary<int, Button> sizeButtons = [];
	private Button gridButton, undoButton, redoButton;

	private static readonly Color StripColor = new(0.96f, 0.92f, 0.79f);
	private static readonly Color LightColor = new(0.99f, 0.96f, 0.88f);
	private static readonly Color ButtonColor = new(0.93f, 0.88f, 0.75f);
	private static readonly Color HoverColor = new(0.98f, 0.94f, 0.84f);
	private static readonly Color PressedColor = new(0.80f, 0.71f, 0.53f);

	public EditorChrome(ScenarioEditor editor) {
		this.editor = editor;
		Name = "Chrome";
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
	}

	// Civ3's rules pages, in its Edit dropdown's order.
	internal static readonly string[] RulesPages = [
		"Citizens", "Civilization Advances", "Civilizations", "Combat Experience", "Culture", "Difficulty Levels",
		"Diplomats and Spies", "Eras", "Flavors", "General Settings", "Governments", "Improvements and Wonders",
		"Natural Resources", "Terrain", "Units", "Worker Jobs", "World Sizes",
	];

	private static GameData Data => EngineStorage.gameData;

	private void Run(ScenarioEditor.MenuItem item) => editor.RunMenuItem(item);

	public void Build(List<PopupMenu> menus) {
		VBoxContainer top = new() { MouseFilter = MouseFilterEnum.Ignore };
		top.AddThemeConstantOverride("separation", 0);
		top.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
		AddChild(top);

		PanelContainer menuPanel = StripPanel();
		HBoxContainer menuRow = new();
		MenuBar menuBar = new();
		StyleAsButton(menuBar, flat: true);
		foreach (PopupMenu menu in menus) {
			StyleMenu(menu);
			menuBar.AddChild(menu);
		}
		menuRow.AddChild(menuBar);
		titleLabel = InkLabel();
		titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		menuRow.AddChild(titleLabel);
		menuPanel.AddChild(menuRow);
		top.AddChild(menuPanel);

		PanelContainer toolPanel = StripPanel();
		HBoxContainer tools = new();
		tools.AddThemeConstantOverride("separation", 3);
		toolPanel.AddChild(tools);
		top.AddChild(toolPanel);

		undoButton = TextButton("Undo", "Undo (Ctrl+Z)", () => Run(ScenarioEditor.MenuItem.Undo));
		redoButton = TextButton("Redo", "Redo (Ctrl+Y)", () => Run(ScenarioEditor.MenuItem.Redo));
		tools.AddChild(undoButton);
		tools.AddChild(redoButton);
		tools.AddChild(Divider());
		tools.AddChild(TextButton("Zoom +", "Zoom in", () => Run(ScenarioEditor.MenuItem.ZoomIn)));
		tools.AddChild(TextButton("Zoom -", "Zoom out", () => Run(ScenarioEditor.MenuItem.ZoomOut)));
		gridButton = TextButton("Grid", "Show the grid", () => Run(ScenarioEditor.MenuItem.Grid));
		gridButton.ToggleMode = true;
		tools.AddChild(gridButton);
		tools.AddChild(Divider());
		foreach (EditorTools.Info tool in EditorTools.All) {
			Button button = new() {
				Icon = EditorTools.Icon(tool.Tool),
				ExpandIcon = true,
				ToggleMode = true,
				CustomMinimumSize = new Vector2(38, 38),
				TooltipText = tool.Tooltip,
			};
			StyleAsButton(button);
			button.Pressed += () => Run(ScenarioEditor.ToolItem(tool.Tool));
			toolButtons[tool.Tool] = button;
			tools.AddChild(button);
		}
		tools.AddChild(Divider());
		foreach (int size in EditorTools.Sizes) {
			Button button = TextButton(size.ToString(), $"A brush {size} tiles across", () => Run(ScenarioEditor.SizeItem(size)));
			button.ToggleMode = true;
			button.CustomMinimumSize = new Vector2(30, 0);
			sizeButtons[size] = button;
			tools.AddChild(button);
		}
		tools.AddChild(Divider());
		tools.AddChild(TextButton("Scenario...", "Scenario properties", null));
		tools.AddChild(TextButton("Players...", "Player properties", null));
		tools.AddChild(Divider());
		OptionButton rulesPage = SetupStyle.MakeDropdown("The rules page to edit");
		rulesPage.SizeFlagsHorizontal = SizeFlags.Fill;
		rulesPage.CustomMinimumSize = new Vector2(190, 0);
		SetupStyle.AddItems(rulesPage, RulesPages, 0);
		rulesPage.Disabled = true;
		tools.AddChild(rulesPage);
		tools.AddChild(TextButton("Edit", "Edit this rules page", null));

		PanelContainer bottom = StripPanel();
		bottom.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
		bottom.GrowVertical = GrowDirection.Begin;
		HBoxContainer bottomRow = new();
		bottomRow.AddThemeConstantOverride("separation", 24);
		tileLabel = InkLabel();
		tileLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		messageLabel = InkLabel();
		toolLabel = InkLabel();
		bottomRow.AddChild(tileLabel);
		bottomRow.AddChild(messageLabel);
		bottomRow.AddChild(toolLabel);
		zoomLabel = InkLabel();
		bottomRow.AddChild(zoomLabel);
		bottom.AddChild(bottomRow);
		AddChild(bottom);
	}

	private static PanelContainer StripPanel() {
		PanelContainer panel = new() { MouseFilter = MouseFilterEnum.Stop };
		StyleBoxFlat style = SetupStyle.Box(StripColor, SetupStyle.Brown, 1, 3);
		style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 0;
		style.ShadowColor = new Color(0, 0, 0, 0.3f);
		style.ShadowSize = 2;
		panel.AddThemeStyleboxOverride("panel", style);
		return panel;
	}

	private static Label InkLabel(string text = " ") {
		Label label = new() { Text = text };
		label.AddThemeColorOverride("font_color", SetupStyle.Ink);
		label.AddThemeFontSizeOverride("font_size", 14);
		return label;
	}

	private static Control Divider() {
		VSeparator divider = new();
		divider.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(SetupStyle.Brown, 0.6f), Vertical = true, Thickness = 1 });
		return divider;
	}

	// Parchment buttons, darker when pressed or chosen.
	private static void StyleAsButton(Control control, bool flat = false) {
		StyleBox normal = flat ? new StyleBoxEmpty() : SetupStyle.Box(ButtonColor, new Color(SetupStyle.Brown, 0.8f), 1, 3);
		control.AddThemeStyleboxOverride("normal", normal);
		control.AddThemeStyleboxOverride("hover", SetupStyle.Box(HoverColor, SetupStyle.Brown, 1, 3));
		control.AddThemeStyleboxOverride("pressed", SetupStyle.Box(PressedColor, SetupStyle.Brown, 1, 3));
		control.AddThemeStyleboxOverride("hover_pressed", SetupStyle.Box(PressedColor, SetupStyle.Brown, 1, 3));
		control.AddThemeStyleboxOverride("disabled", flat ? new StyleBoxEmpty() : SetupStyle.Box(new Color(ButtonColor, 0.5f), new Color(SetupStyle.Brown, 0.4f), 1, 3));
		control.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" }) {
			control.AddThemeColorOverride(color, SetupStyle.Ink);
		}
		control.AddThemeColorOverride("font_disabled_color", new Color(SetupStyle.Faded, 0.7f));
		control.AddThemeFontSizeOverride("font_size", 14);
	}

	private static Button TextButton(string text, string tooltip, Action pressed) {
		Button button = new() { Text = text, TooltipText = tooltip, Disabled = pressed == null };
		StyleAsButton(button);
		if (pressed != null) {
			button.Pressed += pressed;
		}
		return button;
	}

	public void ShowTitle(string title) => titleLabel.Text = title;

	public void ShowTile(string text) => tileLabel.Text = text;

	// Civ3's status bar shows the zoom level.
	public void ShowZoom(float zoom, int? seed, string player) => zoomLabel.Text = $"Player: {player}   "
		+ (seed is int s ? $"Seed {s}   " : "") + $"Zoom {Mathf.RoundToInt(zoom * 100)}%";

	// What Undo and Redo would take back or do again, null for nothing.
	public void ShowUndo(string undo, string redo) {
		undoButton.Disabled = undo == null;
		undoButton.TooltipText = undo == null ? "Nothing to undo" : $"Undo {undo} (Ctrl+Z)";
		redoButton.Disabled = redo == null;
		redoButton.TooltipText = redo == null ? "Nothing to redo" : $"Redo {redo} (Ctrl+Y)";
	}

	public void ShowTool(EditorTool tool, int size, bool grid, string brush) {
		foreach ((EditorTool t, Button button) in toolButtons) {
			button.SetPressedNoSignal(t == tool);
		}
		foreach ((int s, Button button) in sizeButtons) {
			button.SetPressedNoSignal(s == size);
			// Civ3's brush sizes are the terrain brush's.
			button.Disabled = tool != EditorTool.Terrain;
		}
		gridButton.SetPressedNoSignal(grid);
		toolLabel.Text = ToolText(tool, size, brush);
	}

	public void ShowMessage(string text, bool problem) {
		messageLabel.Text = text;
		messageLabel.AddThemeColorOverride("font_color", problem ? SetupStyle.ProblemColor : SetupStyle.Ink);
	}

	public void StyleMenu(PopupMenu menu) {
		StyleBoxFlat panel = SetupStyle.Box(LightColor, SetupStyle.Brown, 1, 4);
		panel.ShadowColor = new Color(0, 0, 0, 0.35f);
		panel.ShadowSize = 4;
		menu.AddThemeStyleboxOverride("panel", panel);
		menu.AddThemeStyleboxOverride("hover", SetupStyle.Box(PressedColor, new Color(SetupStyle.Brown, 0.6f), 0, 2));
		menu.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(SetupStyle.Brown, 0.5f), Thickness = 1 });
		menu.AddThemeStyleboxOverride("labeled_separator_left", new StyleBoxLine { Color = new Color(SetupStyle.Brown, 0.5f), Thickness = 1 });
		menu.AddThemeStyleboxOverride("labeled_separator_right", new StyleBoxLine { Color = new Color(SetupStyle.Brown, 0.5f), Thickness = 1 });
		foreach (string color in new[] { "font_color", "font_hover_color", "font_accelerator_color", "font_separator_color" }) {
			menu.AddThemeColorOverride(color, SetupStyle.Ink);
		}
		menu.AddThemeColorOverride("font_accelerator_color", SetupStyle.Brown);
		menu.AddThemeColorOverride("font_separator_color", SetupStyle.Brown);
		menu.AddThemeColorOverride("font_disabled_color", new Color(SetupStyle.Faded, 0.6f));
		menu.AddThemeFontSizeOverride("font_size", 14);
		menu.AddThemeConstantOverride("v_separation", 6);
		Texture2D on = TextureLoader.TryLoad("ui.checkbox.pressed");
		Texture2D off = TextureLoader.TryLoad("ui.checkbox.inactive");
		if (on != null && off != null) {
			foreach (string icon in new[] { "checked", "radio_checked" }) {
				menu.AddThemeIconOverride(icon, on);
			}
			foreach (string icon in new[] { "unchecked", "radio_unchecked" }) {
				menu.AddThemeIconOverride(icon, off);
			}
			menu.AddThemeConstantOverride("icon_max_width", 16);
		}
	}

	public Control TileProperties(Tile tile, Action close) {
		const int width = 480, height = 560;
		TextureRect window = Popup.CreateBackground(width, height);
		window.CustomMinimumSize = new Vector2(width, height);
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "margin_left", "margin_right" }) {
			margin.AddThemeConstantOverride(side, 26);
		}
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		window.AddChild(margin);
		VBoxContainer box = new();
		box.AddThemeConstantOverride("separation", 8);
		margin.AddChild(box);
		box.AddChild(SetupStyle.MakeHeading(this, "TILE PROPERTIES", 18));

		TabContainer tabs = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
		StyleTabs(tabs);
		box.AddChild(tabs);
		tabs.AddChild(TileTab(tile));
		tabs.AddChild(TextTab("City", TileFacts.City(tile) is string city ? [city] : ["No city here."]));
		List<string> units = TileFacts.Units(tile);
		tabs.AddChild(TextTab("Units", units.Count > 0 ? units : ["No units here."]));

		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 8);
		foreach ((string text, Action action) in new (string, Action)[] { ("OK", close), ("Cancel", close), ("Apply", null), ("Help", null) }) {
			Button button = TextButton(text, null, action);
			button.CustomMinimumSize = new Vector2(84, 26);
			buttons.AddChild(button);
		}
		box.AddChild(buttons);
		return Backdrop(window, 0.25f);
	}

	private static void StyleTabs(TabContainer tabs) {
		StyleBoxFlat panel = SetupStyle.Box(LightColor, SetupStyle.Brown, 1, 10);
		tabs.AddThemeStyleboxOverride("panel", panel);
		StyleBoxFlat selected = SetupStyle.Box(LightColor, SetupStyle.Brown, 1, 4);
		selected.BorderWidthBottom = 0;
		selected.ContentMarginLeft = selected.ContentMarginRight = 14;
		StyleBoxFlat unselected = SetupStyle.Box(ButtonColor, new Color(SetupStyle.Brown, 0.7f), 1, 4);
		unselected.ContentMarginLeft = unselected.ContentMarginRight = 14;
		tabs.AddThemeStyleboxOverride("tab_selected", selected);
		tabs.AddThemeStyleboxOverride("tab_unselected", unselected);
		tabs.AddThemeStyleboxOverride("tab_hovered", SetupStyle.Box(HoverColor, SetupStyle.Brown, 1, 4));
		foreach (string color in new[] { "font_selected_color", "font_unselected_color", "font_hovered_color" }) {
			tabs.AddThemeColorOverride(color, SetupStyle.Ink);
		}
		tabs.AddThemeFontSizeOverride("font_size", 14);
	}

	private Control TileTab(Tile tile) {
		VBoxContainer tab = new() { Name = "Tile" };
		tab.AddThemeConstantOverride("separation", 8);
		GridContainer facts = new() { Columns = 2 };
		facts.AddThemeConstantOverride("h_separation", 16);
		facts.AddThemeConstantOverride("v_separation", 8);
		facts.AddChild(InkLabel("Terrain:"));
		facts.AddChild(InkLabel(TileFacts.Terrain(tile)));
		facts.AddChild(InkLabel("Owner:"));
		HBoxContainer owner = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		owner.AddChild(SetupStyle.MakeValue(TileFacts.Owner(tile)));
		owner.AddChild(TextButton("Change...", null, null));
		facts.AddChild(owner);
		facts.AddChild(InkLabel("Resource:"));
		OptionButton resource = SetupStyle.MakeDropdown();
		(List<string> names, int selected) = TileFacts.Resources(tile, Data);
		SetupStyle.AddItems(resource, names, selected);
		facts.AddChild(resource);
		tab.AddChild(facts);
		tab.AddChild(Rule());
		GridContainer flags = new() { Columns = 2 };
		flags.AddThemeConstantOverride("h_separation", 12);
		flags.AddThemeConstantOverride("v_separation", 4);
		foreach (TileFacts.Flag flag in TileFacts.Flags(tile, Data.map)) {
			flags.AddChild(new Civ3Checkbox {
				Text = flag.Name,
				FontSize = 14,
				ButtonPressed = flag.On,
				CustomMinimumSize = new Vector2(190, 24),
			});
		}
		tab.AddChild(flags);
		return tab;
	}

	private static Control Rule() {
		HSeparator rule = new();
		rule.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(SetupStyle.Brown, 0.5f), Thickness = 1 });
		return rule;
	}

	private static Control TextTab(string name, List<string> lines) {
		VBoxContainer tab = new() { Name = name };
		foreach (string line in lines) {
			tab.AddChild(InkLabel(line));
		}
		return tab;
	}

	// A dialog's backdrop: the map darkened, catching the clicks meant for
	// the map while the dialog is open.
	private static Control Backdrop(Control dialog, float dim = 0.35f) {
		ColorRect backdrop = new() {
			Color = new Color(0, 0, 0, dim),
			MouseFilter = MouseFilterEnum.Stop,
		};
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		CenterContainer center = new() { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		center.AddChild(dialog);
		backdrop.AddChild(center);
		return backdrop;
	}

	// The status line's text: the brush, what it paints and its size.
	private static string ToolText(EditorTool tool, int size, string brush) {
		string name = EditorTools.Of(tool).Name;
		if (tool == EditorTool.FogOfWar) {
			return name;
		}
		string sized = tool == EditorTool.Terrain ? $", {size} x {size}" : "";
		return brush == null ? $"{name} brush{sized}" : $"{name} brush: {brush}{sized}";
	}
}

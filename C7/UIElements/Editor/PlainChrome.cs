using System;
using System.Collections.Generic;
using C7GameData;
using Godot;

// The plain look: a desktop tool's, in Godot's own dark controls. Civ3's
// editor's layout: a menu bar and a toolbar across the top, a status bar
// along the bottom, tabbed dialogs.
public partial class PlainChrome : EditorChrome {
	private Label titleLabel, tileLabel, toolLabel, messageLabel;
	private readonly Dictionary<EditorTool, Button> toolButtons = [];
	private readonly Dictionary<int, Button> sizeButtons = [];
	private Button gridButton;

	private static readonly Color Text = new(0.92f, 0.92f, 0.9f);
	private static readonly Color ProblemText = new(1, 0.45f, 0.45f);

	public PlainChrome(ScenarioEditor editor) : base(editor) {
		// Godot's own controls, not the game's parchment theme.
		Theme = ThemeDB.GetDefaultTheme();
	}

	public override void Build(List<PopupMenu> menus) {
		VBoxContainer top = new() { MouseFilter = MouseFilterEnum.Ignore };
		top.AddThemeConstantOverride("separation", 0);
		top.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
		AddChild(top);

		PanelContainer menuPanel = Bar();
		HBoxContainer menuRow = new();
		MenuBar menuBar = new();
		foreach (PopupMenu menu in menus) {
			StyleMenu(menu);
			menuBar.AddChild(menu);
		}
		menuRow.AddChild(menuBar);
		titleLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center };
		menuRow.AddChild(titleLabel);
		menuPanel.AddChild(menuRow);
		top.AddChild(menuPanel);

		PanelContainer toolPanel = Bar();
		HBoxContainer tools = new();
		tools.AddThemeConstantOverride("separation", 2);
		toolPanel.AddChild(tools);
		top.AddChild(toolPanel);

		tools.AddChild(TextButton("Undo", "Undo (Ctrl+Z)", null));
		tools.AddChild(TextButton("Redo", "Redo (Ctrl+Y)", null));
		tools.AddChild(new VSeparator());
		tools.AddChild(TextButton("Zoom +", "Zoom in", () => Run(ScenarioEditor.MenuItem.ZoomIn)));
		tools.AddChild(TextButton("Zoom -", "Zoom out", () => Run(ScenarioEditor.MenuItem.ZoomOut)));
		gridButton = TextButton("Grid", "Show the grid", () => Run(ScenarioEditor.MenuItem.Grid));
		gridButton.ToggleMode = true;
		tools.AddChild(gridButton);
		tools.AddChild(new VSeparator());
		foreach (EditorTools.Info tool in EditorTools.All) {
			Button button = new() {
				Icon = EditorTools.Icon(tool.Tool),
				ExpandIcon = true,
				ToggleMode = true,
				Flat = true,
				CustomMinimumSize = new Vector2(34, 34),
				TooltipText = tool.Tooltip,
			};
			button.Pressed += () => Run(ScenarioEditor.ToolItem(tool.Tool));
			toolButtons[tool.Tool] = button;
			tools.AddChild(button);
		}
		tools.AddChild(new VSeparator());
		foreach (int size in EditorTools.Sizes) {
			Button button = TextButton(size.ToString(), $"A brush {size} tiles across", () => Run(ScenarioEditor.SizeItem(size)));
			button.ToggleMode = true;
			button.CustomMinimumSize = new Vector2(28, 0);
			sizeButtons[size] = button;
			tools.AddChild(button);
		}
		tools.AddChild(new VSeparator());
		tools.AddChild(TextButton("Scenario...", "Scenario properties", null));
		tools.AddChild(TextButton("Players...", "Player properties", null));
		tools.AddChild(new VSeparator());
		OptionButton rulesPage = new() { TooltipText = "The rules page to edit" };
		foreach (string page in RulesPages) {
			rulesPage.AddItem(page);
		}
		rulesPage.Disabled = true;
		tools.AddChild(rulesPage);
		tools.AddChild(TextButton("Edit", "Edit this rules page", null));

		PanelContainer bottom = Bar();
		bottom.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
		bottom.GrowVertical = GrowDirection.Begin;
		HBoxContainer bottomRow = new();
		bottomRow.AddThemeConstantOverride("separation", 24);
		tileLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, Text = " " };
		messageLabel = new Label();
		toolLabel = new Label();
		bottomRow.AddChild(tileLabel);
		bottomRow.AddChild(messageLabel);
		bottomRow.AddChild(toolLabel);
		bottom.AddChild(bottomRow);
		AddChild(bottom);
		foreach (Label label in new[] { titleLabel, tileLabel, messageLabel, toolLabel }) {
			label.AddThemeColorOverride("font_color", Text);
		}
	}

	// A bar of the window, solid (Godot's panels let the map show through).
	private static PanelContainer Bar() {
		PanelContainer bar = new() { MouseFilter = MouseFilterEnum.Stop };
		bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
			BgColor = new Color(0.15f, 0.16f, 0.19f),
			BorderColor = new Color(0.08f, 0.08f, 0.1f),
			BorderWidthBottom = 1,
			BorderWidthTop = 1,
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
			ContentMarginTop = 2,
			ContentMarginBottom = 2,
		});
		return bar;
	}

	// Civ3's rules pages, in its Edit dropdown's order.
	internal static readonly string[] RulesPages = [
		"Citizens", "Civilization Advances", "Civilizations", "Combat Experience", "Culture", "Difficulty Levels",
		"Diplomats and Spies", "Eras", "Flavors", "General Settings", "Governments", "Improvements and Wonders",
		"Natural Resources", "Terrain", "Units", "Worker Jobs", "World Sizes",
	];

	private static Button TextButton(string text, string tooltip, Action pressed) {
		Button button = new() { Text = text, TooltipText = tooltip, Flat = true, Disabled = pressed == null };
		if (pressed != null) {
			button.Pressed += pressed;
		}
		return button;
	}

	public override void ShowTitle(string title) => titleLabel.Text = title;

	public override void ShowTile(string text) => tileLabel.Text = text;

	public override void ShowTool(EditorTool tool, int size, bool grid) {
		foreach ((EditorTool t, Button button) in toolButtons) {
			button.SetPressedNoSignal(t == tool);
		}
		foreach ((int s, Button button) in sizeButtons) {
			button.SetPressedNoSignal(s == size);
		}
		gridButton.SetPressedNoSignal(grid);
		toolLabel.Text = ToolText(tool, size);
	}

	public override void ShowMessage(string text, bool problem) {
		messageLabel.Text = text;
		messageLabel.AddThemeColorOverride("font_color", problem ? ProblemText : Text);
	}

	public override void StyleMenu(PopupMenu menu) {
		menu.Theme = ThemeDB.GetDefaultTheme();
	}

	public override Control TileProperties(Tile tile, Action close) {
		PanelContainer window = new() { CustomMinimumSize = new Vector2(420, 0) };
		window.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
			BgColor = new Color(0.17f, 0.18f, 0.21f),
			BorderColor = new Color(0.35f, 0.37f, 0.42f),
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			ContentMarginLeft = 12,
			ContentMarginRight = 12,
			ContentMarginTop = 8,
			ContentMarginBottom = 12,
			ShadowColor = new Color(0, 0, 0, 0.5f),
			ShadowSize = 8,
		});
		VBoxContainer box = new();
		box.AddThemeConstantOverride("separation", 8);
		window.AddChild(box);
		Label title = new() { Text = "Tile Properties" };
		title.AddThemeFontSizeOverride("font_size", 18);
		box.AddChild(title);

		TabContainer tabs = new();
		box.AddChild(tabs);
		tabs.AddChild(TileTab(tile));
		tabs.AddChild(CityTab(tile));
		tabs.AddChild(UnitsTab(tile));

		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.End };
		foreach ((string text, Action action) in new (string, Action)[] { ("OK", close), ("Cancel", close), ("Apply", null), ("Help", null) }) {
			Button button = new() { Text = text, CustomMinimumSize = new Vector2(80, 0), Disabled = action == null };
			if (action != null) {
				button.Pressed += action;
			}
			buttons.AddChild(button);
		}
		box.AddChild(buttons);
		Control backdrop = Backdrop(window);
		backdrop.Theme = ThemeDB.GetDefaultTheme();
		return backdrop;
	}

	private static Control TileTab(Tile tile) {
		VBoxContainer tab = new() { Name = "Tile" };
		tab.AddThemeConstantOverride("separation", 8);
		GridContainer facts = new() { Columns = 2 };
		facts.AddThemeConstantOverride("h_separation", 16);
		facts.AddChild(new Label { Text = "Terrain:" });
		facts.AddChild(new Label { Text = TileFacts.Terrain(tile) });
		facts.AddChild(new Label { Text = "Owner:" });
		HBoxContainer owner = new();
		owner.AddChild(new LineEdit { Text = TileFacts.Owner(tile), Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill });
		owner.AddChild(new Button { Text = "Change...", Disabled = true });
		facts.AddChild(owner);
		facts.AddChild(new Label { Text = "Resource:" });
		OptionButton resource = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		(List<string> names, int selected) = TileFacts.Resources(tile, Data);
		foreach (string name in names) {
			resource.AddItem(name);
		}
		resource.Select(selected);
		facts.AddChild(resource);
		tab.AddChild(facts);
		tab.AddChild(new HSeparator());
		GridContainer flags = new() { Columns = 2 };
		flags.AddThemeConstantOverride("h_separation", 24);
		foreach (TileFacts.Flag flag in TileFacts.Flags(tile, Data.map)) {
			flags.AddChild(new CheckBox { Text = flag.Name, ButtonPressed = flag.On });
		}
		tab.AddChild(flags);
		return tab;
	}

	private static Control CityTab(Tile tile) {
		VBoxContainer tab = new() { Name = "City" };
		tab.AddChild(new Label { Text = TileFacts.City(tile) ?? "No city here." });
		return tab;
	}

	private static Control UnitsTab(Tile tile) {
		VBoxContainer tab = new() { Name = "Units" };
		List<string> units = TileFacts.Units(tile);
		if (units.Count == 0) {
			tab.AddChild(new Label { Text = "No units here." });
		} else {
			ItemList list = new() { CustomMinimumSize = new Vector2(0, 160) };
			foreach (string unit in units) {
				list.AddItem(unit);
			}
			tab.AddChild(list);
		}
		return tab;
	}
}

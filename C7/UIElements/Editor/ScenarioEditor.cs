using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// The scenario editor over the map: its menus (as Civ3's editor's), its
// tools, a status line that says what's on the tile under the mouse, and its
// dialogs. The map itself is the game scene's map view (see Game_Editor.cs);
// this moves its camera and, from Phase 1, paints on it. How the controls
// look is the chrome's (EditorChrome), one per EditorLook; this keeps what
// they show and does what they ask.
public partial class ScenarioEditor : Control {
	private readonly Game game;

	private EditorChrome chrome;
	private FileDialog fileDialog;
	private PopupMenu contextMenu;
	private Control dialog;

	// Whether the file dialog is choosing a file to open or to save as.
	private bool choosingFileToSave;

	// Panning the map by dragging with the middle button.
	private bool panning;
	private Vector2 panFrom;

	// What the controls show.
	public EditorLook Look { get; private set; } = EditorLook.Civ3Editor;
	public EditorTool Tool { get; private set; } = EditorTool.Terrain;
	public int BrushSize { get; private set; } = 1;
	public bool GridShown => game.EditorMapView.gridLayer.visible;
	public string TitleText { get; private set; } = "";
	public string MessageText { get; private set; } = "";

	// Every menu item, in Civ3's editor's menus.
	public enum MenuItem {
		New, Open, Save, SaveAs, Exit,
		Undo, Redo,
		Grid, ZoomIn, ZoomOut, LookGame, LookCiv3Editor, LookPlain,
		BrushTerrain, BrushResources, BrushOverlays, BrushCities, BrushUnits, FogOfWar,
		Size1, Size3, Size5, Size7, GenerateMap, ClearMap,
		ScenarioProperties, PlayerProperties, CustomRules, CustomMap, CustomPlayerData,
		EditRules,
		TileProperties, CenterHere,
	}

	// Arrow keys move the view by this many pixels.
	private const float KeyPanPixels = 128;

	public ScenarioEditor() { }

	public ScenarioEditor(Game game) {
		this.game = game;
		Name = "ScenarioEditor";
	}

	public override void _Ready() {
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		// The map under the editor gets the clicks the controls don't take.
		MouseFilter = MouseFilterEnum.Ignore;

		fileDialog = new FileDialog {
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = false,
		};
		fileDialog.FileSelected += OnFileChosen;
		// As Load Scenario's: the editor's scenarios, and Civ3's, which are
		// starting points for new ones.
		ourScenariosButton = fileDialog.AddButton("Our Scenarios", false, ShowOurScenarios);
		civ3ScenariosButton = fileDialog.AddButton("Civ3 Scenarios", false, ShowCiv3Scenarios);
		fileDialog.CustomAction += action => {
			fileDialog.CurrentDir = action == ShowCiv3Scenarios ? Civ3ScenariosDirectory : ScenarioEditorSession.ScenariosDirectory;
		};
		AddChild(fileDialog);

		// The --editor-look=game|civ3|plain developer option; the look last
		// chosen in this run otherwise.
		string lookArg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--editor-look="))?["--editor-look=".Length..];
		SetLook(lookArg switch {
			"game" => EditorLook.Game,
			"plain" => EditorLook.Plain,
			"civ3" => EditorLook.Civ3Editor,
			_ => lastLook,
		});

		// The --editor-shot=<png> developer option: a screenshot of the editor
		// once the map has been drawn, then quit, to check the editor without
		// clicking through it.
		shotPath = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--editor-shot="))?["--editor-shot=".Length..];
	}

	private static EditorLook lastLook = EditorLook.Civ3Editor;

	private string shotPath;
	private int framesShown;

	private const string ShowOurScenarios = "show_our_scenarios";
	private const string ShowCiv3Scenarios = "show_civ3_scenarios";
	private Button ourScenariosButton, civ3ScenariosButton;
	private static string Civ3ScenariosDirectory => Path.Combine(Util.Civ3Root ?? "", "Conquests", "Scenarios");

	public override void _Process(double delta) {
		if (shotPath != null && ++framesShown == 90) {
			GetViewport().GetTexture().GetImage().SavePng(shotPath);
			GetTree().Quit();
		}
	}

	// Builds the controls in a look, keeping what they show.
	public void SetLook(EditorLook look) {
		CloseDialog();
		contextMenu?.QueueFree();
		contextMenu = null;
		if (chrome != null) {
			RemoveChild(chrome);
			chrome.QueueFree();
		}
		Look = lastLook = look;
		chrome = look switch {
			EditorLook.Game => new GameChrome(this),
			EditorLook.Plain => new PlainChrome(this),
			_ => new Civ3EditorChrome(this),
		};
		AddChild(chrome);
		MoveChild(chrome, 0);
		chrome.Build(BuildMenus());
		UpdateTitle();
		chrome.ShowTool(Tool, BrushSize, GridShown);
		chrome.ShowMessage(MessageText, false);
	}

	// Civ3's editor's menus. What isn't done yet is there, greyed.
	private List<PopupMenu> BuildMenus() {
		PopupMenu file = Menu("File");
		Add(file, "New", MenuItem.New, (Key)KeyModifierMask.MaskCtrl | Key.N);
		Add(file, "Open...", MenuItem.Open, (Key)KeyModifierMask.MaskCtrl | Key.O);
		Add(file, "Save", MenuItem.Save, (Key)KeyModifierMask.MaskCtrl | Key.S);
		Add(file, "Save As...", MenuItem.SaveAs);
		file.AddSeparator();
		Add(file, "Exit to Main Menu", MenuItem.Exit);

		PopupMenu edit = Menu("Edit");
		Add(edit, "Undo", MenuItem.Undo, (Key)KeyModifierMask.MaskCtrl | Key.Z, enabled: false);
		Add(edit, "Redo", MenuItem.Redo, (Key)KeyModifierMask.MaskCtrl | Key.Y, enabled: false);

		PopupMenu view = Menu("View");
		view.AddCheckItem("Grid", (int)MenuItem.Grid);
		view.SetItemChecked(view.GetItemIndex((int)MenuItem.Grid), GridShown);
		Add(view, "Zoom In", MenuItem.ZoomIn);
		Add(view, "Zoom Out", MenuItem.ZoomOut);
		view.AddSeparator("Look (to choose one)");
		foreach ((MenuItem item, EditorLook look, string name) in Looks) {
			view.AddRadioCheckItem(name, (int)item);
			view.SetItemChecked(view.GetItemIndex((int)item), look == Look);
		}

		PopupMenu map = Menu("Map");
		foreach (EditorTools.Info tool in EditorTools.All) {
			map.AddRadioCheckItem(tool.Name + (tool.Tool == EditorTool.FogOfWar ? "" : " Brush"), (int)ToolItem(tool.Tool));
			if (tool.Hotkey != Key.None) {
				map.SetItemAccelerator(map.GetItemIndex((int)ToolItem(tool.Tool)), tool.Hotkey);
			}
			map.SetItemChecked(map.GetItemIndex((int)ToolItem(tool.Tool)), tool.Tool == Tool);
		}
		map.AddSeparator("Brush Size");
		foreach (int size in EditorTools.Sizes) {
			map.AddRadioCheckItem($"{size} x {size}", (int)SizeItem(size));
			map.SetItemChecked(map.GetItemIndex((int)SizeItem(size)), size == BrushSize);
		}
		map.AddSeparator();
		Add(map, "Generate Map...", MenuItem.GenerateMap, enabled: false);
		Add(map, "Clear Map...", MenuItem.ClearMap, enabled: false);

		PopupMenu scenario = Menu("Scenario");
		Add(scenario, "Properties...", MenuItem.ScenarioProperties, enabled: false);
		Add(scenario, "Player Properties...", MenuItem.PlayerProperties, enabled: false);
		scenario.AddSeparator();
		foreach ((MenuItem item, string name, bool on) in new[] {
			(MenuItem.CustomRules, "Custom Rules", game.EditorInfo.customRules),
			(MenuItem.CustomMap, "Custom Map", game.EditorInfo.customMap),
			(MenuItem.CustomPlayerData, "Custom Player Data", game.EditorInfo.customPlayerData),
		}) {
			scenario.AddCheckItem(name, (int)item);
			scenario.SetItemChecked(scenario.GetItemIndex((int)item), on);
			scenario.SetItemDisabled(scenario.GetItemIndex((int)item), true);
		}

		PopupMenu rules = Menu("Rules");
		Add(rules, "Edit Rules...", MenuItem.EditRules, enabled: false);

		return [file, edit, view, map, scenario, rules];
	}

	internal static readonly (MenuItem item, EditorLook look, string name)[] Looks = [
		(MenuItem.LookGame, EditorLook.Game, "The Game's"),
		(MenuItem.LookCiv3Editor, EditorLook.Civ3Editor, "Civ3's Editor, in Parchment"),
		(MenuItem.LookPlain, EditorLook.Plain, "Plain"),
	];

	private PopupMenu Menu(string name) {
		PopupMenu menu = new() { Name = name };
		menu.IdPressed += id => RunMenuItem((MenuItem)(int)id);
		return menu;
	}

	private static void Add(PopupMenu menu, string label, MenuItem item, Key accelerator = Key.None, bool enabled = true) {
		menu.AddItem(label, (int)item, accelerator);
		menu.SetItemDisabled(menu.GetItemIndex((int)item), !enabled);
	}

	internal static MenuItem ToolItem(EditorTool tool) => MenuItem.BrushTerrain + (int)tool;
	internal static MenuItem SizeItem(int size) => MenuItem.Size1 + System.Array.IndexOf(EditorTools.Sizes, size);

	// A context menu in this look's style (see EditorChrome.StyleMenu).
	internal PopupMenu StyledMenu(string name) {
		PopupMenu menu = Menu(name);
		chrome.StyleMenu(menu);
		return menu;
	}

	public void RunMenuItem(MenuItem item) {
		switch (item) {
			case MenuItem.New:
				game.NewScenario();
				break;
			case MenuItem.Open:
				ShowFileDialog(save: false);
				break;
			case MenuItem.Save:
				if (game.EditorPath == null) {
					ShowFileDialog(save: true);
				} else {
					SaveTo(game.EditorPath);
				}
				break;
			case MenuItem.SaveAs:
				ShowFileDialog(save: true);
				break;
			case MenuItem.Exit:
				game.ExitEditor();
				break;
			case MenuItem.Grid:
				game.EditorMapView.gridLayer.visible = !GridShown;
				game.EditorMapView.InvalidateMap();
				Refresh();
				break;
			case MenuItem.ZoomIn:
			case MenuItem.ZoomOut:
				MapView mapView = game.EditorMapView;
				mapView.setCameraZoom(mapView.cameraZoom + (item == MenuItem.ZoomIn ? 0.25f : -0.25f), GetViewportRect().Size / 2);
				break;
			case >= MenuItem.LookGame and <= MenuItem.LookPlain:
				SetLook(Looks.First(l => l.item == item).look);
				break;
			case >= MenuItem.BrushTerrain and <= MenuItem.FogOfWar:
				Tool = EditorTool.Terrain + (item - MenuItem.BrushTerrain);
				Refresh();
				break;
			case >= MenuItem.Size1 and <= MenuItem.Size7:
				BrushSize = EditorTools.Sizes[item - MenuItem.Size1];
				Refresh();
				break;
			case MenuItem.TileProperties:
				OpenTileProperties(contextTile);
				break;
			case MenuItem.CenterHere:
				if (contextTile != null) {
					game.EditorMapView.centerCameraOnTile(contextTile);
				}
				break;
		}
	}

	// Shows the tool, brush size and grid in the menus and the chrome.
	private void Refresh() {
		foreach (PopupMenu menu in FindChildren("*", nameof(PopupMenu), true, false).OfType<PopupMenu>()) {
			foreach (EditorTools.Info tool in EditorTools.All) {
				SetChecked(menu, ToolItem(tool.Tool), tool.Tool == Tool);
			}
			foreach (int size in EditorTools.Sizes) {
				SetChecked(menu, SizeItem(size), size == BrushSize);
			}
			SetChecked(menu, MenuItem.Grid, GridShown);
		}
		chrome.ShowTool(Tool, BrushSize, GridShown);
	}

	private static void SetChecked(PopupMenu menu, MenuItem item, bool on) {
		int index = menu.GetItemIndex((int)item);
		if (index >= 0) {
			menu.SetItemChecked(index, on);
		}
	}

	private void UpdateTitle() {
		string title = string.IsNullOrEmpty(game.EditorInfo.title) ? "Untitled" : game.EditorInfo.title;
		string file = game.EditorPath == null ? "not saved yet" : Path.GetFileName(game.EditorPath);
		string rulesOnly = game.EditorInfo.customMap ? "" : "   (rules only: the map isn't saved)";
		TitleText = $"Scenario Editor: {title} ({file}){rulesOnly}";
		chrome.ShowTitle(TitleText);
	}

	// Says something in the status line, as Saved or why something failed.
	private void Say(string message, bool problem = false) {
		MessageText = message;
		chrome.ShowMessage(message, problem);
	}

	private void ShowFileDialog(bool save) {
		choosingFileToSave = save;
		string folder = ScenarioEditorSession.ScenariosDirectory;
		try {
			Directory.CreateDirectory(folder);
		} catch (System.Exception e) when (e is IOException or System.UnauthorizedAccessException) {
			Say($"Couldn't make the scenarios folder: {e.Message}", problem: true);
		}
		if (save) {
			fileDialog.Title = "Save Scenario";
			fileDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
			fileDialog.Filters = ["*.json ; OpenCiv3 scenarios"];
			fileDialog.CurrentDir = game.EditorPath != null ? Path.GetDirectoryName(game.EditorPath) : folder;
			fileDialog.CurrentFile = game.EditorPath != null ? Path.GetFileName(game.EditorPath) : FileNameFor(game.EditorInfo.title);
		} else {
			fileDialog.Title = "Open Scenario";
			fileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
			fileDialog.Filters = [
				"*.json, *.biq ; Scenarios (OpenCiv3 and Civ3)",
				"*.json ; OpenCiv3 scenarios and saved games",
				"*.biq ; Civ3 scenarios",
				"*.sav ; Civ3 saved games",
			];
			fileDialog.CurrentDir = folder;
		}
		ourScenariosButton.Visible = !save;
		civ3ScenariosButton.Visible = !save && !string.IsNullOrEmpty(Util.Civ3Root) && Directory.Exists(Civ3ScenariosDirectory);
		fileDialog.PopupCentered(new Vector2I(900, 600));
	}

	// A file name from the scenario's title, without what Windows refuses.
	private static string FileNameFor(string title) {
		string name = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
		foreach (char c in Path.GetInvalidFileNameChars()) {
			name = name.Replace(c, '_');
		}
		return name + ".json";
	}

	private void OnFileChosen(string path) {
		if (choosingFileToSave) {
			if (!path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase)) {
				path += ".json";
			}
			SaveTo(path);
		} else {
			string problem = game.OpenScenario(path);
			if (problem != null) {
				Say($"Couldn't open {Path.GetFileName(path)}: {problem}", problem: true);
			}
		}
	}

	private void SaveTo(string path) {
		string problem = game.SaveScenario(path);
		if (problem == null) {
			Say($"Saved {Path.GetFileName(path)}");
			UpdateTitle();
		} else {
			Say($"Couldn't save: {problem}", problem: true);
		}
	}

	// The tile a context menu was opened on.
	private Tile contextTile;

	// Civ3's right-click menu on the map: the tile's properties, and
	// centering the view there.
	private void ShowContextMenu(Vector2 position) {
		contextTile = game.EditorTileAt(position);
		if (contextTile == null) {
			return;
		}
		contextMenu?.QueueFree();
		contextMenu = StyledMenu("TileMenu");
		Add(contextMenu, "Tile Properties...", MenuItem.TileProperties);
		Add(contextMenu, "Center Here", MenuItem.CenterHere);
		AddChild(contextMenu);
		contextMenu.Position = (Vector2I)position;
		contextMenu.Popup();
	}

	public bool DialogOpen => dialog != null;

	public void OpenTileProperties(Tile tile) {
		if (tile == null) {
			return;
		}
		CloseDialog();
		dialog = chrome.TileProperties(tile, CloseDialog);
		AddChild(dialog);
	}

	public void CloseDialog() {
		if (dialog != null) {
			dialog.QueueFree();
			dialog = null;
		}
	}

	// The map's input (see Game.EditorUnhandledInput): the wheel zooms,
	// the middle button drags the view, the arrow keys move it, the right
	// button opens the tile's menu, and the status line follows the mouse.
	internal void HandleMapInput(InputEvent @event) {
		if (DialogOpen) {
			if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape }) {
				CloseDialog();
				GetViewport().SetInputAsHandled();
			}
			return;
		}
		MapView mapView = game.EditorMapView;
		switch (@event) {
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
				float delta = wheel.ButtonIndex == MouseButton.WheelUp ? 0.1f : -0.1f;
				mapView.setCameraZoom(mapView.cameraZoom + delta, wheel.Position);
				GetViewport().SetInputAsHandled();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
				panning = middle.Pressed;
				panFrom = middle.Position;
				GetViewport().SetInputAsHandled();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } right:
				ShowContextMenu(right.Position);
				GetViewport().SetInputAsHandled();
				break;
			case InputEventMouseMotion motion:
				if (panning) {
					mapView.cameraLocation += panFrom - motion.Position;
					panFrom = motion.Position;
				}
				ShowTileAt(motion.Position);
				break;
			case InputEventKey { Pressed: true, Echo: false } key when !key.CtrlPressed && !key.AltPressed
				&& EditorTools.All.FirstOrDefault(t => t.Hotkey != Key.None && t.Hotkey == key.Keycode) is EditorTools.Info tool:
				RunMenuItem(ToolItem(tool.Tool));
				GetViewport().SetInputAsHandled();
				break;
			case InputEventKey { Pressed: true } key when key.CtrlPressed && Look == EditorLook.Game
				&& key.Keycode is Key.N or Key.O or Key.S:
				// The other looks' menu bars take these themselves.
				RunMenuItem(key.Keycode switch { Key.N => MenuItem.New, Key.O => MenuItem.Open, _ => MenuItem.Save });
				GetViewport().SetInputAsHandled();
				break;
			case InputEventKey { Pressed: true } key:
				Vector2 direction = key.Keycode switch {
					Key.Left or Key.Kp4 => Vector2.Left,
					Key.Right or Key.Kp6 => Vector2.Right,
					Key.Up or Key.Kp8 => Vector2.Up,
					Key.Down or Key.Kp2 => Vector2.Down,
					_ => Vector2.Zero,
				};
				if (direction != Vector2.Zero) {
					mapView.cameraLocation += direction * KeyPanPixels;
					GetViewport().SetInputAsHandled();
				}
				break;
		}
	}

	private void ShowTileAt(Vector2 position) {
		Tile tile = game.EditorTileAt(position);
		chrome.ShowTile(tile == null ? "" : Describe(tile));
	}

	// What's on a tile, for the status line: where it is, its terrain, its
	// resource, a city and how many units.
	internal static string Describe(Tile tile) {
		List<string> parts = [TileFacts.Terrain(tile)];
		if (tile.Resource != null && tile.Resource != C7GameData.Resource.NONE && tile.Resource.Category != ResourceCategory.NONE) {
			parts.Add(tile.Resource.Name);
		}
		if (TileFacts.City(tile) is string city) {
			parts.Add(city);
		}
		if (tile.unitsOnTile.Count > 0) {
			parts.Add(tile.unitsOnTile.Count == 1 ? $"1 unit ({tile.unitsOnTile[0].unitType.name})" : $"{tile.unitsOnTile.Count} units");
		}
		return $"({tile.XCoordinate}, {tile.YCoordinate})   " + string.Join(", ", parts.Where(p => p.Length > 0));
	}
}

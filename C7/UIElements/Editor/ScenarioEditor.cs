using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;
using C7Engine.Editor;
using C7GameData;
using C7GameData.Save;
using Godot;

// The scenario editor over the map: its menus (as Civ3's editor's), its
// tools, a status line that says what's on the tile under the mouse, and its
// dialogs. The map itself is the game scene's map view (see Game_Editor.cs);
// this moves its camera and paints on it (MapPainter). How the controls
// look is the chrome's (EditorChrome); this keeps what they show and does
// what they ask.
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

	// Painting: the left button held down on the map is one stroke.
	private MapPainter painter;
	private TerrainBrush terrainBrush;
	private C7GameData.Resource resourceBrush;
	private OverlayBrush overlayBrush;
	private UnitPrototype unitBrush;
	// Civ3's active player: whose cities, units and starting locations the
	// brushes place.
	public Player ActivePlayer { get; private set; }
	private bool painting;
	private Vector2 paintFrom;
	private Tile lastPainted;
	// Each brush's pop-up of what it paints.
	private readonly Dictionary<EditorTool, PopupMenu> pickers = [];

	// What the controls show.
	public EditorTool Tool { get; private set; } = EditorTool.Terrain;
	public int BrushSize { get; private set; } = 1;
	public bool GridShown => game.EditorMapView.gridLayer.visible;
	public string TitleText { get; private set; } = "";
	public string MessageText { get; private set; } = "";

	// Every menu item, in Civ3's editor's menus.
	public enum MenuItem {
		New, Open, Save, SaveAs, Exit,
		Undo, Redo,
		Grid, ZoomIn, ZoomOut,
		GridCoordinates, FoodShieldIcons, CleanMap, Territory, CityNames, TileOwners, UnitCounts, Minimap,
		RedistributeStarts, RedistributeResources, RedistributeHuts, RedistributeGrassland, WrapX, WrapY, PolarIceCaps,
		RandomBonusGrassland, SetActivePlayer,
		BrushTerrain, BrushResources, BrushOverlays, BrushCities, BrushUnits, FogOfWar,
		Size1, Size3, Size5, Size7, GenerateMap, ClearMap,
		ScenarioProperties, PlayerProperties, CustomRules, CustomMap, CustomPlayerData,
		EditRules,
		TileProperties, CenterHere,
	}

	// Arrow keys move the view by this many pixels (four times as far with
	// the minimap focused).
	private const float KeyPanPixels = 128;

	// What the editor draws over the map, and its minimap.
	private EditorMapLayer mapLayer;
	private EditorMinimap minimap;
	private float shownZoom = -1;

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

		painter = new MapPainter(EngineStorage.gameData);
		if (game.EditorStartsModified) {
			painter.History.MarkUnsaved();
		}
		// As Civ3's editor starts: the first terrain (desert), 1 x 1.
		terrainBrush = painter.TerrainBrushes().FirstOrDefault();
		resourceBrush = painter.ResourceBrushes().FirstOrDefault();
		overlayBrush = painter.OverlayBrushes().FirstOrDefault();
		unitBrush = EngineStorage.gameData.unitPrototypes.FirstOrDefault();
		// The player chosen before the editor opened again (after adding one),
		// else the first that isn't the barbarians.
		ActivePlayer = EngineStorage.gameData.players.FirstOrDefault(p => ScenarioPlayers.NameOf(p) == ScenarioEditorSession.ActivePlayer)
			?? EngineStorage.gameData.players.FirstOrDefault(p => !p.isBarbarians);

		chrome = new EditorChrome(this);
		AddChild(chrome);
		MoveChild(chrome, 0);
		chrome.Build(BuildMenus());
		UpdateTitle();
		Refresh();
		chrome.ShowMessage(MessageText, false);

		mapLayer = new EditorMapLayer();
		game.EditorMapView.AddTerrainLayer(mapLayer);
		minimap = new EditorMinimap(game.EditorMapView, chrome.StyleMenu);
		AddChild(minimap);
		Refresh();

		// The --editor-shot=<png> developer option: a screenshot of the editor
		// once the map has been drawn, then quit, to check the editor without
		// clicking through it.
		shotPath = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--editor-shot="))?["--editor-shot=".Length..];
	}

	private string shotPath;
	private int framesShown;

	private const string ShowOurScenarios = "show_our_scenarios";
	private const string ShowCiv3Scenarios = "show_civ3_scenarios";
	private Button ourScenariosButton, civ3ScenariosButton;
	private static string Civ3ScenariosDirectory => Path.Combine(Util.Civ3Root ?? "", "Conquests", "Scenarios");

	public override void _Process(double delta) {
		// The button may be let go over the controls, which take the event.
		if (painting && !Input.IsMouseButtonPressed(MouseButton.Left)) {
			EndStroke();
		}
		// The game's minimap, in its HUD frame, gives way to the editor's.
		if (game.EditorMapView.GameMiniMap is MiniMap gameMiniMap && gameMiniMap.Visible) {
			gameMiniMap.Hide();
			gameMiniMap.ProcessMode = ProcessModeEnum.Disabled;
		}
		if (game.EditorMapView.cameraZoom != shownZoom) {
			shownZoom = game.EditorMapView.cameraZoom;
			// Civ3's status bar shows the seed of a generated map.
			GameData data = EngineStorage.gameData;
			chrome.ShowZoom(shownZoom, data.map.generation != null ? data.seed : null, ScenarioPlayers.NameOf(ActivePlayer));
		}
		if (shotPath != null && ++framesShown == 90) {
			GetViewport().GetTexture().GetImage().SavePng(shotPath);
			GetTree().Quit();
		}
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
		Add(edit, "Undo", MenuItem.Undo, (Key)KeyModifierMask.MaskCtrl | Key.Z);
		Add(edit, "Redo", MenuItem.Redo, (Key)KeyModifierMask.MaskCtrl | Key.Y);

		// Civ3's View menu (its toolbar and status bar toggles aside).
		PopupMenu view = Menu("View");
		view.AddCheckItem("Grid", (int)MenuItem.Grid);
		view.AddCheckItem("Grid Coordinates", (int)MenuItem.GridCoordinates);
		Add(view, "Zoom In", MenuItem.ZoomIn);
		Add(view, "Zoom Out", MenuItem.ZoomOut);
		view.AddSeparator();
		view.AddCheckItem("Food and Shield Icons", (int)MenuItem.FoodShieldIcons);
		view.AddCheckItem("Clean Map", (int)MenuItem.CleanMap);
		view.AddSeparator();
		view.AddCheckItem("Territory", (int)MenuItem.Territory);
		view.AddCheckItem("City Names", (int)MenuItem.CityNames);
		view.AddCheckItem("Tile Owners", (int)MenuItem.TileOwners);
		view.AddCheckItem("Number of Units", (int)MenuItem.UnitCounts);
		view.AddSeparator();
		view.AddCheckItem("Minimap", (int)MenuItem.Minimap);
		view.SetItemAccelerator(view.GetItemIndex((int)MenuItem.Minimap), Key.M);
		// Civ3's View > Options.
		view.AddSeparator("Options");
		view.AddCheckItem("Randomize Bonus Grassland", (int)MenuItem.RandomBonusGrassland);

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
		Add(map, "Generate Map...", MenuItem.GenerateMap);
		Add(map, "Clear Map...", MenuItem.ClearMap);
		PopupMenu redistribute = Menu("Redistribute");
		Add(redistribute, "Starting Locations", MenuItem.RedistributeStarts);
		Add(redistribute, "Natural Resources", MenuItem.RedistributeResources);
		Add(redistribute, "Goody Huts", MenuItem.RedistributeHuts);
		Add(redistribute, "Bonus Grassland", MenuItem.RedistributeGrassland);
		chrome.StyleMenu(redistribute);
		map.AddChild(redistribute);
		map.AddSubmenuNodeItem("Redistribute", redistribute);
		map.AddSeparator();
		map.AddCheckItem("Polar Ice Caps", (int)MenuItem.PolarIceCaps);
		map.AddCheckItem("X Wrapping", (int)MenuItem.WrapX);
		map.AddCheckItem("Y Wrapping", (int)MenuItem.WrapY);
		map.AddSeparator();
		Add(map, "Set Active Player...", MenuItem.SetActivePlayer);

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

	private PopupMenu Menu(string name) {
		PopupMenu menu = new() { Name = name };
		menu.IdPressed += id => {
			if (id >= PickerIds) {
				Choose((EditorTool)(id / PickerIds - 1), (int)id % PickerIds);
			} else {
				RunMenuItem((MenuItem)(int)id);
			}
		};
		return menu;
	}

	private static void Add(PopupMenu menu, string label, MenuItem item, Key accelerator = Key.None, bool enabled = true) {
		menu.AddItem(label, (int)item, accelerator);
		menu.SetItemDisabled(menu.GetItemIndex((int)item), !enabled);
	}

	internal static MenuItem ToolItem(EditorTool tool) => MenuItem.BrushTerrain + (int)tool;
	internal static MenuItem SizeItem(int size) => MenuItem.Size1 + System.Array.IndexOf(EditorTools.Sizes, size);

	// A context menu in the chrome's style (see EditorChrome.StyleMenu).
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
			case MenuItem.Undo:
			case MenuItem.Redo:
				if (!painting) {
					EditHistory.Step step = item == MenuItem.Undo ? painter.Undo() : painter.Redo();
					if (step != null) {
						game.EditorMapView.InvalidateMap();
						Say($"{(item == MenuItem.Undo ? "Undid" : "Redid")} {step.Name}");
						UpdateTitle();
						Refresh();
					}
				}
				break;
			case MenuItem.Grid:
				game.EditorMapView.gridLayer.visible = !GridShown;
				// Civ3: turning the coordinates on turns the grid on too, so
				// the grid going off takes them with it.
				if (!GridShown) {
					EngineStorage.gameData.showGridCoordinates = false;
				}
				game.EditorMapView.InvalidateMap();
				Refresh();
				break;
			case MenuItem.GridCoordinates:
				EngineStorage.gameData.showGridCoordinates = !EngineStorage.gameData.showGridCoordinates;
				if (EngineStorage.gameData.showGridCoordinates) {
					game.EditorMapView.gridLayer.visible = true;
				}
				game.EditorMapView.InvalidateMap();
				Refresh();
				break;
			case MenuItem.FoodShieldIcons:
				mapLayer.showYields = !mapLayer.showYields;
				game.EditorMapView.InvalidateMap();
				Refresh();
				break;
			case MenuItem.CleanMap:
				game.EditorMapView.bareMap = !game.EditorMapView.bareMap;
				Refresh();
				break;
			case MenuItem.Territory:
				game.EditorMapView.borderLayer.visible = !game.EditorMapView.borderLayer.visible;
				game.EditorMapView.InvalidateMap();
				Refresh();
				break;
			case MenuItem.CityNames:
				game.EditorMapView.cityLayer.showLabels = !game.EditorMapView.cityLayer.showLabels;
				game.EditorMapView.InvalidateMap();
				Refresh();
				break;
			case MenuItem.TileOwners:
				mapLayer.showOwners = !mapLayer.showOwners;
				game.EditorMapView.InvalidateMap();
				Refresh();
				break;
			case MenuItem.UnitCounts:
				mapLayer.showUnitCounts = !mapLayer.showUnitCounts;
				game.EditorMapView.InvalidateMap();
				Refresh();
				break;
			case MenuItem.Minimap:
				minimap.Visible = !minimap.Visible;
				if (!minimap.Visible) {
					minimap.SetFocused(false);
				}
				Refresh();
				break;
			case MenuItem.ZoomIn:
			case MenuItem.ZoomOut:
				MapView mapView = game.EditorMapView;
				mapView.setCameraZoom(mapView.cameraZoom + (item == MenuItem.ZoomIn ? 0.25f : -0.25f), GetViewportRect().Size / 2);
				break;
			case >= MenuItem.BrushTerrain and <= MenuItem.FogOfWar:
				Tool = EditorTool.Terrain + (item - MenuItem.BrushTerrain);
				Refresh();
				// As Civ3's: choosing the terrain, resource or overlay brush
				// asks which.
				ShowPicker(Tool);
				break;
			case >= MenuItem.Size1 and <= MenuItem.Size7:
				BrushSize = EditorTools.Sizes[item - MenuItem.Size1];
				Refresh();
				break;
			case MenuItem.GenerateMap:
				CloseDialog();
				dialog = chrome.WorldSetupDialog(EngineStorage.gameData.map, EngineStorage.gameData.worldSizes, setup => {
					CloseDialog();
					game.ReopenScenario(WorldMaker.Generate(game.CurrentScenario(), setup));
				}, CloseDialog);
				AddChild(dialog);
				break;
			case MenuItem.ClearMap:
				CloseDialog();
				dialog = chrome.ClearMapDialog(EngineStorage.gameData.terrainTypes, EngineStorage.gameData.worldSizes, (terrain, width, height) => {
					CloseDialog();
					GameMap old = EngineStorage.gameData.map;
					game.ReopenScenario(WorldMaker.Clear(game.CurrentScenario(), width, height, terrain, old.wrapHorizontally, old.wrapVertically, old.polarIceCaps));
				}, CloseDialog);
				AddChild(dialog);
				break;
			case >= MenuItem.RedistributeStarts and <= MenuItem.RedistributeGrassland:
				if (!painting) {
					MapGenerator.Redistribution what = MapGenerator.Redistribution.StartingLocations + (item - MenuItem.RedistributeStarts);
					EditHistory.Step step = painter.Redistribute(what, new System.Random().Next(int.MaxValue));
					game.EditorMapView.InvalidateMap();
					Say(step?.Name ?? "Nothing changed");
					UpdateTitle();
					Refresh();
				}
				break;
			case MenuItem.SetActivePlayer:
				ShowPlayerPicker();
				break;
			case MenuItem.RandomBonusGrassland:
				painter.RandomizeBonusGrassland = !painter.RandomizeBonusGrassland;
				Refresh();
				break;
			case MenuItem.WrapX:
			case MenuItem.WrapY:
			case MenuItem.PolarIceCaps: {
					// The map view is made for the map's wrapping, so the editor
					// opens again with it (as Civ3's editor redraws the map).
					GameMap old = EngineStorage.gameData.map;
					game.ReopenScenario(WorldMaker.SetMapFlags(game.CurrentScenario(),
						old.wrapHorizontally ^ (item == MenuItem.WrapX), old.wrapVertically ^ (item == MenuItem.WrapY), old.polarIceCaps ^ (item == MenuItem.PolarIceCaps)));
					break;
				}
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

	// What the current brush paints, for the status line.
	private string BrushName => Tool switch {
		EditorTool.Terrain => terrainBrush?.Name,
		EditorTool.Resources => resourceBrush?.Name,
		EditorTool.Overlays => overlayBrush?.Name,
		EditorTool.Units => unitBrush?.name,
		_ => null,
	};

	// The pickers' ids, past the menu items': (the tool + 1) thousands.
	private const int PickerIds = 1000;

	private static string PickerName(EditorTool tool) => tool switch {
		EditorTool.Terrain => "TerrainPicker",
		EditorTool.Resources => "ResourcePicker",
		EditorTool.Units => "UnitPicker",
		_ => "OverlayPicker",
	};

	// What the brush's pop-up lists, its icons (resources have theirs) and
	// which is in hand.
	private (List<string> names, List<Texture2D> icons, int chosen) PickerItems(EditorTool tool) {
		switch (tool) {
			case EditorTool.Terrain: {
					List<TerrainBrush> brushes = painter.TerrainBrushes();
					return (brushes.Select(b => b.Name).ToList(), null, brushes.IndexOf(terrainBrush));
				}
			case EditorTool.Resources: {
					List<C7GameData.Resource> resources = painter.ResourceBrushes();
					return (resources.Select(r => r.Name).ToList(), resources.Select(ResourceIcon).ToList(), resources.IndexOf(resourceBrush));
				}
			case EditorTool.Overlays: {
					List<OverlayBrush> brushes = painter.OverlayBrushes();
					return (brushes.Select(b => b.Name).ToList(), null, brushes.IndexOf(overlayBrush));
				}
			case EditorTool.Units: {
					List<UnitPrototype> types = EngineStorage.gameData.unitPrototypes;
					return (types.Select(t => t.name).ToList(), null, types.IndexOf(unitBrush));
				}
			default:
				return (null, null, -1);
		}
	}

	private static Texture2D ResourceIcon(C7GameData.Resource resource) {
		try {
			return TextureLoader.Load("resources.icon", resource, useCache: true);
		} catch (System.Exception) {
			return null;
		}
	}

	private PopupMenu playerPicker, civPicker;
	private const int AddPlayerId = 999;

	// Civ3's Set Active Player: the scenario's players, and Add Player for a
	// civilization that hasn't one.
	private void ShowPlayerPicker() {
		Discard(playerPicker);
		playerPicker = new PopupMenu { Name = "PlayerPicker" };
		chrome.StyleMenu(playerPicker);
		List<Player> players = EngineStorage.gameData.players;
		for (int i = 0; i < players.Count; ++i) {
			playerPicker.AddRadioCheckItem(ScenarioPlayers.NameOf(players[i]), i);
			playerPicker.SetItemChecked(i, players[i] == ActivePlayer);
		}
		if (players.Count > 0) {
			playerPicker.AddSeparator();
		}
		playerPicker.AddItem("Add Player...", AddPlayerId);
		playerPicker.IdPressed += id => {
			if (id == AddPlayerId) {
				ShowCivPicker();
			} else if (id < players.Count) {
				ActivePlayer = players[(int)id];
				Say($"Active player: {ScenarioPlayers.NameOf(ActivePlayer)}");
				Refresh();
			}
		};
		AddChild(playerPicker);
		playerPicker.Position = (Vector2I)GetViewport().GetMousePosition();
		playerPicker.Popup();
	}

	// The civilizations a player can be added for. Adding one opens the
	// scenario again with it (the players are the scenario's, as Civ3's
	// Scenario Properties set them), and it's the active player.
	private void ShowCivPicker() {
		SaveGame scenario = game.CurrentScenario();
		List<Civilization> civs = ScenarioPlayers.Available(scenario);
		Discard(civPicker);
		civPicker = new PopupMenu { Name = "CivPicker" };
		chrome.StyleMenu(civPicker);
		for (int i = 0; i < civs.Count; ++i) {
			civPicker.AddItem(civs[i].name, i);
		}
		civPicker.IdPressed += id => {
			Civilization civ = civs[(int)id];
			ScenarioPlayers.Add(scenario, civ, scenario.Scenario);
			ScenarioEditorSession.ActivePlayer = civ.name;
			game.ReopenScenario(scenario);
		};
		AddChild(civPicker);
		civPicker.Position = (Vector2I)GetViewport().GetMousePosition();
		civPicker.Popup();
	}

	// Takes a menu out of the tree at once (so a new one can have its name)
	// and frees it.
	private static void Discard(Node menu) {
		if (menu != null && IsInstanceValid(menu)) {
			menu.GetParent()?.RemoveChild(menu);
			menu.QueueFree();
		}
	}

	// Civ3's pop-up of what the brush paints, at the mouse.
	private void ShowPicker(EditorTool tool) {
		(List<string> names, List<Texture2D> icons, int chosen) = PickerItems(tool);
		if (names == null) {
			return;
		}
		if (pickers.TryGetValue(tool, out PopupMenu old)) {
			Discard(old);
		}
		PopupMenu picker = StyledMenu(PickerName(tool));
		picker.AddThemeConstantOverride("icon_max_width", 20);
		int ids = ((int)tool + 1) * PickerIds;
		for (int i = 0; i < names.Count; ++i) {
			if (icons?[i] is Texture2D icon) {
				picker.AddIconRadioCheckItem(icon, names[i], ids + i);
			} else {
				picker.AddRadioCheckItem(names[i], ids + i);
			}
			picker.SetItemChecked(i, i == chosen);
		}
		pickers[tool] = picker;
		AddChild(picker);
		picker.Position = (Vector2I)GetViewport().GetMousePosition();
		picker.Popup();
	}

	private void Choose(EditorTool tool, int index) {
		switch (tool) {
			case EditorTool.Terrain when index < painter.TerrainBrushes().Count:
				terrainBrush = painter.TerrainBrushes()[index];
				break;
			case EditorTool.Resources when index < painter.ResourceBrushes().Count:
				resourceBrush = painter.ResourceBrushes()[index];
				break;
			case EditorTool.Overlays when index < painter.OverlayBrushes().Count:
				overlayBrush = painter.OverlayBrushes()[index];
				break;
			case EditorTool.Units when index < EngineStorage.gameData.unitPrototypes.Count:
				unitBrush = EngineStorage.gameData.unitPrototypes[index];
				break;
			default:
				return;
		}
		Tool = tool;
		Refresh();
	}

	// Shows the tool, brush size and grid in the menus and the chrome.
	private void Refresh() {
		foreach (PopupMenu menu in FindChildren("*", nameof(PopupMenu), true, false).OfType<PopupMenu>()) {
			foreach (EditorTools.Info tool in EditorTools.All) {
				SetChecked(menu, ToolItem(tool.Tool), tool.Tool == Tool);
			}
			foreach (int size in EditorTools.Sizes) {
				SetChecked(menu, SizeItem(size), size == BrushSize);
				// Civ3's brush sizes are the terrain brush's.
				SetEnabled(menu, SizeItem(size), Tool == EditorTool.Terrain);
			}
			SetChecked(menu, MenuItem.Grid, GridShown);
			if (mapLayer != null) {
				MapView mapView = game.EditorMapView;
				SetChecked(menu, MenuItem.GridCoordinates, EngineStorage.gameData.showGridCoordinates);
				SetChecked(menu, MenuItem.FoodShieldIcons, mapLayer.showYields);
				SetChecked(menu, MenuItem.CleanMap, mapView.bareMap);
				SetChecked(menu, MenuItem.Territory, mapView.borderLayer.visible);
				SetChecked(menu, MenuItem.CityNames, mapView.cityLayer.showLabels);
				SetChecked(menu, MenuItem.TileOwners, mapLayer.showOwners);
				SetChecked(menu, MenuItem.UnitCounts, mapLayer.showUnitCounts);
				SetChecked(menu, MenuItem.Minimap, minimap.Visible);
				GameMap gameMap = EngineStorage.gameData.map;
				SetChecked(menu, MenuItem.WrapX, gameMap.wrapHorizontally);
				SetChecked(menu, MenuItem.WrapY, gameMap.wrapVertically);
				SetChecked(menu, MenuItem.PolarIceCaps, gameMap.polarIceCaps);
				SetChecked(menu, MenuItem.RandomBonusGrassland, painter.RandomizeBonusGrassland);
				// The status line's player follows a change of player.
				shownZoom = -1;
			}
			SetEnabled(menu, MenuItem.Undo, painter.History.CanUndo);
			SetEnabled(menu, MenuItem.Redo, painter.History.CanRedo);
		}
		chrome.ShowTool(Tool, BrushSize, GridShown, BrushName);
		if (mouseAt is Vector2 at) {
			ShowTileAt(at);
		}
		chrome.ShowUndo(painter.History.CanUndo ? painter.History.UndoName : null, painter.History.CanRedo ? painter.History.RedoName : null);
	}

	private static void SetEnabled(PopupMenu menu, MenuItem item, bool on) {
		int index = menu.GetItemIndex((int)item);
		if (index >= 0) {
			menu.SetItemDisabled(index, !on);
		}
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
		string modified = painter != null && painter.History.Modified ? " *" : "";
		TitleText = $"Scenario Editor: {title} ({file}){modified}{rulesOnly}";
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
			painter.History.MarkSaved();
			Say($"Saved {Path.GetFileName(path)}");
			UpdateTitle();
		} else {
			Say($"Couldn't save: {problem}", problem: true);
		}
	}

	// The tile a context menu was opened on.
	private Tile contextTile;

	// Civ3's right-click menu on the map: the tile's properties, Delete (a
	// submenu of what's on the tile, Civ3 1.34's "cascading popup submenus
	// for the many delete options"), and centering the view there.
	private void ShowContextMenu(Vector2 position) {
		contextTile = game.EditorTileAt(position);
		if (contextTile == null) {
			return;
		}
		Discard(contextMenu);
		contextMenu = StyledMenu("TileMenu");
		Add(contextMenu, "Tile Properties...", MenuItem.TileProperties);
		AddDeleteMenu(contextMenu, contextTile);
		Add(contextMenu, "Center Here", MenuItem.CenterHere);
		AddChild(contextMenu);
		contextMenu.Position = (Vector2I)position;
		contextMenu.Popup();
	}

	// The Delete submenu: each thing on the tile, and All when there are
	// several; greyed when there's nothing.
	private void AddDeleteMenu(PopupMenu menu, Tile tile) {
		List<Erasable> things = painter.ErasablesOn(tile);
		if (things.Count == 0) {
			menu.AddItem("Delete");
			menu.SetItemDisabled(menu.ItemCount - 1, true);
			return;
		}
		PopupMenu delete = new() { Name = "DeleteMenu" };
		chrome.StyleMenu(delete);
		for (int i = 0; i < things.Count; ++i) {
			delete.AddItem(things[i].Name, i);
		}
		if (things.Count > 1) {
			delete.AddSeparator();
			delete.AddItem("All", things.Count);
		}
		delete.IdPressed += id => Erase(tile, id < things.Count ? [things[(int)id]] : things);
		menu.AddChild(delete);
		menu.AddSubmenuNodeItem("Delete", delete);
	}

	private void Erase(Tile tile, List<Erasable> things) {
		if (painting || painter.Erase(tile, things).Count == 0) {
			return;
		}
		game.EditorMapView.InvalidateMap();
		Say(painter.History.UndoName);
		UpdateTitle();
		Refresh();
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
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } left when !panning:
				BeginStroke(left.Position);
				GetViewport().SetInputAsHandled();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when painting:
				EndStroke();
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
				if (painting) {
					PaintAlong(paintFrom, motion.Position);
					paintFrom = motion.Position;
				}
				mouseAt = motion.Position;
				ShowTileAt(motion.Position);
				break;
			// M or Alt+M shows or hides the minimap, Tab moves the focus between
			// it and the map (Civ3's keys).
			case InputEventKey { Pressed: true, Echo: false, Keycode: Key.M } when !Input.IsKeyPressed(Key.Ctrl):
				RunMenuItem(MenuItem.Minimap);
				GetViewport().SetInputAsHandled();
				break;
			case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab }:
				minimap.SetFocused(minimap.Visible && !minimap.Focused);
				GetViewport().SetInputAsHandled();
				break;
			case InputEventKey { Pressed: true, Echo: false } key when !key.CtrlPressed && !key.AltPressed
				&& EditorTools.All.FirstOrDefault(t => t.Hotkey != Key.None && t.Hotkey == key.Keycode) is EditorTools.Info tool:
				RunMenuItem(ToolItem(tool.Tool));
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
					mapView.cameraLocation += direction * KeyPanPixels * (minimap.Focused ? 4 : 1);
					GetViewport().SetInputAsHandled();
				}
				break;
		}
	}

	// Where the mouse was last over the map, so the status and cursor
	// follow a change of brush without a move.
	private Vector2? mouseAt;

	private void ShowTileAt(Vector2 position) {
		Tile tile = game.EditorTileAt(position);
		string cantPaint = tile == null ? null : CantPaint(tile, position);
		// Civ3's "invalid mouse cursor" where the brush can't paint.
		Input.SetDefaultCursorShape(cantPaint != null ? Input.CursorShape.Forbidden : Input.CursorShape.Arrow);
		chrome.ShowTile(tile == null ? "" : Describe(tile) + (cantPaint != null ? $"   (can't paint: {cantPaint})" : ""));
	}

	public override void _ExitTree() {
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
	}

	// Why the brush can't paint on the tile, or null.
	private string CantPaint(Tile tile, Vector2 position) {
		return Tool switch {
			EditorTool.Overlays when PaintingRivers => MapPainter.CantPlaceRiver(tile, EdgeAt(tile, position)),
			EditorTool.Terrain when terrainBrush != null => MapPainter.CantPaint(tile, terrainBrush.Terrain),
			EditorTool.Resources when resourceBrush != null => MapPainter.CantPlace(tile, resourceBrush),
			EditorTool.Overlays when overlayBrush != null => MapPainter.CantPlace(tile, overlayBrush),
			EditorTool.Cities => MapPainter.CantPlaceCity(tile, ActivePlayer),
			EditorTool.Units when unitBrush != null => MapPainter.CantPlaceUnit(tile, unitBrush, ActivePlayer),
			_ => null,
		};
	}

	// Starts a stroke where the button went down, with the brush in hand
	// (the terrain, resource and overlay brushes so far).
	private void BeginStroke(Vector2 position) {
		string name = Tool switch {
			EditorTool.Terrain when terrainBrush != null => $"Paint {terrainBrush.Name}",
			EditorTool.Resources when resourceBrush != null => $"Place {resourceBrush.Name}",
			EditorTool.Overlays when overlayBrush != null => $"Place {overlayBrush.Name}",
			EditorTool.Cities => "Place City",
			EditorTool.Units when unitBrush != null => $"Place {unitBrush.name}",
			_ => null,
		};
		if (name == null || game.EditorTileAt(position) == null) {
			return;
		}
		painting = true;
		paintFrom = position;
		painter.BeginStroke(name);
		PaintAt(position, pressed: true);
	}

	// Paints every tile the mouse passed over since the last event, so a
	// fast drag leaves no gaps: points a quarter of a tile's height apart.
	private void PaintAlong(Vector2 from, Vector2 to) {
		float step = Mathf.Max(4, MapView.cellSize.Y * game.EditorMapView.cameraZoom / 4);
		int steps = Mathf.Max(1, Mathf.CeilToInt(from.DistanceTo(to) / step));
		for (int i = 1; i <= steps; ++i) {
			PaintAt(from.Lerp(to, (float)i / steps));
		}
	}

	private bool PaintingRivers => Tool == EditorTool.Overlays && overlayBrush?.What == OverlayBrush.Kind.River;

	// The tile's edge nearest the point: the quarter of the diamond it's in.
	private TileDirection EdgeAt(Tile tile, Vector2 position) {
		Vector2 off = position - game.EditorMapView.screenLocationOfTile(tile, true);
		return off.Y < 0
			? (off.X >= 0 ? TileDirection.NORTHEAST : TileDirection.NORTHWEST)
			: (off.X >= 0 ? TileDirection.SOUTHEAST : TileDirection.SOUTHWEST);
	}

	private void PaintAt(Vector2 position, bool pressed = false) {
		Tile tile = game.EditorTileAt(position);
		// The river brush lays the edge the mouse is near: a click anywhere
		// in the tile takes the edge of its quarter, a drag only edges it
		// passes close to, so it follows the line the mouse draws.
		if (PaintingRivers) {
			if (tile != null && (pressed || !WellInside(tile, position))
				&& painter.PlaceRiver(tile, EdgeAt(tile, position)).Count > 0) {
				game.EditorMapView.InvalidateMap();
			}
			return;
		}
		if (tile == null || tile == lastPainted) {
			return;
		}
		// A one-tile brush dragged straight from tile to tile crosses the
		// corners where four tiles meet; it takes a tile only once the mouse
		// is well inside it, or a road would come out as a ladder.
		if (!pressed && Tool != EditorTool.Terrain && !WellInside(tile, position)) {
			return;
		}
		// A city or a unit a click (Civ3's: "each click places one").
		if (!pressed && Tool is EditorTool.Cities or EditorTool.Units) {
			return;
		}
		lastPainted = tile;
		List<Tile> changed = Tool switch {
			EditorTool.Resources => painter.PlaceResource(tile, resourceBrush),
			EditorTool.Overlays => painter.PlaceOverlay(tile, overlayBrush),
			EditorTool.Cities => painter.PlaceCity(tile, ActivePlayer) is null ? [] : [tile],
			EditorTool.Units => painter.PlaceUnit(tile, unitBrush, ActivePlayer) is null ? [] : [tile],
			_ => painter.PaintTerrain(tile, BrushSize, terrainBrush),
		};
		if (changed.Count > 0) {
			game.EditorMapView.InvalidateMap();
		}
	}

	// Whether the point is within the inner 60% of the tile's diamond (it
	// reaches a cell's width and height from its center).
	private bool WellInside(Tile tile, Vector2 position) {
		MapView mapView = game.EditorMapView;
		Vector2 off = (position - mapView.screenLocationOfTile(tile, true)).Abs();
		Vector2 half = mapView.scaledCellSize;
		return off.X / half.X + off.Y / half.Y <= 0.6f;
	}

	private void EndStroke() {
		painting = false;
		lastPainted = null;
		if (painter.EndStroke() != null) {
			game.EditorMapView.InvalidateMap();
			UpdateTitle();
			Refresh();
		}
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

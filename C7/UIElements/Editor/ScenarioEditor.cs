using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7Engine;
using C7GameData;
using Godot;

// The scenario editor's own controls over the map: a menu bar across the top
// (as Civ3's editor's), and a status bar along the bottom that says what's on
// the tile under the mouse. The map itself is the game scene's map view (see
// Game_Editor.cs); this moves its camera and, from Phase 1, paints on it.
public partial class ScenarioEditor : Control {
	private readonly Game game;

	private Label titleLabel;
	private Label statusLabel;
	private Label messageLabel;
	private PopupMenu fileMenu;
	private PopupMenu viewMenu;
	private FileDialog fileDialog;

	// Whether the file dialog is choosing a file to open or to save as.
	private bool choosingFileToSave;

	// Panning the map by dragging with the middle button.
	private bool panning;
	private Vector2 panFrom;

	private enum FileItem { New, Open, Save, SaveAs, Exit }
	private enum ViewItem { Grid }

	// The bars' text, light on their dark panels (the game's theme makes
	// labels dark, for parchment).
	private static readonly Color BarText = new(0.92f, 0.92f, 0.88f);

	// Arrow keys move the view by this many pixels.
	private const float KeyPanPixels = 128;

	public ScenarioEditor() { }

	public ScenarioEditor(Game game) {
		this.game = game;
		Name = "ScenarioEditor";
	}

	public override void _Ready() {
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		// The map under the editor gets the clicks the bars don't take.
		MouseFilter = MouseFilterEnum.Ignore;

		PanelContainer top = new() { MouseFilter = MouseFilterEnum.Stop };
		top.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
		HBoxContainer topRow = new();
		MenuBar menuBar = new();
		fileMenu = new PopupMenu { Name = "File" };
		fileMenu.AddItem("New", (int)FileItem.New, (Key)KeyModifierMask.MaskCtrl | Key.N);
		fileMenu.AddItem("Open...", (int)FileItem.Open, (Key)KeyModifierMask.MaskCtrl | Key.O);
		fileMenu.AddItem("Save", (int)FileItem.Save, (Key)KeyModifierMask.MaskCtrl | Key.S);
		fileMenu.AddItem("Save As...", (int)FileItem.SaveAs);
		fileMenu.AddSeparator();
		fileMenu.AddItem("Exit to Main Menu", (int)FileItem.Exit);
		fileMenu.IdPressed += id => OnFileMenu((FileItem)(int)id);
		menuBar.AddChild(fileMenu);
		viewMenu = new PopupMenu { Name = "View" };
		viewMenu.AddCheckItem("Grid", (int)ViewItem.Grid);
		viewMenu.IdPressed += id => OnViewMenu((ViewItem)(int)id);
		menuBar.AddChild(viewMenu);
		topRow.AddChild(menuBar);
		titleLabel = new Label {
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		titleLabel.AddThemeColorOverride("font_color", BarText);
		topRow.AddChild(titleLabel);
		top.AddChild(topRow);
		AddChild(top);

		PanelContainer bottom = new() { MouseFilter = MouseFilterEnum.Stop };
		bottom.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
		bottom.GrowVertical = GrowDirection.Begin;
		HBoxContainer bottomRow = new();
		statusLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, Text = " " };
		statusLabel.AddThemeColorOverride("font_color", BarText);
		bottomRow.AddChild(statusLabel);
		messageLabel = new Label();
		messageLabel.AddThemeColorOverride("font_color", BarText);
		bottomRow.AddChild(messageLabel);
		bottom.AddChild(bottomRow);
		AddChild(bottom);

		fileDialog = new FileDialog {
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = false,
		};
		fileDialog.FileSelected += OnFileChosen;
		AddChild(fileDialog);

		UpdateTitle();

		// The --editor-shot=<png> developer option: a screenshot of the editor
		// once the map has been drawn, then quit, to check the editor without
		// clicking through it.
		shotPath = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--editor-shot="))?["--editor-shot=".Length..];
	}

	private string shotPath;
	private int framesShown;

	public override void _Process(double delta) {
		if (shotPath != null && ++framesShown == 90) {
			GetViewport().GetTexture().GetImage().SavePng(shotPath);
			GetTree().Quit();
		}
	}

	private void UpdateTitle() {
		string title = string.IsNullOrEmpty(game.EditorInfo.title) ? "Untitled" : game.EditorInfo.title;
		string file = game.EditorPath == null ? "not saved yet" : Path.GetFileName(game.EditorPath);
		string rulesOnly = game.EditorInfo.customMap ? "" : "   (rules only: the map isn't saved)";
		titleLabel.Text = $"Scenario Editor: {title} ({file}){rulesOnly}";
	}

	// Says something in the status bar, as Saved or why something failed.
	private void Say(string message, bool problem = false) {
		messageLabel.Text = message;
		messageLabel.AddThemeColorOverride("font_color", problem ? new Color(1, 0.45f, 0.45f) : BarText);
	}

	private void OnFileMenu(FileItem item) {
		switch (item) {
			case FileItem.New:
				game.NewScenario();
				break;
			case FileItem.Open:
				ShowFileDialog(save: false);
				break;
			case FileItem.Save:
				if (game.EditorPath == null) {
					ShowFileDialog(save: true);
				} else {
					SaveTo(game.EditorPath);
				}
				break;
			case FileItem.SaveAs:
				ShowFileDialog(save: true);
				break;
			case FileItem.Exit:
				game.ExitEditor();
				break;
		}
	}

	private void OnViewMenu(ViewItem item) {
		if (item == ViewItem.Grid) {
			int index = viewMenu.GetItemIndex((int)ViewItem.Grid);
			bool on = !viewMenu.IsItemChecked(index);
			viewMenu.SetItemChecked(index, on);
			game.EditorMapView.gridLayer.visible = on;
			game.EditorMapView.InvalidateMap();
		}
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

	// The map's input (see Game.EditorUnhandledInput): the wheel zooms,
	// the middle button drags the view, the arrow keys move it, and the
	// status bar follows the mouse.
	internal void HandleMapInput(InputEvent @event) {
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
			case InputEventMouseMotion motion:
				if (panning) {
					mapView.cameraLocation += panFrom - motion.Position;
					panFrom = motion.Position;
				}
				ShowTileAt(motion.Position);
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
		statusLabel.Text = tile == null ? "" : Describe(tile);
	}

	// What's on a tile, for the status bar: where it is, its terrain, its
	// resource, a city and how many units.
	internal static string Describe(Tile tile) {
		List<string> parts = new();
		string terrain = tile.overlayTerrainType?.DisplayName ?? "";
		if (tile.baseTerrainType != null && tile.baseTerrainType != tile.overlayTerrainType) {
			terrain += $" on {tile.baseTerrainType.DisplayName}";
		}
		parts.Add(terrain);
		if (tile.Resource != null && tile.Resource != C7GameData.Resource.NONE && tile.Resource.Category != ResourceCategory.NONE) {
			parts.Add(tile.Resource.Name);
		}
		if (tile.HasCity(out City city)) {
			parts.Add($"{city.name} (size {city.residents.Count}, {city.owner?.civilization?.name ?? "nobody's"})");
		}
		if (tile.unitsOnTile.Count > 0) {
			parts.Add(tile.unitsOnTile.Count == 1 ? $"1 unit ({tile.unitsOnTile[0].unitType.name})" : $"{tile.unitsOnTile.Count} units");
		}
		return $"({tile.XCoordinate}, {tile.YCoordinate})   " + string.Join(", ", parts.Where(p => p.Length > 0));
	}
}

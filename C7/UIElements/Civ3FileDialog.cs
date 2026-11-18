using C7Engine;
using C7GameData;
using Godot;
using Serilog;

[GlobalClass]
public partial class Civ3FileDialog : FileDialog {
	// An object for passing information (like save file paths) between scenes.
	GlobalSingleton Global;
	private ILogger log;

	// If true, go to scenario setup after loading, for scenarios.
	public bool GoToScenarioSetupAfterLoading = false;

	// Where the game saves, and looks first for saves to load. The Civ 3
	// folder is often read-only, or missing in standalone mode.
	public static string SavesDirectory => System.IO.Path.Combine(C7Settings.WritableDirectory, "Saves");

	private string Civ3SavesDirectory => Util.Civ3Root + "/Conquests/Saves";

	private const string ShowSavesAction = "show_saves";
	private const string ShowCiv3SavesAction = "show_civ3_saves";
	private const string ShowScenariosAction = "show_scenarios";
	private const string ShowCiv3ScenariosAction = "show_civ3_scenarios";
	private string Civ3ScenariosDirectory => Util.Civ3Root + "/Conquests/Scenarios";

	// The buttons that switch between our saves and Civ 3's, when loading.
	private Button savesButton, civ3SavesButton;
	// And between the scenarios made in the scenario editor and Civ 3's.
	private Button scenariosButton, civ3ScenariosButton;

	public override void _Ready() {
		base._Ready();
		log = LogManager.ForContext<Civ3FileDialog>();

		FileMode = FileDialog.FileModeEnum.OpenFile;
		Access = AccessEnum.Filesystem;

		Global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		FileSelected += OnFileSelected;

		savesButton = AddButton("Our Saves", false, ShowSavesAction);
		civ3SavesButton = AddButton("Civ3 Saves", false, ShowCiv3SavesAction);
		savesButton.Visible = civ3SavesButton.Visible = false;
		scenariosButton = AddButton("Our Scenarios", false, ShowScenariosAction);
		civ3ScenariosButton = AddButton("Civ3 Scenarios", false, ShowCiv3ScenariosAction);
		scenariosButton.Visible = civ3ScenariosButton.Visible = false;
		CustomAction += OnCustomAction;
	}

	private void OnCustomAction(StringName action) {
		if (action == ShowSavesAction) {
			CurrentDir = SavesDirectory;
		} else if (action == ShowCiv3SavesAction) {
			CurrentDir = Civ3SavesDirectory;
		} else if (action == ShowScenariosAction) {
			CurrentDir = ScenarioEditorSession.ScenariosDirectory;
		} else if (action == ShowCiv3ScenariosAction) {
			CurrentDir = Civ3ScenariosDirectory;
		}
	}

	private static void EnsureSavesDirectory() {
		try {
			System.IO.Directory.CreateDirectory(SavesDirectory);
		} catch (System.Exception e) {
			LogManager.ForContext<Civ3FileDialog>().Warning(e, "Couldn't create the saves directory {Directory}", SavesDirectory);
		}
	}

	// Opens on our saves, with a button to browse Civ 3's for its .SAV files.
	public void SetDirectoryForLoadingSaves() {
		EnsureSavesDirectory();
		CurrentDir = SavesDirectory;
		FileMode = FileDialog.FileModeEnum.OpenFile;
		// Only saves, so a recorded game's play log (.jsonl) beside them isn't offered.
		Filters = new[] { "*.json, *.sav, *.SAV ; Saved Games" };
		ShowSavesButtons(true);
	}

	// Opens on the scenarios made in the scenario editor, with a button to
	// browse Civ 3's (which aren't there without Civ 3).
	public void SetDirectoryForLoadingScenarios() {
		try {
			System.IO.Directory.CreateDirectory(ScenarioEditorSession.ScenariosDirectory);
		} catch (System.Exception e) {
			LogManager.ForContext<Civ3FileDialog>().Warning(e, "Couldn't create the scenarios directory {Directory}", ScenarioEditorSession.ScenariosDirectory);
		}
		CurrentDir = ScenarioEditorSession.ScenariosDirectory;
		FileMode = FileDialog.FileModeEnum.OpenFile;
		Filters = new[] { "*.json, *.biq, *.BIQ, *.bix, *.BIX, *.bic, *.BIC ; Scenarios" };
		ShowSavesButtons(false);
		if (scenariosButton != null) {
			scenariosButton.Visible = true;
			civ3ScenariosButton.Visible = System.IO.Directory.Exists(Civ3ScenariosDirectory);
		}
	}

	public void SetDirectoryForLoading(string RelPath) {
		CurrentDir = Util.Civ3Root + "/" + RelPath;
		FileMode = FileDialog.FileModeEnum.OpenFile;
		ClearFilters();
		ShowSavesButtons(false);
	}

	// Opens on the saves folder, or on a recorded game's own folder (see
	// C7Engine.PlayLog.PlayLogFolders) with the save's name filled in.
	public void SetDirectoryForSaving(string folder = null, string fileName = null) {
		EnsureSavesDirectory();
		CurrentDir = SavesDirectory;
		if (!string.IsNullOrEmpty(folder)) {
			try {
				System.IO.Directory.CreateDirectory(folder);
				CurrentDir = folder;
			} catch (System.Exception e) {
				log.Warning(e, "Couldn't make the game's folder {Directory}", folder);
			}
		}
		FileMode = FileDialog.FileModeEnum.SaveFile;
		Filters = new[] { "*.json ; Saved Games" };
		if (!string.IsNullOrEmpty(fileName)) {
			CurrentFile = fileName + ".json";
		}
		ShowSavesButtons(false);
	}

	private void ShowSavesButtons(bool show) {
		if (savesButton == null) {
			return;
		}
		savesButton.Visible = show;
		civ3SavesButton.Visible = show && System.IO.Directory.Exists(Civ3SavesDirectory);
		scenariosButton.Visible = civ3ScenariosButton.Visible = false;
	}

	private void OnFileSelected(string path) {
		if (FileMode == FileDialog.FileModeEnum.OpenFile) {
			log.Information($"loading {path}");
			Global.LoadGamePath = path;
			if (GoToScenarioSetupAfterLoading) {
				GetTree().ChangeSceneToFile("res://UIElements/NewGame/scenario_setup.tscn");
			} else {
				LanSession.StartGame(GetTree());
			}
		} else {
			if (!path.EndsWith(".json")) {
				path = path + ".json";
			}

			log.Information($"Saving game to {path}");
			// The game is taken now, and written on a worker thread.
			Node parent = GetParent() ?? this;
			try {
				ReportSaveFailure(SaveManager.SaveInBackground(EngineStorage.gameData, path), path, parent);
			} catch (System.Exception e) {
				ShowSaveFailure(e, path, parent);
			}
		}
	}

	private async void ReportSaveFailure(System.Threading.Tasks.Task saving, string path, Node parent) {
		try {
			await saving;
		} catch (System.Exception e) {
			ShowSaveFailure(e, path, parent);
		}
	}

	private void ShowSaveFailure(System.Exception e, string path, Node parent) {
		log.Error(e, "Couldn't save the game to {Path}", path);
		if (IsInstanceValid(parent) && parent.IsInsideTree()) {
			Util.ShowErrorDialog(parent, "Couldn't save the game", $"The game couldn't be saved to\n{path}\n\n{e.Message}");
		}
	}
}

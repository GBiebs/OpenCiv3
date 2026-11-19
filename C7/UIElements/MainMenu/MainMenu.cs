using Godot;
using System;
using System.Linq;
using C7Engine;
using C7Engine.Network;
using Serilog;

public partial class MainMenu : Node {
	private ILogger log;

	[Export]
	Civ3FileDialog LoadDialog;
	[Export]
	Control NoCiv3Options;
	[Export]
	FileDialog SetCiv3HomeDialog;
	[Export]
	Civ3FileDialog LoadScenarioDialog;
	[Export]
	MenuButtonContainer ButtonContainer;

	GlobalSingleton Global;

	private static bool devWatchUsed = false;
	AudioManager AudioManager;

	public override void _Ready() {
		log = LogManager.ForContext<MainMenu>();
		log.Debug("enter MainMenu._Ready");

		DisplayServer.WindowSetTitle((string)ProjectSettings.GetSetting("application/config/name"));

		try {
			ShowVersion();
			DisplayTitleScreen();

			// A load given up on hosts nothing: otherwise the next game
			// started from the menu would be hosted on the LAN.
			LoadDialog.Canceled += () => LanSession.HostNextGame = false;

			AudioManager = GetNode<AudioManager>("/root/GlobalAudioManager");

			if (LanSession.TakeDevStart()) {
				PlayMusic();
				CallDeferred(nameof(StartDevLanGame));
			} else if (!devWatchUsed && Array.IndexOf(OS.GetCmdlineUserArgs(), "--watch-ai") >= 0) {
				// The --watch-ai developer option goes straight into a game
				// to watch, the first time the menu shows.
				devWatchUsed = true;
				PlayMusic();
				CallDeferred(nameof(WatchAiQuickStart));
			} else if (!devEditorUsed && OS.GetCmdlineUserArgs().FirstOrDefault(a => a == "--edit-scenario" || a.StartsWith("--edit-scenario=")) is string editArg) {
				// The --edit-scenario[=<file>] developer option opens the scenario
				// editor on a new scenario, or on the file, the first time the menu
				// shows.
				devEditorUsed = true;
				CallDeferred(nameof(OpenScenarioEditorFromCommandLine), editArg.Contains('=') ? editArg[(editArg.IndexOf('=') + 1)..] : "");
			} else {
				PlayIntroThenMusic();
			}
		} catch (Exception ex) {
			log.Error(ex, "Could not set up the main menu");
		}
	}

	// The title screen's version, "Air v0.8.2": the version the launcher
	// installed (its installed.json beside the game), else the project's
	// own ("Air dev" in the editor).
	private void ShowVersion() {
		string version = (string)ProjectSettings.GetSetting("application/config/version", "");
		try {
			string record = System.IO.Path.Combine(GamePaths.BaseDir, "installed.json");
			if (System.IO.File.Exists(record)) {
				using var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(record));
				if (json.RootElement.TryGetProperty("version", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String) {
					version = v.GetString();
				}
			}
		} catch (Exception ex) {
			log.Warning(ex, "Could not read the installed version");
		}
		bool numbered = version.Length > 0 && char.IsDigit(version[0]);
		GetNode<RichTextLabel>("Control/Background/RichTextLabel").Text =
			numbered ? $"Air v{version}" : $"Air {version}".TrimEnd();
	}

	private void DisplayTitleScreen() {
		// To pass data between scenes, putting path string in a global singleton and reading it later in createGame
		Global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		Global.ResetLoadGameFields();

		// Let go of anything the UI cached from the last game, including the
		// art of a scenario it was.
		UICaches.Clear();
		Util.setModPath(null);

		// Back at the menu, any LAN game is over.
		LanSession.End();
		LanSession.HostNextGame = false;
		LanSession.PendingGame = null;
		LanSession.ResumeGame = null;
		Global.WatchNextGame = false;
		Global.Scenario = GameScenario.None;
		ScenarioPlay.Clear();
		// A Civ3 scenario or save played in Civ3's mode only for its game.
		Global.ActivatePreferredGameMode();

		LoadDialog.SetDirectoryForLoadingSaves();
		LoadScenarioDialog.SetDirectoryForLoadingScenarios();
		LoadScenarioDialog.GoToScenarioSetupAfterLoading = true;

		// Without a Civ3 install, play in standalone mode rather than asking.
		// The Settings page switches between the two.
		if (!C7Settings.UseStandaloneMode() && !GamePaths.Civ3Available()) {
			log.Information("No Civ3 install found, switching to standalone mode");
			Global.ActivateGameMode(GamePaths.standalone, remember: true);
		}

		ButtonContainer.Visible = true;
		ButtonContainer.CreateButtons();

		// TODO: enable buttons are features are implemented
		ButtonContainer.NewGame.Pressed += () => {
			LanSession.HostNextGame = false;
			GoToWorldSetup();
		};
		ButtonContainer.QuickStart.Pressed += QuickStartGame;
		ButtonContainer.WatchAi.Pressed += WatchAiGame;
		ButtonContainer.Tutorial.Pressed += QuickStartGame;
		// Civ3's original main menu had a Tutorial (Text\labels.txt,
		// #TOP_MENU), but Conquests' has none (Conquests\Text\labels.txt) and
		// its executable has no tutorial game setup, so how that game was set
		// up can't be matched. The tutorial is turned on in a game instead,
		// with Tutorial Mode in the Preferences (see TutorialGuide).
		ButtonContainer.Tutorial.Visible = false;
		ButtonContainer.LoadGame.Pressed += LoadGame;
		ButtonContainer.LoadScenario.Pressed += LoadScenario;
		ButtonContainer.ScenarioEditor.Pressed += OpenScenarioEditor;
		ButtonContainer.HostLan.Pressed += HostLanGame;
		ButtonContainer.JoinLan.Pressed += JoinLanGame;
		ButtonContainer.BrowseOnline.Pressed += BrowseOnlineGames;
		ButtonContainer.HallOfFame.Pressed += HallOfFame;
		ButtonContainer.Settings.Pressed += ShowSettings;
		ButtonContainer.Preferences.Pressed += Preferences;
		ButtonContainer.Preferences.Visible = false;
		ButtonContainer.AudioPreferences.Pressed += Preferences;
		ButtonContainer.AudioPreferences.Visible = false;
		ButtonContainer.Credits.Pressed += showCredits;
		ButtonContainer.Exit.Pressed += _on_Exit_pressed;

		// Hide if valid path is present as proven by reaching this point in code
		NoCiv3Options.Visible = false;
	}

	public void GoToWorldSetup() {
		PlayButtonPressedSound();
		GetTree().ChangeSceneToFile("res://UIElements/NewGame/world_setup.tscn");
	}

	private static bool devEditorUsed = false;

	private void OpenScenarioEditorFromCommandLine(string path) {
		if (path == "") {
			OpenScenarioEditor();
			return;
		}
		ScenarioEditorSession.Open(ScenarioEditorSession.OpenFile(path, out bool isScenario), isScenario ? path : null);
		GetTree().ChangeSceneToFile("res://C7Game.tscn");
	}

	// The scenario editor, on a new scenario of the current rules (see
	// Game_Editor.cs).
	public void OpenScenarioEditor() {
		PlayButtonPressedSound();
		ScenarioEditorSession.Open(C7Engine.Editor.ScenarioFiles.NewScenario(Global.GameMode.GetSave()), null);
		GetTree().ChangeSceneToFile("res://C7Game.tscn");
	}

	public void QuickStartGame() {
		log.Information("start game button pressed");
		PlayButtonPressedSound();
		QuickStartSetup.Init(Global);
		GetTree().ChangeSceneToFile("res://C7Game.tscn");
	}

	// Sets up a game as New Game does, with the map, rivals and rules the
	// user chooses, for the AIs to play while the user watches.
	public void WatchAiGame() {
		Global.WatchNextGame = true;
		LanSession.HostNextGame = false;
		GoToWorldSetup();
	}

	// Starts a game to watch straight away, as Quick Start would (the
	// --watch-ai developer option).
	public void WatchAiQuickStart() {
		Global.WatchNextGame = true;
		QuickStartGame();
	}

	public void LoadGame() {
		log.Information("load game button pressed");
		LanSession.HostNextGame = false;
		OpenLoadDialog();
	}

	private void OpenLoadDialog() {
		PlayButtonPressedSound();
		LoadDialog.Popup();
	}

	// Asks whether to host a new game or a saved one; either way the game
	// opens the LAN lobby once it's ready. A new game is set up on the
	// Multiplayer Setup screen, as in Civ3 (owner's request, 2026-10-28;
	// the world and player setup screens' way was then dropped for it).
	public void HostLanGame() {
		PlayButtonPressedSound();
		AcceptDialog dialog = new() {
			Title = "Host LAN Game",
			DialogText = "Set up a game for players on your local network, or a hotseat game.\n" +
				"For a new game, set a slot to Open for each other player on the\n" +
				"Multiplayer Setup screen; they choose their own civilizations when they join.\n" +
				"Or load a game saved with several human players.",
			OkButtonText = "New Game",
		};
		dialog.AddButton("Load Game", true, "load");
		// The game this machine last hosted, saved as its last turn began.
		Button resume = dialog.AddButton("Resume Last LAN Game", true, "resume");
		resume.Disabled = !LanAutosave.Exists(LanAutosave.DefaultDirectory);
		resume.TooltipText = resume.Disabled
			? "No LAN game hosted here has been saved yet."
			: "Host the last LAN game hosted here again, from the start of its last turn. Its players get their seats back when they reconnect.";
		dialog.AddCancelButton("Cancel");
		dialog.Confirmed += () => {
			// That screen says whether the game is hosted, when it's launched.
			LanSession.HostNextGame = false;
			PlayButtonPressedSound();
			GetTree().ChangeSceneToFile("res://UIElements/NewGame/multiplayer_setup.tscn");
		};
		dialog.CustomAction += action => {
			if (action == "load") {
				dialog.Hide();
				LanSession.HostNextGame = true;
				OpenLoadDialog();
			} else if (action == "resume") {
				dialog.Hide();
				ResumeLanGame();
			}
		};
		dialog.Canceled += () => LanSession.HostNextGame = false;
		// The dialog is made for each click, so free it once it closes.
		dialog.VisibilityChanged += () => {
			if (!dialog.Visible) {
				dialog.QueueFree();
			}
		};
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void ResumeLanGame() {
		LanResumeInfo resume = LanAutosave.ReadResumeInfo(LanAutosave.DefaultDirectory);
		if (resume == null) {
			log.Warning("There is no LAN game to resume");
			return;
		}
		LanSession.ResumeGame = resume;
		LanSession.HostNextGame = true;
		LanSession.StartGame(GetTree());
	}

	private void StartDevLanGame() {
		if (LanSession.DevHostSave != null) {
			Global.LoadGamePath = LanSession.DevHostSave;
			LanSession.HostNextGame = true;
			LanSession.StartGame(GetTree());
		} else {
			JoinLanGame();
		}
	}

	public void JoinLanGame() {
		PlayButtonPressedSound();
		LanLobby.joining = true;
		GetTree().ChangeSceneToFile(LanSession.LobbyScene);
	}

	// The join screen, with the server browser open over it.
	public void BrowseOnlineGames() {
		LanLobby.openBrowser = true;
		JoinLanGame();
	}

	public void LoadScenario() {
		log.Information("load scenario button pressed");
		PlayButtonPressedSound();
		LoadScenarioDialog.Popup();
	}

	public void showCredits() {
		log.Information("credits button pressed");
		GetTree().ChangeSceneToFile("res://Credits.tscn");
	}

	// The main menu's Hall of Fame: as in Conquests, its Campaign Record
	// first, from which the Hall of Fame itself is a button away (Civ3
	// FUN_0055a5e0, menu item 6). Without Conquests' campaigns, straight to
	// the Hall of Fame.
	public void HallOfFame() {
		PlayButtonPressedSound();
		if (CampaignRecordScreen.LoadRecord().Count == 0) {
			ShowHallOfFame();
			return;
		}
		CampaignRecordScreen screen = new(fromMainMenu: true);
		screen.HallOfFameRequested += ShowHallOfFame;
		screen.NextCampaignRequested += PlayCampaign;
		ShowOverMenu(screen, h => screen.Closed += h);
	}

	// Civ3's Hall of Fame, over the menu until it's closed.
	private void ShowHallOfFame() {
		HallOfFameScreen screen = new();
		ShowOverMenu(screen, h => screen.Closed += h);
	}

	private void ShowOverMenu(Control screen, Action<Action> onClosed) {
		CanvasLayer layer = new() { Layer = 10 };
		onClosed(() => layer.QueueFree());
		layer.AddChild(screen);
		AddChild(layer);
	}

	// The Campaign Record's Next: the campaign's scenario, set up as Load
	// Scenario would.
	private void PlayCampaign(string path) {
		log.Information($"playing the campaign {path}");
		LanSession.HostNextGame = false;
		Global.LoadGamePath = path;
		GetTree().ChangeSceneToFile("res://UIElements/NewGame/scenario_setup.tscn");
	}

	public void ShowSettings() {
		log.Information("settings button pressed");
		PlayButtonPressedSound();
		GetTree().ChangeSceneToFile("res://UIElements/Settings/settings_menu.tscn");
	}

	public void Preferences() {
		PlayButtonPressedSound();
	}

	public void _on_Exit_pressed() {
		GetTree().Quit(); // no need to notify the scene tree
	}

	private void _on_SetCiv3Home_pressed() {
		SetCiv3HomeDialog.Popup();
	}

	private void _on_SetCiv3HomeDialog_dir_selected(string path) {
		Util.Civ3Root = path;
		C7Settings.SetValue("locations", "civ3InstallDir", path);
		C7Settings.SaveSettings();
		// This function should only be reachable if DisplayTitleScreen failed on previous runs, so should be OK to run here
		DisplayTitleScreen();
	}

	private void UseStandaloneModePressed() {
		Global.ActivateGameMode(GamePaths.standalone, remember: true);
		DisplayTitleScreen();
	}

	// The intro movie the first time the menu shows, if there is one and it
	// isn't turned off, with the menu's music once it's over.
	private void PlayIntroThenMusic() {
		IntroVideo intro = IntroVideo.TakeForLaunch();
		if (intro == null) {
			PlayMusic();
			return;
		}
		log.Information("Playing the intro movie");
		intro.Finished += PlayMusic;
		AddChild(intro);
	}

	private void PlayMusic() {
		AudioManager.PlayMusic("menu.main_menu_1");
	}

	private void PlayButtonPressedSound() {
		AudioManager.PlayUIAudio("buttons.button_1");
	}
}

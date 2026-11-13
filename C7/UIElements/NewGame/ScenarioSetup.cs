using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using C7Engine;
using C7GameData.Save;
using Serilog;

public partial class ScenarioSetup : Control {
	private static ILogger log = LogManager.ForContext<ScenarioSetup>();

	[Export] TextureRect background;

	[Export] GridContainer playerListContainer;
	List<Civilization> civilizations = new();
	Civilization civilization = null;
	ButtonGroup playerListButtonGroup = new();
	TextureRect leaderHead = new();
	[Export] Label civLabel;

	[Export] GridContainer difficultyContainer;

	Difficulty difficulty = null;
	ButtonGroup difficultyButtonGroup = new();

	[Export] TextureButton confirm;
	[Export] TextureButton cancel;

	[Export] Label loadingLabel;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready() {
		SaveGame save = GetSave();
		// A scenario of rules alone has no players to pick from: its game is
		// set up as a new one (see ScenarioPlay).
		if (ScenarioPlay.SetUpAsNewGame(save, GetNode<GlobalSingleton>("/root/GlobalSingleton").LoadGamePath, GetTree())) {
			return;
		}

		// Set up buttons for the civs the player can play as.
		civilizations = save.Civilizations;
		playerListContainer.Columns = (int)Math.Ceiling(civilizations.Count / 12.0);

		List<SavePlayer> pickablePlayers = save.Players.Where(p => p.canBePicked).ToList();
		string initiallySelectedCiv = pickablePlayers.First(p => p.canBePicked).civilization;

		foreach (SavePlayer player in pickablePlayers) {
			Civilization civ = civilizations.Find(c => c.name == player.civilization);

			Civ3MenuButton button = new() {
				Text = civ.name,
				FontSize = 12,
				textPosition = Civ3MenuButton.TextPosition.TextLeftOfIcon,
				ButtonGroup = playerListButtonGroup,
				ToggleMode = true,
			};
			button.Pressed += () => {
				this.civilization = civ;
				DisplaySelectedLeader();
			};
			playerListContainer.AddChild(button);

			if (civ.name == initiallySelectedCiv) {
				button.ButtonPressed = true;
				this.civilization = civ;
			}
		}
		background.AddChild(leaderHead);
		DisplaySelectedLeader();

		// Set up the difficulty buttons
		difficultyContainer.Columns = save.Difficulties.Count;
		// The difficulty chosen for the last game, if the scenario has it.
		string initiallySelectedDifficulty = NewGameChoices.Load().ChosenDifficulty(save.Difficulties).Name;
		foreach (Difficulty difficulty in save.Difficulties) {
			CenterContainer container = new();
			difficultyContainer.AddChild(container);

			Civ3MenuButton button = new(Civ3MenuButton.TextPosition.TextAboveIcon) {
				Text = difficulty.Name,
				ButtonGroup = difficultyButtonGroup,
				ToggleMode = true,
			};
			button.Pressed += () => { this.difficulty = difficulty; };
			container.AddChild(button);
			if (difficulty.Name == initiallySelectedDifficulty) {
				button.ButtonPressed = true;
				this.difficulty = difficulty;
			}
			container.CustomMinimumSize = new Vector2(843.0f / difficultyContainer.Columns, 0);
		}

		confirm.Pressed += CreateGame;
		cancel.Pressed += BackToMainMenu;
	}

	private void BackToMainMenu() {
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}

	private void DisplaySelectedLeader() {
		leaderHead.Texture = LeaderHeadTextures.Get(civilization);
		leaderHead.Scale = new Vector2(1.7f, 1.7f);
		leaderHead.SetPosition(new Vector2(414, 46));

		string traits = string.Join(", ", civilization.traits);
		civLabel.Text = $"{civilization.leader} of the {civilization.noun}\n({traits})";
	}

	private SaveGame GetSave() {
		string loadGamePath = GetNode<GlobalSingleton>("/root/GlobalSingleton").LoadGamePath;
		return SaveManager.LoadSave(loadGamePath,
									GamePaths.DefaultBicPath,
									(string scenarioSearchPath) => {
										// See corresponding logic in Game.cs
										Util.setModPath(scenarioSearchPath);
										log.Debug("RelativeModPath ", scenarioSearchPath);
										return Util.Civ3MediaPath("Text/PediaIcons.txt");
									});
	}

	// Whether a game is being created, so a second click on the confirm
	// button doesn't start a second one.
	private bool creatingGame = false;

	private void CreateGame() {
		if (creatingGame) {
			return;
		}
		SetCreatingGame(true);
		try {
			GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
			SaveGame save = GetSave();

			// Remembered for the next game, as on the player setup screen.
			// The civilization is the scenario's own, so isn't.
			NewGameChoices choices = NewGameChoices.Load();
			choices.Difficulty = difficulty.Name;
			choices.Save();

			// World generation can take a bit of time if multiple attempts are
			// needed, so we don't want to tie up the UI thread.
			Task.Run(() => {
				try {
					UpdateSaveAndStartGame(save, global);
				} catch (Exception e) {
					Callable.From(() => OnCreateGameFailed(e)).CallDeferred();
				}
			});
		} catch (Exception e) {
			OnCreateGameFailed(e);
		}
	}

	private void SetCreatingGame(bool creating) {
		creatingGame = creating;
		loadingLabel.Visible = creating;
		confirm.Disabled = creating;
		cancel.Disabled = creating;
	}

	private void OnCreateGameFailed(Exception e) {
		log.Error(e, "Couldn't create the game");
		if (!IsInstanceValid(this) || !IsInsideTree()) {
			return;
		}
		SetCreatingGame(false);
		Util.ShowErrorDialog(this, "Couldn't create the game", e.Message);
	}

	private void UpdateSaveAndStartGame(SaveGame save, GlobalSingleton global) {
		foreach (SavePlayer sp in save.Players) {
			sp.human = sp.civilization == this.civilization.name;
		}
		save.GameDifficulty = difficulty;
		// Under Regicide, civs the scenario gives no King get theirs.
		GameSetup.PlaceScenarioKings(save);

		log.Information("saving updated scenario");
		global.SaveGame = save;

		log.Information("opening map");
		Callable.From(StartGame).CallDeferred();
	}

	private void StartGame() {
		if (!IsInstanceValid(this) || !IsInsideTree()) {
			return;
		}
		try {
			LanSession.StartGame(GetTree());
		} catch (Exception e) {
			OnCreateGameFailed(e);
		}
	}
}

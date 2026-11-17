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
	// The difficulty buttons, by level's name.
	private readonly Dictionary<string, Civ3MenuButton> difficultyButtons = [];

	// The civilization picked and the player it makes the human (an
	// editor scenario's "Any" and "Random" players, ScenarioStart).
	private C7Engine.Editor.ScenarioStart.HumanChoice choice;
	private SaveGame shownSave;

	[Export] TextureButton confirm;
	[Export] TextureButton cancel;

	[Export] Label loadingLabel;

	// A scenario made in the editor: the victory conditions and rules it
	// allows, each a box the player may untick (ScenarioRules).
	private readonly List<(C7Engine.Editor.ScenarioRules.Toggle toggle, Civ3Checkbox box)> ruleBoxes = [];

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

		// Only the civilizations the scenario lets a player pick (its
		// players a human may play; for an editor scenario's "Any" player,
		// every civilization no other player has; for a "Random" one, one
		// drawn now).
		shownSave = save;
		List<C7Engine.Editor.ScenarioStart.HumanChoice> choices = C7Engine.Editor.ScenarioRules.IsEditorScenario(save)
			? C7Engine.Editor.ScenarioStart.HumanChoices(save, new Random())
			: save.Players.Where(p => p.canBePicked).Select(p => new C7Engine.Editor.ScenarioStart.HumanChoice(p.civilization, p.id.ToString())).ToList();
		string initiallySelectedCiv = choices.First().Civilization;

		foreach (C7Engine.Editor.ScenarioStart.HumanChoice option in choices) {
			Civilization civ = civilizations.Find(c => c.name == option.Civilization);

			Civ3MenuButton button = new() {
				Text = civ.name,
				FontSize = 12,
				textPosition = Civ3MenuButton.TextPosition.TextLeftOfIcon,
				ButtonGroup = playerListButtonGroup,
				ToggleMode = true,
			};
			button.Pressed += () => {
				this.civilization = civ;
				choice = option;
				DisplaySelectedLeader();
				LockDifficulty();
			};
			playerListContainer.AddChild(button);

			if (civ.name == initiallySelectedCiv && choice == null) {
				button.ButtonPressed = true;
				this.civilization = civ;
				choice = option;
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
			difficultyButtons[difficulty.Name] = button;
			if (difficulty.Name == initiallySelectedDifficulty) {
				button.ButtonPressed = true;
				this.difficulty = difficulty;
			}
			container.CustomMinimumSize = new Vector2(843.0f / difficultyContainer.Columns, 0);
		}

		AddScenarioInfo(save);
		LockDifficulty();

		confirm.Pressed += CreateGame;
		cancel.Pressed += BackToMainMenu;
	}

	// A scenario made in the editor shows its title and description (Civ3's
	// scenario intro), and what it allows as boxes to keep or untick.
	private void AddScenarioInfo(SaveGame save) {
		if (!C7Engine.Editor.ScenarioRules.IsEditorScenario(save)) {
			return;
		}
		// In the panel under the civilizations, empty on this screen.
		VBoxContainer box = new() { Name = "ScenarioInfo", Position = new Vector2(72, 506), Size = new Vector2(610, 0) };
		box.AddThemeConstantOverride("separation", 2);
		Label title = new() { Text = save.Scenario.title ?? "" };
		title.AddThemeFontSizeOverride("font_size", 17);
		box.AddChild(title);
		if (!string.IsNullOrWhiteSpace(save.Scenario.description)) {
			Label description = new() {
				Text = save.Scenario.description,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				CustomMinimumSize = new Vector2(610, 0),
			};
			description.AddThemeFontSizeOverride("font_size", 13);
			box.AddChild(description);
		}
		background.AddChild(box);

		List<C7Engine.Editor.ScenarioRules.Toggle> allowed = C7Engine.Editor.ScenarioRules.Allowed(save);
		if (allowed.Count == 0) {
			return;
		}
		VBoxContainer rules = new() { Name = "ScenarioRules", Position = new Vector2(740, 120), Size = new Vector2(220, 0) };
		rules.AddThemeConstantOverride("separation", 0);
		Label header = new() { Text = "GAME RULES" };
		header.AddThemeFontSizeOverride("font_size", 15);
		rules.AddChild(header);
		foreach (C7Engine.Editor.ScenarioRules.Toggle toggle in allowed) {
			Civ3Checkbox check = new() {
				Text = toggle.Name,
				FontSize = 13,
				ButtonPressed = true,
				TooltipText = "Allowed by the scenario; untick to play without it.",
			};
			rules.AddChild(check);
			ruleBoxes.Add((toggle, check));
		}
		background.AddChild(rules);
	}

	private void BackToMainMenu() {
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}

	private void DisplaySelectedLeader() {
		leaderHead.Texture = LeaderHeadTextures.Get(civilization);
		leaderHead.Scale = new Vector2(1.7f, 1.7f);
		leaderHead.SetPosition(new Vector2(414, 46));

		string traits = string.Join(", ", civilization.traits);
		// An editor scenario's player may have a leader of its own.
		string leader = shownSave?.Scenario?.players.Find(s => s.player == choice?.Player) is { civDefaults: false, leaderName: string own }
			? own : civilization.leader;
		civLabel.Text = $"{leader} of the {civilization.noun}\n({traits})";
	}

	// A difficulty the scenario sets for the chosen player is the only one
	// (Civ3's Player Properties); otherwise any.
	private void LockDifficulty() {
		string fixedLevel = choice == null || shownSave == null ? null : C7Engine.Editor.ScenarioStart.DifficultyFor(shownSave, choice.Player);
		foreach ((string name, Civ3MenuButton button) in difficultyButtons) {
			button.Disabled = fixedLevel != null && name != fixedLevel;
			if (name == fixedLevel) {
				button.ButtonPressed = true;
				difficulty = shownSave.Difficulties.Find(d => d.Name == name);
			}
		}
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
		// A scenario without Custom Rules plays the game's standard rules.
		C7Engine.Editor.RuleEditing.UseStandardRulesIfNotCustom(save, global.GameMode.GetSave());
		// The human's player and civilization, and the other "Any" and
		// "Random" players' (ScenarioStart).
		if (C7Engine.Editor.ScenarioRules.IsEditorScenario(save)) {
			C7Engine.Editor.ScenarioStart.AssignCivilizations(save, choice, new Random());
		} else {
			foreach (SavePlayer sp in save.Players) {
				sp.human = sp.civilization == this.civilization.name;
			}
		}
		save.GameDifficulty = difficulty;
		// What the player unticked of what the scenario allows.
		C7Engine.Editor.ScenarioRules.TurnOff(save, ruleBoxes.Where(r => !r.box.ButtonPressed).Select(r => r.toggle));
		// Under Regicide, civs the scenario gives no King get theirs.
		GameSetup.PlaceScenarioKings(save);
		// What a scenario made in the editor asks of the game as it starts.
		C7Engine.Editor.ScenarioStart.Apply(save);

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

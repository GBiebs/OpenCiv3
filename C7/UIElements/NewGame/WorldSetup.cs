using Godot;
using System;
using System.Linq;
using C7Engine;
using C7Engine.Lua;
using Serilog;
using C7GameData;
using C7GameData.Save;

[Tool]
public partial class WorldSetup : Control {
	private ILogger log = LogManager.ForContext<WorldSetup>();

	[Export] TextureRect background;

	[Export] Label pangaeaLabel;
	[Export] Label continentsLabel;
	[Export] Label archipelagoLabel;

	[Export] TextureButton pangaea60;
	[Export] TextureButton pangaea70;
	[Export] TextureButton pangaea80;

	[Export] TextureButton continents60;
	[Export] TextureButton continents70;
	[Export] TextureButton continents80;

	[Export] TextureButton archipelago60;
	[Export] TextureButton archipelago70;
	[Export] TextureButton archipelago80;

	[Export] TextureRect pangaea60Large;
	[Export] TextureRect pangaea70Large;
	[Export] TextureRect pangaea80Large;
	[Export] TextureRect continents60Large;
	[Export] TextureRect continents70Large;
	[Export] TextureRect continents80Large;
	[Export] TextureRect archipelago60Large;
	[Export] TextureRect archipelago70Large;
	[Export] TextureRect archipelago80Large;

	[Export] TextureButton arid;
	[Export] TextureButton normal;
	[Export] TextureButton wet;
	[Export] TextureButton cool;
	[Export] TextureButton temperate;
	[Export] TextureButton warm;
	[Export] TextureButton billion3;
	[Export] TextureButton billion4;
	[Export] TextureButton billion5;

	[Export] TextureRect aridLarge;
	[Export] TextureRect normalLarge;
	[Export] TextureRect wetLarge;
	[Export] TextureRect coolLarge;
	[Export] TextureRect temperateLarge;
	[Export] TextureRect warmLarge;
	[Export] TextureRect billion3Large;
	[Export] TextureRect billion4Large;
	[Export] TextureRect billion5Large;

	[Export] TextureButton confirm;
	[Export] TextureButton cancel;

	[Export] LineEdit seedInput;

	[Export] VBoxContainer worldSizeButtonsContainer;
	[Export] VBoxContainer barbActivityButtonsContainer;

	WorldCharacteristics.Landform landform = WorldCharacteristics.Landform.Pangaea;
	WorldCharacteristics.OceanCoverage ocean = WorldCharacteristics.OceanCoverage.Percent_70;
	WorldCharacteristics.Age age = WorldCharacteristics.Age.Billion_4;
	WorldCharacteristics.Temperature temp = WorldCharacteristics.Temperature.Temperate;
	WorldCharacteristics.Climate clim = WorldCharacteristics.Climate.Normal;

	private BarbarianActivity _barbarianActivity = BarbarianActivity.Roaming;
	// Whether the barbarians' activity or world size was left to chance, so
	// it's left to chance again next time.
	private bool _randomBarbarians = false;
	private bool _randomWorldSize = false;

	// The choices made when setting up the last game, which this screen
	// starts from.
	private NewGameChoices _choices = new();

	private WorldSize _worldSize = WorldSize.Generic();
	private SaveGame _saveGame;

	private int GameSeed => int.Parse(seedInput.Text);

	// Called when the node enters the scene tree for the first time.
	public override void _Ready() {
		background.Texture = TextureLoader.Load("world_setup.background");

		TextureLoader.SetButtonTextures(pangaea80, "world_setup.pangaea80");
		pangaea80.Pressed += () => {
			landform = WorldCharacteristics.Landform.Pangaea;
			ocean = WorldCharacteristics.OceanCoverage.Percent_80;
			ResetLandformGraphics();
			pangaea80Large.Visible = true;
			pangaeaLabel.Text = "Pangaea (80% water)";
		};

		TextureLoader.SetButtonTextures(pangaea70, "world_setup.pangaea70");
		pangaea70.Pressed += () => {
			landform = WorldCharacteristics.Landform.Pangaea;
			ocean = WorldCharacteristics.OceanCoverage.Percent_70;
			ResetLandformGraphics();
			pangaea70Large.Visible = true;
			pangaeaLabel.Text = "Pangaea (70% water)";
		};

		TextureLoader.SetButtonTextures(pangaea60, "world_setup.pangaea60");
		pangaea60.Pressed += () => {
			landform = WorldCharacteristics.Landform.Pangaea;
			ocean = WorldCharacteristics.OceanCoverage.Percent_60;
			ResetLandformGraphics();
			pangaea60Large.Visible = true;
			pangaeaLabel.Text = "Pangaea (60% water)";
		};

		TextureLoader.SetButtonTextures(continents80, "world_setup.continents80");
		continents80.Pressed += () => {
			landform = WorldCharacteristics.Landform.Continents;
			ocean = WorldCharacteristics.OceanCoverage.Percent_80;
			ResetLandformGraphics();
			continents80Large.Visible = true;
			continentsLabel.Text = "Continents (80% water)";
		};

		TextureLoader.SetButtonTextures(continents70, "world_setup.continents70");
		continents70.Pressed += () => {
			landform = WorldCharacteristics.Landform.Continents;
			ocean = WorldCharacteristics.OceanCoverage.Percent_70;
			ResetLandformGraphics();
			continents70Large.Visible = true;
			continentsLabel.Text = "Continents (70% water)";
		};

		TextureLoader.SetButtonTextures(continents60, "world_setup.continents60");
		continents60.Pressed += () => {
			landform = WorldCharacteristics.Landform.Continents;
			ocean = WorldCharacteristics.OceanCoverage.Percent_60;
			ResetLandformGraphics();
			continents60Large.Visible = true;
			continentsLabel.Text = "Continents (60% water)";
		};

		TextureLoader.SetButtonTextures(archipelago80, "world_setup.archipelago80");
		archipelago80.Pressed += () => {
			landform = WorldCharacteristics.Landform.Archipelago;
			ocean = WorldCharacteristics.OceanCoverage.Percent_80;
			ResetLandformGraphics();
			archipelago80Large.Visible = true;
			archipelagoLabel.Text = "Archipelago (80% water)";
		};

		TextureLoader.SetButtonTextures(archipelago70, "world_setup.archipelago70");
		archipelago70.Pressed += () => {
			landform = WorldCharacteristics.Landform.Archipelago;
			ocean = WorldCharacteristics.OceanCoverage.Percent_70;
			ResetLandformGraphics();
			archipelago70Large.Visible = true;
			archipelagoLabel.Text = "Archipelago (70% water)";
		};

		TextureLoader.SetButtonTextures(archipelago60, "world_setup.archipelago60");
		archipelago60.Pressed += () => {
			landform = WorldCharacteristics.Landform.Archipelago;
			ocean = WorldCharacteristics.OceanCoverage.Percent_60;
			ResetLandformGraphics();
			archipelago60Large.Visible = true;
			archipelagoLabel.Text = "Archipelago (60% water)";
		};

		TextureLoader.SetButtonTextures(arid, "world_setup.arid");
		arid.Pressed += () => {
			clim = WorldCharacteristics.Climate.Arid;
			ResetClimateGraphics();
			aridLarge.Visible = true;
		};

		TextureLoader.SetButtonTextures(normal, "world_setup.normal");
		normal.Pressed += () => {
			clim = WorldCharacteristics.Climate.Normal;
			ResetClimateGraphics();
			normalLarge.Visible = true;
		};

		TextureLoader.SetButtonTextures(wet, "world_setup.wet");
		wet.Pressed += () => {
			clim = WorldCharacteristics.Climate.Wet;
			ResetClimateGraphics();
			wetLarge.Visible = true;
		};

		TextureLoader.SetButtonTextures(warm, "world_setup.warm");
		warm.Pressed += () => {
			temp = WorldCharacteristics.Temperature.Warm;
			ResetTemperatureGraphics();
			warmLarge.Visible = true;
		};

		TextureLoader.SetButtonTextures(temperate, "world_setup.temperate");
		temperate.Pressed += () => {
			temp = WorldCharacteristics.Temperature.Temperate;
			ResetTemperatureGraphics();
			temperateLarge.Visible = true;
		};

		TextureLoader.SetButtonTextures(cool, "world_setup.cool");
		cool.Pressed += () => {
			temp = WorldCharacteristics.Temperature.Cool;
			ResetTemperatureGraphics();
			coolLarge.Visible = true;
		};

		TextureLoader.SetButtonTextures(billion3, "world_setup.billion3");
		billion3.Pressed += () => {
			age = WorldCharacteristics.Age.Billion_3;
			ResetAgeGraphics();
			billion3Large.Visible = true;
		};

		TextureLoader.SetButtonTextures(billion4, "world_setup.billion4");
		billion4.Pressed += () => {
			age = WorldCharacteristics.Age.Billion_4;
			ResetAgeGraphics();
			billion4Large.Visible = true;
		};

		TextureLoader.SetButtonTextures(billion5, "world_setup.billion5");
		billion5.Pressed += () => {
			age = WorldCharacteristics.Age.Billion_5;
			ResetAgeGraphics();
			billion5Large.Visible = true;
		};

		TextureLoader.SetButtonTextures(confirm, "ui.confirm");
		confirm.Pressed += CreateGame;

		TextureLoader.SetButtonTextures(cancel, "ui.cancel");
		cancel.Pressed += BackToMainMenu;

		pangaea60Large.Texture = TextureLoader.Load("world_setup.large.pangaea60");
		pangaea70Large.Texture = TextureLoader.Load("world_setup.large.pangaea70");
		pangaea80Large.Texture = TextureLoader.Load("world_setup.large.pangaea80");
		continents60Large.Texture = TextureLoader.Load("world_setup.large.continents60");
		continents70Large.Texture = TextureLoader.Load("world_setup.large.continents70");
		continents80Large.Texture = TextureLoader.Load("world_setup.large.continents80");
		archipelago60Large.Texture = TextureLoader.Load("world_setup.large.archipelago60");
		archipelago70Large.Texture = TextureLoader.Load("world_setup.large.archipelago70");
		archipelago80Large.Texture = TextureLoader.Load("world_setup.large.archipelago80");

		aridLarge.Texture = TextureLoader.Load("world_setup.large.arid");
		normalLarge.Texture = TextureLoader.Load("world_setup.large.normal");
		wetLarge.Texture = TextureLoader.Load("world_setup.large.wet");
		coolLarge.Texture = TextureLoader.Load("world_setup.large.cool");
		temperateLarge.Texture = TextureLoader.Load("world_setup.large.temperate");
		warmLarge.Texture = TextureLoader.Load("world_setup.large.warm");
		billion3Large.Texture = TextureLoader.Load("world_setup.large.billion3");
		billion4Large.Texture = TextureLoader.Load("world_setup.large.billion4");
		billion5Large.Texture = TextureLoader.Load("world_setup.large.billion5");

		// Start from the choices made for the last game.
		_choices = NewGameChoices.Load();
		Choose(LandformButton(_choices.Landform, _choices.OceanCoverage));
		Choose(_choices.Climate switch {
			WorldCharacteristics.Climate.Arid => arid,
			WorldCharacteristics.Climate.Wet => wet,
			_ => normal,
		});
		Choose(_choices.Temperature switch {
			WorldCharacteristics.Temperature.Cool => cool,
			WorldCharacteristics.Temperature.Warm => warm,
			_ => temperate,
		});
		Choose(_choices.Age switch {
			WorldCharacteristics.Age.Billion_3 => billion3,
			WorldCharacteristics.Age.Billion_5 => billion5,
			_ => billion4,
		});

		AddGameTypeSelector();

		InitBarbarianActivityOptions();
		InitMapSizes();
	}

	// The game types a new game may be, in the order they're listed.
	internal static readonly (GameScenario scenario, string name, string description)[] GameTypes = [
		(GameScenario.None, "Normal Game", "A standard game of Civilization III."),
		(GameScenario.EternalWar, "Eternal War",
			"A scenario: every civilization starts in the Middle Ages with cities scattered among its enemies', "
			+ "at war with all the others for good. No one can build Settlers, so cities change hands only by conquest. "
			+ "Played on a pangaea."),
		(GameScenario.RisingTide, "Rising Tide",
			"A scenario: the sea eats a pangaea a ring of shore at a time, the Tidal Waves coming ever faster, "
			+ "until no land is left. Every civilization starts near the edge; settlers found cities of size 5. "
			+ "The last civilization standing wins."),
		(GameScenario.KingsAndNomads, "Kings & Nomads",
			"A scenario: no one starts with a city, only a King and a wandering horde. Independent city-states, "
			+ "whose garrisons grow and who raise walls on turn 40, dot the map, and Settlers can only be built in conquered cities. Lose your King and your empire falls, "
			+ "its cities free again. The last King standing wins, or whoever holds half the world's cities. "
			+ "Played on continents or a pangaea."),
		(GameScenario.Mdj, "MDJ",
			"MPTournamentMDJ's rules, Medieval Japan's: its units, advances, clans and resources, on a generated map. "
			+ "Everyone is under Feudalism, which no one can leave, and a resource once had is kept for good, though it be cut off."),
	];

	// The rules a new game is played under: MDJ's (bundled, see
	// GameSetup.Mdj.cs) for MDJ and Rising Tide (owner's choice,
	// 2026-10-28), or else the usual ones. Loaded again only when that
	// changes.
	private bool mdjRulesLoaded;

	private void LoadRules(GameScenario scenario) {
		bool mdj = scenario is GameScenario.Mdj or GameScenario.RisingTide;
		if (_saveGame != null && mdj == mdjRulesLoaded) {
			return;
		}
		SaveGame rules = null;
		if (ScenarioPlay.RulesForNewGame != null) {
			// A scenario of rules alone, from Load Scenario.
			rules = ScenarioPlay.RulesForNewGame;
		} else if (mdj) {
			try {
				rules = GameSetup.LoadMdjRules(GamePaths.GameModesDir, GamePaths.GameMode);
			} catch (Exception e) {
				log.Error(e, "Could not load MPTournamentMDJ's rules; playing under the usual ones");
			}
		}
		mdjRulesLoaded = mdj && rules != null;
		_saveGame = rules ?? GameMode.Load(GamePaths.GameModesDir, GamePaths.GameMode).GetSave();
		// The player setup screen shows the civs' leaders, whose Civ3 art
		// MDJ keeps in the scenario's folders (the Sengoku conquest's; none
		// in standalone mode, see GameSetup.LoadMdjRules); the game sets the
		// path again as it starts (see Game.InitializeGame).
		Util.setModPath(_saveGame.ScenarioSearchPath);
		Util.ClearCaches();
		// The world sizes are the rules' own (MDJ's are by player count, 1v1
		// to 8 Player), so their buttons are made again.
		if (worldSizeButtonsContainer != null && worldSizeButtonsContainer.GetChildCount() > 0) {
			foreach (Node button in worldSizeButtonsContainer.GetChildren()) {
				worldSizeButtonsContainer.RemoveChild(button);
				button.QueueFree();
			}
			InitMapSizes();
		}
	}

	// The first choice for a new game: a normal game, or a scenario, which
	// may limit the choices below. A normal game unless one was picked
	// before coming back to this screen.
	private void AddGameTypeSelector() {
		GlobalSingleton global = GetNodeOrNull<GlobalSingleton>("/root/GlobalSingleton");
		// A scenario of rules alone (see ScenarioPlay) is the game type.
		if (ScenarioPlay.RulesForNewGame != null) {
			Label scenarioName = new() {
				Position = new Vector2(432, 17),
				CustomMinimumSize = new Vector2(160, 32),
				VerticalAlignment = VerticalAlignment.Center,
				Text = ScenarioPlay.Title,
				TooltipText = "The scenario being set up: its rules, on a world made for it.",
				MouseFilter = Control.MouseFilterEnum.Pass,
			};
			scenarioName.AddThemeFontSizeOverride("font_size", 15);
			scenarioName.AddThemeColorOverride("font_color", Colors.Black);
			background.AddChild(scenarioName);
			ChooseGameType(global, GameScenario.None);
			return;
		}
		OptionButton selector = new() {
			Position = new Vector2(432, 17),
			CustomMinimumSize = new Vector2(160, 32),
			TooltipText = "The kind of game to play.",
		};
		selector.AddThemeFontSizeOverride("font_size", 15);
		foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) {
			selector.AddThemeColorOverride(color, Colors.Black);
		}
		StyleBoxFlat style = new() {
			BorderColor = Color.Color8(120, 120, 120, 230),
			BgColor = Color.Color8(255, 255, 255, 140),
			BorderWidthBottom = 2,
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			BorderWidthTop = 2,
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
			ContentMarginTop = 2,
			ContentMarginBottom = 2,
		};
		selector.AddThemeStyleboxOverride("normal", style);
		selector.AddThemeStyleboxOverride("hover", style);
		selector.AddThemeStyleboxOverride("pressed", style);
		selector.GetPopup().SetTransparentBackground(false);
		for (int i = 0; i < GameTypes.Length; ++i) {
			selector.AddItem(GameTypes[i].name, i);
			selector.SetItemTooltip(i, GameTypes[i].description);
		}
		int chosen = Math.Max(0, Array.FindIndex(GameTypes, g => g.scenario == (global?.Scenario ?? GameScenario.None)));
		selector.Select(chosen);
		selector.ItemSelected += index => ChooseGameType(global, GameTypes[index].scenario);
		background.AddChild(selector);
		ChooseGameType(global, GameTypes[chosen].scenario);
	}

	private void ChooseGameType(GlobalSingleton global, GameScenario scenario) {
		if (global != null) {
			global.Scenario = scenario;
		}
		LoadRules(scenario);
		// Eternal War and Rising Tide are always played on a pangaea, and
		// Kings & Nomads on continents or a pangaea; only how much water is
		// left to choose.
		bool pangaeaOnly = scenario is GameScenario.EternalWar or GameScenario.RisingTide;
		bool noArchipelago = pangaeaOnly || scenario == GameScenario.KingsAndNomads;
		if (pangaeaOnly && landform != WorldCharacteristics.Landform.Pangaea) {
			Choose(LandformButton(WorldCharacteristics.Landform.Pangaea, ocean));
		} else if (noArchipelago && landform == WorldCharacteristics.Landform.Archipelago) {
			Choose(LandformButton(WorldCharacteristics.Landform.Continents, ocean));
		}
		foreach (TextureButton button in new[] { continents60, continents70, continents80 }) {
			button.Disabled = pangaeaOnly;
			button.Modulate = pangaeaOnly ? new Color(1, 1, 1, 0.35f) : Colors.White;
			button.TooltipText = pangaeaOnly ? "This scenario is played on a pangaea." : "";
		}
		foreach (TextureButton button in new[] { archipelago60, archipelago70, archipelago80 }) {
			button.Disabled = noArchipelago;
			button.Modulate = noArchipelago ? new Color(1, 1, 1, 0.35f) : Colors.White;
			button.TooltipText = pangaeaOnly ? "This scenario is played on a pangaea."
				: noArchipelago ? "This scenario is played on continents or a pangaea." : "";
		}
	}

	// Presses one of the toggle buttons, as if the player had clicked it.
	private static void Choose(BaseButton button) {
		button.ButtonPressed = true;
		button.EmitSignal(BaseButton.SignalName.Pressed);
	}

	private TextureButton LandformButton(WorldCharacteristics.Landform landform, WorldCharacteristics.OceanCoverage ocean) {
		return (landform, ocean) switch {
			(WorldCharacteristics.Landform.Pangaea, WorldCharacteristics.OceanCoverage.Percent_60) => pangaea60,
			(WorldCharacteristics.Landform.Pangaea, WorldCharacteristics.OceanCoverage.Percent_80) => pangaea80,
			(WorldCharacteristics.Landform.Pangaea, _) => pangaea70,
			(WorldCharacteristics.Landform.Continents, WorldCharacteristics.OceanCoverage.Percent_60) => continents60,
			(WorldCharacteristics.Landform.Continents, WorldCharacteristics.OceanCoverage.Percent_80) => continents80,
			(WorldCharacteristics.Landform.Continents, _) => continents70,
			(WorldCharacteristics.Landform.Archipelago, WorldCharacteristics.OceanCoverage.Percent_60) => archipelago60,
			(WorldCharacteristics.Landform.Archipelago, WorldCharacteristics.OceanCoverage.Percent_80) => archipelago80,
			(WorldCharacteristics.Landform.Archipelago, _) => archipelago70,
			_ => pangaea70,
		};
	}

	private void InitBarbarianActivityOptions() {
		var barbRandom = new Random();
		var barbOptions = Enum.GetValues<BarbarianActivity>().OrderBy(x => x).ToList();

		var barbActivityButtonGroup = new ButtonGroup() { ResourceName = "BarbActivityButtonGroup" };
		var randomSizeButton = new Civ3MenuButton
		{
			Text = "Random",
			textPosition = Civ3MenuButton.TextPosition.TextRightOfIcon,
			FontSize = 0,
			ButtonGroup = barbActivityButtonGroup,
			ToggleMode = true
		};
		randomSizeButton.Pressed += () => {
			_barbarianActivity = barbOptions[barbRandom.Next(barbOptions.Count)];
			_randomBarbarians = true;
		};

		// Dynamically create a new button for each barbarian activity option
		foreach (var ba in barbOptions) {
			var barbActivityButton = new Civ3MenuButton
			{
				Text = ba.ToString("G"),
				textPosition = Civ3MenuButton.TextPosition.TextRightOfIcon,
				FontSize = 0,
				ButtonGroup = barbActivityButtonGroup,
				ToggleMode = true,
			};
			barbActivityButton.Pressed += () => {
				_barbarianActivity = ba;
				_randomBarbarians = false;
			};
			barbActivityButtonsContainer.AddChild(barbActivityButton);
			if (ba == _choices.Barbarians) {
				Choose(barbActivityButton);
			}
		}

		// Append random as last in the list
		barbActivityButtonsContainer.AddChild(randomSizeButton);
		if (_choices.Barbarians == null) {
			Choose(randomSizeButton);
		}
	}

	private void InitMapSizes() {
		var sizeRandom = new Random();

		var worldSizeButtonGroup = new ButtonGroup() { ResourceName = "WorldSizeButtonGroup" };
		var randomSizeButton = new Civ3MenuButton
		{
			Text = "Random",
			textPosition = Civ3MenuButton.TextPosition.TextRightOfIcon,
			FontSize = 0,
			ButtonGroup = worldSizeButtonGroup,
			ToggleMode = true
		};

		randomSizeButton.Pressed += () => {
			var wss = _saveGame?.WorldSizes ?? [];
			_worldSize = wss.Count > 0 ? wss[sizeRandom.Next(wss.Count)] : WorldSize.Generic();
			_randomWorldSize = true;
		};

		try {
			// The size chosen last time, or the default if this ruleset
			// doesn't have it.
			bool chooseRandom = _choices.WorldSize == NewGameChoices.Random && _saveGame.WorldSizes.Count > 0;
			WorldSize initial = NewGameChoices.DefaultWorldSize(_saveGame.WorldSizes);
			if (!chooseRandom) {
				initial = _choices.ChosenWorldSize(_saveGame.WorldSizes, sizeRandom);
			}

			// Dynamically create a new button for each world size in the game
			foreach (var ws in _saveGame.WorldSizes) {
				var worldSizeButton = new Civ3MenuButton
				{
					Text = ws.name,
					textPosition = Civ3MenuButton.TextPosition.TextRightOfIcon,
					FontSize = 0,
					ButtonGroup = worldSizeButtonGroup,
					ToggleMode = true,
				};
				worldSizeButton.Pressed += () => {
					_worldSize = ws;
					_randomWorldSize = false;
				};
				worldSizeButtonsContainer.AddChild(worldSizeButton);

				if (!chooseRandom && ws == initial) {
					Choose(worldSizeButton);
				}
			}

			// Append random as last in the list
			worldSizeButtonsContainer.AddChild(randomSizeButton);
			if (chooseRandom) {
				Choose(randomSizeButton);
			}
		} catch (Exception ex) {
			log.Warning(ex, "Failed to load map sizes from game mode.");
		}
	}

	private void ResetLandformGraphics() {
		pangaea60Large.Visible = false;
		pangaea70Large.Visible = false;
		pangaea80Large.Visible = false;
		continents60Large.Visible = false;
		continents70Large.Visible = false;
		continents80Large.Visible = false;
		archipelago60Large.Visible = false;
		archipelago70Large.Visible = false;
		archipelago80Large.Visible = false;

		pangaeaLabel.Text = "Pangaea";
		continentsLabel.Text = "Continents";
		archipelagoLabel.Text = "Archipelago";
	}

	private void ResetClimateGraphics() {
		aridLarge.Visible = false;
		normalLarge.Visible = false;
		wetLarge.Visible = false;
	}

	private void ResetTemperatureGraphics() {
		coolLarge.Visible = false;
		temperateLarge.Visible = false;
		warmLarge.Visible = false;
	}

	private void ResetAgeGraphics() {
		billion3Large.Visible = false;
		billion4Large.Visible = false;
		billion5Large.Visible = false;
	}

	private void CreateGame() {
		GlobalSingleton Global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		Global.ResetLoadGameFields();

		// The size chosen, as these rules have it (see LoadRules).
		if (_worldSize != null && !_saveGame.WorldSizes.Contains(_worldSize)) {
			_worldSize = _saveGame.WorldSizes.Find(w => w.name == _worldSize.name) ?? _worldSize;
		}

		Global.WorldCharacteristics = new WorldCharacteristics(_saveGame) {
			landform = landform,
			oceanCoverage = ocean,
			age = age,
			climate = clim,
			temperature = temp,
			worldSize = _worldSize,
			barbarianActivity = _barbarianActivity,
			mapSeed = GameSeed,
		};

		Global.SaveGame = _saveGame;
		// The scenario's rules go with the game; a new game after it is on the
		// usual ones.
		ScenarioPlay.Clear();

		// Remembered once the game starts, with the choices made on the
		// player setup screen.
		_choices.WorldSize = _randomWorldSize ? NewGameChoices.Random : _worldSize.name;
		_choices.Barbarians = _randomBarbarians ? null : _barbarianActivity;
		_choices.Landform = landform;
		_choices.OceanCoverage = ocean;
		_choices.Climate = clim;
		_choices.Temperature = temp;
		_choices.Age = age;
		Global.NewGameChoices = _choices;

		GetTree().ChangeSceneToFile("res://UIElements/NewGame/player_setup.tscn");
	}

	private void BackToMainMenu() {
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}
}

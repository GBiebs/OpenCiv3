using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using C7Engine;
using C7Engine.Lua;
using C7GameData.Save;
using C7.UIElements;
using Serilog;

public partial class PlayerSetup : Control {
	private static ILogger log = LogManager.ForContext<PlayerSetup>();

	[Export] TextureRect background;

	[Export] GridContainer playerListContainer;
	ButtonGroup playerListButtonGroup = new();
	TextureRect leaderHead = new();
	[Export] Label civLabel;

	[Export] GridContainer opponentListContainer;
	List<OptionButton> opponentSelectors = new();
	// The items every opponent selector offers, and, parallel to
	// opponentSelectors, which of each selector's items are disabled, so
	// only the items that change need updating.
	List<string> opponentItemTexts = new();
	List<bool[]> opponentItemsDisabled = new();
	// Parallel to opponentSelectors: whether that slot is played by another
	// person sharing this computer (hotseat) rather than by the AI, and that
	// person's name.
	List<CheckBox> humanToggles = new();
	List<LineEdit> humanNames = new();
	// Parallel to opponentSelectors: which AI plays the slot (AiType, by
	// item index), on the line under it, where a human slot has its name.
	List<OptionButton> aiTypePickers = new();
	// Shown instead of the civilization for a human slot in a game hosted on
	// the LAN, whose player chooses in the lobby.
	List<Label> playersChoiceLabels = new();
	// Whether this game will be hosted on the LAN, where human slots are for
	// the people who join.
	readonly bool hostingOnLan = LanSession.HostNextGame;
	// The first player's name, shown once there is more than one human.
	LineEdit playerNameEdit;
	const int MaxPlayerNameLength = 24;

	// Teams (see GameSetup.Teams.cs): the player's, and parallel to
	// opponentSelectors, each slot's, numbered from 1 up to the number of
	// civs; unless the game is a free-for-all. Each starts on its default
	// team, until a team is picked by hand.
	OptionButton playerTeam;
	List<OptionButton> teamPickers = new();
	CheckBox freeForAll;
	int teamCount;
	int defaultTeamCount;
	bool teamsPicked = false;

	[Export] GridContainer rulesContainer;

	[Export] GridContainer difficultyContainer;

	ButtonGroup difficultyButtonGroup = new();

	CheckBox showScoreboard;
	CheckBox coreCitiesFreeOfCorruption;
	CheckBox acceleratedProduction;
	CheckBox turnLimit;
	Civ3HSlider corruptionRate;
	// The cities each civ starts with, in the Eternal War scenario only.
	Civ3HSlider eternalWarCities;
	// A checkbox for each victory condition the game may allow.
	CheckBox conquestVictory;
	CheckBox dominationVictory;
	CheckBox spaceRaceVictory;
	CheckBox diplomaticVictory;
	CheckBox culturalVictory;
	CheckBox wonderVictory;
	CheckBox victoryPointScoring;
	CheckBox elimination;
	CheckBox regicide;
	CheckBox massRegicide;
	CheckBox cultureFlips;
	CheckBox scientificLeaders;

	[Export] TextureButton confirm;
	[Export] TextureButton cancel;

	[Export] Label loadingLabel;

	SaveGame save;

	Civilization selectedCivilization;
	Difficulty selectedDifficulty;
	VictoryConditions victoryConditions;

	// The choices made when setting up the last game, which this screen
	// starts from, with those just made on the world setup screen.
	NewGameChoices choices;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready() {
		GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		save = global.SaveGame;
		choices = global.NewGameChoices ?? NewGameChoices.Load();

		// Set up buttons for the civs the player can play as.
		// The civilizations and Random (the barbarians aren't listed).
		playerListContainer.Columns = (int)Math.Ceiling(save.Civilizations.Count / 12.0);
		string initiallySelectedCiv = choices.ChosenCivilization(save.Civilizations).name;
		// A scenario's playable civilizations only (Civ3's Scenario tab).
		if (!C7Engine.Editor.ScenarioRules.IsPlayable(save, initiallySelectedCiv)) {
			initiallySelectedCiv = save.Civilizations.FirstOrDefault(c => !c.isBarbarian && C7Engine.Editor.ScenarioRules.IsPlayable(save, c.name))?.name;
		}
		foreach (Civilization civ in save.Civilizations) {
			if (civ.isBarbarian || !C7Engine.Editor.ScenarioRules.IsPlayable(save, civ.name)) {
				continue;
			}

			Civ3MenuButton button = new() {
				Text = civ.name,
				FontSize = 12,
				textPosition = Civ3MenuButton.TextPosition.TextLeftOfIcon,
				ButtonGroup = playerListButtonGroup,
				ToggleMode = true,
			};
			button.Pressed += () => {
				selectedCivilization = civ;
				UpdateOpponentSelectors();
				DisplaySelectedLeader();
			};
			playerListContainer.AddChild(button);

			if (civ.name == initiallySelectedCiv && !choices.ChoseRandomCivilization) {
				button.ButtonPressed = true;
				selectedCivilization = civ;
			}
		}
		// Last, as in Civ3: a civilization drawn at random when the game
		// starts, from those no one else has (selectedCivilization null).
		Civ3MenuButton randomButton = new() {
			Text = NewGameChoices.Random,
			FontSize = 12,
			textPosition = Civ3MenuButton.TextPosition.TextLeftOfIcon,
			ButtonGroup = playerListButtonGroup,
			ToggleMode = true,
			TooltipText = "Play a civilization drawn at random when the game starts.",
		};
		randomButton.Pressed += () => {
			selectedCivilization = null;
			UpdateOpponentSelectors();
			DisplaySelectedLeader();
		};
		playerListContainer.AddChild(randomButton);
		if (choices.ChoseRandomCivilization) {
			randomButton.ButtonPressed = true;
		}
		background.AddChild(leaderHead);
		DisplaySelectedLeader();

		// Below the leader's name and traits.
		playerNameEdit = new() {
			MaxLength = MaxPlayerNameLength,
			Visible = false,
			Position = new Vector2(421, 372),
			CustomMinimumSize = new Vector2(187, 0),
			TooltipText = "Your name, shown when the screen is handed to you.",
			Text = hostingOnLan ? "" : choices.PlayerName ?? "",
		};
		background.AddChild(playerNameEdit);

		// Set up the options for opponents.
		// Kings & Nomads has about a quarter fewer civs than usual (see
		// GameSetup.KingsAndNomadsCivs).
		int numberOfCivs = global.Scenario == GameScenario.KingsAndNomads
			? GameSetup.KingsAndNomadsCivs(global.WorldCharacteristics.worldSize.numberOfCivs)
			: global.WorldCharacteristics.worldSize.numberOfCivs;
		// A scenario's own map has room for as many civs as it has starts.
		if (ScenarioPlay.StartsOnMap(save) is int starts) {
			numberOfCivs = System.Math.Clamp(starts, 1, numberOfCivs);
		}
		// A scenario's number of players (Civ3's Scenario tab).
		if (save.Scenario?.numberOfPlayers > 0) {
			numberOfCivs = System.Math.Clamp(save.Scenario.numberOfPlayers, 1, numberOfCivs);
		}
		teamCount = numberOfCivs;
		defaultTeamCount = GameSetup.DefaultTeamCount(global.WorldCharacteristics.worldSize);
		AddTeamChoices();
		AddOpponentSelectors(numberOfCivs);
		ChooseOpponentsAsLastTime();
		ApplyDefaultTeams();
		ShowTeams();

		// Set up game rules
		AddRules();
		LimitToScenario();

		// Set up the difficulty buttons
		difficultyContainer.Columns = save.Difficulties.Count;
		string initiallySelectedDifficulty = choices.ChosenDifficulty(save.Difficulties,
			ScenarioPlay.RulesForNewGame != null ? NewGameChoices.ScenarioDifficulty(save) : null, save.Rules?.DefaultDifficulty).Name;
		foreach (Difficulty difficulty in save.Difficulties) {
			CenterContainer container = new();
			difficultyContainer.AddChild(container);

			Civ3MenuButton button = new(Civ3MenuButton.TextPosition.TextAboveIcon) {
				Text = difficulty.Name,
				ButtonGroup = difficultyButtonGroup,
				ToggleMode = true,
			};
			button.Pressed += () => { selectedDifficulty = difficulty; };
			container.AddChild(button);
			if (difficulty.Name == initiallySelectedDifficulty) {
				button.ButtonPressed = true;
				selectedDifficulty = difficulty;
			}
			container.CustomMinimumSize = new Vector2(843.0f / difficultyContainer.Columns, 0);
		}

		confirm.Pressed += CreateGame;
		cancel.Pressed += BackToMainMenu;
	}

	// A scenario made in the editor: what it doesn't allow can't be turned
	// on (Civ3's help: players "cannot enable any conditions that have been
	// disabled"), so its box is greyed out.
	private void LimitToScenario() {
		if (!C7Engine.Editor.ScenarioRules.IsEditorScenario(save)) {
			return;
		}
		HashSet<string> allowed = C7Engine.Editor.ScenarioRules.Allowed(save).Select(t => t.Name).ToHashSet();
		HashSet<string> limited = C7Engine.Editor.ScenarioRules.All.Select(t => t.Name).ToHashSet();
		foreach (Civ3Checkbox box in background.FindChildren("*", nameof(Civ3Checkbox), true, false).OfType<Civ3Checkbox>()) {
			if (limited.Contains(box.Text) && !allowed.Contains(box.Text)) {
				box.ButtonPressed = false;
				box.Disabled = true;
				box.TooltipText = "This scenario doesn't allow it.";
			}
		}
	}

	// A scenario of rules alone (see ScenarioPlay): its game rules and
	// victory conditions are where the boxes start, not the last game's
	// (a scenario such as QC plays with Accelerated Production on), and
	// what's chosen for it isn't remembered for the next normal game.
	private static bool PlayingScenario => ScenarioPlay.RulesForNewGame != null;

	private void AddRules() {
		// The victory types allowed last time (or by default), with the
		// thresholds (domination, culture) the scenario or ruleset sets; a
		// scenario's own.
		victoryConditions = PlayingScenario && save.VictoryConditions != null
			? save.VictoryConditions
			: choices.ChosenVictoryConditions(save.VictoryConditions);
		rulesContainer.Columns = 3;
		rulesContainer.AddThemeConstantOverride("v_separation", 0);
		rulesContainer.AddThemeConstantOverride("h_separation", 12);

		conquestVictory = AddVictoryCheckbox("Conquest victory", victoryConditions.AllowConquestVictory,
			"Win by eliminating every rival civilization.");
		dominationVictory = AddVictoryCheckbox("Domination victory", victoryConditions.AllowDominationVictory,
			$"Win by controlling {victoryConditions.DominationTerritoryPercent}% of the world's land "
			+ $"and {victoryConditions.DominationPopulationPercent}% of its population.");
		spaceRaceVictory = AddVictoryCheckbox("Space race victory", victoryConditions.AllowSpaceRaceVictory,
			"Win by being the first to build every part of the spaceship. Without it, the spaceship can't be built.");
		diplomaticVictory = AddVictoryCheckbox("Diplomatic victory", victoryConditions.AllowDiplomaticVictory,
			"Win by being elected Secretary General in a United Nations vote. Without it, the United Nations holds no votes.");
		culturalVictory = AddVictoryCheckbox("Cultural victory", victoryConditions.AllowCulturalVictory,
			$"Win when one of your cities has {victoryConditions.CultureOneCityWin:N0} culture, or your civilization has "
			+ $"{victoryConditions.CultureAllCitiesWin:N0} culture (on a standard map; less on smaller worlds, more on larger) "
			+ "and twice as much as any rival.");

		wonderVictory = AddVictoryCheckbox("Wonder victory", victoryConditions.AllowWonderVictory,
			"The game ends as soon as every great wonder has been built, and the civilization with the most victory points wins.");
		victoryPointScoring = AddVictoryCheckbox("Victory Point Scoring", victoryConditions.VictoryLocations,
			$"Win by being the first to {victoryConditions.VictoryPointLimit:N0} victory points, earned by building wonders, "
			+ "defeating enemy units, discovering advances, conquering cities and holding Victory Locations (each civilization's "
			+ "starting location) with a defending unit. At the turn limit, the most victory points wins.");

		// Civ3's instant elimination rules, named as on its setup screen.
		elimination = AddVictoryCheckbox("Elimination", victoryConditions.CityElimination,
			victoryConditions.CityEliminationCount == 1
				? "A civilization that loses a city to an enemy is eliminated: its units and cities are destroyed."
				: $"A civilization that loses {victoryConditions.CityEliminationCount} cities to enemies is eliminated: its units and cities are destroyed.");
		// Only rules with King units can play Regicide.
		bool hasKings = save.UnitPrototypes.Any(p => p.flags.Contains(SaveUnitPrototype.Flag.King));
		regicide = AddVictoryCheckbox("Regicide", hasKings && victoryConditions.Regicide,
			"Each civilization has a King. A civilization whose King is killed is eliminated: its units and cities are destroyed.");
		massRegicide = AddVictoryCheckbox("Mass Regicide", hasKings && victoryConditions.MassRegicide,
			$"Each civilization has {InstantElimination.MassRegicideKings} Kings. A civilization whose Kings are all killed is eliminated: its units and cities are destroyed.");
		// Regicide and Mass Regicide aren't exclusive: Civ3's setup screen
		// has a checkbox for each (Conquests' labels.txt), and with both a
		// civ starts with Mass Regicide's Kings but is eliminated when any
		// one of them dies; "For an epic game, check the option of 'Mass
		// Regicide' (but do NOT check 'Regicide' as well!)"
		// (https://forums.civfanatics.com/threads/mass-regicide.76473/).
		// The game plays them that way (see InstantElimination and
		// RegicideCondition).
		regicide.Visible = hasKings;
		massRegicide.Visible = hasKings;

		turnLimit = new Civ3Checkbox {
			Text = $"Turn limit ({save.TimeOptions.turnLimit} turns)",
			FontSize = 14,
			ButtonPressed = victoryConditions.UseTurnLimit,
			TooltipText = $"End the game after turn {save.TimeOptions.turnLimit}, scoring who leads. Without it, the game goes on until someone wins.",
		};
		rulesContainer.AddChild(turnLimit);

		showScoreboard = new Civ3Checkbox {
			Text = "Show scoreboard",
			FontSize = 14,
			ButtonPressed = PlayingScenario ? save.Rules.ShowScoreboard : choices.ShowScoreboard ?? true,
			TooltipText = "With more than one human player, show everyone's score and how long the current turn has taken.",
		};
		rulesContainer.AddChild(showScoreboard);

		// The corruption rules, custom ones, have the box on the right to
		// themselves: with the victory conditions Civ3's rules box had
		// grown to seven rows, which ran out of it over the difficulty
		// levels below.
		VBoxContainer corruptionBox = new() {
			Position = new Vector2(782, 506),
			Size = new Vector2(170, 122),
		};
		corruptionBox.AddThemeConstantOverride("separation", 4);
		background.AddChild(corruptionBox);
		Label corruptionHeader = new() {
			Text = "CORRUPTION",
			Position = new Vector2(784, 468),
			Size = new Vector2(150, 29),
			VerticalAlignment = VerticalAlignment.Center,
		};
		corruptionHeader.AddThemeFontSizeOverride("font_size", 15);
		background.AddChild(corruptionHeader);

		coreCitiesFreeOfCorruption = new Civ3Checkbox {
			Text = "No corruption near capital",
			FontSize = 12,
			// Off unless it was chosen for the last game, like the rule itself.
			ButtonPressed = PlayingScenario ? save.Rules.CoreCitiesFreeOfCorruption : choices.CoreCitiesFreeOfCorruption ?? false,
			TooltipText = "Custom rule: the capital and the 5 cities nearest it have no corruption or waste.",
		};
		corruptionBox.AddChild(coreCitiesFreeOfCorruption);

		acceleratedProduction = new Civ3Checkbox {
			Text = "Accelerated production",
			FontSize = 14,
			// As chosen last time, or as the scenario or ruleset sets it,
			// like in Civ3. Rising Tide always starts with it on (owner's
			// ruling, 2026-10-24).
			ButtonPressed = GetNode<GlobalSingleton>("/root/GlobalSingleton").Scenario == GameScenario.RisingTide
				|| (PlayingScenario ? save.Rules.AcceleratedProduction : choices.AcceleratedProduction ?? save.Rules.AcceleratedProduction),
			TooltipText = "Cities need half the food to grow and half the shields to build, as in Civ3.",
		};
		rulesContainer.AddChild(acceleratedProduction);

		cultureFlips = new Civ3Checkbox {
			Text = "Culture flipping",
			FontSize = 14,
			// As chosen last time, or as the scenario or ruleset sets it,
			// like in Civ3.
			ButtonPressed = PlayingScenario ? save.Rules.AllowCultureFlips : choices.CultureFlips ?? save.Rules.AllowCultureFlips,
			TooltipText = "Cities may defect to a neighboring civilization whose culture outweighs their owner's. A garrison makes it less likely.",
		};
		rulesContainer.AddChild(cultureFlips);

		scientificLeaders = new Civ3Checkbox {
			Text = "Scientific leaders",
			FontSize = 14,
			// As chosen last time, or as the scenario or ruleset sets it,
			// like in Civ3.
			ButtonPressed = PlayingScenario ? save.Rules.ScientificLeaders : choices.ScientificLeaders ?? save.Rules.ScientificLeaders,
			TooltipText = "The first civilization to discover an advance may gain a scientific leader (3% chance, 5% for scientific civilizations), who can complete anything a city is building, wonders included.",
		};
		rulesContainer.AddChild(scientificLeaders);

		AddCorruptionRate(corruptionBox);

		// Eternal War has no corruption, so its own setting takes the
		// corruption box.
		GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		if (global.Scenario == GameScenario.EternalWar) {
			corruptionHeader.Text = "ETERNAL WAR";
			foreach (Node child in corruptionBox.GetChildren()) {
				if (child is Control control) {
					control.Visible = false;
				}
			}
			AddEternalWarCities(corruptionBox, global.WorldCharacteristics.worldSize);
		}
	}

	// A slider for the cities each civ starts with in Eternal War, starting
	// at the default for the world size.
	private void AddEternalWarCities(Container box, WorldSize worldSize) {
		const string tooltip = "The cities each civilization starts with, scattered at random among its enemies'. "
			+ "The default grows with the world: 2 on a tiny world up to 10 on a huge one.";
		VBoxContainer row = new() { TooltipText = tooltip };
		row.AddThemeConstantOverride("separation", 2);
		Label label = new() { TooltipText = tooltip, MouseFilter = MouseFilterEnum.Pass };
		label.AddThemeFontSizeOverride("font_size", 13);
		eternalWarCities = new Civ3HSlider {
			MinValue = GameSetup.MinEternalWarCitiesPerCiv,
			MaxValue = GameSetup.MaxEternalWarCitiesPerCiv,
			Step = 1,
			Value = GameSetup.DefaultEternalWarCitiesPerCiv(save.WorldSizes, worldSize),
			CustomMinimumSize = new Vector2(140, 20),
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			TooltipText = tooltip,
		};
		void ShowCities(double cities) {
			label.Text = $"Cities per civ: {(int)cities}";
		}
		ShowCities(eternalWarCities.Value);
		eternalWarCities.ValueChanged += ShowCities;
		row.AddChild(label);
		row.AddChild(eternalWarCities);
		box.AddChild(row);
	}

	private CheckBox AddVictoryCheckbox(string text, bool allowed, string tooltip) {
		Civ3Checkbox checkbox = new() {
			Text = text,
			FontSize = 14,
			ButtonPressed = allowed,
			TooltipText = tooltip,
		};
		rulesContainer.AddChild(checkbox);
		return checkbox;
	}

	// A slider for the rate corruption and waste are scaled by
	// (Rules.CorruptionRate), starting at the rate chosen last time or the
	// one the ruleset gives.
	private void AddCorruptionRate(Container box) {
		const string tooltip = "Scales every city's corruption and waste. 1.00 is Civ3's rate; "
			+ "the default, 0.90, goes slightly easier on everyone.";
		// The label above the slider, to fit the box.
		VBoxContainer row = new() { TooltipText = tooltip };
		row.AddThemeConstantOverride("separation", 2);
		Label label = new() { TooltipText = tooltip, MouseFilter = MouseFilterEnum.Pass };
		label.AddThemeFontSizeOverride("font_size", 13);
		corruptionRate = new Civ3HSlider {
			MinValue = Rules.MinCorruptionRate,
			MaxValue = Rules.MaxCorruptionRate,
			Step = 0.05,
			Value = PlayingScenario ? save.Rules.CorruptionRate : choices.CorruptionRate ?? save.Rules.CorruptionRate,
			CustomMinimumSize = new Vector2(140, 20),
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			TooltipText = tooltip,
		};
		void ShowRate(double rate) {
			label.Text = $"Corruption rate {rate.ToString("0.00", CultureInfo.InvariantCulture)}";
		}
		ShowRate(corruptionRate.Value);
		corruptionRate.ValueChanged += ShowRate;
		row.AddChild(label);
		row.AddChild(corruptionRate);
		box.AddChild(row);
	}

	// The player's team, in the lower box under the leader's name, and the
	// free-for-all box at the end of the rivals' heading.
	private void AddTeamChoices() {
		playerTeam = MakeTeamPicker("Your team. Teammates are allies for good, and win together.");
		playerTeam.Position = new Vector2(461, 421);
		playerTeam.CustomMinimumSize = new Vector2(107, 24);
		playerTeam.AddThemeFontSizeOverride("font_size", 13);
		background.AddChild(playerTeam);
		freeForAll = new Civ3Checkbox {
			Text = "Free-for-all",
			FontSize = 13,
			// Every game type starts as a free-for-all (owner's ruling,
			// 2026-10-24).
			ButtonPressed = true,
			Position = new Vector2(845, 76),
			TooltipText = "No teams: every civilization plays for itself.",
		};
		freeForAll.Toggled += _ => ShowTeams();
		background.AddChild(freeForAll);
	}

	private OptionButton MakeTeamPicker(string tooltip) {
		OptionButton picker = new() { TooltipText = tooltip, FitToLongestItem = true };
		for (int team = 1; team <= teamCount; ++team) {
			picker.AddItem(GameSetup.TeamName(team), team);
		}
		picker.ItemSelected += _ => teamsPicked = true;
		return picker;
	}

	private void ShowTeams() {
		bool teams = !freeForAll.ButtonPressed;
		playerTeam.Visible = teams;
		foreach (OptionButton picker in teamPickers) {
			picker.Visible = teams;
		}
	}

	// Puts every slot on its default team, unless a team has been picked by
	// hand: the humans dealt out first, in turn order, then the computer
	// players (see GameSetup.DefaultTeam).
	private void ApplyDefaultTeams() {
		if (teamsPicked) {
			return;
		}
		List<int> humans = Enumerable.Range(0, humanToggles.Count).Where(i => humanToggles[i].ButtonPressed).ToList();
		List<int> computers = Enumerable.Range(0, humanToggles.Count).Where(i => !humanToggles[i].ButtonPressed).ToList();
		playerTeam.Select(GameSetup.DefaultTeam(0, defaultTeamCount) - 1);
		int place = 1;
		foreach (int slot in humans.Concat(computers)) {
			teamPickers[slot].Select(GameSetup.DefaultTeam(place++, defaultTeamCount) - 1);
		}
	}

	// The team picked on the picker, or 0 in a free-for-all.
	private int TeamOn(OptionButton picker) {
		return freeForAll.ButtonPressed ? 0 : picker.GetSelectedId();
	}

	private void BackToMainMenu() {
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}

	private void AddOpponentSelectors(int numberOfCivs) {
		int numOpponents = numberOfCivs - 1;

		for (int i = 0; i < numOpponents; ++i) {
			OptionButton optionButton = new();
			StyleOpponentDropdown(optionButton);
			PopupMenu popup = optionButton.GetPopup();
			opponentSelectors.Add(optionButton);

			CheckBox humanToggle = new() {
				Text = "Human",
				TooltipText = hostingOnLan
					? "Played by someone joining over the network, who chooses their own civilization in the lobby."
					: "Played by another person on this computer, taking turns (hotseat).",
			};
			humanToggles.Add(humanToggle);

			Label playersChoice = new() {
				Text = "Player's choice",
				Visible = false,
				VerticalAlignment = VerticalAlignment.Center,
				TooltipText = "Whoever takes this seat chooses their civilization in the LAN lobby.",
				MouseFilter = MouseFilterEnum.Pass,
			};
			playersChoiceLabels.Add(playersChoice);
			if (hostingOnLan) {
				humanToggle.Toggled += (bool on) => {
					// The slot doesn't hold a civilization for the AI any more.
					optionButton.Select(optionButton.ItemCount - 1);
					optionButton.Visible = !on;
					playersChoice.Visible = on;
					UpdateOpponentSelectors();
				};
			}

			LineEdit humanName = new() {
				MaxLength = MaxPlayerNameLength,
				Visible = false,
				TooltipText = "This player's name, shown when the screen is handed to them.",
			};
			humanNames.Add(humanName);
			humanToggle.Toggled += (bool _) => UpdateHumanNameFields();
			Control aiTypeRow = MakeAiTypePicker();
			// A person plays a human slot, so it has no AI to choose.
			humanToggle.Toggled += (bool on) => aiTypeRow.Visible = !on;
			humanToggle.Toggled += (bool _) => ApplyDefaultTeams();
			OptionButton teamPicker = MakeTeamPicker("This civilization's team. Teammates are allies for good, and win together.");
			teamPickers.Add(teamPicker);

			CenterContainer container = new();
			opponentListContainer.AddChild(container);
			VBoxContainer slot = new();
			container.AddChild(slot);
			HBoxContainer row = new();
			slot.AddChild(row);
			row.AddChild(optionButton);
			row.AddChild(playersChoice);
			row.AddChild(teamPicker);
			row.AddChild(humanToggle);
			slot.AddChild(humanName);
			slot.AddChild(aiTypeRow);

			const float humanToggleWidth = 80.0f;
			const float teamPickerWidth = 80.0f;
			container.CustomMinimumSize = new Vector2(312.0f / opponentListContainer.Columns, 315.0f / numOpponents);
			optionButton.CustomMinimumSize = new Vector2(290.0f / opponentListContainer.Columns - humanToggleWidth - teamPickerWidth, optionButton.CustomMinimumSize.Y);
			playersChoice.CustomMinimumSize = optionButton.CustomMinimumSize;

			foreach (Civilization civ in save.Civilizations) {
				if (civ.isBarbarian) {
					continue;
				}
				optionButton.AddItem(civ.name);
			}

			// Add (and default to) random for each opponent.
			optionButton.AddItem("Random");
			optionButton.Select(popup.ItemCount - 1);

			for (int k = 0; k < popup.ItemCount; ++k) {
				popup.SetItemAsRadioCheckable(k, false);
				popup.SetItemAsCheckable(k, false);
			}

			if (opponentItemTexts.Count == 0) {
				for (int k = 0; k < optionButton.ItemCount; ++k) {
					opponentItemTexts.Add(optionButton.GetItemText(k));
				}
			}
			opponentItemsDisabled.Add(new bool[optionButton.ItemCount]);

			optionButton.ItemSelected += (long i) => { UpdateOpponentSelectors(); };
		}

		UpdateOpponentSelectors();
	}

	// The rivals' dropdowns: see-through, with a grey border.
	private static void StyleOpponentDropdown(OptionButton dropdown) {
		StyleBoxFlat styleBox = new() {
			BorderColor = Color.Color8(150, 150, 150, 220),
			BgColor = Color.Color8(255, 255, 255, 0),
			BorderWidthBottom = 2,
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			BorderWidthTop = 2,
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
			ContentMarginTop = 4,
			ContentMarginBottom = 4
		};
		dropdown.AddThemeStyleboxOverride("normal", styleBox);
		dropdown.AddThemeStyleboxOverride("hover", styleBox);
		dropdown.AddThemeStyleboxOverride("pressed", styleBox);

		PopupMenu popup = dropdown.GetPopup();
		// The menu is drawn with the theme's panel (C7Theme.tres), opaque and
		// readable, not the button's see-through box.
		popup.MaxSize = new Vector2I(popup.MaxSize.X, 300);
		popup.SetTransparentBackground(false);
	}

	// The line under a computer slot choosing the AI that plays it, its
	// items in AiType's order.
	private Control MakeAiTypePicker() {
		const string tooltip = "Which AI plays this civilization: Standard, the usual one, or Advanced.";
		HBoxContainer row = new() { TooltipText = tooltip };
		Label label = new() {
			Text = "AI:",
			VerticalAlignment = VerticalAlignment.Center,
			TooltipText = tooltip,
			MouseFilter = MouseFilterEnum.Pass,
		};
		label.AddThemeFontSizeOverride("font_size", 13);
		row.AddChild(label);
		OptionButton picker = new() { TooltipText = tooltip, FitToLongestItem = true };
		StyleOpponentDropdown(picker);
		picker.AddThemeFontSizeOverride("font_size", 13);
		picker.AddItem("Standard", (int)AiType.Standard);
		picker.AddItem("Advanced", (int)AiType.Advanced);
		for (int k = 0; k < picker.ItemCount; ++k) {
			picker.GetPopup().SetItemAsRadioCheckable(k, false);
		}
		picker.Select(0);
		row.AddChild(picker);
		aiTypePickers.Add(picker);
		return row;
	}

	private AiType AiTypeOf(int slot) {
		return (AiType)aiTypePickers[slot].GetSelectedId();
	}

	// Sets each rival's slot as it was set up last time: the civilization
	// chosen for it (if this ruleset has it and nobody else has it), the AI
	// that plays it, and whether a person plays it, hotseat or over the LAN.
	private void ChooseOpponentsAsLastTime() {
		List<string> civs = choices.ChosenOpponents(opponentSelectors.Count, save.Civilizations, selectedCivilization?.name);
		for (int i = 0; i < opponentSelectors.Count; ++i) {
			int item = civs[i] == null ? -1 : opponentItemTexts.IndexOf(civs[i]);
			if (item >= 0) {
				opponentSelectors[i].Select(item);
			}
			aiTypePickers[i].Select(aiTypePickers[i].GetItemIndex((int)choices.AiTypeOf(i)));
			if (hostingOnLan) {
				humanToggles[i].ButtonPressed = choices.LanSlots.Contains(i);
			} else if (choices.HotseatSlots.TryGetValue(i, out string name)) {
				humanNames[i].Text = name;
				humanToggles[i].ButtonPressed = true;
			}
		}
		UpdateOpponentSelectors();
	}

	// Shows a name field for every human player once there is more than one,
	// with "Player N" (in turn order) as the default name. Players on the LAN
	// go by the names they join with instead.
	private void UpdateHumanNameFields() {
		if (hostingOnLan) {
			return;
		}
		int playerNumber = 1;
		playerNameEdit.PlaceholderText = $"Player {playerNumber}";
		for (int i = 0; i < humanToggles.Count; ++i) {
			humanNames[i].Visible = humanToggles[i].ButtonPressed;
			if (humanToggles[i].ButtonPressed) {
				humanNames[i].PlaceholderText = $"Player {++playerNumber}";
			}
		}
		playerNameEdit.Visible = playerNumber > 1;
	}

	private static string PlayerName(LineEdit nameEdit) {
		string name = nameEdit.Text.Trim();
		return name != "" ? name : nameEdit.PlaceholderText;
	}

	private void UpdateOpponentSelectors() {
		HashSet<string> civsTaken = new();
		if (selectedCivilization != null) {
			civsTaken.Add(selectedCivilization.name);
		}

		for (int s = 0; s < opponentSelectors.Count; ++s) {
			OptionButton ob = opponentSelectors[s];
			bool[] disabled = opponentItemsDisabled[s];
			int selected = ob.Selected;
			string selection = selected >= 0 && selected < opponentItemTexts.Count ? opponentItemTexts[selected] : ob.GetItemText(selected);

			// If the player decides to play as civ X and one of the opponent
			// selectors has X selected, change it to random. Similarly if one
			// of the previous option buttons has selected this civ.
			if (civsTaken.Contains(selection)) {
				ob.Select(disabled.Length - 1);
			} else if (selection != "Random") {
				civsTaken.Add(selection);
			}

			// Only touch the items whose state changed.
			for (int i = 0; i < disabled.Length; ++i) {
				bool disable = civsTaken.Contains(opponentItemTexts[i]);
				if (disable != disabled[i]) {
					ob.SetItemDisabled(i, disable);
					disabled[i] = disable;
				}
			}
		}
	}

	private void DisplaySelectedLeader() {
		Civilization civilization = selectedCivilization;
		if (civilization == null) {
			leaderHead.Texture = null;
			civLabel.Text = "A random civilization";
			return;
		}

		leaderHead.Texture = LeaderHeadTextures.Get(civilization);
		leaderHead.Scale = new Vector2(1.7f, 1.7f);
		leaderHead.SetPosition(new Vector2(414, 46));

		string traits = string.Join(", ", civilization.traits);
		civLabel.Text = $"{civilization.leader} of the {civilization.noun}\n({traits})";
	}

	private List<SelectedOpponent> CollectSelectedOpponents() {
		List<SelectedOpponent> opponents = [];

		for (int i = 0; i < opponentSelectors.Count; ++i) {
			if (humanToggles[i].ButtonPressed) {
				continue;
			}

			OptionButton ob = opponentSelectors[i];
			string selectedName = ob.GetItemText(ob.Selected);

			int team = TeamOn(teamPickers[i]);
			AiType aiType = AiTypeOf(i);
			if (selectedName == "Random") {
				opponents.Add(new SelectedOpponent { isRandom = true, team = team, aiType = aiType });
			} else {
				opponents.Add(new SelectedOpponent { isRandom = false, Name = selectedName, team = team, aiType = aiType });
			}
		}

		return opponents;
	}

	// The additional human players. A human slot left on "Random" gets a
	// random civilization nobody else has picked.
	private List<HotseatPlayer> CollectHotseatPlayers(Civilization player) {
		List<Civilization> playable = save.Civilizations.Where(c => !c.isBarbarian).ToList();
		HashSet<string> taken = [player.name];
		foreach (OptionButton ob in opponentSelectors) {
			taken.Add(ob.GetItemText(ob.Selected));
		}

		List<HotseatPlayer> players = [];
		Random rand = new();
		for (int i = 0; i < opponentSelectors.Count; ++i) {
			if (!humanToggles[i].ButtonPressed) {
				continue;
			}

			string selectedName = opponentSelectors[i].GetItemText(opponentSelectors[i].Selected);
			Civilization civ = playable.Find(c => c.name == selectedName);
			if (civ == null) {
				List<Civilization> available = playable.Where(c => !taken.Contains(c.name)).ToList();
				civ = available[rand.Next(available.Count)];
				taken.Add(civ.name);
			}
			players.Add(new HotseatPlayer { civilization = civ, name = PlayerName(humanNames[i]), team = TeamOn(teamPickers[i]) });
		}
		return players;
	}

	// The civilization chosen, or for Random one drawn now from those no
	// rival slot has.
	private Civilization PlayerCivilization() {
		return selectedCivilization ?? NewGameChoices.RandomCivilization(
			save.Civilizations.Where(c => C7Engine.Editor.ScenarioRules.IsPlayable(save, c.name)).ToList(),
			opponentSelectors.Select(ob => ob.GetItemText(ob.Selected)).Where(n => n != NewGameChoices.Random), new Random());
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
			StartCreatingGame();
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

	private void StartCreatingGame() {

		GlobalSingleton global = GetNode<GlobalSingleton>("/root/GlobalSingleton");

		victoryConditions.UseTurnLimit = turnLimit.ButtonPressed;
		victoryConditions.AllowConquestVictory = conquestVictory.ButtonPressed;
		victoryConditions.AllowDominationVictory = dominationVictory.ButtonPressed;
		victoryConditions.AllowSpaceRaceVictory = spaceRaceVictory.ButtonPressed;
		victoryConditions.AllowDiplomaticVictory = diplomaticVictory.ButtonPressed;
		victoryConditions.AllowCulturalVictory = culturalVictory.ButtonPressed;
		victoryConditions.AllowWonderVictory = wonderVictory.ButtonPressed;
		victoryConditions.VictoryLocations = victoryPointScoring.ButtonPressed;
		victoryConditions.CityElimination = elimination.ButtonPressed;
		victoryConditions.Regicide = regicide.ButtonPressed;
		victoryConditions.MassRegicide = massRegicide.ButtonPressed;

		int guestSeats = humanToggles.Count(t => t.ButtonPressed);
		if (hostingOnLan && guestSeats > 0) {
			// The guests choose their civilizations in the lobby, and the
			// game is created once the host starts it.
			GameSetup lanSetup = new() {
				playerCivilization = PlayerCivilization(),
				playerName = LanSession.PlayerName,
				playerTeam = TeamOn(playerTeam),
				difficulty = selectedDifficulty,
				worldCharacteristics = global.WorldCharacteristics,
				opponents = CollectSelectedOpponents(),
				victoryConditions = victoryConditions,
				showScoreboard = showScoreboard.ButtonPressed,
				coreCitiesFreeOfCorruption = coreCitiesFreeOfCorruption.ButtonPressed,
				acceleratedProduction = acceleratedProduction.ButtonPressed,
				allowCultureFlips = cultureFlips.ButtonPressed,
				scientificLeaders = scientificLeaders.ButtonPressed,
				corruptionRate = (float)corruptionRate.Value,
				invalidMapsDirectory = InvalidMaps.FolderIfEnabled,
				scenario = global.Scenario,
				eternalWarCitiesPerCiv = eternalWarCities == null ? null : (int)eternalWarCities.Value,
			};
			PersistGameSettings(lanSetup);
			// The guests start on the teams of their slots, and may change
			// them in the lobby.
			List<int> guestTeams = Enumerable.Range(0, humanToggles.Count).Where(i => humanToggles[i].ButtonPressed)
				.Select(i => TeamOn(teamPickers[i])).ToList();
			LanSession.PendingGame = new PendingLanGame(lanSetup, save, guestSeats, freeForAll.ButtonPressed ? 0 : teamCount, guestTeams);
			StartGame();
			return;
		}

		Civilization player = PlayerCivilization();
		List<HotseatPlayer> hotseatPlayers = CollectHotseatPlayers(player);
		GameSetup gameSetup = new() {
			playerCivilization = player,
			// Names only matter when the screen is handed between players.
			playerName = hotseatPlayers.Count > 0 ? PlayerName(playerNameEdit) : null,
			playerTeam = TeamOn(playerTeam),
			hotseatPlayers = hotseatPlayers,
			difficulty = selectedDifficulty,
			worldCharacteristics = global.WorldCharacteristics,
			opponents = CollectSelectedOpponents(),
			victoryConditions = victoryConditions,
			showScoreboard = showScoreboard.ButtonPressed,
			coreCitiesFreeOfCorruption = coreCitiesFreeOfCorruption.ButtonPressed,
			acceleratedProduction = acceleratedProduction.ButtonPressed,
			allowCultureFlips = cultureFlips.ButtonPressed,
			scientificLeaders = scientificLeaders.ButtonPressed,
			corruptionRate = (float)corruptionRate.Value,
			invalidMapsDirectory = InvalidMaps.FolderIfEnabled,
			scenario = global.Scenario,
			eternalWarCitiesPerCiv = eternalWarCities == null ? null : (int)eternalWarCities.Value,
		};

		PersistGameSettings(gameSetup);

		// World generation can take a bit of time if multiple attempts are
		// needed, so we don't want to tie up the UI thread.
		Task.Run(() => {
			try {
				gameSetup.Populate(save);
				// What a scenario made in the editor asks of the game as it
				// starts (the revealed map, the fog painted clear).
				if (C7Engine.Editor.ScenarioRules.IsEditorScenario(save)) {
					C7Engine.Editor.ScenarioStart.Apply(save);
				}
			} catch (Exception e) {
				Callable.From(() => OnCreateGameFailed(e)).CallDeferred();
				return;
			}

			log.Information("opening map");
			Callable.From(StartGame).CallDeferred();
		});
	}

	// Remembers every choice made on this and the world setup screen, for
	// the next game's setup and Quick Start.
	private void PersistGameSettings(GameSetup gameSetup) {
		try {
			choices.Civilization = selectedCivilization?.name ?? NewGameChoices.Random;
			choices.Difficulty = gameSetup.difficulty.Name;

			choices.Opponents = opponentSelectors.Select(ob => ob.GetItemText(ob.Selected)).ToList();
			// This screen has no Closed slots (the Multiplayer Setup
			// screen's), so Quick Start plays every slot again.
			choices.ClosedSlots = [];
			// Kept for human slots too, so a slot handed back to the
			// computer gets the AI it had.
			choices.AdvancedAiSlots = new(Enumerable.Range(0, aiTypePickers.Count).Where(i => AiTypeOf(i) == AiType.Advanced));
			if (hostingOnLan) {
				// The LAN seats, for the next game hosted; this game has no
				// hotseat players.
				choices.LanSlots = new(Enumerable.Range(0, humanToggles.Count).Where(i => humanToggles[i].ButtonPressed));
				choices.HotseatSlots = [];
			} else {
				string playerName = playerNameEdit.Text.Trim();
				choices.PlayerName = playerName != "" ? playerName : null;
				choices.HotseatSlots = [];
				for (int i = 0; i < humanToggles.Count; ++i) {
					if (humanToggles[i].ButtonPressed) {
						choices.HotseatSlots[i] = humanNames[i].Text.Trim();
					}
				}
			}

			if (PlayingScenario) {
				// The scenario's rules aren't the next normal game's.
				choices.Save();
				return;
			}
			choices.SetVictoryConditions(gameSetup.victoryConditions);
			choices.ShowScoreboard = gameSetup.showScoreboard;
			choices.CoreCitiesFreeOfCorruption = gameSetup.coreCitiesFreeOfCorruption;
			// Not Rising Tide's, which has it on by default.
			if (gameSetup.scenario != GameScenario.RisingTide) {
				choices.AcceleratedProduction = gameSetup.acceleratedProduction;
			}
			choices.CultureFlips = gameSetup.allowCultureFlips;
			choices.ScientificLeaders = gameSetup.scientificLeaders;
			choices.CorruptionRate = gameSetup.corruptionRate;

			choices.Save();
		} catch (Exception e) {
			log.Error(e, "Failed to persist game settings to C7.ini");
		}
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

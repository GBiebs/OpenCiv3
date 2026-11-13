using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using C7Engine.Lua;
using C7GameData;
using C7GameData.Save;
using Serilog;
using static SetupStyle;

// Civ3's Multiplayer Setup screen, in OpenCiv3's own look (owner's request,
// 2026-10-28): the game's settings on the left; the players, the rules and
// the messages on the right; a chat line along the bottom. The host sets up
// a hotseat or networked game here in one go, in place of the world and
// player setup screens. Launch starts a hotseat game straight away, and
// opens the LAN lobby for a networked one, where the people joining take
// the Open slots and choose their civilizations.
public partial class MultiplayerSetupScreen : Control {
	private static readonly ILogger log = LogManager.ForContext<MultiplayerSetupScreen>();

	private GlobalSingleton global;
	private SaveGame save;
	private NewGameChoices choices;
	private bool hasKings;

	private Control frame;
	private RichTextLabel messages;

	// GAME SETTINGS.
	private LineEdit gameNameEdit;
	private OptionButton gameType;
	private OptionButton gameMode;
	private OptionButton turnTimer;
	private OptionButton worldSize;
	private OptionButton barbarians;
	private OptionButton landMass;
	private OptionButton waterCoverage;
	private OptionButton climate;
	private OptionButton temperature;
	private OptionButton age;
	private OptionButton difficulty;
	private AcceptDialog gameLimits;
	private CheckBox turnLimit;

	// The game types, in the order listed, and which of them OpenCiv3 has.
	private static readonly (string text, MultiplayerGameType? type, string tooltip)[] GameTypes = [
		("Hotseat", MultiplayerGameType.Hotseat, "Everyone plays at this computer, taking turns."),
		("LAN", MultiplayerGameType.Lan, "Players on your local network join from \"Join LAN Game\"."),
		("Internet", MultiplayerGameType.Internet,
			"Players on your network, and anywhere else with the join code the lobby shows, through the online relay."),
		("Play By Email", MultiplayerGameType.PlayByEmail,
			"Each player plays their turn on their own computer, then saves the game and emails it to the next player."),
	];

	private static readonly (string text, MultiplayerGameMode mode)[] GameModes = [
		("Standard", MultiplayerGameMode.Standard),
		("Elimination", MultiplayerGameMode.Elimination),
		("Regicide", MultiplayerGameMode.Regicide),
		("Mass Regicide", MultiplayerGameMode.MassRegicide),
		("Victory Point Scoring", MultiplayerGameMode.VictoryPointScoring),
		("Custom", MultiplayerGameMode.Custom),
	];

	private static readonly WorldCharacteristics.Landform[] Landforms = [
		WorldCharacteristics.Landform.Pangaea, WorldCharacteristics.Landform.Continents, WorldCharacteristics.Landform.Archipelago];
	private static readonly WorldCharacteristics.OceanCoverage[] Oceans = [
		WorldCharacteristics.OceanCoverage.Percent_60, WorldCharacteristics.OceanCoverage.Percent_70, WorldCharacteristics.OceanCoverage.Percent_80];
	private static readonly WorldCharacteristics.Climate[] Climates = [
		WorldCharacteristics.Climate.Arid, WorldCharacteristics.Climate.Normal, WorldCharacteristics.Climate.Wet];
	private static readonly WorldCharacteristics.Temperature[] Temperatures = [
		WorldCharacteristics.Temperature.Warm, WorldCharacteristics.Temperature.Temperate, WorldCharacteristics.Temperature.Cool];
	private static readonly WorldCharacteristics.Age[] Ages = [
		WorldCharacteristics.Age.Billion_3, WorldCharacteristics.Age.Billion_4, WorldCharacteristics.Age.Billion_5];
	private static readonly BarbarianActivity[] BarbarianLevels = Enum.GetValues<BarbarianActivity>().OrderBy(b => b).ToArray();

	// PLAYER SETUP: the host's row, then a row for each rival's slot.
	private Button hostNameButton;
	private Panel hostColor;
	private OptionButton hostCivilization;
	private VBoxContainer slotRows;
	private readonly List<(OptionButton kind, OptionButton civ)> rows = [];
	// Parallel to rows: the AI that plays each Computer slot, its items in
	// AiType's order.
	private readonly List<OptionButton> aiPickers = [];
	// The AI column is narrower than the other two.
	private const float AiColumnRatio = 0.7f;
	private List<PlayerSlot> slots = [];
	// The scenario chosen (see WorldSetup.GameTypes), and whether MDJ's
	// rules are loaded, which MDJ and Rising Tide are played under.
	private GameScenario scenario = GameScenario.None;
	private bool mdjRules;
	private OptionButton scenarioPicker;
	private Label eternalWarCitiesLabel;
	private OptionButton eternalWarCities;
	// The row Rename renames: -1 for the host's.
	private int selectedRow = -1;
	private Button renameButton;
	private Button launchButton;
	// The civilizations every Civilizations list offers, then Random.
	private List<string> civNames;

	// GAME RULES.
	private CheckBox dominationVictory;
	private CheckBox spaceRaceVictory;
	private CheckBox diplomaticVictory;
	private CheckBox conquestVictory;
	private CheckBox culturalVictory;
	private CheckBox wonderVictory;
	private CheckBox acceleratedProduction;
	private CheckBox elimination;
	private CheckBox regicide;
	private CheckBox massRegicide;
	private CheckBox victoryPointScoring;
	private CheckBox simultaneousMoves;
	private CheckBox showScoreboard;
	private CheckBox cultureFlips;
	private CheckBox scientificLeaders;
	private CheckBox coreCitiesFreeOfCorruption;
	private VictoryConditions victoryConditions;
	private readonly List<Civ3Checkbox> ruleBoxes = [];
	private bool settingGameMode;

	private bool launching;

	private MultiplayerGameType GameType => GameTypes[gameType.Selected].type ?? MultiplayerGameType.Lan;
	private string HostName => LanSession.PlayerName;

	public override void _Ready() {
		global = GetNode<GlobalSingleton>("/root/GlobalSingleton");
		choices = NewGameChoices.Load();
		// A new game's rules, loaded here as the world setup screen does,
		// so this screen needs nothing set up before it.
		save = GameMode.Load(GamePaths.GameModesDir, GamePaths.GameMode).GetSave();
		hasKings = save.UnitPrototypes.Any(p => p.flags.Contains(SaveUnitPrototype.Flag.King));
		civNames = save.Civilizations.Where(c => !c.isBarbarian).Select(c => c.name).Append(NewGameChoices.Random).ToList();
		victoryConditions = choices.ChosenVictoryConditions(save.VictoryConditions);

		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		ColorRect backdrop = new() { Color = Colors.Black };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(backdrop);

		frame = MakeFrame();
		AddChild(frame);

		AddTitle(frame, this, "MULTIPLAYER SETUP");
		HBoxContainer body = MakeBody(frame);

		body.AddChild(MakeGameSettings());
		VBoxContainer right = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		right.AddThemeConstantOverride("separation", 8);
		body.AddChild(right);
		right.AddChild(MakePlayerSetup());
		right.AddChild(MakeGameRules());
		right.AddChild(MakeMessages());
		AddBottomStrip(frame, BackToMenu);

		ShowGameType();
		GetViewport().SizeChanged += Fit;
		Fit();
	}

	public override void _ExitTree() {
		GetViewport().SizeChanged -= Fit;
	}

	private void Fit() => SetupStyle.Fit(frame, GetViewportRect().Size);

	// ---- The screen's parts ----

	private Control MakeGameSettings() {
		(PanelContainer panel, VBoxContainer box) = MakePanel(this, "GAME SETTINGS");
		panel.CustomMinimumSize = new Vector2(268, 0);

		gameNameEdit = MakeDarkField();
		gameNameEdit.Text = MultiplayerSlots.DefaultGameName(HostName);
		gameNameEdit.MaxLength = C7Relay.RelayProtocol.MaxGameNameLength;
		gameNameEdit.TooltipText = "The game's name, as the online game list shows it.";
		box.AddChild(gameNameEdit);

		ScrollContainer scroll = new() {
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		box.AddChild(scroll);
		VBoxContainer settings = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		settings.AddThemeConstantOverride("separation", 2);
		scroll.AddChild(settings);
		settingsList = settings;

		Enum.TryParse(choices.MultiplayerGameType, out MultiplayerGameType lastType);
		if (choices.MultiplayerGameType == null) {
			lastType = MultiplayerGameType.Lan;
		}
		gameType = AddSetting(settings, "Game Type:", GameTypes.Select(t => t.text),
			Array.FindIndex(GameTypes, t => t.type == lastType), "Who plays, and where.");
		for (int i = 0; i < GameTypes.Length; ++i) {
			gameType.SetItemTooltip(i, GameTypes[i].tooltip);
			gameType.SetItemDisabled(i, GameTypes[i].type == null);
		}
		gameType.ItemSelected += _ => ShowGameType();

		// OpenCiv3's own game types, as the world setup screen offers them
		// (owner's request, 2026-10-28).
		scenarioPicker = AddSetting(settings, "Scenario:", WorldSetup.GameTypes.Select(g => g.name), 0,
			"A normal game, or one of OpenCiv3's scenarios.");
		for (int i = 0; i < WorldSetup.GameTypes.Length; ++i) {
			scenarioPicker.SetItemTooltip(i, WorldSetup.GameTypes[i].description);
		}
		scenarioPicker.ItemSelected += index => ChooseScenario(WorldSetup.GameTypes[index].scenario);

		gameMode = AddSetting(settings, "Game Mode:", GameModes.Select(m => m.text), 0,
			"Sets the special rules below: Elimination, Regicide, Mass Regicide or Victory Point Scoring, or none of them for Standard.");
		for (int i = 0; i < GameModes.Length; ++i) {
			MultiplayerGameMode mode = GameModes[i].mode;
			bool needsKings = mode is MultiplayerGameMode.Regicide or MultiplayerGameMode.MassRegicide;
			// Custom is what a mix of the rules is called, not a choice.
			gameMode.SetItemDisabled(i, mode == MultiplayerGameMode.Custom || (needsKings && !hasKings));
			if (needsKings && !hasKings) {
				gameMode.SetItemTooltip(i, "These rules have no King units.");
			}
		}
		gameMode.ItemSelected += index => ChooseGameMode(GameModes[index].mode);

		List<string> timers = LanLobby.TurnTimeChoices.Select(m => m == 0 ? "None" : $"{m} Minutes").ToList();
		turnTimer = AddSetting(settings, "Turn Timer:", timers, Math.Max(0, Array.IndexOf(LanLobby.TurnTimeChoices, choices.TurnTimerMinutes ?? 0)),
			"How long each player has for their turn before it ends for them. With simultaneous moves, how long everyone has for the round.");

		WorldSize lastSize = NewGameChoices.DefaultWorldSize(save.WorldSizes);
		int sizeIndex = choices.WorldSize == NewGameChoices.Random
			? save.WorldSizes.Count
			: save.WorldSizes.IndexOf(choices.ChosenWorldSize(save.WorldSizes, new Random()));
		worldSize = AddSetting(settings, "World Size:", save.WorldSizes.Select(w => w.name).Append(NewGameChoices.Random),
			sizeIndex < 0 ? save.WorldSizes.IndexOf(lastSize) : sizeIndex,
			"The world's size, which sets how many civilizations it has room for: a slot for each.");
		worldSize.ItemSelected += _ => {
			ShowEternalWarCities();
			MakeSlotRows();
		};

		barbarians = AddSetting(settings, "Barbarian Activity:", BarbarianLevels.Select(b => b.ToString()).Append(NewGameChoices.Random),
			choices.Barbarians is BarbarianActivity b ? Array.IndexOf(BarbarianLevels, b) : BarbarianLevels.Length);
		landMass = AddSetting(settings, "Land Mass:", ["Pangaea", "Continents", "Archipelago"], Array.IndexOf(Landforms, choices.Landform));
		waterCoverage = AddSetting(settings, "Water Coverage:", ["60% Water", "70% Water", "80% Water"], Array.IndexOf(Oceans, choices.OceanCoverage));
		climate = AddSetting(settings, "Climate:", ["Arid", "Normal", "Wet"], Array.IndexOf(Climates, choices.Climate));
		temperature = AddSetting(settings, "Temperature:", ["Warm", "Temperate", "Cool"], Array.IndexOf(Temperatures, choices.Temperature));
		age = AddSetting(settings, "Age:", ["3 Billion", "4 Billion", "5 Billion"], Array.IndexOf(Ages, choices.Age));
		difficulty = AddSetting(settings, "Difficulty:", save.Difficulties.Select(d => d.Name),
			save.Difficulties.IndexOf(choices.ChosenDifficulty(save.Difficulties)), "How hard the computer's civilizations play.");

		// Eternal War's own setting, shown only for it.
		const string citiesTip = "The cities each civilization starts with, scattered at random among its enemies'. "
			+ "The default grows with the world: 2 on a tiny world up to 10 on a huge one.";
		eternalWarCitiesLabel = new Label {
			Text = "Cities per Civ:",
			Visible = false,
			TooltipText = citiesTip,
			MouseFilter = MouseFilterEnum.Pass,
		};
		eternalWarCitiesLabel.AddThemeFontSizeOverride("font_size", 12);
		settings.AddChild(eternalWarCitiesLabel);
		eternalWarCities = MakeDropdown(citiesTip);
		AddItems(eternalWarCities, Enumerable.Range(GameSetup.MinEternalWarCitiesPerCiv,
			GameSetup.MaxEternalWarCitiesPerCiv - GameSetup.MinEternalWarCitiesPerCiv + 1).Select(n => n.ToString()), 0);
		eternalWarCities.Visible = false;
		settings.AddChild(eternalWarCities);

		// Civ3's Game Limits: OpenCiv3's limit is the turn limit.
		Button limits = MakeButton("Game Limits", "When the game ends if no one has won: the turn limit.");
		limits.CustomMinimumSize = new Vector2(110, 24);
		limits.AddThemeFontSizeOverride("font_size", 12);
		limits.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		limits.Pressed += () => gameLimits.PopupCentered();
		box.AddChild(limits);
		MakeGameLimits();
		return panel;
	}

	private OptionButton AddSetting(VBoxContainer box, string label, IEnumerable<string> items, int selected, string tooltip = null) {
		Label name = MakeSettingLabel(label);
		box.AddChild(name);
		OptionButton dropdown = MakeDropdown(tooltip);
		AddItems(dropdown, items, selected);
		box.AddChild(dropdown);
		return dropdown;
	}

	private void MakeGameLimits() {
		gameLimits = new AcceptDialog { Title = "Game Limits", OkButtonText = "OK" };
		VBoxContainer box = new();
		turnLimit = MakeRule($"Turn limit ({save.TimeOptions.turnLimit} turns)", victoryConditions.UseTurnLimit,
			$"End the game after turn {save.TimeOptions.turnLimit}, scoring who leads. Without it, the game goes on until someone wins.");
		box.AddChild(turnLimit);
		Label note = new() {
			Text = "Civ3's time and score limits aren't in OpenCiv3 yet.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(280, 0),
		};
		note.AddThemeFontSizeOverride("font_size", 12);
		box.AddChild(note);
		gameLimits.AddChild(box);
		AddChild(gameLimits);
	}

	private Control MakePlayerSetup() {
		(PanelContainer panel, VBoxContainer box) = MakePanel(this, "PLAYER SETUP");
		panel.SizeFlagsVertical = SizeFlags.ExpandFill;
		panel.SizeFlagsStretchRatio = 1.4f;

		HBoxContainer headings = new();
		headings.AddThemeConstantOverride("separation", 10);
		foreach (string heading in new[] { "Players", "Civilizations", "AI" }) {
			Label label = new() { Text = heading, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center };
			label.AddThemeFontSizeOverride("font_size", 13);
			if (heading == "AI") {
				label.SizeFlagsStretchRatio = AiColumnRatio;
			}
			headings.AddChild(label);
		}
		box.AddChild(headings);

		// Room for eight rows, as Civ3's screen has; a bigger world's
		// slots scroll.
		ScrollContainer scroll = new() {
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 8 * 29),
		};
		box.AddChild(scroll);
		VBoxContainer list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 3);
		scroll.AddChild(list);

		// The host's row: their name, with their civilization's color.
		HBoxContainer hostRow = new();
		hostRow.AddThemeConstantOverride("separation", 10);
		HBoxContainer nameCell = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		hostNameButton = new Button {
			Text = HostName,
			Alignment = HorizontalAlignment.Left,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			ToggleMode = true,
			ButtonPressed = true,
			ClipText = true,
			TooltipText = "You, hosting. Rename changes the name you play under.",
		};
		hostNameButton.AddThemeStyleboxOverride("pressed", Box(new Color(0.85f, 0.78f, 0.6f), Brown, 1, 2));
		hostNameButton.AddThemeStyleboxOverride("normal", Box(new Color(0, 0, 0, 0), new Color(Brown, 0.4f), 1, 2));
		hostNameButton.AddThemeStyleboxOverride("hover", Box(new Color(0.93f, 0.88f, 0.75f), Brown, 1, 2));
		hostNameButton.AddThemeColorOverride("font_pressed_color", Ink);
		hostNameButton.Pressed += () => SelectRow(-1);
		nameCell.AddChild(hostNameButton);
		hostColor = new Panel { CustomMinimumSize = new Vector2(16, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
		nameCell.AddChild(hostColor);
		hostRow.AddChild(nameCell);
		hostCivilization = MakeDropdown("The civilization you play.");
		string hostCiv = choices.ChoseRandomCivilization ? NewGameChoices.Random : choices.ChosenCivilization(save.Civilizations).name;
		AddItems(hostCivilization, civNames, civNames.IndexOf(hostCiv));
		hostCivilization.ItemSelected += _ => UpdateCivilizations();
		hostRow.AddChild(hostCivilization);
		// The host plays their own civ, so has no AI; this keeps the
		// columns lined up.
		hostRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = AiColumnRatio });
		list.AddChild(hostRow);

		slotRows = new VBoxContainer();
		slotRows.AddThemeConstantOverride("separation", 3);
		list.AddChild(slotRows);
		slots = MultiplayerSlots.SlotsAsLastTime(choices, GameType, SlotCount());
		MakeSlotRows();

		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 24);
		renameButton = MakeButton("Rename", "Rename the selected player: you, or a player at this computer in hotseat.");
		renameButton.Pressed += Rename;
		buttons.AddChild(renameButton);
		// While hosting, for the player in the selected slot.
		removeButton = MakeButton("Remove", "Select a player's slot to remove them.");
		removeButton.Pressed += RemoveSelected;
		removeButton.Visible = false;
		buttons.AddChild(removeButton);
		banButton = MakeButton("Ban", "Select a player's slot to ban them.");
		banButton.Pressed += BanSelected;
		banButton.Visible = false;
		buttons.AddChild(banButton);
		launchButton = MakeButton("Launch", "Start the game: in hotseat straight away; on the network once a player has taken each Open slot.", true);
		launchButton.Pressed += Launch;
		buttons.AddChild(launchButton);
		watchingLabel = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		watchingLabel.AddThemeFontSizeOverride("font_size", 13);
		box.AddChild(watchingLabel);
		box.AddChild(buttons);
		return panel;
	}

	// A slot for each civilization the world has room for, but the host's.
	private int SlotCount() {
		WorldSize size = worldSize.Selected < save.WorldSizes.Count ? save.WorldSizes[worldSize.Selected]
			: NewGameChoices.DefaultWorldSize(save.WorldSizes);
		// Kings & Nomads has about a quarter fewer civs than usual (see
		// GameSetup.KingsAndNomadsCivs).
		int civs = scenario == GameScenario.KingsAndNomads ? GameSetup.KingsAndNomadsCivs(size.numberOfCivs) : size.numberOfCivs;
		return Math.Max(1, civs - 1);
	}

	// ---- Scenarios ----

	private void ChooseScenario(GameScenario chosen) {
		scenario = chosen;
		bool mdj = chosen is GameScenario.Mdj or GameScenario.RisingTide;
		if (mdj != mdjRules) {
			LoadRules(mdj);
		}
		// Eternal War and Rising Tide are played on a pangaea, Kings &
		// Nomads on continents or a pangaea.
		bool pangaeaOnly = chosen is GameScenario.EternalWar or GameScenario.RisingTide;
		bool noArchipelago = pangaeaOnly || chosen == GameScenario.KingsAndNomads;
		landMass.SetItemDisabled(1, pangaeaOnly);
		landMass.SetItemDisabled(2, noArchipelago);
		if (pangaeaOnly) {
			landMass.Select(0);
		} else if (noArchipelago && landMass.Selected == 2) {
			landMass.Select(1);
		}
		landMass.TooltipText = pangaeaOnly ? "This scenario is played on a pangaea."
			: noArchipelago ? "This scenario is played on continents or a pangaea." : "";
		// Rising Tide always starts with it on (owner's ruling, 2026-10-24).
		acceleratedProduction.ButtonPressed = chosen == GameScenario.RisingTide;
		ShowEternalWarCities();
		MakeSlotRows();
		var type = WorldSetup.GameTypes.First(g => g.scenario == chosen);
		AddMessage(chosen == GameScenario.None ? "A normal game." : $"{type.name}: {type.description}");
	}

	// Eternal War's cities per civ, at the default for the world size.
	private void ShowEternalWarCities() {
		bool eternalWar = scenario == GameScenario.EternalWar;
		eternalWarCitiesLabel.Visible = eternalWar;
		eternalWarCities.Visible = eternalWar;
		WorldSize size = worldSize.Selected < save.WorldSizes.Count ? save.WorldSizes[worldSize.Selected]
			: NewGameChoices.DefaultWorldSize(save.WorldSizes);
		eternalWarCities.Select(GameSetup.DefaultEternalWarCitiesPerCiv(save.WorldSizes, size) - GameSetup.MinEternalWarCitiesPerCiv);
	}

	// The rules a new game is played under: MDJ's for MDJ and Rising Tide
	// (owner's choice, 2026-10-28), or else the usual ones, as the world
	// setup screen loads them. The lists made from them are made again.
	private void LoadRules(bool mdj) {
		SaveGame rules = null;
		if (mdj) {
			try {
				rules = GameSetup.LoadMdjRules(GamePaths.GameModesDir, GamePaths.GameMode);
			} catch (Exception e) {
				log.Error(e, "Could not load MPTournamentMDJ's rules; playing under the usual ones");
				AddMessage("MDJ's rules couldn't be loaded, so the usual ones are used.", ProblemColor);
			}
		}
		mdjRules = rules != null;
		save = rules ?? GameMode.Load(GamePaths.GameModesDir, GamePaths.GameMode).GetSave();
		// MDJ keeps its leaders' Civ3 art in the scenario's folders.
		Util.setModPath(save.ScenarioSearchPath);
		Util.ClearCaches();
		hasKings = save.UnitPrototypes.Any(p => p.flags.Contains(SaveUnitPrototype.Flag.King));
		civNames = save.Civilizations.Where(c => !c.isBarbarian).Select(c => c.name).Append(NewGameChoices.Random).ToList();
		// The players who joined choose from these rules' civilizations.
		if (Hosting) {
			Host.SetPlayableCivilizations(save.Civilizations);
		}

		// MDJ's world sizes are its own (by player count, 1v1 to 8 Player).
		worldSize.Clear();
		AddItems(worldSize, save.WorldSizes.Select(w => w.name).Append(NewGameChoices.Random),
			save.WorldSizes.IndexOf(NewGameChoices.DefaultWorldSize(save.WorldSizes)));
		string level = difficulty.Selected >= 0 ? difficulty.GetItemText(difficulty.Selected) : null;
		difficulty.Clear();
		AddItems(difficulty, save.Difficulties.Select(d => d.Name),
			Math.Max(0, save.Difficulties.FindIndex(d => d.Name == level)));
		string host = hostCivilization.Selected >= 0 ? hostCivilization.GetItemText(hostCivilization.Selected) : NewGameChoices.Random;
		hostCivilization.Clear();
		AddItems(hostCivilization, civNames, civNames.Contains(host) ? civNames.IndexOf(host) : civNames.Count - 1);
		for (int i = 0; i < slots.Count; ++i) {
			if (!civNames.Contains(slots[i].civilization ?? NewGameChoices.Random)) {
				slots[i] = slots[i] with { civilization = NewGameChoices.Random };
			}
		}
		regicide.Disabled = !hasKings;
		massRegicide.Disabled = !hasKings;
		if (!hasKings) {
			regicide.ButtonPressed = false;
			massRegicide.ButtonPressed = false;
		}
	}

	// Makes the rivals' rows, keeping what was set for the slots that are
	// still there.
	private void MakeSlotRows() {
		int count = SlotCount();
		FitSlots(count);
		while (slots.Count < count) {
			slots.Add(new PlayerSlot(SlotKind.Computer, NewGameChoices.Random));
		}
		foreach ((OptionButton kind, OptionButton civ) in rows) {
			kind.GetParent().QueueFree();
		}
		rows.Clear();
		aiPickers.Clear();
		for (int i = 0; i < count; ++i) {
			int slot = i;
			HBoxContainer row = new();
			row.AddThemeConstantOverride("separation", 10);
			OptionButton kind = MakeDropdown();
			AddItems(kind, ["Open", "Closed", "Computer"], (int)slots[i].kind);
			kind.ItemSelected += index => {
				slots[slot] = slots[slot] with { kind = (SlotKind)index };
				SelectRow(slot);
				ShowSlots();
			};
			kind.Pressed += () => SelectRow(slot);
			row.AddChild(kind);
			OptionButton civ = MakeDropdown();
			AddItems(civ, civNames, civNames.IndexOf(slots[i].civilization ?? NewGameChoices.Random) is int c && c >= 0 ? c : civNames.Count - 1);
			civ.ItemSelected += index => {
				slots[slot] = slots[slot] with { civilization = civNames[(int)index] };
				UpdateCivilizations();
			};
			row.AddChild(civ);
			OptionButton ai = MakeDropdown();
			ai.SizeFlagsStretchRatio = AiColumnRatio;
			AddItems(ai, ["Standard", "Advanced"], (int)slots[i].aiType);
			ai.ItemSelected += index => slots[slot] = slots[slot] with { aiType = (AiType)index };
			row.AddChild(ai);
			slotRows.AddChild(row);
			rows.Add((kind, civ));
			aiPickers.Add(ai);
		}
		if (selectedRow >= count) {
			SelectRow(-1);
		}
		ShowSlots();
	}

	// Shows each slot as the game type has it: in hotseat an Open slot is a
	// player at this computer, under their name; on the network, a seat
	// whose player chooses their civilization in the lobby.
	private void ShowSlots() {
		if (rows.Count == 0 || gameType == null) {
			return;
		}
		bool hotseat = MultiplayerSlots.SeatsSetUpHere(GameType);
		bool byEmail = GameType == MultiplayerGameType.PlayByEmail;
		int playerNumber = 1;
		for (int i = 0; i < rows.Count; ++i) {
			(OptionButton kind, OptionButton civ) = rows[i];
			PlayerSlot slot = slots[i];
			bool open = slot.kind == SlotKind.Open;
			if (open && hotseat) {
				++playerNumber;
			}
			kind.SetItemText(0, open && hotseat ? NewGameChoices.HotseatName(slot.name, playerNumber) : "Open");
			kind.TooltipText = byEmail
				? "Open: another player, who plays their turns from the saves emailed to them. Closed: no civilization. Computer: a civilization the computer plays."
				: hotseat
				? "Open: another player at this computer. Closed: no civilization. Computer: a civilization the computer plays."
				: "Open: a seat for a player joining over the network. Closed: no civilization. Computer: a civilization the computer plays.";
			bool networkSeat = open && !hotseat;
			civ.Disabled = slot.kind == SlotKind.Closed || networkSeat;
			civ.TooltipText = networkSeat ? "Whoever takes this seat chooses their civilization in the lobby; Random if they don't."
				: slot.kind == SlotKind.Closed ? "No one plays this slot."
				: "This player's civilization.";
			civ.Select(civ.Disabled ? civNames.Count - 1 : Math.Max(0, civNames.IndexOf(slot.civilization ?? NewGameChoices.Random)));
			// Only the computer's civs have an AI to choose; the choice is
			// kept for when the slot is set back to Computer.
			OptionButton ai = aiPickers[i];
			ai.Disabled = slot.kind != SlotKind.Computer;
			ai.TooltipText = ai.Disabled ? "Only a Computer slot has an AI."
				: "Which AI plays this civilization: Standard, the usual one, or Advanced.";
		}
		UpdateCivilizations();
		renameButton?.SetDeferred(BaseButton.PropertyName.Disabled, !CanRename(selectedRow));
		SyncSeats();
	}

	// Each civilization can be chosen once: those chosen for one slot are
	// greyed out in the others.
	private void UpdateCivilizations() {
		List<OptionButton> lists = [hostCivilization, .. rows.Where((r, i) => !r.civ.Disabled).Select(r => r.civ)];
		Dictionary<string, OptionButton> chosenBy = [];
		foreach (OptionButton list in lists) {
			string name = civNames[list.Selected];
			if (name != NewGameChoices.Random) {
				chosenBy.TryAdd(name, list);
			}
		}
		// Nor can those the players who joined chose.
		HashSet<string> guestCivs = GuestCivilizations();
		foreach (OptionButton list in lists.Concat(rows.Select(r => r.civ)).Distinct()) {
			for (int i = 0; i < civNames.Count - 1; ++i) {
				list.SetItemDisabled(i, (chosenBy.TryGetValue(civNames[i], out OptionButton by) && by != list) || guestCivs.Contains(civNames[i]));
			}
		}
		string host = civNames[hostCivilization.Selected];
		Civilization civ = save.Civilizations.Find(c => c.name == host);
		StyleBoxFlat square = Box(civ == null ? new Color(0.6f, 0.6f, 0.6f) : TextureLoader.LoadColor(civ.primaryColorIndex), Ink, 1, 0);
		hostColor.AddThemeStyleboxOverride("panel", square);
		hostColor.TooltipText = civ == null ? "Your color, once your civilization is drawn." : $"Your color, as the {civ.noun}.";
		ReserveCivilizations();
	}

	private void SelectRow(int row) {
		selectedRow = row;
		hostNameButton.SetPressedNoSignal(row == -1);
		renameButton?.SetDeferred(BaseButton.PropertyName.Disabled, !CanRename(row));
		if (Hosting) {
			ShowSelectedPlayer();
		}
	}

	// The host can always be renamed, and in hotseat the other players at
	// this computer; people joining bring their own names.
	private bool CanRename(int row) {
		return row == -1 || (row < slots.Count && slots[row].kind == SlotKind.Open && MultiplayerSlots.SeatsSetUpHere(GameType));
	}

	private void Rename() {
		int row = selectedRow;
		if (!CanRename(row)) {
			return;
		}
		ConfirmationDialog dialog = new() { Title = "Rename", OkButtonText = "OK", CancelButtonText = "Cancel" };
		LineEdit name = new() {
			Text = row == -1 ? HostName : slots[row].name ?? "",
			PlaceholderText = row == -1 ? LanSession.DefaultPlayerName : rows[row].kind.GetItemText(0),
			MaxLength = C7Engine.Network.LanHost.MaxPlayerNameLength,
			CustomMinimumSize = new Vector2(260, 0),
		};
		dialog.AddChild(name);
		dialog.RegisterTextEnter(name);
		dialog.Confirmed += () => {
			string text = name.Text.Trim();
			if (row == -1) {
				string oldDefault = MultiplayerSlots.DefaultGameName(HostName);
				LanSession.PlayerName = text;
				hostNameButton.Text = HostName;
				// The game keeps its host's name, unless it was renamed.
				if (gameNameEdit.Text == oldDefault) {
					gameNameEdit.Text = MultiplayerSlots.DefaultGameName(HostName);
				}
				AddMessage($"You are now {HostName}.");
			} else {
				slots[row] = slots[row] with { name = text == "" ? null : text };
				ShowSlots();
			}
		};
		dialog.VisibilityChanged += () => {
			if (!dialog.Visible) {
				dialog.QueueFree();
			}
		};
		AddChild(dialog);
		dialog.PopupCentered();
		name.GrabFocus();
	}

	private Control MakeGameRules() {
		(PanelContainer panel, VBoxContainer box) = MakePanel(this, "GAME RULES");
		ScrollContainer scroll = new() {
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways,
			CustomMinimumSize = new Vector2(0, 5 * 24),
		};
		box.AddChild(scroll);
		GridContainer grid = new() { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 0);
		scroll.AddChild(grid);

		VictoryConditions vc = victoryConditions;
		dominationVictory = MakeRule("Allow Domination Victory", vc.AllowDominationVictory,
			$"Win by controlling {vc.DominationTerritoryPercent}% of the world's land and {vc.DominationPopulationPercent}% of its population.");
		spaceRaceVictory = MakeRule("Allow Space Race Victory", vc.AllowSpaceRaceVictory,
			"Win by being the first to build every part of the spaceship. Without it, the spaceship can't be built.");
		diplomaticVictory = MakeRule("Allow Diplomatic Victory", vc.AllowDiplomaticVictory,
			"Win by being elected Secretary General in a United Nations vote. Without it, the United Nations holds no votes.");
		conquestVictory = MakeRule("Allow Conquest Victory", vc.AllowConquestVictory, "Win by eliminating every rival civilization.");
		culturalVictory = MakeRule("Allow Cultural Victory", vc.AllowCulturalVictory,
			$"Win when one of your cities has {vc.CultureOneCityWin:N0} culture, or your civilization has {vc.CultureAllCitiesWin:N0} culture "
			+ "(on a standard map; less on smaller worlds, more on larger) and twice as much as any rival.");
		wonderVictory = MakeRule("Allow Wonder Victory", vc.AllowWonderVictory,
			"The game ends as soon as every great wonder has been built, and the civilization with the most victory points wins.");
		// Off by default in multiplayer, as is No Corruption Near Capital;
		// Culture Flipping and Scientific Leaders on (owner's ruling,
		// 2026-10-28).
		acceleratedProduction = MakeRule("Accelerated Production", false,
			"Cities need half the food to grow and half the shields to build, as in Civ3.");
		elimination = MakeRule("Elimination", vc.CityElimination,
			vc.CityEliminationCount == 1
				? "A civilization that loses a city to an enemy is eliminated: its units and cities are destroyed."
				: $"A civilization that loses {vc.CityEliminationCount} cities to enemies is eliminated: its units and cities are destroyed.");
		const string noKings = "These rules have no King units.";
		regicide = MakeRule("Regicide", hasKings && vc.Regicide,
			hasKings ? "Each civilization has a King. A civilization whose King is killed is eliminated: its units and cities are destroyed." : noKings,
			hasKings);
		massRegicide = MakeRule("Mass Regicide", hasKings && vc.MassRegicide,
			hasKings ? $"Each civilization has {InstantElimination.MassRegicideKings} Kings. A civilization whose Kings are all killed is eliminated: "
				+ "its units and cities are destroyed." : noKings,
			hasKings);
		victoryPointScoring = MakeRule("Victory Point Scoring", vc.VictoryLocations,
			$"Win by being the first to {vc.VictoryPointLimit:N0} victory points, earned by building wonders, defeating enemy units, discovering "
			+ "advances, conquering cities and holding Victory Locations (each civilization's starting location) with a defending unit. "
			+ "At the turn limit, the most victory points wins.");
		// Civ3's rules OpenCiv3 doesn't have, shown as Civ3 lists them.
		Civ3Checkbox linkedStarts = MakeRule("Culturally Linked Start Loc.", false, "Not in OpenCiv3 yet: civilizations start anywhere.", false);
		Civ3Checkbox noRestart = MakeRule("Don't Restart Eliminated Civilizations", true,
			"OpenCiv3 never restarts an eliminated civilization.", false);
		Civ3Checkbox noGoodyHuts = MakeRule("No Goody Huts", false, "Not in OpenCiv3 yet.", false);
		// OpenCiv3's own rules, which its player setup screen has too.
		simultaneousMoves = MakeRule("Simultaneous Moves", true,
			"Every human plays their turn at the same time, rather than waiting for each other. The computer's civilizations move after them.");
		showScoreboard = MakeRule("Show Scoreboard", choices.ShowScoreboard ?? true,
			"With more than one human player, show everyone's score and how long the current turn has taken.");
		cultureFlips = MakeRule("Culture Flipping", true,
			"Cities may defect to a neighboring civilization whose culture outweighs their owner's. A garrison makes it less likely.");
		scientificLeaders = MakeRule("Scientific Leaders", true,
			"The first civilization to discover an advance may gain a scientific leader (3% chance, 5% for scientific civilizations), "
			+ "who can complete anything a city is building, wonders included.");
		coreCitiesFreeOfCorruption = MakeRule("No Corruption Near Capital", false,
			"Custom rule: the capital and the 5 cities nearest it have no corruption or waste.");

		// Two columns, as Civ3's: the victories on the left, the other rules
		// on the right; then the rest.
		foreach (Control rule in new Control[] {
			dominationVictory, acceleratedProduction,
			spaceRaceVictory, elimination,
			diplomaticVictory, regicide,
			conquestVictory, massRegicide,
			culturalVictory, victoryPointScoring,
			wonderVictory, linkedStarts,
			noRestart, noGoodyHuts,
			simultaneousMoves, showScoreboard,
			cultureFlips, scientificLeaders,
			coreCitiesFreeOfCorruption,
		}) {
			grid.AddChild(rule);
			// Those OpenCiv3 has, as the players are shown them.
			if (rule is Civ3Checkbox ruleBox && rule.Modulate.A == 1) {
				ruleBoxes.Add(ruleBox);
			}
		}
		foreach (CheckBox special in new[] { elimination, regicide, massRegicide, victoryPointScoring }) {
			special.Toggled += _ => ShowGameMode();
		}
		ShowGameMode();
		return panel;
	}

	private void ChooseGameMode(MultiplayerGameMode mode) {
		VictoryConditions rules = new() {
			CityElimination = elimination.ButtonPressed,
			Regicide = regicide.ButtonPressed,
			MassRegicide = massRegicide.ButtonPressed,
			VictoryLocations = victoryPointScoring.ButtonPressed,
		};
		MultiplayerSlots.ApplyGameMode(rules, mode);
		settingGameMode = true;
		elimination.ButtonPressed = rules.CityElimination;
		regicide.ButtonPressed = hasKings && rules.Regicide;
		massRegicide.ButtonPressed = hasKings && rules.MassRegicide;
		victoryPointScoring.ButtonPressed = rules.VictoryLocations;
		settingGameMode = false;
		ShowGameMode();
	}

	// The game mode the special rules make.
	private void ShowGameMode() {
		if (settingGameMode || gameMode == null) {
			return;
		}
		MultiplayerGameMode mode = MultiplayerSlots.GameModeOf(new VictoryConditions {
			CityElimination = elimination.ButtonPressed,
			Regicide = regicide.ButtonPressed,
			MassRegicide = massRegicide.ButtonPressed,
			VictoryLocations = victoryPointScoring.ButtonPressed,
		});
		gameMode.Select(Array.FindIndex(GameModes, m => m.mode == mode));
	}

	private Control MakeMessages() {
		(PanelContainer panel, VBoxContainer box) = MakePanel(this, "MESSAGES");
		panel.SizeFlagsVertical = SizeFlags.ExpandFill;
		// While hosting, how the lobby stands.
		hostStatus = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		hostStatus.AddThemeFontSizeOverride("font_size", 14);
		box.AddChild(hostStatus);
		messages = SetupStyle.MakeMessages();
		box.AddChild(messages);
		return panel;
	}

	private void AddMessage(string text, Color? color = null) => SetupStyle.AddMessage(messages, text, color);

	// ---- Game type ----

	private void ShowGameType() {
		MultiplayerGameType type = GameType;
		// Hotseat and play by email aren't played over the network.
		bool hotseat = MultiplayerSlots.SeatsSetUpHere(type);
		// Only a networked game has a turn clock and simultaneous turns (see
		// LanHost), and a name to list it under.
		turnTimer.Disabled = hotseat;
		simultaneousMoves.Disabled = hotseat;
		simultaneousMoves.Modulate = new Color(1, 1, 1, hotseat ? 0.5f : 1);
		gameNameEdit.Editable = !hotseat;
		string networkOnly = hotseat ? " Only in a networked game." : "";
		turnTimer.TooltipText = "How long each player has for their turn before it ends for them. With simultaneous moves, how long everyone "
			+ "has for the round." + networkOnly;
		UpdateHosting();
		ShowSlots();
		messages.Clear();
		string what = type switch {
			MultiplayerGameType.Hotseat => "a hotseat game",
			MultiplayerGameType.PlayByEmail => "a play by email game",
			_ => gameNameEdit.Text,
		};
		AddMessage($"Setting up {what}, hosted by {HostName}.");
		AddMessage(type switch {
			MultiplayerGameType.Hotseat => "Each Open slot is another player at this computer, taking turns with you. Rename gives them their names.",
			MultiplayerGameType.PlayByEmail => "Each Open slot is another player. Each plays their turn on their own computer, then saves "
				+ "the game and emails the save to the next player. Rename gives them their names.",
			MultiplayerGameType.Internet => "The game is hosted online too: players anywhere join the Open slots with the join code under Game "
				+ "Settings, and players on your network from \"Join LAN Game\". They choose their civilizations here. Launch starts the game "
				+ "once every Open slot is taken.",
			_ => "Players on your network join the Open slots from \"Join LAN Game\" and choose their civilizations here. "
				+ "Launch starts the game once every Open slot is taken.",
		});
	}

	// ---- Launch ----

	private void Launch() {
		if (launching) {
			return;
		}
		MultiplayerGameType type = GameType;
		Random random = new();
		// A random civilization for the host is one no player who joined chose.
		HashSet<string> guestCivs = GuestCivilizations();
		MultiplayerPlan plan = MultiplayerSlots.Plan(type, civNames[hostCivilization.Selected], slots.Take(rows.Count).ToList(),
			save.Civilizations.Where(c => !guestCivs.Contains(c.name)).ToList(), random);
		if (plan.problem != null) {
			AddMessage(plan.problem, ProblemColor);
			return;
		}

		VictoryConditions vc = victoryConditions;
		vc.AllowDominationVictory = dominationVictory.ButtonPressed;
		vc.AllowSpaceRaceVictory = spaceRaceVictory.ButtonPressed;
		vc.AllowDiplomaticVictory = diplomaticVictory.ButtonPressed;
		vc.AllowConquestVictory = conquestVictory.ButtonPressed;
		vc.AllowCulturalVictory = culturalVictory.ButtonPressed;
		vc.AllowWonderVictory = wonderVictory.ButtonPressed;
		vc.CityElimination = elimination.ButtonPressed;
		vc.Regicide = regicide.ButtonPressed;
		vc.MassRegicide = massRegicide.ButtonPressed;
		vc.VictoryLocations = victoryPointScoring.ButtonPressed;
		vc.UseTurnLimit = turnLimit.ButtonPressed;

		WorldCharacteristics world = new(save) {
			worldSize = ChosenWorldSize(random, plan),
			barbarianActivity = barbarians.Selected < BarbarianLevels.Length ? BarbarianLevels[barbarians.Selected]
				: BarbarianLevels[random.Next(BarbarianLevels.Length)],
			landform = Landforms[landMass.Selected],
			oceanCoverage = Oceans[waterCoverage.Selected],
			climate = Climates[climate.Selected],
			temperature = Temperatures[temperature.Selected],
			age = Ages[age.Selected],
			mapSeed = random.Next(),
		};

		GameSetup setup = new() {
			playerCivilization = plan.playerCivilization,
			// Names only matter when there's more than one human.
			playerName = HostName,
			hotseatPlayers = plan.hotseatPlayers,
			difficulty = save.Difficulties[difficulty.Selected],
			worldCharacteristics = world,
			opponents = plan.opponents,
			victoryConditions = vc,
			showScoreboard = showScoreboard.ButtonPressed,
			coreCitiesFreeOfCorruption = coreCitiesFreeOfCorruption.ButtonPressed,
			acceleratedProduction = acceleratedProduction.ButtonPressed,
			allowCultureFlips = cultureFlips.ButtonPressed,
			scientificLeaders = scientificLeaders.ButtonPressed,
			// The rate chosen on the player setup screen, or the rules'.
			corruptionRate = choices.CorruptionRate,
			invalidMapsDirectory = InvalidMaps.FolderIfEnabled,
			scenario = scenario,
			eternalWarCitiesPerCiv = scenario == GameScenario.EternalWar
				? GameSetup.MinEternalWarCitiesPerCiv + eternalWarCities.Selected
				: null,
		};
		RememberChoices(type);

		global.ResetLoadGameFields();
		global.Scenario = scenario;
		global.WatchNextGame = false;
		global.WorldCharacteristics = world;
		global.SaveGame = save;
		global.NewGameChoices = choices;
		SetLaunching(true);

		if (MultiplayerSlots.SeatsSetUpHere(type)) {
			LanSession.HostNextGame = false;
			AddMessage("Creating the world...");
			// World generation can take a while, so not on the UI thread.
			Task.Run(() => {
				try {
					setup.Populate(save);
					// Each plays on their own computer (see PlayByEmail).
					save.PlayByEmail = type == MultiplayerGameType.PlayByEmail;
				} catch (Exception e) {
					Callable.From(() => LaunchFailed(e)).CallDeferred();
					return;
				}
				Callable.From(StartGame).CallDeferred();
			});
			return;
		}

		// Hosting here, the players who joined have chosen their
		// civilizations, and the world is made now.
		if (Hosting) {
			LaunchHosted(setup, plan);
			return;
		}

		// Couldn't host here: the lobby hosts it, where the guests choose
		// their civilizations, and the game is created once the host starts
		// it there.
		int minutes = LanLobby.TurnTimeChoices[turnTimer.Selected];
		string name = gameNameEdit.Text.Trim();
		LanSession.HostNextGame = true;
		LanSession.PendingGame = new PendingLanGame(setup, save, plan.guestSeats,
			gameName: name == "" ? null : name,
			turnTimeLimit: minutes == 0 ? null : TimeSpan.FromMinutes(minutes),
			simultaneousTurns: simultaneousMoves.ButtonPressed,
			hostOnline: type == MultiplayerGameType.Internet);
		StartGame();
	}

	// The world size chosen; for Random, one with room for everyone if there
	// is one.
	private WorldSize ChosenWorldSize(Random random, MultiplayerPlan plan) {
		if (worldSize.Selected < save.WorldSizes.Count) {
			return save.WorldSizes[worldSize.Selected];
		}
		int civs = 1 + plan.opponents.Count + plan.hotseatPlayers.Count + plan.guestSeats;
		List<WorldSize> roomy = save.WorldSizes.Where(w => w.numberOfCivs >= civs).ToList();
		List<WorldSize> pool = roomy.Count > 0 ? roomy : save.WorldSizes;
		return pool[random.Next(pool.Count)];
	}

	// Remembers this screen's choices for next time and Quick Start, as
	// the world and player setup screens do.
	private void RememberChoices(MultiplayerGameType type) {
		try {
			choices.WorldSize = worldSize.Selected < save.WorldSizes.Count ? save.WorldSizes[worldSize.Selected].name : NewGameChoices.Random;
			choices.Barbarians = barbarians.Selected < BarbarianLevels.Length ? BarbarianLevels[barbarians.Selected] : null;
			choices.Landform = Landforms[landMass.Selected];
			choices.OceanCoverage = Oceans[waterCoverage.Selected];
			choices.Climate = Climates[climate.Selected];
			choices.Temperature = Temperatures[temperature.Selected];
			choices.Age = Ages[age.Selected];
			choices.Civilization = civNames[hostCivilization.Selected];
			choices.Difficulty = save.Difficulties[difficulty.Selected].Name;
			MultiplayerSlots.RememberSlots(choices, type, slots.Take(rows.Count).ToList());
			choices.TurnTimerMinutes = LanLobby.TurnTimeChoices[turnTimer.Selected];
			choices.SetVictoryConditions(victoryConditions);
			choices.ShowScoreboard = showScoreboard.ButtonPressed;
			// Not the four rules multiplayer sets by default (see MakeGameRules),
			// which would otherwise change the single-player setup's.
			choices.Save();
		} catch (Exception e) {
			log.Error(e, "Could not remember the multiplayer setup");
		}
	}

	private void SetLaunching(bool on) {
		launching = on;
		launchButton.Disabled = on;
		renameButton.Disabled = on || !CanRename(selectedRow);
	}

	private void LaunchFailed(Exception e) {
		log.Error(e, "Couldn't create the game");
		if (!IsInstanceValid(this) || !IsInsideTree()) {
			return;
		}
		SetLaunching(false);
		AddMessage($"Couldn't create the game: {e.Message}", ProblemColor);
		// The rules were changed making the world; start again from fresh.
		LoadRules(mdjRules);
	}

	private void StartGame() {
		if (!IsInstanceValid(this) || !IsInsideTree()) {
			return;
		}
		try {
			LanSession.StartGame(GetTree());
		} catch (Exception e) {
			LaunchFailed(e);
		}
	}

	private void BackToMenu() {
		StopHosting();
		LanSession.HostNextGame = false;
		LanSession.PendingGame = null;
		GetTree().ChangeSceneToFile("res://UIElements/MainMenu/main_menu.tscn");
	}
}

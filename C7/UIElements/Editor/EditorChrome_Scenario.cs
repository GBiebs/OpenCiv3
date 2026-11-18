using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine.Editor;
using C7GameData;
using Godot;

// Civ3's Scenario Properties (screen_scenario.png): its tabs, the Scenario
// tab first. The dialog changes a copy of the settings; OK and Apply hand
// it to the editor (apply returns why it couldn't, or null).
public partial class EditorChrome {
	private const string NotUsedYet = "Kept with the scenario; the game doesn't use it yet.";
	private static readonly string[] Months = ["January", "February", "March", "April", "May", "June", "July", "August",
		"September", "October", "November", "December"];

	public Control ScenarioPropertiesDialog(ScenarioSettings start, ScenarioLists lists, Func<ScenarioSettings, string> apply, Action close) {
		ScenarioSettings settings = start.Copy();
		List<string> civilizations = lists.Civilizations;
		const int width = 1000, height = 690;
		TextureRect window = Popup.CreateBackground(width, height);
		window.CustomMinimumSize = new Vector2(width, height);
		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "margin_left", "margin_right" }) {
			margin.AddThemeConstantOverride(side, 24);
		}
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		window.AddChild(margin);
		VBoxContainer box = new();
		box.AddThemeConstantOverride("separation", 6);
		margin.AddChild(box);
		box.AddChild(SetupStyle.MakeHeading(this, "SCENARIO PROPERTIES", 18));

		TabContainer tabs = new() { Name = "Tabs", SizeFlagsVertical = SizeFlags.ExpandFill };
		StyleTabs(tabs);
		box.AddChild(tabs);
		tabs.AddChild(ScenarioTab(settings, civilizations));
		Action refreshPoints = null;
		tabs.AddChild(VictoryPointsTab(settings, out refreshPoints));
		Action refreshAlliances = null;
		tabs.AddChild(AlliancesTab(settings, out refreshAlliances));
		tabs.AddChild(PlayersTab(settings, lists));
		tabs.AddChild(DisastersTab(settings));
		// A field is greyed out while its victory is off on the Scenario
		// tab; the alliances name the players as the Players tab has them.
		tabs.TabChanged += _ => {
			refreshPoints();
			refreshAlliances();
		};

		Label problem = InkLabel("");
		problem.Name = "Problem";
		problem.AddThemeColorOverride("font_color", SetupStyle.ProblemColor);
		box.AddChild(problem);
		HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center };
		buttons.AddThemeConstantOverride("separation", 8);
		bool Apply() {
			problem.Text = apply(settings) ?? "";
			return problem.Text == "";
		}
		foreach ((string text, Action action) in new (string, Action)[] {
			("OK", () => { if (Apply()) { close(); } }), ("Cancel", close), ("Apply", () => Apply()) }) {
			Button button = TextButton(text, null, action);
			button.Name = text;
			button.CustomMinimumSize = new Vector2(84, 26);
			buttons.AddChild(button);
		}
		box.AddChild(buttons);
		return Backdrop(window, 0.25f);
	}

	// In a scroll box, for windows too small for all of it.
	private Control ScenarioTab(ScenarioSettings s, List<string> civilizations) {
		ScrollContainer scroll = new() { Name = "Scenario", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		VBoxContainer tab = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		scroll.AddChild(tab);
		tab.AddThemeConstantOverride("separation", 6);
		HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 18);
		tab.AddChild(columns);

		// Title, description, search folders; map options.
		VBoxContainer left = new() { CustomMinimumSize = new Vector2(300, 0) };
		left.AddThemeConstantOverride("separation", 4);
		left.AddChild(InkLabel("Title:"));
		LineEdit title = new() { Name = "Title", Text = s.Title };
		StyleField(title);
		title.TextChanged += t => s.Title = t;
		left.AddChild(title);
		left.AddChild(InkLabel("Description:"));
		TextEdit description = new() { Name = "Description", Text = s.Description, CustomMinimumSize = new Vector2(0, 120), WrapMode = TextEdit.LineWrappingMode.Boundary };
		StyleField(description);
		description.TextChanged += () => s.Description = description.Text;
		left.AddChild(description);
		left.AddChild(InkLabel("Scenario Search Folders:"));
		LineEdit folders = new() { Text = s.SearchFolders, TooltipText = "Folders under the Scenarios folder, separated by semicolons, searched for art first." };
		StyleField(folders);
		folders.TextChanged += t => s.SearchFolders = t;
		left.AddChild(folders);
		left.AddChild(Rule());
		left.AddChild(InkLabel("Select barbarian activity:"));
		OptionButton barbarians = SetupStyle.MakeDropdown();
		SetupStyle.AddItems(barbarians, Barbarians.Select(b => b.label).ToList(), Math.Max(0, Array.FindIndex(Barbarians, b => b.value == s.Barbarians)));
		barbarians.ItemSelected += i => s.Barbarians = Barbarians[(int)i].value;
		left.AddChild(barbarians);
		CheckBox debug = Check("Debug Mode", s.DebugMode);
		debug.TooltipText = "The whole map shown and Shift+F1's unit placing, for testing the scenario.";
		debug.Toggled += on => s.DebugMode = on;
		left.AddChild(debug);
		columns.AddChild(left);

		// Players.
		VBoxContainer middle = new() { CustomMinimumSize = new Vector2(230, 0) };
		middle.AddThemeConstantOverride("separation", 4);
		HBoxContainer number = new();
		number.AddChild(InkLabel("Number of Players:"));
		SpinBox players = Spin(s.NumberOfPlayers, 0, 16);
		players.Name = "NumberOfPlayers";
		players.TooltipText = "The players the scenario is made for, 1 to 16 (0: as the world size has).";
		players.ValueChanged += v => s.NumberOfPlayers = (int)v;
		number.AddChild(players);
		middle.AddChild(number);
		middle.AddChild(InkLabel("Playable Civilizations (none ticked: all):"));
		ScrollContainer civScroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 260) };
		VBoxContainer civList = new() { Name = "PlayableCivs" };
		foreach (string civ in civilizations) {
			CheckBox check = Check(civ, s.PlayableCivs.Contains(civ));
			check.Toggled += on => {
				s.PlayableCivs.Remove(civ);
				if (on) {
					s.PlayableCivs.Add(civ);
				}
			};
			civList.AddChild(check);
		}
		civScroll.AddChild(civList);
		middle.AddChild(civScroll);
		columns.AddChild(middle);

		// Game options.
		VBoxContainer right = new() { CustomMinimumSize = new Vector2(330, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		right.AddThemeConstantOverride("separation", 2);
		right.AddChild(InkLabel("Victory Conditions:"));
		GridContainer victories = new() { Columns = 2 };
		void Option(Container into, string name, bool on, Action<bool> set, string tooltip = null) {
			CheckBox check = Check(name, on);
			check.Name = name;
			check.TooltipText = tooltip;
			check.Toggled += on => set(on);
			into.AddChild(check);
		}
		Option(victories, "Domination", s.Domination, on => s.Domination = on);
		Option(victories, "Space Race", s.SpaceRace, on => s.SpaceRace = on);
		Option(victories, "Diplomatic", s.Diplomatic, on => s.Diplomatic = on);
		Option(victories, "Conquest", s.Conquest, on => s.Conquest = on);
		Option(victories, "Cultural", s.Cultural, on => s.Cultural = on);
		Option(victories, "Wonder", s.Wonder, on => s.Wonder = on);
		right.AddChild(victories);
		right.AddChild(InkLabel("Game Rules:"));
		ScrollContainer ruleScroll = new() { CustomMinimumSize = new Vector2(0, 150) };
		VBoxContainer rules = new();
		Option(rules, "Preserve Random Seed", s.PreserveRandomSeed, on => s.PreserveRandomSeed = on, "A game loaded part way through goes on with the random numbers it was saved with (off: new ones each load).");
		Option(rules, "Accelerated Production", s.AcceleratedProduction, on => s.AcceleratedProduction = on);
		Option(rules, "City Elimination", s.CityElimination, on => s.CityElimination = on);
		Option(rules, "Regicide", s.Regicide, on => s.Regicide = on);
		Option(rules, "Regicide (all Kings)", s.MassRegicide, on => s.MassRegicide = on);
		Option(rules, "Victory Point Scoring", s.VictoryPointScoring, on => s.VictoryPointScoring = on);
		Option(rules, "Capture the Unit", s.CaptureTheFlag, on => s.CaptureTheFlag = on);
		Option(rules, "Reverse Capture the Flag", s.ReverseCaptureTheFlag, on => s.ReverseCaptureTheFlag = on);
		Option(rules, "Civ-Specific Abilities", s.CivSpecificAbilities, on => s.CivSpecificAbilities = on, "Civilizations have their traits (off: none has any).");
		Option(rules, "Culturally-Linked Start", s.CulturallyLinkedStart, on => s.CulturallyLinkedStart = on, "Civilizations of a culture group start near each other on a generated map.");
		Option(rules, "Respawn AI Players", s.RespawnAiPlayers, on => s.RespawnAiPlayers = on, "A computer civilization destroyed comes back as one no player has been, on free land.");
		Option(rules, "Allow Cultural Conversion", s.CulturalConversion, on => s.CulturalConversion = on);
		Option(rules, "Scientific Leaders", s.ScientificLeaders, on => s.ScientificLeaders = on);
		ruleScroll.AddChild(rules);
		right.AddChild(ruleScroll);
		right.AddChild(Rule());
		Option(right, "Auto Place King Units", s.AutoPlaceKings, on => s.AutoPlaceKings = on);
		Option(right, "Auto Place Capture Units", s.AutoPlaceCaptureUnits, on => s.AutoPlaceCaptureUnits = on, NotUsedYet);
		Option(right, "Auto Place Victory Locations", s.AutoPlaceVictoryLocations, on => s.AutoPlaceVictoryLocations = on);
		Option(right, "Reveal Entire Map", s.RevealMap, on => s.RevealMap = on, "Every player knows the whole map from the start.");
		Option(right, "Retain Culture on Capture", s.RetainCulture, on => s.RetainCulture = on, "A captured city keeps its culture for its captor.");
		columns.AddChild(right);

		tab.AddChild(Rule());
		tab.AddChild(TimeOptions(s));
		return scroll;
	}

	// Civ3's Victory Point Limits tab: points per action, winning
	// conditions (each greyed out while its victory is off), capture the
	// flag. refresh greys them as the Scenario tab now has them.
	private Control VictoryPointsTab(ScenarioSettings s, out Action refresh) {
		VBoxContainer tab = new() { Name = "Victory Point Limits" };
		tab.AddThemeConstantOverride("separation", 8);
		List<(SpinBox box, Func<bool> on)> fields = [];
		GridContainer grid = new() { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 20);
		grid.AddThemeConstantOverride("v_separation", 4);
		void Number(string label, int value, int max, Action<int> set, Func<bool> on, string tooltip = null) {
			grid.AddChild(InkLabel(label));
			SpinBox spin = Spin(value, 0, max);
			spin.Name = label;
			spin.TooltipText = tooltip;
			spin.ValueChanged += v => set((int)v);
			grid.AddChild(spin);
			fields.Add((spin, on));
		}
		grid.AddChild(InkLabel("Points per action").WithFontSize(15));
		grid.AddChild(InkLabel(""));
		Number("Wonder (cost x multiple / 10):", s.WonderPoints, 9999, v => s.WonderPoints = v, () => s.VictoryPointScoring);
		Number("Unit killed (cost / 10 x multiple):", s.DefeatPoints, 9999, v => s.DefeatPoints = v, () => s.VictoryPointScoring);
		Number("Advance (cost x multiple):", s.AdvancePoints, 9999, v => s.AdvancePoints = v, () => s.VictoryPointScoring);
		Number("City captured (population x multiple):", s.CityConquestPoints, 9999, v => s.CityConquestPoints = v, () => s.VictoryPointScoring);
		Number("Victory location held, a turn:", s.LocationPoints, 9999, v => s.LocationPoints = v, () => s.VictoryPointScoring);
		grid.AddChild(InkLabel("Winning conditions").WithFontSize(15));
		grid.AddChild(InkLabel(""));
		Number("Victory points:", s.VictoryPointLimit, 999999, v => s.VictoryPointLimit = v, () => s.VictoryPointScoring);
		Number("Culture of one city:", s.CultureOneCity, 9999999, v => s.CultureOneCity = v, () => s.Cultural);
		Number("Culture of the civilization:", s.CultureAllCities, 9999999, v => s.CultureAllCities = v, () => s.Cultural);
		Number("Domination, % of the land:", s.DominationLand, 100, v => s.DominationLand = v, () => s.Domination);
		Number("Domination, % of the population:", s.DominationPopulation, 100, v => s.DominationPopulation = v, () => s.Domination);
		tab.AddChild(grid);
		tab.AddChild(Rule());
		tab.AddChild(InkLabel("Capture the Flag").WithFontSize(15));
		CheckBox anyone = Check("Allow anyone to capture any flag", s.AnyoneCapturesAnyFlag);
		anyone.TooltipText = NotUsedYet;
		anyone.Toggled += on => s.AnyoneCapturesAnyFlag = on;
		tab.AddChild(anyone);
		CheckBox respawn = Check("Respawn flag unit on capture", s.RespawnFlagUnits);
		respawn.TooltipText = NotUsedYet;
		respawn.Toggled += on => s.RespawnFlagUnits = on;
		tab.AddChild(respawn);
		refresh = () => {
			foreach ((SpinBox box, Func<bool> on) in fields) {
				box.Editable = on();
				box.Modulate = on() ? Colors.White : new Color(1, 1, 1, 0.5f);
			}
		};
		refresh();
		return tab;
	}

	// Civ3's Time Options: the limit, the base unit, the start date, the
	// time scale and the campaign.
	private Control TimeOptions(ScenarioSettings s) {
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 22);

		VBoxContainer limit = new();
		CheckBox useLimit = Check("Time Limit", s.UseTurnLimit);
		useLimit.Toggled += on => s.UseTurnLimit = on;
		limit.AddChild(useLimit);
		SpinBox turns = Spin(s.TurnLimit, 1, 9999);
		turns.ValueChanged += v => s.TurnLimit = (int)v;
		limit.AddChild(LabeledColumn("Turns:", turns));
		SpinBox minutes = Spin(s.TimeLimitMinutes, 0, 9999);
		minutes.TooltipText = "The game ends after this many minutes of play, as at the turn limit (0: no limit).";
		minutes.ValueChanged += v => s.TimeLimitMinutes = (int)v;
		limit.AddChild(LabeledColumn("Minutes:", minutes));
		SpinBox campaign = Spin(s.Campaign, 0, 99);
		campaign.TooltipText = NotUsedYet;
		campaign.ValueChanged += v => s.Campaign = (int)v;
		limit.AddChild(LabeledColumn("Campaign:", campaign));
		row.AddChild(limit);

		VBoxContainer date = new();
		date.AddChild(InkLabel("Base unit of time:"));
		OptionButton unit = SetupStyle.MakeDropdown();
		TimeUnit[] units = [TimeUnit.Years, TimeUnit.Months, TimeUnit.Weeks];
		SetupStyle.AddItems(unit, ["Years", "Months", "Weeks"], Math.Max(0, Array.IndexOf(units, s.BaseUnit)));
		unit.ItemSelected += i => s.BaseUnit = units[(int)i];
		date.AddChild(unit);
		date.AddChild(InkLabel("Start Date:"));
		HBoxContainer year = new();
		SpinBox yearNumber = Spin(Math.Abs(s.StartYear), 0, 999999);
		OptionButton era = SetupStyle.MakeDropdown();
		SetupStyle.AddItems(era, ["BC", "AD"], s.StartYear < 0 ? 0 : 1);
		void SetYear() => s.StartYear = era.Selected == 0 ? -(int)yearNumber.Value : (int)yearNumber.Value;
		yearNumber.ValueChanged += _ => SetYear();
		era.ItemSelected += _ => SetYear();
		year.AddChild(InkLabel("Year:"));
		year.AddChild(yearNumber);
		year.AddChild(era);
		date.AddChild(year);
		OptionButton month = SetupStyle.MakeDropdown();
		SetupStyle.AddItems(month, Months, Math.Clamp(s.StartMonth - 1, 0, 11));
		month.ItemSelected += i => s.StartMonth = (int)i + 1;
		date.AddChild(LabeledColumn("Month:", month));
		SpinBox week = Spin(s.StartWeek, 1, 52);
		week.ValueChanged += v => s.StartWeek = (int)v;
		date.AddChild(LabeledColumn("Week:", week));
		row.AddChild(date);

		VBoxContainer scale = new();
		scale.AddChild(InkLabel("Time Scale:"));
		GridContainer grid = new() { Columns = 5 };
		grid.AddThemeConstantOverride("v_separation", 0);
		for (int i = 0; i < 7; ++i) {
			int segment = i;
			grid.AddChild(InkLabel(i == 0 ? "First" : "Next"));
			SpinBox t = Spin(s.ScaleTurns[i], 0, 9999);
			t.ValueChanged += v => s.ScaleTurns[segment] = (int)v;
			grid.AddChild(t);
			grid.AddChild(InkLabel("turns ="));
			SpinBox u = Spin(s.ScaleUnits[i], 0, 9999);
			u.ValueChanged += v => s.ScaleUnits[segment] = (int)v;
			grid.AddChild(u);
			grid.AddChild(InkLabel("units each"));
		}
		scale.AddChild(grid);
		row.AddChild(scale);
		return row;
	}
}

internal static class LabelSizes {
	public static Label WithFontSize(this Label label, int size) {
		label.AddThemeFontSizeOverride("font_size", size);
		return label;
	}
}

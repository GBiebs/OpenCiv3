using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine;

// The strings for each action correspond to values in project.godot for keyboard shortcuts
public static class C7Action {
	public const string EndTurn = "end_turn";
	public const string EndTurnNow = "end_turn_now"; // even with units left to move
	public const string SaveGame = "save_game";
	public const string LoadGame = "load_game";
	public const string ShowPreferences = "show_preferences"; // Ctrl-P
	public const string Escape = "escape";

	public const string MoveUnitSouthwest = "move_unit_southwest";
	public const string MoveUnitSouth = "move_unit_south";
	public const string MoveUnitSoutheast = "move_unit_southeast";
	public const string MoveUnitWest = "move_unit_west";
	public const string MoveUnitEast = "move_unit_east";
	public const string MoveUnitNorthwest = "move_unit_northwest";
	public const string MoveUnitNorth = "move_unit_north";
	public const string MoveUnitNortheast = "move_unit_northeast";

	public const string ToggleAnimations = "toggle_animations";
	public const string EnableTempAnimations = "enable_temp_animations";
	public const string ToggleGrid = "toggle_grid";
	public const string ToggleCoordinates = "toggle_coordinates";
	public const string ToggleZoom = "toggle_zoom";

	public const string ShowDomesticAdvisor = "show_domestic_advisor"; // F1
	public const string ShowTradeAdvisor = "show_trade_advisor";
	public const string ShowMilitaryAdvisor = "show_military_advisor";
	public const string ShowForeignAdvisor = "show_foreign_advisor";
	public const string ShowCulturalAdvisor = "show_cultural_advisor";
	public const string ShowScienceAdvisor = "show_science_advisor"; // F6

	// Civ3's hidden developer advisor (Ctrl+Shift+Alt+S, no input action).
	public const string ShowSorenAdvisor = "show_soren_advisor";

	// The Espionage screen (the "E" button, no input action).
	public const string ShowEspionage = "show_espionage";

	public const string ShowWondersView = "show_wonders_view"; // F7
	public const string ShowVictoryStatusView = "show_victory_status_view";
	public const string ShowPalaceView = "show_palace_view";
	public const string ShowSpaceRaceView = "show_space_race_view";
	public const string ShowDemographicsView = "show_demographics_view"; // F11

	// The advisors and the other report screens, in the order of their keys
	// (F1 to F11), as the advisors menu lists them.
	public static readonly (string action, string name, bool isAdvisor)[] AdvisorScreens = [
		(ShowDomesticAdvisor, "Domestic Advisor", true),
		(ShowTradeAdvisor, "Trade Advisor", true),
		(ShowMilitaryAdvisor, "Military Advisor", true),
		(ShowForeignAdvisor, "Foreign Advisor", true),
		(ShowCulturalAdvisor, "Cultural Advisor", true),
		(ShowScienceAdvisor, "Science Advisor", true),
		(ShowWondersView, "Wonders of the World", false),
		(ShowVictoryStatusView, "Victory Status", false),
		(ShowPalaceView, "Palace", false),
		(ShowSpaceRaceView, "Space Race", false),
		(ShowDemographicsView, "Demographics", false),
	];

	// The keys that also open the advisor and report screens on a Mac, with
	// Cmd held, in the order of AdvisorScreens (see C7's MacShortcuts).
	public static readonly string[] MacAdvisorScreenKeys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-"];

	// The name of an advisor or report screen with its key, e.g.
	// "Trade Advisor (F2)", or on a Mac "Trade Advisor (⌘2)".
	public static string AdvisorScreenLabel(int index, bool mac = false) =>
		mac ? $"{AdvisorScreens[index].name} (⌘{MacAdvisorScreenKeys[index]})" : $"{AdvisorScreens[index].name} (F{index + 1})";

	public const string UnitBombard = "unit_bombard";
	public const string UnitBuildCity = "unit_build_city";
	public const string UnitBuildRoad = "unit_build_road";
	public const string UnitBuildMine = "unit_build_mine";
	public const string UnitIrrigate = "unit_irrigate";
	public const string UnitBuildRailroad = "unit_build_railroad";
	public const string UnitBuildFortress = "unit_build_fortress";
	public const string UnitPlantForest = "unit_plant_forest";
	public const string UnitClearForest = "unit_clear_forest";
	public const string UnitClearWetlands = "unit_clear_wetlands";
	public const string UnitClearDamage = "unit_clear_damage";
	public const string UnitBuildAirfield= "unit_build_airfield";
	public const string UnitBuildRadarTower = "unit_build_radar_tower";
	public const string UnitBuildOutpost = "unit_build_outpost";
	public const string UnitBuildColony = "unit_build_colony";
	public const string UnitJoinCity = "unit_join_city";
	public const string UnitBuildBarricade = "unit_build_barricade";
	public const string UnitAutomate = "unit_automate";
	// Civ3's other kinds of worker automation (see WorkerAutomation), with
	// labels.txt's keys.
	public const string UnitRoadTo = "unit_road_to"; // Ctrl-R
	public const string UnitRailTo = "unit_rail_to"; // Ctrl-Shift-R
	public const string UnitAutoTradeNetwork = "unit_auto_trade_network"; // Ctrl-N
	public const string UnitAutoIrrigateToCity = "unit_auto_irrigate_to_city"; // Ctrl-I
	public const string UnitAutoClearForest = "unit_auto_clear_forest"; // Shift-F
	public const string UnitAutoClearWetlands = "unit_auto_clear_wetlands"; // Shift-W
	public const string UnitAutoClearDamage = "unit_auto_clear_damage"; // Shift-D
	public const string UnitAutoCityOnly = "unit_auto_city_only"; // Shift-I
	public const string UnitAutoNoAltering = "unit_auto_no_altering"; // Shift-A
	public const string UnitAutoCityNoAltering = "unit_auto_city_no_altering"; // Ctrl-Shift-I
	public const string UnitDisband = "unit_disband";
	public const string UnitExplore = "unit_explore";
	public const string UnitFortify = "unit_fortify";
	public const string UnitGoto = "unit_goto";
	// Civ3's Go to City (Ctrl-Shift-G), handled with the other G keys in
	// Game.HandleKeyboardInput.
	public const string UnitGotoCity = "unit_goto_city";
	public const string UnitHold = "unit_hold";
	public const string UnitSentry = "unit_sentry";
	public const string UnitSentryEnemyOnly = "unit_sentry_enemy_only";
	public const string UnitWait = "unit_wait";
	public const string UnitLoad = "unit_load";
	public const string UnitUnload = "unit_unload";
	public const string UnitUpgrade = "unit_upgrade";
	public const string UnitPillage = "unit_pillage";
	public const string UnitBuildArmy = "unit_build_army";
	public const string UnitHurryBuilding = "unit_hurry_building";
	public const string UnitScienceAge = "unit_science_age";
	public const string UnitSacrifice = "unit_sacrifice";
	// Air missions and the airdrop (see MapUnit_Air.cs).
	public const string UnitBombing = "unit_bombing";
	public const string UnitPrecisionBombing = "unit_precision_bombing";
	public const string UnitRecon = "unit_recon";
	public const string UnitAirSuperiority = "unit_air_superiority";
	public const string UnitRebase = "unit_rebase";
	public const string UnitAirdrop = "unit_airdrop";
	public const string UnitAirlift = "unit_airlift";
	// Automated bombarding (see MapUnit_AutoBombard.cs): Shift-B, which an
	// aircraft takes as its bombing, and its buttons. Shift-P, for
	// automated precision bombing, is the pillage key (see Game).
	public const string UnitAutoBombard = "unit_auto_bombard";
	public const string UnitAutoBombing = "unit_auto_bombing";
	public const string UnitAutoPrecisionBombing = "unit_auto_precision_bombing";

	private static readonly Dictionary<string, UnitAction> toUnitAction = new() {
		[UnitBuildCity] = UnitAction.BuildCity,
		[UnitBombard] = UnitAction.Bombard,
		[UnitHold] = UnitAction.Hold,
		[UnitLoad] = UnitAction.Load,
		[UnitUnload] = UnitAction.Unload,
		[UnitUpgrade] = UnitAction.Upgrade,
		[UnitPillage] = UnitAction.Pillage,
		[UnitBuildArmy] = UnitAction.BuildArmy,
		[UnitHurryBuilding] = UnitAction.HurryBuilding,
		[UnitScienceAge] = UnitAction.ScienceAge,
		[UnitSacrifice] = UnitAction.Sacrifice,
		[UnitBuildColony] = UnitAction.BuildColony,
		[UnitJoinCity] = UnitAction.JoinCity,
		[UnitSentry] = UnitAction.Sentry,
		[UnitSentryEnemyOnly] = UnitAction.SentryEnemyOnly,
		[UnitWait] = UnitAction.Wait,
		[UnitFortify] = UnitAction.Fortify,
		[UnitDisband] = UnitAction.Disband,
		[UnitGoto] = UnitAction.Goto,
		[UnitExplore] = UnitAction.Explore,
		[UnitAutomate] = UnitAction.Automate,
		[UnitBombing] = UnitAction.Bombing,
		[UnitPrecisionBombing] = UnitAction.PrecisionBombing,
		[UnitRecon] = UnitAction.Recon,
		[UnitAirSuperiority] = UnitAction.Intercept,
		[UnitRebase] = UnitAction.Rebase,
		[UnitAirdrop] = UnitAction.Airdrop,
		[UnitAirlift] = UnitAction.Airlift,
		[UnitAutoBombard] = UnitAction.AutoBombard,
		[UnitAutoBombing] = UnitAction.AutoBombing,
		[UnitAutoPrecisionBombing] = UnitAction.AutoPrecisionBombing,
	};

	private static readonly Dictionary<string, string> toTooltip = new() {
		[UnitBombard] = "Bombard",
		[UnitBuildCity] = "Build City",
		[UnitBuildRoad] = "Build Road",
		[UnitBuildMine] = "Build Mine",
		[UnitIrrigate] = "Irrigate",
		[UnitBuildRailroad] = "Build Railroad",
		[UnitBuildFortress] = "Build Fortress",
		[UnitPlantForest] = "Plant Forest",
		[UnitClearForest] = "Clear Forest",
		[UnitClearWetlands] = "Clear Wetlands",
		[UnitClearDamage] = "Clear Damage",
		[UnitBuildAirfield] = "Build Airfield",
		[UnitBuildRadarTower] = "Build Radar Tower",
		[UnitBuildOutpost] = "Build Outpost",
		[UnitBuildColony] = "Build Colony",
		// labels.txt's "Join City (B)" (#UNIT_ACTIONS).
		[UnitJoinCity] = "Join City",
		[UnitBuildBarricade] = "Build Barricade",
		[UnitAutomate] = "Automate",
		// labels.txt's names (#UNIT_ACTIONS).
		[UnitRoadTo] = "Road to",
		[UnitRailTo] = "Rail to",
		[UnitAutoTradeNetwork] = "Automate: Build Trade",
		[UnitAutoIrrigateToCity] = "Automate: Irrigate to Nearest City",
		[UnitAutoClearForest] = "Automate: Clear Forest",
		[UnitAutoClearWetlands] = "Automate: Clear Wetlands",
		[UnitAutoClearDamage] = "Automate: Clear Damage",
		[UnitAutoCityOnly] = "Automate: This City Only",
		[UnitAutoNoAltering] = "Automate: No Altering",
		[UnitAutoCityNoAltering] = "Automate: No Altering/This City Only",
		[UnitDisband] = "Disband",
		[UnitExplore] = "Explore",
		[UnitFortify] = "Fortify",
		[UnitGoto] = "Goto",
		[UnitHold] = "Hold",
		[UnitSentry] = "Sentry",
		[UnitSentryEnemyOnly] = "Sentry Enemy Only",
		[UnitWait] = "Wait",
		[UnitLoad] = "Load",
		[UnitUnload] = "Unload",
		[UnitUpgrade] = "Upgrade",
		[UnitPillage] = "Pillage",
		[UnitBuildArmy] = "Build Army",
		[UnitHurryBuilding] = "Hurry Improvement",
		// labels.txt's "Increase Scientific Output" (#UNIT_ACTIONS).
		[UnitScienceAge] = "Increase Scientific Output",
		// labels.txt's "Sacrifice" (#UNIT_ACTIONS), which has no key.
		[UnitSacrifice] = "Sacrifice",
		// Civ3's names for the air missions (GCON_Air_Missions).
		[UnitBombing] = "Bombing Mission",
		[UnitPrecisionBombing] = "Precision Strike",
		[UnitRecon] = "Recon Mission",
		[UnitAirSuperiority] = "Air Superiority",
		[UnitRebase] = "Re-Base",
		[UnitAirdrop] = "Airdrop",
		// labels.txt's "Airlif(T): Choose new airbase".
		[UnitAirlift] = "Airlift",
		// labels.txt's "Automate:^Bombard (Shift-B)", "Automate:^Bombing
		// (Shift-B)" and "Automate:^Precision Bombing (Shift-P)".
		[UnitAutoBombard] = "Automate: Bombard (Shift-B)",
		[UnitAutoBombing] = "Automate: Bombing (Shift-B)",
		[UnitAutoPrecisionBombing] = "Automate: Precision Bombing (Shift-P)",
	};

	// The unit actions that are aimed at a tile on the map.
	public static bool IsTargetedAirMission(UnitAction action) {
		return action is UnitAction.Bombing or UnitAction.PrecisionBombing or UnitAction.Recon
			or UnitAction.Rebase or UnitAction.Airdrop or UnitAction.Airlift;
	}

	// The worker automation commands, in labels.txt's order (#UNIT_ACTIONS),
	// and the kind each gives.
	public static readonly (string action, WorkerAutomation.Kind kind)[] WorkerAutomations = [
		(UnitRoadTo, WorkerAutomation.Kind.RoadTo),
		(UnitRailTo, WorkerAutomation.Kind.RailTo),
		(UnitAutoTradeNetwork, WorkerAutomation.Kind.TradeNetwork),
		(UnitAutoIrrigateToCity, WorkerAutomation.Kind.IrrigateToCity),
		(UnitAutoClearForest, WorkerAutomation.Kind.ClearForest),
		(UnitAutoClearWetlands, WorkerAutomation.Kind.ClearWetlands),
		(UnitAutoClearDamage, WorkerAutomation.Kind.ClearDamage),
		(UnitAutoCityOnly, WorkerAutomation.Kind.ThisCityOnly),
		(UnitAutoNoAltering, WorkerAutomation.Kind.NoAltering),
		(UnitAutoCityNoAltering, WorkerAutomation.Kind.ThisCityNoAltering),
	];

	public static WorkerAutomation.Kind? ToWorkerAutomation(string action) {
		foreach ((string a, WorkerAutomation.Kind kind) in WorkerAutomations) {
			if (a == action) {
				return kind;
			}
		}
		return null;
	}

	// Whether the unit can be given the kind of worker automation: a worker
	// that can automate, and for road and rail to, build them.
	public static bool CanAutomate(MapUnit unit, WorkerAutomation.Kind kind) {
		if (unit == null || !unit.unitType.isWorker || !unit.canAutomate()) {
			return false;
		}
		string job = kind switch {
			WorkerAutomation.Kind.RoadTo => UnitBuildRoad,
			WorkerAutomation.Kind.RailTo => UnitBuildRailroad,
			_ => null,
		};
		if (job == null) {
			return true;
		}
		Terraform terraform = ToTerraform(job);
		return terraform != null && unit.unitType.terraformActions.Contains(terraform) && unit.owner.HasTech(terraform.RequiredTech);
	}

	public static UnitAction? ToUnitAction(string action) {
		return toUnitAction.TryGetValue(action, out var result) ? result : null;
	}

	public static string ToActionString(UnitAction unitAction) {
		return toUnitAction.FirstOrDefault(kv => kv.Value == unitAction).Key;
	}

	// This method transforms an action string into a TileDirection.
	// A null value will be returned if the conversion is unsuccessful.
	public static TileDirection? ToTileDirection(string action) {
		return action switch {
			MoveUnitSouthwest => TileDirection.SOUTHWEST,
			MoveUnitSouth => TileDirection.SOUTH,
			MoveUnitSoutheast => TileDirection.SOUTHEAST,
			MoveUnitWest => TileDirection.WEST,
			MoveUnitEast => TileDirection.EAST,
			MoveUnitNorthwest => TileDirection.NORTHWEST,
			MoveUnitNorth => TileDirection.NORTH,
			MoveUnitNortheast => TileDirection.NORTHEAST,
			_ => null,
		};
	}

	public static Terraform? ToTerraform(string action) {
		return EngineStorage.gameData.Terraforms.FirstOrDefault(tf => tf.UIAction == action);
	}

	public static string? ToTooltip(string action) {
		return toTooltip.TryGetValue(action, out var result) ? result : null;
	}
}

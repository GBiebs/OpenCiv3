using System.Collections.Generic;
using System.Linq;
using C7GameData;

namespace C7Engine.Editor;

// Everything Civ3's Scenario Properties' Scenario tab sets
// (screen_scenario.png), read from the scenario and the game data it's
// kept in, and given back as one undo step (MapPainter.EditScenario).
public sealed record ScenarioSettings {
	public string Title = "", Description = "", SearchFolders = "";
	public BarbarianActivity Barbarians = BarbarianActivity.Roaming;
	public bool DebugMode;

	public int NumberOfPlayers;
	// Empty: every civilization is playable.
	public List<string> PlayableCivs = [];

	// The victory conditions the scenario allows (a player may turn them off
	// at setup, not on).
	public bool Domination, SpaceRace, Diplomatic, Conquest, Cultural, Wonder;

	// The game rules it allows, Civ3's list.
	public bool PreserveRandomSeed, AcceleratedProduction, CityElimination, Regicide, MassRegicide, VictoryPointScoring,
		CaptureTheFlag, ReverseCaptureTheFlag, CivSpecificAbilities, CulturallyLinkedStart, RespawnAiPlayers,
		CulturalConversion, ScientificLeaders;

	public bool AutoPlaceKings, AutoPlaceCaptureUnits, AutoPlaceVictoryLocations, RevealMap, RetainCulture;

	// Time options: the limit (turns and minutes), the base unit, the start
	// date (a year below 0 is BC), the 7 segments of the time scale (turns,
	// units a turn), Conquests' campaign number.
	public bool UseTurnLimit;
	public int TurnLimit, TimeLimitMinutes;
	public TimeUnit BaseUnit = TimeUnit.Years;
	public int StartYear = -4000, StartMonth = 1, StartWeek = 1;
	public int[] ScaleTurns = new int[7], ScaleUnits = new int[7];
	public int Campaign;

	public static ScenarioSettings Of(GameData gameData, ScenarioInfo info) {
		VictoryConditions vc = gameData.victoryConditions ?? new VictoryConditions();
		Rules rules = gameData.rules;
		TimeOptions time = gameData.timeOptions ?? new TimeOptions();
		int[,] scale = time.timeScale;
		return new ScenarioSettings {
			Title = info.title ?? "",
			Description = info.description ?? "",
			SearchFolders = gameData.scenarioSearchPath ?? "",
			Barbarians = gameData.barbarianInfo.barbarianActivity,
			DebugMode = gameData.debugMode,
			NumberOfPlayers = info.numberOfPlayers,
			PlayableCivs = [.. info.playableCivs],
			Domination = vc.AllowDominationVictory,
			SpaceRace = vc.AllowSpaceRaceVictory,
			Diplomatic = vc.AllowDiplomaticVictory,
			Conquest = vc.AllowConquestVictory,
			Cultural = vc.AllowCulturalVictory,
			Wonder = vc.AllowWonderVictory,
			PreserveRandomSeed = info.preserveRandomSeed,
			AcceleratedProduction = rules.AcceleratedProduction,
			CityElimination = vc.CityElimination,
			Regicide = vc.Regicide,
			MassRegicide = vc.MassRegicide,
			VictoryPointScoring = vc.VictoryLocations,
			CaptureTheFlag = vc.CaptureTheFlag,
			ReverseCaptureTheFlag = vc.ReverseCaptureTheFlag,
			CivSpecificAbilities = info.civSpecificAbilities,
			CulturallyLinkedStart = info.culturallyLinkedStart,
			RespawnAiPlayers = info.respawnAiPlayers,
			CulturalConversion = rules.AllowCultureFlips,
			ScientificLeaders = rules.ScientificLeaders,
			AutoPlaceKings = vc.AutoPlaceKings,
			AutoPlaceCaptureUnits = info.autoPlaceCaptureUnits,
			AutoPlaceVictoryLocations = vc.AutoPlaceVictoryLocations,
			RevealMap = info.revealMap,
			RetainCulture = info.retainCulture,
			UseTurnLimit = vc.UseTurnLimit,
			TurnLimit = time.turnLimit,
			TimeLimitMinutes = info.timeLimitMinutes,
			BaseUnit = time.baseUnit,
			StartYear = time.startYear,
			StartMonth = time.startMonth,
			StartWeek = time.startWeek,
			ScaleTurns = Enumerable.Range(0, 7).Select(i => i < scale.GetLength(1) ? scale[0, i] : 0).ToArray(),
			ScaleUnits = Enumerable.Range(0, 7).Select(i => i < scale.GetLength(1) ? scale[1, i] : 0).ToArray(),
			Campaign = info.campaign,
		};
	}

	// The civilizations a scenario's players may be, for Playable
	// Civilizations (the barbarians aside), in the rules' order.
	public static List<string> CivilizationNames(GameData gameData) {
		return gameData.civilizations.Where(c => !c.isBarbarian).Select(c => c.name).ToList();
	}

	// Why the settings can't be the scenario's, or null.
	public string Problem() {
		if (NumberOfPlayers < 0 || NumberOfPlayers > 16) {
			return "A scenario is for 1 to 16 players.";
		}
		if (UseTurnLimit && TurnLimit < 1) {
			return "The time limit needs at least 1 turn.";
		}
		if (ScaleTurns.Any(t => t < 0) || ScaleUnits.Any(u => u < 0)) {
			return "The time scale's numbers can't be below 0.";
		}
		return null;
	}

	public void ApplyTo(GameData gameData, ScenarioInfo info) {
		info.title = Title;
		info.description = Description;
		gameData.scenarioSearchPath = string.IsNullOrWhiteSpace(SearchFolders) ? null : SearchFolders;
		gameData.barbarianInfo.barbarianActivity = Barbarians;
		gameData.debugMode = DebugMode;
		info.numberOfPlayers = NumberOfPlayers;
		info.playableCivs = [.. PlayableCivs];

		gameData.victoryConditions ??= new VictoryConditions();
		VictoryConditions vc = gameData.victoryConditions;
		vc.AllowDominationVictory = Domination;
		vc.AllowSpaceRaceVictory = SpaceRace;
		vc.AllowDiplomaticVictory = Diplomatic;
		vc.AllowConquestVictory = Conquest;
		vc.AllowCulturalVictory = Cultural;
		vc.AllowWonderVictory = Wonder;
		vc.CityElimination = CityElimination;
		vc.Regicide = Regicide;
		vc.MassRegicide = MassRegicide;
		vc.VictoryLocations = VictoryPointScoring;
		vc.CaptureTheFlag = CaptureTheFlag;
		vc.ReverseCaptureTheFlag = ReverseCaptureTheFlag;
		vc.AutoPlaceKings = AutoPlaceKings;
		vc.AutoPlaceVictoryLocations = AutoPlaceVictoryLocations;
		vc.UseTurnLimit = UseTurnLimit;
		gameData.rules.AcceleratedProduction = AcceleratedProduction;
		gameData.rules.AllowCultureFlips = CulturalConversion;
		gameData.rules.ScientificLeaders = ScientificLeaders;
		info.preserveRandomSeed = PreserveRandomSeed;
		info.civSpecificAbilities = CivSpecificAbilities;
		info.culturallyLinkedStart = CulturallyLinkedStart;
		info.respawnAiPlayers = RespawnAiPlayers;
		info.autoPlaceCaptureUnits = AutoPlaceCaptureUnits;
		info.revealMap = RevealMap;
		info.retainCulture = RetainCulture;
		info.timeLimitMinutes = TimeLimitMinutes;
		info.campaign = Campaign;

		TimeOptions old = gameData.timeOptions ?? new TimeOptions();
		int[,] scale = new int[2, 8];
		for (int i = 0; i < 7; ++i) {
			scale[0, i] = ScaleTurns[i];
			scale[1, i] = ScaleUnits[i];
		}
		// After the last segment, a time unit a turn (TimeOptions).
		scale[0, 7] = 50000;
		scale[1, 7] = 1;
		gameData.timeOptions = new TimeOptions {
			baseUnit = BaseUnit,
			negativeLabel = old.negativeLabel,
			positiveLabel = old.positiveLabel,
			startYear = StartYear,
			startMonth = StartMonth,
			startWeek = StartWeek,
			startDay = old.startDay,
			startHour = old.startHour,
			turnLimit = TurnLimit,
			timeScale = scale,
		};
	}
}

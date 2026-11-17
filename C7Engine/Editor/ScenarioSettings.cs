using System;
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

	// Civ3's Victory Point Limits tab: points per action (a wonder's cost x
	// the multiple / 10, a unit killed's cost / 10 x the multiple, an
	// advance's cost x the multiple, a captured city's population x the
	// multiple, a victory location held a turn), the winning conditions
	// (victory points, a city's culture, the civ's culture, domination's
	// land and population %), and the capture-the-flag options.
	public int WonderPoints, DefeatPoints, AdvancePoints, CityConquestPoints, LocationPoints;
	public int VictoryPointLimit, CultureOneCity, CultureAllCities, DominationLand, DominationPopulation;
	public bool AnyoneCapturesAnyFlag, RespawnFlagUnits;

	// Civ3's Locked Alliances tab: four named alliances, the players in
	// each (by player ID; a player in none is left out), which alliances
	// start locked at war (pairs of 1 to 4, the smaller first), and the
	// victory type (Coalition: allies win together).
	public string[] AllianceNames = ["Alliance 1", "Alliance 2", "Alliance 3", "Alliance 4"];
	public Dictionary<string, int> AllianceOf = [];
	public List<(int, int)> AllianceWars = [];
	public bool CoalitionVictory;

	// Civ3's Disasters tab: the plague (the times in years, the variance
	// a % of the grace period, the strength 0 to 100) and the volcano
	// eruption period in years.
	public bool PermitPlagues;
	public string PlagueName = "Plague";
	public int PlagueEarliestStart, PlagueVariance, PlagueDuration, PlagueStrength, PlagueGracePeriod, PlagueMaxOccurrences;
	public int VolcanoEruptionPeriod;

	// Civ3's Player Properties, a player each (barbarians aside).
	public List<PlayerSettings> Players = [];

	// A copy whose lists and arrays are its own, for a dialog to change.
	public ScenarioSettings Copy() => this with {
		PlayableCivs = [.. PlayableCivs],
		ScaleTurns = [.. ScaleTurns],
		ScaleUnits = [.. ScaleUnits],
		AllianceNames = [.. AllianceNames],
		AllianceOf = new(AllianceOf),
		AllianceWars = [.. AllianceWars],
		Players = Players.Select(p => p.Copy()).ToList(),
	};

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
			WonderPoints = vc.WonderVictoryPoints,
			DefeatPoints = vc.DefeatVictoryPoints,
			AdvancePoints = vc.AdvanceVictoryPoints,
			CityConquestPoints = vc.CityConquestVictoryPoints,
			LocationPoints = vc.VictoryLocationPoints,
			VictoryPointLimit = vc.VictoryPointLimit,
			CultureOneCity = vc.CultureOneCityWin,
			CultureAllCities = vc.CultureAllCitiesWin,
			DominationLand = vc.DominationTerritoryPercent,
			DominationPopulation = vc.DominationPopulationPercent,
			AnyoneCapturesAnyFlag = info.anyoneCapturesAnyFlag,
			RespawnFlagUnits = info.respawnFlagUnits,
			AllianceNames = AllianceNamesOf(gameData),
			AllianceOf = gameData.players.Where(p => p.alliance != null && !p.IsIndependent)
				.ToDictionary(p => p.id.ToString(), p => p.alliance.index),
			AllianceWars = gameData.allianceWars.Where(w => w.Key != null && w.Value != null)
				.Select(w => (Math.Min(w.Key.index, w.Value.index), Math.Max(w.Key.index, w.Value.index)))
				.Where(w => w.Item1 != w.Item2).Distinct().ToList(),
			CoalitionVictory = rules.TeamVictory,
			PermitPlagues = rules.PermitPlagues,
			PlagueName = rules.PlagueName ?? "Plague",
			PlagueEarliestStart = rules.PlagueEarliestStart,
			PlagueVariance = rules.PlagueVariance,
			PlagueDuration = rules.PlagueDuration,
			PlagueStrength = rules.PlagueStrength,
			PlagueGracePeriod = rules.PlagueGracePeriod,
			PlagueMaxOccurrences = rules.PlagueMaxOccurrences,
			VolcanoEruptionPeriod = rules.VolcanoEruptionPeriod,
			Players = gameData.players.Where(p => !p.IsIndependent && p.civilization != null)
				.Select(p => PlayerSettings.Of(gameData, p, info.players.Find(s => s.player == p.id.ToString()))).ToList(),
		};
	}

	private static string[] AllianceNamesOf(GameData gameData) {
		string[] names = ["Alliance 1", "Alliance 2", "Alliance 3", "Alliance 4"];
		foreach (Alliance alliance in gameData.alliances ?? []) {
			if (alliance.index is >= 1 and <= 4 && !string.IsNullOrWhiteSpace(alliance.name)) {
				names[alliance.index - 1] = alliance.name;
			}
		}
		return names;
	}

	// The game's difficulty levels, eras and governments, for the Players
	// tab's lists.
	public static List<string> DifficultyNames(GameData gameData) => gameData.difficulties.Select(d => d.Name).ToList();
	public static List<string> GovernmentNames(GameData gameData) => gameData.governments.Select(g => g.name).ToList();
	public static List<string> TechNames(GameData gameData) => gameData.techs.Select(t => t.Name).ToList();
	public static List<string> UnitTypeNames(GameData gameData) => gameData.unitPrototypes.Select(u => u.name).ToList();
	public static readonly string[] EraNames = ["Ancient Times", "Middle Ages", "Industrial Age", "Modern Era"];

	// The civilization's own leader, gender, colour and free advances
	// (Civilization Defaults).
	public static (string leader, Gender gender, int color, List<string> techs) CivilizationDefaults(GameData gameData, string civilization) {
		Civilization civ = gameData.civilizations.Find(c => c.name == civilization);
		if (civ == null) {
			return ("", Gender.Male, 0, []);
		}
		return (civ.leader ?? "", civ.leaderGender, civ.primaryColorIndex,
			gameData.techs.Where(t => civ.startingTechs.Contains(t.id)).Select(t => t.Name).ToList());
	}

	// The alliances anew, each player in its own (or none), and the wars
	// between them.
	private void ApplyAlliances(GameData gameData) {
		List<Alliance> alliances = Enumerable.Range(1, 4).Select(i => new Alliance(i, AllianceNames[i - 1].Trim())).ToList();
		gameData.alliances = [.. alliances];
		foreach (Player player in gameData.players.Where(p => !p.IsIndependent)) {
			player.alliance = AllianceOf.TryGetValue(player.id.ToString(), out int index) ? alliances[index - 1] : null;
		}
		gameData.allianceWars = AllianceWars.Distinct().Select(w => new KeyValuePair<Alliance, Alliance>(alliances[w.Item1 - 1], alliances[w.Item2 - 1])).ToList();
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
		if (new[] { WonderPoints, DefeatPoints, AdvancePoints, CityConquestPoints, LocationPoints, VictoryPointLimit, CultureOneCity, CultureAllCities }.Any(n => n < 0)) {
			return "Victory points and culture can't be below 0.";
		}
		if (DominationLand is < 1 or > 100 || DominationPopulation is < 1 or > 100) {
			return "Domination's shares are 1 to 100%.";
		}
		if (AllianceNames.Length != 4 || AllianceNames.Any(string.IsNullOrWhiteSpace)) {
			return "Each alliance needs a name.";
		}
		if (AllianceNames.Select(n => n.Trim()).Distinct().Count() != 4) {
			return "Two alliances can't have the same name.";
		}
		if (AllianceOf.Values.Any(a => a is < 1 or > 4) || AllianceWars.Any(w => w.Item1 is < 1 or > 4 || w.Item2 is < 1 or > 4 || w.Item1 == w.Item2)) {
			return "There are four alliances, and one can't be at war with itself.";
		}
		if (PermitPlagues && string.IsNullOrWhiteSpace(PlagueName)) {
			return "The plague needs a name.";
		}
		if (PlagueStrength is < 0 or > 100) {
			return "The plague's strength is 0 to 100.";
		}
		if (new[] { PlagueVariance, PlagueDuration, PlagueGracePeriod, PlagueMaxOccurrences, VolcanoEruptionPeriod }.Any(n => n < 0)) {
			return "The disasters' numbers can't be below 0.";
		}
		foreach (PlayerSettings player in Players) {
			if (player.Problem() is string why) {
				return why;
			}
		}
		string twice = Players.Where(p => p.Choice == CivChoice.Fixed).GroupBy(p => p.Civilization).FirstOrDefault(g => g.Count() > 1)?.Key;
		if (twice != null) {
			return $"{twice} can be only one player's civilization.";
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
		vc.WonderVictoryPoints = WonderPoints;
		vc.DefeatVictoryPoints = DefeatPoints;
		vc.AdvanceVictoryPoints = AdvancePoints;
		vc.CityConquestVictoryPoints = CityConquestPoints;
		vc.VictoryLocationPoints = LocationPoints;
		vc.VictoryPointLimit = VictoryPointLimit;
		vc.CultureOneCityWin = CultureOneCity;
		vc.CultureAllCitiesWin = CultureAllCities;
		vc.DominationTerritoryPercent = DominationLand;
		vc.DominationPopulationPercent = DominationPopulation;
		info.anyoneCapturesAnyFlag = AnyoneCapturesAnyFlag;
		info.respawnFlagUnits = RespawnFlagUnits;

		ApplyAlliances(gameData);
		Rules rules = gameData.rules;
		rules.TeamVictory = CoalitionVictory;
		rules.PermitPlagues = PermitPlagues;
		rules.PlagueName = PlagueName;
		rules.PlagueEarliestStart = PlagueEarliestStart;
		rules.PlagueVariance = PlagueVariance;
		rules.PlagueDuration = PlagueDuration;
		rules.PlagueStrength = PlagueStrength;
		rules.PlagueGracePeriod = PlagueGracePeriod;
		rules.PlagueMaxOccurrences = PlagueMaxOccurrences;
		rules.VolcanoEruptionPeriod = VolcanoEruptionPeriod;
		foreach (PlayerSettings player in Players) {
			player.ApplyTo(gameData, info);
		}

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

// Civ3's Player Properties for one player: its civilization (or Any or
// Random), whether a human may play it, Civilization Defaults (else its
// own leader name, gender, team colour and free advances), difficulty
// (null: Any), government, starting era (0 to 3), treasury, starting
// units, Skip 1st Turn and Start with Embassies.
public sealed record PlayerSettings {
	public string Id = "";
	public CivChoice Choice;
	public string Civilization = "";
	public bool Human;
	public bool CivDefaults;
	public string LeaderName = "";
	public Gender LeaderGender;
	public int Color;
	public string Difficulty;
	public string Government = "";
	public int Era;
	public int Treasury;
	public List<StartUnit> StartUnits = [];
	public List<string> FreeTechs = [];
	public bool SkipFirstTurn, StartWithEmbassies;

	public PlayerSettings Copy() => this with {
		StartUnits = StartUnits.Select(u => new StartUnit { type = u.type, count = u.count }).ToList(),
		FreeTechs = [.. FreeTechs],
	};

	public static PlayerSettings Of(GameData gameData, Player player, PlayerSlot slot) {
		Civilization civ = player.civilization;
		return new PlayerSettings {
			Id = player.id.ToString(),
			Choice = slot?.civ ?? CivChoice.Fixed,
			Civilization = civ.name,
			Human = player.canBePicked,
			CivDefaults = slot?.civDefaults ?? false,
			LeaderName = slot?.leaderName ?? civ.leader ?? "",
			LeaderGender = slot?.leaderGender ?? civ.leaderGender,
			Color = player.primaryColorIndex,
			Difficulty = slot?.difficulty,
			Government = player.government?.name ?? "",
			Era = Math.Max(0, Player.EraIndex(player.eraCivilopediaName)),
			Treasury = player.gold,
			StartUnits = slot?.startUnits.Select(u => new StartUnit { type = u.type, count = u.count }).ToList() ?? [],
			FreeTechs = gameData.techs.Where(t => player.knownTechs.Contains(t.id)).Select(t => t.Name).ToList(),
			SkipFirstTurn = player.skipFirstTurn,
			StartWithEmbassies = slot?.startWithEmbassies ?? false,
		};
	}

	public string Problem() {
		if (Treasury < 0) {
			return $"{Civilization}'s treasury can't be below 0.";
		}
		if (!CivDefaults && string.IsNullOrWhiteSpace(LeaderName)) {
			return $"{Civilization}'s leader needs a name.";
		}
		if (StartUnits.Any(u => u.count is < 1 or > 99 || string.IsNullOrEmpty(u.type))) {
			return $"{Civilization}'s starting units are 1 to 99 of a type.";
		}
		return null;
	}

	public void ApplyTo(GameData gameData, ScenarioInfo info) {
		Player player = gameData.players.Find(p => p.id.ToString() == Id);
		if (player == null) {
			return;
		}
		if (gameData.civilizations.Find(c => c.name == Civilization) is Civilization civ && civ != player.civilization) {
			ScenarioPlayers.ChangeCivilization(gameData, player, civ);
		}
		player.canBePicked = Human;
		player.gold = Treasury;
		player.skipFirstTurn = SkipFirstTurn;
		player.eraCivilopediaName = Player.EraIndexToEra(Era);
		if (gameData.governments.Find(g => g.name == Government) is Government government) {
			player.government = government;
		}
		(string leader, Gender gender, int color, List<string> techs) = ScenarioSettings.CivilizationDefaults(gameData, Civilization);
		// The civilization's own free advances are always known (the game
		// adds them as it loads a player), so Free Techs adds to them.
		List<string> known = CivDefaults ? techs : [.. FreeTechs.Union(techs)];
		player.knownTechs = gameData.techs.Where(t => known.Contains(t.Name)).Select(t => t.id).ToHashSet();
		player.primaryColorIndex = CivDefaults ? color : Color;

		info.players.RemoveAll(s => s.player == Id);
		PlayerSlot slot = new() {
			player = Id,
			civ = Choice,
			civDefaults = CivDefaults,
			leaderName = CivDefaults || LeaderName == leader ? null : LeaderName.Trim(),
			leaderGender = CivDefaults || LeaderGender == gender ? null : LeaderGender,
			difficulty = Difficulty,
			startUnits = StartUnits.Select(u => new StartUnit { type = u.type, count = u.count }).ToList(),
			startWithEmbassies = StartWithEmbassies,
		};
		// A player with nothing beyond its own data needs no slot.
		if (slot.civ != CivChoice.Fixed || slot.civDefaults || slot.leaderName != null || slot.leaderGender != null
				|| slot.difficulty != null || slot.startUnits.Count > 0 || slot.startWithEmbassies) {
			info.players.Add(slot);
		}
	}
}

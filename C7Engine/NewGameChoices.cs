using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine;

// What the player chose when last setting up a new game, kept in the
// settings file's [LastGame] section. The setup screens start from these,
// and Quick Start plays them again on a new map.
//
// Choices are kept by name, so ones the current ruleset doesn't have (a
// civilization, world size or difficulty only Civ3 mode or standalone mode
// has) fall back to the defaults when used. A null choice was never made,
// and the screens and Quick Start use their own default for it.
public class NewGameChoices {
	// What a random choice is saved as.
	public const string Random = "Random";

	public const string DefaultCivilization = "Netherlands";
	public const string DefaultDifficulty = "Regent";

	// The world.
	// A world size's name, Random, or null.
	public string WorldSize;
	// Null when Random.
	public BarbarianActivity? Barbarians = BarbarianActivity.Roaming;
	public WorldCharacteristics.Landform Landform = WorldCharacteristics.Landform.Pangaea;
	public WorldCharacteristics.OceanCoverage OceanCoverage = WorldCharacteristics.OceanCoverage.Percent_70;
	public WorldCharacteristics.Climate Climate = WorldCharacteristics.Climate.Normal;
	public WorldCharacteristics.Temperature Temperature = WorldCharacteristics.Temperature.Temperate;
	public WorldCharacteristics.Age Age = WorldCharacteristics.Age.Billion_4;

	// The players.
	public string Civilization;
	public string Difficulty;
	// The first human player's name in a hotseat game, or null for the
	// default.
	public string PlayerName;
	// For each rival's slot, in order, the civilization chosen for it or
	// Random. Slots past the end are Random.
	public List<string> Opponents = [];
	// The slots played by another person on this computer (hotseat), with
	// their names ("" for the default name).
	public SortedDictionary<int, string> HotseatSlots = [];
	// The slots left for people joining over the LAN, when hosting.
	public SortedSet<int> LanSlots = [];
	// The slots set to Closed on the Multiplayer Setup screen: no civ plays
	// them, so the game has fewer civs.
	public SortedSet<int> ClosedSlots = [];
	// The slots the Advanced AI plays when the computer plays them; the
	// Standard AI plays the rest.
	public SortedSet<int> AdvancedAiSlots = [];
	// The Multiplayer Setup screen's game type (see MultiplayerGameType),
	// and its turn timer in minutes (0 for none); null if never chosen.
	public string MultiplayerGameType;
	public int? TurnTimerMinutes;

	// The rules. Null for those never chosen, which take the ruleset's or
	// the screen's default.
	public bool? ConquestVictory;
	public bool? DominationVictory;
	public bool? SpaceRaceVictory;
	public bool? DiplomaticVictory;
	public bool? CulturalVictory;
	public bool? TurnLimit;
	public bool? WonderVictory;
	public bool? VictoryPointScoring;
	public bool? Elimination;
	public bool? Regicide;
	public bool? MassRegicide;
	public bool? ShowScoreboard;
	public bool? CoreCitiesFreeOfCorruption;
	public bool? AcceleratedProduction;
	public bool? CultureFlips;
	public bool? ScientificLeaders;
	public float? CorruptionRate;

	private static class Keys {
		public const string WorldSize = C7Settings.LastGame.WorldSize;
		public const string Barbarians = C7Settings.LastGame.BarbarianActivity;
		public const string Landform = C7Settings.LastGame.Landform;
		public const string OceanCoverage = C7Settings.LastGame.OceanCoverage;
		public const string Climate = C7Settings.LastGame.Climate;
		public const string Temperature = C7Settings.LastGame.Temperature;
		public const string Age = C7Settings.LastGame.Age;
		public const string Civilization = C7Settings.LastGame.Civilization;
		public const string Difficulty = C7Settings.LastGame.Difficulty;
		public const string Opponents = C7Settings.LastGame.Opponents;
		public const string CoreCitiesFreeOfCorruption = C7Settings.LastGame.CoreCitiesFreeOfCorruption;
		public const string PlayerName = nameof(PlayerName);
		public const string HotseatSlots = nameof(HotseatSlots);
		public const string HotseatNames = nameof(HotseatNames);
		public const string LanSlots = nameof(LanSlots);
		public const string ClosedSlots = nameof(ClosedSlots);
		public const string AdvancedAiSlots = nameof(AdvancedAiSlots);
		public const string MultiplayerGameType = nameof(MultiplayerGameType);
		public const string TurnTimerMinutes = nameof(TurnTimerMinutes);
		public const string ConquestVictory = nameof(ConquestVictory);
		public const string DominationVictory = nameof(DominationVictory);
		public const string SpaceRaceVictory = nameof(SpaceRaceVictory);
		public const string DiplomaticVictory = nameof(DiplomaticVictory);
		public const string CulturalVictory = nameof(CulturalVictory);
		public const string TurnLimit = nameof(TurnLimit);
		public const string WonderVictory = nameof(WonderVictory);
		public const string VictoryPointScoring = nameof(VictoryPointScoring);
		public const string Elimination = nameof(Elimination);
		public const string Regicide = nameof(Regicide);
		public const string MassRegicide = nameof(MassRegicide);
		public const string ShowScoreboard = nameof(ShowScoreboard);
		public const string AcceleratedProduction = nameof(AcceleratedProduction);
		public const string CultureFlips = nameof(CultureFlips);
		public const string ScientificLeaders = nameof(ScientificLeaders);
		public const string CorruptionRate = nameof(CorruptionRate);
	}

	// The choices saved in the settings file.
	public static NewGameChoices Load() {
		return Read(key => C7Settings.GetSettingsValueOrDefault(C7Settings.LastGame.SectionName, key, null));
	}

	// Saves these choices to the settings file.
	public void Save() {
		Write((key, value) => {
			if (value == null) {
				C7Settings.RemoveValue(C7Settings.LastGame.SectionName, key);
			} else {
				C7Settings.SetValue(C7Settings.LastGame.SectionName, key, value);
			}
		});
		C7Settings.SaveSettings();
	}

	// The choices from the given saved values, null for one not saved.
	// Values that can't be read are left at their defaults.
	public static NewGameChoices Read(Func<string, string> get) {
		NewGameChoices choices = new();
		choices.WorldSize = NonEmpty(get(Keys.WorldSize));
		string barbarians = get(Keys.Barbarians);
		if (barbarians == Random) {
			choices.Barbarians = null;
		} else {
			choices.Barbarians = ParseEnum(barbarians, BarbarianActivity.Roaming);
		}
		choices.Landform = ParseEnum(get(Keys.Landform), choices.Landform);
		choices.OceanCoverage = ParseEnum(get(Keys.OceanCoverage), choices.OceanCoverage);
		choices.Climate = ParseEnum(get(Keys.Climate), choices.Climate);
		choices.Temperature = ParseEnum(get(Keys.Temperature), choices.Temperature);
		choices.Age = ParseEnum(get(Keys.Age), choices.Age);

		choices.Civilization = NonEmpty(get(Keys.Civilization));
		choices.Difficulty = NonEmpty(get(Keys.Difficulty));
		choices.PlayerName = NonEmpty(get(Keys.PlayerName));
		choices.Opponents = SplitList(get(Keys.Opponents)).Select(o => o == "" ? Random : o).ToList();
		List<int> hotseatSlots = SplitList(get(Keys.HotseatSlots)).Select(ParseSlot).ToList();
		List<string> hotseatNames = SplitList(get(Keys.HotseatNames)).ToList();
		for (int i = 0; i < hotseatSlots.Count; ++i) {
			if (hotseatSlots[i] >= 0) {
				choices.HotseatSlots[hotseatSlots[i]] = i < hotseatNames.Count ? hotseatNames[i] : "";
			}
		}
		choices.LanSlots = new(SplitList(get(Keys.LanSlots)).Select(ParseSlot).Where(s => s >= 0));
		choices.ClosedSlots = new(SplitList(get(Keys.ClosedSlots)).Select(ParseSlot).Where(s => s >= 0));
		choices.AdvancedAiSlots = new(SplitList(get(Keys.AdvancedAiSlots)).Select(ParseSlot).Where(s => s >= 0));
		choices.MultiplayerGameType = NonEmpty(get(Keys.MultiplayerGameType));
		int timer = ParseSlot(get(Keys.TurnTimerMinutes));
		choices.TurnTimerMinutes = timer >= 0 ? timer : null;

		choices.ConquestVictory = ParseBool(get(Keys.ConquestVictory));
		choices.DominationVictory = ParseBool(get(Keys.DominationVictory));
		choices.SpaceRaceVictory = ParseBool(get(Keys.SpaceRaceVictory));
		choices.DiplomaticVictory = ParseBool(get(Keys.DiplomaticVictory));
		choices.CulturalVictory = ParseBool(get(Keys.CulturalVictory));
		choices.TurnLimit = ParseBool(get(Keys.TurnLimit));
		choices.WonderVictory = ParseBool(get(Keys.WonderVictory));
		choices.VictoryPointScoring = ParseBool(get(Keys.VictoryPointScoring));
		choices.Elimination = ParseBool(get(Keys.Elimination));
		choices.Regicide = ParseBool(get(Keys.Regicide));
		choices.MassRegicide = ParseBool(get(Keys.MassRegicide));
		choices.ShowScoreboard = ParseBool(get(Keys.ShowScoreboard));
		choices.CoreCitiesFreeOfCorruption = ParseBool(get(Keys.CoreCitiesFreeOfCorruption));
		choices.AcceleratedProduction = ParseBool(get(Keys.AcceleratedProduction));
		choices.CultureFlips = ParseBool(get(Keys.CultureFlips));
		choices.ScientificLeaders = ParseBool(get(Keys.ScientificLeaders));
		if (float.TryParse(get(Keys.CorruptionRate), NumberStyles.Float, CultureInfo.InvariantCulture, out float rate)
			&& float.IsFinite(rate)) {
			choices.CorruptionRate = Math.Clamp(rate, Rules.MinCorruptionRate, Rules.MaxCorruptionRate);
		}
		return choices;
	}

	// Writes these choices as values to save, null for one to forget.
	public void Write(Action<string, string> set) {
		set(Keys.WorldSize, WorldSize);
		set(Keys.Barbarians, Barbarians?.ToString() ?? Random);
		set(Keys.Landform, Landform.ToString());
		set(Keys.OceanCoverage, OceanCoverage.ToString());
		set(Keys.Climate, Climate.ToString());
		set(Keys.Temperature, Temperature.ToString());
		set(Keys.Age, Age.ToString());

		set(Keys.Civilization, Civilization);
		set(Keys.Difficulty, Difficulty);
		set(Keys.PlayerName, PlayerName);
		set(Keys.Opponents, JoinList(Opponents));
		set(Keys.HotseatSlots, JoinList(HotseatSlots.Keys.Select(s => s.ToString(CultureInfo.InvariantCulture))));
		set(Keys.HotseatNames, JoinList(HotseatSlots.Values));
		set(Keys.LanSlots, JoinList(LanSlots.Select(s => s.ToString(CultureInfo.InvariantCulture))));
		set(Keys.ClosedSlots, JoinList(ClosedSlots.Select(s => s.ToString(CultureInfo.InvariantCulture))));
		set(Keys.AdvancedAiSlots, JoinList(AdvancedAiSlots.Select(s => s.ToString(CultureInfo.InvariantCulture))));
		set(Keys.MultiplayerGameType, MultiplayerGameType);
		set(Keys.TurnTimerMinutes, TurnTimerMinutes?.ToString(CultureInfo.InvariantCulture));

		set(Keys.ConquestVictory, FormatBool(ConquestVictory));
		set(Keys.DominationVictory, FormatBool(DominationVictory));
		set(Keys.SpaceRaceVictory, FormatBool(SpaceRaceVictory));
		set(Keys.DiplomaticVictory, FormatBool(DiplomaticVictory));
		set(Keys.CulturalVictory, FormatBool(CulturalVictory));
		set(Keys.TurnLimit, FormatBool(TurnLimit));
		set(Keys.WonderVictory, FormatBool(WonderVictory));
		set(Keys.VictoryPointScoring, FormatBool(VictoryPointScoring));
		set(Keys.Elimination, FormatBool(Elimination));
		set(Keys.Regicide, FormatBool(Regicide));
		set(Keys.MassRegicide, FormatBool(MassRegicide));
		set(Keys.ShowScoreboard, FormatBool(ShowScoreboard));
		set(Keys.CoreCitiesFreeOfCorruption, FormatBool(CoreCitiesFreeOfCorruption));
		set(Keys.AcceleratedProduction, FormatBool(AcceleratedProduction));
		set(Keys.CultureFlips, FormatBool(CultureFlips));
		set(Keys.ScientificLeaders, FormatBool(ScientificLeaders));
		set(Keys.CorruptionRate, CorruptionRate?.ToString("0.00", CultureInfo.InvariantCulture));
	}

	// The world size chosen, if the ruleset has it, or the ruleset's
	// default; a random one for Random.
	public WorldSize ChosenWorldSize(List<WorldSize> sizes, System.Random random) {
		if (WorldSize == Random && sizes.Count > 0) {
			return sizes[random.Next(sizes.Count)];
		}
		return sizes.Find(s => string.Equals(s.name, WorldSize, StringComparison.OrdinalIgnoreCase))
			?? DefaultWorldSize(sizes);
	}

	// The world size the setup screen starts on when none was chosen: the
	// last the ruleset marks as the default.
	public static WorldSize DefaultWorldSize(List<WorldSize> sizes) {
		return sizes.LastOrDefault(s => s.isDefault) ?? sizes.FirstOrDefault() ?? C7GameData.WorldSize.Generic();
	}

	// The barbarian activity chosen; a random one for Random.
	public BarbarianActivity ChosenBarbarians(System.Random random) {
		if (Barbarians is BarbarianActivity barbarians) {
			return barbarians;
		}
		BarbarianActivity[] all = Enum.GetValues<BarbarianActivity>();
		return all[random.Next(all.Length)];
	}

	// The civilization chosen to play, if the ruleset has it, or the
	// default one.
	// Whether the player chose to play a random civilization.
	public bool ChoseRandomCivilization => Civilization == Random;

	// A civilization to play, at random, that isn't one of the taken ones.
	public static Civilization RandomCivilization(List<Civilization> civilizations, IEnumerable<string> taken, System.Random random) {
		HashSet<string> excluded = taken.Where(n => n != null).ToHashSet();
		List<Civilization> free = civilizations.Where(c => !c.isBarbarian && !excluded.Contains(c.name)).ToList();
		List<Civilization> pool = free.Count > 0 ? free : civilizations.Where(c => !c.isBarbarian).ToList();
		return pool[random.Next(pool.Count)];
	}

	// A scenario's default difficulty: its rules', if it has its own rules.
	public static string ScenarioDifficulty(C7GameData.Save.SaveGame save) {
		return save?.Scenario?.customRules == true ? save.Rules?.DefaultDifficulty : null;
	}

	public Civilization ChosenCivilization(List<Civilization> civilizations) {
		List<Civilization> playable = civilizations.Where(c => !c.isBarbarian).ToList();
		return playable.Find(c => c.name == Civilization)
			?? playable.Find(c => c.name == DefaultCivilization)
			?? playable.FirstOrDefault();
	}

	// The difficulty chosen, if the ruleset has it, or the default one:
	// the rules' own (Civ3's General Settings' Default Difficulty Level),
	// else Regent. A scenario's default comes first (preferred, see
	// ScenarioDifficulty), as Civ3 opens a scenario at its rules' level.
	public Difficulty ChosenDifficulty(List<Difficulty> difficulties, string preferred = null, string rulesDefault = null) {
		return difficulties.Find(d => preferred != null && d.Name == preferred)
			?? difficulties.Find(d => d.Name == Difficulty)
			?? difficulties.Find(d => rulesDefault != null && d.Name == rulesDefault)
			?? difficulties.Find(d => d.Name == DefaultDifficulty)
			?? difficulties.FirstOrDefault();
	}

	// The civilization chosen for each of the given number of rival slots,
	// or null for a random one: Random, a civilization the ruleset doesn't
	// have, or one already taken by the player or an earlier slot.
	public List<string> ChosenOpponents(int slots, List<Civilization> civilizations, string playerCivilization) {
		HashSet<string> taken = [playerCivilization];
		List<string> chosen = [];
		for (int i = 0; i < slots; ++i) {
			string name = i < Opponents.Count ? Opponents[i] : Random;
			bool usable = name != Random && civilizations.Any(c => c.name == name && !c.isBarbarian) && taken.Add(name);
			chosen.Add(usable ? name : null);
		}
		return chosen;
	}

	// The game to play from these choices on a map from the given seed,
	// as Quick Start does: the same world, rivals and rules as chosen last
	// time, with any random choices made again. Choices the ruleset
	// doesn't have fall back to its defaults. People who joined over the
	// LAN are played by the computer, and Closed slots aren't played.
	public GameSetup QuickStartGame(SaveGame save, int mapSeed, System.Random random, string invalidMapsDirectory = null) {
		WorldCharacteristics world = new(save) {
			barbarianActivity = ChosenBarbarians(random),
			landform = Landform,
			oceanCoverage = OceanCoverage,
			age = Age,
			climate = Climate,
			temperature = Temperature,
			worldSize = ChosenWorldSize(save.WorldSizes, random),
			mapSeed = mapSeed,
		};

		int slots = Math.Max(0, world.worldSize.numberOfCivs - 1);
		Civilization player = ChoseRandomCivilization ? null : ChosenCivilization(save.Civilizations);
		List<string> opponentCivs = ChosenOpponents(slots, save.Civilizations, player?.name);
		player ??= RandomCivilization(save.Civilizations, opponentCivs, random);
		List<SelectedOpponent> opponents = [];
		List<HotseatPlayer> hotseatPlayers = [];
		for (int i = 0; i < slots; ++i) {
			string civ = opponentCivs[i];
			if (ClosedSlots.Contains(i)) {
				continue;
			}
			if (HotseatSlots.TryGetValue(i, out string name)) {
				hotseatPlayers.Add(new HotseatPlayer {
					civilization = civ == null ? null : save.Civilizations.Find(c => c.name == civ),
					name = HotseatName(name, hotseatPlayers.Count + 2),
				});
			} else {
				opponents.Add(civ == null
					? new SelectedOpponent { isRandom = true, aiType = AiTypeOf(i) }
					: new SelectedOpponent { Name = civ, aiType = AiTypeOf(i) });
			}
		}

		return new GameSetup {
			playerCivilization = player,
			playerName = hotseatPlayers.Count > 0 ? HotseatName(PlayerName, 1) : null,
			hotseatPlayers = hotseatPlayers,
			difficulty = ChosenDifficulty(save.Difficulties),
			worldCharacteristics = world,
			opponents = opponents,
			victoryConditions = ChosenVictoryConditions(save.VictoryConditions),
			showScoreboard = ShowScoreboard ?? true,
			coreCitiesFreeOfCorruption = CoreCitiesFreeOfCorruption ?? false,
			acceleratedProduction = AcceleratedProduction,
			allowCultureFlips = CultureFlips,
			scientificLeaders = ScientificLeaders,
			corruptionRate = CorruptionRate,
			invalidMapsDirectory = invalidMapsDirectory,
		};
	}

	// The AI chosen for a rival's slot.
	public AiType AiTypeOf(int slot) {
		return AdvancedAiSlots.Contains(slot) ? AiType.Advanced : AiType.Standard;
	}

	// The victory conditions chosen, with the thresholds the ruleset sets.
	public VictoryConditions ChosenVictoryConditions(VictoryConditions ruleset) {
		VictoryConditions conditions = C7GameData.VictoryConditions.NewGameDefaults().CopyThresholdsFrom(ruleset);
		conditions.AllowConquestVictory = ConquestVictory ?? conditions.AllowConquestVictory;
		conditions.AllowDominationVictory = DominationVictory ?? conditions.AllowDominationVictory;
		conditions.AllowSpaceRaceVictory = SpaceRaceVictory ?? conditions.AllowSpaceRaceVictory;
		conditions.AllowDiplomaticVictory = DiplomaticVictory ?? conditions.AllowDiplomaticVictory;
		conditions.AllowCulturalVictory = CulturalVictory ?? conditions.AllowCulturalVictory;
		conditions.UseTurnLimit = TurnLimit ?? conditions.UseTurnLimit;
		conditions.AllowWonderVictory = WonderVictory ?? conditions.AllowWonderVictory;
		conditions.VictoryLocations = VictoryPointScoring ?? conditions.VictoryLocations;
		conditions.CityElimination = Elimination ?? conditions.CityElimination;
		conditions.Regicide = Regicide ?? conditions.Regicide;
		conditions.MassRegicide = MassRegicide ?? conditions.MassRegicide;
		return conditions;
	}

	// Remembers the victory conditions chosen.
	public void SetVictoryConditions(VictoryConditions conditions) {
		ConquestVictory = conditions.AllowConquestVictory;
		DominationVictory = conditions.AllowDominationVictory;
		SpaceRaceVictory = conditions.AllowSpaceRaceVictory;
		DiplomaticVictory = conditions.AllowDiplomaticVictory;
		CulturalVictory = conditions.AllowCulturalVictory;
		TurnLimit = conditions.UseTurnLimit;
		WonderVictory = conditions.AllowWonderVictory;
		VictoryPointScoring = conditions.VictoryLocations;
		Elimination = conditions.CityElimination;
		Regicide = conditions.Regicide;
		MassRegicide = conditions.MassRegicide;
	}

	// A human player's name: the one given, or "Player N" by their turn
	// order, as the player setup screen shows it.
	public static string HotseatName(string name, int playerNumber) {
		return string.IsNullOrWhiteSpace(name) ? $"Player {playerNumber}" : name.Trim();
	}

	private static string NonEmpty(string value) {
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum {
		return Enum.TryParse(value, true, out T result) && Enum.IsDefined(result) ? result : fallback;
	}

	private static bool? ParseBool(string value) {
		return bool.TryParse(value, out bool result) ? result : null;
	}

	private static string FormatBool(bool? value) {
		return value.HasValue ? (value.Value ? "true" : "false") : null;
	}

	private static int ParseSlot(string value) {
		return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot) ? slot : -1;
	}

	// Lists are kept with each item escaped, so names can hold anything,
	// including the separator and the settings file's comment characters.
	private const char ListSeparator = '|';

	// Null, to leave nothing in the settings file, when there's nothing to
	// keep.
	private static string JoinList(IEnumerable<string> items) {
		string joined = string.Join(ListSeparator, items.Select(i => Uri.EscapeDataString(i ?? "")));
		return joined == "" ? null : joined;
	}

	private static IEnumerable<string> SplitList(string value) {
		if (string.IsNullOrEmpty(value)) {
			return [];
		}
		return value.Split(ListSeparator).Select(i => Unescape(i.Trim()));
	}

	private static string Unescape(string item) {
		try {
			return Uri.UnescapeDataString(item);
		} catch (UriFormatException) {
			return item;
		}
	}
}

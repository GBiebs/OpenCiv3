using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C7GameData;
using C7GameData.Save;
using Serilog;

namespace C7Engine;

public struct SelectedOpponent {
	public bool isRandom;
	public string Name;
	// The team the civ plays on, or 0 for none (see GameSetup.Teams.cs).
	public int team;
	// Which AI plays the civ; Standard unless chosen.
	public AiType aiType;
}

// An additional human player: sharing the computer in a hotseat game, or
// joining over the LAN.
public struct HotseatPlayer {
	// Null for a random civilization no other human has.
	public Civilization civilization;
	// The person's name, or null if none was given.
	public string name;
	// The team the person plays on, or 0 for none (see GameSetup.Teams.cs).
	public int team;
}

public partial class GameSetup {
	private static ILogger log = Log.ForContext<GameSetup>();

	public Civilization playerCivilization { get; init; }
	// The first human player's name, or null if none was given.
	public string playerName { get; init; }
	// The first human player's team, or 0 for none (see GameSetup.Teams.cs).
	// A LAN host sets it once the lobby closes, as the host may change it
	// there.
	public int playerTeam { get; set; }
	// Additional human players, sharing this computer in a hotseat game or
	// joining over the LAN. They take their turns after the player above, in
	// list order. A LAN host fills these in once the guests have chosen.
	public List<HotseatPlayer> hotseatPlayers { get; set; } = [];
	public Difficulty difficulty { get; init; }
	public WorldCharacteristics worldCharacteristics { get; init; }
	public List<SelectedOpponent> opponents { get; init; } = [];
	public VictoryConditions victoryConditions { get; set; }
	public bool showScoreboard { get; init; } = true;
	public bool coreCitiesFreeOfCorruption { get; init; } = false;
	// Null keeps the setting the scenario or ruleset came with.
	public bool? acceleratedProduction { get; init; } = null;
	// Scales corruption and waste (see Rules.CorruptionRate). Null keeps
	// the rate the scenario or ruleset came with.
	public float? corruptionRate { get; init; } = null;
	// Whether cities may flip to another civ's culture (see
	// Rules.AllowCultureFlips). Null keeps the scenario or ruleset's choice.
	public bool? allowCultureFlips { get; init; } = null;
	// Whether civs may gain scientific great leaders (see
	// Rules.ScientificLeaders). Null keeps the scenario or ruleset's choice.
	public bool? scientificLeaders { get; init; } = null;
	// Where to save each generated map that can't be played, with why, so
	// it can be looked at; null not to save them.
	public string invalidMapsDirectory { get; init; } = null;
	// The scenario to set the game up as, which overrides some of the
	// choices above (see GameSetup.EternalWar.cs).
	public GameScenario scenario { get; init; } = GameScenario.None;
	// The cities each civ starts with in Eternal War; null for the default
	// for the world size (see DefaultEternalWarCitiesPerCiv).
	public int? eternalWarCitiesPerCiv { get; init; } = null;

	ID.Factory ids;

	public void Populate(SaveGame save) {
		save.GameDifficulty = difficulty;

		save.VictoryConditions = victoryConditions;
		ScaleCulturalVictory(save);
		save.Rules.ShowScoreboard = showScoreboard;
		save.Rules.CoreCitiesFreeOfCorruption = coreCitiesFreeOfCorruption;
		if (acceleratedProduction.HasValue) {
			save.Rules.AcceleratedProduction = acceleratedProduction.Value;
		}
		if (allowCultureFlips.HasValue) {
			save.Rules.AllowCultureFlips = allowCultureFlips.Value;
		}
		if (scientificLeaders.HasValue) {
			save.Rules.ScientificLeaders = scientificLeaders.Value;
		}
		if (corruptionRate.HasValue) {
			save.Rules.CorruptionRate = Math.Clamp(corruptionRate.Value, Rules.MinCorruptionRate, Rules.MaxCorruptionRate);
		}

		if (scenario == GameScenario.EternalWar) {
			SetUpEternalWarWorld(save, worldCharacteristics);
			SetUpEternalWarRules(save);
			int size = worldCharacteristics?.worldSize == null ? -1 : save.WorldSizes.FindIndex(w => w.name == worldCharacteristics.worldSize.name);
			int standard = save.WorldSizes.FindIndex(w => w.name == "Standard");
			save.Rules.MarketOffersPerTurn = Staging.OffersForWorldSize(size, standard);
		}
		if (scenario == GameScenario.RisingTide) {
			SetUpRisingTideWorld(worldCharacteristics);
			SetUpRisingTideRules(save);
		}
		if (scenario == GameScenario.KingsAndNomads) {
			SetUpKingsAndNomadsWorld(worldCharacteristics);
			SetUpKingsAndNomadsRules(save);
		}

		if (save.Map.tiles.Count == 0) {
			log.Information("Starting map generation");
			save.Map = new SaveMap(GenerateMap(save));
			save.Seed = worldCharacteristics.mapSeed;
			log.Information("Done with map generation");
		}

		if (save.Players.Count == 0) {
			ids = new(save);
			PopulatePlayers(save);
		} else {
			ids = new(save);
			PlaceMissingKings(save);
		}
	}

	// Gives the Kings of a scenario played as it is (see PlaceMissingKings).
	public static void PlaceScenarioKings(SaveGame save) {
		new GameSetup { ids = new(save) }.PlaceMissingKings(save);
	}

	// The King unit the civ is given under Regicide, or null if the rules
	// have none.
	private static SaveUnitPrototype KingUnit(SaveGame save, Civilization civ) {
		return InstantElimination.KingUnitFor(civ, save.UnitPrototypes, p => p.name, p => p.flags.Contains(SaveUnitPrototype.Flag.King));
	}

	// Gives each civ of a scenario that has no King its Kings, in its
	// capital, or with its units if it has no city, when the scenario has
	// Regicide on and its kings placed automatically.
	// UNVERIFIED (no Civ3 source found): where Civ3 puts a scenario civ's
	// Kings; the Civilopedia only says each player has them.
	private void PlaceMissingKings(SaveGame save) {
		int kings = InstantElimination.KingsToPlace(save.VictoryConditions);
		if (kings == 0) {
			return;
		}
		foreach (SavePlayer player in save.Players.ToList()) {
			Civilization civ = save.Civilizations.Find(c => c.name == player.civilization);
			if (player.IsIndependent || civ == null || civ.IsIndependent || !player.isIncludedInGame) {
				continue;
			}
			if (save.Units.Any(u => u.owner == player.id && save.UnitPrototypes.Any(p => p.name == u.prototype && p.flags.Contains(SaveUnitPrototype.Flag.King)))) {
				continue;
			}
			List<SaveCity> cities = save.Cities.Where(c => c.owner == player.id).ToList();
			SaveCity city = cities.FirstOrDefault(c => c.capital) ?? cities.FirstOrDefault();
			SaveUnit unit = save.Units.FirstOrDefault(u => u.owner == player.id);
			if (city != null || unit != null) {
				TileLocation location = city != null ? city.location : unit.currentLocation;
				AddKings(save, player, civ, location, kings);
			}
		}
	}

	private void AddKings(SaveGame save, SavePlayer player, Civilization civ, TileLocation location, int count) {
		SaveUnitPrototype king = KingUnit(save, civ);
		if (king == null) {
			log.Warning($"The rules have no King unit for {civ?.name}");
			return;
		}
		for (int i = 0; i < count; ++i) {
			AddUnit(save, player, king.name, location);
		}
	}

	// Conquests scales the culture a whole civ needs for a cultural victory
	// by the world's size (see CulturalVictory.WorldSizePercent).
	private void ScaleCulturalVictory(SaveGame save) {
		if (victoryConditions == null || worldCharacteristics?.worldSize == null) {
			return;
		}
		int size = save.WorldSizes.FindIndex(w => w.name == worldCharacteristics.worldSize.name);
		victoryConditions.CultureAllCitiesWin = CulturalVictory.ScaleForWorldSize(
			victoryConditions.CultureAllCitiesWin, size, save.WorldSizes.Count);
	}

	// How many maps to generate, each from a seed derived from the last,
	// before giving up on the settings. A map is generated again when it
	// can't be played, such as when it has no room for a starting location
	// for every civ the world size's distance between civs apart; there is
	// no fallback that puts civs closer or leaves some out.
	public const int MaxMapAttempts = 25;

	// Generates a map with a starting location for every civ, or throws if
	// none of MaxMapAttempts maps has one.
	private GameMap GenerateMap(SaveGame save) {
		int civs = HumanPlayers().Count() + opponents.Count;
		if (worldCharacteristics.worldSize.numberOfCivs < civs) {
			log.Warning($"The {worldCharacteristics.worldSize.name} world size has room for {worldCharacteristics.worldSize.numberOfCivs} civs, but {civs} are playing; making room for them all");
			worldCharacteristics.worldSize = worldCharacteristics.worldSize.Clone();
			worldCharacteristics.worldSize.numberOfCivs = civs;
		}

		// The seafaring civs picked for the game must start on the coast.
		// Random civs that turn out seafaring are swapped for others if
		// there aren't coastal starts enough (see AssignStartingLocations).
		worldCharacteristics.coastalStartsNeeded = HumanPlayers().Count(h => h.civilization != null && IsSeafaring(h.civilization))
			+ opponents.Count(o => !o.isRandom && save.Civilizations.FirstOrDefault(c => c.name == o.Name) is Civilization civ && IsSeafaring(civ));

		string problem = null;
		for (int attempt = 1; attempt <= MaxMapAttempts; ++attempt) {
			if (attempt > 1) {
				worldCharacteristics.mapSeed = NextMapSeed(worldCharacteristics.mapSeed);
			}
			GameMap map = MapGenerator.GenerateMap(worldCharacteristics, out problem);
			if (problem == null) {
				return map;
			}
			log.Warning($"The map from seed {worldCharacteristics.mapSeed} can't be played ({problem}); generating another");
			SaveInvalidMap(save, map, problem);
		}
		throw new InvalidOperationException(
			$"Couldn't generate a map with room for each of the {civs} civilizations to start " +
			$"in {MaxMapAttempts} tries ({problem}). " +
			"Try a larger world, fewer civilizations, more land or another landform.");
	}

	// The seed of the map to try after the one from the given seed.
	public static int NextMapSeed(int seed) {
		return (int)((uint)seed * 2654435761u % int.MaxValue);
	}

	// Saves a map that can't be played to invalidMapsDirectory, as a game
	// that can be loaded to look at it, with a text file saying why it was
	// turned down. Civs the map has no start for are put on any land, so
	// the save has a player to look through. Failing to save it doesn't stop
	// the game being set up.
	private void SaveInvalidMap(SaveGame save, GameMap map, string problem) {
		if (invalidMapsDirectory == null) {
			return;
		}
		try {
			SaveGame copy = save.Clone();
			copy.Map = new SaveMap(map);
			copy.Seed = worldCharacteristics.mapSeed;
			int starts = copy.Map.startingLocations.Count;
			int civs = HumanPlayers().Count() + opponents.Count;
			HashSet<(int, int)> used = copy.Map.startingLocations.Select(t => (t.X, t.Y)).ToHashSet();
			copy.Map.startingLocations.AddRange(copy.Map.tiles
				.Where(t => !used.Contains((t.X, t.Y)) && t.baseTerrain is not ("coast" or "sea" or "ocean"))
				.Take(Math.Max(0, civs - starts)));
			if (copy.Players.Count == 0 && copy.Map.startingLocations.Count >= civs) {
				ids = new(copy);
				PopulatePlayers(copy);
			}

			Directory.CreateDirectory(invalidMapsDirectory);
			string name = $"{DateTime.Now:yyyy-MM-dd HH.mm.ss} seed {worldCharacteristics.mapSeed}";
			string path = Path.Combine(invalidMapsDirectory, name);
			copy.Save(path + ".json");
			File.WriteAllText(path + ".txt",
				$"{problem}.{Environment.NewLine}" +
				$"Seed {worldCharacteristics.mapSeed}, {worldCharacteristics.worldSize.name} world " +
				$"({worldCharacteristics.worldSize.width}x{worldCharacteristics.worldSize.height}, " +
				$"distance between civs {worldCharacteristics.worldSize.distanceBetweenCivs}), {civs} civilizations, " +
				$"{worldCharacteristics.landform}, {worldCharacteristics.oceanCoverage}, {worldCharacteristics.climate}, " +
				$"{worldCharacteristics.temperature}, {worldCharacteristics.age}.{Environment.NewLine}" +
				$"The map had {starts} proper starting locations; in the save, civs without one start on any land.{Environment.NewLine}");
			log.Information($"Saved the map that can't be played to {path}.json");
		} catch (Exception e) {
			log.Warning(e, "Couldn't save the map that can't be played");
		}
	}

	private void PopulatePlayers(SaveGame save) {
		Random rand = new(worldCharacteristics.mapSeed + 0x531);

		// Add barbarian
		SavePlayer barbarians = AddPlayer(save, save.Civilizations.Find(c => c.isBarbarian), isHuman: false);
		save.BarbarianInfo.barbarianActivity = worldCharacteristics.barbarianActivity;
		AddCampDefenders(save, barbarians);

		// TODO: There is an option called "Culturally Linked Start Loc."
		// which (if on) puts players with the same culture group near each other

		// Pick the human players' civilizations, the ones they chose or a
		// random one nobody picked, then the opponents'.
		HashSet<string> taken = new();
		List<HotseatPlayer> humans = HumanPlayers().ToList();
		foreach (HotseatPlayer human in humans.Where(h => h.civilization != null)) {
			if (!taken.Add(human.civilization.name)) {
				throw new ArgumentException($"{human.civilization.name} was picked by more than one human player");
			}
		}
		List<PlannedPlayer> planned = new();
		foreach (HotseatPlayer human in humans) {
			Civilization civ = human.civilization;
			if (civ == null) {
				string name = RandomCivilization(save, rand, taken);
				taken.Add(name);
				civ = save.Civilizations.Find(c => c.name == name);
			}
			planned.Add(new PlannedPlayer { civ = civ, isHuman = true, name = human.name, isRandom = human.civilization == null, team = human.team });
		}

		foreach (SelectedOpponent opponent in opponents) {
			bool isRandom = opponent.isRandom;
			string selectedName = opponent.Name;

			if (taken.Contains(opponent.Name)) {
				isRandom = true;
			}

			if (isRandom) {
				selectedName = RandomCivilization(save, rand, taken);
			}
			taken.Add(selectedName);

			Civilization civ = save.Civilizations.Find(x => x.name == selectedName);
			planned.Add(new PlannedPlayer { civ = civ, isHuman = false, isRandom = isRandom, team = opponent.team, aiType = opponent.aiType });
		}

		if (save.Map.startingLocations.Count < planned.Count) {
			throw new InvalidOperationException($"The map has {save.Map.startingLocations.Count} starting locations, too few for {planned.Count} civilizations");
		}
		List<(SavePlayer, int)> teams = [];
		if (scenario == GameScenario.EternalWar) {
			List<List<SaveTile>> sites = DealEternalWarSites(save, planned);
			for (int i = 0; i < planned.Count; ++i) {
				SavePlayer player = AddPlayer(save, planned[i].civ, planned[i].isHuman, planned[i].name, sites[i][0], aiType: planned[i].aiType);
				SetUpEternalWarPlayer(save, player, planned[i].civ, sites[i]);
				teams.Add((player, planned[i].team));
			}
			StartEternalWar(save);
			SetUpTeams(save, teams);
			return;
		}

		List<SaveTile> starts = scenario switch {
			GameScenario.RisingTide => RisingTideStarts(save, rand, planned.Count),
			GameScenario.KingsAndNomads => SetUpKingsAndNomadsSites(save, rand, planned.Count, taken),
			_ => AssignStartingLocations(save, rand, planned, taken),
		};
		for (int i = 0; i < planned.Count; ++i) {
			SavePlayer player = AddPlayer(save, planned[i].civ, planned[i].isHuman, planned[i].name, starts[i], aiType: planned[i].aiType);
			// A Kings & Nomads AI gets none of the difficulty's extra
			// starting units, which would otherwise appear in the first
			// city-state it took (owner's ruling, 2026-10-23).
			if (scenario == GameScenario.KingsAndNomads) {
				player.receivedStartUnits = true;
			}
			teams.Add((player, planned[i].team));
		}
		SetUpTeams(save, teams);
	}

	internal class PlannedPlayer {
		public Civilization civ;
		public bool isHuman;
		public string name;
		// Whether the civilization was picked at random, so it may be
		// swapped for another.
		public bool isRandom;
		public int team;
		public AiType aiType;
	}

	private static bool IsSeafaring(Civilization civ) {
		return civ.traits.Contains(Civilization.Trait.Seafaring);
	}

	// Pairs each planned player with a starting location, in order.
	// Seafaring civs must start next to the sea: if one would start inland,
	// it swaps starts with a civ that isn't seafaring but has a coastal one.
	// If there are more seafaring civs than coastal starts, seafaring civs
	// that were picked at random are swapped for other civs first.
	private static List<SaveTile> AssignStartingLocations(SaveGame save, Random rand, List<PlannedPlayer> planned, HashSet<string> taken) {
		List<SaveTile> starts = save.Map.startingLocations.Take(planned.Count).ToList();
		if (save.Rules?.CulturallyLinkedStart == true) {
			starts = LinkStartsByCulture(save.Map, starts, planned);
		}
		HashSet<SaveTile> coastal = starts.Where(t => IsNextToSea(save.Map, t)).ToHashSet();

		int seafaring = planned.Take(starts.Count).Count(p => IsSeafaring(p.civ));
		foreach (PlannedPlayer p in planned.Take(starts.Count).Where(p => p.isRandom && IsSeafaring(p.civ)).ToList()) {
			if (seafaring <= coastal.Count) {
				break;
			}
			List<Civilization> others = save.Civilizations.Skip(1)
				.Where(c => !c.IsIndependent && !taken.Contains(c.name) && !IsSeafaring(c)).ToList();
			if (others.Count == 0) {
				break;
			}
			Civilization replacement = others[rand.Next(others.Count)];
			taken.Remove(p.civ.name);
			taken.Add(replacement.name);
			p.civ = replacement;
			--seafaring;
		}

		// As Civ3's game setup does (Civ3 0x5aec10): each seafaring civ in
		// turn keeps its coastal start, or else takes the first coastal start
		// no seafaring civ has kept or taken yet.
		HashSet<SaveTile> free = new(coastal);
		for (int i = 0; i < starts.Count; ++i) {
			if (!IsSeafaring(planned[i].civ)) {
				continue;
			}
			if (free.Remove(starts[i])) {
				continue;
			}
			int j = starts.FindIndex(free.Contains);
			if (j < 0) {
				log.Warning($"No coastal start left for seafaring {planned[i].civ.name}");
				continue;
			}
			free.Remove(starts[j]);
			(starts[i], starts[j]) = (starts[j], starts[i]);
		}
		return starts;
	}

	// Civ3's Culturally-Linked Start (Rules.CulturallyLinkedStart):
	// civilizations of the same culture group start near each other. Each
	// group in turn, largest first, takes the free start farthest from those
	// taken (the first, for the first group), then its other members the
	// free starts nearest it. Our reading: Civ3's help says nothing of how.
	// Seafaring civs' coastal starts still come first (after this).
	internal static List<SaveTile> LinkStartsByCulture(SaveMap map, List<SaveTile> starts, List<PlannedPlayer> planned) {
		int n = Math.Min(starts.Count, planned.Count);
		SaveTile[] result = new SaveTile[starts.Count];
		List<SaveTile> free = starts.Take(n).ToList();
		List<SaveTile> taken = [];
		IEnumerable<IGrouping<string, int>> groups = Enumerable.Range(0, n)
			.GroupBy(i => planned[i].civ?.cultureGroupKey ?? planned[i].civ?.cultureGroup?.name ?? "")
			.OrderByDescending(g => g.Count()).ThenBy(g => g.Min());
		foreach (IGrouping<string, int> group in groups) {
			SaveTile seed = taken.Count == 0 ? free[0] : free.OrderByDescending(t => taken.Min(o => StartDistance(map, t, o))).First();
			foreach (int i in group) {
				SaveTile start = free.OrderBy(t => StartDistance(map, t, seed)).First();
				free.Remove(start);
				taken.Add(start);
				result[i] = start;
			}
		}
		for (int i = n; i < starts.Count; ++i) {
			result[i] = starts[i];
		}
		return [.. result];
	}

	// Steps between two tiles in Civ3's coordinates (X + Y even), across
	// the map's wrapping edges.
	private static int StartDistance(SaveMap map, SaveTile a, SaveTile b) {
		int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
		if (map.wrapHorizontally) {
			dx = Math.Min(dx, map.tilesWide - dx);
		}
		if (map.wrapVertically) {
			dy = Math.Min(dy, map.tilesTall - dy);
		}
		return Math.Max(dy, (dx + dy) / 2);
	}

	// Whether any of the eight tiles around the tile is salt water.
	private static bool IsNextToSea(SaveMap map, SaveTile tile) {
		(int dx, int dy)[] around = { (1, -1), (1, 1), (-1, 1), (-1, -1), (2, 0), (-2, 0), (0, 2), (0, -2) };
		foreach ((int dx, int dy) in around) {
			int x = tile.X + dx;
			int y = tile.Y + dy;
			if (map.wrapHorizontally) {
				x = (x % map.tilesWide + map.tilesWide) % map.tilesWide;
			}
			SaveTile n = map.tiles.Find(t => t.X == x && t.Y == y);
			if (n != null && !n.isFreshWater && n.baseTerrain is "coast" or "sea" or "ocean") {
				return true;
			}
		}
		return false;
	}

	private static string RandomCivilization(SaveGame save, Random rand, HashSet<string> taken) {
		// The first civilization is the barbarians'. With every other one
		// taken, there is nothing to pick from.
		if (save.Civilizations.Skip(1).All(c => taken.Contains(c.name))) {
			throw new ArgumentException($"There are more players than the {save.Civilizations.Count - 1} civilizations to play");
		}
		string name;
		do {
			name = save.Civilizations[rand.Next(1, save.Civilizations.Count)].name;
		} while (taken.Contains(name));
		return name;
	}

	// The IDs the human players will have once Populate has added them to a
	// new game, in turn order: the first is playerCivilization's, then one
	// for each of hotseatPlayers. They come right after the barbarians'. A
	// LAN host uses these to seat guests before the game is created.
	public static List<ID> HumanPlayerIDs(int count) {
		ID.Factory ids = new();
		ids.CreateID("Player");
		return Enumerable.Range(0, count).Select(_ => ids.CreateID("Player")).ToList();
	}

	private IEnumerable<HotseatPlayer> HumanPlayers() {
		return hotseatPlayers.Prepend(new HotseatPlayer { civilization = playerCivilization, name = playerName, team = playerTeam });
	}

	// Every barbarian camp starts the game guarded by a basic barbarian unit
	// (a Warrior in the standard rules).
	private void AddCampDefenders(SaveGame save, SavePlayer barbarians) {
		string defender = save.BarbarianInfo.basicBarbarianUnit;
		if (defender == null) {
			return;
		}

		foreach (SaveTile tile in save.Map.tiles.Where(t => t.features.Contains(Tile.TileOverlays.BARBARIAN_CAMP))) {
			AddUnit(save, barbarians, defender, new TileLocation(tile.X, tile.Y));
		}
	}

	// idKey names the player's ID: Kings & Nomads' city-states have their
	// own, so that the civs' player IDs are as GameSetup.HumanPlayerIDs
	// says.
	private SavePlayer AddPlayer(SaveGame save, Civilization civ, bool isHuman, string name = null, SaveTile startingTile = null, string idKey = "Player",
			AiType aiType = AiType.Standard) {
		SavePlayer player = new() {
			isBarbarian = civ.isBarbarian,
			isCityStates = civ.isCityStates,
			human = isHuman,
			aiType = aiType == AiType.Standard ? null : aiType,
			name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
			id = ids.CreateID(idKey),
			primaryColorIndex = civ.primaryColorIndex,
			secondaryColorIndex = civ.secondaryColorIndex,
			civilization = civ.name,
			knownTechs = new HashSet<ID>(civ.startingTechs),
			eraCivilopediaName = save.Rules.FirstEraCivilopediaName,
			gold = save.Rules.StartingTreasury,
			governmentId = StartingGovernment(save),
		};
		if (save.Rules.AllTechsAtStart) {
			GiveAllTechs(save, player);
		}
		save.Players.Add(player);

		if (civ.IsIndependent) {
			player.canBePicked = false;
			return player;
		}

		if (startingTile == null) {
			throw new ArgumentException($"{civ.name} has no starting location");
		}
		TileLocation startingLocation = new TileLocation(startingTile.X, startingTile.Y);

		// An Eternal War civ starts with its cities' units instead (see
		// SetUpEternalWarPlayer).
		// A Rising Tide civ starts with a city and units of its own (see
		// AddRisingTideStart).
		if (scenario == GameScenario.RisingTide) {
			AddRisingTideStart(save, player, civ, startingLocation);
		} else if (scenario == GameScenario.KingsAndNomads) {
			AddKingsHorde(save, player, startingLocation);
		} else if (scenario != GameScenario.EternalWar) {
			AddUnit(save, player, save.Rules.StartUnitType1, startingLocation);
			AddUnit(save, player, save.Rules.StartUnitType2, startingLocation);
		}
		if (civ.traits.Contains(Civilization.Trait.Expansionist)) {
			AddUnit(save, player, save.Rules.ScoutUnitType, startingLocation);
		}
		// "In single player mode, the starting location of each player is
		// designated a Victory Location" (Conquests Civilopedia,
		// DESC_GCON_Short_Game_Modes). UNVERIFIED: hotseat and LAN games
		// get them too.
		if (save.VictoryConditions?.VictoryLocations == true && save.VictoryConditions.AutoPlaceVictoryLocations) {
			// The starting locations are copies of the map's tiles.
			SaveTile mapTile = save.Map.tiles.Find(t => t.X == startingTile.X && t.Y == startingTile.Y);
			if (mapTile != null && !mapTile.features.Contains(SaveTile.VictoryLocationFeature)) {
				mapTile.features.Add(SaveTile.VictoryLocationFeature);
			}
		}
		// Under Regicide, each civ's King starts with its first units.
		int kings = InstantElimination.KingsToPlace(save.VictoryConditions);
		if (kings > 0) {
			AddKings(save, player, civ, startingLocation, kings);
		}
		return player;
	}

	private void AddUnit(SaveGame save, SavePlayer player, string unitType, TileLocation location) {
		var defaultExpLevelKey = save.DefaultExperienceLevel;

		var defaultExpLevel = save.ExperienceLevels.First(e => e.key == defaultExpLevelKey);
		var barbExpLevel = save.ExperienceLevels.First(e => e.baseHitPoints == save.BarbarianInfo.maxHitpoints);

		var proto = save.UnitPrototypes.First(p => p.name == unitType);

		SaveUnit unit = new() {
			id = ids.CreateID(unitType),
			name = unitType,
			nationality = player.civilization,
			prototype = unitType,
			owner = player.id,
			previousLocation = new TileLocation(-1, -1),
			currentLocation = location,

			hitPointsRemaining = (player.isBarbarian ? barbExpLevel.baseHitPoints : defaultExpLevel.baseHitPoints) + proto.hpBonus,
			movePointsRemaining = proto.movement,
			experience = player.isBarbarian ? barbExpLevel.key : defaultExpLevel.key,

			facingDirection = TileDirection.SOUTHEAST,
		};
		save.Units.Add(unit);
	}
}

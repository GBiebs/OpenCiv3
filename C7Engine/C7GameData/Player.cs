using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using C7Engine.AI.StrategicAI;
using C7Engine;
using C7GameData.Save;
using MoonSharp.Interpreter;
using Serilog;
using static C7GameData.EraUtils;
using static C7GameData.MultiTurnDeal;
using static C7GameData.PlayerRelationship;
using static C7GameData.Tile;

namespace C7GameData {

	public struct PlayerCommerceBreakdown {
		public int corrupted;           // Amount of commerce lost directly to corruption
		public int taxes;               // Amount of treasury income from REGULAR citizens working tiles
		public int taxmenTaxes;         // Amount of treasury income from tax collector specialists
		public int beakers;             // Amount of commerce going to science
		public int happiness;           // Amount of commerce going to entertainment
		public int fromOtherCivs;       // Income from other Civ GPT deals
		public int toOtherCivs;         // Expenses paid to other Civ GPT deals
		public int interest;            // Interest income from Wall Street-flag small wonder
		public int maintenance;         // Expenses due to aggregate building maintenance
		public int unitSupport;         // Expenses due to unit support costs
		public int wealthProduction;    // Amount of extra commerce from "building" an Inflow that produces commerce
		public int tourism;             // Gold from old great wonders that are tourist attractions (Conquests)

		public int Inflows() {
			return corrupted + taxes + taxmenTaxes + beakers + happiness + fromOtherCivs + interest + wealthProduction + tourism;
		}

		public int Outflows() {
			return corrupted + beakers + happiness + toOtherCivs + maintenance + unitSupport;
		}

		public int Netflows() {
			return Inflows() - Outflows();
		}

		public int CityInflows() {
			return corrupted + taxes + beakers + happiness + wealthProduction;
		}
	}

	// A set of techs that also has an order, which enumerating it follows
	// (plain HashSets only happen to enumerate in insertion order). It is a
	// HashSet so callers can still test membership quickly; don't add to or
	// remove from it.
	public sealed class OrderedTechSet : HashSet<Tech>, IEnumerable<Tech> {
		private readonly List<Tech> ordered;

		// The techs must not contain duplicates.
		public OrderedTechSet(List<Tech> ordered) : base(ordered) {
			this.ordered = ordered;
		}

		public new List<Tech>.Enumerator GetEnumerator() {
			return ordered.GetEnumerator();
		}

		IEnumerator<Tech> IEnumerable<Tech>.GetEnumerator() {
			return ordered.GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
			return ordered.GetEnumerator();
		}
	}

	// O(1) checks that a List or HashSet hasn't been modified since an
	// enumerator was taken from it. Their enumerators are documented to throw
	// from MoveNext once the collection has been modified (and List counts
	// replacing an entry as a modification), so trying to move a copy of the
	// stored enumerator tells us, without walking the collection. (The
	// enumerator is passed by value, so the stored one never moves.) The
	// exception is only thrown, and caught, once per change.
	internal static class CollectionVersion {
		internal static bool Unchanged<T>(List<T>.Enumerator enumerator) {
			try {
				enumerator.MoveNext();
				return true;
			} catch (InvalidOperationException) {
				return false;
			}
		}

		internal static bool Unchanged<T>(HashSet<T>.Enumerator enumerator) {
			try {
				enumerator.MoveNext();
				return true;
			} catch (InvalidOperationException) {
				return false;
			}
		}
	}

	public class Alliance {
		public int index;
		public string name;

		public Alliance(int index, string name) {
			this.index = index;
			this.name = name;
		}
	}

	public class Player {
		private static ILogger log = Log.ForContext<Player>();

		public ID id { get; internal set; }
		public int primaryColorIndex;
		public int secondaryColorIndex;
		public bool isBarbarians { get => civilization.isBarbarian; }

		// Kings & Nomads' city-states (see Civilization.isCityStates).
		public bool isCityStates { get => civilization?.isCityStates == true; }

		// Not a civ: the barbarians or the city-states. They meet no one,
		// make no deals, are at war with every civ for good, and never win,
		// score or fall.
		public bool IsIndependent { get => civilization?.IsIndependent == true; }
		//TODO: Refactor front-end so it sends player GUID with requests.
		//We should allow multiple humans, this is a temporary measure.
		public bool isHuman = false;
		// Which AI plays this civ when the computer plays it (see AiType).
		public AiType aiType = AiType.Standard;
		// Whether the Advanced AI plays this civ. Never for a human (aiType is
		// kept, so observer mode's AI plays a human's civ the usual way), the
		// barbarians or the city-states.
		// Also, for a moment, a human whose choices the play recorder asks the
		// Advanced AI about (see C7Engine.AI.Advanced.ShadowAdvanced).
		public bool IsAdvancedAi => (aiType == AiType.Advanced && !isHuman && !IsIndependent)
			|| C7Engine.AI.Advanced.ShadowAdvanced.Covers(this);
		// An Advanced AI's current strategy (see StrategicPlan), or null.
		public StrategicPlan strategicPlan;
		// An Advanced AI's place in the phase-plan script (see GamePhaseState), or null.
		public GamePhaseState gamePhase;
		// An Advanced AI's policy, as the model last reviewed it (see
		// AdvancedPolicy), or null for the game-wide one.
		public AdvancedPolicy advancedPolicy;
		public bool hasPlayedThisTurn = false;
		public bool skipFirstTurn = false;

		// The name of the person playing, for human players in a hotseat game.
		// Null when no name was given.
		public string name;

		// Hotseat and play by email: the password the player gives before
		// their empire is shown, as Civ3's ("Please Enter New Password (Leave
		// blank for none)"), empty for none. Kept as a slow salted hash, as
		// the save travels to the other players by email. passwordChosen is
		// set once they've been asked to choose one, so it's asked only once.
		public string passwordHash = "";
		public bool passwordChosen = false;

		// Play by email: what this player was told while the others played,
		// kept in the save for their turn (see PlayByEmail.KeepNews), and the
		// turn the computer players last had their say with them (see
		// PlayByEmail.HearFromAIs).
		public List<string> newsWhileAway = new();
		public int heardFromAIsOnTurn = -1;

		// The production queues the player saved under a name, by name (see
		// SavedProductionQueue).
		public List<SavedProductionQueue> savedProductionQueues = new();

		private const int PasswordIterations = 100_000;

		public bool HasPassword() => !string.IsNullOrEmpty(passwordHash);

		// Sets the player's password; empty for none.
		public void SetPassword(string password) {
			passwordChosen = true;
			if (string.IsNullOrEmpty(password)) {
				passwordHash = "";
				return;
			}
			byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
			passwordHash = $"pbkdf2:{PasswordIterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(HashPassword(password, salt, PasswordIterations))}";
		}

		// Whether this is the player's password (anything is, without one).
		public bool CheckPassword(string password) {
			if (!HasPassword()) {
				return true;
			}
			string[] parts = passwordHash.Split(':');
			if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out int iterations) || iterations <= 0) {
				return false;
			}
			try {
				byte[] expected = Convert.FromBase64String(parts[3]);
				byte[] given = HashPassword(password ?? "", Convert.FromBase64String(parts[2]), iterations);
				return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(given, expected);
			} catch (FormatException) {
				return false;
			}
		}

		private static byte[] HashPassword(string password, byte[] salt, int iterations) {
			return System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations,
				System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
		}

		// Has this player been defeated?
		public bool defeated = false;

		public Civilization civilization;

		// Answers if this player-civ is simply included in the game.
		// Some .biq scenarios contain players/civs in their data
		// that are not a part of the gameplay.
		// ex. Mongols in `4 Middle Ages.biq` scenario.
		public bool isIncludedInGame = true;

		// Answers if the human player can pick and play as this player-civ
		// or is it only an AI player
		public bool canBePicked = true;

		public List<MapUnit> units = new List<MapUnit>();
		public List<City> cities = new List<City>();
		public TileKnowledge tileKnowledge { get; private set; }

		// On a LAN guest's machine that isn't sent all of the game, this
		// player's totals as the host worked them out (see HostFacts); null
		// otherwise.
		public Save.PlayerFacts hostFacts;

		//Ordered list of priority data.  First is most important.
		public List<StrategicPriority> strategicPriorityData = new List<StrategicPriority>();

		// A map from player id to the relationship this player has with the other player.
		public Dictionary<ID, PlayerRelationship> playerRelationships = new();

		// The list of techs known by this player.
		public HashSet<ID> knownTechs = new();

		// A mapping of resource types to their corresponding tiles within player territory.
		// Provides lookup of controlled resources without scanning the entire map.
		public Dictionary<Resource, List<Tile>> resourcesInBorders;

		// The tech the player is currently researching.
		public ID currentlyResearchedTech { get; private set; }

		// A queue of technologies the player has specified they want to research
		public Queue<Tech> ResearchQueue { get; private set; } = new();

		// The civilopedia name of the era this player is in.
		//
		// The civilopedia name is what is used for art lookups, not the actual
		// name.
		public string eraCivilopediaName;

		public int turnsUntilPriorityReevaluation = 0;

		// The values of the science/happiness/tax sliders (tax is implicit)
		// A value of 1 => 10%, a value of 10 => 100%.
		//
		// INVARIANT: LuxuryRate + ScienceRate + TaxRate = 10
		public int luxuryRate = 0;
		public int scienceRate = 5;
		public int taxRate = 5;

		// The government caps how high any one slider can go.
		public int maxRate => Math.Clamp(government?.rateCap ?? 10, 0, 10);
		public int maxScienceRate => maxRate;
		public int minScienceRate { get; private set; } = 0;
		public int maxLuxuryRate => maxRate;
		public int minLuxuryRate { get; private set; } = 0;

		// Moves slider points above the government's rate cap to sliders that
		// still have room, preferring tax, then science, then luxury. If the
		// cap is too low for the three sliders to add up to 10, the leftover
		// stays in tax.
		// Takes up a new government once anarchy is over. Under Forced
		// Resettlement (Fascism) "each town in the nation loses 1 population,
		// each city loses 2 population, and each metropolis loses 3
		// population" (C3C editor help, Flags). UNVERIFIED: a city is never
		// left with no citizens; one too small keeps a single citizen.
		public void AdoptGovernment(Government newGovernment) {
			government = newGovernment;
			ApplyGovernmentRateCap();
			if (newGovernment?.forcedResettlement != true) {
				return;
			}
			Rules r = rules ?? EngineStorage.gameData.rules;
			foreach (City c in cities) {
				int size = c.residents.Count;
				int loss = size <= r.MaximumLevel1CitySize ? 1 : size <= r.MaximumLevel2CitySize ? 2 : 3;
				c.RemoveCitizens(Math.Min(loss, size - 1));
			}
		}

		public void ApplyGovernmentRateCap() {
			int excess = 0;
			int Trim(int rate) {
				if (rate <= maxRate) {
					return rate;
				}
				excess += rate - maxRate;
				return maxRate;
			}
			int Fill(int rate) {
				int moved = Math.Min(excess, Math.Max(0, maxRate - rate));
				excess -= moved;
				return rate + moved;
			}

			taxRate = Trim(taxRate);
			scienceRate = Trim(scienceRate);
			luxuryRate = Trim(luxuryRate);

			taxRate = Fill(taxRate);
			scienceRate = Fill(scienceRate);
			luxuryRate = Fill(luxuryRate);
			taxRate += excess;
		}

		// The amount of gold this player has. It never goes below zero: the
		// callers that spend gold (hurrying, espionage, upgrades, deals) check
		// the treasury first, and the turn's deficit is handled by deleting
		// buildings and disbanding units. Should anything still overspend,
		// the treasury is emptied and a warning logged rather than the turn
		// crashing.
		private int _gold = 0;
		public int gold {
			get => _gold;
			set {
				if (value < 0) {
					log.Warning("Tried to set {Player}'s gold to {Gold}; emptying the treasury instead", this, value);
					value = 0;
				}
				_gold = value;
			}
		}

		private int lastGoldPerTurn = 0;

		/// <summary>
		/// Sets the current gold amount of the player. If the add parameter is true, the gold gets appended.<br/>
		/// </summary>
		/// <param name="amount">The number of gold to be added</param>
		/// <param name="add">If true, the gold gets appended, otherwise it overwrites the current value</param>
		/// Scripts may take more gold than there is; the treasury is then emptied.
		[LuaMethod]
		public void SetGold(int amount, bool add = false) {
			int newGold = add ? gold + amount : amount;
			this.gold = Math.Max(0, newGold);
		}

		// The number of "beakers" (gold) spent on the currently researched
		// tech.
		public int beakers = 0;

		// The number of turns the player has been researching the current tech.
		// Only turns that produced beakers count.
		public int turnsResearched = 0;

		// Whether this turn's finances put any beakers into research. Set by
		// DoPerTurnFinanceUpdates for DoPerTurnScienceUpdates, which runs
		// right after it, so it needn't be saved.
		private bool researchFundedThisTurn;

		// If the government is anarchy (or a govt with the transition bool set
		// to true), the turn number at which switching governments is allowed.
		public int inAnarchyUntilTurn = 0;

		// The current government of the player.
		public Government government;

		// The rules of this game.
		public Rules rules;

		// Statistics recorded each turn, for the statistics screen.
		public PlayerStats stats = new();

		// Future techs learned, once every advance was known. They count only
		// for the score (see ScoreVictory.ComputeTurnScore).
		public int futureTechs;

		public List<City> citiesWithCorruptionWonders = new();

		// The number of free techs this player has remaining. Free techs can
		// be awarded after researching a tech (like Philosophy) or after
		// completing a wonder (like Theory of Evolution).
		public int freeTechsRemaining = 0;

		// How many of the free techs are bonus techs for being first to a
		// tech (Philosophy's), which roll for a scientific leader when
		// learned (see SetCurrentlyResearchedTech).
		public int bonusFreeTechsRemaining = 0;

		// The tech a human player last discovered, while they haven't yet
		// chosen what to research next, for the science advisor to announce.
		// It isn't saved.
		public Tech lastDiscoveredTech;

		public Alliance alliance;

		// Whether one of this player's armies has won a battle. The Military
		// Academy can't be built until one has.
		public bool hasVictoriousArmy = false;

		// How many of each spaceship part (by Building.spaceshipPart index)
		// this civ has built. Losing the capital destroys them. See SpaceRace.
		public List<int> spaceshipParts = new();

		// The airports and airfields this player has airlifted a unit from
		// on turn airliftTurn; each sends one a turn (see MapUnit_Airlift.cs).
		public int airliftTurn = -1;
		public HashSet<Tile> airliftOrigins = new();

		public bool HasAirliftedFromThisTurn(Tile tile) {
			return airliftTurn == CurrentTurn() && airliftOrigins.Contains(tile);
		}

		public void RecordAirliftFrom(Tile tile) {
			int turn = CurrentTurn();
			if (airliftTurn != turn) {
				airliftTurn = turn;
				airliftOrigins.Clear();
			}
			airliftOrigins.Add(tile);
		}

		private static int CurrentTurn() => EngineStorage.gameData?.turn ?? 0;

		// War weariness points from saves made before they were kept per
		// enemy (see PlayerRelationship.warWearinessPoints), which can't be
		// put down to any one civ. They count like one more enemy's and fade
		// like any war's after peace. New points never go here.
		public int warWeariness = 0;

		// Whether the civ's economy is mobilized for war ("War-Time"), and
		// whether it may return to Normalcy, which it may once a peace treaty
		// is signed or an enemy destroyed (see Mobilization).
		public bool mobilized = false;
		public bool mayEndMobilization = false;

		// Each civ gets one golden age per game.
		public bool hadGoldenAge = false;
		public int goldenAgeTurnsRemaining = 0;

		// The upgrades the civ's palace has earned, and the pieces built
		// with them.
		public PalaceUpgrades palace = new();

		// How many cities this player has founded, used to pick the next
		// name from the civ's city name list.
		public int citiesFounded = 0;

		// Kings & Nomads: the city-states this player has taken, each of
		// which weakens its units against the rest (see
		// MapUnit.BrethrenPenaltyAgainst).
		public int cityStatesCaptured = 0;

		// Whether this player has never held a city or a settler, like a
		// scenario civ that starts with only units. Such a civ isn't destroyed
		// for having no cities or settlers while it still has units. It's
		// worked out when a game is loaded (see SaveGame.ConvertCities) and
		// cleared once the player gains a city. A civ that loses its starting
		// settlers before founding a city is still destroyed.
		public bool neverHadCityOrSettler = false;

		// Whether this AI has had the difficulty's extra start units (see
		// MaybeSpawnBonusUnits). Saved; older saves work it out on loading
		// (see SaveGame.ConvertCities).
		public bool receivedStartUnits = false;

		// How many of the civ's King units have died, and how many of its
		// cities enemies have taken, for Regicide and Elimination (see
		// InstantElimination).
		public int kingsLost = 0;
		public int citiesLost = 0;

		// The civ's victory points (see VictoryPoints).
		public int victoryPoints = 0;

		// Under PersistentResources (MDJ), the keys of the resources the civ
		// has had, which stay its own (see RecordObtainedResources).
		public HashSet<string> obtainedResources = new();

		// Under PersistentResources, adds the resources the civ's cities have
		// now to those it keeps. Done as each turn begins, and before a
		// Tidal Wave takes land.
		public void RecordObtainedResources(GameData gameData) {
			if (gameData.rules?.PersistentResources != true || IsIndependent) {
				return;
			}
			foreach (City city in cities) {
				foreach (Resource r in city.GetCurrentResources(gameData).Keys) {
					if (r.Key != null) {
						obtainedResources.Add(r.Key);
					}
				}
			}
		}

		// The enemy units this civ has defeated in combat, the cities it
		// has taken, and, under Rising Tide, the turns it has lived through,
		// for the Rising Tide score (see RisingTide.ScoreBonus).
		public int unitsKilled = 0;
		public int citiesCaptured = 0;
		public int turnsSurvived = 0;

		public bool InGoldenAge => goldenAgeTurnsRemaining > 0;

		// The turns left of an Age of Science (see StartScienceAge).
		public int scienceAgeTurnsRemaining = 0;

		public bool InScienceAge => scienceAgeTurnsRemaining > 0;

		public int EraIndex() {
			return GetEraIndex(eraCivilopediaName);
		}

		public static int EraIndex(string era) {
			if (era == "ERAS_Ancient_Times") {
				return 0;
			} else if (era == "ERAS_Middle_Ages") {
				return 1;
			} else if (era == "ERAS_Industrial_Age") {
				return 2;
			} else if (era == "ERAS_Modern_Era") {
				return 3;
			}
			return -1;
		}

		public static string EraIndexToEra(int index) {
			if (index <= 0) {
				return "ERAS_Ancient_Times";
			} else if (index == 1) {
				return "ERAS_Middle_Ages";
			} else if (index == 2) {
				return "ERAS_Industrial_Age";
			} else {
				return "ERAS_Modern_Era";
			}
		}

		public void AddUnit(MapUnit unit) {
			this.units.Add(unit);
		}

		public void SetCurrentlyResearchedTech(ID id) {
			// The beakers gathered while nothing was being researched, such
			// as a human's on the turn they learned a tech and are asked what
			// to research next (CompletePaidResearch runs before the turn's
			// beakers are added), go to the tech chosen.
			(int beakers, int turns) unassigned = currentlyResearchedTech == null ? (beakers, turnsResearched) : (0, 0);

			// Award a free tech if the player has one.
			if (id != null && freeTechsRemaining > 0) {
				--freeTechsRemaining;
				beakers = 0;
				turnsResearched = 0;

				GameData gameData = EngineStorage.gameData;
				Tech tech = Tech.FindById(gameData.techs, id);
				log.Information("Awarding {Tech} to player {Player}", tech.Name, this);
				// Philosophy's free tech counts as a first discovery for a
				// scientific leader: "You can also get one for researching
				// philosophy and getting that free tech" (AutomatedTeller,
				// https://forums.civfanatics.com/threads/how-do-you-get-a-scientific-leader.211436),
				// and two leaders on one turn are reported from it
				// (https://forums.civfanatics.com/threads/2-x-sci-leaders.292238).
				bool rollsForLeader = bonusFreeTechsRemaining > 0;
				if (rollsForLeader) {
					--bonusFreeTechsRemaining;
				}
				bool first = rollsForLeader && IsFirstToLearn(gameData, tech);
				CompleteResearchAndBeginNew(gameData, tech);
				if (first) {
					MapUnit.RollForScientificLeader(gameData, this, tech);
				}
				if (unassigned.beakers > 0) {
					beakers += unassigned.beakers;
					turnsResearched += unassigned.turns;
				}
				return;
			}

			// Choosing the tech already being researched (again, or as the
			// first step towards a further goal) is no switch, and keeps the
			// beakers.
			if (id != null && id == currentlyResearchedTech) {
				lastDiscoveredTech = null;
				return;
			}

			// Switching research throws away the beakers stored, but a switch
			// undone within the same turn costs nothing: "by changing their
			// focus, you lose all of their work on the advance you order them
			// to abandon" (Civ3 manual, p.123), and "Switch to another tech in
			// the middle of a turn; you can then switch back." (kring,
			// https://forums.civfanatics.com/threads/science-slider-when-1-turn-from-tech-change-it-or-not.24350/page-2).
			// So the abandoned tech's progress is kept until the turn ends.
			// It isn't saved: a game reloaded mid-turn has lost it.
			int turn = EngineStorage.gameData?.turn ?? 0;
			if (id == null) {
				abandonedResearch = null;
			} else if (currentlyResearchedTech != null && beakers > 0) {
				abandonedResearch = (currentlyResearchedTech, beakers, turnsResearched, turn);
			}

			currentlyResearchedTech = id;
			if (id != null) {
				lastDiscoveredTech = null;
			}

			// Clear out previous progress, unless switching back the same turn.
			beakers = 0;
			turnsResearched = 0;
			if (id != null && abandonedResearch is (ID abandoned, int stored, int turns, int onTurn) && abandoned == id && onTurn == turn) {
				beakers = stored;
				turnsResearched = turns;
				abandonedResearch = null;
			} else if (id != null) {
				beakers = unassigned.beakers;
				turnsResearched = unassigned.turns;
			}
		}

		// The research abandoned by a switch this turn, which switching back
		// before the turn ends restores (see SetCurrentlyResearchedTech).
		private (ID tech, int beakers, int turnsResearched, int turn)? abandonedResearch;

		public void AddTechItemToResearchQueue(Tech tech) {
			ResearchQueue.Enqueue(tech);
		}

		private IEnumerable<string> CityNameGenerator() {
			List<string> cityNames = civilization?.cityNames;

			// A civ with no city names (as some scenarios define) gets
			// numbered names, so this never loops forever finding nothing.
			if (cityNames == null || cityNames.Count == 0) {
				string baseName = civilization?.adjective;
				if (string.IsNullOrEmpty(baseName)) {
					baseName = civilization?.name;
				}
				if (string.IsNullOrEmpty(baseName)) {
					baseName = "City";
				}
				for (int n = 1; ; ++n) {
					yield return $"{baseName} {n}";
				}
			}

			int loopCounter = 0;

			// Perpetual generator expression to yield all city names lazily
			while (true) {
				foreach (string city in cityNames) {
					if (loopCounter == 0) yield return city;
					else if (loopCounter == 1) yield return $"New {city}";
					else {
						if (loopCounter % 2 == 0) yield return $"{city} {(loopCounter / 2) + 1}";
						else yield return $"New {city} {(loopCounter / 2) + 1}";
					}
				}
				loopCounter++;
			}
		}

		public string GetNextCityName() {
			// Names held by any city in the game, so a civ that lost a city
			// doesn't reuse its name while the conqueror still holds it.
			HashSet<string> cityNameHashSet = (EngineStorage.gameData?.cities ?? cities)
				.Select(city => city.name).ToHashSet();

			// Continue down the list from the last city founded rather than
			// restarting at the top, so lost cities' names aren't reused.
			return CityNameGenerator().Skip(citiesFounded).First(x => !cityNameHashSet.Contains(x));
		}

		public Player() {
			tileKnowledge = new TileKnowledge(this);
		}

		public bool HasExploredTile(Tile tile) {
			return this.tileKnowledge.knownTiles.Contains(tile);
		}

		// Whether paths for our units go only by what we've explored, taking
		// tiles we haven't to be passable: a human's always do, and an AI's
		// do with the AI fog of war (see C7Engine.AI.AIFogOfWar).
		public bool PathsByExploredMap => isHuman || ((EngineStorage.aiFogOfWar || IsAdvancedAi) && !IsIndependent);

		// Whether to tell this player news meant just for them, such as a
		// golden age beginning: a human, and while spectators may watch as
		// it (see EngineStorage.newsForComputerPlayers), a computer player.
		public bool IsToldNews => isHuman || (EngineStorage.newsForComputerPlayers && !IsIndependent);

		public bool IsAtPeaceWith(Player other) {
			return AtPeace(this, other);
		}

		public void EnsureRelationshipExists(Player other) {
			if (this.IsIndependent || other.IsIndependent || this.id == other.id || this.defeated || other.defeated)
				return;

			// If the mutual relationship is not established it means that the 2 civs
			// were not aware of each other (aka they just met), and therefore cannot be at war.
			// Initialize the relationship and establish peace automatically between them,
			// unless the rules keep every civ at war (Rules.PermanentWar).
			if (!this.playerRelationships.ContainsKey(other.id) || !other.playerRelationships.ContainsKey(this.id)) {
				int turn = EngineStorage.gameData?.turn ?? -1;
				this.playerRelationships.TryAdd(other.id, new PlayerRelationship() { firstContactTurn = turn });
				other.playerRelationships.TryAdd(this.id, new PlayerRelationship() { firstContactTurn = turn });
				if (EngineStorage.gameData?.rules?.PermanentWar != true) {
					RegisterMultiTurnDeal(this, other, DEFAULT_PEACE);
				}

				log.Information("Established first contact and relationship between players {Player} and {Other}", this, other);
			}
		}

		// afterWarning is true when the other civ told us to leave its
		// territory or face war, and we chose war: that is no sneak attack,
		// even though our units are inside its borders.
		// afterWarning: the other civ was warned, so this isn't a sneak attack.
		// provoked: the other civ brought the war on itself, so its people
		// don't rally against us.
		public void DeclareWarOn(Player other, int currentTurn, bool afterWarning = false, bool provoked = false) {
			// Locked allies, such as teammates, never go to war with each
			// other, whatever asks them to (see PlayerRelationship.DeclareWar).
			if (EngineStorage.gameData?.AreInLockedPeace(this, other) == true) {
				return;
			}
			EnsureRelationshipExists(other);

			// Check to see if there was a sneak attack - we consider a sneak
			// attack any attack where the player's units were inside the
			// borders of the civ they're declaring war on.
			bool isSneakAttack = !afterWarning && IsASneakAttackOn(other);

			// TODO: take into account broken right of passage, or other deals, etc?
			// Perhaps we need a dedicated method to calculate this.
			//
			// Refuse contact from the aggressor civ until enough turns have
			// elapsed. The exact civ3 mechanism here is unknown, so we just
			// pick some reasonable random number. To penalize sneak attacks we
			// use a higher upper bound.
			int refuseContactUntilTurn = currentTurn + GameData.rng.Next(5, isSneakAttack ? 16 : 12);

			DeclareWar(this, other, isSneakAttack, refuseContactUntilTurn, currentTurn);

			// The other civ's people rally against whoever declares war on
			// them, AI or human: war happiness, unless they provoked it, by
			// their nuclear weapons, their caught spies, or (per the project
			// owner) refusing our demand to take their units out of our
			// territory, when the war is the direct result of that refusal
			// (see WarWearinessWhenWarIsDeclaredOnUs).
			if (!IsIndependent && !provoked && playerRelationships.TryGetValue(other.id, out PlayerRelationship ourView)
				&& ourView.nuclearAtrocityCount == 0 && ourView.espionageIncidents == 0) {
				other.AddWarWeariness(this, WarWearinessWhenWarIsDeclaredOnUs);
				if (other.playerRelationships.TryGetValue(id, out PlayerRelationship theirView)) {
					theirView.warHappinessAtStart = WarWearinessWhenWarIsDeclaredOnUs;
				}
			}

			C7Engine.AI.AiExperiments.onWarDeclared?.Invoke(this, other);

			// Whenever war is declared, re-evaluate priorities.
			turnsUntilPriorityReevaluation = 0;
			other.turnsUntilPriorityReevaluation = 0;
		}

		// Whether, on a tile known to the other player and inside their
		// borders, the top unit is ours.
		//
		// Rather than every tile the other player knows, this looks at the
		// tiles our units are on: a tile whose top unit is ours is one of
		// them, as each unit on a tile has that tile as its location and is
		// in its owner's list of units.
		private bool IsASneakAttackOn(Player other) {
			foreach (MapUnit unit in units) {
				Tile location = unit.location;
				if (location == null || location.owningCity == null || location.owningCity.owner != other) {
					continue;
				}
				if (location.unitsOnTile.Count == 0 || location.unitsOnTile[0].owner != this) {
					continue;
				}
				if (other.tileKnowledge.knownTiles.Contains(location)) {
					return true;
				}
			}

			return false;
		}

		public bool WillAcceptCommunicationFrom(Player other, int currentTurn) {
			EnsureRelationshipExists(other);

			if (EngineStorage.gameData.AreInLockedWar(this, other)) return false;

			PlayerRelationship pr = playerRelationships[other.id];
			if (AtPeace(this, other)) {
				return true;
			}
			return currentTurn >= pr.refuseContactUntilTurn;
		}

		public bool SitsOutFirstTurn() {
			return this.IsIndependent || this.skipFirstTurn;
		}

		// Whether the player's units get the roads' and railroads' movement
		// between the two tiles. "Rival civilizations cannot benefit from the
		// roads or rails within your territory without a right of passage
		// agreement" (Conquests Civilopedia, GCON_Territory), so both tiles
		// must be unowned, ours, or a right of passage partner's: a road
		// from a partner's land into a third civ's is no faster.
		public static bool CanMoveFreely(Player player, Tile sourceTile, Tile targetTile) {
			if (!player.HasExploredTile(targetTile))
				return true;

			return MayUseRoadsOn(player, sourceTile.OwningPlayer())
				&& MayUseRoadsOn(player, targetTile.OwningPlayer());
		}

		private static bool MayUseRoadsOn(Player player, Player tileOwner) {
			return tileOwner == null || tileOwner == player || HaveActiveRightOfPassage(player, tileOwner);
		}

		public bool KnowsAboutResource(Resource resource) {
			if (resource.Prerequisite == null) {
				return true;
			}
			return knownTechs.Contains(resource.Prerequisite);
		}

		public int ArmyCount() {
			int result = 0;
			foreach (MapUnit u in units) {
				if (u.IsArmy()) {
					++result;
				}
			}
			return result;
		}

		// Whether the player has enough cities to support one more army: each
		// army needs CitiesNeededToSupportAnArmy cities.
		public bool CanSupportAnotherArmy() {
			int citiesPerArmy = rules?.CitiesNeededToSupportAnArmy ?? 0;
			return (ArmyCount() + 1) * citiesPerArmy <= RemainingCities();
		}

		// Whether this player's armies can carry an extra unit, thanks to a
		// building like the Pentagon.
		public bool HasLargerArmies() {
			return GetBuildingSnapshot().hasLargerArmies;
		}

		// Whether this player's armies get the bigger share of their members'
		// strength (see MapUnit.ArmyStrengthBonus), thanks to a building like
		// the Military Academy (the BIQ's "increased army value").
		public bool HasIncreasedArmyValue() {
			return GetBuildingSnapshot().hasIncreasedArmyValue;
		}

		public int RemainingCities() {
			int result = 0;
			foreach (City city in cities) {
				// Destroyed cities have a size of zero.
				if (city.residents.Count > 0) {
					++result;
				}
			}
			return result;
		}

		public override string ToString() {
			if (civilization != null)
				return $"{civilization.name} [{this.id}]";
			return "";
		}

		[LuaMethod]
		public List<Tech> GetKnownTechs() {
			List<Tech> result = new();
			foreach (Tech tech in EngineStorage.gameData.techs) {
				if (knownTechs.Contains(tech.id)) {
					result.Add(tech);
				}
			}
			return result;
		}

		public PlayerCommerceBreakdown AggregateFlows() {
			var result = new PlayerCommerceBreakdown
			{
				corrupted = 0,
				taxes = 0,
				taxmenTaxes = 0,
				beakers = 0,
				happiness = 0,
				fromOtherCivs = 0,
				toOtherCivs = 0,
				interest = 0,
				maintenance = 0,
				unitSupport = 0,
				wealthProduction = 0
			};

			// If player has no cities, apply no expenses or income.
			// This is how this behaves in regular Civ 3 as well (if you're not defeated, e.g. you still have a King unit or settler)
			if (cities.Count == 0) return result;

			// Assume player has no buildings that generate interest income until we check
			int interestBuildings = 0;

			foreach (City city in cities) {
				AddCityFlows(ref result, ref interestBuildings, ComputeCityFlows(city));
			}

			return AddEmpireFlows(result, interestBuildings, TotalUnitsAllowedUnitsAndSupportCost().Item3);
		}

		// One city's contribution to AggregateFlows.
		private struct CityFlows {
			public CommerceBreakdown commerce;
			public int maintenance;
			public int interestBuildings;
			public int taxmenTaxes;
			public int tourism;
		}

		private static CityFlows ComputeCityFlows(City city) {
			CityFlows result = new();
			result.commerce = city.CurrentCommerceYield();
			result.maintenance = city.MaintenanceCosts();
			foreach (CityBuilding cb in city.constructed_buildings) {
				if (cb.building.treasuryEarnsInterest && cb.building.TakesEffectFor(city.owner)) {
					++result.interestBuildings;
				}
			}
			foreach (CityResident cr in city.residents) {
				result.taxmenTaxes += cr.citizenType.Taxes;
			}
			result.tourism = city.TourismGold();
			return result;
		}

		private static void AddCityFlows(ref PlayerCommerceBreakdown result, ref int interestBuildings, in CityFlows city) {
			result.corrupted += city.commerce.corrupted;
			result.taxes += city.commerce.taxes;
			result.beakers += city.commerce.beakers;
			result.happiness += city.commerce.happiness;
			result.maintenance += city.maintenance;
			result.wealthProduction += city.commerce.wealth;
			result.tourism += city.tourism;

			interestBuildings += city.interestBuildings;

			// Split city income into "regular citizen" and "tax collector" buckets
			result.taxes -= city.taxmenTaxes;
			result.taxmenTaxes += city.taxmenTaxes;
		}

		// Adds the parts of AggregateFlows that don't come from the cities.
		private PlayerCommerceBreakdown AddEmpireFlows(PlayerCommerceBreakdown result, int interestBuildings, int unitSupport) {
			foreach (var pr in playerRelationships.Values) {
				foreach (var mtd in pr.multiTurnDeals) {
					if (mtd.dealSubType == DealSubType.GoldPerTurn) {
						if (mtd.dealDetails == DealDetails.Inbound) result.fromOtherCivs += mtd.goldPerTurn;
						else if (mtd.dealDetails == DealDetails.Outbound) result.toOtherCivs += mtd.goldPerTurn;
					}
				}
			}

			// Kings & Nomads' trade with the city-states (see CityStateTrade).
			result.fromOtherCivs += CityStateTrade.Income(EngineStorage.gameData, this);

			result.unitSupport = unitSupport;

			result.interest = TreasuryInterest(interestBuildings);

			return result;
		}

		// Interest on the treasury: 5% per interest building (Wall Street),
		// "with a maximum cap of 50 gold per turn" (Civilopedia
		// BLDG_Wall_Street). The cap is on the total: "Whenever the game
		// needs to compute a player's gold income, it separately computes
		// the interest then applies the limit." (Flintlock, reading the 1.22
		// code, https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16881426).
		// Stock Conquests has one such building per civ, so this matters only
		// with more than one.
		private int TreasuryInterest(int interestBuildings) {
			if (interestBuildings <= 0) {
				return 0;
			}
			return Math.Min(interestBuildings * (int)(gold * rules.TreasuryInterestRate), rules.MaxInterest);
		}

		// The treasury that earns the most interest: 1000 gold with one
		// interest building, less with more. Zero without one.
		public int GoldForFullInterest() {
			int interestBuildings = 0;
			foreach (City c in cities) {
				foreach (CityBuilding cb in c.constructed_buildings) {
					if (cb.building.treasuryEarnsInterest && cb.building.TakesEffectFor(this)) {
						++interestBuildings;
					}
				}
			}
			if (interestBuildings == 0 || rules.TreasuryInterestRate <= 0) {
				return 0;
			}
			return (int)Math.Ceiling(rules.MaxInterest / (rules.TreasuryInterestRate * interestBuildings));
		}

		// The same as CalculateGoldPerTurn, from per-city flows computed
		// earlier and the current unit support cost.
		private int GoldPerTurnFrom(CityFlows[] cityFlows, int unitSupport) {
			return FlowsFrom(cityFlows, unitSupport).Netflows();
		}

		private PlayerCommerceBreakdown FlowsFrom(CityFlows[] cityFlows, int unitSupport) {
			if (cityFlows.Length == 0) {
				return new();
			}
			PlayerCommerceBreakdown result = new();
			int interestBuildings = 0;
			foreach (CityFlows flows in cityFlows) {
				AddCityFlows(ref result, ref interestBuildings, flows);
			}
			return AddEmpireFlows(result, interestBuildings, unitSupport);
		}

		public int MaintenanceCosts() {
			int result = 0;
			foreach (City c in cities) {
				result += c.MaintenanceCosts();
			}
			return result;
		}

		// TODO: Add interest and GPT deals
		public int CalculateGoldPerTurn() {
			return AggregateFlows().Netflows();
		}

		// Whether we, an AI, would accept the deal, in which the other player
		// gives theirOffer and we give ourOffer. Cities count for nothing in
		// the valuation (see TradeOffer.GoldEquivalentFor), and per the
		// project owner we never give a city away except in a peace treaty.
		// Peace is weighed up by PeaceAI.
		//
		// theirValuePercent scales what their offer is worth to us, for
		// working out how far a deal is from one we'd take (see
		// C7Engine.AI.CounterOfferAI): below 100 we're harder to please, as
		// when making a counter-offer that leaves us a margin.
		public bool WouldAcceptDealFrom(GameData gameData, Player other, TradeOffer theirOffer, TradeOffer ourOffer,
				int theirValuePercent = 100) {
			// TODO: culture groups.
			// Attitude counts only through the markup on deals with a human
			// (see TradeMarkup), as TheNiceOne measured it. UNVERIFIED:
			// Zwingli's "AI Attitude and Trade" (Civ3 1.29,
			// https://forums.civfanatics.com/threads/ai-attitude-and-trade.59079/)
			// measured a little more asked below cautious too ("about 0.32%
			// more"), and https://forums.civfanatics.com/threads/aggression-level-ai-attitude-and-trading.699178/
			// shows prices changing with attitude but gives no formula.
			if (TradeOffer.ProblemWithDeal(gameData, other, this, theirOffer, ourOffer) != null) {
				return false;
			}
			// A civ that broke a deal it paid for over time isn't trusted to
			// pay over time again (see PlayerRelationship.TradingReputationBroken).
			if ((theirOffer.GivesOverTime() || TradeOffer.IncludesAllianceOrPact(theirOffer, ourOffer))
				&& TradingReputationBroken(this, other, gameData.players)) {
				return false;
			}
			// We don't deal with a civ we refuse to talk to, as for a while
			// after a war starts.
			if (!WillAcceptCommunicationFrom(other, gameData.turn)) {
				return false;
			}
			// Per the owner's decision, 2026-10-20: an AI never pays gold
			// per turn that would put its income in deficit.
			if (!isHuman && !TradeOffer.AffordsGoldPerTurn(this, ourOffer.goldPerTurn)) {
				return false;
			}
			int theirGoldValue = theirOffer.GoldEquivalentFor(gameData, this, counterpart: other);
			int ourGoldValue = ourOffer.GoldEquivalentFor(gameData, this, given: true, counterpart: other);
			// Against a human only: between AIs these cost civs techs (and
			// two or three cities over 200 turns) in AI trials, while what
			// they guard against is a human's exploits.
			if (!isHuman && other.isHuman) {
				if (C7Engine.AI.TradeGuards.Refuses(gameData, this, other, ourOffer)) {
					return false;
				}
				// Civ3's hard-coded markup on deals with a human (see
				// TradeMarkup): what we give costs base x (1 + markup), and
				// what we're given is worth base / (1 + markup).
				int markup = C7Engine.AI.TradeMarkup.Percent(gameData, this, other);
				ourGoldValue = C7Engine.AI.TradeMarkup.Asking(ourGoldValue, markup);
				theirGoldValue = C7Engine.AI.TradeMarkup.Paying(theirGoldValue, markup);
				ourGoldValue += C7Engine.AI.TradeGuards.MonopolyPremium(gameData, this, ourOffer);
				theirGoldValue -= C7Engine.AI.TradeGuards.RiskDiscount(gameData, other, theirOffer);
			}
			if (theirValuePercent != 100) {
				theirGoldValue = (int)Math.Clamp((long)theirGoldValue * theirValuePercent / 100, int.MinValue, int.MaxValue);
			}

			if (theirOffer.partOfPeaceTreaty || ourOffer.partOfPeaceTreaty) {
				return C7Engine.AI.PeaceAI.WouldAcceptPeace(gameData, this, other, theirOffer, ourOffer, theirGoldValue, ourGoldValue);
			}
			if (ourOffer.cities.Count > 0) {
				return false;
			}
			bool pacts = TradeOffer.IncludesAllianceOrPact(theirOffer, ourOffer) || TradeOffer.EmbargoTarget(theirOffer, ourOffer) != null;
			if (pacts && !C7Engine.AI.PactAI.WouldAccept(gameData, this, other, theirOffer, ourOffer, theirGoldValue, ourGoldValue)) {
				return false;
			}
			if (TradeOffer.IncludesRightOfPassage(theirOffer, ourOffer)) {
				return C7Engine.AI.RightOfPassageAI.WouldAccept(gameData, this, other, theirGoldValue, ourGoldValue);
			}
			return pacts || theirGoldValue >= ourGoldValue
				|| C7Engine.AI.AiTechPricing.TakesShortfall(gameData, this, other, theirOffer, ourOffer, theirGoldValue, ourGoldValue);
		}

		// Carries out a deal in which we give ourOffer and the other player
		// gives theirOffer. Returns false, changing nothing, if the deal can no
		// longer be made, such as when one side spent the gold it offered
		// while the other thought it over.
		//
		// A deal in which one side gives nothing is a gift, which the
		// receiver remembers fondly (see Attitudes), unless it is tribute.
		public bool ExecuteDeal(GameData gameData, Player other, TradeOffer theirOffer, TradeOffer ourOffer, bool tribute = false) {
			string problem = TradeOffer.ProblemWithDeal(gameData, other, this, theirOffer, ourOffer);
			if (problem != null) {
				log.Warning("Not executing a trade between {Player} and {Other}: {Problem}", this, other, problem);
				return false;
			}
			log.Information("Executing trade between {Player} and {Other}", this, other);
			log.Information("  {Player} gives {Offer}, worth {Gold} gold", this, ourOffer.ToString(), ourOffer.GoldEquivalentFor(gameData, other));
			log.Information("  {Other} gives {Offer}, worth {Gold} gold)", other, theirOffer.ToString(), theirOffer.GoldEquivalentFor(gameData, this));
			if (theirOffer.partOfPeaceTreaty || ourOffer.partOfPeaceTreaty) {
				SignPeaceAfterWar(this, other, gameData);
				Mobilization.OnWarEnded(this);
				Mobilization.OnWarEnded(other);
			}
			if (TradeOffer.IncludesRightOfPassage(theirOffer, ourOffer)) {
				RightOfPassage.Sign(gameData, this, other);
			}
			if (TradeOffer.IncludesMutualProtectionPact(theirOffer, ourOffer)) {
				Pacts.SignMutualProtectionPact(gameData, this, other);
			}
			if (TradeOffer.EmbargoTarget(theirOffer, ourOffer) is ID embargoed) {
				Pacts.SignTradeEmbargo(gameData, this, other, gameData.GetPlayer(embargoed));
			}

			if (ourOffer.gold.HasValue) {
				other.gold += ourOffer.gold.Value;
				this.gold -= ourOffer.gold.Value;
			}
			if (theirOffer.gold.HasValue) {
				this.gold += theirOffer.gold.Value;
				other.gold -= theirOffer.gold.Value;
			}

			C7Engine.AI.AiExperiments.TechSold(this, other, ourOffer, theirOffer);
			C7Engine.AI.AiExperiments.TechSold(other, this, theirOffer, ourOffer);

			// Gold per turn and resources are sent for the deal's length.
			EnsureRelationshipExists(other);
			other.EnsureRelationshipExists(this);
			RegisterDealsOverTime(gameData, this, other, ourOffer);
			RegisterDealsOverTime(gameData, other, this, theirOffer);

			// The attitudes, valuing the gifts before the techs are known.
			// "-1 if you have done any recent trades (maps or techs)" (AI
			// Attitude Study,
			// https://civfanatics.com/civ3/strategy/game-mechanics/ai-attitude-study/),
			// so a map counts as a tech does. UNVERIFIED (the study names
			// only maps and techs): that trading contacts doesn't.
			if (ourOffer.techs.Count > 0 || theirOffer.techs.Count > 0
				|| ourOffer.worldMap || ourOffer.territoryMap || theirOffer.worldMap || theirOffer.territoryMap) {
				playerRelationships[other.id].lastTechTradeTurn = gameData.turn;
				other.playerRelationships[id].lastTechTradeTurn = gameData.turn;
			}
			if (!tribute && ourOffer.IsEmpty() && !theirOffer.IsEmpty()) {
				Attitudes.RecordGift(playerRelationships[other.id], theirOffer.GoldEquivalentFor(gameData, this, counterpart: other), gameData.turn);
			} else if (!tribute && theirOffer.IsEmpty() && !ourOffer.IsEmpty()) {
				Attitudes.RecordGift(other.playerRelationships[id], ourOffer.GoldEquivalentFor(gameData, other, counterpart: this), gameData.turn);
			}

			// Maps and introductions, once valued.
			GiveMapsAndContacts(this, other, ourOffer);
			GiveMapsAndContacts(other, this, theirOffer);

			// Techs bought (for gold or gold per turn), for AI trials.
			if (ourOffer.techs.Count > 0 && (theirOffer.gold > 0 || theirOffer.goldPerTurn > 0)) {
				C7Engine.AI.AiExperiments.onTechsBought?.Invoke(other, ourOffer.techs.Count);
			}
			if (theirOffer.techs.Count > 0 && (ourOffer.gold > 0 || ourOffer.goldPerTurn > 0)) {
				C7Engine.AI.AiExperiments.onTechsBought?.Invoke(this, theirOffer.techs.Count);
			}
			other.CompleteResearchAndBeginNew(gameData, ourOffer.techs);
			this.CompleteResearchAndBeginNew(gameData, theirOffer.techs);

			// An alliance last, as it may start wars.
			if (TradeOffer.AllianceTarget(theirOffer, ourOffer) is ID enemy) {
				Pacts.SignMilitaryAlliance(gameData, this, other, gameData.GetPlayer(enemy));
			}

			// Workers go to the receiver's capital as slave workers (see
			// TradeOffer.workers).
			TradeWorkers(gameData, this, other, ourOffer);
			TradeWorkers(gameData, other, this, theirOffer);

			// Cities change hands last, once the gold and techs have.
			foreach (City city in ourOffer.cities.ToList()) {
				CityInteractions.TransferCity(city, other, viaDeal: true);
			}
			foreach (City city in theirOffer.cities.ToList()) {
				CityInteractions.TransferCity(city, this, viaDeal: true);
			}
			return true;
		}

		private static void TradeWorkers(GameData gameData, Player giver, Player receiver, TradeOffer offer) {
			Tile capital = receiver.cities.Find(c => c.IsCapital())?.location;
			foreach (MapUnit worker in offer.workers ?? new()) {
				if (worker.owner == giver) {
					gameData.TradeWorker(worker, receiver, capital);
				}
			}
		}

		private static void GiveMapsAndContacts(Player giver, Player receiver, TradeOffer offer) {
			if (offer.worldMap || offer.territoryMap) {
				TradeOffer.GiveMap(giver, receiver, territoryOnly: !offer.worldMap);
			}
			foreach (ID id in offer.contacts ?? new()) {
				Player third = EngineStorage.gameData?.GetPlayer(id);
				if (third != null) {
					receiver.EnsureRelationshipExists(third);
				}
			}
		}

		// Registers what the giver sends the receiver over time in a deal
		// just made, as the giver's Outbound deals (and the receiver's
		// Inbound ones), for the rules' deal length from this turn. They end
		// in PlayerRelationship.CheckForObsoleteDeals; the gold is paid in
		// DoPerTurnFinanceUpdates, and the resources reach the receiver's
		// cities through City.GetAvailableResources.
		private static void RegisterDealsOverTime(GameData gameData, Player giver, Player receiver, TradeOffer offer) {
			int duration = TradeOffer.DealDuration(gameData);
			if (offer.goldPerTurn > 0) {
				RegisterMultiTurnDeal(giver, receiver, new MultiTurnDeal(DealType.Gold, DealSubType.GoldPerTurn,
					DealDetails.Outbound, offer.goldPerTurn, null, duration, gameData.turn));
			}
			foreach (string key in offer.resources) {
				Resource r = gameData.Resources.Find(x => x.Key == key);
				bool luxury = r?.Category == ResourceCategory.LUXURY;
				RegisterMultiTurnDeal(giver, receiver, new MultiTurnDeal(luxury ? DealType.Luxury : DealType.Resource,
					luxury ? DealSubType.LuxuryPerTurn : DealSubType.ResourcePerTurn,
					DealDetails.Outbound, 0, key, duration, gameData.turn));
			}
		}

		// The beakers all our cities produce each turn.
		public int BeakersPerTurn() {
			int beakersPerTurn = 0;
			foreach (City city in cities) {
				beakersPerTurn += city.CurrentCommerceYield().beakers;
			}
			return beakersPerTurn + CityStateTrade.Beakers(EngineStorage.gameData, this);
		}

		public int EstimateTurnsToResearch(GameData gameData, Tech tech) {
			return EstimateTurnsToResearch(gameData, tech, BeakersPerTurn());
		}

		// Like EstimateTurnsToResearch, with the beakers per turn (from
		// BeakersPerTurn) computed once by a caller estimating many techs.
		//
		// This is the number of turns until the tech is learned: research is
		// paid for over BeakerTurnsLeft turns, and the tech arrives one turn
		// later, as "a tech is gained on the turn *after* progress >= cost"
		// (Perez's decompile of Civ3 Conquests,
		// doc/perez-findings/Civ3_Findings.md, Round 2b "Research";
		// authoritative per the owner's 2026-11-12 ruling). See
		// CompletePaidResearch.
		public int EstimateTurnsToResearch(GameData gameData, Tech tech, int beakersPerTurn) {
			int turns = BeakerTurnsLeft(gameData, tech, beakersPerTurn);
			return turns == int.MaxValue ? int.MaxValue : turns + 1;
		}

		// How many more turns of beakers the tech needs before it is paid
		// for, within the rules' minimum and maximum research times; 0 once
		// it is paid for, int.MaxValue if no research is happening.
		private int BeakerTurnsLeft(GameData gameData, Tech tech, int beakersPerTurn) {
			int remainingCost = gameData.TechCostFor(tech, this);
			if (remainingCost > 0 && beakersPerTurn == 0) {
				// No research is happening.
				return int.MaxValue;
			}

			// A tech that's already paid for is done, even with no science.
			int turnsRemaining = remainingCost <= 0 ? 0 : (int)Math.Ceiling((double)remainingCost / beakersPerTurn);

			// Only the tech being researched has turns spent on it.
			int turnsSpent = currentlyResearchedTech == tech.id ? turnsResearched : 0;
			int maxTurnsRemaining = rules.MaximumResearchTime - turnsSpent;
			int minTurnsRemaining = rules.MinimumResearchTime - turnsSpent;

			int result = Math.Min(turnsRemaining, maxTurnsRemaining);
			result = Math.Max(result, minTurnsRemaining);

			return Math.Max(result, 0);
		}

		public string SummarizeScience(GameData gD) {
			Tech tech = Tech.FindById(gD.techs, currentlyResearchedTech);
			if (tech == null) {
				return SummarizeFutureTech(gD, BeakersPerTurn()) ?? "Not selected (-- turns)";
			}
			return SummarizeScience(tech, EstimateTurnsToResearch(gD, tech));
		}

		// "Future Tech 3 (5 turns)" once every advance is known, else null.
		private string SummarizeFutureTech(GameData gD, int beakersPerTurn) {
			if (!KnowsEveryTech(gD)) {
				return null;
			}
			int turns = EstimateTurnsToFutureTech(gD, beakersPerTurn);
			return turns == int.MaxValue ? $"Future Tech {futureTechs + 1} (-- turns)" : $"Future Tech {futureTechs + 1} ({Math.Max(turns, 1)} turns)";
		}

		// Like SummarizeScience, with the beakers per turn (from
		// BeakersPerTurn) computed once by the caller.
		public string SummarizeScience(GameData gD, int beakersPerTurn) {
			Tech tech = Tech.FindById(gD.techs, currentlyResearchedTech);
			if (tech == null) {
				return SummarizeFutureTech(gD, beakersPerTurn) ?? "Not selected (-- turns)";
			}
			return SummarizeScience(tech, EstimateTurnsToResearch(gD, tech, beakersPerTurn));
		}

		private static string SummarizeScience(Tech tech, int turns) {
			if (turns == int.MaxValue) {
				return $"{tech.Name} (-- turns)";
			}

			return $"{tech.Name} ({turns} turns)";
		}

		// Get a fresh research queue that overrides any current or previous queues.
		public void CalculateFreshTechQueueAndAssignNewCurrent(Tech tech) {
			ResearchQueue = GetResearchQueueFor(tech);

			if (ResearchQueue.Count > 0)
				SetCurrentlyResearchedTech(ResearchQueue.Peek().id);
		}

		// Append a new queue at the tail end of the current queue
		public void CalculateTechQueueAndAppendToCurrentQueue(Tech tech) {
			HashSet<Tech> alreadyQueued = new(ResearchQueue);
			foreach (Tech t in GetResearchQueueFor(tech)) {
				if (alreadyQueued.Add(t)) {
					AddTechItemToResearchQueue(t);
				}
			}
		}

		// The techs to research to reach the given one, in research order:
		// depth first, every unknown prerequisite comes before the tech that
		// needs it, and the given tech comes last. Adapted from
		// GBiebs/OpenCiv3.
		private Queue<Tech> GetResearchQueueFor(Tech tech) {
			Queue<Tech> queue = new();
			if (tech == null) {
				return queue;
			}
			HashSet<Tech> visited = new() { tech };
			foreach (Tech t in OrderTechs(tech.Prerequisites)) {
				AddWithPrerequisites(t, queue, visited);
			}
			queue.Enqueue(tech);
			return queue;
		}

		// visited holds every tech this has been called for. Once a call for
		// a tech returns, it and every unknown tech it leads back to are in
		// the queue, so calling it again would add nothing; skipping it keeps
		// a tech tree with many paths to the same tech (which the full tree
		// has) from being walked once per path.
		private void AddWithPrerequisites(Tech tech, Queue<Tech> queue, HashSet<Tech> visited) {
			if (knownTechs.Contains(tech.id) || !visited.Add(tech)) {
				return;
			}
			foreach (Tech t in OrderTechs(tech.Prerequisites)) {
				AddWithPrerequisites(t, queue, visited);
			}
			queue.Enqueue(tech);
		}

		/// <summary>
		/// Takes all the techs in the game and keeps only what could be researched next at a particular point in the game.
		/// </summary>
		/// <param name="allTechs"></param>
		/// <returns>The techs, which enumerate in research priority order.</returns>
		public OrderedTechSet GetAvailableTechsToResearch(List<Tech> allTechs) {
			HashSet<Tech> result = new();
			foreach (Tech tech in allTechs) {
				if (knownTechs.Contains(tech.id) || tech.EraCivilopediaName == EraUtils.HIDDEN_CVLPD) {
					continue;
				}
				if (GetEraIndex(tech.EraCivilopediaName) > EraIndex()) {
					continue;
				}

				bool prereqsKnown = true;
				foreach (Tech prereq in tech.Prerequisites) {
					if (!knownTechs.Contains(prereq.id)) {
						prereqsKnown = false;
						break;
					}
				}
				if (prereqsKnown) {
					result.Add(tech);
				}
			}
			return new OrderedTechSet(OrderTechs(result.ToList()));
		}

		/// <summary>
		/// The techs this player knows that it could trade to the recipient: only
		/// techs the recipient could research right now.
		/// </summary>
		public List<Tech> GetTechsTradableTo(Player recipient, List<Tech> allTechs) {
			OrderedTechSet researchable = recipient.GetAvailableTechsToResearch(allTechs);
			// A tech with the BIQ's "Cannot be traded" flag is never offered
			// (https://forums.civfanatics.com/threads/civ3-conquests-editor-features.67911/).
			// The AI's trades, the deal screen and TradeOffer's validation all
			// go through here.
			return allTechs.FindAll(t => knownTechs.Contains(t.id) && researchable.Contains(t) && !t.CannotBeTraded);
		}

		// Placeholder ordering of techs. Duplicates are dropped, keeping the
		// first.
		private static List<Tech> OrderTechs(List<Tech> techs) {
			if (techs == null || techs.Count == 0) {
				return new List<Tech>();
			}
			// TODO: We would want to eventually order them based on how the AI would do it
			// Details on how Civ3 does it: https://forums.civfanatics.com/threads/what-will-the-ai-research-next.45559/
			HashSet<Tech> seen = new();
			List<Tech> result = new();
			foreach (Tech t in techs.OrderBy(t => t.Cost)) {
				if (seen.Add(t)) {
					result.Add(t);
				}
			}
			return result;
		}

		public List<Government> GetAvailableGovernments(GameData gameData) {
			List<Government> result = new();
			foreach (Government g in gameData.governments) {
				// You can't deliberately switch into the transition type, you
				// have to switch from one non-transitional type to another.
				if (g.transitionType) {
					continue;
				}

				// A scenario may allow only one (Rules.OnlyGovernment).
				if (gameData.rules?.AllowsGovernment(g) == false) {
					continue;
				}

				if (this.HasTech(g.prerequisiteTech)) {

					result.Add(g);
				}
			}
			return result;
		}

		// Whether the player may start a revolution: only with another
		// government to change to. script.txt, #REVOLUTIONNOTAVAILABLE: "We
		// cannot have a revolution until we discover different forms of
		// government."
		public const string RevolutionNotAvailable = "We cannot have a revolution until we discover different forms of government.";

		public bool CanStartRevolution(GameData gameData) {
			return GetAvailableGovernments(gameData).Any(g => g != government);
		}

		// Whether the player knows any tech matching the predicate, e.g. one
		// with a rule flag like Tech.EnablesIrrigationEverywhere.
		[MoonSharpHidden]
		public bool KnowsTechWhere(Func<Tech, bool> predicate) {
			List<Tech> techs = EngineStorage.gameData?.techs;
			if (techs == null) {
				return false;
			}
			foreach (Tech t in techs) {
				if (predicate(t) && knownTechs.Contains(t.id)) {
					return true;
				}
			}
			return false;
		}

		// Whether the player knows a tech with the BIQ flag, e.g.
		// HasTechEnabling(SaveTech.Flag.EnablesMapTrading).
		[MoonSharpHidden]
		public bool HasTechEnabling(SaveTech.Flag flag) {
			return KnowsTechWhere(t => t.HasFlag(flag));
		}

		// Whether the player knows the tech needed for embassies, or the
		// rules have none (see Tech.FindEmbassyTech).
		public bool KnowsEmbassyTech(List<Tech> techs) {
			Tech tech = Tech.FindEmbassyTech(techs);
			return tech == null || knownTechs.Contains(tech.id);
		}

		public bool HasTech(ID techId) {
			bool hasTech = techId == null || this.knownTechs.Contains(techId);
			return hasTech;
		}

		// See https://forums.civfanatics.com/threads/everything-about-corruption-c3c-edition.76619/
		//
		// This is the empire-wide information factored out of the rank corruption
		// calculations so it can be reused for anarchy calculations.
		public int GetAdjustedOptimalCityNumber(GameData gameData) {
			int mapOptimalCityNumber = gameData.map.optimalNumberOfCities;
			// The difficulty level's percentage applies to humans; the AI uses
			// Regent's, as it plays by Regent's rules (see
			// GameData.AiRulesDifficulty). Against 24k cities from Civ3 saves,
			// the game's own level made AI corruption far too high on the hard
			// levels, and Regent's (90%) fits better than the full number:
			// exact corruption matches 77.9% against 74.5%, waste 85.4%
			// against 84.2%. The project owner chose Regent's (2026-10-20).
			int percentOptimalCities = isHuman ? gameData.gameDifficulty.PercentageOfOptimalCities : gameData.AiRulesDifficulty().PercentageOfOptimalCities;

			// "The OCN (Optimal City Number) is increased 25% for Commercial
			// civilizations" (https://civfanatics.com/civ3/faq/), which the
			// executable adds to the map's number before the percentage, in
			// whole cities: "n_com = floor(b/4) if L has the trait else 0" in
			// "ocn = max (1, floor(s_diff * (b + n_fp + n_com + n_gov + n_ai)
			// / 100))" (C3X's notes on the stock formula,
			// https://github.com/maxpetul/C3X/blob/master/Notes/ocn_formula.txt).
			// The government and Forbidden Palace terms follow the same
			// notes: "n_gov = floor(b/8) if L's govt has minimal or nuisance
			// corruption | floor(b/16) if problematic | 2*b if communal",
			// "n_fp = floor((3*f*b) / d)", d 8, or 1 under communal
			// corruption; each term floored. The project owner chose the
			// executable's government term over alexman's 0.1 and 0
			// (2026-10-20); against 24k cities from Civ3 saves, exact
			// corruption matches 78.1% against 77.9%. The notes' AI-only
			// term ("n_ai", more cities on the harder levels) is left out:
			// with it, exact corruption matches fell to 69.3%.
			int commercialCities = civilization.traits.Contains(Civilization.Trait.Commercial) ? mapOptimalCityNumber / 4 : 0;

			// The Secret Police HQ carries the BIQ's Forbidden Palace flag, so
			// it counts here like a Forbidden Palace, under Communism (see
			// Building.WorksAsForbiddenPalaceFor).
			int numCorruptionReducingSmallWondersInEmpire = 0;
			foreach (City c in cities) {
				// We use constructed_buildings here because great wonders can't
				// supply small wonders like forbidden palaces, so we can avoid
				// doing extra work for each city.
				foreach (CityBuilding cb in c.constructed_buildings) {
					if (cb.building.WorksAsForbiddenPalaceFor(this) && cb.building.TakesEffectFor(this)) {
						++numCorruptionReducingSmallWondersInEmpire;
					}
				}
			}

			float communalCorruptionFactor =
				government.corruptionType == Government.CorruptionType.Communal ? 3.0f : 3.0f/8.0f;

			int b = mapOptimalCityNumber;
			int forbiddenPalaceCities = (int)Math.Floor(communalCorruptionFactor * numCorruptionReducingSmallWondersInEmpire * b + 1e-6);
			int governmentCities = government.corruptionType switch {
				Government.CorruptionType.Minimal or Government.CorruptionType.Nuisance => b / 8,
				Government.CorruptionType.Problematic => b / 16,
				Government.CorruptionType.Communal => 2 * b,
				_ => 0,
			};
			double optimal = b + forbiddenPalaceCities + commercialCities + governmentCities;
			// Rounded down, the epsilon keeping 90% of 30 at 27.
			return (int)Math.Floor(percentOptimalCities * optimal / 100 + 1e-6);
		}

		// Notes:
		//  - see https://www.civforum.de/showthread.php?3153-Anarchie-Wieviel-Runden-welche-Strategie&p=67682&viewfull=1#post67682 (in German)
		//    This claims Soren Johnson said the time is
		//    1-5 years, random + 0-3 years, depending on the number of cities.
		//    I think the 1-5 is actually 2 to 6, since the min is 2.
		//
		//  - https://forums.civfanatics.com/threads/frequently-asked-questions-civ3-play-the-world-conquests.170282/
		//    For Religious civilizations, anarchy only lasts 1 turn in Vanilla
		//    and Play the World, and 2 turns in Conquests. For non-Religious
		//    civilizations, the formula is: 1 (2 for Conquests) + random number
		//    between 1-4 + number between 0-3 depending on size of your empire.
		//
		//  - https://civfanatics.com/civ3/faq/ confirms religious civs get 2
		//    turns in Conquests, and
		//    https://forums.civfanatics.com/threads/duration-of-anarchy.683384/
		//    measured Conquests, finding the FAQ slightly off: "Very small
		//    empires incur 2 to 6 turns of anarchy", "Small empires incur 3 to
		//    7 turns of anarchy, mid sized empires 4 to 8 turns and larger
		//    empires 5 to 9 turns", "Religious tribes always incur 2 turns of
		//    anarchy". That is 2 + a random 0 to 4 + 0 to 3 for the empire's
		//    size, which we follow.
		//
		// How the empire size maps to 0-3 isn't documented: UNVERIFIED (no
		// Civ3 source found), we scale it by the optimal city number.
		// The Conquests base length of anarchy, which is all a religious civ
		// suffers.
		private const int AnarchyBaseTurns = 2;

		public int GetTurnsOfAnarchyForTransition(GameData gameData) {
			if (civilization.traits.Contains(Civilization.Trait.Religious)) {
				return AnarchyBaseTurns;
			}

			// Conquests, as measured above: a base of 2 plus a random 0 to 4,
			// so 2 to 6 turns before the empire size is counted.
			int randomPortion = AnarchyBaseTurns + GameData.rng.Next(5);

			// Now we use the OCN to determine the city factor, which is between
			// 0 and 3. This means that sprawling empires will have longer
			// anarchy, but this will scale with map size.
			float numCities = cities.Count;
			int cityPortion = (int)Math.Min(3f, 3f * numCities / GetAdjustedOptimalCityNumber(gameData));

			// The result is the sume of the random portion and the city portion,
			// with the AI cap applied if relevant for this difficulty.
			//
			// Note that this AI transition time is applied after the early
			// return for religious civs. There is a known strategy on higher
			// difficulty levels of using religious civs to help cut down on the
			// AI advantage due to this fact.
			int result = randomPortion + cityPortion;
			if (!isHuman && gameData.AiBonusDifficulty(this).MaxAiGovernmentTransitionTime > 0) {
				result = Math.Min(result, gameData.AiBonusDifficulty(this).MaxAiGovernmentTransitionTime);
			}
			return result;
		}

		public void DoPerTurnFinanceUpdates(GameData gameData) {
			if (IsIndependent) {
				return;
			}

			// Process per-city contributions. Each city's flows are computed
			// once: disbanding a unit only changes the unit support cost, and
			// losing an improvement only changes the flows of its own city
			// (wonders, small wonders and the palace are never lost here).
			CityFlows[] cityFlows = new CityFlows[cities.Count];
			int beakersThisTurn = 0;
			for (int i = 0; i < cities.Count; ++i) {
				cityFlows[i] = ComputeCityFlows(cities[i]);
				beakersThisTurn += cityFlows[i].commerce.beakers;
			}
			// Kings & Nomads' city-states' specialists (see CityStates).
			beakersThisTurn += CityStateTrade.Beakers(gameData, this);
			beakersThisTurn = WithScienceAgeBonus(beakersThisTurn);
			beakers += beakersThisTurn;
			researchFundedThisTurn = beakersThisTurn > 0;
			int unitSupportCost = TotalUnitsAllowedUnitsAndSupportCost().Item3;

			// As in Civ 3, a deficit is fine while the treasury can pay for it.
			// When it can't, a unit is disbanded or city improvements are
			// lost, but the sliders are left alone.
			//
			// Which upkeep is paid first is random, and whichever is paid
			// second takes the shortfall. The C3X mod notes that the building
			// upkeep sum "is called in two places as part of the randomization
			// of whether improv or unit maintenance gets paid first"
			// (https://github.com/maxpetul/C3X/blob/master/injected_code.c).
			// It's rolled only when the treasury falls short, so other turns
			// leave the random numbers alone.
			//
			// What is lost, from Flintlock's reading of the 1.22 code
			// (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/):
			// buildings go one at a time from a random city, "There's a method
			// in the game whose job is to delete a building from a random city"
			// / "The buildings are simply deleted." (post-16789620), so no gold
			// comes back; and "The game will only disband at most one unit per
			// turn due to failure to pay maintenance." (post-16606869), so an
			// empire short on unit upkeep can stay short for several turns.
			List<string> disbandedUnits = new();
			List<string> lostImprovements = new();
			if (gold + GoldPerTurnFrom(cityFlows, unitSupportCost) < 0) {
				bool buildingsPaidFirst = GameData.rng.Next(2) == 0;
				if (buildingsPaidFirst) {
					// Building upkeep is short only if the treasury can't
					// cover it before units are paid for.
					while (gold + GoldPerTurnFrom(cityFlows, unitSupportCost) + unitSupportCost < 0 && TryLoseImprovement()) { }
					if (gold + GoldPerTurnFrom(cityFlows, unitSupportCost) < 0) {
						TryDisbandUnsupportedUnit();
					}
				} else {
					if (gold + GoldPerTurnFrom(cityFlows, unitSupportCost) + BuildingUpkeep() < 0) {
						TryDisbandUnsupportedUnit();
					}
					while (gold + GoldPerTurnFrom(cityFlows, unitSupportCost) < 0 && TryLoseImprovement()) { }
				}
				if (gold + GoldPerTurnFrom(cityFlows, unitSupportCost) < 0) {
					// Units still unpaid wait for next turn's disbanding, and
					// a deficit from gold-per-turn deals has nothing to give
					// up. The treasury bottoms out at zero.
					log.Information("{Player} is still short of gold after paying upkeep (gold={Gold}, gpt={GoldPerTurn})",
						this, gold, GoldPerTurnFrom(cityFlows, unitSupportCost));
				}
			}

			int BuildingUpkeep() {
				int result = 0;
				foreach (CityFlows flows in cityFlows) {
					result += flows.maintenance;
				}
				return result;
			}

			// Disbands the least valuable unit that needs support. Captives and
			// units of types that need no support are free, so disbanding one
			// wouldn't lower the support cost.
			void TryDisbandUnsupportedUnit() {
				if (unitSupportCost <= 0) {
					return;
				}
				MapUnit unitToRemove = LeastValuableSupportedUnit();
				if (unitToRemove == null) {
					return;
				}
				log.Information("{Player} is out of gold, disbanding {Unit} at {Location}", this, unitToRemove, unitToRemove.location);
				disbandedUnits.Add(unitToRemove.name);
				gameData.RemoveUnit(unitToRemove);
				unitSupportCost = TotalUnitsAllowedUnitsAndSupportCost().Item3;
			}

			// Deletes an improvement from a random city that has one to lose.
			bool TryLoseImprovement() {
				(City city, CityBuilding building) = ImprovementToLoseInRandomCity();
				if (city == null) {
					return false;
				}
				log.Information("{Player} is out of gold, losing the {Building} in {City}", this, building.building.name, city);
				lostImprovements.Add($"{building.building.name} in {city.name}");
				city.RemoveBuilding(building);
				new MsgCityChanged(city).send();
				cityFlows[cities.IndexOf(city)] = ComputeCityFlows(city);
				unitSupportCost = TotalUnitsAllowedUnitsAndSupportCost().Item3;
				return true;
			}

			if (IsToldNews && lostImprovements.Count > 0) {
				new MsgShowDomesticAdvisorPopup(this,
					$"We can no longer support our {string.Join(", ", lostImprovements)}.\nWe must think more about our treasury!").send();
			}
			if (IsToldNews && disbandedUnits.Count > 0) {
				new MsgShowMilitaryAdvisorPopup(this,
					$"We have insufficient gold to continue supporting all our units.\nWe had to disband: {string.Join(", ", disbandedUnits)}.", happy: false).send();
			}

			PlayerCommerceBreakdown turnFlows = FlowsFrom(cityFlows, unitSupportCost);
			stats.RecordFinances(turnFlows);
			lastGoldPerTurn = turnFlows.Netflows();
			gold = Math.Max(0, gold + lastGoldPerTurn);
		}

		// Whether the unit counts against the support limit: not a captive,
		// and of a type that requires support.
		private static bool NeedsSupport(MapUnit u) => !u.IsCaptive() && u.unitType.requiresSupport;

		// The unit to disband when unit upkeep can't be paid. Flintlock
		// (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16356899):
		// "The game tries to pick the least valuable unit to disband",
		// avoiding captured units, King units and armies, with ties broken by
		// list order. How the game values a unit isn't given; we use its
		// shield cost. Captives cost nothing here anyway, Kings are never
		// picked, and armies (and their members) go only when nothing else
		// is left. The stock search "forgot about the requires support flag"
		// (post-16874607), a bug we don't copy: such units are free.
		private MapUnit LeastValuableSupportedUnit() {
			MapUnit best = null;
			bool bestInArmy = false;
			foreach (MapUnit u in units) {
				if (!NeedsSupport(u) || u.unitType.isKing) {
					continue;
				}
				bool inArmy = u.IsArmy() || u.IsInArmy();
				if (best == null
					|| (bestInArmy && !inArmy)
					|| (bestInArmy == inArmy && u.unitType.shieldCost < best.unitType.shieldCost)) {
					best = u;
					bestInArmy = inArmy;
				}
			}
			return best;
		}

		// An improvement (not the palace or a wonder) that costs upkeep, from a
		// random city that has one, or (null, null) if nothing costs
		// anything. Which of the city's buildings the game deletes isn't
		// known (compendium 02, open question 7); we take its most expensive.
		private (City, CityBuilding) ImprovementToLoseInRandomCity() {
			List<(City, CityBuilding)> choices = new();
			foreach (City c in cities) {
				CityBuilding best = null;
				foreach (CityBuilding cb in c.constructed_buildings) {
					Building b = cb.building;
					if (b.maintenanceCost <= 0 || b.isCenterOfEmpire || b.isSmallWonder || b.IsGreatWonder()) {
						continue;
					}
					if (best == null || b.maintenanceCost > best.building.maintenanceCost) {
						best = cb;
					}
				}
				if (best != null) {
					choices.Add((c, best));
				}
			}
			if (choices.Count == 0) {
				return (null, null);
			}
			return choices[choices.Count == 1 ? 0 : GameData.rng.Next(choices.Count)];
		}

		public void HandleCityUpdates(GameData gameData) {
			// A copy, since a city may starve to nothing on the way.
			foreach (City c in cities.ToList()) {
				// Barbarian cities (only a scenario can give barbarians one)
				// build nothing and gain no culture: in Debug mode "The
				// Barbarian city was not able to build anything (no buildings
				// and no units)" and its "culture ... always stayed at zero"
				// (Civinator,
				// https://forums.civfanatics.com/threads/barbarian-cities-in-civ-3.646253/).
				bool active = !IsIndependent;

				// Ensure borders expand before we assign the new citizen, so that
				// the new citizen can go on one of our new tiles.
				if (active && c.UpdateCultureAndCheckForExpansion()) {
					gameData.UpdateTileOwners();
					BarbarianInteractions.DisperseCampsWithinBorders(gameData);
					GoodyHuts.OpenWithinBorders(gameData, c);

					// Update the trade network if borders expanded, as a new
					// resource may be part of the network.
					EngineStorage.gameData.InvalidateCachedTradeNetwork();
				}

				c.UpdateResistance(gameData);
				int sizeBefore = c.residents.Count;
				bool starvedOut = c.HandleCityGrowth(gameData);
				if ((starvedOut || c.residents.Count < sizeBefore) && IsToldNews) {
					// Civ3's Script.txt, STARVE.
					new MsgShowDomesticAdvisorPopup(this, $"Starvation at {c.name}!").send();
				}
				if (starvedOut) {
					CityInteractions.DestroyCity(c);
					continue;
				}
				if (active) {
					c.HandleCityProduction(gameData);
				}
				Tile polluted = c.MaybePollute(gameData);
				if (polluted != null && IsToldNews) {
					// Civ3's Script.txt, POLLUTION.
					new MsgShowDomesticAdvisorPopup(this, $"Pollution strikes {c.name}.").send();
				}
				Disease.MaybeStrike(c, gameData.turn);
			}
			Disease.StrikeFortifiedUnits(this);
		}

		// Learns the tech being researched if an earlier turn's beakers paid
		// for it. Runs at the start of the turn's processing, before that
		// turn's beakers are added (DoPerTurnFinanceUpdates), so a tech whose
		// progress reaches its cost is learned the turn after: "a tech is
		// gained on the turn *after* progress >= cost" (Perez's decompile of
		// Civ3 Conquests, doc/perez-findings/Civ3_Findings.md, Round 2b
		// "Research"; authoritative per the owner's 2026-11-12 ruling).
		// Research has no overflow: beakers beyond the cost are lost
		// (CompleteResearchingTech resets them), and the turn's new beakers go
		// to the next tech (see SetCurrentlyResearchedTech for a human who
		// has yet to choose it).
		public void CompletePaidResearch(GameData gameData) {
			if (currentlyResearchedTech == null) {
				ResearchFutureTech(gameData);
				return;
			}
			Tech tech = Tech.FindById(gameData.techs, currentlyResearchedTech);
			// Whether it's paid for doesn't depend on this turn's beakers (any
			// positive rate gives the same answer), only on the beakers stored
			// and the turns spent.
			if (tech == null || BeakerTurnsLeft(gameData, tech, beakersPerTurn: 1) > 0) {
				return;
			}

			// Being the first to research a tech may bring a scientific
			// leader (see MapUnit_ScientificLeader.cs).
			bool first = IsFirstToLearn(gameData, tech);
			CompleteResearchAndBeginNew(gameData, tech);
			if (first) {
				MapUnit.RollForScientificLeader(gameData, this, tech);
			}
		}

		// Counts the turn's research towards the maximum research time, once
		// DoPerTurnFinanceUpdates has added the turn's beakers. A tech paid
		// for this turn is learned at the next turn's CompletePaidResearch. A
		// turn funded while a human chooses the next tech counts for the tech
		// they choose (see SetCurrentlyResearchedTech).
		public void DoPerTurnScienceUpdates(GameData gameData) {
			// Only turns that put beakers into the tech count towards the
			// maximum research time: "You cannot use up more than 50 turns of
			// actual research, no matter how little you spend, as long as you
			// spend more than zero"
			// (https://forums.civfanatics.com/threads/the-research-slider.662300/).
			// Otherwise a civ that stopped funding research would, once it
			// started again, finish the tech at once because the clamp had
			// run out.
			if (researchFundedThisTurn) {
				turnsResearched++;
			}
			researchFundedThisTurn = false;
		}

		// Whether there's no advance left to learn.
		public bool KnowsEveryTech(GameData gameData) {
			return gameData.techs.Count > 0 && gameData.techs.All(t => knownTechs.Contains(t.id));
		}

		// With every advance known, research goes on to future techs: "When
		// your civilization accumulates enough scientific research to finish
		// one unit of Future Technology, you can research another" (Civ3
		// manual, p.125). Each takes FutureTechCostFor's beakers, within the
		// rules' least and most turns of research like an advance.
		private void ResearchFutureTech(GameData gameData) {
			if (!KnowsEveryTech(gameData)) {
				return;
			}
			// Like an advance (see CompletePaidResearch), it is learned the
			// turn after it is paid for, whatever this turn's science; the
			// turns of research are counted by DoPerTurnScienceUpdates.
			if (FutureTechBeakerTurnsLeft(gameData, beakersPerTurn: 1) > 0) {
				return;
			}
			futureTechs++;
			beakers = 0;
			turnsResearched = 0;
			log.Information("{Player} discovered Future Tech {Number}", this, futureTechs);
			if (IsToldNews) {
				new MsgShowScienceAdvisorPopup(this, $"Our scientists have discovered Future Tech {futureTechs}!",
					MsgShowScienceAdvisorPopup.Mood.Happy).send();
			}
		}

		// Like EstimateTurnsToResearch, for the next future tech: the turns
		// until it is learned, one more than the turns of beakers it needs.
		public int EstimateTurnsToFutureTech(GameData gameData, int beakersPerTurn) {
			int turns = FutureTechBeakerTurnsLeft(gameData, beakersPerTurn);
			return turns == int.MaxValue ? int.MaxValue : turns + 1;
		}

		// Like BeakerTurnsLeft, for the next future tech.
		private int FutureTechBeakerTurnsLeft(GameData gameData, int beakersPerTurn) {
			int remainingCost = Math.Max(gameData.FutureTechCostFor(this) - beakers, 0);
			if (remainingCost > 0 && beakersPerTurn <= 0) {
				// No research is happening.
				return int.MaxValue;
			}
			int turnsRemaining = remainingCost <= 0 ? 0 : (int)Math.Ceiling((double)remainingCost / beakersPerTurn);
			turnsRemaining = Math.Min(turnsRemaining, rules.MaximumResearchTime - turnsResearched);
			turnsRemaining = Math.Max(turnsRemaining, rules.MinimumResearchTime - turnsResearched);
			return Math.Max(turnsRemaining, 0);
		}

		// Whether no other civ knows the tech yet. Barbarians don't count.
		internal bool IsFirstToLearn(GameData gameData, Tech tech) {
			return !gameData.players.Any(p => p != this && !p.IsIndependent && p.knownTechs.Contains(tech.id));
		}

		// The Great Library: each turn, while it is active, its owner learns
		// every tech known by at least two other civs it has contact with.
		//
		// Assumptions, as the exact Civ3 rule isn't documented in detail:
		//  - "contact" means we and the civ have a relationship with each
		//    other (we have met), regardless of whether we are at war with
		//    them (see below);
		//  - barbarians and defeated civs don't count;
		//  - techs are checked once a turn "in the order in which they are
		//    listed in the editor", and "You can only gain those techs that
		//    you have the prerequisite for" (justanick,
		//    https://forums.civfanatics.com/threads/quick-answers-newbie-questions.318752/page-275):
		//    a tech granted earlier in the pass counts as a prerequisite for
		//    one later in it. Techs of no era never come from it ("Fixed a
		//    bug with non-era techs that allowed players to potentially
		//    receive them for free from the Great Library", PTW readme).
		//  - the era doesn't hold them back: per the project owner
		//    (2026-10-20), a captured Great Library can take its new owner
		//    past Education, as far as the other civs have progressed, before
		//    it goes obsolete (which is checked after the turn's grants).
		// Returns the techs learned.
		public List<Tech> DoGreatLibraryUpdates(GameData gameData) {
			List<Tech> learned = new();
			if (IsIndependent || defeated) {
				return learned;
			}

			City libraryCity = null;
			Building library = null;
			foreach ((City c, CityBuilding cb) in GetBuildingSnapshot().activeWonders) {
				if (cb.building.gainAnyTechKnownByTwoCivs) {
					libraryCity = c;
					library = cb.building;
					break;
				}
			}
			if (libraryCity == null) {
				return learned;
			}

			// Contact must run both ways: the C3X mod, sharing the library
			// among hotseat humans, notes "we must mark that "this" has
			// contacted the AIs and vice-versa otherwise the techs won't be
			// granted" (patch_Leader_update_great_library_unlocks,
			// https://github.com/maxpetul/C3X/blob/master/injected_code.c).
			List<Player> contacts = gameData.players.Where(p => p != this
				&& !p.IsIndependent
				&& !p.defeated
				&& playerRelationships.ContainsKey(p.id)
				&& p.playerRelationships.ContainsKey(id)).ToList();
			if (contacts.Count < 2) {
				return learned;
			}

			foreach (Tech gained in gameData.techs.ToList()) {
				if (knownTechs.Contains(gained.id) || GetEraIndex(gained.EraCivilopediaName) < 0
						|| !gained.Prerequisites.All(t => knownTechs.Contains(t.id))
						|| contacts.Count(p => p.knownTechs.Contains(gained.id)) < 2) {
					continue;
				}
				log.Information("{Player} learns {Tech} from the Great Library", this, gained.Name);
				CompleteResearchingTech(gameData, gained);
				learned.Add(gained);
			}

			if (learned.Count > 0) {
				// As with research, a human with nothing queued chooses what
				// to research next rather than having it picked for them.
				if (ChoosesResearchThemselves) {
					lastDiscoveredTech = learned[^1];
				} else {
					PlayerAI.MaybePickTechToResearch(this, gameData.techs);
				}
				if (IsToldNews) {
					new MsgShowTemporaryPopup($"The {library.name} in {libraryCity.name} has given us {string.Join(", ", learned.Select(t => t.Name))}.",
						libraryCity.location, this).send();
				}
			}
			return learned;
		}

		// Grants techs for a wonder like Theory of Evolution, which gives its
		// builder free advances on completion. The free techs go through the
		// same path as Philosophy's bonus tech: the tech being researched is
		// completed at once, then the next one picked (from the research
		// queue, or automatically), and so on until the free techs are used.
		// If nothing is being researched, the free techs are kept until a
		// tech is chosen.
		//
		// Assumption: Civ3 completes the current research and then the next
		// one, and research progress isn't carried over.
		// Returns the techs learned right away.
		public List<Tech> GrantFreeTechs(GameData gameData, int count, string source, City city) {
			HashSet<ID> before = new(knownTechs);
			freeTechsRemaining += count;
			if (currentlyResearchedTech != null && !knownTechs.Contains(currentlyResearchedTech)) {
				SetCurrentlyResearchedTech(currentlyResearchedTech);
			}

			List<Tech> learned = gameData.techs.Where(t => knownTechs.Contains(t.id) && !before.Contains(t.id)).ToList();
			if (IsToldNews && learned.Count > 0 && city != null) {
				new MsgShowTemporaryPopup($"The {source} in {city.name} has given us {string.Join(", ", learned.Select(t => t.Name))}.",
					city.location, this).send();
			}
			return learned;
		}

		// Shows the player every tile of the map, as learning Satellites does
		// in Conquests ("Upon discovering this tech, the whole map is revealed
		// to your civ", https://sullla.com/Civ3/CQ/CQ_techs.html). The tiles
		// become known, as explored ones are, not watched: units on them stay
		// hidden in the fog.
		//
		// UNVERIFIED: whether Civ3 shows the cities and improvements on the
		// tiles revealed this way (we show them as they are now, as any newly
		// known tile does, and as SpaceRace.RevealMapToAll does).
		public void RevealWholeMap(GameData gameData) {
			if (gameData?.map?.tiles == null) {
				return;
			}
			foreach (Tile t in gameData.map.tiles) {
				tileKnowledge.AddTileToKnown(t);
			}
			tileKnowledge.RecomputeActiveTiles();
		}

		// Learns a tech acquired outside research, such as one stolen by
		// espionage, then carries on researching.
		public void AcquireTech(GameData gameData, Tech tech) {
			CompleteResearchAndBeginNew(gameData, tech);
		}

		private void CompleteResearchAndBeginNew(GameData gameData, IEnumerable<Tech> techs) {
			Tech last = null;
			foreach (Tech tech in techs) {
				CompleteResearchingTech(gameData, tech);
				last = tech;
			}
			if (ChoosesResearchThemselves) {
				lastDiscoveredTech = last ?? lastDiscoveredTech;
				return;
			}
			PlayerAI.MaybePickTechToResearch(this, gameData.techs);
		}

		// Whether this player, having nothing to research, is to be asked
		// what to research next (see AskWhatToResearch) rather than have it
		// picked for them: a human whose research queue has run out, or a
		// human with a free tech to come (such as Philosophy's), who chooses
		// that tech themselves rather than have it spent on the next tech in
		// their research queue (owner's decision, 2026-10-11: "Philosophy
		// should have a human choice"). Their choice replaces the queue, as
		// any choice of research does. AIs always pick for themselves.
		private bool ChoosesResearchThemselves => isHuman && currentlyResearchedTech == null
			&& (ResearchQueue.Count == 0 || freeTechsRemaining > 0);
		// Asks a human with nothing to research what to research next, once
		// they have a city. The message names the tech just discovered, which
		// a LAN client doesn't otherwise know.
		public void AskWhatToResearch(GameData gameData) {
			if (isHuman && cities.Count > 0 && currentlyResearchedTech == null
					&& GetAvailableTechsToResearch(gameData.techs).Count > 0) {
				new MsgShowScienceSelection(this, lastDiscoveredTech).send();
			}
		}

		private void CompleteResearchAndBeginNew(GameData gameData, Tech tech) {
			CompleteResearchingTech(gameData, tech);

			// A human whose research queue has run out is asked what to
			// research next (see AskWhatToResearch) rather than having it
			// picked for them.
			if (ChoosesResearchThemselves) {
				lastDiscoveredTech = tech;
				return;
			}
			PlayerAI.MaybePickTechToResearch(this, gameData.techs);
		}

		private void CompleteResearchingTech(GameData gameData, Tech tech) {
			// If this tech awards the first civ to research it a free tech and
			// no other civs know about the tech, this player gets the bonus.
			if (tech.BonusTechToFirstCivThatResearches) {
				bool awardBonus = true;
				foreach (Player p in gameData.players) {
					if (p.knownTechs.Contains(tech.id)) {
						awardBonus = false;
					}
				}
				if (awardBonus) {
					++freeTechsRemaining;
					++bonusFreeTechsRemaining;
				}
			}

			knownTechs.Add(tech.id);
			VictoryPoints.OnAdvance(this, tech, gameData.victoryConditions);
			if (tech.RevealsWorldMap) {
				RevealWholeMap(gameData);
			}
			// trigger callback for techs that enable improvements to redraw map
			TechImprovementCallback(this, tech);

			// Only reset research if this was the tech being researched. A tech
			// gained another way (e.g. a trade) shouldn't wipe current progress.
			if (currentlyResearchedTech == tech.id) {
				SetCurrentlyResearchedTech(null);
			}

			// Remove the gained tech from the research queue, wherever it is.
			// It isn't necessarily at the front if it came from a trade.
			ResearchQueue = new Queue<Tech>(ResearchQueue.Where(t => t.id != tech.id));

			if (CanAdvanceToNextEra(gameData)) {
				PalaceUpgrades.OnEraCompleted(this, gameData, EraIndex());
				string previousEra = eraCivilopediaName;
				eraCivilopediaName = GetNextEraNameByIndex(EraIndex());
				// The last era has no next one to enter.
				if (eraCivilopediaName != previousEra) {
					// Civ3 shows its era splash to the human whose era it is,
					// and not on the game's first turn (Civ3 FUN_0056a220,
					// before the scientific civ's free tech).
					if (isHuman && gameData.turn > 0) {
						new MsgEnteredNewEra(this, previousEra, eraCivilopediaName).send();
					}
					GrantScientificEraTech(gameData);
				}
			}
		}

		// A scientific civ "receives a free Civilization Advance at the start
		// of every era" (the Conquests Civilopedia, GCON_Strengths): "one of
		// the starting technologies for that age"
		// (https://civfanatics.com/civ3/faq/). Players found "the free tech
		// is always one of the first set of new age techs", picked at random
		// (https://forums.civfanatics.com/threads/scientific-civ-and-the-free-tech.75647/),
		// so we pick at random among the new era's techs that can be
		// researched now. Firaxis gave the rule: "It is supposed to select,
		// at random, one tech from the new era that is available to the
		// player" "(i.e., it has no prerequisites)." and "all 4 techs should
		// have an equal chance of being selected." (Mike Breitkreutz, quoted
		// in https://codehappy.net/apolyton/threads/65178-1.htm; fixed in
		// PTW: "Improved random free tech selection for the Scientific civ
		// trait.", civ3PTW/readme.txt). So the pick is uniform among the new
		// era's unknown techs with no prerequisites, and only if there are
		// none among the others that can be researched.
		//
		// UNVERIFIED: the same thread says a human may instead pick the tech
		// from the science advisor's "Big Picture" as they enter the era; we
		// always pick at random.
		private void GrantScientificEraTech(GameData gameData) {
			if (civilization?.traits.Contains(Civilization.Trait.Scientific) != true) {
				return;
			}
			List<Tech> candidates = gameData.techs
				.Where(t => t.EraCivilopediaName == eraCivilopediaName && t.Prerequisites.Count == 0 && !knownTechs.Contains(t.id))
				.ToList();
			if (candidates.Count == 0) {
				candidates = GetAvailableTechsToResearch(gameData.techs)
					.Where(t => t.EraCivilopediaName == eraCivilopediaName)
					.ToList();
			}
			if (candidates.Count == 0) {
				return;
			}
			Tech gained = candidates[GameData.rng.Next(candidates.Count)];
			log.Information("{Player} is scientific and learns {Tech} on entering {Era}", this, gained.Name, eraCivilopediaName);
			CompleteResearchingTech(gameData, gained);
			if (IsToldNews) {
				new MsgShowScienceAdvisorPopup(this,
					$"Our scholars greet the {EraUtils.GetNiceEraName(eraCivilopediaName)} with a new advance: {gained.Name}.",
					MsgShowScienceAdvisorPopup.Mood.Happy).send();
			}
		}

		private static void TechImprovementCallback(Player player, Tech tech) {
			var terraforms = EngineStorage.gameData.Terraforms;
			if (terraforms.Any(t => t.Improvement is { layer: TerrainImprovement.Layer.Roads } && t.RequiredTech == tech.id)) {
				foreach (var city in player.cities) {
					var cityLoc = city.location;
					TryAddRoad(cityLoc, cityLoc.HasRoad(), cityLoc.HasRailroad());
					TryAddRailroad(cityLoc, cityLoc.HasRailroad());
				}
			}
		}

		private bool CanAdvanceToNextEra(GameData gameData) {
			foreach (Tech t in gameData.techs) {
				if (t.EraCivilopediaName != eraCivilopediaName) {
					continue;
				}

				if (knownTechs.Contains(t.id)) {
					continue;
				}

				// This is a tech in our era that we don't know. If it is
				// required for era advancement we can't go to the next era yet.
				if (t.RequiredForEraAdvancement) {
					return false;
				}
			}

			return true;
		}

		public (int, int, int) TotalUnitsAllowedUnitsAndSupportCostRaw() {
			int freeUnits = 0;

			Difficulty difficulty = EngineStorage.gameData.AiBonusDifficulty(this);
			if (!isHuman) {
				freeUnits += difficulty.AdditionalFreeUnitSupport;
			}
			// A house rule may support more of the AI's units (see AiHouseRules).
			int extraPerCity = AiHouseRules.ExtraFreeUnitsPerCity(this);

			foreach (City city in cities) {
				if (!isHuman) {
					freeUnits += difficulty.UnitSupportBonusForEachSettlement + extraPerCity;
				}

				if (city.residents.Count <= rules.MaximumLevel1CitySize) {
					freeUnits += government.freeUnitsPerTown;
				} else if (city.residents.Count <= rules.MaximumLevel2CitySize) {
					freeUnits += government.freeUnitsPerCity;
				} else {
					freeUnits += government.freeUnitsPerMetropolis;
				}
			}

			if (government.allUnitsFree) {
				freeUnits = units.Count;
			} else {
				// Units of another nationality (captives) are always free, as
				// are types without the BIQ's "Requires Support": the C3X
				// mod's re-creation of the stock count makes a unit free "if
				// ((unit->Body.RaceID != this->RaceID) || (type->requires_support == 0))"
				// (patch_Leader_count_maintenance_free_units,
				// https://github.com/maxpetul/C3X/blob/master/injected_code.c).
				foreach (MapUnit u in units) {
					if (u.IsCaptive() || !u.unitType.requiresSupport) {
						++freeUnits;
					}
				}
			}

			// The units inside an army are in units too, so each needs
			// support like any other (owner's decision, 2026-10-21).
			int totalUnits = units.Count;
			int allowedUnits = freeUnits;
			// "Players with no cities don't pay unit maintenance, per the
			// original game rules" (same mod and file,
			// charge_maintenance_with_aggressive_penalties).
			int unitSupportCost = cities.Count == 0 ? 0 : Math.Max(0, (totalUnits - allowedUnits) * government.unitCost);
			return (totalUnits, allowedUnits, unitSupportCost);
		}

		[MoonSharpHidden]
		public (int, int, int) TotalUnitsAllowedUnitsAndSupportCost() {
			(int totalUnits, int allowedUnits, int unitSupportCost) result = TotalUnitsAllowedUnitsAndSupportCostRaw();

			foreach (City city in cities) {
				// unitSupport lua infow
				if (city.itemBeingProduced is Inflow inflowUnitSupport && inflowUnitSupport.TryGetInflowYieldFunc(InflowYield.unitsupport, out var unitSupportYieldFunc)) {
					int unitSupportLess = unitSupportYieldFunc.Invoke(new ScriptContext(this, city));
					result.unitSupportCost -= unitSupportLess;
				}
			}

			result.unitSupportCost = Math.Max(0, result.unitSupportCost);

			return (result.totalUnits, result.allowedUnits, result.unitSupportCost);
		}

		// See https://forums.civfanatics.com/threads/military-advisor-relative-strength-assessment-definition.62980/post-1211499 and
		// https://forums.civfanatics.com/threads/study-of-inner-workings-of-military-advisor.83599/
		public float CalculateMilitaryStrength() {
			float result = 4; // The +4 base is hypothesized to avoid div by 0 bugs.
			foreach (MapUnit unit in units) {
				UnitPrototype up = unit.unitType;
				result += unit.maxHitPoints * (up.attack * 3 + up.defense * 2) + up.bombard;
			}
			return result;
		}

		public enum MilitaryStrength {
			WeakTo,
			EquivalentTo,
			StrongTo,
		};

		// See https://forums.civfanatics.com/threads/study-of-inner-workings-of-military-advisor.83599/
		public MilitaryStrength CompareMilitaryStrengthTo(Player other) {
			float us = CalculateMilitaryStrength();
			float them = other.CalculateMilitaryStrength();

			if (us < 0.8 * them) {
				return MilitaryStrength.WeakTo;
			} else if (us > 1.2 * them) {
				return MilitaryStrength.StrongTo;
			} else {
				return MilitaryStrength.EquivalentTo;
			}
		}

		// After losing the capital, rebuilds the palace for free in the city
		// that scores most by the "Free Palace Jump" article (DaveMcW,
		// https://civfanatics.com/civ3/strategy/game-mechanics/free-palace-jump/):
		// 3 points per national citizen and 1 per foreign one, 1 / 2 / 3 per
		// town / city / metropolis of ours "within 8 tiles walking distance
		// from the target city (a 17x17 square)", and 1 per military unit
		// there; "If there is a tie for the most points, the palace jumps to
		// the first city in the database, which is _usually_ the oldest
		// city". UNVERIFIED: tested on vanilla/PTW, and for a razed or
		// abandoned capital; used here for a captured one too, and for C3C.
		// Returns the new capital, or null if there was nothing to do.
		public City RelocatePalace(GameData gameData, Tile oldCapitalLocation) {
			if (cities.Count == 0 || cities.Any(c => c.IsCapital())) {
				return null;
			}

			int SizePoints(City c) {
				int size = c.residents.Count;
				return size <= rules.MaximumLevel1CitySize ? 1 : size <= rules.MaximumLevel2CitySize ? 2 : 3;
			}
			int Score(City c) {
				int score = c.residents.Sum(r => r.nationality == null || r.nationality == civilization ? 3 : 1);
				score += cities.Where(o => o != c && o.location.DistanceTo(c.location) <= 8).Sum(SizePoints);
				score += c.location.unitsOnTile.Count(u => u.owner == this && (u.unitType.attack > 0 || u.unitType.defense > 0)
					&& !u.unitType.isWorker && !u.unitType.isSettler);
				return score;
			}
			City newCapital = cities[0];
			int best = Score(newCapital);
			foreach (City c in cities.Skip(1)) {
				int score = Score(c);
				if (score > best) {
					best = score;
					newCapital = c;
				}
			}
			newCapital.capital = true;

			Building palace = gameData.Buildings.Find(b => b.isCenterOfEmpire);
			if (palace != null && !newCapital.constructed_buildings.Any(cb => cb.building == palace)) {
				newCapital.AddBuilding(palace);
			}

			log.Information("{Player} moved its palace to {City}", this, newCapital);
			return newCapital;
		}

		// Whether the palace screen has anything to show: the civ has its
		// palace, or a city that may start building one.
		public bool CanStartPalace(GameData gameData) {
			if (cities.Any(c => c.IsCapital())) {
				return true;
			}
			HashSet<Resource> resources = null;
			foreach (City c in cities) {
				resources ??= c.GetAccessibleResources(gameData);
				if (gameData.Buildings.Any(b => b.isCenterOfEmpire && b.CanProduce(c, resources))) {
					return true;
				}
			}
			return false;
		}

		// Puts the palace in the city, which becomes the capital, removing it
		// from the old capital (as building a new palace does in Civ3).
		public void MovePalaceTo(City newCapital, Building palace) {
			foreach (City c in cities) {
				if (c == newCapital) {
					continue;
				}
				c.capital = false;
				CityBuilding old = c.constructed_buildings.Find(cb => cb.building.isCenterOfEmpire);
				if (old != null) {
					c.RemoveBuilding(old);
				}
			}
			newCapital.capital = true;
			if (!newCapital.constructed_buildings.Any(cb => cb.building == palace)) {
				newCapital.AddBuilding(palace);
			}
			// The trade network re-checks which city is the capital itself.
			if (EngineStorage.gameData != null) {
				DoCorruptionCalculations(EngineStorage.gameData);
			}
			log.Information("{Player} built a new palace in {City}", this, newCapital);
		}

		// War weariness, as players measured it in Civ3
		// (https://www.civfanatics.com/civ3/strategy/game-mechanics/how-does-war-weariness-work/,
		// "the article" below). "Each civ have one wwp number against each of
		// the other civs", kept in our relationship with them
		// (PlayerRelationship.warWearinessPoints), starting at 0:
		// - "Subtract 30 wwp if the AI attacks you, except when AI is
		//   provoked by: use of nuclear weapons, failed spy mission". We
		//   count war being declared on us, and per the project owner by
		//   any civ, AI or human: the victim of a declaration starts with
		//   war happiness against the aggressor, which the war's weariness
		//   then wears away and eventually turns into war weariness. Also per
		//   the project owner, a civ that refused a demand to take its units
		//   out of the aggressor's territory provoked the war, and gets none,
		//   but only when the war is the direct result of that refusal; one
		//   that kept coming back after promising to leave gets it as normal
		//   (TerritoryDemands).
		// - "Add 1 wwp if you have units in enemys territory when in war.
		//   (In beginning of the turn)"
		// - "Add 1 wwp for each lost unit without defence value,
		//   improvement pillage/bombed, unit that are bombard down to 1 hp"
		// - "Add 2 wwp when a human attacker is defeated"
		// - "Add 2 wwp when a unit with defence value is attacked. (Even if
		//   you win)"
		// - "Add 16 wwp when a size 1 city is captured 17 wwp for bigger
		//   cities."
		// - "Subtract 1 wwp if level >= 1, no enemy inside your territory
		//   and no units in enemys territory."
		// - "Subtract 1/20 of current wwp each turn in peace (round up)"
		// The article lists the points "for a human", and says Civ3 has a bug
		// that gives an AI the human's points in a human-AI battle (and both
		// AIs the first's in an AI-AI war). We give every civ its own points
		// as the rules above say, without the bug.
		public const int WarWearinessWhenWarIsDeclaredOnUs = -30;
		public const int WarWearinessPerTurnInEnemyTerritory = 1;
		public const int WarWearinessForLostUnitWithoutDefence = 1;
		public const int WarWearinessForPillagedOrBombedImprovement = 1;
		public const int WarWearinessForUnitBombardedToOneHitPoint = 1;
		public const int WarWearinessForDefeatedAttacker = 2;
		public const int WarWearinessForUnitAttacked = 2;
		public const int WarWearinessForLostSizeOneCity = 16;
		public const int WarWearinessForLostCity = 17;
		public const int WarWearinessRecoveryPerQuietTurn = 1;
		private const int WarWearinessDecayDivisorAtPeace = 20;

		// The article's war weariness levels: below 0 points is level -1 (war
		// happiness), up to 30 level 0 (no effect), then a level for every 30
		// more points, up to level 4 from 121.
		private const int WarWearinessPointsPerLevel = 30;
		private const int MaxWarWearinessLevel = 4;

		public static int WarWearinessLevel(int points) {
			if (points < 0) {
				return -1;
			}
			if (points == 0) {
				return 0;
			}
			return Math.Min(MaxWarWearinessLevel, (points - 1) / WarWearinessPointsPerLevel);
		}

		// The share of a city's citizens (in percent) made unhappy at each
		// level (0 to 4), by government war weariness: low as in the
		// article's Republic, high as in its Democracy. A Democracy at level
		// 3 or more revolts (see RevoltFromWarWeariness).
		private static readonly int[] LowWarWearinessUnhappyPercent = { 0, 25, 50, 50, 100 };
		private static readonly int[] HighWarWearinessUnhappyPercent = { 0, 50, 100, 100, 100 };

		// "All government: Level -1: 25% happy people"
		private const int WarHappinessPercent = 25;

		// "subtract 25% for police station and 1 for US (Universal
		// Sufferage)": police stations take a quarter of the citizens off
		// the unhappy ones (see WarWearinessUnhappiness), and Universal
		// Suffrage (the BIQ's "reduces war weariness in all cities") one
		// more.
		private const int PoliceStationReductionPercent = 25;
		private const int EverywhereReduction = 1;

		// Our war weariness points against the other civ.
		public int WarWearinessPointsAgainst(Player other) {
			return other != null && other.id != null && playerRelationships.TryGetValue(other.id, out PlayerRelationship pr)
				? pr.warWearinessPoints : 0;
		}

		// Adds war weariness points (or takes them off, if negative) against
		// the other civ. Barbarians, and civs we have no relationship with,
		// don't count.
		public void AddWarWeariness(Player other, int points) {
			if (other == null || other == this || other.IsIndependent || IsIndependent || other.id == null) {
				return;
			}
			if (playerRelationships.TryGetValue(other.id, out PlayerRelationship pr)) {
				pr.warWearinessPoints += points;
			}
		}

		// A unit of the defender's was attacked by one of the attacker's. No
		// one knows whose a hidden nationality unit is, so a fight with one
		// counts for nothing.
		public static void AddWarWearinessForAttack(MapUnit attacker, MapUnit defender, bool attackerDefeated) {
			if (attacker?.owner == null || defender?.owner == null
				|| attacker.unitType.hasHiddenNationality || defender.unitType.hasHiddenNationality) {
				return;
			}
			if (defender.unitType.defense > 0) {
				defender.owner.AddWarWeariness(attacker.owner, WarWearinessForUnitAttacked);
			}
			if (attackerDefeated) {
				attacker.owner.AddWarWeariness(defender.owner, WarWearinessForDefeatedAttacker);
			}
		}

		// The city's owner lost it to the captor. The size is from before
		// the capture.
		public static void AddWarWearinessForLostCity(Player owner, Player captor, int size) {
			owner?.AddWarWeariness(captor, size <= 1 ? WarWearinessForLostSizeOneCity : WarWearinessForLostCity);
		}

		// Called once per turn.
		public void UpdateWarWeariness(GameData gameData) {
			if (warWeariness > 0) {
				warWeariness -= DecayAtPeace(warWeariness);
			}
			if (IsIndependent) {
				return;
			}
			// Whose territory our units are in, found the first time we're at
			// war with someone, in one pass over our units rather than one
			// per enemy.
			HashSet<Player> territoriesWeAreIn = null;
			foreach (Player other in gameData.players) {
				if (other == this || other.IsIndependent || other.id == null
					|| !playerRelationships.TryGetValue(other.id, out PlayerRelationship pr)) {
					continue;
				}
				if (other.defeated || !pr.AtWar()) {
					pr.warWearinessPoints -= DecayAtPeace(pr.warWearinessPoints);
					continue;
				}
				territoriesWeAreIn ??= TerritoriesWithUnits();
				if (territoriesWeAreIn.Contains(other)) {
					pr.warWearinessPoints += WarWearinessPerTurnInEnemyTerritory;
				} else if (WarWearinessLevel(pr.warWearinessPoints) >= 1 && !other.HasUnitsInTerritoryOf(this)) {
					pr.warWearinessPoints -= WarWearinessRecoveryPerQuietTurn;
				}
			}
			RevoltFromWarWeariness(gameData);
		}

		// A government with high war weariness falls into anarchy when its
		// war weariness, summed over every civ and divided by the number of
		// civs it has met, is over 90. Flintlock, reading the 1.22 code
		// (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16199613):
		// "a player gets kicked into anarchy if their WW is over 90", and "it
		// checks against total WW divided by the number of contacts the
		// player has." 91 points is where the community's level 3 begins:
		// Democracy "Level 1: 50% unhappy people, Level 2: 100% unhappy
		// people, Level 3: Revolt"
		// (https://www.civfanatics.com/civ3/strategy/game-mechanics/how-does-war-weariness-work/).
		// Democracy is the only government with high war weariness in the
		// standard rules.
		private const int WarWearinessRevoltThreshold = 90;

		// A government with high war weariness that passes the threshold is
		// overthrown, as a revolution started by the player would be: "Our
		// people are overthrowing our $GOVERNMENT0. Our Civilization is
		// descending into Anarchy!" (Civ3's Script.txt, REVOLUTION).
		internal bool RevoltFromWarWeariness(GameData gameData) {
			if (IsIndependent || government == null || government.transitionType || government.warWeariness < 2) {
				return false;
			}
			// Points kept from saves made before they were kept per civ count
			// too.
			int total = warWeariness;
			int contacts = 0;
			foreach (Player other in gameData.players) {
				if (other == this || other.IsIndependent || other.defeated || other.id == null
					|| !playerRelationships.TryGetValue(other.id, out PlayerRelationship pr)) {
					continue;
				}
				total += pr.warWearinessPoints;
				++contacts;
			}
			bool revolt = total / Math.Max(1, contacts) > WarWearinessRevoltThreshold;
			// Under a locked government (MDJ's Feudalism) no one leaves it,
			// even for Anarchy.
			if (rules?.GovernmentLocked == true) {
				return false;
			}
			Government anarchy = gameData.governments.Find(g => g.transitionType);
			if (!revolt || anarchy == null) {
				return false;
			}
			string overthrown = government.name;
			log.Information("{Player}'s {Government} is overthrown by war weariness", this, overthrown);
			government = anarchy;
			ApplyGovernmentRateCap();
			inAnarchyUntilTurn = gameData.turn + GetTurnsOfAnarchyForTransition(gameData);
			if (IsToldNews) {
				new MsgShowDomesticAdvisorPopup(this,
					$"Our people are overthrowing our {overthrown}. Our Civilization is descending into Anarchy!").send();
			}
			return true;
		}

		// The players in whose territory we have units.
		private HashSet<Player> TerritoriesWithUnits() {
			HashSet<Player> result = new();
			foreach (MapUnit unit in units) {
				Tile location = unit.location;
				if (location != null && location != Tile.NONE) {
					Player owner = location.OwningPlayer();
					if (owner != null) {
						result.Add(owner);
					}
				}
			}
			return result;
		}

		// A twentieth of the points, rounded up, toward 0. The article only
		// gives this for positive points; we fade war happiness (negative
		// points) the same way.
		private static int DecayAtPeace(int points) {
			int decay = (Math.Abs(points) + WarWearinessDecayDivisorAtPeace - 1) / WarWearinessDecayDivisorAtPeace;
			return Math.Sign(points) * decay;
		}

		private bool HasUnitsInTerritoryOf(Player other) {
			foreach (MapUnit unit in units) {
				Tile location = unit.location;
				if (location != null && location != Tile.NONE && location.OwningPlayer() == other) {
					return true;
				}
			}
			return false;
		}

		private int UnhappyPercent(int level) {
			if (level <= 0) {
				return 0;
			}
			return government.warWeariness switch {
				1 => LowWarWearinessUnhappyPercent[level],
				>= 2 => HighWarWearinessUnhappyPercent[level],
				_ => 0,
			};
		}

		// Every source of war weariness points that is felt now: those
		// against each civ we are at war with, and those from older saves.
		// The points against a civ we are at peace with fade (see
		// UpdateWarWeariness) but do nothing until the next war with it:
		// "During peace WW is suspended. Only in war you can have war
		// weariness" (justanick,
		// https://forums.civfanatics.com/threads/mechanics-of-happy-content-faces.701169/ #6),
		// and they carry into that war: "Yes, you will (assuming you still
		// have enough WW left after the peace)" (TimBentley,
		// https://forums.civfanatics.com/threads/how-does-war-weariness-work.61628/ #66).
		// War happiness, also from being at war, is suspended the same way.
		// The points from older saves aren't kept against any civ, so they
		// count until they fade.
		private IEnumerable<int> WarWearinessPointSources() {
			yield return warWeariness;
			foreach (PlayerRelationship pr in playerRelationships.Values) {
				if (pr.AtWar()) {
					yield return pr.warWearinessPoints;
				}
			}
		}

		// The number of citizens in the city made unhappy by war weariness:
		// for each enemy we are at war with, the share its level and our
		// government give, rounded down, added together; then police
		// stations and Universal Suffrage reduce it, and it can't be more
		// than the city's citizens.
		public int WarWearinessUnhappiness(City city) {
			int citizens = city.residents.Count;
			int unhappy = 0;
			foreach (int points in WarWearinessPointSources()) {
				unhappy += citizens * UnhappyPercent(WarWearinessLevel(points)) / 100;
			}
			if (unhappy == 0) {
				return 0;
			}

			// The police station takes off a quarter of the city's
			// citizens, not a quarter of the unhappy ones: "If you have 125%
			// or more ww, the police station would not have any effect since
			// you are above the limit anyway" (Oystein,
			// https://forums.civfanatics.com/threads/how-does-war-weariness-work.61628/ #24).
			// UNVERIFIED: how the quarter is rounded; we round it down.
			foreach (CityBuilding cb in city.EffectiveBuildings()) {
				if (cb.building.reducesWarWeariness) {
					unhappy -= citizens * PoliceStationReductionPercent / 100;
					break;
				}
			}
			if (GetBuildingSnapshot().reducesWarWearinessEverywhere) {
				unhappy -= EverywhereReduction;
			}
			return Math.Clamp(unhappy, 0, citizens);
		}

		// The number of content citizens in the city made happy by war
		// happiness: a quarter of them for each civ we're at level -1
		// against, whatever our government. "War happiness is calculated
		// independent in the same way. (No effect of improvments)"
		public int WarHappiness(City city) {
			int citizens = city.residents.Count;
			int happy = 0;
			foreach (int points in WarWearinessPointSources()) {
				if (WarWearinessLevel(points) < 0) {
					happy += citizens * WarHappinessPercent / 100;
				}
			}
			return Math.Min(happy, citizens);
		}

		public void StartGoldenAge(GameData gameData, string reason) {
			if (hadGoldenAge || IsIndependent) {
				return;
			}
			hadGoldenAge = true;
			goldenAgeTurnsRemaining = gameData.rules.GoldenAgeDuration;
			log.Information("{Player} starts a golden age: {Reason}", this, reason);
			// At the capital, if there is one (Civ3 FUN_00568940).
			City capital = cities.FirstOrDefault(c => c.capital);
			if (capital != null) {
				gameData.gameHistory?.Record(gameData.turn, ReplayEventType.GoldenAge, this, capital.location);
			}
			if (IsToldNews) {
				new MsgShowMilitaryAdvisorPopup(this, $"{reason}\nOur civilization enters a Golden Age!", happy: true).send();
			}
		}

		// Called at the end of each turn.
		public void AdvanceGoldenAge() {
			if (goldenAgeTurnsRemaining > 0) {
				--goldenAgeTurnsRemaining;
			}
			if (scienceAgeTurnsRemaining > 0) {
				--scienceAgeTurnsRemaining;
				if (scienceAgeTurnsRemaining == 0 && IsToldNews) {
					// Civ3's SUMMARY_END_SCIENCE_AGE (script.txt).
					new MsgShowScienceAdvisorPopup(this,
						"Our Civilization's Age of Scientific Discovery has ended. So say our analysts...",
						MsgShowScienceAdvisorPopup.Mood.Sad).send();
				}
			}
		}

		// An Age of Science is "a 20-turn period in which the scientific
		// research of each city in that civilization's empire is 25% more
		// productive. An Age of Science can only be triggered by a scientific
		// leader" (Conquests Civilopedia, GCON_Age_of_Science). The shipped
		// 1.22 game never adds the bonus to research: Antal1987 found NOPs in
		// the executable where "_Total_Bulbs = (_Total_Bulbs * 1.25);"
		// belongs (https://forums.civfanatics.com/threads/patch-framework-for-c3c-v1-22.526966/),
		// and the C3X mod's notes say the bonus points "are not actually
		// counted towards research progress. They appear only on the
		// interface" (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
		// We give the intended bonus rather than the bug: a quarter more
		// beakers into research each turn (see DoPerTurnFinanceUpdates).
		// UNVERIFIED (no source found): whether a second leader can start
		// another age during one; here it can't.
		public const int ScienceAgeTurns = 20;
		public const int ScienceAgeResearchBonusPercent = 25;

		public bool CanStartScienceAge() {
			return !IsIndependent && !InScienceAge;
		}

		public void StartScienceAge() {
			if (!CanStartScienceAge()) {
				return;
			}
			scienceAgeTurnsRemaining = ScienceAgeTurns;
			log.Information("{Player} starts an Age of Science", this);
			if (IsToldNews) {
				// Civ3's SUMMARY_SCIENCE_AGE (script.txt).
				new MsgShowScienceAdvisorPopup(this,
					"Our Great Civilization has entered an Age of Scientific Discovery!",
					MsgShowScienceAdvisorPopup.Mood.Happy).send();
			}
		}

		// This turn's research with the Age of Science bonus, if any. The
		// rounding is UNVERIFIED; here fractions are dropped.
		internal int WithScienceAgeBonus(int beakers) {
			return InScienceAge ? beakers + beakers * ScienceAgeResearchBonusPercent / 100 : beakers;
		}

		// A golden age starts once the great wonders this civ has built cover
		// all of its strengths: "If a civilization builds a Great Wonder (or
		// Wonders) that corresponds to [both] its unique strengths, its Golden
		// Age can be triggered" (the Conquests Civilopedia, GCON_Golden_Age).
		// Each wonder may supply a different strength, and captured wonders
		// count as well, but the check is only made when the civ completes a
		// great wonder itself (City.HandleCityProduction calls this then):
		// "the game checks the qualifications for a golden age everytime you
		// build a wonder (any wonder)" (Bamspeedy,
		// https://forums.civfanatics.com/threads/compiled-list-of-bugs-and-non-bugs-not-a-dicussion-thread.70063/),
		// "Capture or build a needed wonder, capture another needed wonder,
		// self-build any wonder = Golden Age" (Puppeteer,
		// https://forums.civfanatics.com/threads/golden-age-triggers-by-great-wonders.411703/).
		// Owner's decision, 2026-10-21: a golden age comes only from a
		// unique unit's victory (see MapUnit.Fight) or from great wonders
		// covering both traits; small wonders never count, and captured
		// wonders count toward the traits but never start one by themselves,
		// as capturing a city doesn't run this check.
		public void MaybeStartGoldenAgeFromWonders(GameData gameData) {
			if (hadGoldenAge || civilization.traits.Count == 0) {
				return;
			}
			HashSet<Civilization.Trait> wonderTraits = new();
			foreach (City c in cities) {
				foreach (CityBuilding cb in c.constructed_buildings) {
					if (cb.building.IsGreatWonder()) {
						wonderTraits.UnionWith(cb.building.traits);
					}
				}
			}
			if (civilization.traits.IsSubsetOf(wonderTraits)) {
				StartGoldenAge(gameData, "Our wonders have inspired the people.");
			}
		}

		public void DoCorruptionCalculations(GameData gameData) {
			if (cities.Count == 0) {
				return;
			}

			// Precalculate the cities of interest for distance corruption.
			citiesWithCorruptionWonders.Clear();
			bool foundCapital = false;
			foreach (City c in cities) {
				if (c.IsCapital()) {
					citiesWithCorruptionWonders.Add(c);
					foundCapital = true;
				}
				// We use constructed_buildings here because great wonders can't
				// supply small wonders like forbidden palaces, so we can avoid
				// doing extra work for each city.
				if (c.constructed_buildings.Any(x => x.building.WorksAsForbiddenPalaceFor(this) && x.building.TakesEffectFor(this))) {
					citiesWithCorruptionWonders.Add(c);
				}
			}
			if (!foundCapital) {
				// TODO: Ensure we always have a capital, even in scenarios
				// without palaces added.
				citiesWithCorruptionWonders.Add(cities[0]);
			}

			// Order the cities by distance to the capital. Cities at the same
			// distance go in the order they were founded: "founding date is a
			// tie-breaker for cities at the same distance" (alexman,
			// https://forums.civfanatics.com/threads/everything-about-corruption-c3c-edition.76619/,
			// #79), then in database order ("in order of founding" and then
			// database order, the C3C corruption article). The game's city
			// list is that order: a new city is added at its end and a
			// captured one keeps its place, and a Civ3 save's cities import
			// in its database order. The player's own list is not, since a
			// captured city joins its end until the game is reloaded.
			City capital = cities.Find(x => x.IsCapital());
			if (capital == null) {
				capital = cities[0];
			}
			Dictionary<City, int> foundingOrder = new(cities.Count);
			foreach (City c in cities) {
				foundingOrder[c] = int.MaxValue;
			}
			List<City> allCities = gameData.cities;
			for (int i = 0; i < allCities.Count; ++i) {
				if (allCities[i].owner == this) {
					foundingOrder[allCities[i]] = i;
				}
			}
			List<City> citiesInRankOrdering = cities
				.OrderBy(x => x.location.RankDistanceTo(capital.location))
				.ThenBy(x => foundingOrder[x])
				.ToList();

			// This is the same for every city (it depends on the whole empire,
			// not on anything the loop below changes), so work it out once.
			int adjustedOptimalCityNumber = GetAdjustedOptimalCityNumber(gameData);
			for (int i = 0; i < citiesInRankOrdering.Count; ++i) {
				citiesInRankOrdering[i].rankIndex = i;

				// For each city, calculate its corruption level so this
				// calculation doesn't have to be done on the fly.
				citiesInRankOrdering[i].CalculateCorruption(gameData, adjustedOptimalCityNumber);
			}
		}

		// Call once at turn advance, decrement penalty turns of unhappiness for drafting / whipping
		public void DecrementCityUnhappinessPenalties(GameData gameData) {
			foreach (City c in cities) {
				c.turnsOfUnhappinessDueToPopRushing -= (c.turnsOfUnhappinessDueToPopRushing > 0) ? 1 : 0;
				c.turnsOfUnhappinessDueToDrafting -= (c.turnsOfUnhappinessDueToDrafting > 0) ? 1 : 0;
			}
		}

		public void RecalculateCitizenMoods(GameData gameData, bool goIntoDisorderIfUnhappy = false) {
			foreach (City c in cities) {
				// A governor managing moods gets its turn first, so its city
				// doesn't riot (after war weariness grows, say).
				if (goIntoDisorderIfUnhappy) {
					C7Engine.AI.CityTileAssignmentAI.GovernMoods(gameData, c);
				}
				City.Mood cityMood = c.RecalculateCitizenMoods(gameData);

				// Disorder and celebrations start and end only at the turn's
				// check, so a recalculation elsewhere (e.g. loading a save)
				// leaves them alone.
				if (goIntoDisorderIfUnhappy) {
					bool wasInDisorder = c.isInCivilDisorder;
					c.isInCivilDisorder = cityMood == City.Mood.Unhappy;
					c.celebrating = !c.isInCivilDisorder && c.QualifiesForCelebration(rules);
					// A city kept in disorder through a whole turn may have
					// its nuclear plant melt down (see Meltdown).
					if (wasInDisorder && c.isInCivilDisorder) {
						C7Engine.Meltdown.MaybeMeltDown(gameData, c);
						c.MaybeRiot(gameData);
					}
				}
			}
		}

		// Returns a list of specialists that this player can use.
		public List<CitizenType> GetKnownSpecialists(GameData gameData) {
			return gameData.citizenTypes.FindAll(x => {
				return !x.IsDefaultCitizen && this.HasTech(x.PrerequisiteTech);
			});
		}

		// Returns the list of all wonders owned by this player, excluding those
		// that have become obsolete.
		public List<Tuple<City, CityBuilding>> GetActiveWonders() {
			return new List<Tuple<City, CityBuilding>>(GetBuildingSnapshot().activeWonders);
		}

		// Bumped whenever the buildings of this player's cities change: by
		// City.AddBuilding and City.RemoveBuilding, when a city's building list
		// is replaced, and when a city changes hands (for both players).
		private long buildingsVersion = 0;
		internal long BuildingsVersion => Interlocked.Read(ref buildingsVersion);

		internal void OnBuildingsChanged() {
			Interlocked.Increment(ref buildingsVersion);
		}

		// Empire-wide facts derived from the buildings in this player's cities
		// (the active great wonders, and whether a building like the Pentagon
		// makes armies larger). Every city yield needs the wonders, so they are
		// cached here instead of rescanning every building of every city.
		//
		// The snapshot is immutable and is replaced, never modified, so readers
		// on other threads always see a consistent one. It is checked against
		// everything it was computed from each time it is used, in O(1):
		//  - this player's BuildingsVersion, which catches buildings being
		//    added, removed or swapped in any of our cities, and cities
		//    changing hands;
		//  - the cities list (same list object, same count, and not modified
		//    since), which catches cities being founded, captured, lost,
		//    destroyed or reordered, and a new list on load, even where the
		//    code doing it doesn't tell us;
		//  - knownTechs (same set, same count, and not modified since), which
		//    catches wonders going obsolete.
		internal sealed class BuildingSnapshot {
			internal long buildingsVersion;
			internal List<City> citiesList;
			internal int citiesCount;
			internal List<City>.Enumerator citiesVersion;
			internal HashSet<ID> knownTechs;
			internal int knownTechsCount;
			internal HashSet<ID>.Enumerator knownTechsVersion;

			// Do not modify; GetActiveWonders hands out copies.
			internal List<Tuple<City, CityBuilding>> activeWonders;
			internal bool hasLargerArmies;
			internal bool hasIncreasedArmyValue;
			internal bool reducesWarWearinessEverywhere;
			internal bool paysTradeMaintenance;
			internal int shipMovementBonus;
			internal bool safeSeaTravel;
			internal bool doubleCombatVsBarbarians;
			internal bool increasesLeaderChance;
			// Longevity: cities grow by two citizens at a time.
			internal bool doublesCityGrowthEverywhere;

			internal bool IsValidFor(Player player) {
				if (buildingsVersion != player.BuildingsVersion
					|| !ReferenceEquals(citiesList, player.cities)
					|| citiesCount != player.cities.Count
					|| !ReferenceEquals(knownTechs, player.knownTechs)
					|| (knownTechs != null && knownTechsCount != knownTechs.Count)) {
					return false;
				}
				return CollectionVersion.Unchanged(citiesVersion)
					&& (knownTechs == null || CollectionVersion.Unchanged(knownTechsVersion));
			}
		}

		private BuildingSnapshot buildingSnapshot;

		internal BuildingSnapshot GetBuildingSnapshot() {
			BuildingSnapshot snapshot = buildingSnapshot;
			if (snapshot != null && snapshot.IsValidFor(this)) {
				return snapshot;
			}

			// Take the versions before scanning, so a change made during the
			// scan leaves the snapshot stale rather than wrongly valid.
			snapshot = new BuildingSnapshot {
				buildingsVersion = BuildingsVersion,
				citiesList = cities,
				citiesCount = cities.Count,
				citiesVersion = cities.GetEnumerator(),
				knownTechs = knownTechs,
				knownTechsCount = knownTechs?.Count ?? 0,
				activeWonders = new(),
			};
			if (knownTechs != null) {
				snapshot.knownTechsVersion = knownTechs.GetEnumerator();
			}
			foreach (City c in cities.ToArray()) {
				foreach (CityBuilding cb in c.constructed_buildings) {
					bool isGreatWonder = cb.building.greatWonderProperties != null;
					if (cb.building.IsObsoleteFor(this)) {
						continue;
					}
					if (cb.building.allowsLargerArmies) {
						snapshot.hasLargerArmies = true;
					}
					if (cb.building.increasedArmyValue) {
						snapshot.hasIncreasedArmyValue = true;
					}
					// Combat and movement effects (the Great Wall, the Great
					// Lighthouse, Magellan's Voyage, the Heroic Epic) come from
					// any of our buildings but an obsolete one.
					AddCombatAndMovementEffects(snapshot, cb.building);
					if (!isGreatWonder) {
						continue;
					}
					snapshot.activeWonders.Add(new Tuple<City, CityBuilding>(c, cb));
					if (cb.building.reducesWarWearinessEverywhere) {
						snapshot.reducesWarWearinessEverywhere = true;
					}
					if (cb.building.paysTradeMaintenance) {
						snapshot.paysTradeMaintenance = true;
					}
					if (cb.building.doublesCityGrowthEverywhere) {
						snapshot.doublesCityGrowthEverywhere = true;
					}
				}
			}
			buildingSnapshot = snapshot;
			return snapshot;
		}

		private static void AddCombatAndMovementEffects(BuildingSnapshot snapshot, Building building) {
			// Each wonder that increases ship movement adds its own +1, so
			// the Great Lighthouse and Magellan's Voyage stack (an assumption:
			// in Conquests the Great Lighthouse is usually obsolete by the
			// time Magellan's is built). The PlusTwoShipMovement flag, which
			// no Conquests building uses, adds +2.
			if (building.increasedShipMovement) {
				snapshot.shipMovementBonus += 1;
			}
			if (building.plusTwoShipMovement) {
				snapshot.shipMovementBonus += 2;
			}
			if (building.safeSeaTravel) {
				snapshot.safeSeaTravel = true;
			}
			if (building.doubleCombatVsBarbarians) {
				snapshot.doubleCombatVsBarbarians = true;
			}
			if (building.increasesLeaderChance) {
				snapshot.increasesLeaderChance = true;
			}
		}

		// The extra movement points this player's ships get from wonders
		// like the Great Lighthouse and Magellan's Voyage, and one more if
		// the civ is seafaring.
		public int ShipMovementBonus() {
			int bonus = GetBuildingSnapshot().shipMovementBonus;
			if (civilization?.traits.Contains(Civilization.Trait.Seafaring) == true) {
				bonus += 1;
			}
			return bonus;
		}

		// Whether this player's coastal ships (like the Galley) may enter
		// Sea tiles, thanks to the Great Lighthouse.
		public bool HasSafeSeaTravel() {
			return GetBuildingSnapshot().safeSeaTravel;
		}

		// Whether this player's ships are safe on water of the given terrain.
		// Coast is always safe. Sea is safe once the player can build a ship
		// that doesn't "sink in sea" (in Conquests, the Caravel, with
		// Astronomy), or has the Great Lighthouse, and Ocean once they can
		// build one that doesn't "sink in ocean" (the Galleon, with
		// Magnetism, or for the Portuguese their Carrack, with Astronomy).
		// Only the civ's own ships count, so others' unique units don't, and
		// resources aren't needed. See MapUnit.IsUnsafeWater.
		public bool CanSailSafelyOn(TerrainType terrain) {
			if (terrain.IsOcean) {
				UpdateSafeWaters();
				return safeOnOcean;
			}
			if (terrain.IsSea) {
				UpdateSafeWaters();
				return safeOnSea || HasSafeSeaTravel();
			}
			return true;
		}

		// What the player's naval research makes safe, worked out again only
		// when they learn a tech, as pathfinding asks for every water tile.
		private int safeWatersTechCount = -1;
		private bool safeOnSea;
		private bool safeOnOcean;

		private void UpdateSafeWaters() {
			if (knownTechs.Count == safeWatersTechCount) {
				return;
			}
			safeWatersTechCount = knownTechs.Count;
			safeOnSea = false;
			safeOnOcean = false;
			foreach (UnitPrototype p in EngineStorage.gameData?.unitPrototypes ?? []) {
				if (!p.IsSeaUnit() || (p.requiredTech != null && !knownTechs.Contains(p.requiredTech.id))) {
					continue;
				}
				if (civilization != null && !p.producibleBy.Contains(civilization)) {
					continue;
				}
				safeOnSea |= !p.sinksInSea;
				safeOnOcean |= !p.sinksInOcean;
			}
		}

		// Whether this player's units fight at double strength against
		// barbarians, thanks to the Great Wall.
		public bool HasDoubleCombatVsBarbarians() {
			return GetBuildingSnapshot().doubleCombatVsBarbarians;
		}

		// Whether this player's elite units are more likely to produce a
		// great leader, thanks to the Heroic Epic.
		public bool HasIncreasedLeaderChance() {
			return GetBuildingSnapshot().increasesLeaderChance;
		}

		public void MaybeSpawnBonusUnits(GameData gD) {
			// A non-barbarian AI gets the difficulty's extra units "at start"
			// (compendium 08 section 1.1), once, at the start of the first
			// turn it has a city: an AI whose settler walked a few turns
			// before founding its capital used to miss them, as they came
			// only on turn 1.
			if (receivedStartUnits || isHuman || IsIndependent || cities.Count == 0) {
				return;
			}
			receivedStartUnits = true;
			City city = cities.Find(c => c.capital) ?? cities[0];
			Difficulty bonuses = gD.AiBonusDifficulty(this);

			for (int i = 0; i < bonuses.ExtraStartUnit1; ++i) {
				city.AddUnit(gD.unitPrototypes.Find(x => x.name == gD.rules.StartUnitType1), gD);
			}
			for (int i = 0; i < bonuses.ExtraStartUnit2; ++i) {
				city.AddUnit(gD.unitPrototypes.Find(x => x.name == gD.rules.StartUnitType2), gD);
			}
			// The best defender and attacker the city can build, if it can
			// build any units at all.
			List<UnitPrototype> buildableUnits = city.ListProductionOptions(gD).OfType<UnitPrototype>().ToList();
			UnitPrototype bestDefender = buildableUnits.MaxBy(u => u.defense);
			UnitPrototype bestAttacker = buildableUnits.MaxBy(u => u.attack);
			for (int i = 0; i < bonuses.NumberOfAIDefensiveStartingUnits && bestDefender != null; ++i) {
				city.AddUnit(bestDefender, gD);
			}
			for (int i = 0; i < bonuses.NumberOfAIOffensiveStartingUnits && bestAttacker != null; ++i) {
				city.AddUnit(bestAttacker, gD);
			}
		}

		public void UpdateResourcesInBorders(IEnumerable<Tile> ownedTiles) {
			resourcesInBorders = ownedTiles
								.Where(t => t.Resource != Resource.NONE)
								.GroupBy(t => t.Resource)
								.ToDictionary(g => g.Key, g => g.ToList());
		}

		public bool HasRequiredTechnology(IProducible producible) {
			return producible.requiredTech == null ||
				   knownTechs.Contains(producible.requiredTech.id);
		}

		public bool CanBridgeRoads() {
			ID engineeringTechId = Tech.FindBridgeTech(EngineStorage.gameData.techs)?.id;
			// With no such tech, nothing is ever bridged.
			return engineeringTechId is not null && knownTechs != null && knownTechs.Contains(engineeringTechId);
		}

		public int ShieldCost(IProducible producible) {
			if (producible == null) {
				return int.MaxValue;
			}
			// At higher difficulties, AI players get a cost discount.
			Difficulty difficulty = EngineStorage.gameData.AiBonusDifficulty(this);
			float costFactor = isHuman ? 1.0f : difficulty.AiCostFactor / (float)(difficulty.HumanCostFactor);
			// A house rule may make it cheaper still (see AiHouseRules).
			costFactor *= AiHouseRules.CostFactor(this);
			// Accelerated Production halves the shield box (see
			// City.AcceleratedProduction), as it does tech costs
			// (GameData.TechCostFor).
			if (City.AcceleratedProduction(this)) {
				costFactor *= 0.5f;
			}

			if (producible is Building { isCenterOfEmpire: true }) {
				return (int)(PalaceShieldCost() * costFactor);
			}
			return producible.ShieldCost(civilization.traits, costFactor);
		}

		// Since 1.16f "The cost of building a Palace is now based on the
		// number of cities in your civilization" (Civ3 readme): "PC = (C *
		// 600) / OCN", rounded down to a hundred, and "The palace can never
		// cost less than 300 shields and never more than 1000 shields"
		// (https://civfanatics.com/civ3/strategy/game-mechanics/cost-of-the-palace-for-c3c/).
		// UNVERIFIED: whether OCN there is the map's or the civ's after the
		// difficulty's share; the map's is used.
		private int PalaceShieldCost() {
			int optimal = Math.Max(1, EngineStorage.gameData.map.optimalNumberOfCities);
			int cost = cities.Count * 600 / optimal / 100 * 100;
			return Math.Clamp(cost, 300, 1000);
		}

		// The key of this player in GameData.history, which is id.ToString().
		// Cached (with the id it was made from, in case the id is replaced),
		// since building the string allocates.
		private Tuple<ID, string> historyKey;
		public string HistoryKey {
			get {
				ID currentId = id;
				Tuple<ID, string> key = historyKey;
				if (key == null || !ReferenceEquals(key.Item1, currentId)) {
					key = new Tuple<ID, string>(currentId, currentId.ToString());
					historyKey = key;
				}
				return key.Item2;
			}
		}

		public void UpdateHistory(GameData gameData) {
			if (!gameData.history.TryGetValue(HistoryKey, out List<HistTurnRecord> history))
				return;

			if (gameData.gameOver) // Game is already over
				return;

			int n = history.Count;
			HistTurnRecord lastTurn = history.LastOrDefault();

			// TODO: Make formulas moddable

			// The power the Histograph shows. "Power is an amalgam of cities,
			// gold, culture, advances, resources, military strength, nuclear
			// weapons, and wonder." The AI's PowerRank uses "the same power
			// values shown on the F8 histograph screen"
			// (https://forums.civfanatics.com/threads/all-about-ai-aggression-levels-and-war-plotting.703785/),
			// but no source gives its formula. UNVERIFIED: the military
			// strength the military advisor compares (CalculateMilitaryStrength)
			// stands in for it.
			int power = (int)Math.Round(CalculateMilitaryStrength());

			// Score is the _average_ of "turn scores". We keep the exact sum
			// of the turn scores and round only the average, as rounding a
			// running average each turn would let errors build up (and a
			// rounded-down average could only rise in large enough steps).
			// Records without a sum come from older saves; the best we can
			// do there is assume every turn scored the recorded average.
			double previousSum = 0;
			if (lastTurn != null) {
				previousSum = lastTurn.TurnScoreSum ?? (double)lastTurn.Score * n;
			}
			float turnScore = ScoreVictory.ComputeTurnScore(this, gameData);
			// Under Rising Tide a fallen civ's score stands as it was when
			// it fell, for the end of the game to weigh against the rest
			// (see RisingTide.Winner); the turns it adds keep its average.
			bool risingTide = RisingTide.IsOn(gameData);
			if (risingTide && defeated && n > 0) {
				turnScore = (float)(previousSum / n);
			} else if (risingTide) {
				turnsSurvived++;
			}
			double turnScoreSum = previousSum + turnScore;
			int score = (int)Math.Round(turnScoreSum / (n + 1), MidpointRounding.AwayFromZero);
			if (risingTide) {
				score += RisingTide.ScoreBonus(this, gameData);
			}

			// Culture is "the sum of the cultural value of all your cities"
			int totalCulture = 0;
			foreach (City c in cities) {
				totalCulture += c.GetCulture();
			}

			history.Add(new HistTurnRecord {
				Date = gameData.timeOptions.GetRawNumber(gameData.turn),
				Power = power,
				Score = score,
				TurnScoreSum = turnScoreSum,
				Culture = totalCulture,
				VP = 0 // TODO: victory points
			});
		}
	}

}

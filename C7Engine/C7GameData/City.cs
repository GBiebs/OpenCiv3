using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog;
using C7Engine;
using C7Engine.Pathing;
using MoonSharp.Interpreter;

namespace C7GameData {
	public class CityBuilding {
		public Building building;
		public Player builtByPlayer;
		public int year;
		public int totalCulture; // This represents the total culture produced by the building.
								 // In Civ3, this value is displayed in the cultural advisor tab
								 // For a building that grants a unit every few turns (the Statue of
								 // Zeus, Knights Templar), the turns since it last did.
		public int turnsTowardFreeUnit;
	}

	public struct CommerceBreakdown {
		public int corrupted;
		public int taxes;
		public int beakers;
		public int happiness;
		public int wealth;
	}

	public struct CorruptableValue {
		// Civ3's City_CalcCorruption rounds the amount lost, halves up, and
		// then caps it at capTenths tenths of the amount, rounded down (90%
		// less 10% for each courthouse-class building, see
		// City.corruptionCapTenths). So 25 commerce at the 90% cap loses 22
		// and keeps 3. Civ3 binary (doc/perez-findings/Civ3_Findings.md,
		// Economy). With a cap of at most 90%, a city that makes anything
		// always keeps at least one.
		public CorruptableValue(int value, float corruption, int capTenths = 9) {
			if (value <= 0) {
				useful = value;
				corrupt = 0;
				return;
			}
			int lost = (int)Math.Floor(value * corruption + .5f);
			lost = Math.Clamp(lost, 0, value * Math.Max(capTenths, 0) / 10);
			corrupt = lost;
			useful = value - lost;
		}

		public int useful;
		public int corrupt;
	}

	public class City {
		private static ILogger log = Log.ForContext<City>();

		public ID id { get; set; }
		public Tile location { get; internal set; }
		public string name;
		public Dictionary<Player, int> perPlayerCulture = new();

		//Temporary production code because production is fun.
		public IProducible itemBeingProduced;
		public int shieldsStored { get; private set; } = 0;

		// What the player has queued up to build after itemBeingProduced, in
		// order.
		public List<IProducible> productionQueue = new();

		public int foodStored = 0;

		public bool capital = false;

		// Changing hands changes the buildings of both players, which tells
		// the caches derived from them (see Player.GetBuildingSnapshot).
		public Player owner {
			get => _owner;
			set {
				Player previous = _owner;
				_owner = value;
				if (!ReferenceEquals(previous, value)) {
					previous?.OnBuildingsChanged();
					value?.OnBuildingsChanged();
				}
			}
		}
		private Player _owner;

		public List<CityResident> residents = new List<CityResident>();

		// The list of buildings built in this city. You probably want to use
		// GetBuildings, which can also include buildings granted by wonders.
		//
		// Change it through AddBuilding and RemoveBuilding, or by assigning a
		// new list, so the owner's caches hear of it. (This city's own cache
		// also notices the list being changed directly, but the owner's
		// empire-wide one doesn't.)
		public List<CityBuilding> constructed_buildings {
			get => _constructedBuildings;
			set {
				_constructedBuildings = value;
				_owner?.OnBuildingsChanged();
			}
		}
		private List<CityBuilding> _constructedBuildings;

		// The order of this city within all the cities of a player for the
		// purposes of rank corruption calculations.
		//
		// This is updated each turn to avoid each city needing to do an O(n)
		// scan of the list, which would cause an O(n^2) overall calcuation.
		public int rankIndex = -1;

		// The amount of corruption, between 0 and 1.
		public float corruption = 0;

		// The share of shields wasted while celebrating "We Love the King
		// Day", between 0 and 1: less than corruption (see
		// CalculateCorruption). Commerce is corrupted as usual.
		public float celebrationWaste = 0;

		// The most corruption and waste can take, in tenths of the amount:
		// 9 less one per courthouse-class building and 7 per Forbidden
		// Palace (see CalculateCorruption).
		public int corruptionCapTenths = 9;

		// The number of turns of unhappiness this city will experience due to
		// pop rushing. Larger values result in larger numbers of citizens being
		// unhappy as well, in addition to the time penalty.
		public int turnsOfUnhappinessDueToPopRushing = 0;

		// The same for drafting (see Draft), and the citizens drafted this
		// turn, against the government's draft limit.
		public int turnsOfUnhappinessDueToDrafting = 0;
		public int unitsDraftedThisTurn = 0;

		// Whether a building was sold here this turn (see SellBuilding).
		public bool soldBuildingThisTurn = false;
		// Citizens confused by enemy propaganda that didn't make the city
		// defect; each is an unhappy face, and one fewer each turn (see
		// Espionage.ApplyPropaganda).
		public int propagandaConfusedCitizens = 0;

		public bool isInCivilDisorder = false;

		// Resistance in a conquered city: the number of citizens still
		// resisting, who are the last ones in residents, and resistanceFrom,
		// the civ it was taken from. As in Civ3 resisters work no tile and
		// eat nothing, and the city can't hurry. See StartResistance and
		// UpdateResistance.
		public int resisters = 0;
		public Player resistanceFrom;

		public bool IsInResistance => resisters > 0;

		// The turn the city last changed hands (taken, flipped, traded or
		// won by propaganda), or -1 if it never has or the save doesn't say. A city
		// never flips to another civ's culture on the turn it changed hands
		// (see CultureFlip).
		public int ownerChangedTurn = -1;

		// Civ3's flip cooldown: while it is at least 1 the city can't flip,
		// and each round's flip check counts it down by one instead (see
		// CultureFlip.SetFlipCooldown). 0 in older saves.
		public int flipCooldown;

		// Kings & Nomads: the city was a city-state a civ took, so it needs
		// an aqueduct to grow past the first size limit, fresh water or not
		// (see CanGrowPopulationByOne).
		public bool wasCityState;

		// The civ this city was last taken from by force, so that razing it
		// can give slave workers of that nationality (see
		// CityInteractions.RazeCity). Not saved: the captor decides whether
		// to raze in the same turn; after a load razing falls back to the
		// citizens' own nationality.
		internal Civilization capturedFrom;
		// The gold taken from its old owner when it was taken, for the news
		// if it's then razed (see LossNews). Not saved, like capturedFrom.
		internal int plunderedOnCapture;

		// A culture flip of this city that the human it would go to has yet
		// to accept or refuse, and the turn it was offered, -1 if none; the
		// offer lapses when that turn ends (see CultureFlip.Defect).
		public Player cultureFlipOfferedTo;
		public int cultureFlipOfferTurn = -1;

		// Whether production was hurried this turn, with gold, citizens or a
		// great leader. As in Civ3, the city then can't change what it's
		// building until the turn ends, so the hurried shields can't be
		// moved to something else.
		public bool hurriedThisTurn = false;

		// Whether the city has been given the shields of a cleared forest
		// since it last completed something. Per the project owner, it then
		// can't switch to a wonder, so chopped shields never go into one
		// (see Tile.MaybeAwardForestClearingShields).
		public bool receivedForestShields = false;

		// The turn disease last cost the city a citizen, or -1 (see
		// Disease).
		public int lastDiseaseTurn = -1;

		// Whether the city is ill: it loses a citizen every turn until it
		// recovers (see Disease).
		public bool ill = false;

		// Whether the city is celebrating "We Love the King Day".
		public bool celebrating = false;

		// What the city's governor (Civ3's "Contact Governor") manages for
		// its owner. See CityGovernor.
		public CityGovernor governor = new();

		// Where Civ3's "Set Rally Point" sends the units this city builds
		// (a goto, picked up at the start of the next turn), or null for no
		// rally point. Only human players' cities use it.
		public Tile rallyPoint;

		public static City NONE = new City(Tile.NONE, null, "Dummy City", ID.None("city"));

		public static bool IsValidCity(City city) {
			return city != null && city != City.NONE;
		}

		public City(Tile location, Player owner, string name, ID id) {
			this.id = id;
			this.location = location;
			this.owner = owner;
			this.name = name;
			if (owner != null) {
				this.perPlayerCulture.Add(owner, 0);
			}
			constructed_buildings = new();
		}

		internal City() {
			constructed_buildings = new();
		}

		public void SetItemBeingProduced(IProducible producible) {
			this.itemBeingProduced = producible;
		}

		// Sets what the city builds, on the player's order. Starting an army
		// the empire can't support yet is allowed, but the player is warned
		// that it won't be finished until there are enough cities.
		public void ChooseProduction(IProducible producible) {
			SetItemBeingProduced(producible);
			if (IsUnsupportedArmy(producible)) {
				WarnAboutUnsupportedArmy($"{name} has started on an army, but our empire is too small to support another one. "
					+ $"It won't be finished until we have {owner.rules.CitiesNeededToSupportAnArmy} cities for each army.");
			}
		}

		// Whether the item is an army that the owner doesn't have enough
		// cities to support.
		private bool IsUnsupportedArmy(IProducible producible) {
			return producible is UnitPrototype { isArmy: true } && !owner.CanSupportAnotherArmy();
		}

		private void WarnAboutUnsupportedArmy(string message) {
			if (owner.isHuman)
				new MsgShowMilitaryAdvisorPopup(owner, message, happy: false).send();
		}

		// Whether the player may switch production to the item.
		public bool CanChangeProduction(IProducible producible) {
			return WhyCannotChangeProduction(producible) == null;
		}

		// Why the player may not switch production to the item, or null if
		// they may. Production hurried this turn is locked in, and a city
		// given a cleared forest's shields can't switch to a wonder until it
		// has completed something, per the project owner.
		public string WhyCannotChangeProduction(IProducible producible) {
			if (producible == itemBeingProduced) {
				return null;
			}
			if (hurriedThisTurn) {
				return "Production was hurried this turn";
			}
			if (receivedForestShields && IsWonder(producible)) {
				return "Shields from a forest or a disbanded unit can't go to a wonder";
			}
			if (!Mobilization.AllowsStarting(owner, producible)) {
				return "While mobilized we may build only military units and improvements";
			}
			// A build with shields in the box that will be done next turn
			// can't become a wonder: "If it gets to one turn left you can
			// switch to other units/improvements, but NOT wonders"
			// (Travathian, https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/42626-distilled-civ3-tips-and-notes).
			if (IsWonder(producible) && itemBeingProduced != null && shieldsStored > 0 && TurnsUntilProductionFinished() <= 1) {
				return "A build finishing next turn can't be switched to a wonder";
			}
			return null;
		}

		public static bool IsWonder(IProducible producible) {
			return producible is Building b && (b.IsGreatWonder() || b.isSmallWonder);
		}

		// The stored shields that would be lost by switching production to
		// the item: Civ3 has no penalty for switching between categories,
		// per the project owner, but shields beyond the new item's cost
		// don't carry over.
		public int ShieldsLostByChangingTo(IProducible producible) {
			if (producible == null || producible == itemBeingProduced) {
				return 0;
			}
			return Math.Max(0, shieldsStored - owner.ShieldCost(producible));
		}

		// A short note for the production picker on what switching to the
		// item would mean, or null if nothing: why it can't be chosen, or
		// how many shields would be wasted.
		public string ProductionChangeWarning(IProducible producible) {
			string reason = WhyCannotChangeProduction(producible);
			if (reason != null) {
				return reason;
			}
			int lost = ShieldsLostByChangingTo(producible);
			return lost > 0 ? $"{lost} shield{(lost == 1 ? "" : "s")} will be wasted" : null;
		}

		// Changes production at the player's request. Stored shields carry over
		// to the new item, as in Civ 3, but any beyond its cost are lost.
		// Returns false if production can't change to it (see
		// WhyCannotChangeProduction).
		public bool ChangeProduction(IProducible producible) {
			string reason = WhyCannotChangeProduction(producible);
			if (reason != null) {
				log.Information("Not changing production in {City} to {Producible}: {Reason}", this, producible, reason);
				return false;
			}
			ChooseProduction(producible);
			shieldsStored = Math.Min(shieldsStored, owner.ShieldCost(producible));
			return true;
		}

		public bool IsCapital() {
			return capital;
		}

		// Whether a cleared forest's shields may go to this city: not while
		// it builds a wonder, per the project owner, or something that can't
		// hold shields.
		// Cities building a wonder, the Palace, a spaceship part or Wealth
		// are passed over: CivWiki (Wald roden (Civ3), https://www.civ-wiki.de/)
		// lists wonders, small wonders, the Palace, spaceship parts and Wealth;
		// forests "CANNOT be harvested to speed up PALACES, SMALL WONDERS or
		// WONDERS" (piratebrun, Apolyton thread 85881, compendium 07 section
		// 4.1). Wealth costs no shields.
		public bool CanReceiveForestShields() {
			return itemBeingProduced != null && !IsWonder(itemBeingProduced)
				&& !(itemBeingProduced is Building b && (b.isCenterOfEmpire || b.IsSpaceshipPart))
				&& owner.ShieldCost(itemBeingProduced) > 0;
		}

		// Adds a cleared forest's shields to the production box, up to the
		// item's cost. The city then can't switch to a wonder until it has
		// completed something (see WhyCannotChangeProduction).
		public void AddForestShields(int shields) {
			shieldsStored = Math.Min(shieldsStored + shields, owner.ShieldCost(itemBeingProduced));
			receivedForestShields = true;
		}

		// Adds a disbanded unit's shields to the production box. As with a
		// forest's, the city then can't switch to a wonder: with forest or
		// disband shields in the box "that particular city won't be able to
		// switch to a wonder" (Bamspeedy,
		// https://forums.civfanatics.com/threads/disbanding-question.18365/).
		public void AddDisbandShields(int shields) {
			SetStoredShields(shields, true);
			receivedForestShields = true;
		}

		/// <summary>
		/// Sets the current shield amount in the production box. If the add parameter is true, the shields get appended.<br/>
		/// </summary>
		/// <param name="shields">The number of shields to be added</param>
		/// <param name="add">If true, the shields get appended, otherwise they overwrite the current value</param>
		[LuaMethod]
		public void SetStoredShields(int shields, bool add = false) {
			// TODO: account for overflow if needed
			if (add)
				this.shieldsStored += shields;
			else
				this.shieldsStored = shields;
		}

		// The cached result of EffectiveBuildings: the buildings built in this
		// city followed by those granted by the owner's active great wonders.
		// It is immutable and replaced as a whole, and it is checked against
		// everything it was computed from on each use, in O(1): the owner, the
		// location (for continent-wide wonders), the constructed_buildings
		// list, its count and whether it has been modified since (which
		// catches a building being swapped for another without the count
		// changing), and the owner's wonder snapshot (which validates itself,
		// see Player.GetBuildingSnapshot).
		private sealed class EffectiveBuildingsCache {
			internal Player owner;
			internal Tile location;
			internal List<CityBuilding> source;
			internal int sourceCount;
			internal List<CityBuilding>.Enumerator sourceVersion;
			internal Player.BuildingSnapshot wonders;

			// The buildings, with the constructed ones first. Entries from
			// grantedStart on are granted by wonders. Never modified.
			internal List<CityBuilding> buildings;
			internal int grantedStart;

			// The same less any building obsolete for the owner: the
			// buildings whose effects apply (see EffectiveBuildings). The
			// same list as buildings when none is obsolete.
			internal List<CityBuilding> active;

			// Every building the owner's wonders provide to this city, whether
			// or not the city has also built it.
			internal HashSet<Building> providedByWonders;
		}

		private EffectiveBuildingsCache effectiveBuildingsCache;

		private EffectiveBuildingsCache GetEffectiveBuildingsCache() {
			Player.BuildingSnapshot wonders = owner.GetBuildingSnapshot();
			EffectiveBuildingsCache cache = effectiveBuildingsCache;
			if (cache != null
				&& ReferenceEquals(cache.wonders, wonders)
				&& ReferenceEquals(cache.owner, owner)
				&& ReferenceEquals(cache.location, location)
				&& ReferenceEquals(cache.source, constructed_buildings)
				&& cache.sourceCount == constructed_buildings.Count
				&& CollectionVersion.Unchanged(cache.sourceVersion)) {
				return cache;
			}

			cache = new EffectiveBuildingsCache {
				owner = owner,
				location = location,
				source = constructed_buildings,
				sourceCount = constructed_buildings.Count,
				sourceVersion = constructed_buildings.GetEnumerator(),
				wonders = wonders,
			};

			HashSet<Building> buildingsSeen = new();
			HashSet<Building> providedByWonders = new();
			List<CityBuilding> result = new();
			foreach (CityBuilding cb in constructed_buildings) {
				result.Add(cb);
				buildingsSeen.Add(cb.building);
			}
			cache.grantedStart = result.Count;

			// Granted buildings belong to the city's owner, not to whoever
			// built the wonder, so a captured wonder's buildings make culture
			// for its captor.
			//
			// Loop through all the wonders we control that aren't obsolete.
			foreach ((City c, CityBuilding cb) in wonders.activeWonders) {
				Building b = cb.building;

				if (b.greatWonderProperties.buildingGainedInEveryCity != null) {
					providedByWonders.Add(b.greatWonderProperties.buildingGainedInEveryCity);
				}
				if (b.greatWonderProperties.buildingGainedInEveryCityOnContinent != null
					&& c.location.continent == location.continent) {
					providedByWonders.Add(b.greatWonderProperties.buildingGainedInEveryCityOnContinent);
				}

				if (b.greatWonderProperties.buildingGainedInEveryCity != null
					&& buildingsSeen.Add(b.greatWonderProperties.buildingGainedInEveryCity)) {
					// Adding to buildingsSeen as we go keeps two wonders that
					// grant the same building from granting it twice.
					result.Add(new CityBuilding() {
						building = b.greatWonderProperties.buildingGainedInEveryCity,
						builtByPlayer = owner,
						year = cb.year,
						totalCulture = 0, // TODO: calculate this
					});
				}
				if (b.greatWonderProperties.buildingGainedInEveryCityOnContinent != null
					&& c.location.continent == location.continent
					&& buildingsSeen.Add(b.greatWonderProperties.buildingGainedInEveryCityOnContinent)) {
					result.Add(new CityBuilding() {
						building = b.greatWonderProperties.buildingGainedInEveryCityOnContinent,
						builtByPlayer = owner,
						year = cb.year,
						totalCulture = 0, // TODO: calculate this
					});
				}
			}

			cache.buildings = result;
			cache.active = result;
			foreach (CityBuilding cb in result) {
				if (cb.building.IsObsoleteFor(owner)) {
					cache.active = result.Where(x => !x.building.IsObsoleteFor(owner)).ToList();
					break;
				}
			}
			cache.providedByWonders = providedByWonders;
			effectiveBuildingsCache = cache;
			return cache;
		}

		// The buildings whose effects apply in this city: the same buildings
		// as GetBuildings, without allocating, less any building that is
		// obsolete for the owner (see Building.IsObsoleteFor). An obsolete
		// great wonder loses its effects, which the shipped game's code
		// forgets to check, but keeps making culture (owner's decision,
		// 2026-10-21): "The cultural benefits of a Wonder continue to accrue
		// unless the Wonder is destroyed" (the manual, p.130; compendium 04).
		// Other obsolete buildings do the same. The list is shared and must not be
		// modified, nor its granted entries (which GetBuildings hands out
		// fresh copies of).
		internal List<CityBuilding> EffectiveBuildings() {
			return GetEffectiveBuildingsCache().active;
		}

		// Every building in the city, an obsolete great wonder included,
		// for what the city has rather than what takes effect (what it may
		// build, say).
		internal List<CityBuilding> AllEffectiveBuildings() {
			return GetEffectiveBuildingsCache().buildings;
		}

		public List<CityBuilding> GetBuildings() {
			EffectiveBuildingsCache cache = GetEffectiveBuildingsCache();
			List<CityBuilding> result = new(cache.buildings.Count);
			for (int i = 0; i < cache.buildings.Count; ++i) {
				CityBuilding cb = cache.buildings[i];
				if (i < cache.grantedStart) {
					result.Add(cb);
				} else {
					// Buildings granted by wonders aren't stored anywhere, so
					// callers have always received new objects for them.
					result.Add(new CityBuilding() {
						building = cb.building,
						builtByPlayer = cb.builtByPlayer,
						year = cb.year,
						totalCulture = 0, // TODO: calculate this
					});
				}
			}
			return result;
		}

		// Whether this city has the building, including one granted by a
		// wonder.
		internal bool HasEffectiveBuilding(Building building) {
			foreach (CityBuilding cb in AllEffectiveBuildings()) {
				if (cb.building == building) {
					return true;
				}
			}
			return false;
		}

		public IEnumerable<IProducible> ListProductionOptions(GameData gameData) {
			HashSet<Resource> accessibleResources = GetAccessibleResources(gameData);
			return ListProductionOptions(gameData, accessibleResources);
		}

		// Like the original Where over units, buildings and inflows, this is
		// evaluated lazily each time it is enumerated. The facts every building
		// check needs (this city's buildings, what the empire is building) are
		// gathered once per enumeration instead of once or twice per building.
		private IEnumerable<IProducible> ListProductionOptions(GameData gameData, HashSet<Resource> accessibleResources) {
			foreach (UnitPrototype unitPrototype in gameData.unitPrototypes) {
				if (unitPrototype.CanProduce(this, accessibleResources)) {
					yield return unitPrototype;
				}
			}

			Building.ProductionContext context = null;
			foreach (Building building in gameData.Buildings) {
				// Per the project owner, the AI never builds a palace to move
				// its capital (losing the capital rebuilds it for free). Leaving
				// it out here covers every way the AI picks production: scoring,
				// the most expensive item after a lost wonder, leader hurries.
				if (building.isCenterOfEmpire && !owner.isHuman) {
					continue;
				}
				// A mobilized civ may start only military improvements, but
				// may finish what is under way (see Mobilization).
				if (building != itemBeingProduced && !Mobilization.AllowsStarting(owner, building)) {
					continue;
				}
				context ??= new Building.ProductionContext(this);
				if (building.CanProduce(this, accessibleResources, context)) {
					yield return building;
				}
			}

			foreach (Inflow inflow in gameData.Inflows) {
				if (inflow != itemBeingProduced && !Mobilization.AllowsStarting(owner, inflow)) {
					continue;
				}
				if (inflow.CanProduce(this, accessibleResources)) {
					yield return inflow;
				}
			}
		}

		internal HashSet<Resource> GetAccessibleResources(GameData gameData) {
			return GetAvailableResources(gameData).Keys.ToHashSet();
		}

		public int FoodNeededToGrow() {
			Rules rules = owner.rules;
			int food;
			if (residents.Count <= rules.MaximumLevel1CitySize) {
				food = rules.FoodNeededToGrowForLevel1Cities;
			} else if (residents.Count <= rules.MaximumLevel2CitySize) {
				food = rules.FoodNeededToGrowForLevel2Cities;
			} else {
				food = rules.FoodNeededToGrowForLevel3Cities;
			}

			// The difficulty's cost factor applies to the AI's growth as well
			// as its shields and research: "Cost Factor (affects growth,
			// shields, research) ... player cost factor is always 10", so on
			// Emperor (8) an AI city's food box is 8/10 the size
			// (https://forums.civfanatics.com/threads/ai-difficulty-level-bonuses.37490/).
			// See Player.ShieldCost and GameData.TechCostFor.
			Difficulty difficulty = EngineStorage.gameData?.AiBonusDifficulty(owner);
			if (!owner.isHuman && difficulty != null && difficulty.AiCostFactor > 0) {
				food = Math.Max(1, food * difficulty.AiCostFactor / difficulty.HumanCostFactor);
			}
			// A house rule may make the AI's growth cheaper still (see
			// AiHouseRules).
			float houseFactor = AiHouseRules.CostFactor(owner);
			if (houseFactor != 1) {
				food = Math.Max(1, (int)(food * houseFactor));
			}
			if (AcceleratedProduction(owner)) {
				food = Math.Max(1, food / 2);
			}
			return food;
		}

		// Civ3's Accelerated Production game option "simply halves the
		// capacity of every player's food- and shield-boxes" (tjs282, C3X
		// thread, post-16624265); Flintlock's code reading has a value scaled by
		// difficulty and "divided by 2 if accelerated production is on"
		// (post-16848272, context unclear). The tiles'
		// yields stay as they are, so a city grows and builds twice as fast
		// on the same surplus, and its commerce is unchanged. It halves tech
		// costs too: flag 0x200 "halves the food box, unit cost, improvement
		// cost and tech cost base" (Perez's decompile of Civ3 Conquests,
		// doc/perez-findings/Civ3_Findings.md, Round 2b; authoritative per the
		// owner's 2026-11-12 ruling), so research also goes twice as fast. See
		// Player.ShieldCost for the shield box and GameData.TechCostFor for
		// research.
		internal static bool AcceleratedProduction(Player owner) {
			return owner?.rules?.AcceleratedProduction == true;
		}

		public int TurnsUntilGrowth() {
			int foodGrowthPerTurn = FoodGrowthPerTurn();
			int foodNeededToGrow = FoodNeededToGrow();
			if (foodGrowthPerTurn == 0 || foodStored > foodNeededToGrow) {
				return int.MaxValue;
			} else if (foodGrowthPerTurn < 0) {
				return int.MinValue;
			}

			int additionalFoodNeeded = foodNeededToGrow - foodStored;
			int turnsRoundedDown = additionalFoodNeeded / foodGrowthPerTurn;
			if (additionalFoodNeeded % foodGrowthPerTurn != 0) {
				return turnsRoundedDown + 1;
			}
			return turnsRoundedDown;
		}

		public int TurnsToProduce(IProducible item) {
			int additionalProductionNeeded = owner.ShieldCost(item) - shieldsStored;
			int usefulShields = CurrentProductionYield().useful;
			if (usefulShields == 0) {
				return int.MaxValue;
			}

			int turnsRoundedDown = additionalProductionNeeded / usefulShields;
			if (additionalProductionNeeded % usefulShields != 0) {
				return Math.Max(turnsRoundedDown + 1, 1);
			}
			return Math.Max(turnsRoundedDown, 1);
		}

		public int TurnsUntilProductionFinished() {
			return TurnsToProduce(itemBeingProduced);
		}

		// Whether the production box is empty. Hurrying then costs double, as
		// in Civ3.
		private bool HurryingFromAnEmptyBox() => shieldsStored == 0;

		// The shields still needed to finish the current item.
		private int ShieldsLeftToHurry() {
			return owner.ShieldCost(itemBeingProduced) - shieldsStored;
		}

		// Hurrying from an empty box costs double. Civ3's
		// City_HurryProduction doubles the price, citizens or gold, after
		// working it out for the shields still needed. Civ3 binary
		// (doc/perez-findings/Civ3_Findings.md, 3.4 Hurry).
		private int EmptyBoxFactor() => HurryingFromAnEmptyBox() ? 2 : 1;

		// The gold it costs to buy the rest of the current item: the BIQ's
		// "shield value in gold" (4 in the standard rules) for each shield
		// still needed, the same for units and improvements, doubled from an
		// empty box. "The cost is four gold per shield purchased (or eight if
		// no shields have yet been expended on building it--this represents
		// the extra cost of starting and finishing the project on the same
		// turn)."
		// https://apolyton.net/forum/civilization-series/civilization-iii/132835-food-shield-gold-explanation-help-please
		internal int HurryGoldCost() {
			return ShieldsLeftToHurry() * owner.rules.ShieldValueInGold * EmptyBoxFactor();
		}

		// The citizens it costs to hurry the current item: those for the
		// shields still needed, rounded up, then doubled from an empty box,
		// "cost = ⌈remaining / shields-per-pop⌉, doubled with nothing
		// invested", Civ3 binary (doc/perez-findings/Civ3_Findings.md, 3.4
		// Hurry). So 30 shields at 20 a citizen from an empty box is 4
		// citizens, not the 3 that doubling the shields first would give.
		internal int HurryPopCost() {
			return PopRushCost(ShieldsLeftToHurry(), owner.rules.CitizenValueInShields) * EmptyBoxFactor();
		}

		// The citizens it costs to hurry the given number of shields: each
		// citizen sacrificed produces the BIQ's shields per citizen
		// ("Determines how many shields are produced by "sacrificing" a point
		// of population when hurrying", C3C editor help, Citizen Value in
		// Shields), so enough are taken to finish the item: rounded up, and
		// never none. The project owner chose to follow Civ3's own text here
		// (2026-10-20), as DBear's "per 20 shields or fraction thereof" also
		// reads.
		internal static int PopRushCost(int shields, int citizenValueInShields) {
			int value = Math.Max(1, citizenValueInShields);
			return Math.Max(1, (shields + value - 1) / value);
		}

		// Returns the feasibility of hurrying production
		public class HurryProductionDetails {
			public string? errorMessage;
			public string? costMessage;

			public int popCost = -1;
			public int goldCost = -1;
		}
		public HurryProductionDetails GetHurryProductionDetails() {
			Rules rules = EngineStorage.gameData.rules;

			if (isInCivilDisorder) {
				return new HurryProductionDetails() { errorMessage = "We cannot hurry production while our city is in civil disorder." };
			}
			if (IsInResistance) {
				return new HurryProductionDetails() { errorMessage = "We cannot hurry production while our city is in resistance." };
			}

			// Nothing to hurry: no production, a full box, or something like
			// Wealth that costs no shields.
			if (itemBeingProduced == null) {
				return new HurryProductionDetails() { errorMessage = "The city is not producing anything to hurry." };
			}
			if (ShieldsLeftToHurry() <= 0) {
				return new HurryProductionDetails() { errorMessage = "There is nothing to hurry in this city." };
			}

			// Civ3 never lets wonders, great or small, be bought or rushed
			// with citizens: "you cannot rush wonders, even the small ones"
			// (https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive/85927-when-and-why-to-hurry-the-production);
			// "a Leader is the only way to speed up Wonder production"
			// (https://apolyton.net/forum/civilization-series/civilization-iii/44853-can-you-hurry-up-wonders).
			if (itemBeingProduced is Building { isSmallWonder: true } || itemBeingProduced is Building b && b.IsGreatWonder()) {
				return new HurryProductionDetails() { errorMessage = "Wonders cannot be hurried." };
			}

			switch (owner.government.hurryingType) {
				case Government.HurryProductionType.CannotHurry:
					return new HurryProductionDetails() { errorMessage = "We cannot hurry production with this government." };

				case Government.HurryProductionType.ForcedLabor:
					int popCost = HurryPopCost();
					if (popCost > residents.Count / 2f) {
						return new HurryProductionDetails() { errorMessage = $"Hurrying production would take the lives of too many citizens ({popCost})." };
					}
					return new HurryProductionDetails() {
						costMessage = $"Hurrying production will take the lives of {popCost} citizen(s), are you sure?",
						popCost = popCost,
					};

				case Government.HurryProductionType.PaidLabor:
					int goldCost = HurryGoldCost();
					if (goldCost > owner.gold) {
						return new HurryProductionDetails() { errorMessage = $"Hurrying production would cost too much gold! ({goldCost})." };
					}
					return new HurryProductionDetails() {
						costMessage = $"Hurrying production will cost {goldCost} gold, are you sure?",
						goldCost = goldCost,
					};
			}
			throw new Exception($"Unknown hurrying type: {owner.government.hurryingType}");
		}

		public void HurryProduction() {
			Rules rules = EngineStorage.gameData.rules;
			HurryProductionDetails details = GetHurryProductionDetails();

			// The UI only offers hurrying when it's possible, but the city may
			// have changed since, so quietly ignore a request that no longer is.
			if (details.errorMessage != null) {
				log.Warning("Not hurrying production in {City}: {Error}", this, details.errorMessage);
				return;
			}

			switch (owner.government.hurryingType) {
				case Government.HurryProductionType.ForcedLabor:
					RemoveCitizens(details.popCost);
					turnsOfUnhappinessDueToPopRushing += rules.TurnPenaltyForEachHurrySacrifice * details.popCost;
					shieldsStored = owner.ShieldCost(itemBeingProduced);
					break;

				case Government.HurryProductionType.PaidLabor:
					owner.gold -= details.goldCost;
					shieldsStored = owner.ShieldCost(itemBeingProduced);
					break;
			}
			hurriedThisTurn = true;
		}

		// The Conquests Civilopedia (GCON_Draft): "When a civilization
		// develops Nationalism its citizens can be drafted into military
		// service. Each city can draft some of its population each turn,
		// depending upon the government currently in effect. A civilization
		// in Anarchy may not use the draft. A city in a Democracy or Republic
		// can draft [one] citizen per turn. A city in a country under
		// Communism, Despotism, Fascism, Feudalism, or Monarchy can draft
		// [two] citizens per turn. ... You may only draft citizens from a
		// city with a population of 7 or higher. The draft produces the
		// following conscript units, depending upon your level of scientific
		// development: Spearman, Pikeman, Musketman, Rifleman, Infantry, Mech.
		// Infantry ... However, drafting citizens will cause unhappiness in
		// the city." The tech is the BIQ's "enables conscription" one, the
		// per-city limit the government's draft limit, and the units those
		// with the BIQ's Draft flag; "you can't draft from any city less than
		// size 7" and "it makes 1 citizen per unit drafted unhappy for 20
		// turns" (https://forums.civfanatics.com/threads/question-about-drafting-in-civ3.107429/),
		// the 20 being the BIQ's turn penalty per drafted citizen.
		//
		// UNVERIFIED (simplest consistent behaviour):
		// - each draft costs one citizen, the Civilopedia's "draft [one]
		//   citizen" (a forum post says two, another one:
		//   https://forums.civfanatics.com/threads/draft-citizens.40326/);
		// - the draftee is the strongest defender with the Draft flag the
		//   city could build now (strategic resources included: "anywhere
		//   from a Spearman (all resources cut off) to a Mechanized
		//   Infantry", same thread), ties going to the attack, then the cost;
		// - like hurrying, a city in disorder or resisting can't draft;
		// - the citizen drafted is the last one, as for the whip.
		//
		// The size test is the city's size level, not the number 7:
		// "cities must be at least of level 2 size in order to draft." /
		// "it's actually checking that the city is at least size level 2."
		// (Flintlock, reading the 1.22 code,
		// https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16833692).
		// With the default BIQ (towns up to 6) that is the Civilopedia's 7.
		public static int MinimumPopulationToDraft(Rules rules) => rules.MaximumLevel1CitySize + 1;

		// The unit a draft here would give, or null if none could be.
		public UnitPrototype DraftableUnit(GameData gameData) {
			UnitPrototype best = null;
			foreach (IProducible p in ListProductionOptions(gameData)) {
				if (p is not UnitPrototype u || !u.isDraftable) {
					continue;
				}
				if (best == null || u.defense > best.defense
					|| (u.defense == best.defense && (u.attack > best.attack
						|| (u.attack == best.attack && u.shieldCost > best.shieldCost)))) {
					best = u;
				}
			}
			return best;
		}

		// Why this city can't draft a citizen now, or null if it can.
		public string WhyCannotDraft(GameData gameData) {
			if (!owner.HasTechEnabling(C7GameData.Save.SaveTech.Flag.EnablesConscription)) {
				return "We do not yet know how to draft citizens.";
			}
			if (owner.government.transitionType || owner.government.draftLimit <= 0) {
				return "We cannot draft citizens with this government.";
			}
			if (isInCivilDisorder) {
				return "The city is in disorder and cannot draft citizens.";
			}
			if (IsInResistance) {
				return "The city is resisting our rule and cannot draft citizens.";
			}
			if (residents.Count < MinimumPopulationToDraft(owner.rules)) {
				return $"Only cities of size {MinimumPopulationToDraft(owner.rules)} or more can draft citizens.";
			}
			if (unitsDraftedThisTurn >= owner.government.draftLimit) {
				return "The city has drafted all the citizens it can this turn.";
			}
			if (DraftableUnit(gameData) == null) {
				return "There is no unit for the draftees.";
			}
			return null;
		}

		// Drafts a citizen into a conscript unit, returning it, or null if
		// the city can't draft (see WhyCannotDraft).
		public MapUnit Draft(GameData gameData) {
			string why = WhyCannotDraft(gameData);
			if (why != null) {
				log.Warning("Not drafting in {City}: {Error}", this, why);
				return null;
			}
			UnitPrototype type = DraftableUnit(gameData);
			RemoveLastCitizen();
			++unitsDraftedThisTurn;
			turnsOfUnhappinessDueToDrafting += gameData.rules.TurnPenaltyForEachDraftedCitizen;

			MapUnit unit = gameData.SpawnUnit(owner, type, location);
			// Conscripts, the least experienced level.
			if (gameData.experienceLevels.Count > 0) {
				unit.experienceLevel = gameData.experienceLevels[0];
				unit.experienceLevelKey = unit.experienceLevel.key;
				unit.hitPointsRemaining = unit.maxHitPoints;
			}
			return unit;
		}

		// Fills the production box, so that the current item is finished at
		// the end of the turn. A great leader hurries production this way.
		internal void FillProductionBox() {
			if (itemBeingProduced != null) {
				shieldsStored = owner.ShieldCost(itemBeingProduced);
				hurriedThisTurn = true;
			}
		}

		public void HandleCityProduction(GameData gameData) {
			// The turn is over, so production can be changed again, and the
			// city may draft again.
			hurriedThisTurn = false;
			unitsDraftedThisTurn = 0;
			soldBuildingThisTurn = false;
			IProducible producedItem = ComputeTurnProduction();
			if (producedItem == null) {
				return;
			}

			log.Debug("Produced {ProducedItem} in {City}", producedItem, this);
			if (producedItem is not Inflow) {
				receivedForestShields = false;
			}
			if (producedItem is UnitPrototype prototype) {
				owner.stats.RecordUnitBuilt(prototype);
				SendToRallyPoint(AddUnit(prototype, gameData));
			} else if (producedItem is Building building) {
				if (building.isCenterOfEmpire) {
					owner.MovePalaceTo(this, building);
				} else if (building.IsSpaceshipPart) {
					// Spaceship parts go to the owner's ship, not the city.
					SpaceRace.OnPartCompleted(gameData, this, building);
				} else {
					AddBuilding(building);
				}

				if (building.buildSpaceshipParts) {
					SpaceRace.OnApolloCompleted(gameData, this, building);
				}

				// Theory of Evolution: two free advances, once, on completion.
				if (building.twoFreeAdvances) {
					owner.GrantFreeTechs(gameData, 2, building.name, this);
				}

				// If we completed a great wonder, mark it as completed so no
				// other civ can build it. If any other cities are building the
				// wonder then change production to the next item in their
				// queue, or else the most expensive option, to avoid wasting
				// the shields.
				//
				// TODO: This should interrupt the player's turn to let them
				// make the choice. And does the game allow wonder shuffling in
				// this situation?
				if (building.greatWonderProperties != null) {
					gameData.GreatWondersBuilt.Add(building.name);
					// At the city, for its owner (Civ3 FUN_004b3f20).
					gameData.gameHistory.Record(gameData.turn, ReplayEventType.WonderBuilt, owner, location, building.name);
					VictoryPoints.OnWonderBuilt(owner, building, gameData.victoryConditions);
					owner.MaybeStartGoldenAgeFromWonders(gameData);
					AnnounceWonder(gameData, building);

					foreach (Player p in gameData.players) {
						if (p == this.owner) {
							continue;
						}

						foreach (City c in p.cities) {
							if (c.itemBeingProduced?.name == building.name) {
								c.SwitchFromLostWonder(gameData);
							}
						}
					}
				}
			} else if (producedItem is Inflow inflow) {
				// we don't want to "complete" the inflow production
				// unless the player has a reason to change it, this should be ongoing
				return;
			}

			IProducible next = ChooseNextProduction(gameData, producedItem);
			SetItemBeingProduced(next);
			if (owner.isHuman) {
				new MsgCityProductionCompleted(owner, this, producedItem.name, next?.name).send();
			}
		}

		// Tells every other human player which civ has completed a great
		// wonder. The builder hears of it from the production popup.
		// Spectators hear of every wonder.
		private void AnnounceWonder(GameData gameData, Building wonder) {
			foreach (Player p in gameData.players) {
				if (!p.isHuman || p.defeated || p == owner) {
					continue;
				}
				new MsgWonderCompleted(p, owner, wonder.name, name).send();
			}
			new MsgWonderCompleted(null, owner, wonder.name, name) { forSpectators = true }.send();
		}

		// What the city builds once it has finished an item: the next item in
		// its production queue that can still be built, or, for a human player
		// with nothing queued, the same unit again, unless the city's governor
		// manages production (Civ3 FUN_00438ab0 at 0x438c49 repeats the unit
		// only if governor flag 0x10 is off). Otherwise the AI picks, with
		// the governor's production options (see GovernorProduction).
		private IProducible ChooseNextProduction(GameData gameData, IProducible completed) {
			IProducible queued = TakeNextQueuedProduction(gameData);
			if (queued != null) {
				return queued;
			}
			if (owner.isHuman && !governor.manageProduction && completed is UnitPrototype
					&& ListProductionOptions(gameData).Contains(completed)) {
				return completed;
			}
			return ChooseProducible.Choose(this, owner);
		}

		// Takes the next item off the production queue that can still be
		// built, dropping any before it that can't. Null if there's none.
		private IProducible TakeNextQueuedProduction(GameData gameData) {
			if (productionQueue.Count == 0) {
				return null;
			}
			HashSet<IProducible> options = ListProductionOptions(gameData).ToHashSet();
			while (productionQueue.Count > 0) {
				IProducible queued = productionQueue[0];
				productionQueue.RemoveAt(0);
				if (options.Contains(queued)) {
					return queued;
				}
			}
			return null;
		}

		// Adds an item to the end of the production queue, to be built after
		// the current item and those already queued.
		public void EnqueueProduction(IProducible producible) {
			productionQueue.Add(producible);
		}

		public void ClearProductionQueue() {
			productionQueue.Clear();
		}

		// Moves a city off a great wonder a rival has just completed, to the
		// next item in its queue or else the most expensive one it can build.
		// The stored shields carry over, but any beyond the new item's cost
		// are lost at once, at the moment of the switch, rather than on the
		// next production pass: otherwise they would sit in the box, and a
		// switch back to something dearer before the turn ended would keep
		// them all (owner's decision, 2026-10-21).
		internal void SwitchFromLostWonder(GameData gameData) {
			IProducible next = TakeNextQueuedProduction(gameData) ?? GetMostExpensiveItemToProduce();
			SetItemBeingProduced(next);
			if (next != null) {
				shieldsStored = Math.Min(shieldsStored, owner.ShieldCost(next));
			}
		}

		private IProducible GetMostExpensiveItemToProduce() {
			IProducible best = null;
			int bestCost = 0;
			foreach (IProducible ip in ListProductionOptions(EngineStorage.gameData)) {
				int cost = owner.ShieldCost(ip);
				if (best == null || cost > bestCost) {
					best = ip;
					bestCost = cost;
				}
			}
			return best;
		}

		// Stores the turn's food, then grows or starves the city. Returns
		// true if it starved to nothing, for the caller to destroy (see
		// Player.HandleCityUpdates).
		public bool HandleCityGrowth(GameData gameData) {
			int foodNeededToGrow = FoodNeededToGrow();
			int foodGrowth = FoodGrowthPerTurn();
			// In disorder the citizens eat everything the city makes, so it
			// neither stores food nor starves: "In disorder upkeep =
			// production, so surplus is 0". Civ3 binary
			// (doc/perez-findings/Civ3_Findings.md, Food, growth, starvation).
			if (isInCivilDisorder) {
				foodGrowth = 0;
			}
			bool hasGranary = HasGranary();

			foodStored += foodGrowth;
			// Food beyond the box is thrown away, also every turn a city can't grow.
			owner.stats.RecordGrowth(surplus: Math.Max(foodGrowth, 0), wasted: Math.Max(foodStored - foodNeededToGrow, 0));
			foodStored = Math.Min(foodStored, foodNeededToGrow);

			// Handle the city starving: "stock < 0 => stock 0 and lose 1
			// citizen; if size reaches 0 the city is destroyed. This can
			// happen to a size-1 city if its tile yield falls below its
			// upkeep" (same source).
			if (foodStored < 0) {
				foodStored = 0;
				if (residents.Count <= 1) {
					return true;
				}
				RemoveStarvingCitizen();
				return false;
			}

			// No growth necessary.
			if (foodStored < foodNeededToGrow) {
				return false;
			}

			// With Longevity a full food box adds two citizens instead of one.
			// The second still needs room to grow (an aqueduct or hospital
			// where the city reaches the next size level).
			int newCitizens = owner.GetBuildingSnapshot().doublesCityGrowthEverywhere ? 2 : 1;
			bool grew = false;
			for (int i = 0; i < newCitizens && CanGrowPopulationByOne(gameData); ++i) {
				CityResident newResident = new CityResident();
				newResident.nationality = owner.civilization;
				newResident.city = this;
				newResident.citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen);
				AddCitizen(newResident);
				C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, newResident, manageMoods: governor.manageMoods);
				grew = true;
			}

			if (grew) {
				if (hasGranary) {
					foodStored /= 2;
				} else {
					foodStored = 0;
				}
			}
			return false;
		}

		// Whether a fresh water source is within the city's radius, as a
		// Nuclear Plant needs: "A city must have fresh water within its
		// radius to build a Nuclear Plant" (Conquests Civilopedia,
		// BLDG_Nuclear_Plant; "a source of fresh water within its 21-tile
		// city radius",
		// https://civfanatics.com/civ3/strategy/game-mechanics/everything-you-wanted-to-know-about-power-plants/).
		// Fresh water is a river or a lake (Civilopedia, TERR_River and
		// TERR_Lake), so a tile in the radius along a river or a lake tile.
		public bool HasFreshWaterInRadius() {
			foreach (Tile t in GetTilesOfRank(owner.rules.MaxRankOfWorkableTiles)) {
				if (t == null || t == Tile.NONE) {
					continue;
				}
				if (t.BordersRiver() || (t.IsWater() && t.isFreshWater)) {
					return true;
				}
			}
			return false;
		}

		// Whether the city is growing: it has a food surplus and room to
		// grow past its present size (see CanGrowPopulationByOne).
		public bool CanGrow() {
			GameData gameData = EngineStorage.gameData;
			return gameData != null && FoodGrowthPerTurn() > 0 && CanGrowPopulationByOne(gameData);
		}

		// Whether a unit may join the city (see MapUnit.CanJoinCity): Civ3
		// lets one join a city with room to grow past its present size
		// (FUN_004b8d50) whose food surplus isn't negative (FUN_005d0670
		// tests the city's food surplus, city+0x250, against 0).
		public bool CanBeJoined() {
			GameData gameData = EngineStorage.gameData;
			return gameData != null && CanGrowPopulationByOne(gameData) && FoodGrowthPerTurn() >= 0;
		}

		internal bool CanGrowPopulationByOne(GameData gD) {
			// A Kings & Nomads city-state grows without limit (owner's ruling,
			// 2026-10-25), so it comes to have specialists (see CityStates).
			if (CityStateTrade.IsCityState(this)) {
				return true;
			}

			// We can always grow up to size 6 (see Rules.SizeWithoutAqueduct).
			if (residents.Count + 1 <= gD.rules.GrowthLimitWithoutAqueduct) {
				return true;
			}

			// If the city doesn't have fresh water and doesn't have an aqueduct
			// then it can't grow into a city.
			//
			// TODO: lakes are bodies of water under 20 tiles (https://civilization.fandom.com/wiki/Fresh_Water_Lake_(Civ3))
			bool hasFreshwaterAccess = location.BordersRiver();
			foreach (Tile t in location.neighbors.Values) {
				if (!t.IsLand() && t.isFreshWater) {
					hasFreshwaterAccess = true;
					break;
				}
			}
			// A city-state a civ took needs an aqueduct (owner's ruling,
			// 2026-10-25).
			if (wasCityState) {
				hasFreshwaterAccess = false;
			}

			List<CityBuilding> buildings = EffectiveBuildings();
			bool canGrowIntoCity = hasFreshwaterAccess;
			if (!hasFreshwaterAccess) {
				foreach (CityBuilding cb in buildings) {
					if (cb.building.allowsCitySize2 || cb.building.allowsCitySize3) {
						canGrowIntoCity = true;
						break;
					}
				}
			}

			if (!canGrowIntoCity) {
				return false;
			}

			// With fresh water or an aqueduct we can grow to size 12.
			if (residents.Count + 1 <= gD.rules.GrowthLimitWithoutHospital) {
				return true;
			}

			// If we're a city trying to grow into a metropolis, we need a
			// hospital.
			foreach (CityBuilding cb in buildings) {
				if (cb.building.allowsCitySize3) {
					return true;
				}
			}
			return false;
		}

		public bool HasGranary() {
			foreach (CityBuilding cb in EffectiveBuildings()) {
				if (cb.building.doublesCityGrowthRate) {
					return true;
				}
			}
			return false;
		}

		public bool HasWalls() {
			foreach (CityBuilding cb in EffectiveBuildings()) {
				if (cb.building.providesWalls) {
					return true;
				}
			}
			return false;
		}

		// The city's barracks, harbor and airport, built or granted by a wonder:
		// the buildings that make veteran land, sea and air units. Null if it
		// has none.
		public Building Barracks() => EffectiveBuildingWhere(b => b.providesVeteranGroundUnits);
		public Building Harbor() => EffectiveBuildingWhere(b => b.providesVeteranSeaUnits);
		public Building Airport() => EffectiveBuildingWhere(b => b.providesVeteranAirUnits);

		private Building EffectiveBuildingWhere(Func<Building, bool> predicate) {
			foreach (CityBuilding cb in EffectiveBuildings()) {
				if (predicate(cb.building)) {
					return cb.building;
				}
			}
			return null;
		}

		// The bonuses units in the city get defending against the attacker
		// (null if unknown, treated as a land unit).
		public IEnumerable<StrengthBonus> GetDefenseBonuses(MapUnit attacker = null) {
			// None while it resists: "Cities with resisters do not give
			// defensive bonuses" (Conquests Civilopedia, GCON_Terrain_Combat).
			// The tile's terrain bonus still counts (see MapUnit).
			if (IsInResistance) {
				yield break;
			}

			GameData gD = EngineStorage.gameData;

			// Cities give defense bonuses based on their size.
			if (residents.Count > gD.rules.MaximumLevel2CitySize) {
				yield return gD.cityLevel3DefenseBonus;
			} else if (residents.Count > gD.rules.MaximumLevel1CitySize) {
				yield return gD.cityLevel2DefenseBonus;
			} else {
				yield return gD.cityLevel1DefenseBonus;
			}

			bool isTown = residents.Count <= gD.rules.MaximumLevel1CitySize;

			// Buildings, such as walls, can also give bonuses. The BIQ gives a
			// building one bonus against land attacks and another against
			// naval ones: City Walls give "+50% to defensive bonus against
			// land attacks", a Coastal Fortress "+50% against naval attacks"
			// (https://civfanatics.com/civ3/civilopedia/improvements/).
			// UNVERIFIED: which applies against air units, which only
			// bombard in Civ3; the land one does, as before.
			// MapUnit.NONE, the map's opponent when no unit is selected, has
			// no unit type.
			//
			// Only the best single building counts: they don't add up, so a
			// town with Walls and Civil Defense gets +50%, not +100%. Walls,
			// meaning a building with a bombard defense, count only while the
			// settlement is a town. C3X's rewrite of the stock
			// City_get_building_defense_bonus, which its author describes as
			// only making the stock loop faster, keeps the largest bonus
			// ("if (building_defense > tr) tr = building_defense;") and
			// skips a building with "improv->Combat_Bombard" above 0 unless
			// the city is no bigger than "MaximumSize_Town"
			// (https://github.com/maxpetul/C3X/blob/master/injected_code.c,
			// patch_City_get_building_defense_bonus). UNVERIFIED (not
			// modelled): its doubling for a wonder with the "Doubles City
			// Defenses" flag, which no Conquests wonder has.
			bool navalAttack = attacker?.unitType?.IsSeaUnit() ?? false;
			StrengthBonus? best = null;
			foreach (CityBuilding cb in EffectiveBuildings()) {
				StrengthBonus? bonus = navalAttack ? cb.building.navalDefenseBonus : cb.building.combatDefenseBonus;
				if (bonus is not StrengthBonus defenseBonus) {
					continue;
				}
				bool walls = cb.building.onlyUsefulInTowns || cb.building.bombardDefense > 0;
				if (walls && !isTown) {
					continue;
				}
				if (best is not StrengthBonus b || defenseBonus.amount > b.amount) {
					best = defenseBonus;
				}
			}
			if (best is StrengthBonus bestBonus) {
				yield return bestBonus;
			}
		}

		/**
		 * Computes turn production.  If the production queue finishes,
		 * returns the item that is built.  Otherwise, returns null.
		 */
		public IProducible ComputeTurnProduction() {
			int shieldsBefore = shieldsStored;
			int cost = owner.ShieldCost(itemBeingProduced);
			CorruptableValue production = CurrentProductionYield();
			shieldsStored += production.useful;
			// Shields beyond the cost are thrown away, whether the build finishes or not.
			int excess = Math.Max(shieldsStored - cost, 0);

			// Like a settler waiting for the city to grow, an army the empire
			// can't support waits with its shields kept in the box.
			bool unsupportedArmy = IsUnsupportedArmy(itemBeingProduced);
			if (shieldsStored >= cost && residents.Count > itemBeingProduced.populationCost && !unsupportedArmy) {
				shieldsStored = 0;
				// Wealth turns all its shields into gold every turn.
				RecordProductionStats(production, overflowed: itemBeingProduced is Inflow ? 0 : excess, capped: 0);
				RemoveCitizens(itemBeingProduced.populationCost);
				return itemBeingProduced;
			}

			if (unsupportedArmy && shieldsStored >= cost && shieldsBefore < cost) {
				WarnAboutUnsupportedArmy($"The army in {name} is ready, but our empire is too small to support another one. "
					+ $"It will be finished once we have {owner.rules.CitiesNeededToSupportAnArmy} cities for each army.");
			}

			shieldsStored = Math.Min(shieldsStored, owner.ShieldCost(itemBeingProduced));
			RecordProductionStats(production, overflowed: 0, capped: excess);
			return null;
		}

		// For the statistics (see PlayerStats.RecordProduction). In civil
		// disorder or anarchy all the city's shields count as lost to it.
		private void RecordProductionStats(CorruptableValue production, int overflowed, int capped) {
			bool anarchy = owner.government.transitionType;
			bool idle = anarchy || isInCivilDisorder;
			int lost = idle ? production.corrupt : 0;
			owner.stats.RecordProduction(produced: production.useful + production.corrupt, corrupted: idle ? 0 : production.corrupt,
				lostToDisorder: anarchy ? 0 : lost, lostToAnarchy: anarchy ? lost : 0, overflowed, capped);
		}

		public int CurrentFoodYield() {
			List<CityBuilding> buildings = EffectiveBuildings();
			int yield = location.FoodYield(this, buildings).yield;
			foreach (CityResident r in WorkingResidents()) {
				yield += r.tileWorked.FoodYield(this, buildings).yield;
			}
			return yield;
		}

		// The food, shields and commerce the government's tile penalty (e.g.
		// despotism's) takes from the city's tiles this turn.
		public (int food, int shields, int commerce) TileYieldPenalties() {
			List<CityBuilding> buildings = EffectiveBuildings();
			int food = location.FoodYield(this, buildings).penalty;
			int shields = location.ProductionYield(this, buildings).penalty;
			int commerce = location.CommerceYield(this, buildings).penalty;
			foreach (CityResident r in WorkingResidents()) {
				food += r.tileWorked.FoodYield(this, buildings).penalty;
				shields += r.tileWorked.ProductionYield(this, buildings).penalty;
				commerce += r.tileWorked.CommerceYield(this, buildings).penalty;
			}
			return (food, shields, commerce);
		}

		public CorruptableValue CurrentProductionYield() {
			List<CityBuilding> buildings = EffectiveBuildings();
			int yield = location.ProductionYield(this, buildings).yield;
			foreach (CityResident r in WorkingResidents()) {
				yield += r.tileWorked.ProductionYield(this, buildings).yield;
			}
			// A celebrating city wastes fewer shields (see CalculateCorruption).
			CorruptableValue result = new(yield, celebrating ? celebrationWaste : corruption, corruptionCapTenths);

			// Using our value of corruption, figure out how much useful
			// production we have to work with. Special case anarchy, where no
			// useful production is available. We do this here rather than
			// setting corruption to 100% because CorruptableValue would give us
			// one useful commerce in that situation.
			//
			// The same is true for civil disorder.
			if (owner.government.transitionType || isInCivilDisorder) {
				result.useful = 0;
				result.corrupt = yield;
			}

			// Specialists work only when the city does. Policemen cut waste
			// before factories and power plants multiply what's left: a
			// policeman "reduces corruption by one shield and one commerce
			// arrow, subject to multiplier buildings"
			// (https://forums.civfanatics.com/threads/civ3-conquests-additions-changes-list.104294/).
			bool working = !owner.government.transitionType && !isInCivilDisorder;
			if (working) {
				RecoverWithPolicemen(ref result);
			}

			// Factories and power plants boost the shields left after waste.
			result.useful += result.useful * ProductionBonusPercent(buildings) / 100;

			// Civil engineers then add their own shields, but only to
			// buildings: each "produces two incorruptible, non-multiplicative
			// shields for use on buildings" (same source), "not military,
			// workers or settlers"
			// (https://civfanatics.com/civ3/strategy/empire-management/the-role-of-the-specialist-citizen/).
			if (working && itemBeingProduced is Building) {
				foreach (CityResident cr in WorkingResidents()) {
					result.useful += cr.citizenType?.Construction ?? 0;
				}
			}

			// Kings & Nomads' trade with the city-states brings shields that
			// nothing wastes (see CityStateTrade), and a city-state's
			// specialists make shields (see CityStates).
			if (working) {
				result.useful += CityStateTrade.Shields(EngineStorage.gameData, this);
				if (CityStateTrade.IsCityState(this)) {
					result.useful += CityStates.Specialists(this) * CityStates.SpecialistShields;
				}
			}

			return result;
		}

		// Policemen (specialists with a corruption value) win back lost
		// commerce or shields, the BIQ's value of each: "By assigning one of
		// your citizens to be a policeman, you can cause one corrupted
		// commerce and one wasted shield to become uncorrupted"
		// (https://forums.civfanatics.com/threads/everything-about-corruption-c3c-edition.76619/).
		// Never more than was lost.
		private void RecoverWithPolicemen(ref CorruptableValue value) {
			int recovered = 0;
			foreach (CityResident cr in WorkingResidents()) {
				recovered += cr.citizenType?.Corruption ?? 0;
			}
			recovered = Math.Min(recovered, value.corrupt);
			if (recovered > 0) {
				value.corrupt -= recovered;
				value.useful += recovered;
			}
		}

		// The percentage the given buildings add to the city's useful shields.
		// As in Civ3, a production building only works alongside the building
		// it requires: a power plant ("increases factory output") does nothing
		// without a Factory, even one granted by the Hoover Dam. And a city
		// only runs one power plant (they replace each other), so of several
		// plants, e.g. a built Coal Plant and the Hoover Dam's Hydro Plant,
		// only the best counts.
		internal static int ProductionBonusPercent(List<CityBuilding> buildings) {
			int total = 0;
			int bestReplaceable = 0;
			foreach (CityBuilding cb in buildings) {
				Building b = cb.building;
				if (b.productionBonusPercent == 0) {
					continue;
				}
				if (b.requiredBuilding != null && !buildings.Exists(other => other.building == b.requiredBuilding)) {
					continue;
				}
				if (b.replacesOtherBuildings) {
					bestReplaceable = Math.Max(bestReplaceable, b.productionBonusPercent);
				} else {
					total += b.productionBonusPercent;
				}
			}
			return total + bestReplaceable;
		}

		// The pollution the city's buildings add to it, from the BIQ's
		// per-building pollution value (e.g. the Iron Works' 4). A building
		// one of the owner's wonders provides adds none: the C3X mod offers,
		// as a change to the base game, an option that "Causes improvements
		// granted for free by great or small wonders to contribute pollution
		// as though they had been built in the city"
		// (https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
		// As with upkeep (see MaintenanceCostsRaw), one also built in the
		// city counts as the wonder's.
		public int BuildingPollution() {
			int result = 0;
			foreach (CityBuilding cb in EffectiveBuildings()) {
				if (!IsProvidedByWonders(cb.building)) {
					result += cb.building.pollution;
				}
			}
			return result;
		}

		// The city's pollution icons ("triangles"), each a 1% chance a turn
		// that pollution strikes the city (see MaybePollute). From
		// https://alt.games.civ3.narkive.com/xmuzDw5z/civ-3-pollution-reduction:
		// "Each population point above 12 will produce 1 pollution icon in
		// the city display"; "Certain buildings produce pollution icons. For
		// example... a factory will always produce 2 pollution icons"; mass
		// transit "eliminates all but 1 pollution icons due to population";
		// a recycling center "removes all but 1 icon due to improvements";
		// "each icon represents a 1% chance of pollution hitting that city
		// each turn". The same "one pollution point for each citizen over
		// 12 in a city, plus those from buildings" is in
		// https://steamcommunity.com/sharedfiles/filedetails/?id=1265159547,
		// "it will only cut it down to 1 for pop and 1 for buildings" and
		// population pollution "linked to city size level 3, i.e. ... only
		// ... in a metropolis" in
		// https://forums.civfanatics.com/threads/pollution-from-population.299975/,
		// so the 12 is the BIQ's largest size-2 city. The Conquests
		// Civilopedia: "The Mass Transit System reduces pollution caused by
		// population", "The Recycling Center decreases pollution caused by
		// city improvements" (the BIQ's two pollution flags). Population
		// pollution starts over the threshold, at size 13, not at size 12 as
		// in the shipped game (owner's decision, 2026-10-21).
		public int PollutionIcons(Rules rules) {
			int population = Math.Max(0, residents.Count - rules.MaximumLevel2CitySize);
			int buildingPollution = 0;
			bool removesPopulation = false;
			bool reducesBuildings = false;
			foreach (CityBuilding cb in EffectiveBuildings()) {
				if (!IsProvidedByWonders(cb.building)) {
					buildingPollution += cb.building.pollution;
				}
				removesPopulation |= cb.building.removesPopulationPollution;
				reducesBuildings |= cb.building.reducesBuildingPollution;
			}
			if (removesPopulation) {
				population = Math.Min(population, 1);
			}
			if (reducesBuildings) {
				buildingPollution = Math.Min(buildingPollution, 1);
			}
			return population + Math.Max(0, buildingPollution);
		}

		// Called once a turn: with a chance of 1% per pollution icon,
		// pollution strikes one of the city's tiles, which is returned.
		//
		// Which tile: the search starts at a random one of the 20 tiles
		// around the city and takes the first suitable one from there. The
		// C3X mod intercepts "the call where the game gets a random index at
		// which to start searching for a suitable tile to become polluted.
		// Normally, it passes 20 as the limit here"
		// (patch_rand_int_to_place_pollution,
		// https://github.com/maxpetul/C3X/blob/master/injected_code.c). The
		// tiles are taken in Civ3's city-tile spiral (see
		// ImportCiv3.GetTileFromSpiral), the order of the save's worked
		// tiles. UNVERIFIED: that the search walks that order, and what
		// counts as suitable; here a land tile other than the city's own
		// ("a tile outside your city",
		// https://apolyton.net/forum/miscellaneous/archives/civ3-strategy-archive-aa/54340-pollution)
		// that isn't already polluted. None, no pollution.
		//
		// The city's pollution icons also add to world pollution, which
		// brings global warming (see GlobalWarming); pollution lying on the
		// map doesn't.
		private const int PollutionSearchTiles = 20;

		public Tile MaybePollute(GameData gameData) {
			int icons = PollutionIcons(gameData.rules);
			// No draw without icons, so cities that can't pollute leave the
			// random numbers alone.
			if (icons <= 0 || GameData.rng.Next(100) >= icons) {
				return null;
			}
			Tile tile = null;
			int start = GameData.rng.Next(PollutionSearchTiles);
			Save.TileLocation here = new(location);
			for (int k = 0; k < PollutionSearchTiles && tile == null; ++k) {
				Save.TileLocation at = ImportCiv3.GetTileFromSpiral(here, 1 + (start + k) % PollutionSearchTiles);
				Tile t = gameData.map.tileAt(at.X, at.Y);
				if (t != location && t != Tile.NONE && !t.IsWater() && !t.HasPollution()) {
					tile = t;
				}
			}
			if (tile == null || !Tile.TryAddPollution(tile)) {
				return null;
			}
			// The citizen working the tile is taken off it and made a
			// specialist. C3X's option "keeps citizens working the tiles
			// they've been assigned to when pollution spawns there instead of
			// converting them to specialists" (do_not_unassign_workers_from_polluted_tiles,
			// https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini),
			// so the shipped game converts them. UNVERIFIED: which
			// specialist; here the first the owner knows (the entertainer).
			CityResident worker = tile.personWorkingTile;
			if (worker != null) {
				List<CitizenType> specialists = worker.city.owner.GetKnownSpecialists(gameData);
				if (specialists.Count > 0) {
					tile.personWorkingTile = null;
					worker.tileWorked = Tile.NONE;
					worker.citizenType = specialists[0];
				}
			}
			return tile;
		}

		// The commerce the city's tiles make, before corruption takes its
		// share: Civ3's corruption plus the city's income (City_Body's
		// Corruption and CashIncome in C3X's Civ3Conquests.h), which Civ3
		// FUN_00569720 adds up over a civ's cities.
		public int TotalCommerce() {
			return TotalCommerce(EffectiveBuildings());
		}

		private int TotalCommerce(List<CityBuilding> buildings) {
			int commerce = location.CommerceYield(this, buildings).yield;
			foreach (CityResident r in WorkingResidents()) {
				commerce += r.tileWorked.CommerceYield(this, buildings).yield;
			}
			return commerce;
		}

		public CommerceBreakdown CurrentCommerceYieldRaw(bool respectCivilDisorder = true) {
			List<CityBuilding> buildings = EffectiveBuildings();
			int uncorruptedCommerce = TotalCommerce(buildings);

			// Using our value of corruption, figure out how much useful
			// commerce we have to work with. Special case anarchy, where no
			// useful commerce is available. We do this here rather than setting
			// corruption to 100% because CorruptableValue would give us one
			// useful commerce in that situation.
			//
			// The same is true in civil disorder, but we allow ignoring civil
			// disorder for the purpose of calculating whether a city would be
			// happy at a certain luxury slider value even while the city is in
			// civil disorder.
			CorruptableValue commerce = new CorruptableValue(uncorruptedCommerce, corruption, corruptionCapTenths);
			RecoverWithPolicemen(ref commerce);
			bool inDisorder = isInCivilDisorder && respectCivilDisorder;
			bool inAnarchy = owner.government.transitionType;
			if (inAnarchy || inDisorder) {
				commerce.useful = 0;
				commerce.corrupt = uncorruptedCommerce;
			}

			// TODO: Science/Luxury commerce doesn't seem to be tabulating correctly, can be negative in some cases with specialists, might be ImportCiv3 issue?
			CommerceBreakdown result = new();
			result.corrupted = commerce.corrupt;
			(result.happiness, result.beakers, result.taxes) = SplitCommerce(commerce.useful);

			// Each library, marketplace and similar building adds 50% to the
			// share of commerce it affects, and each research-doubling wonder
			// (Copernicus, Newton's) adds another 100%. The bonuses stack
			// additively on the base share.
			int researchPercent = 0, luxuryBuildings = 0, taxBuildings = 0;
			foreach (CityBuilding cb in buildings) {
				researchPercent += cb.building.increasesResearch ? 50 : 0;
				researchPercent += cb.building.doublesResearch ? 100 : 0;
				luxuryBuildings += cb.building.increasesLuxury ? 1 : 0;
				taxBuildings += cb.building.increasesTax ? 1 : 0;
			}
			result.beakers += result.beakers * researchPercent / 100;
			result.happiness += result.happiness * luxuryBuildings / 2;
			result.taxes += result.taxes * taxBuildings / 2;

			// Specialists add their own taxes, beakers and luxuries. Under
			// anarchy they go on working: "No city production, no research
			// ... Specialists still work"
			// (https://civfanatics.com/civ3/civilopedia/governments/). A city
			// in disorder produces nothing, specialists included: "Civil
			// disorder is when your city stops producing any commerce and
			// shields - it essentially stops all production in that city"
			// (https://civfanatics.com/civ3/faq/), which doesn't name
			// specialists.
			if (!inDisorder) {
				foreach (CityResident cr in WorkingResidents()) {
					result.happiness += cr.citizenType.Luxuries;
					result.beakers += cr.citizenType.Research;
					result.taxes += cr.citizenType.Taxes;
				}
				// And so do buildings that bring in gold by themselves.
				result.taxes += BuildingGold(buildings);
			}

			return result;
		}

		// The gold a turn the buildings bring the city by themselves (a
		// scenario's, e.g. Eternal War's): each one's own, and as much for
		// each coast tile a citizen works, all doubled by a building that
		// doubles it.
		public int BuildingGold(List<CityBuilding> buildings) {
			int gold = 0, perCoastTile = 0;
			bool doubled = false;
			foreach (CityBuilding cb in buildings) {
				gold += cb.building.goldPerTurn;
				perCoastTile += cb.building.goldPerWorkedCoastTile;
				doubled |= cb.building.doublesBuildingGold;
			}
			if (perCoastTile > 0) {
				gold += perCoastTile * WorkingResidents().Count(r => r.tileWorked != null && r.tileWorked != Tile.NONE
					&& r.tileWorked.baseTerrainType.IsCoast);
			}
			return doubled ? 2 * gold : gold;
		}

		// Shares out commerce by the owner's sliders, as CurrentCommerceYieldRaw
		// does (without the buildings' bonuses).
		private void AddUsefulCommerce(ref CommerceBreakdown result, int commerce) {
			if (commerce <= 0) {
				return;
			}
			(int happiness, int beakers, int taxes) = SplitCommerce(commerce);
			result.happiness += happiness;
			result.beakers += beakers;
			result.taxes += taxes;
		}

		// Shares out commerce by the owner's sliders as Civ3's
		// City_RecalcTrade does: luxuries and science are each rounded to the
		// nearest whole value, halves up (8 commerce at 70% science is 6
		// beakers, not 5). If the two come to more than there is, science
		// keeps its share and luxuries take the shortfall (5 commerce at
		// 50%/50% is 2 luxuries and 3 beakers). Taxes get the rest. Civ3
		// binary (doc/perez-findings/Civ3_Findings.md, Economy).
		internal (int luxuries, int beakers, int taxes) SplitCommerce(int commerce) {
			int luxuries = RoundedShare(commerce, owner.luxuryRate);
			int beakers = RoundedShare(commerce, owner.scienceRate);
			if (luxuries + beakers > commerce) {
				luxuries = commerce - beakers;
			}
			return (luxuries, beakers, commerce - luxuries - beakers);
		}

		// The share of `commerce` a slider at `rate` (in tenths) gets, rounding
		// halves up.
		private static int RoundedShare(int commerce, int rate) {
			return (commerce * rate + 5) / 10;
		}

		[MoonSharpHidden]
		public CommerceBreakdown CurrentCommerceYield(bool respectCivilDisorder = true) {
			CommerceBreakdown result = CurrentCommerceYieldRaw(respectCivilDisorder);
			if (this.itemBeingProduced is not Inflow) {
				return result;
			}

			// commerce lua infow
			if (this.itemBeingProduced is Inflow inflowCommerce && inflowCommerce.TryGetInflowYieldFunc(InflowYield.commerce, out var commerceYieldFunc)) {
				int extraCommerce = commerceYieldFunc.Invoke(new ScriptContext(this.owner, this));
				result.wealth += extraCommerce;
			}

			// science lua infow
			if (this.itemBeingProduced is Inflow inflowScience && inflowScience.TryGetInflowYieldFunc(InflowYield.science, out var scienceYieldFunc)) {
				int extraBeakers = scienceYieldFunc.Invoke(new ScriptContext(this.owner, this));
				result.beakers += extraBeakers;
			}

			// happiness lua infow
			if (this.itemBeingProduced is Inflow inflowHappiness && inflowHappiness.TryGetInflowYieldFunc(InflowYield.happiness, out var happinessYieldFunc)) {
				int extraHappiness = happinessYieldFunc.Invoke(new ScriptContext(this.owner, this));
				result.happiness += extraHappiness;
			}

			// corruption lua infow
			if (this.itemBeingProduced is Inflow inflowCorruption && inflowCorruption.TryGetInflowYieldFunc(InflowYield.corruption, out var corruptionYieldFunc)) {
				// The commerce saved from corruption is useful again, so it is
				// shared out by the sliders like the rest.
				int lessCorruption = Math.Min(corruptionYieldFunc.Invoke(new ScriptContext(this.owner, this)), result.corrupted);
				result.corrupted -= lessCorruption;
				AddUsefulCommerce(ref result, lessCorruption);
			}

			return result;
		}

		// Whether one of the owner's wonders provides the building to this
		// city (as the Pyramids may a granary), whether or not it was also
		// built here.
		public bool IsProvidedByWonders(Building building) {
			return GetEffectiveBuildingsCache().providedByWonders.Contains(building);
		}

		public int MaintenanceCostsRaw() {
			// Anarchy: "No maintenance costs or unit upkeep needed"
			// (https://civfanatics.com/civ3/civilopedia/governments/).
			if (owner?.government != null && !owner.government.RequiresMaintenance()) {
				return 0;
			}
			EffectiveBuildingsCache cache = GetEffectiveBuildingsCache();
			int result = 0;
			foreach (CityBuilding cb in constructed_buildings) {
				result += UpkeepOf(cb.building, cache);
			}
			// A house rule may cut the AI's upkeep (see AiHouseRules).
			int percent = AiHouseRules.BuildingMaintenancePercent(owner);
			if (percent != 100) {
				result = (result * percent + 50) / 100;
			}
			return result;
		}

		// The gold a turn one of the city's buildings costs (the share of
		// MaintenanceCostsRaw it accounts for, before any house rule), as
		// the city screen shows it.
		public int MaintenanceCostOf(CityBuilding cb) {
			if (owner?.government != null && !owner.government.RequiresMaintenance()) {
				return 0;
			}
			return UpkeepOf(cb.building, GetEffectiveBuildingsCache());
		}

		private int UpkeepOf(Building b, EffectiveBuildingsCache cache) {
			// Civ3 charges nothing for a building one of the owner's
			// wonders also provides (a granary alongside the Pyramids),
			// and Smith's Trading Company pays for commercial buildings.
			// The Conquests Civilopedia (BLDG_Trading_Company): "Pays
			// maintenance costs for all trade-based city improvements
			// (Harbors, Marketplaces, Banks, Airports, Commerical Dock and
			// Stock Exchanges)", the buildings with the Commercial trait.
			// Older lists of just Harbors, Marketplaces, Banks and Airports
			// (https://civfanatics.com/civ3/civilopedia/wonders/) predate
			// the Commercial Dock.
			if (cache.providedByWonders.Contains(b)
				|| (cache.wonders.paysTradeMaintenance && b.traits.Contains(Civilization.Trait.Commercial))) {
				return 0;
			}
			// Nor for an obsolete improvement: "Players are not supposed to
			// pay maintenance for obsolete city improvements however they
			// often do because the game does not recompute maintenance
			// after a player unlocks a technology that obsoletes a city
			// improvement" (the C3X mod's notes on the 1.22 executable,
			// https://github.com/maxpetul/C3X/blob/master/default.c3x_config.ini).
			// The intended rule, not the bug.
			if (b.IsObsoleteFor(owner)) {
				return 0;
			}
			return b.maintenanceCost;
		}

		[MoonSharpHidden]
		public int MaintenanceCosts() {
			if (owner?.government != null && !owner.government.RequiresMaintenance()) {
				return 0;
			}
			int result = 0;
			result += MaintenanceCostsRaw();
			// maintenance lua infow
			if (this.itemBeingProduced is Inflow inflowMaintenance && inflowMaintenance.TryGetInflowYieldFunc(InflowYield.maintenance, out var maintenanceYieldFunc)) {
				int lessMaintenance = maintenanceYieldFunc.Invoke(new ScriptContext(this.owner, this));
				result -= lessMaintenance;
			}
			return result;
		}

		public int FoodGrowthPerTurn() {
			return CurrentFoodYield() - FoodConsumedPerTurn();
		}

		// What barbarians entering a city did to it.
		public enum BarbarianSackOutcome {
			// The city had nothing they could take.
			Nothing,
			// They stole gold from the owner's treasury.
			Gold,
			// They killed some of its citizens.
			Citizens,
			// They destroyed the shields stored towards its production.
			Production,
		}

		public record struct BarbarianSack(BarbarianSackOutcome outcome, int amount);

		// The gold barbarians steal from the player when they choose gold:
		// the treasury divided by the number of its cities, rounded down
		// (see SackedByBarbarians).
		public static int BarbarianRaidGold(Player player) {
			if (player == null || player.gold <= 0) {
				return 0;
			}
			return player.gold / Math.Max(1, player.cities.Count);
		}

		// The chance of each thing barbarians entering a city may do, weighed
		// slightly towards stealing gold, per the project owner. Only what
		// they can do in the city is rolled for.
		internal const int BarbarianGoldWeight = 40;
		internal const int BarbarianCitizensWeight = 30;
		internal const int BarbarianProductionWeight = 30;

		// Barbarians entering the city. In Civ3 they don't take cities: "The
		// barbs plunder the city (steal gold) or sabotage production. If they
		// don't find anything to plunder, the population of the city is
		// reduced by one point" (Civinator,
		// https://forums.civfanatics.com/threads/barbarian-cities-in-civ-3.646253/).
		//
		// Per the project owner, each entry does one of three things, at
		// random among those possible: take gold (if that is any), kill a
		// citizen (if the city has two or more; one, as in the quote above)
		// or destroy the shields stored towards its production (if there are
		// any). With none possible, nothing happens.
		//
		// The gold is the owner's treasury divided by its number of cities:
		// "Dabei wird 1/Städteanzahl-tel des Schatzkammerbestandes geraubt"
		// ("1/number-of-cities of the treasury is stolen"; CivWiki,
		// Barbaren (Civ3), https://www.civ-wiki.de/), and TimBentley's
		// "gold of any amount (which may be gold/NumCities ...)"
		// (https://forums.civfanatics.com/threads/barbarian-information.267101/;
		// compendium 07). This replaces the owner's earlier eighth of the
		// treasury (owner's decision, 2026-10-20). The sources give orders of
		// priority that disagree with each other, not chances, so the
		// owner's 40/30/30 split stays.
		public BarbarianSack SackedByBarbarians() {
			int gold = BarbarianRaidGold(owner);
			List<(BarbarianSackOutcome outcome, int weight)> possible = new();
			if (gold > 0) {
				possible.Add((BarbarianSackOutcome.Gold, BarbarianGoldWeight));
			}
			if (residents.Count >= 2) {
				possible.Add((BarbarianSackOutcome.Citizens, BarbarianCitizensWeight));
			}
			if (shieldsStored > 0) {
				possible.Add((BarbarianSackOutcome.Production, BarbarianProductionWeight));
			}
			if (possible.Count == 0) {
				return new BarbarianSack(BarbarianSackOutcome.Nothing, 0);
			}

			int roll = GameData.rng.Next(possible.Sum(p => p.weight));
			BarbarianSackOutcome outcome = possible[^1].outcome;
			foreach ((BarbarianSackOutcome o, int weight) in possible) {
				if (roll < weight) {
					outcome = o;
					break;
				}
				roll -= weight;
			}

			switch (outcome) {
				case BarbarianSackOutcome.Gold:
					owner.gold -= gold;
					return new BarbarianSack(outcome, gold);
				case BarbarianSackOutcome.Citizens:
					RemoveRandomCitizen();
					return new BarbarianSack(outcome, 1);
				default:
					int shields = shieldsStored;
					shieldsStored = 0;
					return new BarbarianSack(outcome, shields);
			}
		}

		// Resisters don't eat (see StartResistance).
		public int FoodConsumedPerTurn() {
			return (residents.Count - ResistersInCity()) * 2;
		}

		// Civ3's resistance in a city taken by force, after
		// https://civfanatics.com/civ3/strategy/game-mechanics/the-inner-workings-of-resistance-revealed/:
		// "upon capturing a city, each citizen has a certain chance of
		// becoming a resistor", set by how the old owner's civ regards our
		// culture and by the two governments. Resistors "refuse to work the
		// land, meaning no food, shields, or commerce come out of them
		// (although they also don't require any food)", and the city can't
		// rush anything. No one resists for the barbarians.
		//
		// Per the project owner, every citizen who isn't of the captor's
		// nationality may resist, whichever civ they come from, and a civ's
		// last city doesn't resist when it is taken. Cities that change hands
		// peacefully (see CityInteractions.TransferCity) never resist. The
		// resisters are moved to the end of residents, keeping their order.
		public void StartResistance(Player formerOwner) {
			EndResistance();
			if (formerOwner == null || formerOwner.IsIndependent || formerOwner == owner) {
				return;
			}
			// The city was the old owner's last.
			if (formerOwner.cities.Count == 0) {
				return;
			}
			int chance = ResistancePercent(formerOwner, continuing: false);
			List<CityResident> loyal = new();
			List<CityResident> resisting = new();
			foreach (CityResident r in residents) {
				bool foreign = r.nationality != null && r.nationality != owner.civilization;
				if (foreign && GameData.rng.Next(100) < chance) {
					resisting.Add(r);
				} else {
					loyal.Add(r);
				}
			}
			residents.Clear();
			residents.AddRange(loyal);
			residents.AddRange(resisting);
			resisters = resisting.Count;
			resistanceFrom = resisters > 0 ? formerOwner : null;
		}

		// The residents who aren't resisting, and so work.
		internal IEnumerable<CityResident> WorkingResidents() {
			return residents.Take(residents.Count - ResistersInCity());
		}

		// Whether the resident is resisting.
		public bool IsResisting(CityResident resident) {
			int index = residents.IndexOf(resident);
			return index >= 0 && index >= residents.Count - ResistersInCity();
		}

		// Resisters "refuse to work": they work no tile and aren't
		// specialists either, so any resister on a tile or in a specialty
		// is taken off it.
		private void IdleResisters(GameData gameData) {
			CitizenType laborer = gameData.citizenTypes.Find(x => x.IsDefaultCitizen);
			foreach (CityResident r in residents.Skip(residents.Count - ResistersInCity())) {
				if (r.tileWorked != Tile.NONE) {
					if (r.tileWorked.personWorkingTile == r) {
						r.tileWorked.personWorkingTile = null;
					}
					r.tileWorked = Tile.NONE;
				}
				if (laborer != null) {
					r.citizenType = laborer;
				}
			}
		}

		// Citizens who stopped resisting go back to work, on the tiles the
		// city's governor would give them.
		private void PutFormerResistersToWork(GameData gameData) {
			foreach (CityResident r in WorkingResidents().Where(r => r.citizenType.IsDefaultCitizen && r.tileWorked == Tile.NONE).ToList()) {
				C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, r);
			}
		}

		private int ResistersInCity() => Math.Clamp(resisters, 0, residents.Count);

		// The working residents who aren't specialists, whose moods count.
		private IEnumerable<CityResident> Laborers() => WorkingResidents().Where(r => r.citizenType.IsDefaultCitizen);

		// The chance, in per cent, that a citizen starts (or, continuing,
		// keeps) resisting, from the article above. By how the old owner
		// regards our culture: "disdainful of" 90% ("they have three or more
		// times as much culture as you, OR if you don't have any culture at
		// all"), "dismissive of" 80% ("twice as much"), "unimpressed by" 70%
		// ("the ratio is 3:4 in favor of them"), "impressed with" 60% ("the
		// same amount"), "admirers of" 50% ("you have twice as much") and
		// "in awe of" 40% ("three times as much"). Continuing, "these
		// percentages are lowered by 10%". Then 5% less if our government is
		// higher up the list Democracy, Communism, Republic, Monarchy,
		// Despotism, Anarchy than theirs, 5% more if lower, except that a
		// Republic taking a Monarchy's city, or a Democracy a Communist one,
		// is 5% more (see ResistanceGovernmentModifiers, which also has
		// Feudalism and Fascism). Where the steps fall between the article's
		// ratios is our reading of it.
		internal int ResistancePercent(Player formerOwner, bool continuing) {
			int ours = CultureReport.TotalCulture(owner);
			int theirs = CultureReport.TotalCulture(formerOwner);
			int percent;
			if (ours <= 0 || theirs >= 3 * ours) {
				percent = 90;
			} else if (theirs >= 2 * ours) {
				percent = 80;
			} else if (3 * theirs >= 4 * ours) {
				percent = 70;
			} else if (ours >= 3 * theirs) {
				percent = 40;
			} else if (ours >= 2 * theirs) {
				percent = 50;
			} else {
				percent = 60;
			}
			if (continuing) {
				percent -= 10;
			}
			return percent + ResistanceGovernmentModifier(owner.government, formerOwner.government);
		}

		// conquests.biq's GOVT "Resistance Modifier", by our government (row)
		// against the former owner's (column), in the order Anarchy,
		// Despotism, Monarchy, Communism, Republic, Democracy, Fascism,
		// Feudalism: "the modifier to a single population point's chance of
		// continuing to resist each turn" (C3C editor help). Its core matches
		// the article's ranking above; it adds Feudalism and Fascism.
		private static readonly string[] ResistanceGovernmentOrder =
			["Anarchy", "Despotism", "Monarchy", "Communism", "Republic", "Democracy", "Fascism", "Feudalism"];
		private static readonly int[,] ResistanceGovernmentModifiers = {
			{ 0, 5, 5, 5, 5, 5, 0, 0 },
			{ -5, 0, 5, 5, 5, 5, 0, 0 },
			{ -5, -5, 0, 5, 5, 5, 0, 0 },
			{ -5, -5, -5, 0, -5, 5, 0, 0 },
			{ -5, -5, 5, 5, 0, 5, 0, 0 },
			{ -5, -5, -5, 5, -5, 0, 0, 0 },
			{ -5, -5, 0, -10, -5, -5, 0, 0 },
			{ -5, -5, 5, 5, 0, 0, 5, 0 },
		};

		private static int ResistanceGovernmentModifier(Government ours, Government theirs) {
			int row = Array.IndexOf(ResistanceGovernmentOrder, ours?.name);
			int column = Array.IndexOf(ResistanceGovernmentOrder, theirs?.name);
			return row < 0 || column < 0 ? 0 : ResistanceGovernmentModifiers[row, column];
		}

		// Each turn every resister rolls again to keep resisting, but "the
		// maximum number of resistors that can be quelled on a certain turn
		// is determined by the number of military units in the city times
		// the difficulty level's number of citizens quelled by military"
		// (the BIQ's military law), so an empty city goes on resisting. "Air
		// units, water units, artillery units, settlers, workers, and other
		// non-ground and/or non-combat units cannot quell resistors"; we
		// count land units that can attack and defend. Source as above.
		//
		// Per the project owner, resistance goes on after the old owner is
		// destroyed. It ends if the old owner has the city back, or at peace
		// with it: resisters "stay that way until you make peace with their
		// mother country or 'win them over'" (Civ3 manual); "Resisters can be
		// quelled by ending the war" (Civilopedia, GCON_Resistance).
		public void UpdateResistance(GameData gameData) {
			if (resisters <= 0) {
				return;
			}
			if (resistanceFrom == null || resistanceFrom == owner
					|| (!resistanceFrom.defeated && !PlayerRelationship.AtWar(owner, resistanceFrom))) {
				EndResistance();
				PutFormerResistersToWork(gameData);
				return;
			}
			resisters = ResistersInCity();

			int garrison = location.unitsOnTile.Count(u => u.owner == owner && u.CanAttack() && u.CanDefendOnLand());
			// The owner's difficulty: Regent's for the AI (GameData.DifficultyFor).
			int quellable = garrison * Math.Max(1, gameData.DifficultyFor(owner)?.MilitaryLaw ?? 1);
			int chance = ResistancePercent(resistanceFrom, continuing: true);
			int givingUp = 0;
			for (int i = 0; i < resisters; ++i) {
				if (GameData.rng.Next(100) >= chance) {
					++givingUp;
				}
			}
			resisters -= Math.Min(givingUp, quellable);
			PutFormerResistersToWork(gameData);
			if (resisters == 0) {
				EndResistance();
				log.Information("Resistance in {City} has ended", this);
				if (owner.IsToldNews) {
					new MsgShowTemporaryPopup($"The resistance in {name} has been quelled.", location, owner).send();
				}
			}
		}

		private void EndResistance() {
			resisters = 0;
			resistanceFrom = null;
		}


		// A starving city loses a foreign citizen first, the last of them
		// (resisters, who are last, go first). Foreign citizens never become
		// the owner's own, but they can be starved out (owner's decision,
		// 2026-10-20).
		internal void RemoveStarvingCitizen() {
			int index = residents.FindLastIndex(r => r.nationality != null && r.nationality != owner.civilization);
			RemoveCitizenAt(index >= 0 ? index : residents.Count - 1);
		}

		private void RemoveLastCitizen() {
			RemoveCitizenAt(residents.Count - 1);
		}

		public void RemoveRandomCitizen() {
			if (residents.Count <= 1)
				return; // TODO: Handle extreme case

			var idx = GameData.rng.Next(residents.Count);
			RemoveCitizenAt(idx);
		}

		// Removes the given citizen, if it lives here.
		public void RemoveCitizen(CityResident resident) {
			int index = residents.IndexOf(resident);
			if (index >= 0) {
				RemoveCitizenAt(index);
			}
		}

		private void RemoveCitizenAt(int index) {
			// A resister lost leaves one fewer resisting.
			if (resisters > 0 && index >= residents.Count - ResistersInCity()) {
				--resisters;
				if (resisters == 0) {
					EndResistance();
				}
			}
			residents[index].tileWorked.personWorkingTile = null;
			residents.RemoveAt(index);
		}

		// New citizens don't resist, so they go in ahead of the resisters
		// (see StartResistance).
		public void AddCitizen(CityResident cr) {
			residents.Insert(residents.Count - ResistersInCity(), cr);
		}

		public void RemoveCitizens(int number) {
			for (int i = 0; i < number; i++) {
				if (residents.Count > 0) {
					RemoveLastCitizen();
				} else {
					Log.Warning("Trying to remove last citizen from {City}", name);
					break;
				}
			}
		}

		public void RemoveAllCitizens() {
			while (residents.Count > 0) {
				RemoveLastCitizen();
			}
		}

		public override string ToString() {
			return $"{name} ({residents.Count})";
		}

		public int GetCulture() {
			return perPlayerCulture[owner];
		}

		public int GetCultureFor(Player player) {
			return perPlayerCulture.GetValueOrDefault(player, 0);
		}

		public int GetCulturePerTurnRaw() {
			(int improvements, int wonders) = GetCulturePerTurnBySource();
			return improvements + wonders;
		}

		// GetCulturePerTurnRaw split into what the city's ordinary buildings
		// and its wonders (great and small) make.
		public (int improvements, int wonders) GetCulturePerTurnBySource() {
			// The year can't change during the loop, so look it up once.
			int currentGameYear = CurrentGameYear();
			EffectiveBuildingsCache cache = GetEffectiveBuildingsCache();
			int improvements = 0;
			int wonders = 0;
			for (int i = 0; i < cache.buildings.Count; ++i) {
				CityBuilding cb = cache.buildings[i];
				int culture = CultureOf(cb, i >= cache.grantedStart, currentGameYear);
				if (cb.building.IsGreatWonder() || cb.building.isSmallWonder) {
					wonders += culture;
				} else {
					improvements += culture;
				}
			}
			return (improvements, wonders);
		}

		// The culture a turn one of the city's buildings makes (its share of
		// GetCulturePerTurnRaw), as the city screen shows it. A building a
		// wonder grants isn't among the city's constructed buildings.
		public int CulturePerTurnOf(CityBuilding cb) {
			return CultureOf(cb, !constructed_buildings.Contains(cb), CurrentGameYear());
		}

		private int CultureOf(CityBuilding cb, bool granted, int currentGameYear) {
			// Only the owner's own buildings make culture: a captured
			// city's wonders make none for its captor.
			if (cb.builtByPlayer != null && cb.builtByPlayer != owner) {
				return 0;
			}
			// Buildings granted by wonders never get old.
			return cb.building.culturePerTurn * (granted ? 1 : AgeMultiplier(cb, currentGameYear));
		}

		private static int CurrentGameYear() {
			GameData gameData = EngineStorage.gameData;
			return gameData?.timeOptions?.GetRawNumber(gameData.turn) ?? 0;
		}

		// A building's culture doubles once it is 1000 years old or more,
		// and only once: "doubled when the building is 1000+ years old"
		// (Civ3 binary, doc/perez-findings/Civ3_Findings.md, "Culture and
		// flipping"), as the tourism bands start at 1000 years inclusive.
		// Civ3 saves show a temple from 1830 BC making 4 culture a turn in
		// 230 AD, not 8. A save with a library from 620 AD still making 3
		// in 1620 AD reads as "more than 1000"; the project owner ruled the
		// binary authoritative (2026-11-12). (Perhaps that save's culture
		// was counted before the year advanced.)
		internal static int AgeMultiplier(CityBuilding cb, int currentGameYear) {
			return currentGameYear - cb.year >= 1000 ? 2 : 1;
		}

		// Conquests tourism: the gold a turn this city's great wonders with
		// the tourist attraction flag earn its owner once they're old enough.
		// The table is the Civilopedia's (GCON_Tourist_Attraction).
		//
		// Assumptions: the gold goes straight to the treasury, untouched by
		// corruption or the tax rate; a wonder still attracts tourists after
		// its city changes hands; and the bands start at 1000 years
		// inclusive. An obsolete wonder still attracts tourists, as it keeps
		// its culture, though its other effects stop (owner's decision,
		// 2026-10-21; see EffectiveBuildings).
		public int TourismGold() {
			int currentGameYear = CurrentGameYear();
			int result = 0;
			foreach (CityBuilding cb in constructed_buildings) {
				if (cb.building.touristAttraction && cb.building.IsGreatWonder()) {
					result += TourismGoldForAge(currentGameYear - cb.year);
				}
			}
			return result;
		}

		internal static int TourismGoldForAge(int years) {
			if (years < 1000) return 0;
			if (years <= 1500) return 2;
			if (years <= 1750) return 4;
			if (years <= 1875) return 6;
			if (years <= 2000) return 8;
			if (years <= 2250) return 10;
			if (years <= 2500) return 12;
			return 14;
		}

		[MoonSharpHidden]
		public int GetCulturePerTurn() {
			// Under a xenophobic government (Fascism), "no culture is gained
			// in cities where less than half of the population of the city is
			// from the same race as the controlling civilization" (C3C editor
			// help, Flags); the Civilopedia: conquered cities "do not
			// generate culture until a popular majority has been
			// assimilated".
			if (owner?.government?.xenophobic == true && 2 * residents.Count(r => r.nationality == null || r.nationality == owner.civilization) < residents.Count) {
				return 0;
			}
			int result = Mobilization.AdjustCulture(owner, GetCulturePerTurnRaw());
			// culture lua infow
			if (this.itemBeingProduced is Inflow inflow && inflow.TryGetInflowYieldFunc(InflowYield.culture, out var cultureYieldFunc)) {
				int extraCulture = cultureYieldFunc.Invoke(new ScriptContext(this.owner, this));
				result += extraCulture;
			}

			return result;
		}

		// The city's culture level, 1 to 6, which sets its border radius:
		// "Culture level = 1 + number of times g_RuleCultureBase fits into
		// the owner's city culture, max 6" (Civ3 binary,
		// doc/perez-findings/Civ3_Findings.md, "Culture and flipping"). We
		// read "fits" as successive powers, as the editor describes the
		// RULE section's Border Factor: "There are 6 levels of border
		// expansion and under the default Border Factor of 10, they occur at
		// 0, 10, 100, 1000, 10000, 100000" (compendium §3.10). So the base
		// is Rules.BorderFactor, 10 in conquests.biq; a value below 2 is
		// taken as 10.
		public int GetBorderExpansionLevel() {
			int culture = GetCulture();
			int factor = owner?.rules?.BorderFactor ?? EngineStorage.gameData?.rules?.BorderFactor ?? 10;
			if (factor < 2) {
				factor = 10;
			}
			int level = 1;
			for (long threshold = factor; level < MaxBorderExpansionLevel && culture >= threshold; threshold *= factor) {
				++level;
			}
			return level;
		}

		public const int MaxBorderExpansionLevel = 6;

		public void AddBuilding(Building building) {
			// A new power plant replaces any other one in the city.
			if (building.replacesOtherBuildings) {
				constructed_buildings.RemoveAll(cb => cb.building.replacesOtherBuildings);
			}
			constructed_buildings.Add(new CityBuilding {
				building = building,
				builtByPlayer = owner,
				year = CurrentGameYear(),
				totalCulture = 0
			});
			// The Apollo Program is remembered for the world, so that
			// SpaceRace.ApolloProgramBuilt needn't search the cities for it.
			if (building.buildSpaceshipParts) {
				EngineStorage.gameData?.GreatWondersBuilt.Add(building.name);
			}
			owner?.OnBuildingsChanged();
		}
		// Selling a building, from the city screen's list of improvements
		// as in Civ3 ("The top of the 3 options will be Sell",
		// https://forums.civfanatics.com/threads/can-you-sell-city-improvements.9238/).
		// "You can only sell one building in a given city per turn"; "Wonders
		// of the World also cannot be sold"; "Your palace cannot be sold";
		// "If a building exists in a city solely because of a Wonder, then
		// you can't sell the building", even a temple the city built itself
		// under the Temple of Artemis; and "the aqueduct falls into this
		// category, and I think the hospital does as well"
		// (https://www.gog.com/forum/sid_meiers_civilization_series/civilization_3_cant_sell_city_improvements).
		// Civ3 asks "Are you sure you want to sell this improvement for $NUM0
		// gold and $NUM1 shields?" (Script.txt, IMPROVEMENT_SCRAP); the
		// shields are Recycling's "Returns 25% of the shield cost when
		// selling improvements in addition to the gold received" (Conquests
		// Civilopedia, TECH_Recycling), which no BIQ flag we read gives, so
		// it isn't modelled.
		//
		// UNVERIFIED (simplest consistent behaviour): the gold. No source
		// found gives it; we pay the building's shield cost in gold. Small
		// wonders count as wonders, and the buildings that lift the city's
		// size limit (aqueducts, hospitals) as the ones that can't be sold.
		public string WhyCannotSell(Building building) {
			CityBuilding cb = constructed_buildings.Find(c => c.building == building);
			if (cb == null) {
				return "The city has no such improvement.";
			}
			if (building.isCenterOfEmpire) {
				return "The palace cannot be sold.";
			}
			if (building.IsGreatWonder() || building.isSmallWonder) {
				return "Wonders cannot be sold.";
			}
			if (IsProvidedByWonders(building)) {
				return $"The {building.name} comes from a wonder and cannot be sold.";
			}
			if (building.allowsCitySize2 || building.allowsCitySize3) {
				return $"The {building.name} cannot be sold.";
			}
			if (soldBuildingThisTurn) {
				return "The city has already sold an improvement this turn.";
			}
			return null;
		}

		public int SalePrice(Building building) {
			return Math.Max(0, owner.ShieldCost(building));
		}

		// Sells the building, returning the gold it brought, or -1 if it
		// can't be sold (see WhyCannotSell).
		public int SellBuilding(Building building) {
			string why = WhyCannotSell(building);
			if (why != null) {
				log.Warning("Not selling {Building} in {City}: {Error}", building, this, why);
				return -1;
			}
			int gold = SalePrice(building);
			RemoveBuilding(constructed_buildings.Find(c => c.building == building));
			owner.gold += gold;
			soldBuildingThisTurn = true;
			return gold;
		}

		// A city kept in disorder may riot: "Determines the percentage chance
		// of rioting occurring in any city experiencing disorder. Default is
		// 20%" (C3C editor help, Chance of Rioting), and prolonged disorder
		// leads to "Destruction of existing city improvements" (Civilopedia,
		// GCON_Disorder). Civ3 tells of it: "Civil Disorder intensifies in
		// $CITYNAME0! The mob has destroyed $IMPROVEMENT1!" (script.txt).
		// Civ3's City_UpdateDisorder (Civ3 binary,
		// doc/perez-findings/Civ3_Findings.md, "Happiness, disorder,
		// celebration" and Round 2b): never in the capital; Rand(100) <
		// the chance, then for a town (size up to MaximumLevel1CitySize)
		// a second Rand(100) < twice the chance; and it never destroys a
		// wonder, a small wonder, a palace-class building or one that lets
		// the city grow (aqueduct and sewer class).
		// UNVERIFIED: which of the rest goes is picked at random among them.
		// The binary makes up to size + 20 random picks, which may all miss.
		public void MaybeRiot(GameData gameData) {
			if (IsCapital()) {
				return;
			}
			Rules rules = owner?.rules ?? gameData.rules;
			int chance = rules?.ChanceOfRioting ?? 0;
			if (chance <= 0 || GameData.rng.Next(100) >= chance) {
				return;
			}
			if (rules != null && residents.Count <= rules.MaximumLevel1CitySize && GameData.rng.Next(100) >= 2 * chance) {
				return;
			}
			List<CityBuilding> breakable = constructed_buildings
				.Where(cb => !cb.building.IsGreatWonder() && !cb.building.isSmallWonder && !cb.building.isCenterOfEmpire
					&& !cb.building.allowsCitySize2 && !cb.building.allowsCitySize3)
				.ToList();
			if (breakable.Count == 0) {
				return;
			}
			CityBuilding destroyed = breakable[GameData.rng.Next(breakable.Count)];
			RemoveBuilding(destroyed);
			log.Information("Rioters in {City} destroyed its {Building}", this, destroyed.building.name);
			if (owner.IsToldNews) {
				new MsgShowTemporaryPopup($"Civil Disorder intensifies in {name}! The mob has destroyed {destroyed.building.name}!", location, owner).send();
			}
		}

		public void RemoveBuilding(CityBuilding building) {
			constructed_buildings.Remove(building);
			owner?.OnBuildingsChanged();
		}

		public MapUnit AddUnit(UnitPrototype proto, GameData gameData) {
			MapUnit newUnit = proto.GetInstance(gameData.GenerateID(proto.name), proto, owner, location: location);
			newUnit.experienceLevelKey = gameData.defaultExperienceLevelKey;
			newUnit.experienceLevel = gameData.defaultExperienceLevel;
			newUnit.hitPointsRemaining = newUnit.maxHitPoints;

			location.unitsOnTile.Add(newUnit);
			TileChangeJournal.Record(location);
			gameData.AddMapUnit(newUnit);
			owner.AddUnit(newUnit);

			foreach (CityBuilding b in EffectiveBuildings()) {
				b.building.onFinishedUnitProduction?.Invoke(newUnit);
			}
			// A house rule may train the AI's fighting units as veterans (see
			// AiHouseRules); one that a barracks made veteran stays veteran.
			if (newUnit.unitType.attack + newUnit.unitType.defense > 0 && !newUnit.unitType.isSettler
				&& newUnit.experienceLevel == gameData.defaultExperienceLevel && AiHouseRules.UnitsStartVeteran(owner)) {
				newUnit.Promote();
			}
			return newUnit;
		}

		// Longest name Civ3 lets a city be given.
		public const int MaxNameLength = 24;

		// Gives the city a new name, as Civ3's "Rename" does. Returns whether
		// the name was taken: it must not be empty once trimmed.
		public bool TryRename(string newName) {
			newName = newName?.Trim();
			if (string.IsNullOrEmpty(newName)) {
				return false;
			}
			if (newName.Length > MaxNameLength) {
				newName = newName.Substring(0, MaxNameLength).TrimEnd();
			}
			name = newName;
			return true;
		}

		// Why the tile can't be this city's rally point, or null if it can:
		// the units go there on foot, so it has to be land on the city's
		// continent that its owner knows.
		public string WhyCannotRallyAt(Tile tile) {
			if (!owner.isHuman) {
				return "Only human players use rally points.";
			}
			if (!Tile.IsTileValid(tile) || tile == location) {
				return "Pick a tile away from the city.";
			}
			if (!tile.IsLand() || tile.continent != location.continent) {
				return "The rally point must be on the city's continent.";
			}
			if (!owner.tileKnowledge.knownTiles.Contains(tile)) {
				return "The rally point must be a tile we have seen.";
			}
			return null;
		}

		// Sends a unit this city just built toward the rally point, if there
		// is one and a way to get there.
		internal void SendToRallyPoint(MapUnit unit) {
			if (rallyPoint == null || !owner.isHuman || rallyPoint == location || !unit.GetAvailableActions().Contains(UnitAction.Goto)) {
				return;
			}
			TilePath path = PathingAlgorithmChooser.GetAlgorithm(unit).PathFrom(location, rallyPoint, unit);
			if (path != null && path.PathLength() > 0) {
				unit.path = path;
			}
		}

		// Whether a river runs along any tile of the city's radius, its own
		// included, whoever owns the tile.
		public bool HasRiverInRadius() {
			return location.GetTilesWithinRankDistance(owner.rules.MaxRankOfWorkableTiles).Exists(t => t.BordersRiver());
		}

		// The list of tiles that could be worked by this city.
		// This isn't necessarily a subset of our borders, because we're allowed
		// to work tiles owned by our civ in our big fat cross, even if our
		// borders haven't expanded yet.
		public List<Tile> GetWorkableTiles() {
			List<Tile> result = new();
			// A Kings & Nomads city-state works only the tiles around it (see
			// CityStates).
			int rank = CityStateTrade.IsCityState(this) ? 1 : owner.rules.MaxRankOfWorkableTiles;
			foreach (Tile t in GetTilesOfRank(rank)) {
				// Skip tiles not owned by this player.
				if (t.owningCity == null || t.owningCity.owner != this.owner) {
					continue;
				}

				// Skip tiles with cities on them.
				if (t.HasCity()) {
					continue;
				}

				result.Add(t);
			}
			return result;
		}

		// The list of tiles that are within the borders of this city, without
		// taking into account border collisions with other cities.
		public List<Tile> GetTilesWithinBorders() {
			// A Kings & Nomads city-state's land is its nine tiles, no more
			// (owner's ruling, 2026-10-25).
			if (CityStateTrade.IsCityState(this)) {
				return GetTilesOfRank(Math.Min(1, GetBorderExpansionLevel()));
			}
			return GetTilesOfRank(GetBorderExpansionLevel());
		}

		// Like GetTilesWithinRankDistance, but with the filtering of ocean
		// tiles for city border calculations.
		private List<Tile> GetTilesOfRank(int rank) {
			// Filtered in place, keeping the order, rather than copied.
			List<Tile> result = location.GetTilesWithinRankDistance(rank);
			result.RemoveAll(t =>
				// Borders of a city near the map's edge run off it: skip the
				// off-map placeholder, which has no terrain or map.
				t == Tile.NONE
				// Law II
				// Ocean tiles may only hold claims of rank 2.
				|| (t.baseTerrainType.IsOcean && t.RankDistanceTo(location) > 2));
			return result;
		}

		// See https://forums.civfanatics.com/threads/everything-about-corruption-c3c-edition.76619/
		internal float CalculateDistanceCorruption(GameData gameData, int numAntiCorruptionBuildings) {
			float maxD = (location.map.numTilesWide + location.map.numTilesTall) / 4;

			float distanceToPalace = owner.citiesWithCorruptionWonders.Min(x => location.RankDistanceTo(x.location));
			if (owner.government.corruptionType == Government.CorruptionType.Communal) {
				distanceToPalace = maxD / 4;
			}

			// Cities cut off from the capital's trade network suffer more.
			bool connectedToCapital = gameData.GetTradeNetwork().ConnectedToCapital(owner, this);
			float tradeFactor = connectedToCapital ? 1.0f : 5.0f/4.0f;

			float govtFactor = owner.government.corruptionType switch {
				Government.CorruptionType.Minimal => 3.0f/4.0f,
				Government.CorruptionType.Nuisance => 1f,
				Government.CorruptionType.Problematic => 1f,
				Government.CorruptionType.Rampant => 3.0f/2.0f,
				Government.CorruptionType.Catastrophic => 1f, // anarchy, special cased
				Government.CorruptionType.Communal => 1f,
				Government.CorruptionType.Off => 0f,
				_ => throw new ArgumentOutOfRangeException(nameof(owner.government.corruptionType), owner.government.corruptionType, null)
			};


			float adjustedDistance =
					(float)Math.Pow(0.5f, numAntiCorruptionBuildings)
					* Math.Min(govtFactor * tradeFactor * distanceToPalace, maxD);
			return adjustedDistance / maxD;
		}

		// See https://forums.civfanatics.com/threads/everything-about-corruption-c3c-edition.76619/
		internal float CalculateRankCorruption(int adjustedOptimalCityNumber, int mapOptimalCityNumber, int numAntiCorruptionBuildings) {
			int rank = rankIndex;
			if (owner.government.corruptionType == Government.CorruptionType.Communal) {
				rank = owner.cities.Count / 2;
			}

			// Each courthouse or police station raises the city's optimal
			// city number by a quarter of the map's, unaffected by difficulty
			// level, government or wonders. This fits the Civ3 saves better
			// than raising the adjusted number by 25%.
			float nOpt = Math.Max(
				1,
				adjustedOptimalCityNumber + .25f * mapOptimalCityNumber * numAntiCorruptionBuildings);

			if (rank < nOpt) {
				return rank / (2 * nOpt);
			} else {
				return (2 * rank - nOpt) / (2 * nOpt);
			}
		}

		public void CalculateCorruption(GameData gameData) {
			CalculateCorruption(gameData, owner.GetAdjustedOptimalCityNumber(gameData));
		}

		// The adjusted optimal city number is empire-wide, so when updating
		// every city Player.DoCorruptionCalculations works it out once and
		// passes it in rather than rescanning the empire for each city.
		internal void CalculateCorruption(GameData gameData, int adjustedOptimalCityNumber) {
			int numAntiCorruptionBuildings = 0;

			// Civ3's Secret Police HQ is a second Forbidden Palace under
			// Communism: the BIQ gives it the same "Forbidden Palace" flag, so
			// it counts here and as a center of the empire for distance
			// corruption while its government lasts (see
			// Building.WorksAsForbiddenPalaceFor).
			int numCorruptionReducingSmallWondersInCity = 0;
			foreach (CityBuilding cb in EffectiveBuildings()) {
				if (cb.building.reducesCorruption) {
					++numAntiCorruptionBuildings;
				}
				if (cb.building.WorksAsForbiddenPalaceFor(owner)) {
					++numCorruptionReducingSmallWondersInCity;
				}
			}

			// A house rule may spare the AI some corruption and waste (see
			// AiHouseRules).
			float houseCorruptionFactor = AiHouseRules.CorruptionFactor(owner);
			corruption = (CalculateDistanceCorruption(gameData, numAntiCorruptionBuildings)
					+ CalculateRankCorruption(adjustedOptimalCityNumber, gameData.map.optimalNumberOfCities, numAntiCorruptionBuildings))
					* gameData.rules.CorruptionRate * houseCorruptionFactor;
			// Policemen are applied to the corrupt amounts themselves, see
			// RecoverWithPolicemen.

			// We Love the King Day lowers waste, not corruption: "For waste
			// calculations only, when the city is in a WLTKD celebration,
			// divide da by 2" and "add OCN/4 to Nopt"
			// (https://civfanatics.com/civ3/strategy/game-mechanics/everything-about-corruption-c3c-edition/;
			// "WLTKD does nothing for corruption, the loss of commerce",
			// https://forums.civfanatics.com/threads/we-love-the-king-days.34249/).
			// That is what one more courthouse does to each part, without
			// lowering the cap below. It is worked out whether or not the city
			// is celebrating, since that can change before the next update.
			celebrationWaste = (CalculateDistanceCorruption(gameData, numAntiCorruptionBuildings + 1)
					+ CalculateRankCorruption(adjustedOptimalCityNumber, gameData.map.optimalNumberOfCities, numAntiCorruptionBuildings + 1))
					* gameData.rules.CorruptionRate * houseCorruptionFactor;

			// Corruption maxes out at 90%, and this max can be reduced further
			// via courthouses/police stations, and the forbidden palace/SPHQ.
			// The cap applies to the amounts themselves (see CorruptableValue).
			corruptionCapTenths = Math.Max(0, 9 - numAntiCorruptionBuildings - 7 * numCorruptionReducingSmallWondersInCity);
			float maxCorruption = corruptionCapTenths / 10f;
			corruption = Math.Max(corruption, 0);
			corruption = Math.Min(corruption, maxCorruption);
			celebrationWaste = Math.Clamp(celebrationWaste, 0, maxCorruption);

			// The capital is rank 0, so the next CitiesFreeOfCorruption
			// cities nearest it are ranks 1 to CitiesFreeOfCorruption.
			Rules rules = gameData.rules;
			if (rules != null && (rules.NoCorruption || rules.CoreCitiesFreeOfCorruption && rankIndex <= rules.CitiesFreeOfCorruption)) {
				corruption = 0;
				celebrationWaste = 0;
			}
		}

		// Does the per turn culture updating for the city and returns whether
		// the borders need to be updated.
		public bool UpdateCultureAndCheckForExpansion() {
			int start = GetBorderExpansionLevel();
			// Only built buildings keep a culture total. GetBuildings also
			// lists the buildings granted by wonders, but as new objects each
			// time, so adding to their totals never had any effect.
			foreach (CityBuilding cb in constructed_buildings) {
				cb.totalCulture += cb.building.culturePerTurn;
			}

			perPlayerCulture[owner] += GetCulturePerTurn();

			return start != GetBorderExpansionLevel();
		}

		// Initializes the citizen moods, before positive and negative
		// influcences are added. A fixed number of citizens are born content,
		// based on the difficulty level (Regent's for the AI, see
		// GameData.AiRulesDifficulty), and after that all citizens are born
		// unhappy. Specialists and resisters are excluded from this:
		// "Resistors, content people, and Specialists are ignored in the
		// calculation" of disorder (Civ3 manual); resisters are shown as
		// resisting, neither happy nor unhappy (Civ3's popHeads.pcx has a
		// resisting head between the happy and unhappy ones).
		// A scenario may have fewer of the rest born unhappy (see
		// Rules.BornUnhappyPercent).
		private void InitializeMoodsForDifficulty(Difficulty gameDifficulty, Rules rules) {
			foreach (CityResident r in residents.Skip(residents.Count - ResistersInCity())) {
				r.mood = CityResident.Mood.Resisting;
			}
			List<CityResident> laborers = Laborers().ToList();
			// A house rule may make more of the AI's citizens content (see
			// AiHouseRules).
			int content = Math.Min(gameDifficulty.NumberOfCitizensBornContent + AiHouseRules.ExtraContentCitizens(owner), laborers.Count);
			int unhappyPercent = Math.Clamp(rules?.BornUnhappyPercent ?? 100, 0, 100);
			content = laborers.Count - (laborers.Count - content) * unhappyPercent / 100;

			foreach (CityResident r in laborers) {
				if (content > 0) {
					--content;
					r.mood = CityResident.Mood.Content;
				} else {
					r.mood = CityResident.Mood.Unhappy;
				}
			}
		}

		// Attemps to move the specified number of citizens that have mood `from`
		// to mood `to`, returning the actual number changed.
		private int ApplyMoodChange(int count, CityResident.Mood from, CityResident.Mood to) {
			int result = 0;

			foreach (CityResident r in Laborers()) {

				if (count <= 0) {
					break;
				}

				if (r.mood == from) {
					--count;
					++result;
					r.mood = to;
				}
			}

			return result;
		}

		// Handles the process of consuming content to happy moves, first making
		// content faces happy, then unhappy to happy at 1/2 the rate, and then
		// applying the remainder of unhappy->content, if any.
		private void ConsumeContentToHappyMoves(int contentToHappyMoves) {
			CityResident.Mood happy = CityResident.Mood.Happy;
			CityResident.Mood content = CityResident.Mood.Content;
			CityResident.Mood unhappy = CityResident.Mood.Unhappy;

			// Now move content faces to happy faces.
			contentToHappyMoves -= ApplyMoodChange(contentToHappyMoves, content, happy);

			// If there are moves left over we can try moving unhappy
			// faces to happy, but it takes two "points".
			contentToHappyMoves -= 2 * ApplyMoodChange(contentToHappyMoves / 2, unhappy, happy);

			// Account for the remainder of the integer division
			// (ApplyMoodChange ignores a negative input).
			ApplyMoodChange(contentToHappyMoves, unhappy, content);
		}

		public enum Mood {
			Unhappy,
			Happy
		};

		// A city that is big enough, isn't starving, has no unhappy citizens
		// and more happy than content ones celebrates "We Love the King Day".
		// Specialists don't count, and "The city cannot have any resisters"
		// (Civilopedia, GCON_We_Love_The_King_Day).
		public bool QualifiesForCelebration(Rules rules) {
			if (residents.Count < rules.MinimumPopulationForWeLoveTheKing || FoodGrowthPerTurn() < 0 || IsInResistance) {
				return false;
			}
			List<CityResident> laborers = Laborers().ToList();
			int happy = laborers.Count(r => r.mood == CityResident.Mood.Happy);
			int content = laborers.Count(r => r.mood == CityResident.Mood.Content);
			bool anyUnhappy = laborers.Any(r => r.mood == CityResident.Mood.Unhappy);
			return !anyUnhappy && happy > content;
		}

		// The net number of unhappy citizens made content (negative: content
		// citizens made unhappy) by the buildings in this city and by the
		// owner's great wonders:
		//  - each building's own faces in its city (the Temple's one, the
		//    Hanging Gardens' three, Shakespeare's Theater's eight, ...);
		//  - a wonder that doubles a building (the Oracle for Temples, the
		//    Sistine Chapel for Cathedrals) adds that building's faces again
		//    in every city of the owner that has it, wonder-granted ones
		//    (Temple of Artemis) included;
		//  - a wonder's faces in all cities (the Hanging Gardens' one, Cure
		//    for Cancer's one, JS Bach's two), limited to the wonder's
		//    continent when it has continental mood effects (JS Bach).
		// An obsolete building does none of this, not even in its own city.
		//
		// Assumption: the all-cities faces don't add to the wonder's own city,
		// which only gets its in-city faces. That matches the Civilopedia: the
		// Hanging Gardens (3 in city, 1 in all) make "three unhappy citizens
		// content in its city and one ... in all other friendly cities", and
		// JS Bach (2 and 2) "decreases unhappy citizens by two per city".
		// Assumption: the all-cities faces only reach the owner's cities; Cure
		// for Cancer has no flag for other civs and the Conquests Civilopedia
		// just says "in every city".
		internal int BuildingContentFaces(List<CityBuilding> buildings) {
			int result = 0;
			foreach (CityBuilding cb in buildings) {
				if (cb.building.IsObsoleteFor(owner)) {
					continue;
				}
				result -= cb.building.unhappyFacesInCity;
				result += cb.building.contentFacesInCity;
			}

			foreach ((City c, CityBuilding cb) in owner.GetBuildingSnapshot().activeWonders) {
				Building wonder = cb.building;
				if (wonder.doublesHappinessOf != null) {
					foreach (CityBuilding doubled in buildings) {
						if (doubled.building == wonder.doublesHappinessOf) {
							result += doubled.building.contentFacesInCity;
							break;
						}
					}
				}
				if (wonder.contentFacesAllCities != 0 && c != this
					&& (!wonder.continentalMoodEffects || c.location.continent == location.continent)) {
					result += wonder.contentFacesAllCities;
				}
			}
			return result;
		}

		// The number of citizens made unhappy because we're at war with the
		// civ they come from. The Conquests Civilopedia (GCON_Nationality):
		// "Having a city with foreign nationals is not a problem unless you
		// are at war with the nation that originally produced them. When at
		// war, foreign nationals become unhappy more quickly than others";
		// such citizens say
		// "Stop the aggression against our mother country!" and "will remain
		// unhappy until you peace with the civ concern"
		// (https://forums.civfanatics.com/threads/why-are-the-citizens-of-my-newly-conquered-cities-so-unhappy.79817/),
		// and the happiness thread lists "aggression against their mother
		// land" with war weariness, the whip and conscription as the
		// modifiers that make citizens unhappy
		// (https://forums.civfanatics.com/threads/how-is-happiness-calculated.74966/),
		// so it is applied the same way as those.
		//
		// UNVERIFIED: how many. The only figure found is "About 50% of those
		// citizen will be unhappy if I remember correctly"
		// (https://forums.civfanatics.com/threads/wartime-unhappiness-problems.62461/).
		// We use half of them per enemy civ, rounded up so a lone foreigner
		// still protests ("will become unhappy if you go to war with the
		// country that they come from",
		// https://forums.civfanatics.com/threads/getting-rid-of-foreigners.10085/).
		internal int MotherlandUnhappiness(GameData gameData) {
			if (owner == null || owner.IsIndependent) {
				return 0;
			}
			Dictionary<Civilization, int> foreigners = null;
			// Resisters take no part in the city's moods (see
			// InitializeMoodsForDifficulty), so they don't add to it either.
			foreach (CityResident r in WorkingResidents()) {
				if (r.nationality == null || r.nationality == owner.civilization) {
					continue;
				}
				foreigners ??= new();
				foreigners[r.nationality] = foreigners.GetValueOrDefault(r.nationality) + 1;
			}
			if (foreigners == null) {
				return 0;
			}
			int unhappy = 0;
			foreach (Player p in gameData.players) {
				if (p == owner || p.IsIndependent || p.defeated || p.civilization == null
					|| !foreigners.TryGetValue(p.civilization, out int count)
					|| !PlayerRelationship.AtWar(owner, p)) {
					continue;
				}
				unhappy += (count + 1) / 2;
			}
			return Math.Min(unhappy, residents.Count);
		}

		// This function does the heavy lifting of happiness calculations,
		// combining the various bonuses and penalties that affect citizen moods.
		//
		// Some references:
		//  - https://forums.civfanatics.com/threads/how-is-happiness-calculated.74966/
		//  - https://codehappy.net/apolyton/threads/83368-1.htm
		//
		public Mood RecalculateCitizenMoods(GameData gameData) {
			IdleResisters(gameData);
			CityResident.Mood content = CityResident.Mood.Content;
			CityResident.Mood unhappy = CityResident.Mood.Unhappy;
			InitializeMoodsForDifficulty(owner.isHuman ? gameData.gameDifficulty : gameData.AiRulesDifficulty(), gameData.rules);

			// The faces are applied in turn, as in justanick's worked example
			// of a Republic at war
			// (https://forums.civfanatics.com/threads/mechanics-of-happy-content-faces.701169/
			// #2, which the thread's starter confirmed in game): "You start at
			// 4 content and 8 unhappy citizens" (the born moods), the
			// buildings' and wonders' content faces make unhappy citizens
			// content, then "4 content faces and 6 unhappy faces turn it into
			// 2 content and 10 unhappy citizens" (war weariness makes content
			// citizens unhappy), and only then do the happy faces of luxuries
			// work, on content citizens first. So war weariness and the other
			// unhappy faces don't cancel happy faces one for one: an unhappy
			// face makes a content citizen unhappy, and it takes two happy
			// faces to make that citizen happy again (see
			// ConsumeContentToHappyMoves).
			//
			// UNVERIFIED: where martial law and entertainers fall in this
			// order. Martial law is counted with the content faces and
			// entertainers with the happy faces, as the Civilopedia
			// (GCON_Happy_Faces) lists them: "Military police content faces",
			// "Entertainers happy faces". The other unhappy faces below (the
			// whip, propaganda, the draft, "mother country" protests) are
			// applied with war weariness, like it an unhappiness reason of
			// the city's citizens (labels.txt, CITIZEN_UNHAPPY_REASONS).
			int contentFaces = 0;
			int unhappyFaces = 0;
			int happyFaces = 0;

			// Each citizen lost to pop rushing has a 20 turn penalty, so
			// multiple citizens lost causes multiple unhappy faces.
			if (turnsOfUnhappinessDueToPopRushing > 0) {
				unhappyFaces += (turnsOfUnhappinessDueToPopRushing - 1) / gameData.rules.TurnPenaltyForEachHurrySacrifice + 1;
			}

			// "If the city does not flip, the persuaded citizen will be
			// confused. Each will give 1 unhappy face."
			// (https://civfanatics.com/civ3/strategy/game-mechanics/a-study-of-espionage-missions/)
			unhappyFaces += Math.Min(propagandaConfusedCitizens, residents.Count);

			// Drafting works the same way: "it makes 1 citizen per unit
			// drafted unhappy for 20 turns" (the BIQ's turn penalty per
			// drafted citizen),
			// https://forums.civfanatics.com/threads/question-about-drafting-in-civ3.107429/.
			if (turnsOfUnhappinessDueToDrafting > 0) {
				int penalty = Math.Max(1, gameData.rules.TurnPenaltyForEachDraftedCitizen);
				unhappyFaces += (turnsOfUnhappinessDueToDrafting - 1) / penalty + 1;
			}

			// War weariness makes citizens unhappy, like pop rushing, and war
			// happiness makes them happy.
			unhappyFaces += owner.WarWearinessUnhappiness(this);
			happyFaces += owner.WarHappiness(this);

			// Citizens of a civ we're at war with protest "Stop the aggression
			// against our mother country!", like the whip and war weariness.
			unhappyFaces += MotherlandUnhappiness(gameData);

			List<CityBuilding> buildings = EffectiveBuildings();

			// Building and wonder happiness/unhappiness, which only affects the
			// unhappy to content transition, nothing with happy faces.
			contentFaces += BuildingContentFaces(buildings);

			// Depending on the government type, our land units that can
			// attack serve as military police: "military units (units with
			// attack rating greater than zero)" (C3C editor help, Military
			// Police Limit).
			int landDefenders = 0;
			foreach (MapUnit unit in location.unitsOnTile) {
				if (unit.owner == owner && unit.IsLandUnit() && unit.unitType.attack > 0) {
					++landDefenders;
				}
			}
			// Each, up to the government's limit, makes one citizen content:
			// each unit up to the limit "will make one citizen content; any
			// extra units (above the limit) will do nothing"
			// (https://forums.civfanatics.com/threads/military-police.250479/).
			// The difficulty's military law is for quelling resistance
			// instead (see City.UpdateResistance).
			contentFaces += Math.Min(owner.government.militaryPoliceLimit, landDefenders);

			// Luxury spending moves content faces to happy faces, one face for
			// every luxury (see the civfanatics thread above: "one luxury
			// happiness point affects one citizen").
			//
			// Don't respect civil disorder during this calculation, because if
			// we are currently in civil disorder our commerce is all corrupt,
			// but we still need to be able to calculate whether a certain
			// luxury slider value would get us out of civil disorder.
			happyFaces += CurrentCommerceYield(respectCivilDisorder: false).happiness;

			// As do luxury resources, which can be boosted by marketplaces.
			int effectiveLux = CountedLuxuries(GetLuxuries(gameData).Keys.Count);
			bool increasesLuxuryTrade = false;
			foreach (CityBuilding cb in buildings) {
				if (cb.building.increasesLuxuryTrade) {
					increasesLuxuryTrade = true;
					break;
				}
			}
			if (increasesLuxuryTrade) {
				effectiveLux = (int)(Math.Floor(effectiveLux / 2f) * Math.Ceiling(effectiveLux / 2f) + Math.Ceiling(effectiveLux / 2f));
			}
			happyFaces += effectiveLux;

			// Content faces (negative: a building's unhappy faces) first,
			// then the unhappy faces, which can only turn content citizens
			// unhappy, then the happy faces.
			if (contentFaces >= 0) {
				ApplyMoodChange(contentFaces, unhappy, content);
			} else {
				ApplyMoodChange(-contentFaces, content, unhappy);
			}
			ApplyMoodChange(unhappyFaces, content, unhappy);
			ConsumeContentToHappyMoves(happyFaces);

			// A Kings & Nomads city-state's citizens are all happy (see
			// CityStates).
			if (CityStateTrade.IsCityState(this)) {
				foreach (CityResident cr in Laborers()) {
					cr.mood = CityResident.Mood.Happy;
				}
			}

			// A city riots when happy - unhappy < 0; content citizens count as
			// 0. Specialists and resisters take no part (see
			// InitializeMoodsForDifficulty).
			int happyCount = 0;
			int unhappyCount = 0;
			foreach (CityResident cr in Laborers()) {
				if (cr.mood == CityResident.Mood.Happy) { ++happyCount; }
				if (cr.mood == CityResident.Mood.Unhappy) { ++unhappyCount; }
			}
			if (happyCount - unhappyCount < 0) {
				return Mood.Unhappy;
			} else {
				return Mood.Happy;
			}
		}

		// The resources this city can use: those on its own trade network
		// segment, plus, for cities connected to the capital, those imported
		// from other civs through deals, less those exported to them. Under
		// PersistentResources (MDJ), every resource its civ has had too (see
		// Player.RecordObtainedResources).
		//
		// The returned dictionary may be shared with other cities, so it must
		// not be modified.
		public Dictionary<Resource, int> GetAvailableResources(GameData gameData) {
			Dictionary<Resource, int> current = GetCurrentResources(gameData);
			if (gameData.rules?.PersistentResources != true || owner.obtainedResources.Count == 0) {
				return current;
			}
			Dictionary<Resource, int> result = null;
			foreach (Resource r in gameData.Resources) {
				if (owner.obtainedResources.Contains(r.Key) && !current.ContainsKey(r)) {
					result ??= new(current);
					result[r] = 1;
				}
			}
			return result ?? current;
		}

		// The resources the city has now, whatever its civ has had before.
		internal Dictionary<Resource, int> GetCurrentResources(GameData gameData) {
			C7Engine.Pathing.TradeNetwork network = gameData.GetTradeNetwork();
			Dictionary<Resource, int> local = network.GetResourcesAvailableToCity(owner, this);
			if (!network.ConnectedToCapital(owner, this)) {
				return local;
			}
			List<ResourceDeal> inbound = TradeReport.ResourceDeals(gameData, owner, DealDetails.Inbound);
			List<ResourceDeal> outbound = TradeReport.ResourceDeals(gameData, owner, DealDetails.Outbound);
			if (inbound.Count == 0 && outbound.Count == 0) {
				return local;
			}

			Dictionary<Resource, int> result = new(local);
			foreach (ResourceDeal deal in inbound) {
				if (owner.KnowsAboutResource(deal.resource)) {
					result[deal.resource] = result.GetValueOrDefault(deal.resource) + 1;
				}
			}
			foreach (ResourceDeal deal in outbound) {
				if (result.TryGetValue(deal.resource, out int count)) {
					if (count <= 1) {
						result.Remove(deal.resource);
					} else {
						result[deal.resource] = count - 1;
					}
				}
			}
			return result;
		}

		private Dictionary<Resource, int> ListResourceAccess(GameData gameData, ResourceCategory category) {
			Dictionary<Resource, int> availableResources = GetAvailableResources(gameData);
			Dictionary<Resource, int> result = new();
			foreach ((Resource r, int count) in availableResources) {
				if (r.Category == category) {
					result[r] = count;
				}
			}
			return result;
		}

		public Dictionary<Resource, int> GetStrategicResources(GameData gameData) {
			return ListResourceAccess(gameData, ResourceCategory.STRATEGIC);
		}

		// At most eleven luxuries make anyone happy. Flintlock, reading the
		// 1.22 code (https://forums.civfanatics.com/threads/c3x-exe-mod-including-bug-fixes-stack-bombard-and-much-more.666881/post-16652030):
		// "the game still has a soft limit of 11 luxury resources", and
		// (post-16541856) "cities are limited to eleven luxuries anyway".
		// Stock Conquests has eight luxury types, so only mods reach it.
		public const int MaxLuxuriesCounted = 11;

		public static int CountedLuxuries(int luxuries) => Math.Min(luxuries, MaxLuxuriesCounted);

		public Dictionary<Resource, int> GetLuxuries(GameData gameData) {
			return ListResourceAccess(gameData, ResourceCategory.LUXURY);
		}
	}
}

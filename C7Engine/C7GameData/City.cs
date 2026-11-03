using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using C7Engine;
using C7GameData.Save;
using MoonSharp.Interpreter;

namespace C7GameData {
	public class CityBuilding {
		public Building building;
		public Player builtByPlayer;
		public int year;
		public int turnBuilt;
		public int baseCulturePerTurn;
		public int currentCulturePerTurn;
		public int totalCulture; // This represents the total culture produced by the building.
								 // In Civ3, this value is displayed in the cultural advisor tab
	}

	public struct CommerceBreakdown {
		public int corrupted;
		public int taxes;
		public int beakers;
		public int happiness;
		public int wealth;
	}

	public struct CorruptableValue {
		public CorruptableValue(int value, float corruption) {
			// Apply the corruption amount, ensuring that there is always at
			// least one shield/commerce if we started with at least one.
			useful = (int)Math.Round(value * (1 - corruption));
			if (value > 0) {
				useful = Math.Max(1, useful);
			}

			// Whatever is left over is corrupt.
			corrupt = value - useful;
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

		public int foodStored = 0;

		public bool capital = false;
		public Player owner { get; set; }
		public List<CityResident> residents = new List<CityResident>();

		// The list of buildings built in this city. You probably want to use
		// GetBuildings, which can also include buildings granted by wonders.
		public List<CityBuilding> constructed_buildings;

		// The order of this city within all the cities of a player for the
		// purposes of rank corruption calculations.
		//
		// This is updated each turn to avoid each city needing to do an O(n)
		// scan of the list, which would cause an O(n^2) overall calcuation.
		public int rankIndex = -1;

		// The amount of corruption, between 0 and 1.
		public float corruption = 0;

		// The amount of waste (the shields' corruption), between 0 and 1: the
		// same as corruption, but less while the city celebrates.
		public float waste = 0;

		// Whether the city is celebrating We Love the King Day (see
		// UpdateCelebration).
		public bool celebrating = false;

		// The number of turns of unhappiness this city will experience due to
		// pop rushing. Larger values result in larger numbers of citizens being
		// unhappy as well, in addition to the time penalty.
		public int turnsOfUnhappinessDueToPopRushing = 0;

		// The same for drafting citizens: each one drafted adds the rules' turn
		// penalty, and makes one more citizen unhappy while it lasts.
		public int turnsOfUnhappinessDueToDrafting = 0;

		// Citizens confused by another civ's propaganda: one unhappy face
		// each, one fewer each turn. See Espionage.
		public int confusedCitizens = 0;

		// The turn citizens were last drafted here, and how many that turn.
		public int draftTurn = -1;
		public int draftedThatTurn = 0;

		public bool isInCivilDisorder = false;
		public bool hasFreshwaterAccess => location.BordersRiver() || location.NeighborsFreshWater();

		// The food a citizen eats each turn.
		public const int FoodPerCitizen = 2;

		// The governor of a human's city (see CityGovernor).
		public CityGovernor governor = new();
		// How many turns running the city has been in civil disorder.
		public int turnsInDisorder = 0;

		// The turn this city was last captured, or -1, and who from. Its new
		// owner may raze it only on that turn, as Civ3 offers right after the
		// capture. Not saved.
		public int capturedOnTurn = -1;
		// The last turn the city's airport sent a unit (see Air.Airlift).
		public int lastAirliftTurn = -1;
		public Player capturedFrom;

		// The turn this city last flipped to its owner by culture, or -1, and
		// who from (a human gaining it may rebuff the rebels and hand it back).
		// Not saved.
		public int flippedOnTurn = -1;
		public Player flippedFrom;

		// The turn the owner last sold a building here, or -1.
		public int buildingSoldOnTurn = -1;

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

		// What a city can build falls in three categories (units,
		// improvements, wonders), and in Civ3 switching the build to another
		// category loses half the shields in the box. Wealth is Civ3's
		// Capitalization improvement; small wonders and spaceship parts count
		// as wonders.
		public enum ProductionCategory {
			Unit,
			Improvement,
			Wonder,
		}

		public static ProductionCategory CategoryOf(IProducible producible) {
			return producible switch {
				UnitPrototype => ProductionCategory.Unit,
				Building b when b.IsGreatWonder() || b.isSmallWonder || b.spaceshipPart != null => ProductionCategory.Wonder,
				_ => ProductionCategory.Improvement,
			};
		}

		// This turn's switch away from the category being built, if any: what
		// it was, and the shields it cost, which switching back returns.
		public ProductionCategory? switchedFromCategory;
		public int shieldsLostToSwitching;

		// Sets what the city builds. Switching to another category keeps half
		// the shields (Rules.ProductionSwitchKeptPercent, rounded down);
		// switching back the same turn returns them, and a further switch
		// costs nothing more.
		public void SetItemBeingProduced(IProducible producible) {
			if (itemBeingProduced != null && producible != null) {
				ProductionCategory from = CategoryOf(itemBeingProduced);
				ProductionCategory to = CategoryOf(producible);
				if (switchedFromCategory == null && to != from) {
					switchedFromCategory = from;
					shieldsLostToSwitching = shieldsStored - shieldsStored * Math.Clamp(EngineStorage.gameData.rules.ProductionSwitchKeptPercent, 0, 100) / 100;
					shieldsStored -= shieldsLostToSwitching;
				} else if (switchedFromCategory == to) {
					shieldsStored += shieldsLostToSwitching;
					switchedFromCategory = null;
					shieldsLostToSwitching = 0;
				}
			}
			this.itemBeingProduced = producible;
		}

		// Of what the city can build now, the most expensive thing, in the same
		// category as `like` if there is one (so a city that lost a wonder
		// race moves its shields to another wonder without losing half).
		internal IProducible GetMostExpensiveItemToProduce(IProducible like) {
			List<IProducible> options = ListProductionOptions(EngineStorage.gameData).ToList();
			List<IProducible> sameCategory = options.Where(p => like != null && CategoryOf(p) == CategoryOf(like)).ToList();
			return (sameCategory.Count > 0 ? sameCategory : options).MaxBy(p => owner.ShieldCost(p));
		}

		public bool IsCapital() {
			return capital;
		}

		// Whether the two cities are on the same continent, i.e., a land unit
		// could get from one to the other without a boat.
		public bool IsOnSameContinentAs(City other) {
			return location.continent == other.location.continent;
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

		public List<CityBuilding> GetBuildings() {
			HashSet<Building> buildingsSeen = new();
			List<CityBuilding> result = new();
			foreach (CityBuilding cb in constructed_buildings) {
				result.Add(cb);
				buildingsSeen.Add(cb.building);
			}

			// Loop through all the wonders we control that aren't obsolete.
			foreach ((City c, CityBuilding cb) in owner.GetActiveWonders()) {
				Building b = cb.building;

				if (b.greatWonderProperties.buildingGainedInEveryCity != null
					&& !buildingsSeen.Contains(b.greatWonderProperties.buildingGainedInEveryCity)) {
					result.Add(new CityBuilding() {
						building = b.greatWonderProperties.buildingGainedInEveryCity,
						builtByPlayer = cb.builtByPlayer,
						year = cb.year,
						totalCulture = 0, // TODO: calculate this
					});
				}
				if (b.greatWonderProperties.buildingGainedInEveryCityOnContinent != null
					&& c.IsOnSameContinentAs(this)
					&& !buildingsSeen.Contains(b.greatWonderProperties.buildingGainedInEveryCityOnContinent)) {
					result.Add(new CityBuilding() {
						building = b.greatWonderProperties.buildingGainedInEveryCityOnContinent,
						builtByPlayer = cb.builtByPlayer,
						year = cb.year,
						totalCulture = 0, // TODO: calculate this
					});
				}
			}

			return result;
		}

		public IEnumerable<IProducible> ListProductionOptions(GameData gameData) {
			HashSet<Resource> accessibleResources = GetAccessibleResources(gameData);

			IEnumerable<IProducible> producibles = gameData.unitPrototypes.Cast<IProducible>()
													.Concat(gameData.Buildings.Cast<IProducible>()
														.Concat(gameData.Inflows.Cast<IProducible>()));

			return producibles.Where(p => p.CanProduce(this, accessibleResources));
		}

		public HashSet<Resource> GetAccessibleResources(GameData gameData) {
			return gameData.GetTradeNetwork().GetResourcesAvailableToCity(owner, this).Keys.ToHashSet();
		}

		// 1 for a town, 2 for a city, 3 for a metropolis, by the rules' size limits.
		public int SizeLevel() {
			Rules rules = owner.rules;
			if (residents.Count <= rules.MaximumLevel1CitySize) {
				return 1;
			} else if (residents.Count <= rules.MaximumLevel2CitySize) {
				return 2;
			} else {
				return 3;
			}
		}

		public int FoodNeededToGrow() {
			Rules rules = owner.rules;
			if (residents.Count <= rules.MaximumLevel1CitySize) {
				return rules.FoodNeededToGrowForLevel1Cities;
			} else if (residents.Count <= rules.MaximumLevel2CitySize) {
				return rules.FoodNeededToGrowForLevel2Cities;
			} else {
				return rules.FoodNeededToGrowForLevel3Cities;
			}
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

		private int ShieldCostForHurrying() {
			// If there are no shields in the box, hurrying costs double.
			if (shieldsStored == 0) {
				return owner.ShieldCost(itemBeingProduced) * 2;
			}
			return owner.ShieldCost(itemBeingProduced) - shieldsStored;
		}

		// Returns the feasibility of hurrying production
		public class HurryProductionDetails {
			public string? errorMessage;
			public string? costMessage;
			public Government.HurryProductionType hurryProductionType = Government.HurryProductionType.CannotHurry;

			public int popCost = -1;
			public int goldCost = -1;
		}
		public HurryProductionDetails GetHurryProductionDetails() {
			Rules rules = EngineStorage.gameData.rules;
			int shieldCost = ShieldCostForHurrying();

			if (isInCivilDisorder) {
				return new HurryProductionDetails() { errorMessage = "The city is in disorder and cannot hurry production." };
			}

			if (IsInResistance()) {
				return new HurryProductionDetails() { errorMessage = "We cannot hurry production while our city is in resistance." };
			}

			if (this.itemBeingProduced is Building building) {
				// TODO: expose rules and make configurable
				if (building.greatWonderProperties != null || building.isSmallWonder)
					return new HurryProductionDetails() { errorMessage = $"We cannot hurry {building.name}" };
			}

			switch (owner.government.hurryingType) {
				case Government.HurryProductionType.CannotHurry:
					return new HurryProductionDetails() { errorMessage = "We cannot hurry production with this government." };

				case Government.HurryProductionType.ForcedLabor:
					int popCost = (int)Math.Ceiling((float)shieldCost / rules.CitizenValueInShields);
					if (popCost > residents.Count / 2f) {
						return new HurryProductionDetails() { errorMessage = $"Rushing this project would cost the lives of too many citizens ({popCost})." };
					}
					return new HurryProductionDetails() {
						costMessage = $"Are you sure? Hurrying {this.itemBeingProduced.name} will take the lives of {popCost} citizen(s).",
						popCost = popCost,
						hurryProductionType = Government.HurryProductionType.ForcedLabor,
					};

				case Government.HurryProductionType.PaidLabor:
					int goldCost = shieldCost * rules.ShieldValueInGold;
					if (goldCost > owner.gold) {
						return new HurryProductionDetails() { errorMessage = $"Ummm.. sir. We would need at least {goldCost} gold to do that.." };
					}
					return new HurryProductionDetails() {
						costMessage = $"Hurrying {this.itemBeingProduced.name} will cost {goldCost} gold... Are you sure?",
						goldCost = goldCost,
						hurryProductionType = Government.HurryProductionType.PaidLabor,
					};
			}
			throw new Exception($"Unknown hurrying type: {owner.government.hurryingType}");
		}

		public void HurryProduction() {
			Rules rules = EngineStorage.gameData.rules;
			HurryProductionDetails details = GetHurryProductionDetails();

			switch (owner.government.hurryingType) {
				case Government.HurryProductionType.CannotHurry:
					throw new Exception("Unexpectedly trying to hurry production with a government that doesn't support it.");

				case Government.HurryProductionType.ForcedLabor:
					if (details.popCost <= 0) {
						throw new Exception(details.errorMessage);
					}
					RemoveCitizens(details.popCost);
					turnsOfUnhappinessDueToPopRushing += rules.TurnPenaltyForEachHurrySacrifice * details.popCost;
					shieldsStored = owner.ShieldCost(itemBeingProduced);
					break;

				case Government.HurryProductionType.PaidLabor:
					if (details.goldCost <= 0) {
						throw new Exception(details.errorMessage);
					}
					owner.gold -= details.goldCost;
					shieldsStored = owner.ShieldCost(itemBeingProduced);
					break;
			}
		}

		public void HandleCityProduction(GameData gameData) {
			IProducible producedItem = ComputeTurnProduction();
			if (producedItem == null) {
				return;
			}

			log.Debug($"Produced {producedItem} in {this}");
			if (producedItem is UnitPrototype prototype) {
				owner.stats.RecordUnitBuilt(prototype);
				AddUnit(prototype, gameData);
			} else if (producedItem is Building part && part.spaceshipPart != null) {
				Spaceship.AddPart(gameData, this, part);
			} else if (producedItem is Building building) {
				AddBuilding(building, gameData);

				// The Theory of Evolution "grants two Civilization Advances as
				// soon as completed".
				if (building.HasWonderFlag(SaveBuilding.WonderFlag.TwoFreeAdvances)) {
					owner.GrantFreeTechs(gameData, 2);
				}

				// If we completed a great wonder, mark it as completed so no
				// other civ can build it. If any other cities are building the
				// wonder then change production to the most expensive option,
				// to avoid wasting the shields.
				//
				// TODO: This should interrupt the player's turn to let them
				// make the choice. And does the game allow wonder shuffling in
				// this situation?
				if (building.greatWonderProperties != null) {
					gameData.GreatWondersBuilt.Add(building.name);
					owner.MaybeStartGoldenAgeFromWonders(gameData);
					VictoryPoints.OnGreatWonderBuilt(gameData, owner, building);

					foreach (Player p in gameData.players) {
						if (p == this.owner) {
							continue;
						}

						foreach (City c in p.cities) {
							if (c.itemBeingProduced.name == building.name) {
								c.SetItemBeingProduced(c.GetMostExpensiveItemToProduce(like: c.itemBeingProduced));
							}
						}
					}
				}
			} else if (producedItem is Inflow inflow) {
				// we don't want to "complete" the inflow production
				// unless the player has a reason to change it, this should be ongoing
				return;
			}

			// A human's city builds the same unit again (Civ3's "always build
			// previously built unit"; there's no "what next?" question yet),
			// unless its governor manages production.
			if (owner.isHuman) {
				if (!governor.manageProduction && producedItem is UnitPrototype again && ListProductionOptions(gameData).Contains(again)) {
					return;
				}
				if (governor.manageProduction) {
					SetItemBeingProduced(governor.ChooseProduction(this));
					return;
				}
			}
			SetItemBeingProduced(ChooseProducible.Choose(this, owner));
		}

		public void HandleCityGrowth(GameData gameData) {
			int foodNeededToGrow = FoodNeededToGrow();
			int foodGrowth = FoodGrowthPerTurn();
			bool hasGranary = HasGranary();

			foodStored += foodGrowth;
			// Food beyond the box is thrown away, also every turn a city can't grow.
			owner.stats.RecordGrowth(surplus: Math.Max(foodGrowth, 0), wasted: Math.Max(foodStored - foodNeededToGrow, 0));
			foodStored = Math.Min(foodStored, foodNeededToGrow);

			// Handle the city starving. Resisters are the first to starve.
			if (foodStored < 0) {
				CityResident resister = residents.FirstOrDefault(r => r.resisting);
				if (resister != null) {
					RemoveCitizen(resister);
				} else {
					RemoveLastCitizen();
				}
				foodStored = 0;
				return;
			}

			// No growth necessary.
			if (foodStored < foodNeededToGrow) {
				return;
			}

			if (CanGrowPopulationByOne(gameData)) {
				// With Longevity, "a population increase of two instead of one",
				// as far as the size limit allows.
				int newCitizens = owner.OwnsBuilding(b => b.HasWonderFlag(SaveBuilding.WonderFlag.CityGrowthPlusTwoCitizens)) ? 2 : 1;
				for (int i = 0; i < newCitizens && CanGrowPopulationByOne(gameData); ++i) {
					CityResident newResident = new() {
						nationality = owner.civilization,
						city = this,
						citizenType = gameData.citizenTypes.Find(x => x.IsDefaultCitizen),
					};
					AddCitizen(newResident);
					C7Engine.AI.CityTileAssignmentAI.AssignNewCitizenToTile(gameData, newResident);
				}

				if (hasGranary) {
					foodStored /= 2;
				} else {
					foodStored = 0;
				}
			}
		}

		private bool CanGrowPopulationByOne(GameData gD) {
			return residents.Count + 1 <= SizeLimit(gD);
		}

		// The largest this city can grow: a town can always reach the rules'
		// town size; it needs fresh water or an aqueduct to become a city, and
		// a hospital to become a metropolis. Obsolete buildings don't count.
		public int SizeLimit(GameData gD) {
			if (!CanGrowIntoCity()) {
				return gD.rules.MaximumLevel1CitySize;
			}

			// If we're a city trying to grow into a metropolis, we need a
			// hospital.
			if (GetBuildings().Any(cb => cb.building.allowsCitySize3 && !cb.building.IsObsolete(owner))) {
				return int.MaxValue;
			}

			// With fresh water or an aqueduct we can grow to a city.
			return gD.rules.MaximumLevel2CitySize;
		}

		// Whether the city may pass town size: with fresh water or an aqueduct.
		public bool CanGrowIntoCity() {
			// TODO: lakes are bodies of water under 20 tiles (https://civilization.fandom.com/wiki/Fresh_Water_Lake_(Civ3))
			return hasFreshwaterAccess
				|| GetBuildings().Any(cb => (cb.building.allowsCitySize2 || cb.building.allowsCitySize3) && !cb.building.IsObsolete(owner));
		}

		public bool HasGranary() {
			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			foreach (CityBuilding cb in GetBuildings()) {
				if (cb.building.doublesCityGrowthRate) {
					return true;
				}
			}
			return false;
		}

		public bool HasWalls() {
			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			foreach (CityBuilding cb in GetBuildings()) {
				if (cb.building.providesWalls) {
					return true;
				}
			}
			return false;
		}

		public IEnumerable<StrengthBonus> GetDefenseBonuses() {
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

			// Buildings, such as walls, can also give bonuses.
			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			foreach (CityBuilding cb in GetBuildings()) {
				if (cb.building.combatDefenseBonus is not StrengthBonus defenseBonus) {
					continue;
				}

				// If this building doesn't have the "only useful in towns" flag
				// we can always provide the bonus regardless of the city size.
				//
				// But if the building is only useful in towns we need to check
				// the city size before providing the bonus.
				if (!cb.building.onlyUsefulInTowns || isTown) {
					yield return defenseBonus;
				}
			}
		}

		/**
		 * Computes turn production.  If the production queue finishes,
		 * returns the item that is built.  Otherwise, returns null.
		 */
		// Whether this city has a building that lets it build armies (the
		// Military Academy).
		public bool HasBuildingAllowingArmies() {
			return constructed_buildings.Any(cb => cb.building.HasWonderFlag(SaveBuilding.WonderFlag.AllowsBuildArmy) && !cb.building.IsObsolete(owner));
		}

		public IProducible ComputeTurnProduction() {
			// A switch made this turn can't be undone any more.
			switchedFromCategory = null;
			shieldsLostToSwitching = 0;

			CorruptableValue production = CurrentProductionYield();
			int cost = owner.ShieldCost(itemBeingProduced);
			shieldsStored += production.useful;
			// Shields beyond the cost are thrown away, whether the build finishes or not.
			int excess = Math.Max(shieldsStored - cost, 0);
			// An army waits, keeping its shields, while we lack the cities to
			// support another (upstream proposal #1011; Civ3 doesn't say).
			bool armyWaits = itemBeingProduced is UnitPrototype { isArmy: true } && !owner.CanSupportAnotherArmy();
			if (shieldsStored >= cost && residents.Count > itemBeingProduced.populationCost && !armyWaits) {
				shieldsStored = 0;
				// Inflows (e.g. Wealth) turn all their shields into something else every turn.
				RecordProductionStats(production, overflowed: itemBeingProduced is Inflow ? 0 : excess, capped: 0);
				RemoveCitizens(itemBeingProduced.populationCost);
				return itemBeingProduced;
			}

			shieldsStored = Math.Min(shieldsStored, cost);
			RecordProductionStats(production, overflowed: 0, capped: excess);
			return null;
		}

		private void RecordProductionStats(CorruptableValue production, int overflowed, int capped) {
			// What the city would have had to work with without civil disorder or
			// anarchy, which zero out its useful production.
			int produced = production.useful + production.corrupt;
			CorruptableValue normal = new(produced, corruption);
			int lost = normal.useful - production.useful;
			bool anarchy = owner.government.transitionType;
			owner.stats.RecordProduction(produced, normal.corrupt,
				lostToDisorder: anarchy ? 0 : lost, lostToAnarchy: anarchy ? lost : 0,
				overflowed, capped);
		}

		public int CurrentFoodYield() {
			int yield = location.FoodYield(this).yield;
			foreach (CityResident r in residents) {
				yield += r.tileWorked.FoodYield(this).yield;
			}
			return yield;
		}

		public CorruptableValue CurrentProductionYield() {
			int yield = location.ProductionYield(this).yield;
			int tilesMakingShields = yield > 0 ? 1 : 0;
			foreach (CityResident r in residents) {
				int tileYield = r.tileWorked.ProductionYield(this).yield;
				yield += tileYield;
				if (tileYield > 0) {
					tilesMakingShields++;
				}
			}
			// Mobilized, a city building a military unit makes a shield more on
			// every tile that makes one (Civilopedia "Mobilization").
			if (owner.mobilized && itemBeingProduced is UnitPrototype unit && unit.IsMilitary()) {
				yield += tilesMakingShields;
			}
			CorruptableValue result = new(yield, waste);

			// Using our value of waste, figure out how much useful
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

			// Factories and the like multiply what's left after waste.
			result.useful = (int)Math.Floor(result.useful * (1 + ProductionBonus()));

			// TODO: add specialist shields here. Do specialists still work in
			// civil disorder?

			return result;
		}

		// The fraction of extra shields the city's buildings give (Civilopedia:
		// a Factory +50%, a power plant +50% more, a Manufacturing Plant +50%,
		// the Iron Works +100%), added up. A building that needs another one in
		// the city (a power plant, a Factory) does nothing without it, as one a
		// wonder grants may (the Hoover Dam's Hydro Plant); and the power
		// plants replace each other, so only the best one counts.
		public double ProductionBonus() {
			List<Building> present = GetBuildings().Select(cb => cb.building).ToList();
			List<Building> working = present
				.Where(b => b.productionBonus > 0 && !b.IsObsolete(owner))
				.Where(b => b.requiredBuilding == null || b.numberOfRequiredBuildings > 0 || present.Contains(b.requiredBuilding))
				.ToList();
			double bestPlant = working.Where(b => b.replacesOtherBuildings).Select(b => b.productionBonus).DefaultIfEmpty(0).Max();
			return working.Where(b => !b.replacesOtherBuildings).Sum(b => b.productionBonus) + bestPlant;
		}

		public CommerceBreakdown CurrentCommerceYieldRaw(bool respectCivilDisorder = true) {
			int uncorruptedCommerce = location.CommerceYield(this).yield;
			foreach (CityResident r in residents) {
				uncorruptedCommerce += r.tileWorked.CommerceYield(this).yield;
			}

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
			CorruptableValue commerce = new CorruptableValue(uncorruptedCommerce, corruption);
			if (owner.government.transitionType || (isInCivilDisorder && respectCivilDisorder)) {
				commerce.useful = 0;
				commerce.corrupt = uncorruptedCommerce;
			}

			// We use constructed_buildings rather than GetBuildings because the
			// bonuses only come from buildings in this city, including wonders
			// built here, and it avoids the cost of collecting every wonder in
			// the empire each time.
			List<Building> activeBuildings = constructed_buildings
				.Select(cb => cb.building)
				.Where(b => !b.IsObsolete(owner))
				.ToList();
			double researchMultiplier = 1 + activeBuildings.Sum(b => b.researchBonus)
				+ (owner.InAgeOfScience() ? EngineStorage.gameData.rules.AgeOfScienceResearchBonus / 100.0 : 0);
			double luxuryMultiplier = 1 + activeBuildings.Sum(b => b.luxuryBonus);
			double taxMultiplier = 1 + activeBuildings.Sum(b => b.commerceBonus);

			// Split the commerce first, then apply the building bonuses to each part.
			int baseBeakers = (int)Math.Floor(commerce.useful * owner.scienceRate / 10.0);
			int baseHappiness = (int)Math.Floor(commerce.useful * owner.luxuryRate / 10.0);
			int baseTaxes = commerce.useful - baseBeakers - baseHappiness;

			// TODO: Science/Luxury commerce doesn't seem to be tabulating correctly, can be negative in some cases with specialists, might be ImportCiv3 issue?
			CommerceBreakdown result = new();
			result.corrupted = commerce.corrupt;
			result.beakers = (int)Math.Floor(baseBeakers * researchMultiplier);
			result.happiness = (int)Math.Floor(baseHappiness * luxuryMultiplier);
			result.taxes = (int)Math.Floor(baseTaxes * taxMultiplier);

			foreach (CityResident cr in residents) {
				result.beakers += cr.citizenType.Research;
				result.happiness += cr.citizenType.Luxuries;
				result.taxes += cr.citizenType.Taxes;
			}

			return result;
		}

		[MoonSharpHidden]
		public CommerceBreakdown CurrentCommerceYield(bool respectCivilDisorder = true) {
			CommerceBreakdown result = CurrentCommerceYieldRaw(respectCivilDisorder);

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
				int lessCorruption = corruptionYieldFunc.Invoke(new ScriptContext(this.owner, this));
				result.corrupted -= lessCorruption;
			}

			return result;
		}

		public int MaintenanceCostsRaw() {
			int result = 0;
			// Smith's Trading Company pays for the trade buildings (Civilopedia:
			// Harbors, Marketplaces, Banks, Airports, Commercial Docks and Stock
			// Exchanges, which are the buildings with the commercial trait).
			bool tradePaidFor = owner.OwnsBuilding(b => b.HasWonderFlag(SaveBuilding.WonderFlag.PaysTradeMaintenance));
			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			foreach (CityBuilding cb in constructed_buildings) {
				if (tradePaidFor && cb.building.traits.Contains(Civilization.Trait.Commercial)) {
					continue;
				}
				result += cb.building.maintenanceCost;
			}
			return result;
		}

		[MoonSharpHidden]
		public int MaintenanceCosts() {
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

		public int FoodConsumedPerTurn() {
			// Resisters eat nothing.
			return residents.Count(r => !r.resisting) * FoodPerCitizen;
		}

		// Whether some of this city's citizens refuse to work for its owner.
		public bool IsInResistance() {
			return residents.Any(r => r.resisting);
		}


		private void RemoveLastCitizen() {
			RemoveCitizenAt(residents.Count - 1);
		}

		public void RemoveRandomCitizen() {
			if (residents.Count == 1)
				return; // TODO: Handle extreme case

			var idx = GameData.rng.Next(residents.Count);
			RemoveCitizenAt(idx);
		}

		private void RemoveCitizenAt(int index) {
			residents[index].tileWorked.personWorkingTile = null;
			residents.RemoveAt(index);
		}

		public void RemoveCitizen(CityResident resident) {
			RemoveCitizenAt(residents.IndexOf(resident));
		}

		public void AddCitizen(CityResident cr) {
			residents.Add(cr);
		}

		public void RemoveCitizens(int number) {
			for (int i = 0; i < number; i++) {
				if (residents.Count > 0) {
					RemoveLastCitizen();
				} else {
					Log.Warning("Trying to remove last citizen from " + name);
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
			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			return GetBuildings().Sum(CulturePerTurnFrom);
		}

		// The culture one of this city's buildings makes a turn (its age bonus
		// included, see UpdateAgeBonus).
		public int CulturePerTurnFrom(CityBuilding cb) {
			return IsCapturedWonder(cb) ? 0 : cb.currentCulturePerTurn;
		}

		// A great wonder that another civ built and we captured keeps its
		// effects but makes no culture for us, as in Civ3.
		private bool IsCapturedWonder(CityBuilding cb) {
			return cb.building.IsGreatWonder() && cb.builtByPlayer != null && cb.builtByPlayer != owner;
		}

		// The gold this city's great wonders earn as tourist attractions this
		// turn: by age, from the rules' table (Civilopedia "Tourist
		// Attraction"). Obsolete ones too: fame is what draws the tourists.
		public int TourismGold() {
			GameData gameData = EngineStorage.gameData;
			if (gameData?.timeOptions == null) {
				return 0;
			}
			int year = gameData.timeOptions.GetAbsoluteYear(gameData.turn);
			int gold = 0;
			foreach (CityBuilding cb in constructed_buildings.Where(cb => cb.building.HasWonderFlag(SaveBuilding.WonderFlag.TouristAttraction))) {
				int age = year - cb.year;
				gold += gameData.rules.TouristAttractionGold.Where(step => age >= step.years).Select(step => step.gold).DefaultIfEmpty(0).Max();
			}
			return gold;
		}

		// Returns the age bonus for a building. Uses the Lua ageBonus function if available; otherwise returns 0 (no bonus).
		public int GetAgeBonus(CityBuilding cb) {
			Building building = cb.building;
			if (building.culturePerTurn <= 0 || building.noAgeBonus) {
				return 0;
			}

			GameData gameData = EngineStorage.gameData;
			if (gameData?.timeOptions == null) {
				return 0;
			}

			// Use the Lua ageBonus function if available
			Func<CityBuilding, GameData, City, Player, int> ageBonus = gameData?.ageBonusFunc;
			if (ageBonus != null) {
				return ageBonus(cb, gameData, this, owner);
			}

			// Default: no age bonus applied
			return 0;
		}

		// Uses the Lua ageBonus function if available; otherwise keeps base culture per turn unchanged.
		public void UpdateAgeBonuses(GameData gameData) {
			foreach (CityBuilding cb in constructed_buildings) {
				UpdateAgeBonus(gameData, cb);
			}
		}

		[MoonSharpHidden]
		public int GetCulturePerTurn() {
			int result = GetCulturePerTurnRaw();
			// culture lua infow
			if (this.itemBeingProduced is Inflow inflow && inflow.TryGetInflowYieldFunc(InflowYield.culture, out var cultureYieldFunc)) {
				int extraCulture = cultureYieldFunc.Invoke(new ScriptContext(this.owner, this));
				result += extraCulture;
			}

			return result;
		}

		public int GetBorderExpansionLevel() {
			// Give ourselves a minimum of 1 culture to avoid taking the log of 0
			int culture = Math.Max(1, GetCulture());

			// Take the log10 of culture, rounding down (so a culture of 123
			// would be 2, a culture of 5 would be 0, etc) and then add one to
			// get the expansion level. With 0-9 culture our culture goal is 10^1
			// and we have one tile of borders, with 10-99 our culture goal is
			// 10^2 and we have two tiles of borders.
			return (int)Math.Floor(Math.Log10(culture)) + 1;
		}

		public void AddBuilding(Building building, GameData gameData) {
			CityBuilding cb = new CityBuilding {
				building = building,
				builtByPlayer = owner,
				// A game without a calendar (bare test data) records year 0.
				year = gameData.timeOptions?.GetAbsoluteYear(gameData.turn) ?? 0,
				turnBuilt = gameData.turn,
				baseCulturePerTurn = building.culturePerTurn,
				currentCulturePerTurn = building.culturePerTurn,
				totalCulture = 0
			};
			UpdateAgeBonus(gameData, cb);
			constructed_buildings.Add(cb);
			// It may be a Harbor or an Airport.
			gameData.InvalidateCachedTradeNetwork();
		}

		// Uses the Lua ageBonus function if available; otherwise keeps base culture per turn unchanged.
		private void UpdateAgeBonus(GameData gameData, CityBuilding cb) {
			if (cb.building == null || cb.building.culturePerTurn <= 0 || cb.building.noAgeBonus) {
				cb.currentCulturePerTurn = cb.baseCulturePerTurn;
				return;
			}

			Func<CityBuilding, GameData, City, Player, int> ageBonus = gameData?.ageBonusFunc;
			if (ageBonus != null) {
				cb.currentCulturePerTurn = ageBonus(cb, gameData, this, owner);
				return;
			}

			// Default: keep the base culture per turn (no age bonus applied)
			cb.currentCulturePerTurn = cb.baseCulturePerTurn;
		}

		public void RemoveBuilding(CityBuilding building) {
			constructed_buildings.Remove(building);
			EngineStorage.gameData?.InvalidateCachedTradeNetwork();
		}

		// The best unit this city can draft its citizens into: of the
		// draftable units it could build (with its resources, and not made
		// obsolete by a better one), the strongest defender.
		public UnitPrototype DraftableUnit(GameData gameData) {
			HashSet<Resource> resources = GetAccessibleResources(gameData);
			return gameData.unitPrototypes
				.Where(u => u.draftable && u.CanProduce(this, resources))
				.OrderByDescending(u => u.defense)
				.ThenByDescending(u => u.attack)
				.FirstOrDefault();
		}

		// Why this city can't draft a citizen now, or null if it can. As in
		// Civ3 (Civilopedia "Draft"): it needs the tech that enables the draft
		// (Nationalism), a government with a draft limit (none in Anarchy), at
		// least the rules' city size (7 in Civ3: no towns), and it may draft
		// no more than that limit each turn.
		public string WhyCannotDraft(GameData gameData) {
			if (!owner.HasTechWithFlag(SaveTech.Flag.EnablesConscription)) {
				return "We don't know how to draft citizens yet.";
			}
			int limit = owner.government?.draftLimit ?? 0;
			if (limit <= 0) {
				return "We can't draft citizens under this government.";
			}
			if (residents.Count <= gameData.rules.MaximumLevel1CitySize) {
				return $"Only cities larger than {gameData.rules.MaximumLevel1CitySize} can draft citizens.";
			}
			if (draftTurn == gameData.turn && draftedThatTurn >= limit) {
				return $"{name} can't draft any more citizens this turn.";
			}
			if (DraftableUnit(gameData) == null) {
				return $"{name} has no unit to draft citizens into.";
			}
			return null;
		}

		// Drafts a citizen into a conscript of the best draftable unit, adding
		// the rules' turns of unhappiness. Returns the unit, or null if the city
		// can't draft now.
		public MapUnit Draft(GameData gameData) {
			if (WhyCannotDraft(gameData) != null) {
				return null;
			}
			UnitPrototype unitType = DraftableUnit(gameData);
			// One of the owner's own laborers goes, if there is one; never a resister.
			CityResident drafted = residents.LastOrDefault(r => !r.resisting && r.nationality == owner.civilization)
				?? residents.Last(r => !r.resisting);
			RemoveCitizen(drafted);
			if (draftTurn != gameData.turn) {
				draftTurn = gameData.turn;
				draftedThatTurn = 0;
			}
			++draftedThatTurn;
			turnsOfUnhappinessDueToDrafting += gameData.rules.TurnPenaltyForEachDraftedCitizen;

			MapUnit unit = gameData.SpawnUnit(owner, unitType, location);
			ExperienceLevel conscript = gameData.experienceLevels.OrderBy(e => e.baseHitPoints).First();
			unit.experienceLevel = conscript;
			unit.experienceLevelKey = conscript.key;
			unit.hitPointsRemaining = conscript.baseHitPoints + unitType.hpBonus;
			log.Information($"{name} drafts a citizen into a conscript {unitType.name}");
			return unit;
		}

		// Wonders and the palace can't be sold, nor buildings that let a city
		// grow past a size limit (aqueducts, hospitals): Civ3 doesn't allow it
		// (the C3X mod adds an option to).
		public static bool CanBeSold(Building building) {
			return !building.IsGreatWonder() && !building.isSmallWonder && !building.isCenterOfEmpire
				&& !building.allowsCitySize2 && !building.allowsCitySize3;
		}

		// Why the owner can't sell this building now, or null if they can: as
		// in Civ3, only one building a turn per city.
		public string WhyBuildingCannotBeSold(Building building) {
			if (!constructed_buildings.Any(cb => cb.building == building) || !CanBeSold(building)) {
				return $"We can't sell the {building.name}.";
			}
			if (buildingSoldOnTurn == EngineStorage.gameData.turn) {
				return $"We have already sold a building in {name} this turn.";
			}
			return null;
		}

		// The owner sells a building by choice. Returns the gold received, or
		// 0 if it can't be sold now.
		public int SellBuildingByChoice(Building building) {
			if (WhyBuildingCannotBeSold(building) != null) {
				return 0;
			}
			buildingSoldOnTurn = EngineStorage.gameData.turn;
			return SellBuilding(constructed_buildings.First(cb => cb.building == building));
		}

		// The gold the owner gets for selling a building. The formula is the Lua
		// behavior gameplay.buildings.sale_value, so game modes can change it.
		public int SaleValue(Building building) {
			Func<City, Building, int, int> saleValue = EngineStorage.gameData.luaBehaviorEngine.ImportFunc<Func<City, Building, int, int>>("gameplay.buildings.sale_value");
			return Math.Max(0, saleValue.Invoke(this, building, building.ShieldCost(owner, 1.0f)));
		}

		// Sells a building: it's removed and the owner gets its sale value.
		// Returns the gold received.
		public int SellBuilding(CityBuilding cityBuilding) {
			int gold = SaleValue(cityBuilding.building);
			RemoveBuilding(cityBuilding);
			owner.gold += gold;
			return gold;
		}

		public void AddUnit(UnitPrototype proto, GameData gameData) {
			MapUnit newUnit = proto.GetInstance(gameData.GenerateID(proto.name), proto, owner, location: location);
			newUnit.experienceLevelKey = gameData.defaultExperienceLevelKey;
			newUnit.experienceLevel = gameData.defaultExperienceLevel;

			location.unitsOnTile.Add(newUnit);
			gameData.mapUnits.Add(newUnit);
			owner.AddUnit(newUnit);

			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			GetBuildings().ForEach(b => b.building.onFinishedUnitProduction?.Invoke(newUnit));
		}

		// The list of tiles that could be worked by this city.
		// This isn't necessarily a subset of our borders, because we're allowed
		// to work tiles owned by our civ in our big fat cross, even if our
		// borders haven't expanded yet.
		public List<Tile> GetWorkableTiles() {
			List<Tile> result = new();
			foreach (Tile t in GetTilesOfRank(owner.rules.MaxRankOfWorkableTiles)) {
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
			return GetTilesOfRank(GetBorderExpansionLevel());
		}

		// Like GetTilesWithinRankDistance, but with the filtering of ocean
		// tiles for city border calculations.
		private List<Tile> GetTilesOfRank(int rank) {
			List<Tile> result = new();
			foreach (Tile t in location.GetTilesWithinRankDistance(rank)) {
				// Law II
				// Ocean tiles may only hold claims of rank 2.
				if (t.baseTerrainType.Key == "ocean" && t.RankDistanceTo(location) > 2) {
					continue;
				}
				result.Add(t);
			}
			return result;
		}

		// See https://forums.civfanatics.com/threads/everything-about-corruption-c3c-edition.76619/
		// For the waste of a city celebrating We Love the King Day, the adjusted
		// distance is halved.
		private float CalculateDistanceCorruption(GameData gameData, int numAntiCorruptionBuildings, bool celebrationWaste = false) {
			float maxD = (location.map.numTilesWide + location.map.numTilesTall) / 4;

			float distanceToPalace = owner.citiesWithCorruptionWonders.Min(x => location.RankDistanceTo(x.location));
			if (owner.government.corruptionType == Government.CorruptionType.Communal) {
				distanceToPalace = maxD / 4;
			}

			// A city on the capital's trade network (by road; harbors and
			// airports aren't in the network yet) counts as nearer.
			bool connectedTocapital = gameData.GetTradeNetwork().ConnectedToCapital(owner, this);
			float tradeFactor = connectedTocapital ? 1.0f : 5.0f/4.0f;

			float govtFactor = owner.government.corruptionType switch {
				Government.CorruptionType.Minimal => 3.0f/4.0f,
				Government.CorruptionType.Nuisance => 1f,
				Government.CorruptionType.Problematic => 1f,
				Government.CorruptionType.Rampant => 3.0f/2.0f,
				Government.CorruptionType.Catastrophic => 1f, // anarchy, special cased
				Government.CorruptionType.Communal => 1f,
				Government.CorruptionType.Off => 0f
			};


			float adjustedDistance =
					(float)Math.Pow(0.5f, numAntiCorruptionBuildings)
					* Math.Min(govtFactor * tradeFactor * distanceToPalace, maxD);
			if (celebrationWaste) {
				adjustedDistance /= 2;
			}
			return adjustedDistance / maxD;
		}

		// See https://forums.civfanatics.com/threads/everything-about-corruption-c3c-edition.76619/
		// For the waste of a city celebrating We Love the King Day, the optimal
		// city number grows by a quarter of the map's.
		private float CalculateRankCorruption(GameData gameData, int numAntiCorruptionBuildings, bool celebrationWaste = false) {
			int rank = rankIndex;
			if (owner.government.corruptionType == Government.CorruptionType.Communal) {
				rank = owner.cities.Count / 2;
			}

			// Each courthouse or police station adds a quarter of the map's
			// optimal city number (Nopt = OCN * (... + 0.25 * Ni) in the article),
			// and so does a celebration, for waste.
			int mapOptimalCityNumber = gameData.map.optimalNumberOfCities;
			float nOpt = Math.Max(
				1,
				owner.GetAdjustedOptimalCityNumber(gameData) + .25f * numAntiCorruptionBuildings * mapOptimalCityNumber
					+ (celebrationWaste ? .25f * mapOptimalCityNumber : 0));

			if (rank < nOpt) {
				return rank / (2 * nOpt);
			} else {
				return (2 * rank - nOpt) / (2 * nOpt);
			}
		}

		public void CalculateCorruption(GameData gameData) {
			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			List<CityBuilding> buildings = GetBuildings();
			int numAntiCorruptionBuildings = buildings.Count(x => x.building.reducesCorruption);

			// TODO: Handle the SPHQ.
			int numCorruptionReducingSmallWondersInCity = buildings.Count(x => x.building.HasWonderFlag(SaveBuilding.WonderFlag.ReducesCorruption));

			corruption = CalculateDistanceCorruption(gameData, numAntiCorruptionBuildings)
					+ CalculateRankCorruption(gameData, numAntiCorruptionBuildings);
			// TODO: apply policeman modifiers, before applying the max

			// Corruption maxes out at 90%, and this max can be reduced further
			// via courthouses/police stations, and the forbidden palace/SPHQ.
			float maxCorruption = Math.Max(
				0,
				.9f - (.1f * numAntiCorruptionBuildings + .7f * numCorruptionReducingSmallWondersInCity));
			corruption = Math.Max(corruption, 0);
			corruption = Math.Min(corruption, maxCorruption);

			// Waste is the same, but less while the city celebrates We Love the
			// King Day (Civilopedia "We Love the King Day"; by how much: the
			// article above).
			waste = corruption;
			if (celebrating) {
				waste = CalculateDistanceCorruption(gameData, numAntiCorruptionBuildings, celebrationWaste: true)
					+ CalculateRankCorruption(gameData, numAntiCorruptionBuildings, celebrationWaste: true);
				waste = Math.Clamp(waste, 0, maxCorruption);
			}
		}

		// Does the per turn culture updating for the city and returns whether
		// the borders need to be updated.
		public bool UpdateCultureAndCheckForExpansion() {
			int start = GetBorderExpansionLevel();
			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			foreach (CityBuilding cb in GetBuildings()) {
				cb.totalCulture += CulturePerTurnFrom(cb);
			}

			perPlayerCulture[owner] += GetCulturePerTurn();

			return start != GetBorderExpansionLevel();
		}

		// Initializes the citizen moods, before positive and negative
		// influcences are added. A fixed number of citizens are born content,
		// based on the difficulty level, and after that all citizens are born
		// unhappy. Specialists and resisters are excluded from this.
		private void InitializeMoodsForDifficulty(Difficulty gameDifficulty) {
			int numLaborers = residents.Count(HasMood);
			int content = Math.Min(gameDifficulty.NumberOfCitizensBornContent, numLaborers);

			foreach (CityResident r in residents) {
				if (!HasMood(r)) {
					continue;
				}

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
		// Whether a citizen's mood counts: laborers do; specialists and
		// resisters don't.
		private static bool HasMood(CityResident r) {
			return r.citizenType.IsDefaultCitizen && !r.resisting;
		}

		private int ApplyMoodChange(int count, CityResident.Mood from, CityResident.Mood to) {
			int result = 0;

			foreach (CityResident r in residents) {
				if (!HasMood(r)) {
					continue;
				}

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

		// The citizens of this city whose civ is at war with its owner. In Civ3
		// (Civilopedia "Nationality") they "become unhappy more quickly": the
		// user's Civ3 saves show one unhappy face for each, specialists
		// included. Resisters don't count; they have no mood at all.
		public int CitizensOfEnemyNationality(GameData gameData) {
			int count = 0;
			foreach (IGrouping<Civilization, CityResident> nationals in residents
					.Where(r => !r.resisting && r.nationality != null && r.nationality != owner.civilization)
					.GroupBy(r => r.nationality)) {
				Player home = gameData.PlayerOfNationality(nationals.Key);
				if (home != null && PlayerRelationship.AtWar(home, owner)) {
					count += nationals.Count();
				}
			}
			return count;
		}

		// Whether this city, with its citizens' current moods, qualifies for
		// We Love the King Day (Civilopedia "We Love the King Day"): it isn't
		// starving, has no resisters and no unhappy citizens, more happy than
		// content citizens, and is at least the rules' size (6 in Conquests).
		public bool QualifiesForCelebration(GameData gameData) {
			if (residents.Count < gameData.rules.MinimumPopulationForWeLoveTheKing || isInCivilDisorder
					|| residents.Any(r => r.resisting) || FoodGrowthPerTurn() < 0) {
				return false;
			}
			List<CityResident> laborers = residents.Where(HasMood).ToList();
			int happy = laborers.Count(r => r.mood == CityResident.Mood.Happy);
			int content = laborers.Count(r => r.mood == CityResident.Mood.Content);
			return laborers.All(r => r.mood != CityResident.Mood.Unhappy) && happy > content;
		}

		// Starts or ends the city's We Love the King Day as it qualifies or
		// not, once a turn after its moods are worked out. A celebrating city
		// wastes fewer shields (CalculateCorruption). Returns whether it
		// started or ended.
		public bool UpdateCelebration(GameData gameData) {
			bool qualifies = QualifiesForCelebration(gameData);
			if (qualifies == celebrating) {
				return false;
			}
			celebrating = qualifies;
			return true;
		}

		public enum Mood {
			Unhappy,
			Happy
		};

		// This function does the heavy lifting of happiness calculations,
		// combining the various bonuses and penalties that affect citizen moods.
		//
		// Some references:
		//  - https://forums.civfanatics.com/threads/how-is-happiness-calculated.74966/
		//  - https://codehappy.net/apolyton/threads/83368-1.htm
		//
		public Mood RecalculateCitizenMoods(GameData gameData) {
			CityResident.Mood happy = CityResident.Mood.Happy;
			CityResident.Mood content = CityResident.Mood.Content;
			CityResident.Mood unhappy = CityResident.Mood.Unhappy;
			InitializeMoodsForDifficulty(gameData.gameDifficulty);

			// We want to track the move deltas from content to happy and unhappy
			// to content. We can also move from unhappy straight to content,
			// but it costs 2 "points" from the contentToHappy counter, and is
			// only applied if there are no content faces.
			int contentToHappyMoves = 0;
			int unhappyToContentMoves = 0;

			// Each citizen lost to pop rushing has a 20 turn penalty, so
			// multiple citizens lost causes multiple unhappy faces.
			if (turnsOfUnhappinessDueToPopRushing > 0) {
				contentToHappyMoves -= (turnsOfUnhappinessDueToPopRushing - 1) / gameData.rules.TurnPenaltyForEachHurrySacrifice + 1;
			}

			// The same for each citizen drafted.
			if (turnsOfUnhappinessDueToDrafting > 0) {
				contentToHappyMoves -= (turnsOfUnhappinessDueToDrafting - 1) / Math.Max(1, gameData.rules.TurnPenaltyForEachDraftedCitizen) + 1;
			}

			// War weariness, or war happiness while another civ's declaration
			// of war on us still rallies the people (see WarWeariness).
			(int warWeary, int warHappy) = WarWeariness.CitizensAffected(gameData, this);
			contentToHappyMoves += warHappy - warWeary;

			// Citizens whose civ we're at war with resent our aggression
			// against their home country: one unhappy face each.
			contentToHappyMoves -= CitizensOfEnemyNationality(gameData);

			// And those confused by propaganda.
			contentToHappyMoves -= confusedCitizens;

			// Building happiness/unhappiness, which only affects the unhappy to
			// content transition, nothing with happy faces.
			foreach (CityBuilding cb in GetBuildings()) {
				if (cb.building.IsObsolete(owner)) {
					continue;
				}
				unhappyToContentMoves -= cb.building.unhappyFacesInCity;
				unhappyToContentMoves += cb.building.contentFacesInCity;
			}

			// Buildings in our other cities with an empire-wide effect.
			unhappyToContentMoves += owner.GetNetContentFacesFromOtherCities(this);

			// Depending on the government type, land defensive units can serve
			// as military police. An army is one unit; its members don't count.
			unhappyToContentMoves += Math.Min(owner.government.militaryPoliceLimit, location.unitsOnTile.Count(x => !x.IsInArmy() && x.CanDefendOnLand()));

			// Luxury spending moves content faces to happy faces.
			//
			// Don't respect civil disorder during this calculation, because if
			// we are currently in civil disorder our commerce is all corrupt,
			// but we still need to be able to calculate whether a certain
			// luxury slider value would get us out of civil disorder.
			contentToHappyMoves += CurrentCommerceYield(respectCivilDisorder: false).happiness;

			// As do luxury resources, which can be boosted by marketplaces.
			int effectiveLux = GetLuxuries(gameData).Keys.Count;
			// TODO: Skip obsolete buildings (see Building.IsObsolete).
			if (GetBuildings().Any(x => x.building.increasesLuxuryTrade)) {
				effectiveLux = (int)(Math.Floor(effectiveLux / 2f) * Math.Ceiling(effectiveLux / 2f) + Math.Ceiling(effectiveLux / 2f));
			}
			contentToHappyMoves += effectiveLux;

			if (contentToHappyMoves >= 0) {
				if (unhappyToContentMoves >= 0) {
					// First apply all the unhappy->content moves. If there are
					// left over content faces that's ok, they can't be used to
					// get a citizen to happy.
					ApplyMoodChange(unhappyToContentMoves, unhappy, content);

					// Then do the same for content->happy moves, which can also
					// make unhappy->happy moves.
					ConsumeContentToHappyMoves(contentToHappyMoves);
				} else {
					int happyPoints = contentToHappyMoves;
					int sadPoints = -unhappyToContentMoves; // Deal with positive numbers

					// Each "sadness point" wipes away a content->happy move,
					// so netralize things out.
					contentToHappyMoves = Math.Max(0, happyPoints - sadPoints);
					sadPoints = Math.Max(0, sadPoints - happyPoints);

					// Use up all our content to happy moves.
					ConsumeContentToHappyMoves(contentToHappyMoves);

					// Now apply any remaining "sadness points", moving happy
					// faces back to content.
					ApplyMoodChange(sadPoints, happy, content);
				}
			} else {
				if (unhappyToContentMoves >= 0) {
					int sadPoints = -contentToHappyMoves; // Deal with positive numbers
					int contentPoints = unhappyToContentMoves;

					// Each H->C move point wipes away a content move, so
					// netralize things out.
					unhappyToContentMoves = Math.Max(0, contentPoints - sadPoints);
					sadPoints = Math.Max(0, sadPoints - contentPoints);

					// Content faces get moved to unhappy by the penalty. We
					// start with some content based on the difficulty level so
					// this is meaningful. We don't need to worry about H->U
					// moves because there is no way to have happy faces on this
					// code path.
					ApplyMoodChange(sadPoints, content, unhappy);

					// Then our positive U->C moves get applied.
					ApplyMoodChange(unhappyToContentMoves, unhappy, content);
				} else {
					int sadPoints = -contentToHappyMoves; // Deal with positive numbers

					// We have buildings that make happy faces go to content
					// faces, but there is no way to have happy faces on this
					// code path, so we just need to move content faces to
					// unhappy faces with the main penalty.
					ApplyMoodChange(sadPoints, content, unhappy);
				}
			}

			int happyCount = 0;
			int unhappyCount = 0;
			foreach (CityResident cr in residents.Where(HasMood)) {
				if (cr.mood == CityResident.Mood.Happy) { ++happyCount; }
				if (cr.mood == CityResident.Mood.Unhappy) { ++unhappyCount; }
			}
			if (unhappyCount > 0 && unhappyCount > happyCount) {
				return Mood.Unhappy;
			} else {
				return Mood.Happy;
			}
		}

		private Dictionary<Resource, int> ListResourceAccess(GameData gameData, ResourceCategory category) {
			Dictionary<Resource, int> availableResources = gameData.GetTradeNetwork().GetResourcesAvailableToCity(owner, this);
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

		public Dictionary<Resource, int> GetLuxuries(GameData gameData) {
			return ListResourceAccess(gameData, ResourceCategory.LUXURY);
		}
	}
}

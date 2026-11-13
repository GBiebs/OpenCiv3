using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData.Save;
using C7Engine;
using C7Engine.Lua;

namespace C7GameData {
	public class Building : IProducible {
		public class GreatWonderProperties {
			// The building this building gives to every city in the empire on
			// on the continent (like the pyramids or the internet), if any.
			public Building buildingGainedInEveryCity;
			public Building buildingGainedInEveryCityOnContinent;
		}

		public string name { get; set; }
		// The building's key in the Civilopedia, like BLDG_Barracks, or null
		// if the rules don't say.
		public string civilopediaEntry;
		public int shieldCost { get; set; }
		public int populationCost { get; set; } // Will always be equal to 0 in the Civ3 rule set

		// Filled in in SaveGame::ConvertBuildings
		public Tech requiredTech { get; set; }
		public Tech? renderedObsoleteBy;

		public Building requiredBuilding;

		List<Func<City, bool>> productionPrerequisites = [];
		public Action<MapUnit> onFinishedUnitProduction;
		public Action<Tile.Yield> tileModifier;

		// Filled in in SaveGame::ConvertBuildings. Non-null for great wonders.
		public GreatWonderProperties? greatWonderProperties;

		public bool isSmallWonder;
		public bool isCenterOfEmpire;
		public bool increasesLuxuryTrade;
		public bool reducesCorruption;
		public bool isForbiddenPalace;

		// Whether the building works as a Forbidden Palace for the player.
		// One that needs a government only does under it: Civ3's Secret
		// Police HQ "acts as a second FP while in Communism"
		// (https://forums.civfanatics.com/threads/civ3-conquests-additions-changes-list.104294/),
		// "a corruption-fighting building which only works under Communism"
		// (https://sullla.com/Civ3/CQ/CQ_techs.html). Its BIQ ties it to
		// Communism through its required government.
		public bool WorksAsForbiddenPalaceFor(Player player) {
			return isForbiddenPalace && (requiredGovernment == null || player?.government?.id == requiredGovernment.id);
		}
		public bool allowsCitySize2;
		public bool allowsCitySize3;
		public bool doublesCityGrowthRate;
		public bool providesWalls;
		public bool onlyUsefulInTowns;
		// A city keeps only one building with this flag (the power plants).
		public bool replacesOtherBuildings;
		// Against land attacks, and against naval ones (see
		// City.GetDefenseBonuses).
		public StrengthBonus? combatDefenseBonus;
		public StrengthBonus? navalDefenseBonus;
		public bool providesVeteranGroundUnits;
		public bool providesVeteranSeaUnits;
		public bool providesVeteranAirUnits;
		// Trade network links (see SaveBuilding.Flag.AllowsWaterTrade).
		public bool allowsWaterTrade;
		public bool allowsAirTrade;
		// Can melt down in a city in disorder (see Meltdown).
		public bool canMeltdown;
		public bool allowsEnemyTerritoryHealing;
		public bool reducesWarWeariness;
		// See City.PollutionIcons.
		public bool removesPopulationPollution;
		public bool reducesBuildingPollution;
		public bool reducesWarWearinessEverywhere;
		public bool treasuryEarnsInterest;
		public bool paysTradeMaintenance;
		// Doubles the culture of a sacrifice in the city (see
		// MapUnit.SacrificeValue).
		public bool doublesSacrifice;
		public bool increasesResearch;
		public bool doublesResearch;
		public bool increasesLuxury;
		public bool increasesTax;
		public int productionBonusPercent = 0;

		// Wonder effects, mirroring the SaveBuilding flags and fields of the
		// same names (documented there).
		public bool continentalMoodEffects;
		public bool safeSeaTravel;
		public bool gainAnyTechKnownByTwoCivs;
		public bool doubleCombatVsBarbarians;
		public bool increasedShipMovement;
		public bool plusTwoShipMovement;
		public bool cheaperUpgrades;
		public bool twoFreeAdvances;
		public bool allowDiplomaticVictory;
		public bool allowsNuclearWeapons;
		public bool doublesCityGrowthEverywhere;
		public bool touristAttraction;
		public bool increasesLeaderChance;
		public bool increasedArmyValue;
		public bool decreasesMissileSuccess;
		public bool allowsSpyMissions;
		public bool buildSpaceshipParts;
		// The part's index in Rules.SpaceshipPartsRequired for spaceship
		// parts, -1 for every other building. See SpaceRace.
		public int spaceshipPart = -1;
		public bool IsSpaceshipPart => spaceshipPart >= 0;
		public int requiredBuildingCount;
		// Filled in in SaveGame::ConvertBuildings.
		public Government? requiredGovernment;
		public bool goodsMustBeInCityRadius;
		public int contentFacesAllCities;
		// Filled in in SaveGame::ConvertBuildings.
		public Building? doublesHappinessOf;
		public int pollution;
		// The strength the building defends itself with against bombardment
		// (BIQ "bombard defense"), for walls. Rulesets without the value get
		// Civ3's 8 for walls (Civilopedia: "City walls have a land
		// bombardment defense of 8").
		public int bombardDefense;
		internal const int DefaultWallBombardDefense = 8;
		// The strength the building fires at enemy aircraft bombing its city
		// with (BIQ "air power", 8 for the SAM Missile Battery in
		// conquests.biq): "SAM Missile Batteries attack enemy air units that
		// attempt to attack the SAM site's city" (Civilopedia,
		// BLDG_SAM_Missile_Battery). See MapUnit.TryToIntercept.
		public int airPower;
		// The strength with which the building bombards enemy ships passing
		// the city (the BIQ's naval power: 8 for the Coastal Fortress in
		// conquests.biq). See MapUnit.FindCoastalFortressesAlong.
		public int navalPower;
		// Resolved by name against GameData.unitPrototypes when needed.
		public string? unitProducedName;
		public int unitFrequency;

		// Army buildings: the Military Academy lets its city build armies, and
		// the Pentagon lets the owner's armies carry one more unit.
		public bool allowsBuildArmy;
		public bool allowsLargerArmies;
		public bool requiresVictoriousArmy;
		public int numberOfArmiesRequired;

		public int culturePerTurn = 0;
		public int maintenanceCost = 0;
		// See SaveBuilding and City.BuildingGold.
		public int goldPerTurn = 0;
		public int goldPerWorkedCoastTile = 0;
		public bool doublesBuildingGold;

		// The number of unhappy faces that become content in the city with this
		// building.
		public int contentFacesInCity = 0;

		// The number of happy faces that become content in the city with this
		// building. Note that this is less powerful than other sources of
		// unhappiness, like drafting or poprushing, which converts happy faces
		// to sad faces.
		public int unhappyFacesInCity = 0;

		public HashSet<Resource> requiredResources { get; set; } = [];

		public int iconRowIndex = 0;

		SaveBuilding dataSource;

		public Building(SaveBuilding building, GameData gameData) {
			dataSource = building;

			name = building.name;
			civilopediaEntry = building.civilopediaEntry;
			shieldCost = building.shieldCost;
			populationCost = building.populationCost;
			isSmallWonder = building.isSmallWonder;
			culturePerTurn = building.culturePerTurn;
			maintenanceCost = building.maintenanceCost;
			goldPerTurn = building.goldPerTurn;
			goldPerWorkedCoastTile = building.goldPerWorkedCoastTile;
			doublesBuildingGold = building.doublesBuildingGold;
			iconRowIndex = building.iconRowIndex;

			if (building.contentFacesInCity < 0) {
				unhappyFacesInCity = -building.contentFacesInCity;
			} else {
				contentFacesInCity = building.contentFacesInCity;
			}

			if (building.combatDefenseBonus > 0) {
				combatDefenseBonus = new(name, building.combatDefenseBonus);
			}
			if (building.navalDefenseBonus > 0) {
				navalDefenseBonus = new(name, building.navalDefenseBonus);
			}

			isCenterOfEmpire = building.flags.Contains(SaveBuilding.Flag.IsCenterOfEmpire);
			increasesLuxuryTrade = building.flags.Contains(SaveBuilding.Flag.IncreasesLuxuryTrade);
			reducesCorruption = building.flags.Contains(SaveBuilding.Flag.ReducesCorruption);
			isForbiddenPalace = building.flags.Contains(SaveBuilding.Flag.ForbiddenPalace);
			allowsCitySize2 = building.flags.Contains(SaveBuilding.Flag.AllowsCitySize2);
			allowsCitySize3 = building.flags.Contains(SaveBuilding.Flag.AllowsCitySize3);
			doublesCityGrowthRate = building.flags.Contains(SaveBuilding.Flag.DoublesCityGrowthRate);
			providesWalls = building.flags.Contains(SaveBuilding.Flag.ProvidesWalls);
			onlyUsefulInTowns = building.flags.Contains(SaveBuilding.Flag.CanOnlyBeBuiltInTowns);
			replacesOtherBuildings = building.flags.Contains(SaveBuilding.Flag.ReplacesOtherBuildings);
			providesVeteranGroundUnits = building.flags.Contains(SaveBuilding.Flag.VeteranGroundUnits);
			providesVeteranSeaUnits = building.flags.Contains(SaveBuilding.Flag.VeteranSeaUnits);
			providesVeteranAirUnits = building.flags.Contains(SaveBuilding.Flag.VeteranAirUnits);
			allowsWaterTrade = building.flags.Contains(SaveBuilding.Flag.AllowsWaterTrade);
			allowsAirTrade = building.flags.Contains(SaveBuilding.Flag.AllowsAirTrade);
			canMeltdown = building.flags.Contains(SaveBuilding.Flag.CanMeltdown);
			allowsEnemyTerritoryHealing = building.flags.Contains(SaveBuilding.Flag.AllowsEnemyTerritoryHealing);
			reducesWarWeariness = building.flags.Contains(SaveBuilding.Flag.ReducesWarWeariness);
			removesPopulationPollution = building.flags.Contains(SaveBuilding.Flag.RemovesPopulationPollution);
			reducesBuildingPollution = building.flags.Contains(SaveBuilding.Flag.ReducesBuildingPollution);
			reducesWarWearinessEverywhere = building.flags.Contains(SaveBuilding.Flag.ReducesWarWearinessEverywhere);
			treasuryEarnsInterest = building.flags.Contains(SaveBuilding.Flag.TreasuryEarnsInterest);
			paysTradeMaintenance = building.flags.Contains(SaveBuilding.Flag.PaysTradeMaintenance);
			doublesSacrifice = building.flags.Contains(SaveBuilding.Flag.DoublesSacrifice);
			allowsBuildArmy = building.flags.Contains(SaveBuilding.Flag.AllowsBuildArmy);
			allowsLargerArmies = building.flags.Contains(SaveBuilding.Flag.AllowsLargerArmies);
			requiresVictoriousArmy = building.flags.Contains(SaveBuilding.Flag.RequiresVictoriousArmy);
			numberOfArmiesRequired = building.numberOfArmiesRequired;
			increasesResearch = building.flags.Contains(SaveBuilding.Flag.Plus50PercentResearch);
			doublesResearch = building.flags.Contains(SaveBuilding.Flag.DoublesResearchOutput);
			// Civ3's "+50% tax" flag (marketplace, bank, stock exchange) leaves
			// the luxury slider alone; only the separate luxury flag boosts it.
			increasesLuxury = building.flags.Contains(SaveBuilding.Flag.Plus50PercentLuxury);
			increasesTax = building.flags.Contains(SaveBuilding.Flag.Plus50PercentCommerce);
			productionBonusPercent = building.productionBonusPercent;
			continentalMoodEffects = building.flags.Contains(SaveBuilding.Flag.ContinentalMoodEffects);
			safeSeaTravel = building.flags.Contains(SaveBuilding.Flag.SafeSeaTravel);
			gainAnyTechKnownByTwoCivs = building.flags.Contains(SaveBuilding.Flag.GainAnyTechKnownByTwoCivs);
			doubleCombatVsBarbarians = building.flags.Contains(SaveBuilding.Flag.DoubleCombatVsBarbarians);
			increasedShipMovement = building.flags.Contains(SaveBuilding.Flag.IncreasedShipMovement);
			plusTwoShipMovement = building.flags.Contains(SaveBuilding.Flag.PlusTwoShipMovement);
			cheaperUpgrades = building.flags.Contains(SaveBuilding.Flag.CheaperUpgrades);
			twoFreeAdvances = building.flags.Contains(SaveBuilding.Flag.TwoFreeAdvances);
			allowDiplomaticVictory = building.flags.Contains(SaveBuilding.Flag.AllowDiplomaticVictory);
			allowsNuclearWeapons = building.flags.Contains(SaveBuilding.Flag.AllowsNuclearWeapons);
			doublesCityGrowthEverywhere = building.flags.Contains(SaveBuilding.Flag.DoublesCityGrowthEverywhere);
			touristAttraction = building.flags.Contains(SaveBuilding.Flag.TouristAttraction);
			increasesLeaderChance = building.flags.Contains(SaveBuilding.Flag.IncreasesLeaderChance);
			increasedArmyValue = building.flags.Contains(SaveBuilding.Flag.IncreasedArmyValue);
			decreasesMissileSuccess = building.flags.Contains(SaveBuilding.Flag.DecreasesMissileSuccess);
			allowsSpyMissions = building.flags.Contains(SaveBuilding.Flag.AllowsSpyMissions);
			buildSpaceshipParts = building.flags.Contains(SaveBuilding.Flag.BuildSpaceshipParts);
			spaceshipPart = building.spaceshipPart is int part && part >= 0 ? part : -1;
			requiredBuildingCount = building.requiredBuildingCount;
			goodsMustBeInCityRadius = building.flags.Contains(SaveBuilding.Flag.GoodsMustBeInCityRadius);
			contentFacesAllCities = building.contentFacesAllCities;
			pollution = building.pollution;
			bombardDefense = building.bombardDefense > 0 ? building.bombardDefense
				: providesWalls ? DefaultWallBombardDefense : 0;
			airPower = building.airPower;
			navalPower = building.navalPower;
			unitProducedName = building.unitProduced;
			unitFrequency = building.unitFrequency;

			if (building.greatWonderProperties != null) {
				greatWonderProperties = new();
			}

			LoadLuaFunctions(gameData);
		}

		[LuaMethod]
		public bool IsGreatWonder() {
			return this.greatWonderProperties != null;
		}

		// Facts about a city and its owner that the CanProduce check of every
		// building needs. Listing a city's production options gathers them
		// once instead of rescanning the empire for each building. Only valid
		// while nothing changes, i.e. for one pass over the buildings.
		internal sealed class ProductionContext {
			private readonly City city;

			// The buildings the city has, including ones granted by wonders.
			internal readonly HashSet<Building> cityBuildings = new();

			// The rest are filled in when first needed.
			private HashSet<string> namesBeingProducedByOtherCities;
			private HashSet<Building> builtByEmpire;
			private HashSet<IProducible> producedByOtherCities;
			private int armyCount = -1;

			// Whether the city has a Forbidden Palace or Secret Police HQ.
			internal readonly bool hasForbiddenPalace;

			private Dictionary<Building, int> citiesWithBuilding;

			internal ProductionContext(City city) {
				this.city = city;
				foreach (CityBuilding cb in city.AllEffectiveBuildings()) {
					cityBuildings.Add(cb.building);
					hasForbiddenPalace |= cb.building.isForbiddenPalace;
				}
			}

			// The number of the owner's cities with the building.
			internal int CitiesWithBuilding(Building building) {
				citiesWithBuilding ??= new(ReferenceEqualityComparer.Instance);
				if (!citiesWithBuilding.TryGetValue(building, out int count)) {
					count = Building.CitiesWithBuilding(city.owner, building);
					citiesWithBuilding[building] = count;
				}
				return count;
			}

			// Whether another of the owner's cities is producing something
			// with the given name. This city is excluded, so a wonder it is
			// building stays among its own options.
			internal bool OtherCityIsProducingNamed(string name) {
				if (namesBeingProducedByOtherCities == null) {
					namesBeingProducedByOtherCities = new();
					foreach (City c in city.owner.cities) {
						if (c != city && c.itemBeingProduced != null) {
							namesBeingProducedByOtherCities.Add(c.itemBeingProduced.name);
						}
					}
				}
				return namesBeingProducedByOtherCities.Contains(name);
			}

			// Whether any of the owner's cities has built the building.
			internal bool EmpireHasBuilt(Building building) {
				if (builtByEmpire == null) {
					builtByEmpire = new(ReferenceEqualityComparer.Instance);
					foreach (City c in city.owner.cities) {
						foreach (CityBuilding cb in c.constructed_buildings) {
							builtByEmpire.Add(cb.building);
						}
					}
				}
				return builtByEmpire.Contains(building);
			}

			// Whether another of the owner's cities is producing this exact
			// item.
			internal bool OtherCityIsProducing(IProducible producible) {
				if (producedByOtherCities == null) {
					producedByOtherCities = new(ReferenceEqualityComparer.Instance);
					foreach (City c in city.owner.cities) {
						if (c != city && c.itemBeingProduced != null) {
							producedByOtherCities.Add(c.itemBeingProduced);
						}
					}
				}
				return producedByOtherCities.Contains(producible);
			}

			internal int ArmyCount() {
				if (armyCount < 0) {
					armyCount = city.owner.ArmyCount();
				}
				return armyCount;
			}
		}

		public bool CanProduce(City city, HashSet<Resource> accessibleResources) {
			return CanProduce(city, accessibleResources, null);
		}

		// The context, when given, must have been made for this city since
		// the last change to the game.
		internal bool CanProduce(City city, HashSet<Resource> accessibleResources, ProductionContext context) {
			if (!city.owner.HasRequiredTechnology(this)) {
				return false;
			}

			if (renderedObsoleteBy != null && city.owner.knownTechs.Contains(renderedObsoleteBy.id)) {
				return false;
			}

			if (context != null ? context.cityBuildings.Contains(this) : city.HasEffectiveBuilding(this)) {
				return false;
			}

			if (greatWonderProperties != null) {
				// We can't build a great wonder if it was already built.
				if (EngineStorage.gameData.GreatWondersBuilt.Contains(name)) {
					return false;
				}

				// We can't build a great wonder if another one of our cities is
				// building it. This city building it doesn't count, or the
				// wonder would vanish from its own list of options.
				if (context != null) {
					if (context.OtherCityIsProducingNamed(name)) {
						return false;
					}
				} else {
					foreach (City c in city.owner.cities) {
						if (c != city && c.itemBeingProduced != null && c.itemBeingProduced.name == name) {
							return false;
						}
					}
				}
			}

			// "If you turn off diplomatic victory, you can't build the UN
			// anymore" (https://forums.civfanatics.com/threads/no-city-may-build-space-ship.11649/).
			if (allowDiplomaticVictory && EngineStorage.gameData != null && !UnitedNations.DiplomaticVictoryAllowed(EngineStorage.gameData)) {
				return false;
			}

			if (isCenterOfEmpire) {
				// Building a palace in another city moves the capital there.
				// Assumption: like the Forbidden Palace below, Civ3 doesn't
				// offer it in a city that already has a second palace.
				if (city.IsCapital() || HasForbiddenPalace(city, context)) {
					return false;
				}
			}

			if (isForbiddenPalace) {
				// A second (or, with the Secret Police HQ, third) center of
				// the empire makes no sense in the capital or in a city that
				// already is one.
				if (city.IsCapital() || HasForbiddenPalace(city, context)) {
					return false;
				}
				// "The number of cities a player must have to enable
				// construction of any wonder with this flag is equal to the
				// Optimal Number of Cities divided by 2" (C3C editor help,
				// Reduces Corruption); "at least ten cities (on a standard size
				// map)" (Civilopedia, BLDG_Forbidden_Palace).
				int optimal = EngineStorage.gameData?.map?.optimalNumberOfCities ?? 0;
				if (city.owner.cities.Count < optimal / 2) {
					return false;
				}
			}

			if (requiredGovernment != null && city.owner.government?.id != requiredGovernment.id) {
				return false;
			}

			if (isSmallWonder || isCenterOfEmpire) {
				// A civ builds each small wonder once, in one city at a time.
				// The palace can be rebuilt elsewhere (moving it), but also
				// only in one city at a time.
				if (context != null) {
					if ((isSmallWonder && context.EmpireHasBuilt(this)) || context.OtherCityIsProducing(this)) {
						return false;
					}
				} else {
					foreach (City c in city.owner.cities) {
						if (isSmallWonder && c.constructed_buildings.Exists(cb => cb.building == this)) {
							return false;
						}
						if (c != city && c.itemBeingProduced == this) {
							return false;
						}
					}
				}
			}

			// Spaceship parts need the Apollo Program and are limited to the
			// number the ship needs. See SpaceRace.
			if (spaceshipPart >= 0 && !SpaceRace.CanBuildPart(EngineStorage.gameData, city, this)) {
				return false;
			}

			if (requiresVictoriousArmy && !city.owner.hasVictoriousArmy) {
				return false;
			}

			if (numberOfArmiesRequired > 0
				&& (context != null ? context.ArmyCount() : city.owner.ArmyCount()) < numberOfArmiesRequired) {
				return false;
			}

			if (requiredBuilding != null) {
				if (requiredBuildingCount > 1) {
					// Civ3's "number of required buildings": the civ needs that
					// many cities with the building (like Wall Street's five
					// stock exchanges). Assumption: the building city itself
					// needn't be one of them.
					int count = context != null ? context.CitiesWithBuilding(requiredBuilding) : CitiesWithBuilding(city.owner, requiredBuilding);
					if (count < requiredBuildingCount) {
						return false;
					}
				} else if (!(context != null ? context.cityBuildings.Contains(requiredBuilding) : city.HasEffectiveBuilding(requiredBuilding))) {
					return false;
				}
			}

			foreach (Resource resource in requiredResources) {
				if (!accessibleResources.Contains(resource)) {
					return false;
				}
				// Some buildings need the resource in the city's own radius,
				// not merely connected to it.
				if (goodsMustBeInCityRadius && !city.GetWorkableTiles().Exists(t => t.Resource == resource)) {
					return false;
				}
			}

			foreach (Func<City, bool> prerequisite in productionPrerequisites) {
				if (!prerequisite(city)) {
					return false;
				}
			}

			return true;
		}

		// The number of the player's cities that have the building, including
		// ones granted by wonders.
		internal static int CitiesWithBuilding(Player player, Building building) {
			int count = 0;
			foreach (City c in player.cities) {
				if (c.HasEffectiveBuilding(building)) {
					++count;
				}
			}
			return count;
		}

		private static bool HasForbiddenPalace(City city, ProductionContext context) {
			if (context != null) {
				return context.hasForbiddenPalace;
			}
			foreach (CityBuilding cb in city.constructed_buildings) {
				if (cb.building.isForbiddenPalace) {
					return true;
				}
			}
			return false;
		}

		// The civilization strengths this building is associated with.
		public IReadOnlySet<Civilization.Trait> traits => dataSource.traits;

		// Whether the building has this flag from its rules, for flags with
		// no field of their own.
		public bool HasFlag(SaveBuilding.Flag flag) => dataSource.flags.Contains(flag);

		// Civ3 halves the cost of the improvements tied to a civ's traits.
		// The FAQ lists them all, and none is a wonder: "Religious buildings
		// (Temples, Cathedrals) are all half price", "Scientific buildings
		// (Libraries, Universities, Research Labs) are half price", ...
		// (https://civfanatics.com/civ3/faq/). Wonders carry trait flags too,
		// but "the trait of a wonder determines what civilisations it can
		// trigger golden ages for"
		// (https://civfanatics.com/civ3/strategy/empire-management/a-guide-to-great-wonders-and-how-to-use-them/).
		// No source says outright that wonders are never discounted.
		public int ShieldCost(HashSet<Civilization.Trait> civTraits, float costFactor) {
			if (IsGreatWonder() || isSmallWonder) {
				return (int)(shieldCost * costFactor);
			}
			foreach (Civilization.Trait trait in dataSource.traits) {
				if (civTraits.Contains(trait)) {
					return (int)(shieldCost * EngineStorage.gameData.rules.BuildingDiscountForCivTraits * costFactor);
				}
			}
			return (int)(shieldCost * costFactor);
		}

		// Whether the building's effects apply for the owner: every building
		// but one obsolete for it (see City.EffectiveBuildings).
		public bool TakesEffectFor(Player owner) {
			return !IsObsoleteFor(owner);
		}

		// Whether the owner knows the tech that makes the building obsolete.
		// An obsolete building can't be built, loses its effects and costs no
		// upkeep, but keeps making culture.
		//
		// Civ3's editor offers "Rendered Obsolete By" for great wonders only
		// (EDHELP, Rendered_Obsolete_By_Wonder_Properties: "This option is
		// only available if the "wonder" category is selected"), so in
		// Conquests' rules no other building goes obsolete. A rule file can
		// still give one to any building, and the 1.22 code checks it for
		// every building in places: the luxury, science and tax multipliers
		// skip "a building ... if its owner already knows the building's
		// obsoleting tech" (Perez's decompile, Civ3_Findings.md), and
		// maintenance, when recomputed, "does skip obsolete buildings"
		// (Flintlock, compendium c3x-thread-notes-2). So every obsolete
		// building loses its effects, as an obsolete great wonder does.
		// UNVERIFIED beyond those two: whether the stock code checks it for
		// a regular improvement's other effects (defence, happiness,
		// production) is not known (compendium 02, open questions); this
		// takes the wonders' rule, effects stop and culture continues
		// (MANUAL p.130), as the most plausible reading.
		public bool IsObsoleteFor(Player owner) {
			return renderedObsoleteBy != null && owner?.knownTechs != null && owner.knownTechs.Contains(renderedObsoleteBy.id);
		}

		// Whether this is a great wonder obsolete for the owner.
		public bool isGreatWonderObsolete(Player owner) {
			return greatWonderProperties != null && IsObsoleteFor(owner);
		}

		public SaveBuilding ToSaveBuilding() {
			return dataSource;
		}

		public override string ToString() {
			return name;
		}

		private void LoadLuaFunctions(GameData gameData) {
			BehaviorEngine luaEngine = gameData.luaBehaviorEngine;

			foreach (var path in dataSource.productionPrerequisites) {
				var rule = luaEngine.ImportFunc<Func<City, bool>>(path);
				productionPrerequisites.Add(rule);
			}

			foreach (var path in dataSource.onFinishedUnitProduction) {
				var handler = luaEngine.ImportFunc<Action<MapUnit>>(path);
				onFinishedUnitProduction += handler;
			}

			foreach (var path in dataSource.tileModifiers) {
				var modifier = luaEngine.ImportFunc<Action<Tile.Yield>>(path);
				tileModifier += modifier;
			}
		}
	}
}

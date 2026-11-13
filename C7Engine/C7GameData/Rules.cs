using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace C7GameData {
	public class Rules {
		public int MaximumResearchTime;
		public int MinimumResearchTime;
		public int ShieldValueInGold;
		public int ForestValueInShields;
		public int CitizenValueInShields;
		public int TurnPenaltyForEachHurrySacrifice;
		public int MaximumLevel1CitySize;
		public int MaximumLevel2CitySize;
		public int FoodNeededToGrowForLevel1Cities = 20;
		public int FoodNeededToGrowForLevel2Cities = 40;
		public int FoodNeededToGrowForLevel3Cities = 60;
		public float BuildingDiscountForCivTraits = .5f;
		public string StartUnitType1;
		public string StartUnitType2;
		public string ScoutUnitType;
		public int MaxRankOfWorkableTiles;
		public int MaxRankOfBarbarianCampTiles;
		public int DefaultDealDuration;
		public float TreasuryInterestRate = .05f;
		public int MaxInterest = 50;
		public int ShieldCostPerGold;
		// The share of a unit's shield cost a city gets when the unit is
		// disbanded in it (rounded down, and nothing towards a wonder). Civ3
		// gives a quarter; it isn't in the BIQ. See
		// https://civfanatics.com/civ3/faq/ ("The number of shields added is
		// 1/4 the shield cost of the unit, rounded down") and
		// https://forums.civfanatics.com/threads/disbanding-units-for-shields.211405/
		public float ShieldRateForDisbanding = .25f;
		public bool AllowLesserUnitProduction; // for example, allow building a Spearman/Pikeman when we can build a Musketman (simultaneously)
		public int RadarTileVisibility; // how many tiles, a unit with the Radar ability, can see ahead

		// Game option: whether units can be unloaded from an army once they've
		// been loaded into it. Armies are rigid by default.
		public bool AllowUnloadFromArmy = false;

		// Game option: whether games with more than one human (hotseat or
		// LAN) show the scoreboard of players' scores and the turn clock.
		public bool ShowScoreboard = true;

		// Game option, not in Civ3: the capital and the cities nearest it
		// (the first CitiesFreeOfCorruption by rank) have no corruption or
		// waste. Off by default, so saves and Civ3 games play as before.
		public bool CoreCitiesFreeOfCorruption = false;
		public int CitiesFreeOfCorruption = 5;

		// Game option, not in Civ3: scales every city's corruption and waste
		// before the cap. 1 is Civ3's rate; by default C7 runs slightly below
		// it to go easier on players. Chosen when setting up a game, and
		// saves made before it was saved keep the 0.9 they were played with.
		public const float DefaultCorruptionRate = 0.9f;
		public const float MinCorruptionRate = 0.5f;
		public const float MaxCorruptionRate = 1.5f;
		public float CorruptionRate = DefaultCorruptionRate;

		// Scenario rules, not Civ3, set by the Eternal War scenario (see
		// Scenarios). Their defaults leave the game as Civ3 plays it.
		//
		// No city has corruption or waste.
		public bool NoCorruption = false;
		// Every civ is at war with every other from the first turn, and none
		// may make peace or even talk (see GameData.AreInLockedWar).
		public bool PermanentWar = false;
		// The percentage of the citizens past those born content who are born
		// unhappy; the rest are born content too, rounded in their favor.
		public int BornUnhappyPercent = 100;
		// The buildings a captured city always keeps, which would otherwise
		// be lost like any other (see CityInteractions.CaptureCity).
		public List<string> BuildingsKeptOnCapture = [];
		// The sizes a city grows to without fresh water or an aqueduct, and
		// without a hospital; 0 for the town and city size limits
		// (MaximumLevel1CitySize and MaximumLevel2CitySize), as in Civ3. They
		// move only the growth limits, not what counts as a town or a city.
		public int SizeWithoutAqueduct = 0;
		public int SizeWithoutHospital = 0;
		[JsonIgnore] public int GrowthLimitWithoutAqueduct => SizeWithoutAqueduct > 0 ? SizeWithoutAqueduct : MaximumLevel1CitySize;
		[JsonIgnore] public int GrowthLimitWithoutHospital => SizeWithoutHospital > 0 ? SizeWithoutHospital : MaximumLevel2CitySize;
		// Added to every tile's commerce, city centers included.
		public int TileCommerceBonus = 0;
		// Added to the food of forest on tundra.
		public int TundraForestFoodBonus = 0;
		// Added to the shields of desert, but not of flood plains.
		public int DesertShieldBonus = 0;
		// A worker beaten in battle is taken as a slave worker to the
		// winner's nearest city.
		public bool DefeatedWorkersBecomeSlaves = false;
		// How near the ocean a city must be to build a coastal building
		// (one that must be coastal, such as a harbor or the Colossus): 1
		// for next to it, as in Civ3, or the farthest rank of its radius.
		public int CoastalBuildingRange = 1;
		// Every turn starts with Staging, the Mercenary Market, where the
		// civs bid for units (see C7Engine.Staging).
		public bool StagingUnits = false;
		// How many units the market offers a turn.
		public int MarketOffersPerTurn = 3;
		// Tidal Waves turn the shore to sea ever faster (see
		// C7Engine.RisingTide).
		public bool RisingTide = false;
		// The citizens a settler founds a city with.
		public int NewCitySize = 1;
		// The food a city's center makes, before the agricultural bonus
		// (Civ3's is 2; Rising Tide's feeds two citizens).
		public int CityCenterFood = 2;
		// Settlers can only be built in cities taken from someone else
		// (Kings & Nomads; see GameSetup.KingsAndNomads.cs).
		public bool SettlersOnlyInConqueredCities = false;
		// A fallen civ's cities go to the independents (the barbarians)
		// rather than being destroyed (Kings & Nomads).
		public bool FallenCitiesGoIndependent = false;
		// The name of the only government anyone may have, or null for any
		// (Kings & Nomads: Monarchy).
		public string OnlyGovernment = null;

		// Every civ starts knowing every advance (MDJ; see GameSetup.Mdj.cs).
		public bool AllTechsAtStart = false;
		// A resource a civ has had once stays its own for the rest of the
		// game, though the road to it be cut or its tile lost (MDJ; see
		// Player.RecordObtainedResources).
		public bool PersistentResources = false;
		// War weariness can't overthrow the government either: everyone
		// keeps the one they start with (MDJ).
		public bool GovernmentLocked = false;
		// The tech tree isn't Civ3's (MDJ's): the science advisor draws its
		// arrows itself, over backgrounds without Civ3's, and lays out a page
		// with advances placed off it (see ScienceAdvisor).
		public bool OwnTechTree = false;

		public bool AllowsGovernment(Government government) {
			return OnlyGovernment == null || government?.name == OnlyGovernment;
		}

		// Not Civ3: the game was set up with teams, each a locked alliance,
		// and a team wins together (see GameSetup.Teams.cs and
		// GameData.AreTeammates).
		public bool TeamVictory = false;

		// HOUSE RULES, not Civ3: bonuses for AI players only, to make the AI
		// a stronger opponent beyond what Civ3's difficulty levels give it
		// (see Difficulty). Human players are never affected. Each default
		// leaves the game as Civ3 plays it. See AiHouseRules, which applies
		// them.
		//
		// The percentage of their normal maintenance the AI's buildings cost.
		// Civ3's AI pays full upkeep: none of the BIQ's difficulty (DIFF)
		// fields is a maintenance bonus (doc/civ3-compendium/08, section 1).
		// MDJ's ruleset sets 0.
		public int AiBuildingMaintenancePercent = 100;
		// Units each AI city supports for free on top of what its government
		// and the difficulty level allow.
		public int AiExtraFreeUnitsPerCity = 0;
		// The percentage of their normal corruption and waste the AI's
		// cities suffer.
		public int AiCorruptionPercent = 100;
		// Citizens born content in each AI city on top of the difficulty
		// level's number.
		public int AiExtraContentCitizens = 0;
		// The percentage of their normal cost the AI pays in shields, food
		// to grow and research, on top of the difficulty level's cost factor.
		public int AiCostPercent = 100;
		// Whether the AI's new fighting units start veteran, as if every AI
		// city had barracks, a harbor and an airport.
		public bool AiUnitsStartVeteran = false;

		// Game option from Civ3 (Play the World): the food, shields and
		// commerce cities generate each turn are doubled, speeding up growth,
		// research and production. Off by default.
		public bool AcceleratedProduction = false;

		// Game option from Civ3: whether cities may flip to a civ whose
		// culture outweighs their owner's (see CultureFlip). "Cultural flips
		// and SGL's can be turned off on the game setup screen" in Conquests
		// (https://forums.civfanatics.com/threads/civ3-conquests-additions-changes-list.104294/),
		// and "I'm pretty sure the default is on"
		// (https://forums.civfanatics.com/threads/cultural-conversion-question.87425/).
		// The BIQ and SAV GAME sections' "allow cultural conversions" flag.
		public bool AllowCultureFlips = true;

		// Game option from Conquests: whether a civ that is the first to
		// discover a tech may gain a scientific great leader (see
		// MapUnit_ScientificLeader.cs). "Cultural flips and SGL's can be
		// turned off on the game setup screen" (the thread above). The
		// owner asked for the option, on by default (owner's decision,
		// 2026-10-11). The BIQ and SAV GAME sections' "allow scientific
		// leaders" flag.
		public bool ScientificLeaders = true;

		// Each army needs this many cities to support it. A civ can't build an
		// army unless (armies + 1) * CitiesNeededToSupportAnArmy <= cities.
		public int CitiesNeededToSupportAnArmy = 4;

		// The unit a city builds through a building that allows building
		// armies (the Military Academy).
		public string BuildArmyUnit;

		// The unit (a military great leader) an elite unit's victory can
		// create. Null means victories create no leaders.
		public string BattleCreatedUnit;

		// How many of each spaceship part (by Building.spaceshipPart index)
		// the spaceship needs, from the BIQ's RULE section. Conquests needs
		// one of each of its ten parts. A part missing from the list needs
		// one.
		public List<int> SpaceshipPartsRequired = new();

		public int SpaceshipPartsNeeded(int partIndex) {
			if (partIndex < 0) {
				return 0;
			}
			return partIndex < SpaceshipPartsRequired.Count ? SpaceshipPartsRequired[partIndex] : 1;
		}

		public int MinimumPopulationForWeLoveTheKing = 3;
		public int GoldenAgeDuration = 20;
		public int UpgradeCostPerShield = 3; // gold per shield of difference between a unit and its upgrade

		// The gold each civ starts a new game with: the BIQ RULE's starting
		// treasury (10 in conquests.biq).
		public int StartingTreasury = 10;

		// The volcano eruption period, in years: the BIQ GAME section's
		// (5000 in conquests.biq). "Each volcano on the map will erupt at
		// least once within the indicated eruption period (shown in years)"
		// (https://forums.civfanatics.com/threads/questions-regarding-volcanos.330741/).
		// 0 or less, volcanoes never erupt. See Volcano.
		public int VolcanoEruptionPeriod = 5000;

		// A scenario's plague: the BIQ GAME section's Disasters settings
		// (PermitPlagues is 0 in conquests.biq, so the standard rules have
		// none). The times are in years. See Plague.
		public bool PermitPlagues = false;
		public string PlagueName = "Plague";
		public int PlagueEarliestStart;
		// How far past the earliest start the first outbreak may come, as a
		// percentage of the grace period.
		public int PlagueVariance;
		public int PlagueDuration;
		// 0 to 100.
		public int PlagueStrength;
		public int PlagueGracePeriod;
		public int PlagueMaxOccurrences;

		// The civilopedia name of the first era, which civs start a new game
		// in.
		public string FirstEraCivilopediaName = "ERAS_Ancient_Times";

		// What each era calls its researchers (the BIQ ERAS section's
		// researcher names in use, such as "Wise Men"), by the era's
		// civilopedia name, for the era splash's $WISEMEN1. Empty in saves
		// from before they were imported.
		public Dictionary<string, List<string>> EraResearcherNames = new();

		// The food each citizen eats a turn: the BIQ RULE's food consumption
		// per citizen (2 in conquests.biq).
		public int FoodConsumptionPerCitizen = 2;

		// How many road moves a unit can make for one movement point. Games
		// imported from Civ3 build the road's movement cost from it (see
		// SaveTerrainImprovement.Civ3Improvements). conquests.biq has 3: "when
		// on a road, a unit can travel three tiles, regardless of the
		// underlying terrain" (https://civfanatics.com/civ3/faq/).
		public int MovementAlongRoads = 3;

		// The defense bonus of a fortress, in per cent. Games imported from
		// Civ3 build the fortress's defense bonus from it. conquests.biq has
		// 50: "When a unit is stationed in a fortress it receives a 50% bonus
		// to their defense"
		// (https://forums.civfanatics.com/threads/combat-system-explained.7679/).
		public int FortressDefensiveBonus = 50;

		// The chances, in per cent, that a fighter flying air superiority,
		// or a SAM battery, intercepts an enemy bombing run: the BIQ RULE's
		// chances to intercept enemy air and stealth missions (50 and 5 in
		// conquests.biq). "Fighters, when in a city or airbase and given the
		// 'Air Superiority' order, have a 50% chance to attempt to
		// intercept"; stealth aircraft "have a very low chance (5%?) of
		// getting intercepted" (T-hawk), and "the 'Chance to Intercept Enemy
		// Stealth Missions' is used instead" for them (Ozymandias)
		// (https://forums.civfanatics.com/threads/aerial-combat-mechanics.90783/,
		// https://forums.civfanatics.com/threads/air-units-bombing-mechanics-in-the-open.678152/).
		// See MapUnit_Air.cs.
		public int ChanceToInterceptAirMissions = 50;
		public int ChanceToInterceptStealthMissions = 5;

		// The following come from the BIQ's RULE section, but nothing uses
		// them yet. The defaults are only for saves made before they were
		// imported.

		// The civ-wide culture level multiplier (the editor's "Lvl.
		// Multiplier"), and the base for the city culture thresholds at
		// which borders grow (City.GetBorderExpansionLevel): levels at 10,
		// 100, 1000 ... with the standard 10. Saves made before they were
		// imported get Civ3's defaults.
		public int BorderExpansionMultiplier = 1000;
		public int BorderFactor = 10;
		// The cost of each future tech.
		public int FutureTechCost;
		// The chance, in per cent, that a city in disorder riots.
		public int ChanceOfRioting = 20;
		// How many unhappy citizens each happy face from luxuries or
		// buildings makes content.
		public int CitizensAffectedByEachHappyFace = 2;
		// The strengths a city's buildings and citizens defend with against
		// bombardment. The editor: "Building Defense Bonus: Determines the
		// defensive rating (0-1000) for each building in a city. This value
		// is used only to determine building losses during city
		// bombardment", and the same for citizens; 16 each in Conquests
		// (https://forums.civfanatics.com/threads/citizen-and-buildings-defense-bonus.693504/).
		public int BuildingDefensiveBonus = 16;
		public int CitizenDefensiveBonus = 16;
		// The turns of unhappiness each drafted citizen causes.
		public int TurnPenaltyForEachDraftedCitizen = 20;
		// The unit a unit that enslaves its defeated foe makes (Civ3's
		// Slave), and the unit of capture-the-flag games.
		public string SlaveUnitType;
		public string FlagUnitType;

		// The names of a civ's culture levels, lowest first, as in "Our people
		// have developed a Solid culture." These are the Civ3 defaults; games
		// imported from Civ3 take them from the BIQ.
		public List<string> CultureLevelNames = ["Fledgling", "Weak", "Fragile", "Solid", "Strong", "Glorious"];

		// How the people of a civ regard another civ's culture, from the
		// highest ratio of the other's culture to theirs to the lowest, as in
		// "The Romans are in awe of our culture."
		public List<CultureOpinion> CultureOpinions = [
			new() { Name = "in awe of", Numerator = 3, Denominator = 1 },
			new() { Name = "admirers of", Numerator = 2, Denominator = 1 },
			new() { Name = "impressed with", Numerator = 1, Denominator = 1 },
			new() { Name = "unimpressed by", Numerator = 3, Denominator = 4 },
			new() { Name = "dismissive of", Numerator = 1, Denominator = 2 },
			new() { Name = "disdainful of", Numerator = 1, Denominator = 3 },
		];
	}

	// An opinion one civ's people hold of another civ's culture when the other
	// civ has at least Numerator/Denominator times their culture.
	public class CultureOpinion {
		public string Name;
		public int Numerator;
		public int Denominator;
	}
}

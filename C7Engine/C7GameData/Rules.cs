using System.Collections.Generic;

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
		// How far apart (in tiles, counting diagonals as one) a new city must
		// be from every other: 2, as in Civ3, forbids only neighbors (user,
		// 2026-11-03, after trying 3); 3 would leave two tiles between. The
		// AI keeps two tiles between its own cities anyway.
		public int MinimumCityDistance = 2;
		public int CapitalNeighborDistance = 8; // for capital relocation calculations
		public float BuildingDiscountForCivTraits = .5f;
		public string StartUnitType1;
		public string StartUnitType2;
		public string ScoutUnitType;
		// The unit captured workers and settlers become (Civ3's "slave" unit).
		public string SlaveUnitType = "Worker";
		// Gold per shield of difference to upgrade a unit (3 in Conquests).
		public int UpgradeCost = 3;
		// How long each drafted citizen makes a city unhappier, in turns.
		public int TurnPenaltyForEachDraftedCitizen = 20;
		// The smallest city that can celebrate We Love the King Day.
		public int MinimumPopulationForWeLoveTheKing = 6;
		// How many turns a golden age lasts.
		public int GoldenAgeDuration = 20;
		// Whether cities can flip to another civ by culture (Civ3's GAME
		// option "allow cultural conversions"). See CultureFlip.
		public bool AllowCulturalConversions = true;
		// The unit elite units can produce by winning battles (Civ3's
		// "battle-created unit": the military great leader).
		public string BattleCreatedUnitType = "Leader";
		// The unit Conquests' capture the princess plays with (RULE "flag unit
		// type"). See Princess.
		public string FlagUnitType = "Princess";
		// The chance, in percent, that a civ first to discover an advance gets
		// a scientific great leader, and with the Scientific trait; and the
		// Age of Science such a leader can start (Civilopedia "Leaders", "Age
		// of Science": 20 turns, research 25% more productive). The chances
		// aren't in Civ3's BIQ; these are players' figures (CivFanatics).
		public int ScientificLeaderChance = 3;
		public int ScientificTraitLeaderChance = 5;
		public int AgeOfScienceDuration = 20;
		public int AgeOfScienceResearchBonus = 25;
		// The chance, one in this many, that an elite unit winning an attack
		// against another civ produces a great leader; defending halves it.
		// Civ3's BIQ has no such value; these are Civ3's (CivFanatics,
		// "Creating and Using Leaders"): 16, or 12 with a building that
		// increases the chance (the Heroic Epic).
		public int LeaderChanceOneIn = 16;
		public int IncreasedLeaderChanceOneIn = 12;
		// The unit a leader turns into to build an army, and how many cities the
		// player needs for each army.
		public string ArmyUnitType = "Army";
		public int CitiesNeededToSupportAnArmy = 4;
		// An army fights with a bonus of its members' total attack (or defense)
		// divided by this, rounded down; the second with a building that
		// increases army value (the Military Academy). Not in Civ3's BIQ; these
		// are Conquests' (MapUnit.BaseCombatStrength).
		public int ArmyStrengthDivisor = 6;
		public int IncreasedArmyStrengthDivisor = 4;
		public int MaxRankOfWorkableTiles;
		public int MaxRankOfBarbarianCampTiles;
		public int BarbarianCampGold = 25; // gold for dispersing a barbarian camp; not in Civ3's BIQ, so this is also the default for Civ3 saves and older OpenCiv3 saves
		public int DefaultDealDuration;
		// Hotseat and play by email: the turns a human can ask another's
		// envoys to stay away (Civ3's "do not contact me for 20 turns"). Not
		// in Civ3's BIQ. See HumanDiplomacy.
		public int EnvoyRefusalTurns = 20;
		public float TreasuryInterestRate = .05f;
		public int MaxInterest = 50;
		public int ShieldCostPerGold;
		public float ShieldRateForDisbanding; // per cent
		public bool AllowLesserUnitProduction; // for example, allow building a Spearman/Pikeman when we can build a Musketman (simultaneously)
		public int RadarTileVisibility; // how many tiles, a unit with the Radar ability, can see ahead

		// What goody huts give (GoodyHuts); Civ3's amounts aren't documented,
		// so these follow players' reports: gold of 25 or 50 ("50 gp is the
		// max"), a map of the tiles within 5, and three angry tribesmen ("three
		// barbarians spawn often"). Not in Civ3's BIQ.
		public List<int> GoodyHutGold = [25, 50];
		public int GoodyHutMapRadius = 5;
		public int GoodyHutBarbarians = 3;

		// The chance of each goody hut outcome, in percent, by difficulty,
		// easiest first (a row whose name matches the game's difficulty wins,
		// else the row at its place), for most civs and for Expansionists. See
		// GoodyHuts. Placing them on new maps: one for every so many land
		// tiles, at least so far apart (and from barbarian camps). The angry
		// tribesmen are of the advanced barbarian type this percent of the time.
		public List<GoodyHutOdds> GoodyHutOdds = GoodyHutOddsRow.Standard();
		public List<GoodyHutOdds> ExpansionistGoodyHutOdds = GoodyHutOddsRow.Expansionist();
		public int LandTilesPerGoodyHut = 55;
		public int GoodyHutSpacing = 4;
		public int GoodyHutAdvancedBarbarianPercent = 25;

		// The chance, in percent, each of a city's pollution points gives each
		// turn that one of its tiles gets polluted (see Pollution). Civ3's
		// figure isn't documented; not in its BIQ.
		public int PollutionChancePerPoint = 5;
		// The points a building that reduces pollution (Mass Transit for the
		// citizens', a Recycling Center for the buildings') leaves: Civ3
		// leaves 1; a community patch made it 0.
		public int PollutionLeftByReducingBuildings = 1;

		// The percent of its shields a city keeps when it switches production
		// between a unit, an improvement and a wonder (rounded down).
		public int ProductionSwitchKeptPercent = 50;

		// The chance, one in this many, that a unit that enslaves turns a
		// unit it beats into a slave (Civ3: one in three).
		public int EnslaveChanceOneIn = 3;

		// How much less, in percent, upgrades cost with a wonder that makes
		// them cheaper.
		public int UpgradeDiscountPercent = 50;

		// A unit that wins defending rolls for a great leader at the attack
		// chance divided by this (CivFanatics: half).
		public int DefendingLeaderChanceFactor = 2;

		// What a golden age adds to each tile already making a shield or a
		// commerce.
		public int GoldenAgeTileBonus = 1;

		// Razing a city makes one slave for every so many of its citizens
		// (Civ3: one for two); 0 makes none.
		public int RazedCitizensPerSlave = 2;

		// Zone of control: a unit's free shot at one moving past hits with the
		// chance A / (this × D + A) (CivFanatics), taking this many hit points.
		public int ZoneOfControlDefenseFactor = 16;
		public int ZoneOfControlDamage = 1;

		// Nuclear meltdowns (see Pollution.CheckMeltdowns): the chance, in
		// percent, each turn that a building that can melt down (the Nuclear
		// Plant) does in a city in civil disorder at least this many turns
		// running (CivFanatics measured 10-15%, never on the turn the disorder
		// starts; Civ3's manual says 50%).
		public int MeltdownChancePercent = 12;
		public int MeltdownTurnsInDisorder = 2;

		// Disease (see Disease): the chance, in percent of the terrain's
		// disease strength (Civ3's TERR value, 50), each turn for each citizen
		// working an unhealthy tile that the city loses a citizen, and for a
		// fortified military unit out on one that it dies. Not documented.
		public int CityDiseaseChancePerStrength = 10;
		public int UnitDiseaseChancePerStrength = 2;

		// Volcanoes (see Volcanoes): each erupts on average once in this many
		// years (Civ3's GAME "eruption period"; 0 for never), most of the time
		// after smoking for up to this many turns; lava hits each tile around
		// it with this chance. The warning and hit chances aren't documented.
		public int VolcanoEruptionPeriod = 5000;
		public int VolcanoWarningPercent = 80;
		public int VolcanoWarningTurns = 3;
		public int VolcanoTileHitPercent = 50;

		// Global warming (see GlobalWarming): each polluted land tile beyond
		// this many per thousand land tiles is this chance, in percent, a turn
		// that warming changes this many tiles. Not documented.
		public int GlobalWarmingThresholdPerThousandLandTiles = 20;
		public int GlobalWarmingChancePerExcessTile = 1;
		public int GlobalWarmingTilesChanged = 1;

		// Plague (Civ3's GAME plague settings; off in the standard rules). See
		// Plague.
		public PlagueRules plague = new();

		// How the city governor values a tile (see CityGovernor): a food, a
		// shield and a commerce are each worth this many of the next, and
		// emphasis multiplies what's emphasized by the factor (TimBentley's
		// measurement: 4 and 4).
		public int GovernorValueRatio = 4;
		public int GovernorEmphasisFactor = 4;

		// Diplomatic and spy missions (Civ3's ESPN section), with Conquests'
		// base costs. See Embassies.
		public List<EspionageMission> EspionageMissions = EspionageMission.Conquests();

		public EspionageMission Mission(string key) {
			return EspionageMissions.Find(m => m.key == key);
		}

		// How civs feel about each other. See Attitude.
		public AttitudeRules attitude = new();

		// What goods in a deal are worth. See TradeOffer.
		public DealValueRules dealValues = new();

		// Spies and their missions. See Espionage.
		public EspionageRules espionage = new();

		// War weariness. See WarWeariness.
		public WarWearinessRules warWeariness = new();

		// Culture flipping (Firaxis's formula). See CultureFlip.
		public CultureFlipRules cultureFlip = new();

		// The civilization traits' bonuses that aren't in Civ3's BIQ.
		public TraitBonusRules traitBonuses = new();

		// What each victory condition takes. See the classes in Victory/.
		public VictoryRules victory = new();

		// Ships, submarines and coastal defenses. See Naval.
		public NavalRules naval = new();

		// Air missions, interception and anti-aircraft fire. See Air.
		public AirRules air = new();

		// Nuclear weapons. See Nuclear.
		public NuclearRules nuclear = new();

		// Colonies, outposts, airfields and radar towers. See Structures.
		public StructureRules structures = new();

		// Power on the histograph. See Power.
		public PowerRules power = new();

		// Old buildings produce more culture. See AgeBonusRules.
		public AgeBonusRules ageBonus = new();

		// The gold a great wonder that's a tourist attraction earns each turn
		// once it has stood for `years` (the Civilopedia's "Tourist Attraction"
		// table); the last step it has reached counts. Not in Civ3's BIQ.
		public List<TouristAttractionStep> TouristAttractionGold = [
			new() { years = 1000, gold = 2 },
			new() { years = 1501, gold = 4 },
			new() { years = 1751, gold = 6 },
			new() { years = 1876, gold = 8 },
			new() { years = 2001, gold = 10 },
			new() { years = 2251, gold = 12 },
			new() { years = 2501, gold = 14 },
		];
	}

	public class TouristAttractionStep {
		public int years;
		public int gold;
	}

	// One difficulty's goody hut chances, in percent. They needn't add up to
	// 100: the possible outcomes share the total (see GoodyHuts).
	public class GoodyHutOdds {
		public string difficulty;
		public double city;
		public double tech;
		public double gold;
		public double settler;
		public double map;
		public double mercenaries;
		public double nothing;
		public double barbarians;
	}

	public static class GoodyHutOddsRow {
		private static readonly string[] Difficulties = ["Chieftain", "Warlord", "Regent", "Monarch", "Emperor", "Demigod", "Deity", "Creator"];

		private static List<GoodyHutOdds> Table(double[][] rows) {
			List<GoodyHutOdds> table = [];
			for (int i = 0; i < rows.Length; ++i) {
				double[] r = rows[i];
				table.Add(new() {
					difficulty = Difficulties[i],
					city = r[0],
					tech = r[1],
					gold = r[2],
					settler = r[3],
					map = r[4],
					mercenaries = r[5],
					nothing = r[6],
					barbarians = r[7],
				});
			}
			return table;
		}

		// CivFanatics' "Probabilities of goody huts (C3C)": city, tech, gold,
		// settler, map, mercenaries, nothing, barbarians.
		public static List<GoodyHutOdds> Standard() => Table([
			[5, 35, 15, 15, 10, 10, 0, 10],
			[10, 25, 10, 10, 10, 10, 0, 25],
			[5, 20, 10, 10, 10, 10, 0, 35],
			[5, 15, 10, 10, 10, 10, 0, 40],
			[5, 10, 5, 5, 5, 5, 0, 65],
			[0, 5, 5, 5, 5, 5, 0, 75],
			[0, 0, 5, 0, 5, 5, 0, 85],
			[0, 0, 0, 0, 0, 0, 0, 90],
		]);

		// An Expansionist civ: the next easier level's, with no barbarians.
		public static List<GoodyHutOdds> Expansionist() => Table([
			[10, 35, 15, 15, 10, 10, 5, 0],
			[10, 35, 15, 15, 10, 10, 5, 0],
			[10, 25, 10, 10, 10, 10, 0, 0],
			[5, 20, 10, 10, 10, 10, 0, 0],
			[5, 15, 10, 10, 10, 10, 0, 0],
			[5, 10, 5, 5, 5, 5, 0, 0],
			[0, 5, 5, 5, 5, 5, 0, 0],
			[0, 0, 5, 0, 5, 5, 0, 0],
		]);
	}

	// A scenario's plague (see Plague): Civ3's GAME settings, the years as
	// calendar years.
	public class PlagueRules {
		public bool enabled = false;
		public string name = "Plague";
		public int earliestStartYear = -700;
		// The chance, in percent, each turn that an outbreak starts (Civ3's
		// "variation").
		public int chancePercent = 5;
		public int durationYears = 100;
		public int strength = 80;
		public int graceYears = 1000;
		public int maxOutbreaks = 3;
	}

	// How a civ feels about another (see Attitude; CivFanatics' "AI Attitude
	// Study", measured in Civ3's debug mode). Points add up to a score; good
	// things are negative. Not in Civ3's BIQ.
	public class AttitudeRules {
		// The score's upper end for each level, and its highest.
		public int graciousAtMost = -11;
		public int politeAtMost = -1;
		public int cautiousAtMost = 0;
		public int annoyedAtMost = 10;
		public int maximum = 100;

		// The civ's aggression (RACE, -2 to 2) plus this, by difficulty
		// (easiest first; the study's Chieftain-Warlord 0, Regent-Monarch 1,
		// Emperor-Deity 2), held between the two limits.
		public List<int> aggressionOffsetByDifficulty = [0, 0, 1, 1, 2, 2, 2, 2];
		public int aggressionMinimum = -1;
		public int aggressionMaximum = 3;

		// While they last.
		public int sameCultureGroup = -1;
		public int sameGovernment = -1;
		// Both in our favorite government; them in our shunned one, us not.
		public int favoriteGovernment = -5;
		public int shunnedGovernment = 4;
		// A trade in the last so many turns, or a per-turn deal running.
		public int recentTrade = -1;
		public int recentTradeTurns = 20;
		// We have more culture than them (they lose this much), or they more
		// than us (they gain it).
		public int cultureLead = 1;
		public int embassy = -2;
		public int rightOfPassage = -5;
		public int mutualProtection = -10;
		public int commonEnemy = -3;
		public int alliance = -2;
		public int resourceTrade = -5;
		public int tradeEmbargoAgainstUs = 10;
		// Gifts: a point for each so much gold they're worth, up to a
		// maximum; forgotten at so much gold a turn.
		public int goldPerGiftPoint = 10;
		public int maximumGiftPoints = 10;
		public int giftGoldForgottenPerTurn = 10;
		// While they have more military power than us, the good points count
		// this percent (rounded away from zero); right of passage in full.
		public int powerLeadGoodPercent = 50;

		// For good: on us, and on others at peace with the victim.
		public int declaredWarOnUs = 4;
		public int declaredWarOnFriend = 1;
		public int brokePeaceTreaty = 4;
		public int brokePeaceTreatyWithFriend = 1;
		public int brokeRightOfPassage = 4;
		public int brokeRightOfPassageWithFriend = 1;
		public int brokeRightOfPassageWithUnitsInside = 6;
		public int brokeRightOfPassageWithUnitsInsideWithFriend = 2;
		public int razedOurCity = 12;
		public int razedFriendsCity = 1;
		// Nuked us, or anyone ("a tremendous negative effect upon world
		// opinion", Civilopedia; not in the attitude study).
		public int nukedUs = 15;
		public int usedNukes = 6;
		// Made peace with the civ we were allied against, before the
		// alliance ended.
		public int brokeAlliance = 12;
		public int brokeAllianceWithFriend = 2;
		// One of our cities flipped to them and they took it, or refused it.
		public int tookOurFlippedCity = 1;
		public int refusedOurFlippedCity = -1;
		// They sold us one of their workers.
		public int soldUsWorkers = 1;
		// They demanded tribute of us (less if we were Polite or better), we
		// demanded it of them and they paid, or they refused (called our
		// bluff).
		public int demandedTribute = 2;
		public int demandedTributeWhileFriendly = 1;
		public int paidUsTribute = -1;
		public int calledOurBluff = 1;
		// While their troops stay in our land after we asked them to leave,
		// and after we ordered them home.
		public int askedToLeave = 1;
		public int orderedToLeave = 4;
		// They failed a spy mission against us: for good, and for a while
		// (a point fades every so many turns).
		public int failedEspionage = 1;
		public int failedEspionageForAWhile = 3;
		public int spyAngerFadeTurns = 5;
	}

	// Spies and their missions (see Espionage; CivFanatics' "A Study of
	// Espionage Missions"). Not in Civ3's BIQ.
	public class EspionageRules {
		// The percent chance that planting a spy works, by the spies'
		// experience (conscript, regular, veteran, elite); and a failure
		// flags the civ, which can't try again until the flag clears, one
		// chance in so many each turn.
		public List<int> plantSpyPercent = [20, 50, 60, 75];
		public int plantFailedClearsOneIn = 3;
		// The percent chance of getting away, by risk (immediate, carefully,
		// safely), through an embassy and by the spy's experience (conscript
		// and elite not measured); a mission that gets away works this percent
		// of the time. The safer ways cost this percent of the price.
		public List<int> embassyGetAwayPercent = [40, 55, 65];
		public List<List<int>> spyGetAwayPercent = [[50, 65, 75], [60, 75, 85], [70, 85, 95], [80, 95, 99]];
		public int successPercent = 80;
		public List<int> riskCostPercent = [100, 150, 200];
		// Propaganda persuades each citizen with the culture level's chance
		// and the governments' modifier, plus this for the spy's experience,
		// less this for each unit in the city, for a building resistant to
		// propaganda (the Courthouse) and for the capital, plus this for our
		// own nationals; the city defects when this percent of its people
		// are persuaded.
		public List<int> propagandaSpyBonus = [-10, 0, 10, 20];
		public int propagandaPerUnit = 5;
		public int propagandaResistantBuilding = 20;
		public int propagandaCapital = 40;
		public int propagandaOwnNationality = 20;
		public int propagandaFlipPercent = 75;
	}

	// What goods in a deal are worth, in gold (see TradeOffer). Civ3's values
	// aren't documented. Not in Civ3's BIQ.
	public class DealValueRules {
		// A tile of a map that's new to the receiver is worth a gold in so
		// many.
		public int mapTilesPerGold = 4;
		// Meeting a civ.
		public int contactGold = 50;
		// A resource the receiver lacks, for the deal's duration; giving away
		// one of several costs this percent of it.
		public int strategicResourceGold = 200;
		public int luxuryResourceGold = 150;
		public int spareResourcePercent = 10;
		// A city or a worker is worth its shields (a city's citizens and
		// buildings) at the rules' shield value in gold; giving away a city
		// costs this many times that.
		public int cityGiveFactor = 3;
	}

	// War weariness points and what they do (see WarWeariness; CivFanatics,
	// "How does war weariness work?"). Not in Civ3's BIQ.
	public class WarWearinessRules {
		// Each this many points, at war, is a level.
		public int pointsPerLevel = 30;

		// The points each event adds (or, if negative, takes away).
		public int declaredWarOn = -30;
		public int unitsInEnemyLandPerTurn = 1;
		public int noFightingPerTurn = -1;
		public int unitThatCantDefendLost = 1;
		public int improvementDestroyed = 1;
		public int bombardedDownToOneHitPoint = 1;
		public int attackerKilled = 2;
		public int unitAttacked = 2;
		public int townOfSizeOneLost = 16;
		public int cityLost = 17;

		// In peace, the points divided by this, rounded up, wear away each turn.
		public int peaceDecayDivisor = 20;

		// The percent of each city's citizens made unhappy at each level (0,
		// 1, 2...; the last for any higher level), under a government with low
		// and with high war weariness.
		public List<int> lowUnhappyPercent = [0, 25, 50, 50, 100];
		public List<int> highUnhappyPercent = [0, 50, 100, 100, 100];

		// The level against any enemy that overthrows a high war weariness
		// government.
		public int revoltLevel = 3;

		// The percent of each city's citizens made happy while the points are
		// below zero.
		public int happyPercent = 25;

		// A Police Station takes this percent of its city's war weariness
		// away, and Universal Suffrage this many unhappy citizens in every city.
		public int policeStationReductionPercent = 25;
		public int allCitiesReduction = 1;
	}

	// Culture flipping's chance (see CultureFlip): ((foreign citizens +
	// their tiles) × local culture × mood × culture ratio − garrison) ÷
	// (divisor × distance ratio).
	public class CultureFlipRules {
		public double divisor = 2000;
		// Their capital's distance over ours, held between these.
		public double minDistanceRatio = .25;
		public double maxDistanceRatio = 4;
		// When their culture in the city beats its owner's.
		public double localCultureFactor = 2;
		public double celebratingFactor = .5;
		public double disorderFactor = 2;
	}

	// Air units (see Air): Civ3's RULE interception chances; the rest isn't
	// in the BIQ.
	public class AirRules {
		// The chance, in percent, that a unit on air superiority duty
		// intercepts an enemy mission within half its operational range
		// (Civilopedia, Civ3 manual), and a stealth one (RULE: 50 and 5).
		public int interceptPercent = 50;
		public int stealthInterceptPercent = 5;
		// Anti-aircraft fire: the strongest air defense at the target (a
		// unit's, or a city's air power: the SAM Missile Battery's 8) shoots
		// once, downing the aircraft with this many percent per point
		// (players measured SAMs at about 40%), and a stealth one with a
		// tenth of that (as interception).
		public int antiAirPercentPerPoint = 5;
		// A recon mission reveals the tiles this far around its target.
		public int reconRadius = 2;
		// Re-basing reaches this many times the operational range
		// (Civilopedia: "up to Six times").
		public int rebaseRangeFactor = 6;
	}

	// Nuclear strikes (see Nuclear). Civ3 documents none of these.
	public class NuclearRules {
		// A missile defense (the Strategic Missile Defense) shoots down this
		// percent of the nukes aimed at its owner's cities (players: 3/4).
		public int missileDefensePercent = 75;
		// The target city loses this percent of its people; each of its
		// buildings (not wonders) is destroyed with this chance; each land
		// tile of the blast gets fallout with this chance.
		public int populationKilledPercent = 50;
		public int buildingDestroyedPercent = 50;
		public int falloutPercent = 50;
	}

	// Structures a worker builds (see Structures), from the Civilopedia.
	public class StructureRules {
		// "The range of vision for the Outpost is 2 tiles on flat terrain, 3
		// on hills and 4 on mountains."
		public int outpostVision = 2;
		public int outpostVisionOnHills = 3;
		public int outpostVisionOnMountains = 4;
		// Colonies and radar towers see this far on any terrain (the tiles
		// Civ3's saves mark as seen by structures).
		public int colonyVision = 1;
		public int radarVision = 2;
		// Radar towers: "Friendly units within the Radar Towers range get a
		// +25% attack and defense bonus", within 2 tiles, once however many
		// towers there are.
		public int radarRange = 2;
		public int radarBonusPercent = 25;
	}

	// Civ3's histograph: "Power is an amalgam of cities, gold, culture,
	// advances, resources, military strength, nuclear weapons, and
	// wonders." It doesn't say how they're weighed; these are the points
	// each gives (see Power).
	public class PowerRules {
		public int pointsPerCity = 10;
		public int goldPerPoint = 10;
		public int culturePerPoint = 10;
		public int pointsPerAdvance = 5;
		// For each luxury or strategic resource the capital has.
		public int pointsPerResource = 10;
		// For each point of attack and defense of the military units.
		public int pointsPerStrength = 1;
		public int pointsPerNuke = 50;
		// For each great wonder in the civ's cities.
		public int pointsPerWonder = 25;
	}

	// Naval rules not in Civ3's BIQ.
	public class NavalRules {
		// The chance, in percent, that a ship ending its turn where it may
		// sink does, and for a Seafaring civ (CivFanatics' measurements on
		// Conquests: about 50% and 75% survive, whatever the ship, terrain or
		// difficulty).
		public int sinkPercent = 50;
		public int seafaringSinkPercent = 25;
	}

	// The victory conditions' limits: Civ3's GAME "game limits" where the
	// BIQ has them (Conquests' values), the Civilopedia's otherwise.
	public class VictoryRules {
		// Domination: this percent of the world's land within our borders
		// and of its people in our cities.
		public int dominationTerrainPercent = 66;
		public int dominationPopulationPercent = 66;

		// Culture: a city with this many culture points, or a civ with this
		// many, scaled by the map size (Civilopedia "Victory Conditions":
		// 60,000 on a Tiny map to 160,000 on a Huge one; not in the BIQ),
		// and this many times the culture of every other civ.
		public int cultureCityPoints = 20000;
		public int cultureCivPoints = 100000;
		public List<int> cultureCivMapSizePercent = [60, 80, 100, 130, 160];
		public int cultureCivLead = 2;

		// The United Nations: its owner may call an election every this many
		// turns (CivFanatics' Civ3 FAQ: 11); a civ with this percent of the
		// world's land or people is a candidate too, up to this many
		// candidates (Civilopedia). Not in Civ3's BIQ.
		public int electionEveryTurns = 11;
		public int candidatePercent = 25;
		public int maxCandidates = 3;

		// The space race: how many of each spaceship part (BLDG spaceship
		// part index) the ship needs (Civ3's RULE section).
		public List<int> spaceshipParts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1];

		// Conquests' game types (GAME "game limits"). Victory points: the
		// limit that wins; a great wonder, a unit killed (half for a
		// barbarian) and a conquered city earn its shield cost ÷ 10, its shield
		// cost ÷ 10 and its citizens times these; an advance its editor cost
		// times this; holding a victory location this many a turn; bringing a
		// princess home this many and this much gold (and she reappears).
		public int victoryPointLimit = 50000;
		public int wonderPointsMultiple = 10;
		public int unitKillPointsMultiple = 5;
		public int advancePointsMultiple = 5;
		public int cityConquestPointsMultiple = 100;
		public int victoryLocationPoints = 25;
		public int princessPoints = 1000;
		public int princessGold = 0;
		public bool respawnPrincess = true;
		// Elimination: losing this many cities to enemies loses the game.
		public int cityEliminationCount = 1;
		// Mass regicide: kings each civ starts with (CivFanatics' Civ3 FAQ;
		// not in the BIQ).
		public int massRegicideKings = 7;
	}

	// Trait bonuses Civ3 builds in (Civilopedia, CivWiki; checked against
	// the user's Civ3 saves, see Tile_Yield).
	public class TraitBonusRules {
		// Agricultural: food on the city tile (which despotism takes back
		// unless the city has fresh water or is past town size) and on
		// irrigated desert.
		public int agriculturalCityFood = 1;
		public int agriculturalIrrigatedDesertFood = 1;
		// Commercial: commerce on the tile of a city and of a metropolis.
		public int commercialCityCommerce = 2;
		public int commercialMetropolisCommerce = 3;
		// Seafaring: commerce on a coastal city's tile, and ship moves.
		public int seafaringCoastalCityCommerce = 1;
		public int seafaringShipMoves = 1;
		// Industrious: shields on a metropolis's tile.
		public int industriousMetropolisShields = 1;
	}
}

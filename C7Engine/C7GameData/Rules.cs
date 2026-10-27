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
		// When the capital is lost, how far (in tiles of walking distance) another
		// city counts as a neighbor of a candidate new capital. Civ3: 8, a 17x17
		// square. Civ3's BIQ has no such value, so Civ3 saves get this default.
		public int CapitalNeighborDistance = 8;
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
		public float TreasuryInterestRate = .05f;
		public int MaxInterest = 50;
		public int ShieldCostPerGold;
		public float ShieldRateForDisbanding; // per cent
		public bool AllowLesserUnitProduction; // for example, allow building a Spearman/Pikeman when we can build a Musketman (simultaneously)
		public int RadarTileVisibility; // how many tiles, a unit with the Radar ability, can see ahead

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
}

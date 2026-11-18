namespace C7GameData {
	// https://forums.civfanatics.com/threads/ai-difficulty-level-bonuses.37490/
	public class Difficulty {
		public ID id;
		public string Name;
		public int NumberOfCitizensBornContent;

		// Only applies to AI; at higher difficulty levels the AI can change
		// governments much faster.
		public int MaxAiGovernmentTransitionTime;

		// If non-zero, the AI gets this number of the best offensive/defensive
		// units the AI can build at the start of the game.
		public int NumberOfAIDefensiveStartingUnits;
		public int NumberOfAIOffensiveStartingUnits;

		// Usually the number of extra settlers
		public int ExtraStartUnit1;

		// Usually the number of extra workers
		public int ExtraStartUnit2;

		// Bonuses for unit support.
		public int AdditionalFreeUnitSupport;
		public int UnitSupportBonusForEachSettlement;

		// A percentage strength bonus for human players' units attacking
		// barbarians (see MapUnit.ListStrengthBonusesVersus): 800, 400, 200,
		// 100, 50 and 0 from Chieftain to Deity
		// (https://forums.civfanatics.com/threads/ai-difficulty-level-bonuses.37490/).
		public int AttackBonusAgainstBarbarians;

		// The cost factor for techs, growth, and production, 10 is a neutral
		// value.
		public int AiCostFactor;
		public int HumanCostFactor = 10;

		public int PercentageOfOptimalCities;

		public int AIToAITradeRate;

		// The percentage of normal corruption and waste a player at this
		// level suffers (the BIQ's corruption percentage, Civ3's editor's
		// Corruption Modifier; 100 at every level in the standard rules).
		// See City.DifficultyCorruptionFactor.
		public int CorruptionPercentage = 100;

		// Number of citizens quelled by military: how many resisters each
		// unit in a conquered city can quell a turn, "the number of military
		// units in the city times the difficulty level's number of citizens
		// quelled by military" (see City.UpdateResistance and
		// https://civfanatics.com/civ3/strategy/game-mechanics/the-inner-workings-of-resistance-revealed/).
		// It appears to always be 1 in the default game and scenarios, but
		// it is moddable.
		public int MilitaryLaw = 1;
	}
}

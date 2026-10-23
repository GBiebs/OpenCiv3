namespace C7GameData {
	// How one civ's people see another civ's culture: Civ3's culture levels,
	// from "in awe of" to "disdainful of". Which level applies depends on how
	// the other civ's total culture compares to their own, and it sets how
	// likely their citizens are to resist when the other civ captures their
	// city (and how well the other civ's propaganda works on them).
	public class CultureOpinion {
		// Like "in awe of".
		public string name;

		// This level applies when the other civ has at least this percentage of
		// their culture (300 means three times as much).
		public int cultureRatioPercent;

		// The percent chance that each citizen resists when the other civ
		// captures their city.
		public int initialResistanceChance;

		// The percent chance that each resister keeps resisting each turn.
		public int continuedResistanceChance;

		// The percent chance that the other civ's propaganda succeeds.
		public int propagandaChance;
	}
}

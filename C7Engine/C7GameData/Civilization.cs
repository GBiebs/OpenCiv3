using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace C7GameData {
	/**
	 * Represents a civilization, such as the French, which can be
	 * assigned to a player.
	 */
	public enum Gender {
		Male,
		Female,
	}

	public class CultureGroup() {
		public int index { get; init; }
		public string name { get; init; }
	}

	public class Civilization {
		public enum Trait {
			Militaristic,
			Commercial,
			Expansionist,
			Scientific,
			Religious,
			Industrious,
			Agricultural,
			Seafaring,
		}

		public Civilization() { }

		public Civilization(string name) {
			this.name = name;
		}
		public string name;

		// `noun` is "Americans" for "America", or "Spanish" for "Spain", etc.
		public string noun;
		// `adjective` is "American" for "America", or "Celtic" for "Celts", etc.
		public string adjective;
		public string leader;
		// The leader's title from the BIQ (RACE's leader title), or null in
		// saves from before it was imported.
		public string leaderTitle;
		public int primaryColorIndex;
		public int secondaryColorIndex;
		public Gender leaderGender;

		[JsonIgnore]
		public CultureGroup cultureGroup { get; private set; }

		// This is null during gameplay to discourage usage, and instead use the cultureGroup
		// It's only used for saving/loading
		public string cultureGroupKey;

		public void SetCultureGroup(int index, string cultureGroupName) {
			cultureGroupKey = null;
			var cg = new CultureGroup {
				index = index,
				name = cultureGroupName
			};
			cultureGroup = cg;
		}

		// Like `art\advisors\LZ_all.pcx` for the English.
		public string leaderArtFile;

		// The civilization's key in the Civilopedia, like RACE_Romans, or
		// null if the rules don't say.
		public string civilopediaEntry;

		// The name of the civ's King unit (Abraham Lincoln for the
		// Americans), given to it under Regicide and Mass Regicide, or null
		// if it has none. See InstantElimination.
		public string kingUnit;

		public List<string> cityNames = new List<string>();

		// Kept for scenarios; the game doesn't use them yet: the BIQ RACE's
		// great leaders and scientific leaders (the game names leaders from
		// the eras' researchers), whether the name is plural and its
		// grammatical gender, the diplomacy text index, and the city
		// governor's settings for the AI.
		public List<string> greatLeaderNames = new();
		public List<string> scientificLeaderNames = new();
		public bool pluralName = true;
		public Gender nameGender;
		public int diplomacyTextIndex;
		public bool governorManagesCitizens, governorEmphasizesFood, governorEmphasizesShields, governorEmphasizesTrade,
			governorManagesProduction, governorNoWonders, governorNoSmallWonders;

		// The IDs of all the techs that this civ starts with.
		public HashSet<ID> startingTechs = new();

		// The traits that this civilization has.
		public HashSet<Trait> traits = new();

		public bool isBarbarian = false;

		// Kings & Nomads' independent city-states, a player of their own,
		// made at setup (see GameSetup.KingsAndNomads.cs).
		public bool isCityStates = false;

		// Not a civilization one plays: the barbarians or the city-states.
		[System.Text.Json.Serialization.JsonIgnore]
		public bool IsIndependent => isBarbarian || isCityStates;

		// How aggressive the civ's AI is, from -2 (the least) to 2 (the
		// most), as in Civ3's editor. It sets the AI's starting attitude (see
		// Attitudes).
		public int aggressionLevel = 0;

		// The names of the governments the civ's AI favours and shuns, or
		// null for none. They change its attitude (see Attitudes).
		public string favoriteGovernment;
		public string shunnedGovernment;

		// What the BIQ's RACE section says the civ's AI builds often and
		// never (doc/civ3-compendium/08, section 3.2). Explore has no
		// PlanFocus, so it is only counted in buildFlagCount, the number of
		// flags set in all (Civ3 ignores them all past a limit; see
		// PlanEffects.FlagAdjustment). Empty in saves from before.
		public HashSet<PlanFocus> buildOften = new();
		public HashSet<PlanFocus> buildNever = new();
		public int buildFlagCount;

		public class SettlerTileAdjustments {
			public int DistancePenaltyRadius = 4;

			// Each rival city within this many tiles of a site lowers its score.
			public int RivalCityRadius = 4;
			public float RivalCityPenalty = -25;

			// TODO: Eventually, there should be different weights based on whether the AI already
			// has the resource or not (more important to secure ones that they don't have).
			// But since we don't have trade networks yet, for now there's only one value.

			// multipliers
			public float CommerceYieldBonus = 2;
			public float DistancePenalty = -2;
			public float FoodYieldBonus = 5;
			public float ProductionYieldBonus = 3;

			// constants
			public float HillsBonus = 10;
			public float LuxuryResourceBonus = 15;
			public float StrategicResourceBonus = 20;
			public float WaterBonus = 10;
		}

		public SettlerTileAdjustments Adjustments = new();
	}
}

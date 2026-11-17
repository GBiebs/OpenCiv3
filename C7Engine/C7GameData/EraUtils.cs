namespace C7GameData;

public static class EraUtils {

	// The civilopedia (CVLPD) version for each of the era names
	public const string ANCIENT_TIMES_CVLPD = "ERAS_Ancient_Times";
	public const string MIDDLE_AGES_CVLPD = "ERAS_Middle_Ages";
	public const string INDUSTRIAL_AGE_CVLPD = "ERAS_Industrial_Age";
	public const string MODERN_ERA_CVLPD = "ERAS_Modern_Era";

	public const string ANCIENT_TIMES = "Ancient Times";
	public const string MIDDLE_AGES = "Middle Ages";
	public const string INDUSTRIAL_AGE = "Industrial Age";
	public const string MODERN_ERA = "Modern Era";

	// The era of an advance the BIQ gives no era (-1, "None" in Civ3's
	// editor). Such advances "can only be assigned; they cannot be
	// researched, or traded, or "discovered" by the Great Library"
	// (https://forums.civfanatics.com/threads/preventing-advancement-to-next-age.244311/):
	// MDJ's clans' own advances and its Place-Holder Tech.
	public const string HIDDEN_CVLPD = "Hidden";

	public static int GetEraIndex(string era) {
		if (era == ANCIENT_TIMES_CVLPD) {
			return 0;
		} else if (era == MIDDLE_AGES_CVLPD) {
			return 1;
		} else if (era == INDUSTRIAL_AGE_CVLPD) {
			return 2;
		} else if (era == MODERN_ERA_CVLPD) {
			return 3;
		}
		return -1;
	}

	public static string EraIndexToEra(int index) {
		if (index <= 0) {
			return ANCIENT_TIMES_CVLPD;
		} else if (index == 1) {
			return MIDDLE_AGES_CVLPD;
		} else if (index == 2) {
			return INDUSTRIAL_AGE_CVLPD;
		} else {
			return MODERN_ERA_CVLPD;
		}
	}

	public static string GetNiceEraName(string era) {
		// The rules' name for it (a scenario may rename an era).
		if (era != null && C7Engine.EngineStorage.gameData?.rules?.EraNames?.TryGetValue(era, out string named) == true
				&& !string.IsNullOrWhiteSpace(named)) {
			return named;
		}
		if (era == ANCIENT_TIMES_CVLPD) {
			return ANCIENT_TIMES;
		} else if (era == MIDDLE_AGES_CVLPD) {
			return MIDDLE_AGES;
		} else if (era == INDUSTRIAL_AGE_CVLPD) {
			return INDUSTRIAL_AGE;
		} else if (era == MODERN_ERA_CVLPD) {
			return MODERN_ERA;
		}
		return $"Not A Valid Era: {era}";
	}

	public static string GetNextEraNiceName(string era) {
		if (era == ANCIENT_TIMES_CVLPD) {
			return MIDDLE_AGES;
		} else if (era == MIDDLE_AGES_CVLPD) {
			return INDUSTRIAL_AGE;
		}
		return MODERN_ERA;
	}

	public static string GetPreviousEraNiceName(string era) {
		if (era == MODERN_ERA_CVLPD) {
			return INDUSTRIAL_AGE;
		} else if (era == INDUSTRIAL_AGE_CVLPD) {
			return MIDDLE_AGES;
		}
		return ANCIENT_TIMES;
	}

	public static string GetNextEraNameByIndex(int index) {
		return EraIndexToEra(index + 1);
	}
	public static string GetPreviousEraNameByIndex(int index) {
		return EraIndexToEra(index - 1);
	}
}

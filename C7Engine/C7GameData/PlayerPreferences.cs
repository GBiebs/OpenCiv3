namespace C7GameData {
	// The preferences of Civ3's that change what the engine does for a
	// human (the same names as the game's Civ3Preferences, which sends them
	// with MsgSetPreferences; each human's own, kept in the save).
	public static class PlayerPreferences {
		public const string AlwaysBuildPreviousUnit = nameof(AlwaysBuildPreviousUnit);
		public const string AskBuildOrdersAfterUnit = nameof(AskBuildOrdersAfterUnit);
		public const string CapitalGovernorDefault = nameof(CapitalGovernorDefault);
		public const string CancelOrdersForFriendlyCombatUnit = nameof(CancelOrdersForFriendlyCombatUnit);
		public const string CancelOrdersForEnemyCombatUnit = nameof(CancelOrdersForEnemyCombatUnit);

		// Civ3's defaults for a player who hasn't said.
		public static bool Prefers(this Player player, string key) {
			if (player.preferences.TryGetValue(key, out bool on)) {
				return on;
			}
			return key is AlwaysBuildPreviousUnit or CancelOrdersForEnemyCombatUnit;
		}
	}
}

using System.Collections.Generic;
using System.Linq;
using C7Engine;

namespace C7.UIElements;

// Civ3's preferences (its Ctrl-P screen), kept in C7.ini's Preferences
// section like the others. Each has a key, its label on the page, its
// section, what it starts as, and, for those the game can't act on yet, why
// not (shown greyed out). Those the engine needs (the player's own
// choices, as each human may differ) reach it with MsgSetPreferences.
public static class Civ3Preferences {
	public record Option(string key, string label, string section, bool defaultOn, string unavailable = null, bool engine = false);

	public const string Game = "game", Units = "units", Sound = "sound";

	public const string AlwaysWaitAtEndOfTurn = nameof(AlwaysWaitAtEndOfTurn);
	public const string CapitalGovernorDefault = nameof(CapitalGovernorDefault);
	public const string ShowWonderInitiation = nameof(ShowWonderInitiation);
	public const string ShowCivilDisorder = nameof(ShowCivilDisorder);
	public const string ShowFoodAndShieldsOnMap = nameof(ShowFoodAndShieldsOnMap);
	public const string ShowCityPopShadow = nameof(ShowCityPopShadow);
	public const string AskBuildOrdersAfterUnit = nameof(AskBuildOrdersAfterUnit);
	public const string DisablePopulationLimitWarnings = nameof(DisablePopulationLimitWarnings);
	public const string AlwaysRenegotiateDeals = nameof(AlwaysRenegotiateDeals);
	public const string AlwaysBuildPreviousUnit = nameof(AlwaysBuildPreviousUnit);
	public const string NoAutoSelectOnline = nameof(NoAutoSelectOnline);
	public const string ShowAdvancedUnitButtons = nameof(ShowAdvancedUnitButtons);
	public const string ShowOurManualMoves = nameof(ShowOurManualMoves);
	public const string ShowOurAutomaticMoves = nameof(ShowOurAutomaticMoves);
	public const string ShowFriendMoves = nameof(ShowFriendMoves);
	public const string ShowEnemyMoves = nameof(ShowEnemyMoves);
	public const string ShowUnitsOverCities = nameof(ShowUnitsOverCities);
	public const string ShowTeamColorDisc = nameof(ShowTeamColorDisc);
	public const string AnimateBattles = nameof(AnimateBattles);
	public const string AnimateOurManualMoves = nameof(AnimateOurManualMoves);
	public const string AnimateOurAutomaticMoves = nameof(AnimateOurAutomaticMoves);
	public const string AnimateFriendMoves = nameof(AnimateFriendMoves);
	public const string AnimateEnemyMoves = nameof(AnimateEnemyMoves);
	public const string CancelOrdersForFriendlyCombatUnit = nameof(CancelOrdersForFriendlyCombatUnit);
	public const string CancelOrdersForEnemyCombatUnit = nameof(CancelOrdersForEnemyCombatUnit);
	public const string AllSoundsOn = nameof(AllSoundsOn);
	public const string MusicOn = nameof(MusicOn);
	public const string SoundFxOn = nameof(SoundFxOn);

	public static readonly Option[] All = [
		new(AlwaysWaitAtEndOfTurn, "Always Wait at End of Turn", Game, true),
		new(CapitalGovernorDefault, "Capital Governor is Default for New Cities", Game, false, engine: true),
		new(ShowWonderInitiation, "Show Wonder Initiation Pop-Up", Game, true),
		new(ShowCivilDisorder, "Show Civil Disorder Pop-Up", Game, true),
		new(ShowFoodAndShieldsOnMap, "Show Food and Shields on Map", Game, false),
		new(ShowCityPopShadow, "Show City Pop. Dropshadow", Game, true),
		new(AskBuildOrdersAfterUnit, "Ask for Build Orders after Unit Construction", Game, false, engine: true),
		new(AlwaysBuildPreviousUnit, "Always Build Previously Built Unit", Game, true, engine: true),
		new(DisablePopulationLimitWarnings, "Disable Population Limit Warnings", Game, false),
		new(AlwaysRenegotiateDeals, "Always Renegotiate Deals", Game, false),
		new("ShowCivilopediaCursor", "Show Civilopedia Book Cursor", Game, false, "There's no Civilopedia yet."),
		new("TutorialMode", "Tutorial Mode", Game, false, "There's no tutorial yet."),
		new("ColorBlindHelp", "Color Blind Help", Game, false, "Not available yet."),
		new("ShowFewerMultiplayerPopups", "Show Fewer Multiplayer Popups", Game, false, "Comes with the next multiplayer work."),
		new(NoAutoSelectOnline, "Do not auto-select units in Multiplayer", Game, false),
		new(ShowAdvancedUnitButtons, "Show Advanced Unit Action Buttons", Units, true),
		new(ShowOurManualMoves, "Show our Manual Moves", Units, true),
		new(ShowOurAutomaticMoves, "Show our Automatic Moves", Units, true),
		new(ShowFriendMoves, "Show Friend Moves", Units, true),
		new(ShowEnemyMoves, "Show Enemy Moves", Units, true),
		new(ShowUnitsOverCities, "Show Units over Cities", Units, true),
		new(ShowTeamColorDisc, "Show Team Color Disc", Units, false),
		new(AnimateBattles, "Animate Battles", Units, true),
		new(AnimateOurManualMoves, "Animate our Manual Moves", Units, true),
		new(AnimateOurAutomaticMoves, "Animate our Automatic Moves", Units, true),
		new(AnimateFriendMoves, "Animate Friend Moves", Units, true),
		new(AnimateEnemyMoves, "Animate Enemy Moves", Units, true),
		new(CancelOrdersForFriendlyCombatUnit, "Cancel Orders for Friendly Combat Unit", Units, false, engine: true),
		new(CancelOrdersForEnemyCombatUnit, "Cancel Orders for Enemy Combat Unit", Units, true, engine: true),
		new(AllSoundsOn, "All Sounds On", Sound, true),
		new(MusicOn, "Music On", Sound, true),
		new(SoundFxOn, "Sound FX On", Sound, true),
	];

	private static readonly Dictionary<string, Option> byKey = All.ToDictionary(o => o.key);

	// The two older animation settings (ours, everyone else's) seed the new
	// ones they split into.
	private static bool DefaultFor(Option option) {
		return option.key switch {
			AnimateOurManualMoves or AnimateOurAutomaticMoves => PreferencesSettings.GetAnimateFriendlyUnits(),
			AnimateFriendMoves or AnimateEnemyMoves => PreferencesSettings.GetAnimateEnemyUnits(),
			_ => option.defaultOn,
		};
	}

	public static bool Get(string key) {
		Option option = byKey[key];
		return option.unavailable == null && C7Settings.GetBoolOrDefault(PreferencesSettings.SectionName, key, DefaultFor(option));
	}

	public static void Set(string key, bool value) {
		C7Settings.SetBool(PreferencesSettings.SectionName, key, value);
		C7Settings.SaveSettings();
	}

	// The map's scroll speed, in percent of the usual (25 to 200).
	public const string MapScrollSpeed = nameof(MapScrollSpeed);

	public static int GetMapScrollSpeed() {
		return int.TryParse(C7Settings.GetSettingsValueOrDefault(PreferencesSettings.SectionName, MapScrollSpeed, "100"), out int speed)
			? System.Math.Clamp(speed, 25, 200) : 100;
	}

	public static void SetMapScrollSpeed(int percent) {
		C7Settings.SetValue(PreferencesSettings.SectionName, MapScrollSpeed, System.Math.Clamp(percent, 25, 200).ToString());
		C7Settings.SaveSettings();
	}

	// What the engine is told (MsgSetPreferences): "key=1;key=0".
	public static string ForEngine() {
		return string.Join(";", All.Where(o => o.engine).Select(o => $"{o.key}={(Get(o.key) ? 1 : 0)}"));
	}
}

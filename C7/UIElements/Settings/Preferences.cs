using System;
using System.Collections.Generic;
using C7Engine;

// Civ3's preferences (the Preferences popup in the game, Ctrl-P), saved to
// C7.ini. They're this machine's choices about what the game shows and plays,
// never its rules, so each player in a network game can have their own.
public static class Preferences {
	public const string SettingsSection = "preferences";

	public enum Option {
		AutoSave,
		CapitalGovernorIsDefaultForNewCities,
		AlwaysWaitAtEndOfTurn,
		AskForBuildOrdersAfterUnitConstruction,
		ShowAdvancedUnitActionButtons,
		ShowOurManualMoves,
		ShowOurAutomaticMoves,
		ShowFriendMoves,
		ShowEnemyMoves,
		AnimateBattles,
		AnimateOurManualMoves,
		AnimateOurAutomaticMoves,
		AnimateFriendMoves,
		AnimateEnemyMoves,
		AllSoundsOn,
		MusicOn,
		SoundFxOn,
	}

	// What each is until the player changes it. These keep the game as it
	// was before it had preferences: it waits at the end of the turn, asks
	// what to build next, and doesn't move the map to units or animate them
	// (hold Shift to see the animations).
	private static readonly Dictionary<Option, bool> defaults = new() {
		[Option.AutoSave] = false,
		[Option.CapitalGovernorIsDefaultForNewCities] = false,
		[Option.AlwaysWaitAtEndOfTurn] = true,
		[Option.AskForBuildOrdersAfterUnitConstruction] = true,
		// Civ3's worker automation buttons (Road to and the rest) take a
		// row of their own, so they're off until asked for.
		[Option.ShowAdvancedUnitActionButtons] = false,
		[Option.ShowOurManualMoves] = false,
		[Option.ShowOurAutomaticMoves] = false,
		[Option.ShowFriendMoves] = false,
		[Option.ShowEnemyMoves] = false,
		[Option.AnimateBattles] = false,
		[Option.AnimateOurManualMoves] = false,
		[Option.AnimateOurAutomaticMoves] = false,
		[Option.AnimateFriendMoves] = false,
		[Option.AnimateEnemyMoves] = false,
		[Option.AllSoundsOn] = true,
		[Option.MusicOn] = true,
		[Option.SoundFxOn] = true,
	};

	// Read once from C7.ini, as the animation checks ask for them often.
	private static readonly Dictionary<Option, bool> values = new();

	// Told of each preference the player changes, so it takes effect at once.
	public static event Action<Option> Changed;

	public static bool Get(Option option) {
		if (!values.TryGetValue(option, out bool value)) {
			string saved = C7Settings.GetSettingsValueOrDefault(SettingsSection, option.ToString(), null);
			value = saved switch {
				"true" => true,
				"false" => false,
				_ => defaults[option],
			};
			values[option] = value;
		}
		return value;
	}

	public static void Set(Option option, bool value) {
		if (Get(option) == value) {
			return;
		}
		values[option] = value;
		C7Settings.SetValue(SettingsSection, option.ToString(), value ? "true" : "false");
		C7Settings.SaveSettings();
		Changed?.Invoke(option);
	}

	public static bool AutoSave => Get(Option.AutoSave);
	public static bool CapitalGovernorIsDefaultForNewCities => Get(Option.CapitalGovernorIsDefaultForNewCities);
	public static bool AlwaysWaitAtEndOfTurn => Get(Option.AlwaysWaitAtEndOfTurn);
	public static bool AskForBuildOrdersAfterUnitConstruction => Get(Option.AskForBuildOrdersAfterUnitConstruction);
	public static bool AnimateBattles => Get(Option.AnimateBattles);
	public static bool AllSoundsOn => Get(Option.AllSoundsOn);
	public static bool MusicOn => Get(Option.MusicOn);
	public static bool SoundFxOn => Get(Option.SoundFxOn);

	// Whose moves the map shows and animates.
	public enum Mover { OurManual, OurAutomatic, Friend, Enemy }

	public static bool ShowMoves(Mover mover) => Get(mover switch {
		Mover.OurManual => Option.ShowOurManualMoves,
		Mover.OurAutomatic => Option.ShowOurAutomaticMoves,
		Mover.Friend => Option.ShowFriendMoves,
		_ => Option.ShowEnemyMoves,
	});

	public static bool AnimateMoves(Mover mover) => Get(mover switch {
		Mover.OurManual => Option.AnimateOurManualMoves,
		Mover.OurAutomatic => Option.AnimateOurAutomaticMoves,
		Mover.Friend => Option.AnimateFriendMoves,
		_ => Option.AnimateEnemyMoves,
	});

	// Whether the map shows anything of units moving or fighting at all.
	public static bool AnyMovesShown() {
		if (AnimateBattles) {
			return true;
		}
		foreach (Mover mover in Enum.GetValues<Mover>()) {
			if (ShowMoves(mover) || AnimateMoves(mover)) {
				return true;
			}
		}
		return false;
	}
}

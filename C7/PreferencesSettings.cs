using System.Linq;
using C7Engine;
using Godot;

namespace C7.UIElements;

/// <summary>
/// User preferences, stored outside the save file so they stay stable across
/// games and scenarios. Surfaced by the Preferences scene, reachable from the
/// main menu, the in-game GameMenu, and Ctrl-P.
/// </summary>
public static class PreferencesSettings {
	public const string SectionName = "Preferences";
	public const string PromptForResearch = nameof(PromptForResearch);
	public const bool DefaultPromptForResearch = true;
	public const string ShowGrid = nameof(ShowGrid);
	public const bool DefaultShowGrid = false;
	public const string Fullscreen = nameof(Fullscreen);
	public const bool DefaultFullscreen = true;
	public const string AnimateFriendlyUnits = nameof(AnimateFriendlyUnits);
	public const string AnimateEnemyUnits = nameof(AnimateEnemyUnits);

	public static bool GetPromptForResearch() {
		return C7Settings.GetBoolOrDefault(SectionName, PromptForResearch, DefaultPromptForResearch);
	}

	public static void SetPromptForResearch(bool value) {
		C7Settings.SetBool(SectionName, PromptForResearch, value);
		C7Settings.SaveSettings();
	}

	public static bool GetFullscreen() {
		return C7Settings.GetBoolOrDefault(SectionName, Fullscreen, DefaultFullscreen);
	}

	public static void SetFullscreen(bool value) {
		C7Settings.SetBool(SectionName, Fullscreen, value);
		C7Settings.SaveSettings();
		ApplyWindowMode();
	}

	// Full screen unless the player chose a window, or the command line
	// asks for one after "--" (`-- --windowed`, as the screenshot tools do;
	// Godot keeps its own --windowed from the game).
	public static void ApplyWindowMode() {
		bool windowed = !GetFullscreen() || OS.GetCmdlineUserArgs().Contains("--windowed");
		DisplayServer.WindowSetMode(windowed ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
	}

	// Whether our own units' moves and fights are animated, and every other
	// civ's (barbarians too).
	public static bool GetAnimateFriendlyUnits() {
		return C7Settings.GetBoolOrDefault(SectionName, AnimateFriendlyUnits, true);
	}

	public static void SetAnimateFriendlyUnits(bool value) {
		C7Settings.SetBool(SectionName, AnimateFriendlyUnits, value);
		C7Settings.SaveSettings();
	}

	public static bool GetAnimateEnemyUnits() {
		return C7Settings.GetBoolOrDefault(SectionName, AnimateEnemyUnits, true);
	}

	public static void SetAnimateEnemyUnits(bool value) {
		C7Settings.SetBool(SectionName, AnimateEnemyUnits, value);
		C7Settings.SaveSettings();
	}

	public static bool GetShowGrid() {
		return C7Settings.GetBoolOrDefault(SectionName, ShowGrid, DefaultShowGrid);
	}

	public static void SetShowGrid(bool value) {
		C7Settings.SetBool(SectionName, ShowGrid, value);
		C7Settings.SaveSettings();
	}
}

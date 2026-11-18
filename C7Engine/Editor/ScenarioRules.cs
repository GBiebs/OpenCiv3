using System;
using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// What a scenario allows at game setup (Civ3's help: "any victory condition
// not enabled ... may not be enabled at the game setup screen. Players may
// still choose to disable any victory conditions you have enabled"; the
// same for game rules), and which civilizations are playable. Used by Load
// Scenario's and the new game's setup screens.
public static class ScenarioRules {
	public sealed record Toggle(string Name, Func<VictoryConditions, Rules, bool> Get, Action<VictoryConditions, Rules, bool> Set);

	// The victory conditions and game rules of Civ3's Scenario tab that the
	// game plays, in its order.
	public static readonly Toggle[] All = [
		new("Domination victory", (v, _) => v.AllowDominationVictory, (v, _, on) => v.AllowDominationVictory = on),
		new("Space race victory", (v, _) => v.AllowSpaceRaceVictory, (v, _, on) => v.AllowSpaceRaceVictory = on),
		new("Diplomatic victory", (v, _) => v.AllowDiplomaticVictory, (v, _, on) => v.AllowDiplomaticVictory = on),
		new("Conquest victory", (v, _) => v.AllowConquestVictory, (v, _, on) => v.AllowConquestVictory = on),
		new("Cultural victory", (v, _) => v.AllowCulturalVictory, (v, _, on) => v.AllowCulturalVictory = on),
		new("Wonder victory", (v, _) => v.AllowWonderVictory, (v, _, on) => v.AllowWonderVictory = on),
		new("Accelerated production", (_, r) => r.AcceleratedProduction, (_, r, on) => r.AcceleratedProduction = on),
		new("Elimination", (v, _) => v.CityElimination, (v, _, on) => v.CityElimination = on),
		new("Regicide", (v, _) => v.Regicide, (v, _, on) => v.Regicide = on),
		new("Mass Regicide", (v, _) => v.MassRegicide, (v, _, on) => v.MassRegicide = on),
		new("Victory Point Scoring", (v, _) => v.VictoryLocations, (v, _, on) => v.VictoryLocations = on),
		new("Capture the Princess", (v, _) => v.CaptureTheFlag, (v, _, on) => v.CaptureTheFlag = on),
		new("Reverse Capture the Flag", (v, _) => v.ReverseCaptureTheFlag, (v, _, on) => v.ReverseCaptureTheFlag = on),
		new("Culture flipping", (_, r) => r.AllowCultureFlips, (_, r, on) => r.AllowCultureFlips = on),
		new("Scientific leaders", (_, r) => r.ScientificLeaders, (_, r, on) => r.ScientificLeaders = on),
	];

	// Whether the save is a scenario made in the editor (it then limits the
	// setup screens).
	public static bool IsEditorScenario(SaveGame save) => save?.Scenario != null;

	// The victory conditions and rules a player may keep or turn off: those
	// the scenario has on. None for a game that isn't an editor scenario.
	public static List<Toggle> Allowed(SaveGame save) {
		if (!IsEditorScenario(save)) {
			return [];
		}
		return All.Where(t => t.Get(save.VictoryConditions ?? new VictoryConditions(), save.Rules)).ToList();
	}

	// Whether the scenario lets a player pick the civilization (Civ3's
	// Playable Civilizations: none chosen means all). A Civ3 file played
	// as it is keeps its list in the save (SaveGame.PlayableCivilizations).
	public static bool IsPlayable(SaveGame save, string civilization) {
		List<string> playable = save?.Scenario != null ? save.Scenario.playableCivs : save?.PlayableCivilizations;
		return playable == null || playable.Count == 0 || playable.Contains(civilization);
	}

	// Turns off what the player turned off at setup.
	public static void TurnOff(SaveGame save, IEnumerable<Toggle> off) {
		save.VictoryConditions ??= new VictoryConditions();
		foreach (Toggle toggle in off) {
			toggle.Set(save.VictoryConditions, save.Rules, false);
		}
	}
}

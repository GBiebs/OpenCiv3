using System;
using System.Collections.Generic;
using C7Engine;

namespace C7GameData;

public class VictoryConditions {
	// TODO: default/preferred victory conditions
	public bool AllowDominationVictory { get; set; }
	public bool AllowSpaceRaceVictory { get; set; }
	public bool AllowDiplomaticVictory { get; set; }
	public bool AllowConquestVictory { get; set; }
	public bool AllowCulturalVictory { get; set; }
	public bool AllowWonderVictory { get; set; }
	public bool CityElimination { get; set; }
	public bool Regicide { get; set; }
	public bool MassRegicide { get; set; }
	public bool VictoryLocations { get; set; }
	public bool CaptureTheFlag { get; set; }
	public bool ReverseCaptureTheFlag { get; set; }

	// A condition the new game screen offers: its C7.ini key and label, and
	// whether it's on when C7.ini doesn't say (Civ3 starts with its six
	// standard victories on; domination and conquest keep OpenCiv3's off).
	public record Option(string key, string label, bool onByDefault, Func<VictoryConditions, bool> get, Action<VictoryConditions, bool> set);

	public static readonly List<Option> Options = [
		new(nameof(AllowDominationVictory), "Allow Domination Victory", false, vc => vc.AllowDominationVictory, (vc, on) => vc.AllowDominationVictory = on),
		new(nameof(AllowConquestVictory), "Allow Conquest Victory", false, vc => vc.AllowConquestVictory, (vc, on) => vc.AllowConquestVictory = on),
		new(nameof(AllowCulturalVictory), "Allow Cultural Victory", true, vc => vc.AllowCulturalVictory, (vc, on) => vc.AllowCulturalVictory = on),
		new(nameof(AllowDiplomaticVictory), "Allow Diplomatic Victory", true, vc => vc.AllowDiplomaticVictory, (vc, on) => vc.AllowDiplomaticVictory = on),
		new(nameof(AllowSpaceRaceVictory), "Allow Space Race Victory", true, vc => vc.AllowSpaceRaceVictory, (vc, on) => vc.AllowSpaceRaceVictory = on),
	];

	public static VictoryConditions FromSettings() {
		VictoryConditions vc = new();
		foreach (Option option in Options) {
			string raw = C7Settings.GetSettingsValueOrDefault(C7Settings.Rules.SectionName, option.key, "");
			option.set(vc, bool.TryParse(raw, out bool on) ? on : option.onByDefault);
		}
		return vc;
	}
}

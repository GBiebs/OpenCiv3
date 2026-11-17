using System.Collections.Generic;
using System.Linq;
using C7GameData;
using C7GameData.Save;

namespace C7Engine.Editor;

// What a scenario made in the editor asks of the game when it starts, beyond
// what the save itself sets up: called by Load Scenario as it starts a
// scenario with players (ScenarioSetup).
public static class ScenarioStart {
	public static void Apply(SaveGame save) {
		NameAutoNamedCities(save);
	}

	// Civ3's City Properties "Auto": each such city takes the first name of
	// its owner's civilization no city has yet, in the cities' order.
	private static void NameAutoNamedCities(SaveGame save) {
		List<string> auto = save.Scenario?.autoNamedCities;
		if (auto == null || auto.Count == 0) {
			return;
		}
		HashSet<string> autoIds = auto.ToHashSet();
		HashSet<string> taken = save.Cities.Where(c => !autoIds.Contains(c.id.ToString())).Select(c => c.name).ToHashSet();
		foreach (SaveCity city in save.Cities.Where(c => autoIds.Contains(c.id.ToString()))) {
			SavePlayer owner = save.Players.Find(p => Equals(p.id, city.owner));
			Civilization civ = save.Civilizations.Find(c => c.name == owner?.civilization);
			string name = civ?.cityNames.FirstOrDefault(n => !taken.Contains(n));
			if (name != null) {
				city.name = name;
				taken.Add(name);
			}
		}
	}
}

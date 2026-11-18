using System;
using System.Collections.Generic;
using C7GameData;

namespace C7Engine.AI;

// Civ3's flavors (Rules.Flavors, the scenario editor's Flavors page): named
// leanings that make a computer civilization favour the advances and
// improvements sharing its civilization's flavors, and, by each flavor's
// relationship (%) with the others, those of related flavors. Civ3 doesn't
// say by how much; the rules' FlavorPreferencePercent is how much more an
// item of the civ's own flavor is worth to it (its research value, its
// building score), an item of a related flavor that times the
// relationship.
public static class Flavors {
	// The civ's liking (%) for an item: the strongest relationship between
	// one of its civilization's flavors and one of the item's, 0 with none.
	public static int Affinity(Rules rules, Civilization civ, string item, Func<FlavorRule, List<string>> members) {
		List<FlavorRule> flavors = rules?.Flavors;
		if (flavors == null || flavors.Count == 0 || civ == null || item == null) {
			return 0;
		}
		int best = 0;
		for (int i = 0; i < flavors.Count; ++i) {
			if (!flavors[i].Civilizations.Contains(civ.name)) {
				continue;
			}
			for (int j = 0; j < flavors.Count; ++j) {
				if (!members(flavors[j]).Contains(item)) {
					continue;
				}
				List<int> relationships = flavors[i].Relationships;
				int relationship = j < relationships.Count ? relationships[j] : (i == j ? 100 : 0);
				best = Math.Max(best, relationship);
			}
		}
		return best;
	}

	// What the item's value is multiplied by for the civ.
	public static double Factor(Rules rules, Civilization civ, string item, Func<FlavorRule, List<string>> members) {
		int affinity = Affinity(rules, civ, item, members);
		return affinity <= 0 ? 1 : 1 + (rules.FlavorPreferencePercent / 100.0) * (affinity / 100.0);
	}

	public static double TechFactor(Rules rules, Civilization civ, Tech tech) => Factor(rules, civ, tech?.Name, f => f.Techs);

	public static double BuildingFactor(Rules rules, Civilization civ, Building building) => Factor(rules, civ, building?.name, f => f.Buildings);
}

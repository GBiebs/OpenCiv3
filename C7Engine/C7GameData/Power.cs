using System;
using System.Linq;

namespace C7GameData {
	// A civ's power on the histograph. Civ3: "Power is an amalgam of cities,
	// gold, culture, advances, resources, military strength, nuclear
	// weapons, and wonders." How it weighs them is undocumented, so the
	// points are rules (Rules.power).
	public static class Power {
		public static int Of(Player player, GameData gameData) {
			PowerRules rules = player.rules?.power ?? gameData.rules.power;
			City capital = player.cities.Find(c => c.IsCapital());
			int resources = capital == null ? 0 : gameData.GetTradeNetwork().GetResourcesAvailableToCity(player, capital).Keys
				.Count(r => r.Category is ResourceCategory.LUXURY or ResourceCategory.STRATEGIC);
			int strength = player.units.Where(u => u.IsCombatUnit() && !Nuclear.IsNuke(u.unitType))
				.Sum(u => u.unitType.attack + u.unitType.defense);
			int nukes = player.units.Count(u => Nuclear.IsNuke(u.unitType));
			int wonders = player.cities.Sum(c => c.constructed_buildings.Count(b => b.building.IsGreatWonder()));

			return player.cities.Count * rules.pointsPerCity
				+ player.gold / Math.Max(1, rules.goldPerPoint)
				+ player.TotalCulture() / Math.Max(1, rules.culturePerPoint)
				+ player.knownTechs.Count * rules.pointsPerAdvance
				+ resources * rules.pointsPerResource
				+ strength * rules.pointsPerStrength
				+ nukes * rules.pointsPerNuke
				+ wonders * rules.pointsPerWonder;
		}
	}
}

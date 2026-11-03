using System.Collections.Generic;
using System.Linq;

namespace C7GameData {
	// What the military advisor shows: our units by type or by city, which
	// can be upgraded now, and what we know of each rival's forces.
	public static class MilitaryReport {
		// Our units of one type: how many, and how many could be upgraded
		// where they stand with the gold we have (and what that costs all
		// together, cheapest first, as far as the gold goes).
		public record UnitGroup(UnitPrototype type, List<MapUnit> units, int upgradable, int upgradeCost);

		// What we know of a rival's forces: the units we can see (all of them
		// while our spy is planted there), counted by type, strongest first.
		public record Intelligence(bool spy, int seen, List<(UnitPrototype type, int count)> types);

		// Our units by type, in the order the game lists unit types.
		public static List<UnitGroup> ByType(GameData gameData, Player player) {
			List<UnitGroup> groups = new();
			foreach (IGrouping<UnitPrototype, MapUnit> group in player.units.GroupBy(u => u.unitType)
				.OrderBy(g => gameData.unitPrototypes.IndexOf(g.Key))) {
				List<MapUnit> units = group.ToList();
				int gold = player.gold, upgradable = 0, cost = 0;
				foreach (int price in units.Select(u => u.UpgradeOffer()).Where(o => o != null).Select(o => o.Value.cost).Order()) {
					if (price > gold) {
						break;
					}
					gold -= price;
					cost += price;
					upgradable++;
				}
				groups.Add(new UnitGroup(group.Key, units, upgradable, cost));
			}
			return groups;
		}

		// Our units in each city (founding order), then those elsewhere under
		// a null city.
		public static List<(City city, List<MapUnit> units)> ByCity(Player player) {
			List<(City, List<MapUnit>)> result = player.cities
				.Select(c => (c, player.units.Where(u => u.location == c.location).ToList()))
				.ToList<(City, List<MapUnit>)>();
			List<MapUnit> field = player.units.Where(u => u.location?.cityAtTile?.owner != player).ToList();
			if (field.Count > 0) {
				result.Add((null, field));
			}
			return result;
		}

		public static Intelligence On(Player us, Player them) {
			bool spy = Espionage.HasSpy(us, them);
			List<MapUnit> known = them.units
				.Where(u => spy || (us.tileKnowledge.isActiveTile(u.location) && Naval.IsVisibleTo(u, us)))
				.Where(u => u.unitType.attack > 0 || u.unitType.defense > 0)
				.ToList();
			List<(UnitPrototype, int)> types = known.GroupBy(u => u.unitType)
				.OrderByDescending(g => System.Math.Max(g.Key.attack, g.Key.defense)).ThenByDescending(g => g.Key.shieldCost)
				.Select(g => (g.Key, g.Count())).ToList();
			return new Intelligence(spy, known.Count, types);
		}

		// Our cities with no unit that can defend them.
		public static List<City> Undefended(Player player) {
			return player.cities.Where(c => !c.location.unitsOnTile.Any(u => u.owner == player && u.CanDefendOnLand())).ToList();
		}
	}
}

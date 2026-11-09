using System.Collections.Generic;
using System.Linq;

namespace C7GameData {
	// Civ3's production queue (the city screen's Shift+click; Shift+Delete
	// clears it, Shift-Q saves it under a name, Q loads one): when the city
	// finishes what it builds, the next item it can still build starts, and
	// items it no longer can are dropped.
	public static class ProductionQueue {
		public const int MaxLength = 9;

		// An item as kept in saves and orders: its kind and name.
		public static string Entry(IProducible item) {
			return item switch {
				UnitPrototype => "U:",
				Building => "B:",
				_ => "I:",
			} + item.name;
		}

		public static IProducible Find(string entry, List<UnitPrototype> units, List<Building> buildings, List<Inflow> inflows) {
			if (entry == null || entry.Length < 3 || entry[1] != ':') {
				return null;
			}
			string name = entry[2..];
			return entry[0] switch {
				'U' => units.Find(u => u.name == name),
				'B' => buildings.Find(b => b.name == name),
				'I' => inflows.Find(i => i.name == name),
				_ => null,
			};
		}

		// Takes the next item off the city's queue that it can build now.
		public static IProducible TakeNext(City city, GameData gameData) {
			if (city.productionQueue.Count == 0) {
				return null;
			}
			HashSet<IProducible> options = city.ListProductionOptions(gameData).ToHashSet();
			while (city.productionQueue.Count > 0) {
				IProducible next = city.productionQueue[0];
				city.productionQueue.RemoveAt(0);
				if (options.Contains(next)) {
					return next;
				}
			}
			return null;
		}
	}
}

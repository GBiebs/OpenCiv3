using System.Collections.Generic;
using System.Linq;

namespace C7GameData {
	// Civ3's sentry (Y, Shift-Y; the Conquests readme: "wake whenever next
	// to friendly or enemy unit(s)", and Shift-Y "next to enemy units"):
	// a unit on sentry sits out its turns until a unit comes onto its tile
	// or next to it. With Y any other unit wakes it (ours too, as one
	// coming to board a ship would); with Shift-Y only a unit of a civ at war
	// with its owner (barbarians included). Checked as each unit enters a
	// tile, in every game alike.
	public static class Sentries {
		public static void WakeNear(MapUnit mover, Tile tile) {
			IEnumerable<Tile> around = tile.neighbors.Values.Prepend(tile).Where(t => t != null && t != Tile.NONE);
			foreach (MapUnit unit in around.SelectMany(t => t.unitsOnTile).ToList()) {
				if (unit == mover || unit.sentry == MapUnit.SentryMode.None) {
					continue;
				}
				// (Not by the units it carries, nor the ship carrying it.)
				if (unit.loadedOnUnitId == mover.id || mover.loadedOnUnitId == unit.id) {
					continue;
				}
				if (unit.sentry == MapUnit.SentryMode.AnyUnit || IsEnemy(unit.owner, mover.owner)) {
					unit.sentry = MapUnit.SentryMode.None;
				}
			}
		}

		private static bool IsEnemy(Player us, Player them) {
			return us != them && (them.isBarbarians || us.isBarbarians || !us.IsAtPeaceWith(them));
		}
	}
}

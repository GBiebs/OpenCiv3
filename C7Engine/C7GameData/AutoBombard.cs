using System.Linq;
using System.Threading.Tasks;
using C7Engine.AI;

namespace C7GameData {
	// Civ3's automated bombarding (Conquests' labels.txt: "Automate:
	// Bombard (Shift-B)", "Bombing (Shift-B)", "Precision Bombing
	// (Shift-P)"): once a turn the unit bombards, or the plane flies its
	// mission at, the most valuable target in range that belongs to a civ
	// at war with its owner (barbarians too); never a civ at peace, so it
	// starts no war. With nothing in range it waits.
	public static class AutoBombard {
		public static bool CanAutomate(MapUnit unit, bool precision) {
			if (unit.IsAirUnit()) {
				return Air.CanFly(unit, precision ? Air.Mission.PrecisionStrike : Air.Mission.Bombing);
			}
			return !precision && unit.HasBombardAbility() && !Nuclear.IsNuke(unit.unitType);
		}

		public static void Start(MapUnit unit, bool precision) {
			if (!CanAutomate(unit, precision)) {
				return;
			}
			unit.Wake();
			unit.autoBombard = !unit.IsAirUnit() ? MapUnit.AutoBombardMode.Bombard
				: precision ? MapUnit.AutoBombardMode.PrecisionStrike : MapUnit.AutoBombardMode.Bombing;
		}

		public static async Task Act(GameData gameData, MapUnit unit) {
			if (unit.autoBombardTurn == gameData.turn) {
				return;
			}
			unit.autoBombardTurn = gameData.turn;
			if (Target(gameData, unit) is Tile target) {
				if (unit.autoBombard == MapUnit.AutoBombardMode.Bombard) {
					await unit.Bombard(target);
				} else {
					Air.Mission mission = unit.autoBombard == MapUnit.AutoBombardMode.PrecisionStrike ? Air.Mission.PrecisionStrike : Air.Mission.Bombing;
					await Air.Fly(gameData, unit, mission, target);
				}
			}
		}

		// The enemy tile in reach worth the most: units by their cost, a city
		// 10 more (a precision strike: enemy cities only).
		public static Tile Target(GameData gameData, MapUnit unit) {
			Player us = unit.owner;
			bool Enemy(Player them) => them != null && them != us && (them.isBarbarians || !us.IsAtPeaceWith(them));
			int range = unit.IsAirUnit() ? unit.unitType.operationalRange : unit.unitType.bombardRange;
			return unit.location.GetTilesWithinTileSquare(range)
				.Where(t => Tile.IsTileValid(t) && t != unit.location && us.tileKnowledge.isActiveTile(t))
				.Select(t => (tile: t, value: Value(t, us, Enemy, unit.autoBombard)))
				.Where(c => c.value > 0 && CanHit(gameData, unit, c.tile))
				.OrderByDescending(c => c.value)
				.Select(c => c.tile)
				.FirstOrDefault();
		}

		private static int Value(Tile t, Player us, System.Func<Player, bool> enemy, MapUnit.AutoBombardMode mode) {
			bool enemyCity = t.HasCity() && enemy(t.cityAtTile.owner);
			if (mode == MapUnit.AutoBombardMode.PrecisionStrike) {
				return enemyCity ? 1 : 0;
			}
			int units = t.unitsOnTile.Where(u => enemy(u.owner) && Naval.IsVisibleTo(u, us)).Sum(u => u.unitType.shieldCost);
			// (A tile also holding units of a civ at peace is left alone.)
			if (t.unitsOnTile.Any(u => u.owner != us && !enemy(u.owner)) || (t.HasCity() && !enemyCity && t.cityAtTile.owner != us)) {
				return 0;
			}
			return units + (enemyCity ? 10 : 0);
		}

		private static bool CanHit(GameData gameData, MapUnit unit, Tile t) {
			return unit.autoBombard switch {
				MapUnit.AutoBombardMode.Bombard => unit.CanBombardTile(t),
				MapUnit.AutoBombardMode.PrecisionStrike => Air.CanFly(unit, Air.Mission.PrecisionStrike, t),
				_ => Air.CanFly(unit, Air.Mission.Bombing, t),
			};
		}
	}
}

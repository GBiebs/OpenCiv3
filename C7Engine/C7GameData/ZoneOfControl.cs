using System;
using System.Linq;
using System.Threading.Tasks;
using C7Engine;
using Serilog;

namespace C7GameData {
	// Civ3's zone of control (Conquests): a unit with the ability takes a free
	// shot at an enemy passing through the eight tiles around it, that is
	// moving from one of those tiles to another, without a full battle. The
	// strongest such unit shoots once per move, using its attack or bombard
	// strength (whichever is higher); the mover loses one hit point with
	// chance A / (16 D + A), D being its defense. A unit down to its last hit
	// point is spared, so zone of control never kills. Land units exert it on
	// land units and sea units on sea units; aircraft don't.
	// Sources: the Conquests Civilopedia (GCON_ZOC), the formula from the C3X
	// mod's author, and C3X's options, which describe the base game's limits
	// (see the skill's civ3-rules.md).
	public static class ZoneOfControl {
		private static ILogger log = Log.ForContext(typeof(ZoneOfControl));

		// Whether moving from `from` to `to` passes through the zone of a unit
		// standing on `zoneTile`.
		public static bool PassesThrough(Tile from, Tile to, Tile zoneTile) {
			return from.DistanceTo(zoneTile) == 1 && to.DistanceTo(zoneTile) == 1;
		}

		// A unit's strength for its free shot (an army's, its fighting member's).
		public static int Strength(MapUnit unit) {
			return Math.Max((int)unit.BaseCombatStrength(CombatRole.Attack), unit.CombatType(CombatRole.Attack).bombard);
		}

		// The chance that a shot of strength `attack` hits a unit with defense
		// `defense`: A / (16D + A) with Civ3's rules.
		public static double HitChance(int attack, int defense) {
			if (attack <= 0) {
				return 0;
			}
			return attack / ((double)EngineStorage.gameData.rules.ZoneOfControlDefenseFactor * Math.Max(defense, 0) + attack);
		}

		private static bool CanShootAt(MapUnit unit, MapUnit mover) {
			return unit.unitType.zoneOfControl
				&& !unit.IsAirUnit()
				&& !unit.IsInArmy()
				&& unit.owner != mover.owner
				&& !unit.owner.IsAtPeaceWith(mover.owner)
				&& unit.IsLandUnit() == mover.IsLandUnit()
				&& unit.hitPointsRemaining > 0
				&& Strength(unit) > 0;
		}

		// The unit that takes the shot at `mover` going from `from` to `to`, or
		// MapUnit.NONE if no enemy zone of control covers the move (or the
		// mover is on its last hit point).
		public static MapUnit Interceptor(MapUnit mover, Tile from, Tile to) {
			if (mover.hitPointsRemaining <= 1) {
				return MapUnit.NONE;
			}
			MapUnit best = MapUnit.NONE;
			int bestStrength = 0;
			foreach (Tile zoneTile in from.neighbors.Values.Where(t => Tile.IsTileValid(t) && t != to && PassesThrough(from, to, t))) {
				foreach (MapUnit unit in zoneTile.unitsOnTile) {
					if (CanShootAt(unit, mover) && Strength(unit) > bestStrength) {
						best = unit;
						bestStrength = Strength(unit);
					}
				}
			}
			return best;
		}

		// A Coastal Fortress "automatically bombards passing enemy ships"
		// (Civilopedia): an enemy city with a building with naval power takes
		// the same kind of shot, with that strength, at a ship passing through
		// the water around it. The strongest such city, or null.
		public static City CoastalFortress(MapUnit mover, Tile from, Tile to) {
			if (!mover.IsWaterUnit() || mover.hitPointsRemaining <= 1) {
				return null;
			}
			return from.neighbors.Values
				.Where(t => Tile.IsTileValid(t) && t != to && t.HasCity() && PassesThrough(from, to, t))
				.Select(t => t.cityAtTile)
				.Where(c => c.owner != mover.owner && !c.owner.IsAtPeaceWith(mover.owner) && NavalPower(c) > 0)
				.OrderByDescending(NavalPower)
				.FirstOrDefault();
		}

		public static int NavalPower(City city) {
			return city.constructed_buildings.Select(cb => cb.building.navalPower).DefaultIfEmpty(0).Max();
		}

		// Resolves a coastal fortress's shot at `mover` going from `from` to
		// `to`. Returns the city that hit it, or null.
		public static City ApplyCoastalFortress(MapUnit mover, Tile from, Tile to) {
			City fortress = CoastalFortress(mover, from, to);
			if (fortress == null || GameData.rng.NextDouble() >= HitChance(NavalPower(fortress), (int)mover.BaseCombatStrength(CombatRole.Defense))) {
				return null;
			}
			mover.hitPointsRemaining = Math.Max(1, mover.hitPointsRemaining - Math.Max(1, EngineStorage.gameData.rules.ZoneOfControlDamage));
			log.Information($"{fortress.name}'s coastal fortress hits {mover} passing from {from} to {to} ({mover.hitPointsRemaining} HP left)");
			return fortress;
		}

		// Resolves the zone of control for `mover` going from `from` to `to`.
		// Returns the unit that hit it (it has lost a hit point), or
		// MapUnit.NONE.
		public static async Task<MapUnit> Apply(MapUnit mover, Tile from, Tile to) {
			MapUnit interceptor = Interceptor(mover, from, to);
			if (interceptor == MapUnit.NONE) {
				return MapUnit.NONE;
			}

			TileDirection facing = interceptor.facingDirection;
			interceptor.facingDirection = interceptor.GetAttackAnimationDirection(interceptor.location.DirectionTo(to));
			await interceptor.animateAsync(MapUnit.AnimatedAction.ATTACK1);
			interceptor.facingDirection = facing;

			if (GameData.rng.NextDouble() >= HitChance(Strength(interceptor), (int)mover.BaseCombatStrength(CombatRole.Defense))) {
				return MapUnit.NONE;
			}
			// Never the last hit point.
			mover.hitPointsRemaining = Math.Max(1, mover.hitPointsRemaining - Math.Max(1, EngineStorage.gameData.rules.ZoneOfControlDamage));
			log.Information($"{interceptor} at {interceptor.location} hits {mover} passing from {from} to {to} ({mover.hitPointsRemaining} HP left)");
			return interceptor;
		}
	}
}

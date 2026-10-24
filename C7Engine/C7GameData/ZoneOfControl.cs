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

		// A unit's strength for its free shot.
		public static int Strength(MapUnit unit) {
			return Math.Max(unit.unitType.attack, unit.unitType.bombard);
		}

		// The chance that a shot of strength `attack` takes a hit point off a
		// unit with defense `defense`.
		public static double HitChance(int attack, int defense) {
			if (attack <= 0) {
				return 0;
			}
			return attack / (16.0 * Math.Max(defense, 0) + attack);
		}

		private static bool CanShootAt(MapUnit unit, MapUnit mover) {
			return unit.unitType.zoneOfControl
				&& !unit.IsAirUnit()
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

			if (GameData.rng.NextDouble() >= HitChance(Strength(interceptor), mover.unitType.defense)) {
				return MapUnit.NONE;
			}
			mover.hitPointsRemaining -= 1;
			log.Information($"{interceptor} at {interceptor.location} hits {mover} passing from {from} to {to} ({mover.hitPointsRemaining} HP left)");
			return interceptor;
		}
	}
}

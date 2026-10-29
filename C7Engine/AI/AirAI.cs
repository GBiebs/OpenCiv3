using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using C7GameData;

namespace C7Engine.AI {
	// The AI's aircraft (see Air). At war, a plane bombs the most valuable
	// target in range it can see (enemy units near our cities first, then any
	// enemy units, then enemy cities), skipping ones the anti-aircraft fire
	// there would likely down; a fighter with nothing to bomb flies air
	// superiority. Planes with nothing to do move to the city (or airfield)
	// nearest the enemy within re-basing range. Paratroopers stay with the
	// ground units.
	public static class AirAI {
		// Don't bomb where anti-aircraft fire downs a plane this often (%).
		internal const int MaxShootDownPercent = 25;

		public static async Task Act(Player player, GameData gameData) {
			HashSet<Player> enemies = gameData.players.Where(p => p != player && !p.isBarbarians && !p.defeated && !player.IsAtPeaceWith(p)).ToHashSet();
			foreach (MapUnit plane in player.units.Where(u => u.IsAirUnit() && u.movementPoints.canMove).ToList()) {
				if (plane.hitPointsRemaining <= 0 || plane.airSuperiority) {
					continue;
				}
				// A damaged plane rests to heal.
				if (plane.hitPointsRemaining < plane.maxHitPoints) {
					continue;
				}
				if (enemies.Count > 0 && BombingTarget(gameData, plane, player) is Tile target) {
					await Air.Fly(gameData, plane, Air.Mission.Bombing, target);
					continue;
				}
				if (enemies.Count > 0 && Air.CanFly(plane, Air.Mission.AirSuperiority)) {
					await Air.Fly(gameData, plane, Air.Mission.AirSuperiority);
					continue;
				}
				if (enemies.Count > 0 && FrontBase(plane, player, enemies) is Tile front) {
					await Air.Fly(gameData, plane, Air.Mission.Rebase, front);
				}
			}
		}

		// The best tile to bomb, or null.
		internal static Tile BombingTarget(GameData gameData, MapUnit plane, Player player) {
			if (!Air.CanFly(plane, Air.Mission.Bombing)) {
				return null;
			}
			return plane.location.GetTilesWithinTileSquare(plane.unitType.operationalRange)
				.Where(t => Tile.IsTileValid(t) && player.tileKnowledge.isTileKnown(t)
					&& (t.HasCity() || t.unitsOnTile.Any(u => u.owner != player && Naval.IsVisibleTo(u, player))))
				.Where(t => Air.CanFly(plane, Air.Mission.Bombing, t) && Air.ShootDownPercent(gameData, plane, t) <= MaxShootDownPercent)
				.OrderByDescending(t => Value(t, player))
				.FirstOrDefault(t => Value(t, player) > 0);
		}

		private static int Value(Tile t, Player player) {
			int units = t.unitsOnTile.Where(u => u.owner != player && Naval.IsVisibleTo(u, player)).Sum(u => u.unitType.shieldCost);
			bool nearUs = t.neighbors.Values.Any(n => n.cityAtTile?.owner == player) || t.OwningPlayer() == player;
			return (nearUs ? 3 : 1) * units + (t.HasCity() ? 10 : 0);
		}

		// Our city or airfield nearest an enemy city that the plane can
		// re-base to, if it's nearer than where the plane is.
		internal static Tile FrontBase(MapUnit plane, Player player, HashSet<Player> enemies) {
			List<Tile> enemyCities = enemies.SelectMany(e => e.cities).Select(c => c.location).ToList();
			if (enemyCities.Count == 0 || !Air.CanFly(plane, Air.Mission.Rebase)) {
				return null;
			}
			int Distance(Tile t) => enemyCities.Min(e => e.DistanceTo(t));
			IEnumerable<Tile> airfields = plane.location.GetTilesWithinTileSquare(plane.unitType.operationalRange * (player.rules?.air.rebaseRangeFactor ?? 6))
				.Where(t => Tile.IsTileValid(t) && Structures.Has(t, Tile.TileOverlays.AIRFIELD, player));
			Tile best = player.cities.Select(c => c.location).Concat(airfields)
				.Where(t => Air.CanFly(plane, Air.Mission.Rebase, t))
				.OrderBy(Distance)
				.FirstOrDefault();
			return best != null && Distance(best) < Distance(plane.location) ? best : null;
		}
	}
}

using System.Linq;
using System.Threading.Tasks;
using C7GameData;
using C7GameData.AIData;
using Serilog;
using static C7GameData.PlayerRelationship;

namespace C7Engine.AI {
	// The AI pillages its enemies' land in wartime. Civ3's AI does pillage
	// (Flintlock, reading the Civ3 code, C3X thread post-16398188,
	// compendium 1.13: "There is a condition stopping AI units from
	// pillaging tiles if doing so would leave the unit itself stranded"),
	// and cutting an enemy city's roads cuts it off from the empire (ainwood,
	// CFC 61562 #1, compendium 3: "By pillaging and blocking the roads, the
	// city was cut-off from the empire"). Pillaging also costs the victim war
	// weariness.
	//
	// HEURISTIC (when, and what): after its units have moved, each unit
	// left with moves in the land of a civ we're at war with pillages the
	// improvements it stands on. One that stands on none, and isn't marching
	// with a war campaign, may first step onto a next tile of that land with
	// one, if it would still have a move to pillage with and no hostile
	// unit that could attack it is next to that tile (unless ours are there
	// already): roads and railroads first, to cut the enemy's supply, then
	// mines and irrigation. Using only leftover moves, a raid never delays an
	// attack or a march. A unit doesn't pillage:
	//   - in a city-state's or the barbarians' land, nor its own or no one's;
	//   - when wounded and resting, as acting would cost it its healing (a
	//     unit heals only if it kept all its moves, see MapUnit.OnBeginTurn).
	// A fortified unit fortifies again after pillaging, so its defence is
	// unchanged. (Civ3's "stranded" condition is not modelled further: the
	// raid step keeps a move in hand and away from enemy attackers.)
	public static class PillageAI {
		private static readonly ILogger log = Log.ForContext(typeof(PillageAI));

		// An army pillages for nothing (see MapUnit.Pillage), so it could go
		// on until the tile is bare: a railroad, then its road and the rest.
		private const int MaxPillagesPerUnit = 3;

		// Pillages with each of the player's units that should; returns how
		// many times they pillaged.
		public static async Task<int> PlayTurn(Player player, GameData gameData) {
			if (player.isHuman || player.IsIndependent || !gameData.players.Any(p => p != player && IsPillageable(player, p))) {
				return 0;
			}
			if (AIFogOfWar.EnabledFor(player)) {
				AIFogOfWar.Refresh(player);
			}
			int pillaged = 0;
			foreach (MapUnit unit in player.units.ToArray()) {
				if (unit.owner != player || unit.location == null || unit.location == Tile.NONE) {
					continue;
				}
				if (!ShouldPillage(unit, player) && RaidTarget(unit, player) is Tile target) {
					TileDirection dir = unit.location.neighbors.First(kv => kv.Value == target).Key;
					if (!await unit.Move(dir) || unit.location != target) {
						continue;
					}
					log.Information("{Unit} steps onto {Tile} to pillage it", unit, target);
				}
				for (int i = 0; i < MaxPillagesPerUnit && ShouldPillage(unit, player); ++i) {
					bool wasFortified = unit.isFortified;
					if (!unit.Pillage()) {
						break;
					}
					++pillaged;
					if (wasFortified) {
						unit.isFortified = true;
					}
				}
			}
			if (pillaged > 0) {
				log.Information("{Player} pillaged {Count} times", player, pillaged);
			}
			return pillaged;
		}

		// Whether the unit should pillage the tile it stands on.
		internal static bool ShouldPillage(MapUnit unit, Player player) {
			if (unit.owner != player || unit.IsLoaded() || unit.IsLockedInArmy() || !unit.CanPillage()) {
				return false;
			}
			return IsPillageable(player, unit.location.OwningPlayer()) && !IsResting(unit);
		}

		// A next tile in enemy land the unit could step onto and still
		// pillage, or null. Roads and railroads come first.
		internal static Tile RaidTarget(MapUnit unit, Player player) {
			Tile here = unit.location;
			if (unit.IsLoaded() || unit.IsLockedInArmy() || unit.currentAI is WarCampaign.MemberAI || !unit.IsLandUnit()
				|| !unit.unitType.actions.Contains(UnitAction.Pillage) || unit.hasPillagedThisTurn || !unit.movementPoints.canMove
				|| IsResting(unit) || !IsPillageable(player, here.OwningPlayer())) {
				return null;
			}
			Tile best = null;
			int bestRank = int.MaxValue;
			foreach ((TileDirection dir, Tile n) in here.neighbors) {
				if (n == null || n == Tile.NONE || !n.IsLand() || n.HasCity() || !IsPillageable(player, n.OwningPlayer())
					|| n.unitsOnTile.Any(u => u.owner != player)) {
					continue;
				}
				int rank = Rank(n);
				if (rank >= bestRank) {
					continue;
				}
				float cost = TilePath.GetMovementCost(player, here, dir, n, unit.unitType);
				if (cost >= unit.movementPoints.remaining || !unit.CanEnterPeacefully(n)) {
					continue;
				}
				if (!n.unitsOnTile.Any(u => u.owner == player) && HostileAttackerNextTo(n, player)) {
					continue;
				}
				best = n;
				bestRank = rank;
			}
			return best;
		}

		// How much the tile is worth pillaging, lower first: a road or
		// railroad 0, another man-made improvement 1, none int.MaxValue.
		private static int Rank(Tile tile) {
			int rank = int.MaxValue;
			foreach (TerrainImprovement i in tile.overlays.GetImprovements()) {
				if (!Tile.TileOverlays.IsManMade(i)) {
					continue;
				}
				rank = System.Math.Min(rank, i.layer == TerrainImprovement.Layer.Roads ? 0 : 1);
			}
			return rank;
		}

		// A wounded unit that has kept all its moves heals at the start of
		// its next turn; acting would cost it that.
		private static bool IsResting(MapUnit unit) {
			return unit.CompositeHitPoints() < unit.CompositeMaxHitPoints() && unit.movementPoints.remaining >= unit.MaxMovementPoints();
		}

		// Land we pillage: a civ's, other than ours, that we're at war with.
		private static bool IsPillageable(Player player, Player landOwner) {
			return landOwner != null && landOwner != player && !landOwner.IsIndependent && !landOwner.defeated
				&& AtWar(player, landOwner);
		}

		// Whether a hostile land unit that can attack, which we can see, is
		// next to the tile.
		private static bool HostileAttackerNextTo(Tile tile, Player player) {
			bool fog = AIFogOfWar.EnabledFor(player);
			foreach (Tile n in tile.neighbors.Values) {
				if (n == null || n == Tile.NONE || n.unitsOnTile.Count == 0 || (fog && !AIFogOfWar.SeesUnitsOn(player, n))) {
					continue;
				}
				foreach (MapUnit u in n.unitsOnTile) {
					if (u.owner != player && u.IsLandUnit() && u.unitType.attack > 0 && AtWar(player, u.owner)) {
						return true;
					}
				}
			}
			return false;
		}
	}
}

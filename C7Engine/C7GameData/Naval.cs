using System.Collections.Generic;
using System.Linq;
using C7Engine;
using C7GameData.Save;
using Serilog;

namespace C7GameData {
	// Ships that sink at sea (Civilopedia: a galley "must end each turn in a
	// coastal square -- they are in danger of sinking if they end their turn
	// in a sea or ocean square. If The Great Lighthouse is built, galleys may
	// safely end turns at sea, but never ocean squares"; the Caravel is in
	// danger only in ocean). Civ3's PRTO "sinks in sea" and "sinks in ocean"
	// flags say which water; a ship ending its turn there sinks with the
	// rules' chance, 50%, or 25% for a Seafaring civ (CivFanatics, "Sinking
	// Chances of Seafaring and Non-Seafaring Civilizations"), taking its
	// cargo down.
	public static class Naval {
		private static ILogger log = Log.ForContext(typeof(Naval));

		// Whether `ship` would be in danger of sinking at `tile`, for an owner
		// that has (or hasn't) a wonder allowing safe sea travel.
		public static bool InDanger(UnitPrototype ship, Tile tile, bool safeAtSea) {
			if (!tile.IsWater() || tile.HasCity()) {
				return false;
			}
			if (tile.IsSea()) {
				return ship.sinksInSea && !safeAtSea;
			}
			return tile.baseTerrainType.isOcean() && ship.sinksInOcean;
		}

		public static bool SafeAtSea(Player player) {
			return player.OwnsBuilding(b => b.HasWonderFlag(SaveBuilding.WonderFlag.SafeSeaTravel));
		}

		public static int SinkPercent(Player player) {
			NavalRules rules = player.rules?.naval ?? new NavalRules();
			return player.civilization?.traits.Contains(Civilization.Trait.Seafaring) == true ? rules.seafaringSinkPercent : rules.sinkPercent;
		}

		// Amphibious attacks (Civilopedia: Marines "can unload from a sea
		// transport into any coastal land square, including enemy cities and
		// enemy-occupied squares"): other land units can't fight, or enter a
		// foreign city, straight from a ship.
		public static bool MayAttackFrom(MapUnit unit, Tile from) {
			return unit.unitType.amphibious || !(unit.Container() is MapUnit ship && ship.IsWaterUnit() && from.IsWater());
		}

		// The Privateer "carries no nationality markings, allowing them to
		// attack and be attacked without revealing the nationality": a unit
		// with hidden nationality fights any foreign ship at sea without war,
		// and any unit may attack a stack of them (Civilopedia).
		public static bool FightsWithoutWar(MapUnit attacker, Tile tile) {
			List<MapUnit> foreign = tile.unitsOnTile.Where(u => u.owner != attacker.owner).ToList();
			if (foreign.Count == 0 || attacker.owner.isBarbarians) {
				return false;
			}
			if (attacker.unitType.hiddenNationality && tile.IsWater() && !tile.HasCity()) {
				return foreign.All(u => u.IsWaterUnit());
			}
			return foreign.All(u => u.unitType.hiddenNationality);
		}

		// Submarines "can operate invisibly to enemy units ... However, other
		// submarines and AEGIS Cruisers and Destroyers can see them plainly"
		// (Civilopedia): a submarine is seen by its owner and by the viewer's
		// units that can see submarines, within their sight (2 tiles with
		// radar, else 1).
		public static bool IsVisibleTo(MapUnit unit, Player viewer) {
			if (!unit.unitType.submarine || unit.owner == viewer || viewer == null) {
				return true;
			}
			int radar = viewer.rules?.RadarTileVisibility ?? 2;
			return viewer.units.Any(u => u.unitType.canSeeSubmarines && u.location != Tile.NONE
				&& u.location.DistanceTo(unit.location) <= (u.unitType.hasRadar ? radar : 1));
		}

		// The units in a stack a stealth attacker may pick (Civilopedia
		// "Stealth Attack": "a selection box appears listing the target
		// units"): the enemies there of its stealth target types.
		public static List<MapUnit> StealthTargets(MapUnit attacker, Tile tile) {
			if (!attacker.unitType.stealthAttack) {
				return [];
			}
			return tile.unitsOnTile
				.Where(u => u.owner != attacker.owner && !u.IsInArmy() && u.CanDefendAgainst(attacker) && u.CanDefendItself()
					&& attacker.unitType.stealthTargets.Contains(u.unitType.name))
				.ToList();
		}

		// The AI's pick: the target it's likeliest to beat, the dearest first.
		public static MapUnit PickStealthTarget(MapUnit attacker, List<MapUnit> targets) {
			return targets
				.OrderByDescending(t => C7Engine.AI.CombatOdds.AttackerWinChance(attacker, t))
				.ThenByDescending(t => t.unitType.shieldCost)
				.First();
		}

		// At the end of `player`'s turn: each of its ships in danger may sink.
		public static void SinkShips(GameData gameData, Player player) {
			bool safeAtSea = SafeAtSea(player);
			int percent = SinkPercent(player);
			foreach (MapUnit ship in player.units.Where(u => u.IsWaterUnit() && InDanger(u.unitType, u.location, safeAtSea)).ToList()) {
				if (GameData.rng.Next(100) >= percent) {
					continue;
				}
				int cargo = ship.Passengers().Count;
				log.Information($"{ship} sinks at {ship.location} with {cargo} units aboard");
				new MsgShipSank(player.id, ship.location, ship.name, cargo).send();
				gameData.RemoveUnit(ship);
			}
		}
	}
}

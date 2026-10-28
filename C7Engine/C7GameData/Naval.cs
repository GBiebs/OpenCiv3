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

using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace C7GameData {
	// Civ3's Respawn AI Players (Rules.RespawnAiPlayers, the scenario
	// editor's Scenario tab): a computer civilization destroyed comes back.
	// Civ3's help says no more; our reading, at the end of the round in
	// which it fell (after the victory check, so a conquest can still win):
	// the fallen player's place goes to a civilization no player has been
	// this game, starting afresh as in a new game (its free advances, the
	// starting treasury and government, the rules' two starting units and a
	// scout if expansionist) on the free land farthest from every city. No
	// civilization left, or no free land, and it stays fallen.
	public static class AiRespawn {
		private static readonly ILogger log = Log.ForContext(typeof(AiRespawn));

		// How far from any city a respawned civilization may start.
		public const int MinimumDistanceFromCities = 4;

		public static void ProcessEndOfRound(GameData gameData) {
			if (gameData.rules?.RespawnAiPlayers != true || gameData.gameOver) {
				return;
			}
			foreach (Player player in gameData.players.Where(p => p.defeated && !p.isHuman && !p.IsIndependent).ToList()) {
				TryRespawn(gameData, player);
			}
		}

		// Brings the fallen player back as a new civilization; returns
		// whether it did.
		public static bool TryRespawn(GameData gameData, Player player) {
			HashSet<Civilization> used = gameData.players.Select(p => p.civilization).Where(c => c != null).ToHashSet();
			used.UnionWith(gameData.fallenCivilizations);
			List<Civilization> free = gameData.civilizations.Where(c => !c.isBarbarian && !c.IsIndependent && !used.Contains(c)).ToList();
			Tile start = StartFor(gameData);
			if (free.Count == 0 || start == null) {
				return false;
			}
			Civilization civ = free[GameData.rng.Next(free.Count)];
			gameData.fallenCivilizations.Add(player.civilization);
			log.Information("{Old} respawn as {New} at {Tile}", player.civilization?.name, civ.name, start);

			player.civilization = civ;
			player.primaryColorIndex = civ.primaryColorIndex;
			player.secondaryColorIndex = civ.secondaryColorIndex;
			player.defeated = false;
			player.neverHadCityOrSettler = false;
			player.citiesFounded = 0;
			player.gold = gameData.rules.StartingTreasury;
			player.knownTechs = [.. civ.startingTechs];
			player.eraCivilopediaName = gameData.rules.FirstEraCivilopediaName;
			Government government = gameData.rules.OnlyGovernment is string only ? gameData.governments.Find(g => g.name == only) : null;
			player.government = government ?? gameData.governments.Find(g => g.defaultType) ?? player.government;
			player.ForgetTheMap();

			foreach (string type in new[] { gameData.rules.StartUnitType1, gameData.rules.StartUnitType2,
					civ.traits.Contains(Civilization.Trait.Expansionist) ? gameData.rules.ScoutUnitType : null }) {
				UnitPrototype proto = type == null ? null : gameData.unitPrototypes.Find(p => p.name == type);
				if (proto != null) {
					gameData.SpawnUnit(player, proto, start);
				}
			}
			return true;
		}

		// The free land farthest from every city, at least
		// MinimumDistanceFromCities away; null if there is none.
		private static Tile StartFor(GameData gameData) {
			List<Tile> cities = gameData.cities.Select(c => c.location).ToList();
			Tile best = null;
			int bestDistance = -1;
			foreach (Tile t in gameData.map.tiles) {
				if (!t.IsLand() || t.HasCity() || t.unitsOnTile.Count > 0 || t.hasBarbarianCamp || t.IsImpassable() || !t.IsAllowCities()
						|| t.owningCity != null) {
					continue;
				}
				int distance = cities.Count == 0 ? int.MaxValue : cities.Min(c => c.DistanceTo(t));
				if (distance >= MinimumDistanceFromCities && distance > bestDistance) {
					best = t;
					bestDistance = distance;
				}
			}
			return best;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// Conquests' volcanic eruptions (Civilopedia "Volcanic Eruptions"): a
	// volcano "can suddenly erupt with no warning. Most of the time, however,
	// a dark cloud begins to emanate from the volcano's mouth a few turns in
	// advance". It "spews forth lava, in the form of pollution, that will mar
	// the landscape of many of the tiles adjacent", destroying units and
	// improvements there (Conquests' changes list). Civ3's editor setting is
	// an eruption period: every volcano erupts at least once in that many
	// years (5000 in Conquests). Here each volcano erupts on average once
	// per period: a turn of N years is an N / period chance. Which of the 8
	// tiles around it are hit, and what a hit city loses, aren't documented:
	// rules (Rules.VolcanoTileHitPercent) and our choice (a city hit loses
	// half its citizens but is never destroyed, and the units inside are
	// sheltered).
	public static class Volcanoes {
		private static ILogger log = Log.ForContext(typeof(Volcanoes));

		// The number of years the turn just ended took, at least 1.
		public static int YearsThisTurn(GameData gameData) {
			try {
				int now = gameData.timeOptions.GetAbsoluteYear(gameData.turn);
				int before = gameData.timeOptions.GetAbsoluteYear(Math.Max(0, gameData.turn - 1));
				return Math.Max(1, now - before);
			} catch (Exception) {
				// Past the calendar's end.
				return 1;
			}
		}

		// The chance, between 0 and 1, that a quiet volcano starts to erupt
		// this turn.
		public static double EruptionChance(GameData gameData) {
			int period = gameData.rules.VolcanoEruptionPeriod;
			return period <= 0 ? 0 : Math.Clamp((double)YearsThisTurn(gameData) / period, 0, 1);
		}

		// Once a turn: smoking volcanoes whose time has come erupt, and quiet
		// ones may start to smoke (or erupt at once, without warning).
		public static void UpdateForNewTurn(GameData gameData) {
			Rules rules = gameData.rules;
			double chance = EruptionChance(gameData);
			foreach (Tile tile in gameData.map.tiles.Where(t => t.IsVolcano())) {
				if (tile.volcanoEruptsOnTurn >= 0) {
					if (gameData.turn >= tile.volcanoEruptsOnTurn) {
						Erupt(gameData, tile);
					}
					continue;
				}
				if (chance <= 0 || GameData.rng.NextDouble() >= chance) {
					continue;
				}
				bool warns = rules.VolcanoWarningTurns > 0 && GameData.rng.Next(100) < rules.VolcanoWarningPercent;
				if (!warns) {
					Erupt(gameData, tile);
					continue;
				}
				tile.volcanoEruptsOnTurn = gameData.turn + 1 + GameData.rng.Next(rules.VolcanoWarningTurns);
				log.Information($"The volcano at {tile} smokes; it erupts on turn {tile.volcanoEruptsOnTurn}");
				TellThoseWhoKnow(gameData, tile, MsgVolcano.Event.Active);
			}
		}

		private static void TellThoseWhoKnow(GameData gameData, Tile volcano, MsgVolcano.Event what) {
			foreach (Player player in gameData.players.Where(p => !p.isBarbarians && p.tileKnowledge.isTileKnown(volcano))) {
				new MsgVolcano(player.id, volcano, what).send();
			}
		}

		public static void Erupt(GameData gameData, Tile volcano) {
			volcano.volcanoEruptsOnTurn = -1;
			log.Information($"The volcano at {volcano} erupts");
			TellThoseWhoKnow(gameData, volcano, MsgVolcano.Event.Erupted);
			TerrainImprovement pollution = gameData.terrainImprovements.Find(i => i.key == Tile.TileOverlays.POLLUTION);
			foreach (Tile tile in volcano.neighbors.Values) {
				if (!tile.IsLand() || tile.IsVolcano() || GameData.rng.Next(100) >= gameData.rules.VolcanoTileHitPercent) {
					continue;
				}
				if (tile.HasCity()) {
					City city = tile.cityAtTile;
					int lost = Math.Min(city.residents.Count - 1, (city.residents.Count + 1) / 2);
					city.RemoveCitizens(lost);
					log.Information($"Lava torches {city.name}: {lost} citizens die");
					new MsgVolcano(city.owner.id, tile, MsgVolcano.Event.TorchedCity, city.name).send();
					continue;
				}
				foreach (IGrouping<Player, MapUnit> owned in tile.unitsOnTile.Where(u => !u.IsInArmy()).ToList().GroupBy(u => u.owner)) {
					foreach (MapUnit unit in owned) {
						gameData.RemoveUnit(unit);
					}
					log.Information($"Lava kills {owned.Count()} units of {owned.Key} at {tile}");
					new MsgVolcano(owned.Key.id, tile, MsgVolcano.Event.KilledUnits).send();
				}
				for (int i = 0; i < 10 && tile.overlays.NextToPillage().Count > 0; ++i) {
					tile.overlays.PillageNext();
				}
				if (pollution != null && tile.overlays.CanAdd(tile, pollution)) {
					tile.overlays.Add(pollution);
				}
			}
		}
	}
}

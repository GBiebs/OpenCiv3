using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// Conquests' capture the princess (Civilopedia "Short Game Modes"): every
	// civ starts with a Princess (Civ3's RULE "flag unit") at its starting
	// location. She can't move herself; an enemy reaching her unguarded
	// captures her and carries her "at its speed". Brought to the captor's
	// capital, she earns it victory points (1000) and the game's gold, and
	// she is "spawned again at the original owner's Capital City"; if there's
	// none, "it remains in the captor's Capital and bestows another 1,000
	// points". Only in a game with capture the princess.
	public static class Princess {
		private static ILogger log = Log.ForContext(typeof(Princess));

		public static bool On(GameData gameData) {
			return gameData.victoryConditions?.CaptureTheFlag == true;
		}

		public static bool Is(GameData gameData, MapUnit unit) {
			return unit.unitType.name == gameData.rules.FlagUnitType;
		}

		// When the game begins, each civ's princess at its start.
		public static void Place(GameData gameData) {
			UnitPrototype princess = gameData.unitPrototypes.Find(u => u.name == gameData.rules.FlagUnitType);
			if (!On(gameData) || princess == null) {
				return;
			}
			foreach (Player player in gameData.players.Where(p => !p.isBarbarians)) {
				Tile start = player.units.FirstOrDefault(u => u.unitType.isSettler)?.location ?? player.units.FirstOrDefault()?.location;
				if (start != null) {
					gameData.SpawnUnit(player, princess, start);
				}
			}
		}

		// `captor` captured her: it carries her from now on.
		public static void OnCaptured(MapUnit princess, MapUnit captor) {
			princess.loadedOnUnitId = captor.id;
		}

		// `carrier` entered a tile, carrying her: at its civ's capital, she's
		// delivered.
		public static void OnCarried(GameData gameData, MapUnit carrier) {
			foreach (MapUnit princess in carrier.Passengers().Where(p => Is(gameData, p)).ToList()) {
				City capital = carrier.owner.cities.FirstOrDefault(c => c.capital);
				if (capital?.location == carrier.location && princess.nationality != carrier.owner.civilization) {
					Deliver(gameData, princess, carrier.owner, capital);
				}
			}
		}

		private static void Deliver(GameData gameData, MapUnit princess, Player captor, City capital) {
			VictoryRules rules = gameData.rules.victory;
			Player home = gameData.players.FirstOrDefault(p => p.civilization == princess.nationality && !p.defeated);
			City homeCapital = home?.cities.FirstOrDefault(c => c.capital);
			bool respawns = rules.respawnPrincess && homeCapital != null;
			VictoryPoints.OnPrincessDelivered(gameData, captor);
			if (!respawns) {
				VictoryPoints.OnPrincessDelivered(gameData, captor);
			}
			captor.gold += rules.princessGold;
			princess.loadedOnUnitId = null;
			log.Information($"{captor} brings the {princess.nationality.adjective} princess to {capital.name}");
			new MsgPrincessDelivered(captor, princess.nationality, capital).send();
			if (respawns) {
				gameData.TransferUnit(princess, home);
				gameData.TeleportUnit(princess, homeCapital.location);
			}
		}
	}
}

using System.Collections.Generic;
using System.Linq;
using C7GameData;
using Serilog;

namespace C7Engine {
	// Hotseat and play by email: the administrator, whoever knows the admin
	// password chosen when the game was set up (Civ3's "Password for
	// Administration"), can replace a human who has left with the AI or
	// another player, and clear a forgotten password so its player chooses a
	// new one. At least one human always stays.
	public static class Administration {
		private static ILogger log = Log.ForContext(typeof(Administration));

		public class Change {
			public ID playerId;
			// Whether the civ is played by a human from now on.
			public bool human;
			public bool clearPassword;
		}

		// The civs the administrator can change.
		public static List<Player> Civs(GameData gameData) {
			return gameData.players.Where(p => !p.isBarbarians && !p.defeated && p.isIncludedInGame).ToList();
		}

		// Applies the changes if the admin password is right and a human is
		// left; a new admin password replaces the old one (none keeps it).
		public static bool Apply(GameData gameData, string adminPassword, List<Change> changes, string newAdminPassword = null) {
			if (!gameData.CheckAdminPassword(adminPassword)) {
				log.Warning("The admin password was wrong");
				return false;
			}
			List<Player> civs = Civs(gameData);
			bool HumanAfter(Player p) => changes.Find(c => c.playerId == p.id) is Change change ? change.human : p.isHuman;
			if (!civs.Any(HumanAfter)) {
				return false;
			}
			foreach (Change change in changes) {
				Player player = civs.Find(p => p.id == change.playerId);
				if (player == null) {
					continue;
				}
				if (player.isHuman != change.human) {
					log.Information($"The administrator hands {player} to {(change.human ? "a human" : "the AI")}");
					player.isHuman = change.human;
					HumanDiplomacy.Forget(gameData, player);
				}
				if (change.clearPassword) {
					player.SetPassword("");
				}
			}
			if (!string.IsNullOrEmpty(newAdminPassword)) {
				gameData.adminPasswordHash = Passwords.Hash(newAdminPassword);
			}
			return true;
		}
	}
}

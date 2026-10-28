using System.Collections.Generic;
using System.Linq;
using C7Engine;
using Serilog;

namespace C7GameData {
	// Troops in another civ's land (Civ3's diplomacy.txt AIBORDERWARNING:
	// "your troops seem to be within our territory near ... we humbly request
	// that you correct it"; AIBORDERWAR: "your movement of troops into our
	// territory constitutes a declaration of war ... Send them home
	// IMMEDIATELY"). On its turn an AI looks for military units of civs it's
	// at peace with, without right of passage, on its land. The first turn
	// it asks them to leave, the next it orders them home, and if they're
	// still there after that it declares war, unless it has less than half
	// their military (then it keeps ordering). Another AI sends its troops
	// home when asked; a human chooses (MsgBorderWarning). While troops stay,
	// the AI holds it against their civ: +1 attitude when it asked, +4 when it
	// ordered (CivFanatics' study: "as long as you leave when forced to, you
	// get the points back the next turn"; rules).
	public static class BorderWarnings {
		private static ILogger log = Log.ForContext(typeof(BorderWarnings));

		internal const double WarStrength = .5;

		public static List<MapUnit> Intruders(GameData gameData, Player owner, Player other) {
			if (!PlayerRelationship.TryGetRelationship(owner, other, out PlayerRelationship relationship) || relationship.AtWar()
				|| Agreements.Has(owner, other, DealSubType.RightOfPassage) || gameData.AreInLockedPeace(owner, other)) {
				return [];
			}
			return other.units.Where(u => u.IsCombatUnit() && !u.IsLoaded() && u.location.OwningPlayer() == owner).ToList();
		}

		// Each unit goes to its owner's nearest city.
		public static void SendHome(GameData gameData, IEnumerable<MapUnit> units) {
			foreach (MapUnit unit in units.ToList()) {
				City home = unit.owner.cities.MinBy(c => c.location.DistanceTo(unit.location));
				if (home != null) {
					gameData.TeleportUnit(unit, home.location);
				}
			}
		}

		// On `owner`'s turn.
		public static void Check(GameData gameData, Player owner) {
			AttitudeRules rules = gameData.rules.attitude;
			foreach (Player other in gameData.players.Where(p => p != owner && !p.isBarbarians && !p.defeated).ToList()) {
				if (!PlayerRelationship.TryGetRelationship(owner, other, out PlayerRelationship relationship)) {
					continue;
				}
				List<MapUnit> intruders = Intruders(gameData, owner, other);
				if (intruders.Count == 0) {
					relationship.borderWarning = 0;
					relationship.borderAnger = 0;
					continue;
				}
				City near = owner.cities.MinBy(c => c.location.DistanceTo(intruders[0].location));
				if (relationship.borderWarning >= 2) {
					if (owner.CalculateMilitaryStrength() >= WarStrength * other.CalculateMilitaryStrength()) {
						log.Information($"{other}'s troops stayed in {owner}'s land: war");
						relationship.borderWarning = 0;
						relationship.borderAnger = 0;
						owner.DeclareWarOn(other, gameData.turn);
						new MsgWarDeclarationNotification(owner, other).send();
					}
					continue;
				}
				bool order = relationship.borderWarning == 1;
				relationship.borderWarning = order ? 2 : 1;
				relationship.borderAnger = order ? rules.orderedToLeave : rules.askedToLeave;
				log.Information($"{owner} {(order ? "orders" : "asks")} {other}'s troops out of its land near {near?.name}");
				if (other.isHuman) {
					new MsgBorderWarning(owner, other, near, order).send();
				} else {
					SendHome(gameData, intruders);
				}
			}
		}
	}
}

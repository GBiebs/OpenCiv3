using System.Linq;
using C7GameData;

namespace C7Engine.AI {
	// The AI's nuclear weapons (see Nuclear). Players report Civ3's AI
	// "will most likely not fire theirs at you first. If you fire just one
	// nuke, the AI will have no qualms about releasing its entire arsenal":
	// so the AI fires only once someone has used one, at the enemy target in
	// range that hurts most (the most people, then the dearest units), never
	// where the blast would hit its own units or cities. Nukes otherwise stay
	// where they are.
	public static class NuclearAI {
		public static void Act(Player player, GameData gameData) {
			if (gameData.nukesUsed == 0) {
				return;
			}
			foreach (MapUnit nuke in player.units.Where(u => Nuclear.IsNuke(u.unitType) && u.movementPoints.canMove).ToList()) {
				if (Target(gameData, nuke) is Tile target) {
					Nuclear.Strike(gameData, nuke, target);
				}
			}
		}

		internal static Tile Target(GameData gameData, MapUnit nuke) {
			Player us = nuke.owner;
			return gameData.map.tiles
				.Where(t => (t.HasCity() || t.unitsOnTile.Count > 0) && Nuclear.CanStrike(nuke, t))
				.Where(t => t.neighbors.Values.Append(t).All(n => n.cityAtTile?.owner != us && n.unitsOnTile.All(u => u.owner != us)))
				.OrderByDescending(t => (t.cityAtTile?.residents.Count ?? 0) * 100 + t.unitsOnTile.Sum(u => u.unitType.shieldCost))
				.FirstOrDefault();
		}
	}
}
